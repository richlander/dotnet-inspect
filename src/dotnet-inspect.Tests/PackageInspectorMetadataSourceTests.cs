using System.IO.Compression;
using System.Net;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;
using InertText;

namespace DotnetInspector.Tests;

// Mutates the process-global CoreCache root; serialize with in-process CLI/cache tests (#3471).
[Collection("Console")]
public sealed class PackageInspectorMetadataSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"package-inspector-metadata-{Guid.NewGuid():N}");

    public PackageInspectorMetadataSourceTests()
    {
        Directory.CreateDirectory(_root);
        CoreCache.Initialize("dotnet-inspect-test");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectAsync_ConfiguredAuthoritiesDoNotShareProducerIndexes(bool local)
    {
        CoreCache.Initialize("dotnet-inspect-test", Path.Combine(_root, "cache"));
        const string packageId = "Authority.Index";
        const string version = "1.0.0";
        const string producer = "shared-test-producer";
        PackageIndexCache.Set(packageId, version, producer,
            new InspectionResult
            {
                PackageName = packageId,
                Version = version,
                Description = new InertString(TextPolicy.Field, "Legacy index", maxLength: 100),
            });
        using var client = new HttpClient(new RoutingHandler(_ =>
            throw new InvalidOperationException("This inspection does not request remote metadata.")));

        foreach (string name in new[] { "first", "second" })
        {
            string root = Path.Combine(_root, name);
            Directory.CreateDirectory(root);
            var authority = new ConfiguredPackageAuthority(new NuGetFetch.PackageSource(
                name, local ? root : $"https://feed.example/v3/index.json?tenant={name}"));
            var resolution = new PackageExtractionResult(
                root, TempDir: null, packageId, version, ProducerKey: producer)
            {
                Authority = authority,
            };
            NuspecData nuspec = NuspecParser.ParseContent($"""
                <package><metadata><id>{packageId}</id><version>{version}</version>
                <description>{name}</description></metadata></package>
                """)!;
            InspectionResult result = await PackageInspector.InspectAsync(
                resolution, packageId, version, isLocalFile: false,
                localFilePath: null, nuspec, client, new VerboseLogger(enabled: false));

            Assert.Equal(name, result.Description?.ToString());
            if (local)
            {
                Assert.Equal(authority.PersistentCacheKey, resolution.CacheScopeKey);
                Assert.Equal(name,
                    PackageIndexCache.TryGet(packageId, version, resolution.CacheScopeKey!)?.Description?.ToString());
            }
            else
            {
                Assert.Null(resolution.CacheScopeKey);
            }
        }
        Assert.Equal("Legacy index", PackageIndexCache.TryGet(packageId, version, producer)?.Description?.ToString());
    }

    [Fact]
    public async Task InspectAsync_UsesTheAcquiredPackageProducerForMetadata()
    {
        const string sourceA = "https://a.example/v3/index.json";
        const string sourceB = "https://b.example/v3/index.json";
        bool queriedSourceA = false;
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            if (request.RequestUri!.Host == "a.example")
            {
                queriedSourceA = true;
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return request.RequestUri.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://b.example/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json("""
                    {
                      "items": [
                        {
                          "@id": "/metadata/release-page",
                          "lower": "1.0.0",
                          "upper": "1.0.0"
                        }
                      ]
                    }
                    """),
                "/metadata/release-page" => Json("""
                    {
                      "items": [
                        {
                          "catalogEntry": {
                            "id": "Private.Package",
                            "version": "1.0.0",
                            "published": "2025-01-02T00:00:00Z"
                          }
                        }
                      ]
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }));

        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: "Private.Package",
            Version: "1.0.0",
            ProducerKey: NuGetCache.GetSourceKey(sourceB));
        InspectionResult result = await PackageInspector.InspectAsync(
            resolution,
            "Wrapper.Package",
            "1.0.0",
            isLocalFile: false,
            localFilePath: null,
            nuspec: null,
            client,
            new VerboseLogger(enabled: false),
            forceLatest: true,
            verbosity: Verbosity.Detailed,
            fetchMetadata: true,
            sourceOptions: new NuGetSourceOptions
            {
                Sources = [sourceA, sourceB],
            });

        Assert.Equal(2025, result.Published!.Value.Year);
        Assert.Equal("Private.Package", result.PackageName);
        Assert.False(queriedSourceA);
    }

    [Fact]
    public async Task InspectAsync_PreservesToolWrapperClassificationOnPayload()
    {
        string wrapperRoot = Path.Combine(_root, "wrapper");
        string wrapperTools = Path.Combine(wrapperRoot, "tools", "net10.0", "any");
        Directory.CreateDirectory(wrapperTools);
        await File.WriteAllTextAsync(
            Path.Combine(wrapperTools, "DotnetToolSettings.xml"),
            """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="wrapper-command" EntryPoint="Payload.dll" Runner="dotnet" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="Wrapper.Package.linux-x64" />
                <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="Wrapper.Package.any" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """,
            TestContext.Current.CancellationToken);

        string payloadRoot = Path.Combine(_root, "payload");
        string payloadTools = Path.Combine(payloadRoot, "tools", "net10.0", "any");
        Directory.CreateDirectory(payloadTools);
        File.Copy(
            typeof(PackageInspectorMetadataSourceTests).Assembly.Location,
            Path.Combine(payloadTools, "Payload.dll"));
        WriteNuspec(
            payloadRoot,
            "Wrapper.Package.any",
            "1.0.0");
        string localPackagePath = Path.Combine(
            _root,
            "Wrapper.Package.1.0.0.nupkg");
        await File.WriteAllTextAsync(
            localPackagePath,
            "",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(_root, "Wrapper.Package.any.1.0.0.nupkg"),
            "",
            TestContext.Current.CancellationToken);

        var resolution = new PackageExtractionResult(
            payloadRoot,
            TempDir: null,
            PackageName: "Wrapper.Package.any",
            Version: "1.0.0")
        {
            ToolWrapperChain =
            [
                new ToolWrapperPackage(
                    wrapperRoot,
                    "Wrapper.Package",
                    "1.0.0",
                    ProducerKey: "wrapper-source")
            ]
        };
        NuspecData payloadNuspec = Assert.IsType<NuspecData>(
            NuspecParser.ParseContent(
                """
                <package>
                  <metadata>
                    <id>Wrapper.Package.any</id>
                    <version>1.0.0</version>
                    <packageTypes>
                      <packageType name="Template" />
                    </packageTypes>
                  </metadata>
                </package>
                """));

        using var client = new HttpClient();
        InspectionResult unverified = await PackageInspector.InspectAsync(
            resolution,
            "Wrapper.Package.any",
            "1.0.0",
            isLocalFile: true,
            localFilePath: localPackagePath,
            nuspec: payloadNuspec,
            client,
            new VerboseLogger(enabled: false));
        Assert.Null(Assert.Single(
            unverified.RuntimeIdentifierPackages!,
            package => package.RuntimeIdentifier == "any").Exists);
        Assert.Null(Assert.Single(
            unverified.RuntimeIdentifierPackages!,
            package => package.RuntimeIdentifier == "linux-x64").Exists);
        Assert.Equal(
            "unknown",
            Assert.Single(
                PackageInspectionJson.Create(unverified).RuntimeIdentifierPackages!,
                package => package.RuntimeIdentifier == "linux-x64").Available);
        Assert.Null(unverified.PackageTypes);

        InspectionResult result = await PackageInspector.InspectAsync(
            resolution,
            "Wrapper.Package.any",
            "1.0.0",
            isLocalFile: true,
            localFilePath: localPackagePath,
            nuspec: payloadNuspec,
            client,
            new VerboseLogger(enabled: false),
            verifyRidPackageAvailability: true);

        Assert.Equal("Wrapper.Package.any", result.PackageName);
        Assert.Equal("Tool v2", new InspectionResultView(result).PackageType);
        Assert.Equal(["wrapper-command"], result.ToolCommands);
        Assert.True(result.IsRidSpecificPointerPackage);
        Assert.True(result.IsFrameworkDependent);
        Assert.False(result.HasRidSpecificAssets);
        Assert.Equal(["any"], result.SupportedRids);
        Assert.Null(result.PackageTypes);
        RidPackageReference anyPackage = Assert.Single(
            result.RuntimeIdentifierPackages!,
            package => package.RuntimeIdentifier == "any"
                && package.PackageId == "Wrapper.Package.any");
        Assert.True(anyPackage.Exists);
        RidPackageReference linuxPackage = Assert.Single(
            result.RuntimeIdentifierPackages!,
            package => package.RuntimeIdentifier == "linux-x64");
        Assert.False(linuxPackage.Exists);
        List<RidPackageReferenceJson> jsonPackages =
            PackageInspectionJson.Create(result).RuntimeIdentifierPackages!;
        Assert.Equal(
            "yes",
            Assert.Single(
                jsonPackages,
                package => package.RuntimeIdentifier == "any").Available);
        Assert.Equal(
            "no",
            Assert.Single(
                jsonPackages,
                package => package.RuntimeIdentifier == "linux-x64").Available);
    }

    [Fact]
    public async Task InspectAsync_OversizedWrapperNuspecUsesBoundedProbe()
    {
        string wrapperRoot = Path.Combine(_root, "oversized-wrapper");
        string wrapperTools = Path.Combine(
            wrapperRoot,
            "tools",
            "net10.0",
            "any");
        Directory.CreateDirectory(wrapperTools);
        await File.WriteAllTextAsync(
            Path.Combine(wrapperTools, "DotnetToolSettings.xml"),
            """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="wrapper-command" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="Wrapper.Package.any" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """,
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(wrapperRoot, "Wrapper.Package.nuspec"),
            "<package><metadata><id>Wrapper.Package</id><version>1.0.0</version>"
            + $"<description>{new string('x', PackageExtractor.MaxNuspecBytes)}</description>"
            + "</metadata></package>",
            TestContext.Current.CancellationToken);

        string payloadRoot = Path.Combine(_root, "oversized-payload");
        string payloadTools = Path.Combine(
            payloadRoot,
            "tools",
            "net10.0",
            "any");
        Directory.CreateDirectory(payloadTools);
        File.Copy(
            typeof(PackageInspectorMetadataSourceTests).Assembly.Location,
            Path.Combine(payloadTools, "Payload.dll"));
        WriteNuspec(
            payloadRoot,
            "Wrapper.Package.any",
            "1.0.0");
        var resolution = new PackageExtractionResult(
            payloadRoot,
            TempDir: null,
            PackageName: "Wrapper.Package.any",
            Version: "1.0.0")
        {
            ToolWrapperChain =
            [
                new ToolWrapperPackage(
                    wrapperRoot,
                    "Wrapper.Package",
                    "1.0.0",
                    ProducerKey: "wrapper-source"),
            ],
        };

        InspectionResult result = await PackageInspector.InspectAsync(
            resolution,
            "Wrapper.Package.any",
            "1.0.0",
            isLocalFile: true,
            localFilePath: Path.Combine(_root, "Wrapper.Package.1.0.0.nupkg"),
            nuspec: null,
            new HttpClient(),
            new VerboseLogger(enabled: false));

        Assert.Equal("Tool v2", new InspectionResultView(result).PackageType);
        Assert.Equal(["wrapper-command"], result.ToolCommands);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InspectAsync_RedirectedPayloadRidMappingsAreNotProbed(
        bool usePayloadCache)
    {
        string wrapperRoot = Path.Combine(_root, "wrapper-probe");
        string wrapperTools = Path.Combine(
            wrapperRoot,
            "tools",
            "net10.0",
            "any");
        Directory.CreateDirectory(wrapperTools);
        await File.WriteAllTextAsync(
            Path.Combine(wrapperTools, "DotnetToolSettings.xml"),
            """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="wrapper-command" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="any" Id="Wrapper.Package.any" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """,
            TestContext.Current.CancellationToken);

        string payloadRoot = Path.Combine(_root, "payload-probe");
        string payloadTools = Path.Combine(
            payloadRoot,
            "tools",
            "net10.0",
            "any");
        Directory.CreateDirectory(payloadTools);
        await File.WriteAllTextAsync(
            Path.Combine(payloadTools, "DotnetToolSettings.xml"),
            """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="payload-command" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="Unmapped.Package" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """,
            TestContext.Current.CancellationToken);
        File.Copy(
            typeof(PackageInspectorMetadataSourceTests).Assembly.Location,
            Path.Combine(payloadTools, "Payload.dll"));
        WriteNuspec(
            payloadRoot,
            "Wrapper.Package.any",
            "1.0.0");

        string configPath = Path.Combine(_root, "nuget.config");
        await File.WriteAllTextAsync(
            configPath,
            """
            <configuration>
              <packageSources>
                <clear />
                <add key="private" value="https://private.example/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="private">
                  <package pattern="Wrapper.*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """,
            TestContext.Current.CancellationToken);

        int requestCount = 0;
        using var client = new HttpClient(new RoutingHandler(_ =>
        {
            requestCount++;
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        const string payloadPackage = "Wrapper.Package.any";
        const string payloadProducer = "payload-source";
        if (usePayloadCache)
        {
            PackageIndexCache.Set(
                payloadPackage,
                "1.0.0",
                payloadProducer,
                new InspectionResult
                {
                    PackageName = payloadPackage,
                    Version = "1.0.0",
                    IsRidSpecificPointerPackage = true,
                    RuntimeIdentifierPackages =
                    [
                        new RidPackageReference
                        {
                            RuntimeIdentifier = "linux-x64",
                            PackageId = "Unmapped.Package",
                        },
                    ],
                });
        }

        var resolution = new PackageExtractionResult(
            payloadRoot,
            TempDir: null,
            PackageName: payloadPackage,
            Version: "1.0.0",
            ProducerKey: usePayloadCache ? payloadProducer : null)
        {
            ToolWrapperChain =
            [
                new ToolWrapperPackage(
                    wrapperRoot,
                    "Wrapper.Package",
                    "1.0.0",
                    ProducerKey: "private"),
            ],
        };

        InspectionResult result = await PackageInspector.InspectAsync(
            resolution,
            payloadPackage,
            "1.0.0",
            isLocalFile: false,
            localFilePath: null,
            nuspec: null,
            client,
            new VerboseLogger(enabled: false),
            verifyRidPackageAvailability: true,
            sourceOptions: new NuGetSourceOptions { ConfigFile = configPath });

        Assert.Equal(["wrapper-command"], result.ToolCommands);
        RidPackageReference ridPackage = Assert.Single(
            result.RuntimeIdentifierPackages!);
        Assert.Equal("Wrapper.Package.any", ridPackage.PackageId);
        Assert.True(ridPackage.Exists);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task InspectAsync_CachedUnknownRidAvailabilityIsRecheckedWithoutPersisting()
    {
        string packageName = $"Pointer.Package.{Guid.NewGuid():N}";
        const string version = "1.0.0";
        const string source = "https://feed.example/v3/index.json";
        string producerKey = NuGetCache.GetSourceKey(source);
        PackageIndexCache.Set(
            packageName,
            version,
            producerKey,
            new InspectionResult
            {
                PackageName = packageName,
                Version = version,
                IsRidSpecificPointerPackage = true,
                RuntimeIdentifierPackages =
                [
                    new RidPackageReference
                    {
                        RuntimeIdentifier = "linux-x64",
                        PackageId = $"{packageName}.linux-x64",
                    }
                ],
            });

        using var client = new HttpClient(new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json(
                    """
                    {
                      "version": "3.0.0",
                      "resources": [
                        { "@id": "https://feed.example/flat/", "@type": "PackageBaseAddress/3.0.0" }
                      ]
                    }
                    """),
                _ when request.RequestUri.AbsolutePath.EndsWith(
                    ".nuspec",
                    StringComparison.Ordinal) => Xml(
                    $"""
                    <package>
                      <metadata>
                        <id>{packageName}.linux-x64</id>
                        <version>{version}</version>
                      </metadata>
                    </package>
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            }));
        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: packageName,
            Version: version,
            ProducerKey: producerKey);

        InspectionResult result = await PackageInspector.InspectAsync(
            resolution,
            packageName,
            version,
            isLocalFile: false,
            localFilePath: null,
            nuspec: null,
            client,
            new VerboseLogger(enabled: false),
            verifyRidPackageAvailability: true,
            sourceOptions: new NuGetSourceOptions { Sources = [source] });

        Assert.True(
            Assert.Single(result.RuntimeIdentifierPackages!).Exists);
        Assert.Null(
            Assert.Single(
                PackageIndexCache.TryGet(
                    packageName,
                    version,
                    producerKey)!.RuntimeIdentifierPackages!).Exists);
    }

    [Fact]
    public async Task InspectAsync_ReverifiesEveryCachedRidCoordinateWithoutPersisting()
    {
        string packageName =
            $"Cached.Pointer.{Guid.NewGuid():N}";
        string firstPackage = $"{packageName}.linux-x64";
        string secondPackage = $"{packageName}.win-x64";
        const string Version = "1.0.0";
        const string Source = "https://fixture.example/v3/index.json";
        string producerKey = NuGetCache.GetSourceKey(Source);
        PackageIndexCache.Set(
            packageName,
            Version,
            producerKey,
            new InspectionResult
            {
                PackageName = packageName,
                Version = Version,
                IsRidSpecificPointerPackage = true,
                RuntimeIdentifierPackages =
                [
                    new RidPackageReference
                    {
                        RuntimeIdentifier = "linux-x64",
                        PackageId = firstPackage,
                    },
                    new RidPackageReference
                    {
                        RuntimeIdentifier = "win-x64",
                        PackageId = secondPackage,
                        Exists = true,
                    },
                ],
            });

        bool probedSecondPackage = false;
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            string firstId = firstPackage.ToLowerInvariant();
            string secondId = secondPackage.ToLowerInvariant();
            if (path == $"/flat/{secondId}/{Version}/{secondId}.nuspec")
                probedSecondPackage = true;

            return path switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        {
                          "@id": "https://fixture.example/flat/",
                          "@type": "PackageBaseAddress/3.0.0"
                        }
                      ]
                    }
                    """),
                _ when path == $"/flat/{firstId}/{Version}/{firstId}.nuspec" =>
                    Xml($"""
                        <package><metadata>
                          <id>{firstPackage}</id>
                          <version>{Version}</version>
                        </metadata></package>
                        """),
                _ when path == $"/flat/{secondId}/{Version}/{secondId}.nuspec" =>
                    Xml($"""
                        <package><metadata>
                          <id>{secondPackage}</id>
                          <version>{Version}</version>
                        </metadata></package>
                        """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }));
        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: packageName,
            Version: Version,
            ProducerKey: producerKey);

        InspectionResult result = await PackageInspector.InspectAsync(
            resolution,
            packageName,
            Version,
            isLocalFile: false,
            localFilePath: null,
            nuspec: null,
            client,
            new VerboseLogger(enabled: false),
            verifyRidPackageAvailability: true,
            sourceOptions: new NuGetSourceOptions
            {
                Sources = [Source],
            });

        Assert.All(
            result.RuntimeIdentifierPackages!,
            package => Assert.True(package.Exists));
        Assert.True(probedSecondPackage);
        Assert.All(
            PackageIndexCache.TryGet(
                packageName,
                Version,
                producerKey)!.RuntimeIdentifierPackages!,
            package => Assert.Null(package.Exists));
    }

    [Fact]
    public async Task MarkAcquiredRidPackages_UsesResolutionCoordinatesAndRedirectChain()
    {
        string middlePath = Path.Combine(_root, "middle");
        Directory.CreateDirectory(middlePath);
        WriteNuspec(
            middlePath,
            "Wrapper.Package.middle",
            "1.0.0");
        WriteNuspec(
            _root,
            "Wrapper.Package.any",
            "1.0.0");
        var result = new InspectionResult
        {
            PackageName = "Spoofed.By.Payload.Nuspec",
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "middle",
                    PackageId = "Wrapper.Package.middle",
                },
                new RidPackageReference
                {
                    RuntimeIdentifier = "any",
                    PackageId = "Wrapper.Package.any",
                },
                new RidPackageReference
                {
                    RuntimeIdentifier = "spoofed",
                    PackageId = "Spoofed.By.Payload.Nuspec",
                },
            ],
        };
        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: "Wrapper.Package.any",
            Version: "1.0.0")
        {
            ToolWrapperChain =
            [
                new ToolWrapperPackage(
                    Path.Combine(_root, "wrapper"),
                    "Wrapper.Package",
                    "1.0.0",
                    ProducerKey: "wrapper-source"),
                new ToolWrapperPackage(
                    middlePath,
                    "Wrapper.Package.middle",
                    "1.0.0",
                    ProducerKey: "middle-source"),
            ],
        };

        await PackageInspector.MarkAcquiredRidPackagesAsync(
            result,
            resolution,
            wrapperVersion: "1.0.0");

        Assert.True(result.RuntimeIdentifierPackages[0].Exists);
        Assert.True(result.RuntimeIdentifierPackages[1].Exists);
        Assert.Null(result.RuntimeIdentifierPackages[2].Exists);
    }

    [Fact]
    public async Task MarkAcquiredRidPackages_UsesNormalizedVersionIdentity()
    {
        WriteNuspec(
            _root,
            "Wrapper.Package.any",
            "1.0.0+payload");
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "any",
                    PackageId = "Wrapper.Package.any",
                },
            ],
        };
        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: "Wrapper.Package.any",
            Version: "1.0.0+payload");

        await PackageInspector.MarkAcquiredRidPackagesAsync(
            result,
            resolution,
            wrapperVersion: "1.0.0+wrapper");

        Assert.True(Assert.Single(result.RuntimeIdentifierPackages).Exists);
    }

    [Fact]
    public async Task MarkAcquiredRidPackages_AppliesOneCoordinateToDuplicateMappings()
    {
        WriteNuspec(
            _root,
            "Wrapper.Package.any",
            "1.0.0");
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "any",
                    PackageId = "Wrapper.Package.any",
                },
                new RidPackageReference
                {
                    RuntimeIdentifier = "portable",
                    PackageId = "wrapper.package.ANY",
                },
            ],
        };
        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: "Wrapper.Package.any",
            Version: "1.0.0");

        await PackageInspector.MarkAcquiredRidPackagesAsync(
            result,
            resolution,
            wrapperVersion: "1.0.0");

        Assert.All(
            result.RuntimeIdentifierPackages,
            package => Assert.True(package.Exists));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Wrong.Payload.Identity")]
    public async Task MarkAcquiredRidPackages_RequiresMatchingNuspec(
        string? nuspecId)
    {
        if (nuspecId is not null)
        {
            WriteNuspec(
                _root,
                nuspecId,
                "1.0.0",
                fileName: "Wrapper.Package.any.nuspec");
        }

        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "any",
                    PackageId = "Wrapper.Package.any",
                },
            ],
        };
        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: "Wrapper.Package.any",
            Version: "1.0.0");

        await PackageInspector.MarkAcquiredRidPackagesAsync(
            result,
            resolution,
            wrapperVersion: "1.0.0");

        Assert.Null(
            Assert.Single(result.RuntimeIdentifierPackages).Exists);
    }

    [Theory]
    [InlineData(false, null)]
    [InlineData(true, true)]
    public async Task AcquiredIndeterminateEvidenceCombinesWithRemoteProbe(
        bool remotePresent,
        bool? expectedAvailability)
    {
        const string source = "https://feed.example/v3/index.json";
        using var client = new HttpClient(new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "resources": [
                        {
                          "@type": "PackageBaseAddress/3.0.0",
                          "@id": "https://feed.example/flat/"
                        }
                      ]
                    }
                    """),
                "/flat/wrapper.package.any/1.0.0/wrapper.package.any.nuspec"
                    when remotePresent => Xml("""
                        <package>
                          <metadata>
                            <id>Wrapper.Package.any</id>
                            <version>1.0.0</version>
                          </metadata>
                        </package>
                        """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            }));
        var result = new InspectionResult
        {
            RuntimeIdentifierPackages =
            [
                new RidPackageReference
                {
                    RuntimeIdentifier = "any",
                    PackageId = "Wrapper.Package.any",
                },
            ],
        };
        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: "Wrapper.Package.any",
            Version: "1.0.0");

        IReadOnlyDictionary<string, NuspecProbeStatus> acquiredEvidence =
            await PackageInspector.MarkAcquiredRidPackagesAsync(
                result,
                resolution,
                wrapperVersion: "1.0.0");
        await RidPackageVerifier.VerifyAsync(
            client,
            result,
            "1.0.0",
            localDir: null,
            new VerboseLogger(enabled: false),
            new NuGetSourceOptions { Sources = [source] },
            acquiredEvidence);

        Assert.Equal(
            expectedAvailability,
            Assert.Single(result.RuntimeIdentifierPackages).Exists);
    }

    [Fact]
    public async Task InspectAsync_IdentifierAuditMetadataIncludesAlternatePackageId()
    {
        const string source = "https://audit.example/v3/index.json";
        using var client = new HttpClient(new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        {
                          "@id": "https://audit.example/registration/",
                          "@type": "RegistrationsBaseUrl/3.6.0"
                        }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json("""
                    {
                      "items": [
                        {
                          "lower": "1.0.0",
                          "upper": "1.0.0",
                          "items": [
                            {
                              "catalogEntry": {
                                "id": "Private.Package",
                                "version": "1.0.0",
                                "deprecation": {
                                  "reasons": [ "Legacy" ],
                                  "alternatePackage": {
                                    "id": "Δelta.Tools"
                                  }
                                }
                              }
                            }
                          ]
                        }
                      ]
                    }
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            }));

        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: "Private.Package",
            Version: "1.0.0",
            ProducerKey: NuGetCache.GetSourceKey(source));
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [source],
        };
        InspectionResult withoutMetadata =
            await PackageInspector.InspectAsync(
                resolution,
                "Private.Package",
                "1.0.0",
                isLocalFile: false,
                localFilePath: null,
                nuspec: null,
                client,
                new VerboseLogger(enabled: false),
                forceLatest: true,
                verbosity: Verbosity.Normal,
                fetchMetadata: false,
                sourceOptions: sourceOptions);
        Assert.DoesNotContain(
            IdentifierConfusionAudit.InspectPackage(withoutMetadata),
            value => value.Location
                == "Deprecation.AlternatePackageId");

        InspectionResult result = await PackageInspector.InspectAsync(
            resolution,
            "Private.Package",
            "1.0.0",
            isLocalFile: false,
            localFilePath: null,
            nuspec: null,
            client,
            new VerboseLogger(enabled: false),
            forceLatest: true,
            verbosity: Verbosity.Normal,
            fetchMetadata: true,
            sourceOptions: sourceOptions);

        IdentifierConfusionCase identifierCase = Assert.Single(
            IdentifierConfusionAudit.InspectPackage(result),
            value => value.Location
                == "Deprecation.AlternatePackageId");
        Assert.Equal("Package ID", identifierCase.Kind);
        Assert.Contains(
            0x0394,
            identifierCase.Confusion.NonAsciiCodePoints);
    }

    [Fact]
    public async Task InspectAsync_IdentifierAuditMetadataFailureRemainsVisible()
    {
        const string source = "https://audit-failure.example/v3/index.json";
        var requests = new List<Uri>();
        using var client = new HttpClient(new RoutingHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return request.RequestUri!.AbsolutePath switch
            {
                "/v3/index.json" => Json("""
                    {
                      "version": "3.0.0",
                      "resources": [
                        {
                          "@id": "https://audit-failure.example/registration/",
                          "@type": "RegistrationsBaseUrl/3.6.0"
                        }
                      ]
                    }
                    """),
                "/registration/private.package/index.json" => Json("""
                    {
                      "items": [
                        {
                          "@id": "/metadata/unavailable-page",
                          "lower": "1.0.0",
                          "upper": "1.0.0"
                        }
                      ]
                    }
                    """),
                "/metadata/unavailable-page" =>
                    new HttpResponseMessage(HttpStatusCode.BadGateway),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }));

        var resolution = new PackageExtractionResult(
            _root,
            TempDir: null,
            PackageName: "Private.Package",
            Version: "1.0.0",
            ProducerKey: NuGetCache.GetSourceKey(source));
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [source],
        };

        InspectionResult result = await PackageInspector.InspectAsync(
            resolution,
            "Private.Package",
            "1.0.0",
            isLocalFile: false,
            localFilePath: null,
            nuspec: null,
            client,
            new VerboseLogger(enabled: false),
            forceLatest: true,
            verbosity: Verbosity.Normal,
            fetchMetadata: true,
            requireIdentifierMetadata: true,
            sourceOptions: sourceOptions);
        await AuditSignalBuilder.PopulatePackageAuditAsync(
            result,
            client,
            new VerboseLogger(enabled: false),
            sourceOptions);

        Assert.Equal(
            IdentifierConfusionAuditFailureKind.PackageMetadataUnavailable,
            result.IdentifierConfusionFailure);
        AuditSignal signal = Assert.Single(
            result.AuditSignals!,
            value => value.Signal == "Identifier confusion");
        Assert.Equal("Unavailable", signal.Value);
        Assert.Equal(
            "package registry metadata unavailable",
            signal.Evidence);
        Assert.Contains(
            requests,
            request => request.AbsolutePath == "/metadata/unavailable-page");
    }

    [Fact]
    public async Task PackageCommand_IdentifierMetadataFailureIsScopedToIdentifierConsumers()
    {
        string packageId =
            $"Private.Command.{Guid.NewGuid():N}";
        string normalizedId = packageId.ToLowerInvariant();
        const string source =
            "https://command-audit.example/v3/index.json";
        byte[] package = CreatePackage(packageId);
        var requests = new List<Uri>();
        HttpResponseMessage Respond(HttpRequestMessage request)
        {
                requests.Add(request.RequestUri!);
                return request.RequestUri!.AbsolutePath switch
                {
                    "/v3/index.json" => Json($$"""
                        {
                          "version": "3.0.0",
                          "resources": [
                            {
                              "@id": "https://command-audit.example/flat/",
                              "@type": "PackageBaseAddress/3.0.0"
                            },
                            {
                              "@id": "https://command-audit.example/registration/",
                              "@type": "RegistrationsBaseUrl/3.6.0"
                            }
                          ]
                        }
                        """),
                    var path when path
                        == $"/flat/{normalizedId}/index.json" =>
                        Json("""{ "versions": [ "1.0.0" ] }"""),
                    var path when path
                        == $"/flat/{normalizedId}/1.0.0/"
                            + $"{normalizedId}.1.0.0.nupkg" =>
                        Bytes(package),
                    var path when path
                        == $"/registration/{normalizedId}/index.json" =>
                        Json(
                            $$"""
                            {
                              "items": [
                                {
                                  "@id": "/metadata/{{normalizedId}}/unavailable-page",
                                  "lower": "1.0.0",
                                  "upper": "1.0.0"
                                }
                              ]
                            }
                            """),
                    var path when path
                        == $"/metadata/{normalizedId}/unavailable-page" =>
                        new HttpResponseMessage(
                            HttpStatusCode.BadGateway),
                    _ => throw new InvalidOperationException(
                        $"Unexpected command request: "
                        + $"{request.Method} {request.RequestUri}"),
                };
        }
        using var client = new HttpClient(new RoutingHandler(Respond));
        var sourceOptions = new NuGetSourceOptions
        {
            Sources = [source],
        };
        InspectionOptions Options() => new()
        {
            PackageArgs = [$"{packageId}@1.0.0"],
            ForceLatest = true,
            Verbosity = Verbosity.Normal,
            IncludeSections =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    PackageSections.Signals,
                },
            SourceOptions = sourceOptions,
        };

        var rendered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                Options(),
                PayloadContext(client, Respond)));
        InspectionOptions discoveryOptions = Options() with
        {
            IncludeSections = null,
            Discover = [PackageSections.Signals],
        };
        var discovered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                discoveryOptions,
                PayloadContext(client, Respond)));
        InspectionOptions auditDiscoveryOptions = Options() with
        {
            IncludeSections = null,
            Discover = [PackageSections.AuditIdentifierConfusion],
        };
        var auditDiscovered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                auditDiscoveryOptions,
                PayloadContext(client, Respond)));
        InspectionOptions statisticsOptions = Options() with
        {
            IncludeSections =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    PackageSections.Statistics,
                },
        };
        var statistics = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                statisticsOptions,
                PayloadContext(client, Respond)));

        Assert.Equal(1, rendered.ExitCode);
        Assert.Contains("Identifier confusion", rendered.Output);
        Assert.Contains("Unavailable", rendered.Output);
        Assert.Equal(
            "Warning: Identifier audit failed for package input #1: "
            + "package registry metadata unavailable"
            + Environment.NewLine,
            rendered.Error);
        Assert.Equal(1, discovered.ExitCode);
        Assert.Contains("| Signal | column |", discovered.Output);
        Assert.Equal(
            "Warning: Identifier audit failed for package input #1: "
            + "package registry metadata unavailable"
            + Environment.NewLine,
            discovered.Error);
        Assert.Equal(1, auditDiscovered.ExitCode);
        Assert.Contains(
            "Warning: Identifier audit failed for package input #1: "
            + "package registry metadata unavailable",
            auditDiscovered.Error);
        Assert.Equal(0, statistics.ExitCode);
        Assert.DoesNotContain(
            "Identifier audit failed",
            statistics.Error);
        Assert.DoesNotContain(packageId, rendered.Error);
        Assert.DoesNotContain(packageId, discovered.Error);
        Assert.DoesNotContain(packageId, auditDiscovered.Error);
        Assert.DoesNotContain(packageId, statistics.Error);
        Assert.Contains(
            requests,
            request => request.AbsolutePath
                == $"/metadata/{normalizedId}/unavailable-page");
    }

    [Fact]
    public void PackageCommand_BareDiscoveryRequestsEverySection()
    {
        var pipeline =
            PackageSectionDescriptors.CreateCatalog().Pipeline;

        Assert.True(PackageCommand.DiscoverRequestsSection(
            [],
            PackageSections.Manifest,
            pipeline));
        Assert.True(PackageCommand.DiscoverRequestsSection(
            [],
            PackageSections.Signals,
            pipeline));
        Assert.False(PackageCommand.DiscoverRequestsSection(
            null,
            PackageSections.Manifest,
            pipeline));
        Assert.False(PackageCommand.DiscoverRequestsSection(
            [PackageSections.Signals],
            PackageSections.Manifest,
            pipeline));
    }

    [Fact]
    public void PackageCommand_SelectionConstrainsDiscoveryProducers()
    {
        var pipeline =
            PackageSectionDescriptors.CreateCatalog().Pipeline;
        var options = new InspectionOptions
        {
            Discover = [],
            IncludeSections =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PackageSections.PackageInfo,
                },
        };

        Assert.False(PackageCommand.RequestsSelectedOrDiscoveredSection(
            options,
            PackageSections.Manifest,
            pipeline));
        Assert.False(PackageCommand.RequestsSelectedOrDiscoveredSection(
            options,
            PackageSections.Signals,
            pipeline));
        Assert.False(PackageCommand.RequiresPackageMetadata(
            options,
            pipeline));

        options = options with
        {
            Discover = [PackageSections.PackageInfo],
            IncludeSections =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PackageSections.PackageInfo,
                    PackageSections.Manifest,
                },
        };

        Assert.True(PackageCommand.RequestsSelectedOrDiscoveredSection(
            options,
            PackageSections.PackageInfo,
            pipeline));
        Assert.False(PackageCommand.RequestsSelectedOrDiscoveredSection(
            options,
            PackageSections.Manifest,
            pipeline));
        Assert.False(PackageCommand.RequestsRidPackageAvailability(
            options,
            isLocalFile: false,
            pipeline));

        options = options with
        {
            Discover = [PackageSections.Manifest],
            IncludeSections =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PackageSections.Manifest,
                    PackageSections.Signals,
                    PackageSections.SourceLinkFiles,
                    PackageSections.Vulnerabilities,
                },
            Verbosity = Verbosity.Detailed,
        };
        InspectionOptions producerOptions =
            PackageCommand.CreateProducerOptions(
                options,
                userVerbosity: Verbosity.Minimal,
                pipeline);

        Assert.Equal(
            [PackageSections.Manifest],
            producerOptions.IncludeSections);
        Assert.Equal(Verbosity.Minimal, producerOptions.Verbosity);
        Assert.False(PackageCommand.RequiresPackageMetadata(
            producerOptions,
            pipeline));
        Assert.False(PackageCommand.AllowsVulnerabilityTraffic(
            producerOptions));
        Assert.Empty(pipeline.GetRequiredQueries(
            producerOptions.Verbosity,
            producerOptions.IncludeSections,
            producerOptions.FixedOverview,
            excludeUnbounded: true));
    }

    [Fact]
    public void PackageCommand_BareDiscoveryUsesBoundedProducerCandidates()
    {
        var pipeline =
            PackageSectionDescriptors.CreateCatalog().Pipeline;
        var options = new InspectionOptions
        {
            Discover = [],
            Verbosity = Verbosity.Detailed,
        };

        InspectionOptions producerOptions =
            PackageCommand.CreateProducerOptions(
                options,
                userVerbosity: Verbosity.Minimal,
                pipeline);
        HashSet<string> includeSections =
            producerOptions.IncludeSections!;

        Assert.Contains(
            PackageSections.Manifest,
            includeSections);
        Assert.DoesNotContain(
            PackageSections.SourceLinkFiles,
            includeSections);
        Assert.DoesNotContain(
            PackageSections.SourceLinkAvailability,
            includeSections);
        Assert.DoesNotContain(
            PackageSections.SourceLinkIntegrity,
            includeSections);
        Assert.DoesNotContain(
            PackageSections.SourceLinkMissingFiles,
            includeSections);
        Assert.DoesNotContain(
            PackageSections.AuditFindings,
            includeSections);
        Assert.Empty(pipeline.GetRequiredQueries(
            producerOptions.Verbosity,
            includeSections,
            producerOptions.FixedOverview,
            excludeUnbounded: true));

        options = options with
        {
            IncludeSections =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PackageSections.SourceLinkFiles,
                },
        };
        producerOptions = PackageCommand.CreateProducerOptions(
            options,
            userVerbosity: Verbosity.Minimal,
            pipeline);

        Assert.Empty(producerOptions.IncludeSections!);
    }

    [Fact]
    public async Task PackageCommand_BareDiscoveryDoesNotAcquireSignalPdbs()
    {
        string packageId =
            $"Private.Discovery.{Guid.NewGuid():N}";
        string normalizedId = packageId.ToLowerInvariant();
        const string source =
            "https://bounded-discovery.example/v3/index.json";
        byte[] package = CreatePackage(
            packageId,
            assemblyPath:
                typeof(PackageInspectorMetadataSourceTests)
                    .Assembly.Location);
        var requests = new List<Uri>();
        HttpResponseMessage Respond(HttpRequestMessage request)
        {
                requests.Add(request.RequestUri!);
                return request.RequestUri!.AbsolutePath switch
                {
                    "/v3/index.json" => Json($$"""
                        {
                          "version": "3.0.0",
                          "resources": [
                            {
                              "@id": "https://bounded-discovery.example/flat/",
                              "@type": "PackageBaseAddress/3.0.0"
                            },
                            {
                              "@id": "https://bounded-discovery.example/registration/",
                              "@type": "RegistrationsBaseUrl/3.6.0"
                            }
                          ]
                        }
                        """),
                    var path when path
                        == $"/flat/{normalizedId}/index.json" =>
                        Json("""{ "versions": [ "1.0.0" ] }"""),
                    var path when path
                        == $"/flat/{normalizedId}/1.0.0/"
                            + $"{normalizedId}.1.0.0.nupkg" =>
                        Bytes(package),
                    var path when path
                        == $"/registration/{normalizedId}/index.json" =>
                        new HttpResponseMessage(
                            HttpStatusCode.BadGateway),
                    _ => new HttpResponseMessage(
                        HttpStatusCode.NotFound),
                };
        }
        using var client = new HttpClient(new RoutingHandler(Respond));

        var discovered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                new InspectionOptions
                {
                    PackageArgs = [$"{packageId}@1.0.0"],
                    Discover = [],
                    ForceLatest = true,
                    SourceOptions = new NuGetSourceOptions
                    {
                        Sources = [source],
                    },
                },
                PayloadContext(client, Respond)));

        Assert.Equal(0, discovered.ExitCode);
        Assert.Contains("| Signals | section |", discovered.Output);
        Assert.DoesNotContain(
            "Identifier audit failed",
            discovered.Error);
        Assert.DoesNotContain(
            requests,
            request => request.AbsolutePath.EndsWith(
                ".snupkg",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            requests,
            request => request.AbsolutePath
                == $"/registration/{normalizedId}/index.json");
    }

    [Fact]
    public void PackageCommand_TargetedDiscoveryKeepsExplicitBoundedProducer()
    {
        var pipeline =
            PackageSectionDescriptors.CreateCatalog().Pipeline;
        var options = new InspectionOptions
        {
            Discover = [PackageSections.AuditIdentifierConfusion],
            Verbosity = Verbosity.Detailed,
        };

        InspectionOptions producerOptions =
            PackageCommand.CreateProducerOptions(
                options,
                userVerbosity: Verbosity.Minimal,
                pipeline);

        Assert.Contains(
            PackageSections.AuditIdentifierConfusion,
            producerOptions.IncludeSections!);
        Assert.True(PackageCommand.RequiresIdentifierMetadata(
            producerOptions,
            pipeline));
    }

    [Theory]
    [InlineData(PackageSections.Statistics)]
    [InlineData(PackageSections.Vulnerabilities)]
    public void PackageCommand_SelectedMetadataSectionsAuthorizeTheirProducer(
        string section)
    {
        var pipeline =
            PackageSectionDescriptors.CreateCatalog().Pipeline;
        var options = new InspectionOptions
        {
            IncludeSections =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    section,
                },
        };

        Assert.True(PackageCommand.RequiresPackageMetadata(
            options,
            pipeline));
        Assert.False(PackageCommand.RequiresIdentifierMetadata(
            options,
            pipeline));
        Assert.True(PackageCommand.AllowsVulnerabilityTraffic(
            options));
    }

    [Fact]
    public void PackageCommand_DetailedMetadataProducerAuthorizesVulnerabilityTraffic()
    {
        var options = new InspectionOptions
        {
            IncludeSections =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PackageSections.Manifest,
                },
            Verbosity = Verbosity.Detailed,
        };

        Assert.True(PackageCommand.AllowsVulnerabilityTraffic(
            options));
    }

    [Theory]
    [InlineData(Verbosity.Normal)]
    [InlineData(Verbosity.Detailed)]
    public void PackageCommand_LocalRenderedManifestRequestsRidAvailability(
        Verbosity verbosity)
    {
        var pipeline =
            PackageSectionDescriptors.CreateCatalog().Pipeline;
        var options = new InspectionOptions
        {
            Verbosity = verbosity,
        };

        Assert.True(PackageCommand.RequestsRidPackageAvailability(
            options,
            isLocalFile: true,
            pipeline));
        Assert.False(PackageCommand.RequestsRidPackageAvailability(
            options,
            isLocalFile: false,
            pipeline));
    }

    [Fact]
    public async Task PackageCommand_FlatContainerOnlyPreservesLocalIdentifierDetection()
    {
        string packageId =
            $"Private.Flat.{Guid.NewGuid():N}";
        string normalizedId = packageId.ToLowerInvariant();
        const string source =
            "https://flat-audit.example/v3/index.json";
        byte[] package = CreatePackage(
            packageId,
            dependencyId: "\u0405ystem.Text.Json");
        HttpResponseMessage Respond(HttpRequestMessage request) =>
                request.RequestUri!.AbsolutePath switch
                {
                    "/v3/index.json" => Json($$"""
                        {
                          "version": "3.0.0",
                          "resources": [
                            {
                              "@id": "https://flat-audit.example/flat/",
                              "@type": "PackageBaseAddress/3.0.0"
                            }
                          ]
                        }
                        """),
                    var path when path
                        == $"/flat/{normalizedId}/index.json" =>
                        Json("""{ "versions": [ "1.0.0" ] }"""),
                    var path when path
                        == $"/flat/{normalizedId}/1.0.0/"
                            + $"{normalizedId}.1.0.0.nupkg" =>
                        Bytes(package),
                    _ => new HttpResponseMessage(
                        HttpStatusCode.NotFound),
                };
        using var client = new HttpClient(new RoutingHandler(Respond));

        var rendered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                new InspectionOptions
                {
                    PackageArgs = [$"{packageId}@1.0.0"],
                    ForceLatest = true,
                    Verbosity = Verbosity.Normal,
                    IncludeSections =
                        new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            PackageSections.Signals,
                        },
                    SourceOptions = new NuGetSourceOptions
                    {
                        Sources = [source],
                    },
                },
                PayloadContext(client, Respond)));

        Assert.Equal(0, rendered.ExitCode);
        Assert.Contains(
            "| Identity | Identifier confusion | Detected |",
            rendered.Output);
        Assert.Contains(
            "reserved-prefix homoglyph (System)",
            rendered.Output);
        Assert.Contains(
            "source advertises no deprecation metadata",
            rendered.Output);
        Assert.Empty(rendered.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CommandContext PayloadContext(
        HttpClient client, Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(verbose: false, client, createPackageSourceComposition: () =>
            new DesktopPackageSourceComposition(
                TimeSpan.FromSeconds(5), new NoCredentials(),
                (_, _) => new RoutingHandler(respond)));

    private sealed class NoCredentials : NuGetFetch.Plugins.ICredentialSource
    {
        public bool HasCredentialSources => false;

        public Task<NuGetFetch.PackageSourceCredential?> GetCredentialsAsync(
            Uri uri, bool isRetry, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("These feeds do not request credentials.");
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Xml(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/xml"),
        };

    private static HttpResponseMessage Bytes(byte[] content) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };

    private static byte[] CreatePackage(
        string packageId,
        string? dependencyId = null,
        string? assemblyPath = null)
    {
        string dependencies = dependencyId is null
            ? ""
            : $"""
                  <dependencies>
                    <dependency id="{dependencyId}" version="1.0.0" />
                  </dependencies>
                """;
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (var writer = new StreamWriter(
                archive.CreateEntry(
                    $"{packageId}.nuspec").Open(),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(
                    $$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package>
                      <metadata>
                        <id>{{packageId}}</id>
                        <version>1.0.0</version>
                        <authors>Test</authors>
                        <description>Test package</description>
                    {{dependencies}}
                      </metadata>
                    </package>
                    """);
            }
            if (assemblyPath is not null)
            {
                using Stream assembly = File.OpenRead(assemblyPath);
                using Stream entry = archive.CreateEntry(
                    "lib/net8.0/Test.dll").Open();
                assembly.CopyTo(entry);
            }
        }
        return stream.ToArray();
    }

    private static void WriteNuspec(
        string directory,
        string packageId,
        string version,
        string? fileName = null)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(
                directory,
                fileName ?? $"{packageId}.nuspec"),
            $"<package><metadata><id>{packageId}</id>"
            + $"<version>{version}</version></metadata></package>");
    }

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(route(request));
    }
}
