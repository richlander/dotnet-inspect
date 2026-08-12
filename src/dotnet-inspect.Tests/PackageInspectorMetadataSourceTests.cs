using System.Net;
using System.Text;
using DotnetInspector.Core;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;

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

    private sealed class RoutingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(route(request));
    }
}
