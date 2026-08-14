using System.Net;
using System.Text;
using DotnetInspector.Core;
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

        using var client = new HttpClient();
        InspectionResult result = await PackageInspector.InspectAsync(
            resolution,
            "Wrapper.Package.any",
            "1.0.0",
            isLocalFile: true,
            localFilePath: localPackagePath,
            nuspec: null,
            client,
            new VerboseLogger(enabled: false),
            verifyRidPackageAvailability: true);

        Assert.Equal("Wrapper.Package.any", result.PackageName);
        Assert.Equal("Tool v2", new InspectionResultView(result).PackageType);
        Assert.Equal(["wrapper-command"], result.ToolCommands);
        Assert.True(result.IsRidSpecificPointerPackage);
        Assert.True(result.IsFrameworkDependent);
        Assert.False(result.HasRidSpecificAssets);
        RidPackageReference anyPackage = Assert.Single(
            result.RuntimeIdentifierPackages!,
            package => package.RuntimeIdentifier == "any"
                && package.PackageId == "Wrapper.Package.any");
        Assert.True(anyPackage.Exists);
        RidPackageReference linuxPackage = Assert.Single(
            result.RuntimeIdentifierPackages!,
            package => package.RuntimeIdentifier == "linux-x64");
        Assert.False(linuxPackage.Exists);
    }

    [Fact]
    public async Task InspectAsync_CachedUnknownRidAvailabilityIsRechecked()
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
        Assert.True(
            Assert.Single(
                PackageIndexCache.TryGet(
                    packageName,
                    version,
                    producerKey)!.RuntimeIdentifierPackages!).Exists);
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

    private static HttpResponseMessage Xml(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/xml"),
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
