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
using DotnetInspector.Views;

namespace DotnetInspector.Tests;

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
                "/registration/private.package/1.0.0.json" => Json(
                    """{ "published": "2025-01-02T00:00:00Z" }"""),
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
                "/registration/private.package/1.0.0.json" => Json("""
                    {
                      "catalogEntry": {
                        "deprecation": {
                          "reasons": [ "Legacy" ],
                          "alternatePackage": {
                            "id": "Δelta.Tools"
                          }
                        }
                      }
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
        using var client = new HttpClient(new RoutingHandler(request =>
            request.RequestUri!.AbsolutePath switch
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
                "/registration/private.package/1.0.0.json" => Json(
                    """{ "catalogEntry": "/catalog/private.package.json" }"""),
                "/catalog/private.package.json" =>
                    new HttpResponseMessage(HttpStatusCode.BadGateway),
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
    }

    [Fact]
    public async Task PackageCommand_IdentifierMetadataFailureIsNonzero()
    {
        string packageId =
            $"Private.Command.{Guid.NewGuid():N}";
        string normalizedId = packageId.ToLowerInvariant();
        const string source =
            "https://command-audit.example/v3/index.json";
        byte[] package = CreatePackage(packageId);
        using var client = new HttpClient(
            new RoutingHandler(request =>
                request.RequestUri!.AbsolutePath switch
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
                        == $"/registration/{normalizedId}/1.0.0.json" =>
                        Json(
                            $$"""
                            {
                              "catalogEntry":
                                "/catalog/{{normalizedId}}.json"
                            }
                            """),
                    var path when path
                        == $"/catalog/{normalizedId}.json" =>
                        new HttpResponseMessage(
                            HttpStatusCode.BadGateway),
                    _ => throw new InvalidOperationException(
                        $"Unexpected command request: "
                        + $"{request.Method} {request.RequestUri}"),
                }));
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
                new CommandContext(
                    verbose: false,
                    client)));
        InspectionOptions discoveryOptions = Options() with
        {
            IncludeSections = null,
            Discover = [PackageSections.Signals],
        };
        var discovered = await ConsoleCapture.RunAsync(
            () => PackageCommand.ExecuteAsync(
                discoveryOptions,
                new CommandContext(
                    verbose: false,
                    client)));

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
        Assert.DoesNotContain(packageId, rendered.Error);
        Assert.DoesNotContain(packageId, discovered.Error);
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
        using var client = new HttpClient(
            new RoutingHandler(request =>
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
                }));

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
                new CommandContext(
                    verbose: false,
                    client)));

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

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Bytes(byte[] content) =>
        new(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };

    private static byte[] CreatePackage(
        string packageId,
        string? dependencyId = null)
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
            using var writer = new StreamWriter(
                archive.CreateEntry(
                    $"{packageId}.nuspec").Open(),
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false));
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
        return stream.ToArray();
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
