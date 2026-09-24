using System.IO.Compression;
using System.Text.Json;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections.Tests;

public sealed class ExactLibraryApiInspectionOperationTests
{
    const string PackageId = "exact-library-api.test";
    const string Version = "1.0.0";
    const string Framework = "net11.0";
    const string SourceUrl = "https://example.test/v3/index.json";

    static readonly PackageSource Source =
        new("test", SourceUrl);

    [Fact]
    public async Task ExecuteAsync_ReturnsExactDetachedLibrarySurface()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        var request = new ExactLibraryApiInspectionRequest(
            PackageId,
            Version,
            Framework,
            "DotnetInspector.Sections.dll");

        ExactLibraryApiInspectionExecution execution =
            await ExactLibraryApiInspectionOperation.ExecuteAsync(
                request,
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);

        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;
        ExactLibraryApiInspectionResult result = envelope.Content;
        Assert.Equal(
            ExactLibraryApiInspectionOutcome.Available,
            result.Outcome);
        Assert.Equal(
            "DotnetInspector.Sections.dll",
            result.Asset?.AssemblyName);
        Assert.Equal(
            NuGetCache.GetSourceKey(SourceUrl),
            result.Source?.Producer);
        Assert.Equal(
            typeof(ExactLibraryApiInspectionOperation).Module.ModuleVersionId,
            result.Assembly?.ModuleVersionId);
        Assert.NotNull(execution.Surface);
        Assert.Equal(
            execution.Surface.PublicTypeCount,
            result.Inventory?.TypeKinds.Sum(facet => facet.Count));
        Assert.NotEmpty(result.Inventory?.Namespaces ?? []);
        Assert.IsType<InspectionShare.Available>(envelope.Share);
        AssertDetachedContract();
    }

    [Fact]
    public async Task ExactAssetIdAndColdSelectionProduceEquivalentResults()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextLoadOptions options = LoadOptions(client, store);
        WorkspaceContextInput input = Input();
        PackageRootBinding root =
            Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Acquired>(
                await WorkspaceContextLoader.AcquirePackageRootAsync(
                    input,
                    options,
                    TestContext.Current.CancellationToken))
            .Root;
        PackageCompileAsset asset = Assert.Single(
            root.Root.AssetSelection.Assets,
            candidate => candidate.AssemblyName
                == "DotnetInspector.Sections.dll");
        var coldRequest = new ExactLibraryApiInspectionRequest(
            PackageId,
            Version,
            Framework,
            asset.AssemblyName);
        var exactRequest = new ExactLibraryApiInspectionRequest(
            PackageId,
            Version,
            Framework,
            asset.Id,
            ExactLibraryApiSelectionKind.AssetId);

        ExactLibraryApiInspectionExecution cold =
            await ExactLibraryApiInspectionOperation.ExecuteAsync(
                coldRequest,
                options,
                TestContext.Current.CancellationToken);
        await using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization realization =
            await workspace.RealizePackageAssemblyContextRolesAsync(
                root,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        ExactLibraryApiInspectionExecution retained =
            ExactLibraryApiInspectionOperation.Execute(
                root,
                realization,
                exactRequest,
                ExactLibraryApiInspectionOperation.DefaultLimits);

        Assert.Equal(
            JsonSerializer.Serialize(cold.Surface),
            JsonSerializer.Serialize(retained.Surface));
        Assert.Equal(
            cold.Inspection.Content.Assembly,
            retained.Inspection.Content.Assembly);
        Assert.Equal(
            JsonSerializer.Serialize(cold.Inspection.Content.Inventory),
            JsonSerializer.Serialize(retained.Inspection.Content.Inventory));
        Assert.Equal(
            cold.Inspection.Diagnostics,
            retained.Inspection.Diagnostics);
    }

    [Fact]
    public async Task
        PackageNamespaceDiscoveryReturnsDetachedExactLibraryDeclarations()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());
        WorkspaceContextInput input = Input();
        PackageRootBinding root =
            Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Acquired>(
                await WorkspaceContextLoader.AcquirePackageRootAsync(
                    input,
                    LoadOptions(client, store),
                    TestContext.Current.CancellationToken))
            .Root;
        InspectionEnvelope<PackageNamespaceDiscoveryOutcome> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using PackageAssemblyContextRealization realization =
                await workspace.RealizePackageAssemblyContextRolesAsync(
                    root,
                    cancellationToken:
                        TestContext.Current.CancellationToken);
            inspection =
                await PackageNamespaceDiscoveryInspection.ExecuteAsync(
                    realization,
                    new(
                        "DotnetInspector.Queries.Definitions",
                        new(
                            maxTypes: 500_000,
                            maxMembers: 0,
                            maxInspectionFailures: 10_000,
                            maxTypeForwarders: 100_000,
                            maxMetadataRows: int.MaxValue,
                            maxRetainedTextCharacters: int.MaxValue),
                        new(
                            maxCapturedImageBytes:
                                512L * 1024 * 1024,
                            maxRetainedArtifactBytes:
                                512L * 1024 * 1024)),
                    TestContext.Current.CancellationToken);
        }

        Assert.True(inspection.Content.IsComplete);
        Assert.Empty(inspection.Diagnostics);
        PackageNamespaceDiscoveryHit hit =
            Assert.Single(inspection.Content.Hits);
        Assert.Equal(PackageId, hit.PackageId);
        Assert.Equal(Version, hit.PackageVersion);
        Assert.Equal("DotnetInspector.Queries", hit.Library);
        Assert.Equal(
            "DotnetInspector.Queries.Definitions",
            hit.Namespace);
        Assert.Contains(
            hit.Declarations,
            static declaration =>
                declaration.Identity.ToMetadataFullName()
                    == "DotnetInspector.Queries.Definitions.WorkspaceSharePacket");
        Assert.All(
            hit.Declarations,
            static declaration =>
                Assert.Equal(
                    "DotnetInspector.Queries.Definitions",
                    declaration.Namespace.ToString()));
        AssertDetachedContract();
    }

    [Fact]
    public async Task MissingLibraryRetainsTypedFailureAndShare()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());

        ExactLibraryApiInspectionExecution execution =
            await ExactLibraryApiInspectionOperation.ExecuteAsync(
                new ExactLibraryApiInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Missing.dll"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;

        Assert.Equal(
            ExactLibraryApiInspectionOutcome.NotFound,
            envelope.Content.Outcome);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code == "exact-library-api.not-found");
        Assert.IsType<InspectionShare.Available>(envelope.Share);
    }

    [Fact]
    public async Task ProjectionBoundIsVisibleAndNotSuccessShaped()
    {
        var store = await CachedStoreAsync();
        using var client = new HttpClient(new FailingHandler());

        ExactLibraryApiInspectionExecution execution =
            await ExactLibraryApiInspectionOperation.ExecuteAsync(
                new ExactLibraryApiInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "DotnetInspector.Sections.dll"),
                LoadOptions(client, store),
                new ApiSurfaceProjectionLimits(
                    maxParticipants: 1,
                    maxTypes: 1,
                    maxMembers: 1,
                    maxInspectionFailures: 10,
                    maxTypeForwarders: 10,
                    maxMetadataRows: 10_000),
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;

        Assert.Equal(
            ExactLibraryApiInspectionOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Null(execution.Surface);
        Assert.NotNull(envelope.Content.Truncation);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "exact-library-api.projection-truncated");
    }

    [Fact]
    public async Task MalformedLibraryIsVisibleAsParticipantFailure()
    {
        var store = new InMemoryPackageStore();
        await CommitAsync(
            store,
            Archive(
                ($"ref/{Framework}/Malformed.dll",
                    [0x4d, 0x5a, 0, 1])));
        using var client = new HttpClient(new FailingHandler());

        ExactLibraryApiInspectionExecution execution =
            await ExactLibraryApiInspectionOperation.ExecuteAsync(
                new ExactLibraryApiInspectionRequest(
                    PackageId,
                    Version,
                    Framework,
                    "Malformed.dll"),
                LoadOptions(client, store),
                TestContext.Current.CancellationToken);
        InspectionEnvelope<ExactLibraryApiInspectionResult> envelope =
            execution.Inspection;

        Assert.Equal(
            ExactLibraryApiInspectionOutcome.Unavailable,
            envelope.Content.Outcome);
        Assert.Null(execution.Surface);
        Assert.Contains(
            envelope.Diagnostics,
            diagnostic =>
                diagnostic.Code
                    == "exact-library-api.participant-unavailable");
    }

    static WorkspaceContextLoadOptions LoadOptions(
        HttpClient client,
        IPackageStore store) =>
        new()
        {
            HttpClient = client,
            SourceAuthorization =
                new UniformPackageSourceAuthorization([Source]),
            PackageStore = store,
        };

    static WorkspaceContextInput Input() =>
        new()
        {
            Framework = Framework,
            Members =
            [
                WorkspaceMemberCoordinate.Package(
                    PackageId,
                    Version,
                    Framework),
            ],
        };

    static async Task<IPackageStore> CachedStoreAsync()
    {
        var store = new InMemoryPackageStore();
        byte[] package = Archive(
            ($"ref/{Framework}/DotnetInspector.Sections.dll",
                await File.ReadAllBytesAsync(
                    typeof(ExactLibraryApiInspectionOperation)
                        .Assembly
                        .Location,
                    TestContext.Current.CancellationToken)),
            ($"ref/{Framework}/DotnetInspector.Queries.dll",
                await File.ReadAllBytesAsync(
                    typeof(ExactLibraryApiInspectionQuery)
                        .Assembly
                        .Location,
                    TestContext.Current.CancellationToken)));
        await CommitAsync(store, package);
        return store;
    }

    static async Task CommitAsync(
        IPackageStore store,
        byte[] package)
    {
        await store.CommitAsync(
            PackageId,
            Version,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(package),
            TestContext.Current.CancellationToken);
    }

    static void AssertDetachedContract()
    {
        Type[] forbidden =
        [
            typeof(InspectionWorkspace),
            typeof(PackageRootBinding),
            typeof(PackageRootRealization),
            typeof(PackageAssemblyContextRealization),
            typeof(WorkspaceRealizationOperationLease),
            typeof(Stream),
            typeof(Delegate),
        ];
        Type[] contract =
        [
            typeof(InspectionEnvelope<ExactLibraryApiInspectionResult>),
            typeof(ExactLibraryApiInspectionExecution),
            typeof(ExactLibraryApiQueryExecution),
            typeof(ExactLibraryApiInspectionResult),
            typeof(ExactLibraryApiSourceCoordinate),
            typeof(ExactLibraryApiAsset),
            typeof(ExactLibraryApiAssemblyIdentity),
            typeof(ExactLibraryApiInventory),
            typeof(ExactLibraryApiInspectionFailure),
            typeof(PackageNamespaceDiscoveryRequest),
            typeof(PackageNamespaceDiscoveryHit),
            typeof(PackageNamespaceDiscoveryOutcome),
        ];
        foreach (Type type in contract)
        {
            foreach (System.Reflection.PropertyInfo property
                     in type.GetProperties())
            {
                Assert.DoesNotContain(
                    forbidden,
                    candidate =>
                        candidate.IsAssignableFrom(property.PropertyType));
            }
        }
    }

    static byte[] Archive(
        params (string EntryPath, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }
        return buffer.ToArray();
    }

    sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Unexpected request to {request.RequestUri}.");
    }
}
