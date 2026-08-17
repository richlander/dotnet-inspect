using System.IO.Compression;
using System.Net;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class DependencyGraphServiceTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        $"dependency-graph-tests-{Guid.NewGuid():N}");

    public DependencyGraphServiceTests()
    {
        NuGetCache.Initialize(
            "dotnet-inspect-test",
            Path.Combine(_testRoot, "cache"),
            skipNuGetCache: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, recursive: true);
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_FileInput_ReturnsAssemblyReferenceGraph()
    {
        var assemblyPath = typeof(DependencyGraphServiceTests).Assembly.Location;
        using var httpClient = new HttpClient();
        var logger = new VerboseLogger(enabled: false);

        var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
            httpClient, assemblyPath, sourceOptions: null, logger);

        var graph = Assert.IsType<LibraryDependencyGraphResult.Graph>(result);
        Assert.Equal("dotnet-inspect.Tests", graph.AssemblyName);
        Assert.NotEmpty(graph.References);
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputUsesLibDescendingRootOrder()
    {
        var packageDir = Directory.CreateTempSubdirectory("depends-package-root-test").FullName;
        var packagePath = Path.Combine(packageDir, "DependsRoot.1.0.0.nupkg");
        var sourceAssembly = typeof(DependencyGraphServiceTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "lib/net6.0/A.dll");
                archive.CreateEntryFromFile(sourceAssembly, "lib/netstandard2.0/Z.dll");
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                httpClient,
                packagePath,
                sourceOptions: null,
                logger);

            var graph = Assert.IsType<LibraryDependencyGraphResult.Graph>(result);
            Assert.Equal("Z", graph.AssemblyName);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildLibraryDependencyTreeAsync_PackageInputWithNoLibAssembliesReportsSpecificError()
    {
        var packageDir = Directory.CreateTempSubdirectory("depends-package-no-lib-test").FullName;
        var packagePath = Path.Combine(packageDir, "NoLib.1.0.0.nupkg");
        var sourceAssembly = typeof(DependencyGraphServiceTests).Assembly.Location;

        try
        {
            using (var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(sourceAssembly, "tools/NoLib.dll");
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            var result = await DependencyGraphService.BuildLibraryDependencyTreeAsync(
                httpClient,
                packagePath,
                sourceOptions: null,
                logger);

            var error = Assert.IsType<LibraryDependencyGraphResult.Error>(result);
            Assert.Contains("No libraries found in package", error.Message);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildPackageDependencyTreeAsync_LocalPackageStillSupported()
    {
        string packageDir =
            Directory.CreateTempSubdirectory("depends-local-package-test")
                .FullName;
        string packagePath =
            Path.Combine(packageDir, "Depends.Local.1.0.0.nupkg");

        try
        {
            using (ZipArchive archive =
                ZipFile.Open(packagePath, ZipArchiveMode.Create))
            {
                ZipArchiveEntry nuspec =
                    archive.CreateEntry("Depends.Local.nuspec");
                await using Stream stream = nuspec.Open();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(
                    """
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>Depends.Manifest</id>
                        <version>1.0.0</version>
                      </metadata>
                    </package>
                    """);
            }

            using var httpClient = new HttpClient();
            var logger = new VerboseLogger(enabled: false);

            PackageDependencyGraphResult result =
                await DependencyGraphService.BuildPackageDependencyTreeAsync(
                    httpClient,
                    packagePath,
                    requestedTfm: null,
                    sourceOptions: null,
                    logger);

            var empty =
                Assert.IsType<PackageDependencyGraphResult.Empty>(result);
            Assert.Equal("Depends.Local", empty.PackageName);
            Assert.Equal(
                "Depends.Manifest",
                empty.ManifestPackageName);
        }
        finally
        {
            Directory.Delete(packageDir, recursive: true);
        }
    }

    [Fact]
    public async Task BuildPackageDependencyTreeAsync_CachedFloatingLookupTimesOutEarly()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Depends.Cached.{suffix}";
        const string Version = "1.0.0";
        string serviceIndex =
            $"https://feed.example.test/{suffix}/v3/index.json";
        SeedCachedPackage(packageId, Version, serviceIndex);
        var handler = new BlockingHandler();
        using var httpClient = new HttpClient(handler);
        var logger = new VerboseLogger(enabled: false);

        PackageDependencyGraphResult result =
            await DependencyGraphService.BuildPackageDependencyTreeAsync(
                httpClient,
                packageId,
                requestedTfm: null,
                new NuGetSourceOptions { Sources = [serviceIndex] },
                logger);

        var error =
            Assert.IsType<PackageDependencyGraphResult.Error>(result);
        Assert.Contains("lookup timed out", error.Message);
        Assert.Contains(
            $"Locally cached versions: {Version}",
            error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task BuildPackageDependencyTreeAsync_MissingLocalPackageReportsError()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Depends.Missing.{suffix}";
        string serviceIndex =
            $"https://feed.example.test/{suffix}/v3/index.json";
        string flatContainer =
            $"https://content.example.test/{suffix}/flat/";
        var handler = new ManifestOnlyHandler(
            serviceIndex,
            flatContainer,
            $"https://other.example.test/{suffix}/v3/index.json",
            $"https://other-content.example.test/{suffix}/flat/",
            packageId);
        using var httpClient = new HttpClient(handler);
        var logger = new VerboseLogger(enabled: false);
        string packagePath = Path.Combine(
            Path.GetTempPath(),
            suffix,
            $"{packageId}.1.0.0.nupkg");

        PackageDependencyGraphResult result =
            await DependencyGraphService.BuildPackageDependencyTreeAsync(
                httpClient,
                packagePath,
                requestedTfm: null,
                new NuGetSourceOptions { Sources = [serviceIndex] },
                logger);

        var error =
            Assert.IsType<PackageDependencyGraphResult.Error>(result);
        Assert.Contains("File not found", error.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task BuildPackageDependencyTreeAsync_AcquiresOnlyRootNuspec()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Depends.Root.{suffix}";
        string reportingServiceIndex =
            $"https://reporting.example.test/{suffix}/v3/index.json";
        string reportingFlatContainer =
            $"https://reporting-content.example.test/{suffix}/flat/";
        string otherServiceIndex =
            $"https://other.example.test/{suffix}/v3/index.json";
        string otherFlatContainer =
            $"https://other-content.example.test/{suffix}/flat/";
        var handler = new ManifestOnlyHandler(
            reportingServiceIndex,
            reportingFlatContainer,
            otherServiceIndex,
            otherFlatContainer,
            packageId);
        using var httpClient = new HttpClient(handler);
        var logger = new VerboseLogger(enabled: false);

        PackageDependencyGraphResult result =
            await DependencyGraphService.BuildPackageDependencyTreeAsync(
                httpClient,
                packageId,
                requestedTfm: null,
                new NuGetSourceOptions
                {
                    Sources =
                    [
                        otherServiceIndex,
                        reportingServiceIndex,
                    ],
                },
                logger);

        Assert.IsType<PackageDependencyGraphResult.Empty>(result);
        Assert.Contains(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                $"/{packageId.ToLowerInvariant()}.nuspec",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            handler.Requests,
            uri => uri.AbsoluteUri.StartsWith(
                    otherFlatContainer,
                    StringComparison.Ordinal)
                && uri.AbsolutePath.EndsWith(
                    $"/1.0.0/{packageId.ToLowerInvariant()}.nuspec",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageDependencies_AcquiresOnlyPrereleaseRootNuspec()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Package.Dependencies.{suffix}";
        string serviceIndex =
            $"https://feed.example.test/{suffix}/v3/index.json";
        string flatContainer =
            $"https://content.example.test/{suffix}/flat/";
        const string PreviewVersion = "2.0.0-preview.1";
        var handler = new ManifestOnlyHandler(
            serviceIndex,
            flatContainer,
            $"https://other.example.test/{suffix}/v3/index.json",
            $"https://other-content.example.test/{suffix}/flat/",
            packageId,
            reportingVersions: ["1.0.0", PreviewVersion],
            manifestVersion: PreviewVersion);
        using var httpClient = new HttpClient(handler);

        var rendered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                new InspectionOptions
                {
                    PackageArgs = [packageId],
                    ShowDependencies = true,
                    IncludePrerelease = true,
                    SourceOptions = new NuGetSourceOptions
                    {
                        Sources = [serviceIndex],
                    },
                },
                new CommandContext(
                    verbose: false,
                    httpClient)));

        Assert.Equal(0, rendered.ExitCode);
        Assert.Contains(
            "Tip: use 'depends --package' for dependency trees.",
            rendered.Error);
        Assert.Contains(
            "No dependencies declared in package.",
            rendered.Error);
        Assert.Contains(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                $"/{PreviewVersion}/{packageId.ToLowerInvariant()}.nuspec",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageDependencies_RequestedTfmRequiresExactRootGroup()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Package.Dependencies.Tfm.{suffix}";
        string serviceIndex =
            $"https://feed.example.test/{suffix}/v3/index.json";
        var handler = new ManifestOnlyHandler(
            serviceIndex,
            $"https://content.example.test/{suffix}/flat/",
            $"https://other.example.test/{suffix}/v3/index.json",
            $"https://other-content.example.test/{suffix}/flat/",
            packageId,
            dependenciesXml:
                """
                <dependencies>
                  <group targetFramework="net8.0" />
                </dependencies>
                """);
        using var httpClient = new HttpClient(handler);

        var rendered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                new InspectionOptions
                {
                    PackageArgs = [$"{packageId}@1.0.0"],
                    ShowDependencies = true,
                    Tfm = "net9.0",
                    SourceOptions = new NuGetSourceOptions
                    {
                        Sources = [serviceIndex],
                    },
                },
                new CommandContext(
                    verbose: false,
                    httpClient)));

        Assert.Equal(1, rendered.ExitCode);
        Assert.Contains(
            "No dependencies found for TFM 'net9.0'.",
            rendered.Error);
        Assert.Contains("Available TFMs: net8.0", rendered.Error);
        Assert.DoesNotContain(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PackageDependencies_PreviewCacheRetainsTimeoutDiagnostic()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Package.Dependencies.Preview.{suffix}";
        const string PreviewVersion = "2.0.0-preview.1";
        string serviceIndex =
            $"https://feed.example.test/{suffix}/v3/index.json";
        SeedCachedPackage(packageId, PreviewVersion, serviceIndex);
        using var httpClient = new HttpClient(
            new DelayedNotFoundHandler());
        var logger = new VerboseLogger(enabled: false);

        PackageDependencyGraphResult result =
            await DependencyGraphService.BuildPackageDependencyTreeAsync(
                httpClient,
                packageId,
                requestedTfm: null,
                new NuGetSourceOptions { Sources = [serviceIndex] },
                logger,
                includePrerelease: true);

        var error =
            Assert.IsType<PackageDependencyGraphResult.Error>(result);
        Assert.Contains("lookup timed out", error.Message);
        Assert.Contains(
            $"Locally cached versions: {PreviewVersion}",
            error.Message);
    }

    [Fact]
    public async Task PackageDependencies_FeedFailureRetainsSourceDiagnostic()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Package.Dependencies.Auth.{suffix}";
        string serviceIndex =
            $"https://feed.example.test/{suffix}/v3/index.json";
        using var httpClient = new HttpClient(
            new FixedStatusHandler(HttpStatusCode.Unauthorized));

        var rendered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                new InspectionOptions
                {
                    PackageArgs = [$"{packageId}@1.0.0"],
                    ShowDependencies = true,
                    SourceOptions = new NuGetSourceOptions
                    {
                        Sources = [serviceIndex],
                    },
                },
                new CommandContext(
                    verbose: false,
                    httpClient)));

        Assert.Equal(1, rendered.ExitCode);
        Assert.Contains(
            "could not be resolved because a source requires credentials",
            rendered.Error);
        Assert.Contains(serviceIndex, rendered.Error);
        Assert.DoesNotContain(
            "Nuspec for package",
            rendered.Error);
    }

    [Fact]
    public async Task PackageDependencies_MissingVersionRetainsVersionsHint()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Package.Dependencies.Missing.{suffix}";
        string serviceIndex =
            $"https://feed.example.test/{suffix}/v3/index.json";
        var handler = new ManifestOnlyHandler(
            serviceIndex,
            $"https://content.example.test/{suffix}/flat/",
            $"https://other.example.test/{suffix}/v3/index.json",
            $"https://other-content.example.test/{suffix}/flat/",
            packageId);
        using var httpClient = new HttpClient(handler);

        var rendered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                new InspectionOptions
                {
                    PackageArgs = [$"{packageId}@9.9.9"],
                    ShowDependencies = true,
                    SourceOptions = new NuGetSourceOptions
                    {
                        Sources = [serviceIndex],
                    },
                },
                new CommandContext(
                    verbose: false,
                    httpClient)));

        Assert.Equal(1, rendered.ExitCode);
        Assert.Contains(
            "Version '9.9.9' of package",
            rendered.Error);
        Assert.Contains(
            packageId,
            rendered.Error,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Use --versions to see available versions.",
            rendered.Error);
        Assert.DoesNotContain(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPackageDependencyTreeAsync_ReporterDoesNotReactivateDisabledAlias()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Depends.Alias.{suffix}";
        string serviceIndex =
            $"https://feed.example.test/{suffix}/v3/index.json";
        string flatContainer =
            $"https://content.example.test/{suffix}/flat/";
        Directory.CreateDirectory(_testRoot);
        string configPath = Path.Combine(
            _testRoot,
            $"NuGet-{suffix}.Config");
        File.WriteAllText(
            configPath,
            $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="active" value="{{serviceIndex}}" />
                <add key="disabled" value="{{serviceIndex}}" />
              </packageSources>
              <packageSourceCredentials>
                <disabled>
                  <add key="Username" value="disabled-user" />
                  <add key="ClearTextPassword" value="disabled-password" />
                </disabled>
              </packageSourceCredentials>
              <packageSourceMapping>
                <packageSource key="active">
                  <package pattern="Depends.*" />
                </packageSource>
                <packageSource key="disabled">
                  <package pattern="Depends.*" />
                </packageSource>
              </packageSourceMapping>
              <disabledPackageSources>
                <add key="disabled" value="true" />
              </disabledPackageSources>
            </configuration>
            """);
        var handler = new ManifestOnlyHandler(
            serviceIndex,
            flatContainer,
            $"https://other.example.test/{suffix}/v3/index.json",
            $"https://other-content.example.test/{suffix}/flat/",
            packageId);
        using var httpClient = new HttpClient(handler);
        var logger = new VerboseLogger(enabled: false);
        var sourceOptions =
            new NuGetSourceOptions { ConfigFile = configPath };

        PackageDependencyGraphResult result =
            await DependencyGraphService.BuildPackageDependencyTreeAsync(
                httpClient,
                packageId,
                requestedTfm: null,
                sourceOptions,
                logger);

        Assert.IsType<PackageDependencyGraphResult.Empty>(result);
        Assert.Contains(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                $"/{packageId.ToLowerInvariant()}.nuspec",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ResolvedSourceRestriction_ReleasesAmbientSources()
    {
        const string FeedA = "https://feed-a.example/v3/index.json";
        const string FeedB = "https://feed-b.example/v3/index.json";
        var original = new NuGetSourceOptions
        {
            Sources = [FeedA, FeedB],
        };
        NuGetSourceOptions restricted =
            NuGetSourceResolver.RestrictToResolvedSources(
                original,
                [new NuGetFetch.PackageSource(FeedA, FeedA)]);

        Assert.Equal(
            [FeedA],
            NuGetSourceResolver.ResolveSources(restricted)
                .Select(source => source.Url));

        NuGetSourceOptions? unrestricted =
            NuGetSourceResolver.WithoutSourceRestriction(restricted);
        Assert.Equal(
            [FeedA, FeedB],
            NuGetSourceResolver.ResolveSources(unrestricted)
                .Select(source => source.Url));
    }

    [Fact]
    public async Task BuildPackageDependencyTreeAsync_ToolPackageUsesArchive()
    {
        string suffix = Guid.NewGuid().ToString("N");
        string packageId = $"Depends.Tool.{suffix}";
        string serviceIndex =
            $"https://feed.example.test/{suffix}/v3/index.json";
        var handler = new ManifestOnlyHandler(
            serviceIndex,
            $"https://content.example.test/{suffix}/flat/",
            $"https://other.example.test/{suffix}/v3/index.json",
            $"https://other-content.example.test/{suffix}/flat/",
            packageId,
            isToolPackage: true);
        using var httpClient = new HttpClient(handler);
        var logger = new VerboseLogger(enabled: false);

        PackageDependencyGraphResult result =
            await DependencyGraphService.BuildPackageDependencyTreeAsync(
                httpClient,
                $"{packageId}@1.0.0",
                requestedTfm: null,
                new NuGetSourceOptions { Sources = [serviceIndex] },
                logger);

        Assert.IsType<PackageDependencyGraphResult.Error>(result);
        Assert.Contains(
            handler.Requests,
            uri => uri.AbsolutePath.EndsWith(
                ".nupkg",
                StringComparison.OrdinalIgnoreCase));
    }

    private void SeedCachedPackage(
        string packageId,
        string version,
        string source)
    {
        string staged = Path.Combine(
            _testRoot,
            $"staged-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staged);
        File.WriteAllText(
            Path.Combine(staged, $"{packageId}.nuspec"),
            $$"""
            <package>
              <metadata>
                <id>{{packageId}}</id>
                <version>{{version}}</version>
              </metadata>
            </package>
            """);
        Directory.CreateDirectory(Path.Combine(staged, "payload"));
        File.WriteAllText(
            Path.Combine(staged, "payload", "marker.txt"),
            "cached");
        NuGetCache.CommitPackage(
            staged,
            nupkgPath: null,
            packageId,
            version,
            NuGetCache.GetSourceKey(source));
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class DelayedNotFoundHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(1500),
                cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
            };
        }
    }

    private sealed class FixedStatusHandler(HttpStatusCode status)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(status)
                {
                    RequestMessage = request,
                });
    }

    private sealed class ManifestOnlyHandler(
        string reportingServiceIndex,
        string reportingFlatContainer,
        string otherServiceIndex,
        string otherFlatContainer,
        string packageId,
        bool isToolPackage = false,
        IReadOnlyList<string>? reportingVersions = null,
        string manifestVersion = "1.0.0",
        string dependenciesXml = "") : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);

            if (uri.AbsoluteUri.Equals(
                reportingServiceIndex,
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    ServiceIndex(reportingFlatContainer)));
            }
            if (uri.AbsoluteUri.Equals(
                otherServiceIndex,
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    ServiceIndex(otherFlatContainer)));
            }

            string normalizedPackageId = packageId.ToLowerInvariant();
            if (uri.AbsoluteUri.Equals(
                $"{reportingFlatContainer}{normalizedPackageId}/index.json",
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    System.Text.Json.JsonSerializer.Serialize(
                        new
                        {
                            versions =
                                reportingVersions ?? ["1.0.0"],
                        })));
            }
            if (uri.AbsoluteUri.Equals(
                $"{otherFlatContainer}{normalizedPackageId}/index.json",
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    """{"versions":["0.9.0"]}"""));
            }

            if (uri.AbsolutePath.EndsWith(
                $"/{manifestVersion}/{normalizedPackageId}.nuspec",
                StringComparison.Ordinal))
            {
                return Task.FromResult(Response(
                    "\uFEFF"
                    + $$"""
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>{{packageId}}</id>
                        <version>{{manifestVersion}}</version>
                        {{(isToolPackage ? "<packageTypes><packageType name=\"DotnetTool\" /></packageTypes>" : "")}}
                        {{dependenciesXml}}
                      </metadata>
                    </package>
                    """));
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                });
        }

        private static string ServiceIndex(string flatContainer) =>
            $$"""
            {
              "resources": [
                {
                  "@type": "PackageBaseAddress/3.0.0",
                  "@id": "{{flatContainer}}"
                }
              ]
            }
            """;

        private static HttpResponseMessage Response(string content) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(content),
            };
    }
}
