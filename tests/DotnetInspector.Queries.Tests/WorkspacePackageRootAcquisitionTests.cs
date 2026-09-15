using System.IO.Compression;
using System.Net;

using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspacePackageRootAcquisitionTests
{
    const string PackageId = "workspace.root";
    const string Version = "1.0.0";
    const string Framework = "net10.0";
    static readonly PackageSource Source =
        new("fixture", "https://fixture.invalid/v3/index.json");

    [Fact]
    public async Task AcquiredRoot_PreservesExactFactsBeforeImageRealization()
    {
        var store = await CachedPackageAsync(
            ($"lib/{Framework}/Unread.dll", [1, 2, 3]));
        using var handler = new RejectingHandler();
        using var client = new HttpClient(handler);

        var acquired = Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Acquired>(
            await WorkspaceContextLoader.AcquirePackageRootAsync(
                Input(), Options(client, store), TestContext.Current.CancellationToken));

        Assert.Equal(PackageId, acquired.Root.Coordinate.PackageId);
        Assert.Equal(Version, acquired.Root.Coordinate.Version);
        Assert.Equal(Framework, acquired.Root.Coordinate.Framework);
        Assert.Equal(NuGetCache.GetSourceKey(Source.Url), acquired.Root.Coordinate.Producer);
        Assert.Equal(PackageCompileAssetSelectionStatus.Selected,
            acquired.Root.Root.AssetSelection.Status);
        Assert.Single(acquired.Root.Root.AssetSelection.Assets);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("readme.txt", PackageCompileAssetSelectionStatus.NoCompileAssets)]
    [InlineData("ref/net10.0/_._", PackageCompileAssetSelectionStatus.EmptyCompileGroup)]
    public async Task AcquiredRoot_PreservesAnAssemblyFreeSelection(
        string entry, PackageCompileAssetSelectionStatus expected)
    {
        var store = expected == PackageCompileAssetSelectionStatus.EmptyCompileGroup
            ? await CachedPackageAsync(
                (entry, []),
                ($"lib/{Framework}/Unread.dll", [1, 2, 3]))
            : await CachedPackageAsync((entry, []));
        using var client = new HttpClient(new RejectingHandler());

        var acquired = Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Acquired>(
            await WorkspaceContextLoader.AcquirePackageRootAsync(
                Input(), Options(client, store), TestContext.Current.CancellationToken));

        Assert.Equal(expected, acquired.Root.Root.AssetSelection.Status);
        Assert.Empty(acquired.Root.Root.AssetSelection.Assets);
        Assert.Equal(PackageId, acquired.Root.Coordinate.PackageId);
    }

    [Fact]
    public async Task CompatibleImplementationUniversePreservesRequestedCoordinate()
    {
        var store = await CachedPackageAsync(("lib/net8.0/Unread.dll", [1, 2, 3]));
        using var client = new HttpClient(new RejectingHandler());

        var acquired = Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Acquired>(
            await WorkspaceContextLoader.AcquirePackageRootAsync(
                Input(), Options(client, store), TestContext.Current.CancellationToken));

        Assert.Equal(Framework, acquired.Root.Coordinate.Framework);
        Assert.Equal("net8.0", acquired.Root.Root.AssetSelection.TargetFramework);
        Assert.Equal(PackageCompileAssetSelectionStatus.Selected,
            acquired.Root.Root.AssetSelection.Status);
        Assert.Single(acquired.Root.Root.AssetSelection.Assets);
    }

    [Fact]
    public async Task InvalidTarget_IsRejectedBeforeAuthorization()
    {
        using var handler = new RejectingHandler();
        using var client = new HttpClient(handler);
        var authorization = new DenyingAuthorization();

        var failed = Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Failed>(
            await WorkspaceContextLoader.AcquirePackageRootAsync(
                Input() with { Framework = "not a framework" },
                Options(client, new InMemoryPackageStore()) with
                {
                    SourceAuthorization = authorization,
                },
                TestContext.Current.CancellationToken));

        Assert.Contains(failed.Failures,
            failure => failure.Kind == WorkspaceContextLoadFailureKind.InvalidCoordinate);
        Assert.Empty(authorization.Requests);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task DeniedSource_CannotUseAnOtherwiseCachedRoot()
    {
        var store = await CachedPackageAsync(("readme.txt", []));
        using var handler = new RejectingHandler();
        using var client = new HttpClient(handler);
        var authorization = new DenyingAuthorization();

        var failed = Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Failed>(
            await WorkspaceContextLoader.AcquirePackageRootAsync(
                Input(),
                Options(client, store) with { SourceAuthorization = authorization },
                TestContext.Current.CancellationToken));

        Assert.Equal([PackageId], authorization.Requests);
        Assert.Equal(WorkspaceContextLoadFailureKind.PackageUnavailable,
            Assert.Single(failed.Failures).Kind);
        Assert.Equal("Fixture source denied.", failed.Failures[0].Message);
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task MultipleMembers_AreRejectedBeforeAuthorization()
    {
        using var client = new HttpClient(new RejectingHandler());
        var authorization = new DenyingAuthorization();

        var failed = Assert.IsType<WorkspacePackageRootAcquisitionOutcome.Failed>(
            await WorkspaceContextLoader.AcquirePackageRootAsync(
                Input() with
                {
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(PackageId, Version),
                        WorkspaceMemberCoordinate.Package("workspace.other", Version),
                    ],
                },
                Options(client, new InMemoryPackageStore()) with
                {
                    SourceAuthorization = authorization,
                },
                TestContext.Current.CancellationToken));

        Assert.Equal(WorkspaceContextLoadFailureKind.InvalidCoordinate,
            Assert.Single(failed.Failures).Kind);
        Assert.Empty(authorization.Requests);
    }

    static WorkspaceContextInput Input() => new()
    {
        Framework = Framework,
        Members = [WorkspaceMemberCoordinate.Package(PackageId, Version)],
    };

    static WorkspaceContextLoadOptions Options(HttpClient client, IPackageStore store) => new()
    {
        HttpClient = client,
        SourceAuthorization = new UniformPackageSourceAuthorization([Source]),
        PackageStore = store,
    };

    static async Task<InMemoryPackageStore> CachedPackageAsync(
        params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream manifest = archive.CreateEntry($"{PackageId}.nuspec").Open())
                manifest.Write("<package />"u8);
            foreach ((string path, byte[] content) in entries)
            {
                using Stream entry = archive.CreateEntry(path).Open();
                entry.Write(content);
            }
        }
        buffer.Position = 0;
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId, Version, NuGetCache.GetSourceKey(Source.Url),
            buffer, TestContext.Current.CancellationToken);
        return store;
    }

    sealed class DenyingAuthorization : IPackageSourceAuthorization
    {
        public List<string> Requests { get; } = [];

        public PackageSourceAuthorization AuthorizeSourcesFor(string packageId)
        {
            Requests.Add(packageId);
            return PackageSourceAuthorization.Deny("Fixture source denied.");
        }
    }

    sealed class RejectingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                RequestMessage = request,
            });
        }
    }
}
