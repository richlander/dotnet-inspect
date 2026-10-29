using System.Net;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserRetainedWorkspaceActivationTests
{
    const string ConfiguredCallGraphRootPackage =
        "configured.callgraph.root";
    const string ConfiguredCallGraphTargetPackage =
        "configured.callgraph.target";
    const string ConfiguredCallGraphVersion = "1.0.0";
    const string ConfiguredCallGraphFramework = "netstandard2.0";
    const string ConfiguredCallGraphEndpoint =
        "https://configured.example/v3/index.json";

    [Fact]
    public async Task
        RetainedPackageCallGraph_UsesTheAdmittedWorkspacePackageSource()
    {
        byte[] rootArchive = ConfiguredCallGraphPackage(
            ConfiguredCallGraphRootPackage,
            FixtureCatalog.AnalysisCallerGraphCaller.AssemblyPath(),
            (ConfiguredCallGraphTargetPackage, ConfiguredCallGraphVersion));
        byte[] targetArchive = ConfiguredCallGraphPackage(
            ConfiguredCallGraphTargetPackage,
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath());
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            ConfiguredCallGraphRootPackage,
            ConfiguredCallGraphVersion,
            NuGetCache.GetSourceKey(ConfiguredCallGraphEndpoint),
            new MemoryStream(rootArchive),
            TestContext.Current.CancellationToken);
        using var handler = new ConfiguredCallGraphFeedHandler(
            (ConfiguredCallGraphRootPackage, rootArchive),
            (ConfiguredCallGraphTargetPackage, targetArchive));
        using var client = new HttpClient(handler);
        CompleteRestorationExecutionOptions baseline = await OptionsAsync();
        var authorization = new UniformPackageSourceAuthorization(
            [
                new PackageSource(
                    ConfiguredCallGraphEndpoint,
                    ConfiguredCallGraphEndpoint),
            ]);
        CompleteRestorationExecutionOptions options = baseline with
        {
            ContextLoad = baseline.ContextLoad with
            {
                HttpClient = client,
                SourceAuthorization = authorization,
                PackageStore = store,
                IncludePackageRootBindings = true,
            },
        };
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting = await ActivateAsync(
            owner,
            "configured-call-graph",
            ConfiguredCallGraphPacket());
        BrowserRetainedWorkspacePackagePresentation package =
            Assert.Single(posting.Packages);
        BrowserTypeSurfaceInfo type = Assert.Single(
            package.Surface.Types,
            candidate => candidate.QueryId == "Shared.Entry");
        BrowserMemberBodySelectorInfo body = Assert.Single(
            type.Api.SelectMany(member => member.BodySelectors),
            candidate => candidate.MemberName == "RunAcrossBoundary");

        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome> envelope =
            await BrowserRetainedCallGraphOperations.QueryAsync(
                owner,
                posting.RetainedDefinitionId,
                posting.RealizationId,
                package.NavigationId,
                package.Surface.Package,
                package.Surface.Version,
                package.Surface.ActiveFramework,
                type.AssemblyId,
                type.DefinitionId,
                body.MemberName,
                body.SelectorKey,
                body.Token,
                ConfiguredCallGraphFramework,
                package.Inventory.Package.RuntimeIdentifier,
                platformPruning: null,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var available = Assert.IsType<
            PackageDependencyMemberCallGraphInspectionOutcome.Available>(
                envelope.Content);
        Assert.Contains(
            available.Document.Routes,
            route =>
                route.Destination
                    is PackageDependencyMemberCallGraphInspectionDestination
                        .Package destination
                && destination.Descriptor.PackageId.Equals(
                    ConfiguredCallGraphTargetPackage,
                    StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            handler.Requested,
            request => request.AbsolutePath.EndsWith(
                $"/{ConfiguredCallGraphTargetPackage}/"
                    + $"{ConfiguredCallGraphVersion}/"
                    + $"{ConfiguredCallGraphTargetPackage}."
                    + $"{ConfiguredCallGraphVersion}.nupkg",
                StringComparison.OrdinalIgnoreCase));
        Assert.All(
            handler.Requested,
            request => Assert.Equal(
                "configured.example",
                request.Host));
    }

    static byte[] ConfiguredCallGraphPackage(
        string packageId,
        string assemblyPath,
        params (string PackageId, string Version)[] dependencies)
    {
        string dependencyXml = string.Join(
            Environment.NewLine,
            dependencies.Select(
                dependency =>
                    $"""<dependency id="{dependency.PackageId}" version="[{dependency.Version}]" />"""));
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            <package>
              <metadata>
                <id>{{packageId}}</id>
                <version>{{ConfiguredCallGraphVersion}}</version>
                <authors>dotnet-inspect</authors>
                <description>Configured-source call-graph fixture.</description>
                <dependencies>
                  <group targetFramework="{{ConfiguredCallGraphFramework}}">
                    {{dependencyXml}}
                  </group>
                </dependencies>
              </metadata>
            </package>
            """);
        return Archive(
            ($"{packageId}.nuspec", manifest),
            (
                $"lib/{ConfiguredCallGraphFramework}/"
                    + Path.GetFileName(assemblyPath),
                File.ReadAllBytes(assemblyPath)));
    }

    static string ConfiguredCallGraphPacket()
    {
        string json =
            """
            {"f":5,"s":[["__ENDPOINT__"]],"t":[["__PACKAGE__","__VERSION__","__FRAMEWORK__",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}
            """
            .Replace(
                "__ENDPOINT__",
                ConfiguredCallGraphEndpoint,
                StringComparison.Ordinal)
            .Replace(
                "__PACKAGE__",
                ConfiguredCallGraphRootPackage,
                StringComparison.Ordinal)
            .Replace(
                "__VERSION__",
                ConfiguredCallGraphVersion,
                StringComparison.Ordinal)
            .Replace(
                "__FRAMEWORK__",
                ConfiguredCallGraphFramework,
                StringComparison.Ordinal);
        return WorkspaceSharePacketCodec.Encode(
            WorkspaceSharePacketCodec.ParseJson(
                json,
                TestContext.Current.CancellationToken));
    }

    sealed class ConfiguredCallGraphFeedHandler(
        params (string PackageId, byte[] Archive)[] packages)
        : HttpMessageHandler
    {
        readonly Dictionary<string, (byte[] Archive, byte[] Manifest)>
            _packages = packages.ToDictionary(
                package => package.PackageId,
                package => (
                    package.Archive,
                    ReadManifest(package.PackageId, package.Archive)),
                StringComparer.OrdinalIgnoreCase);

        internal List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Uri uri = request.RequestUri
                ?? throw new InvalidOperationException(
                    "The configured-source test request has no URI.");
            Requested.Add(uri);
            if (uri.AbsolutePath == "/v3/index.json")
            {
                return Task.FromResult(Json(
                    """
                    {"version":"3.0.0","resources":[
                      {"@id":"https://configured.example/v3-flatcontainer/","@type":"PackageBaseAddress/3.0.0"},
                      {"@id":"https://configured.example/v3/registration5-gz-semver2/","@type":"RegistrationsBaseUrl/3.6.0"}
                    ]}
                    """));
            }

            foreach ((string packageId, (byte[] archive, byte[] manifest))
                in _packages)
            {
                string id = packageId.ToLowerInvariant();
                if (uri.AbsolutePath.Equals(
                    $"/v3-flatcontainer/{id}/index.json",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(Json(
                        JsonSerializer.Serialize(new
                        {
                            versions = new[]
                            {
                                ConfiguredCallGraphVersion,
                            },
                        })));
                }
                if (uri.AbsolutePath.Equals(
                    $"/v3/registration5-gz-semver2/{id}/index.json",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(Json(
                        JsonSerializer.Serialize(new
                        {
                            items = new[]
                            {
                                new
                                {
                                    items = new[]
                                    {
                                        new
                                        {
                                            catalogEntry = new
                                            {
                                                version =
                                                    ConfiguredCallGraphVersion,
                                                listed = true,
                                            },
                                        },
                                    },
                                },
                            },
                        })));
                }
                if (uri.AbsolutePath.Equals(
                    $"/v3-flatcontainer/{id}/"
                        + $"{ConfiguredCallGraphVersion}/{id}.nuspec",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(
                        HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(manifest),
                    });
                }
                if (uri.AbsolutePath.Equals(
                    $"/v3-flatcontainer/{id}/"
                        + $"{ConfiguredCallGraphVersion}/{id}."
                        + $"{ConfiguredCallGraphVersion}.nupkg",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(new HttpResponseMessage(
                        HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(archive),
                    });
                }
            }
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        static HttpResponseMessage Json(string content) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    content,
                    Encoding.UTF8,
                    "application/json"),
            };

        static byte[] ReadManifest(string packageId, byte[] archive)
        {
            using var buffer = new MemoryStream(archive, writable: false);
            using var package = new System.IO.Compression.ZipArchive(
                buffer,
                System.IO.Compression.ZipArchiveMode.Read);
            System.IO.Compression.ZipArchiveEntry entry =
                package.GetEntry($"{packageId}.nuspec")
                ?? throw new InvalidOperationException(
                    $"Package '{packageId}' has no manifest.");
            using Stream source = entry.Open();
            using var manifest = new MemoryStream();
            source.CopyTo(manifest);
            return manifest.ToArray();
        }
    }
}
