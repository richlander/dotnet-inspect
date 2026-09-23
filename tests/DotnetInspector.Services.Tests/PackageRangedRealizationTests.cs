using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Ranged payload realization through the operation lease and the House:
/// the package source model's ranged step, owned by
/// <c>docs/design/package-source-model.md#ranged-payload-realization</c>.
/// </summary>
public sealed class PackageRangedRealizationTests
{
    private const string Feed = "https://ranged.example/v3/index.json";
    private const string Flat = "https://ranged.example/flat2/";
    private const string PclStorage = "PCLStorage";
    private const string PclStorageVersion = "1.0.2";

    private static readonly string[] Net45Assemblies =
    [
        "lib/net45/PCLStorage.dll",
        "lib/net45/PCLStorage.Abstractions.dll",
    ];

    /// <summary>
    /// Design gate 14a: PCLStorage 1.0.2 (real asset; its local extra fields
    /// are longer than its central records) realized for net45 by range with
    /// the Packages layer's slack reads each selected assembly in exactly one
    /// request, retains only those entries, and commits nothing.
    /// </summary>
    [Fact]
    public async Task RangedRealize_RealAsset_ReadsEachSelectedEntryInOneRequest()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        var store = new InMemoryPackageStore();
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        PackageHouseSettlement settlement = await environment.RealizeAsync(
            store,
            PackagePayloadAccess.Ranged,
            "net45");

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(settlement);
        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Equal(PackagePayloadOrigin.Ranged, acquired.Payload.Origin);
        var content = Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        Assert.Equal(
            Net45Assemblies.Order(StringComparer.Ordinal),
            content.MaterializedEntries.Order(StringComparer.Ordinal));
        var compile = Assert.IsType<PackageHouseRealizationReceipt.Compile>(
            acquired.Result.Evidence.Realization);
        Assert.Equal(
            Net45Assemblies.Order(StringComparer.Ordinal),
            compile.Receipt.Selection.Assets
                .Select(asset => asset.Path)
                .Order(StringComparer.Ordinal));
        Assert.Same(content.GenerationIdentity, compile.Receipt.Generation);

        // One tail read for the directory, then one request per selected
        // entry: the slack covers the longer local extra fields.
        Assert.Equal(1 + Net45Assemblies.Length, server.RangedRequests);
        Assert.Equal(0, server.FullRequests);
        Assert.Null(store.TryGetCached(PclStorage, PclStorageVersion, null));
        using (ZipArchive oracle = new(new MemoryStream(archive)))
        {
            foreach (string path in Net45Assemblies)
            {
                Assert.True(content.TryOpenEntry(path, out Stream? stream));
                using (stream)
                using (Stream expected = oracle.GetEntry(path)!.Open())
                {
                    Assert.Equal(ReadAll(expected), ReadAll(stream!));
                }
            }
        }
    }

    /// <summary>
    /// Ranged content keeps the complete directory but no archive, and an
    /// entry the realization did not select is a visible refusal, not a
    /// missing entry.
    /// </summary>
    [Fact]
    public async Task RangedContent_UnmaterializedEntryIsVisible()
    {
        var server = new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage());
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net45"));
        IPackageContent content = acquired.Payload.Content;

        Assert.Contains("PCLStorage.nuspec", content.EnumerateEntries());
        Assert.Contains("lib/sl5/PCLStorage.dll", content.EnumerateEntries());
        Assert.True(
            ((IPackageContentEntryManifest)content).TryGetEntryLength(
                "lib/sl5/PCLStorage.dll",
                out long length));
        Assert.Equal(26_624, length);
        Assert.Null(content.RootPath);
        Assert.Null(content.NupkgPath);
        Assert.False(content.TryOpenArchive(out _));
        var refused = Assert.Throws<PackageEntryNotMaterializedException>(
            () => content.TryOpenEntry("PCLStorage.nuspec", out _));
        Assert.Equal("PCLStorage.nuspec", refused.EntryPath);
        Assert.False(content.TryOpenEntry("lib/net45/Missing.dll", out _));
    }

    /// <summary>
    /// A source that answers the ranged request with the whole archive is
    /// refused as <c>RangeIgnored</c>; the same authority's complete fetch
    /// serves the realization and commits to the authority's store.
    /// </summary>
    [Fact]
    public async Task RangedRealize_RangeIgnored_FallsBackToCompleteOnTheSameAuthority()
    {
        var server = new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage())
        {
            IgnoreRange = true,
        };
        var store = new InMemoryPackageStore();
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                store,
                PackagePayloadAccess.Ranged,
                "net45"));

        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.IsNotType<RangedPackageContent>(acquired.Payload.Content);
        Assert.Equal(2, server.FullRequests);
        Assert.NotNull(store.TryGetCached(
            PclStorage,
            PclStorageVersion,
            [acquired.Payload.ProducerKey]));
    }

    /// <summary>
    /// An authorized cache still answers first under ranged access; no
    /// package request is made.
    /// </summary>
    [Fact]
    public async Task RangedRealize_CachedPayloadAnswersWithoutTransfer()
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        var warm = new RangeFeed(PclStorage, PclStorageVersion, archive)
        {
            IgnoreRange = true,
        };
        await using (RangedEnvironment first = RangedEnvironment.Create(warm))
        {
            Assert.IsType<PackageHouseSettlement.Acquired>(
                await first.RealizeAsync(
                    store,
                    PackagePayloadAccess.Complete,
                    "net45"));
        }

        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                store,
                PackagePayloadAccess.Ranged,
                "net45"));

        Assert.Equal(PackagePayloadOrigin.Cache, acquired.Payload.Origin);
        Assert.Equal(0, server.RangedRequests + server.FullRequests);
    }

    /// <summary>
    /// A ranged read not bounded by a realization is refused before any
    /// source work: an Acquire operation selects no assets.
    /// </summary>
    [Fact]
    public async Task RangedAccess_RequiresRealize()
    {
        var server = new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage());
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        var house = new PackageHouse(
            environment.Authorization,
            new PackagePayloadAcquisitionPlan(
                (_, _) => new InMemoryPackageStore(),
                access: PackagePayloadAccess.Ranged));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(PclStorage, PclStorageVersion)),
            PackageHouseOperation.Create(PackageHouseOperationProfile.Acquire));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => house.ExecuteAsync(
                request,
                environment.Root.IssueOperationLease(
                    TestContext.Current.CancellationToken,
                    request.Operation.RequestTimeout,
                    request.Operation.OperationTimeout)));
        Assert.Equal(0, server.RangedRequests + server.FullRequests);
    }

    /// <summary>
    /// The Packages layer maps its payload limits into the archive reader's
    /// bounds and chooses the largest entry-read slack.
    /// </summary>
    [Fact]
    public void RangedLimits_MapThePayloadLimits()
    {
        var limits = new PackagePayloadLimits
        {
            MaxArchiveBytes = 1_000_000,
            MaxEntryCount = 321,
            MaxExpandedBytes = 7_000_000,
        };

        ZipFetch.ZipReadLimits mapped =
            PackageAcquisitionCandidatePayloadAcquirer.RangedLimits(limits);

        Assert.Equal(1_000_000, mapped.MaxArchiveBytes);
        Assert.Equal(321, mapped.MaxEntryCount);
        Assert.Equal(7_000_000, mapped.MaxExpandedBytes);
        Assert.Equal(ZipFetch.ZipReadLimits.MaxEntryReadSlack, mapped.EntryReadSlack);
        Assert.Equal(
            ZipFetch.ZipReadLimits.Default.MaxDirectoryBytes,
            mapped.MaxDirectoryBytes);
    }

    private static byte[] ReadPclStorage() =>
        File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "nugetfetch",
            "pclstorage.1.0.2.nupkg"));

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class RangedEnvironment : IAsyncDisposable
    {
        private readonly IPackageSourceClient _client;

        private RangedEnvironment(
            IPackageSourceAuthorization authorization,
            PackageSourceSettlementLease root,
            IPackageSourceClient client)
        {
            Authorization = authorization;
            Root = root;
            _client = client;
        }

        public IPackageSourceAuthorization Authorization { get; }

        public PackageSourceSettlementLease Root { get; }

        public static RangedEnvironment Create(RangeFeed feed)
        {
            PackageSourceAuthorization authorization =
                PackageSourceAuthorization.Authorize(
                    [new PackageSource("ranged", Feed)]);
            ConfiguredPackageAuthority authority =
                Assert.Single(authorization.Authorities);
            IPackageSourceClient client = PackageSourceClientFactory.Create(
                authority.Source,
                authority.Association,
                feed);
            PackageSourceSettlementLease root =
                PackageSourceSettlementService.IssueLease(_ => client);
            return new(new Fixed(authorization), root, client);
        }

        public Task<PackageHouseSettlement> RealizeAsync(
            IPackageStore store,
            PackagePayloadAccess access,
            string framework)
        {
            var house = new PackageHouse(
                Authorization,
                new PackagePayloadAcquisitionPlan(
                    (_, _) => store,
                    access: access));
            var request = new PackageHouseRequest(
                new PackageHouseDemand.Exact(
                    PackageSourceCoordinate.Create(PclStorage, PclStorageVersion)),
                PackageHouseOperation.Create(PackageHouseOperationProfile.Realize),
                PackageHouseTargetContext.Exact(framework),
                PackageHouseAssetSelectionKind.Compile);
            return house.ExecuteAsync(
                request,
                Root.IssueOperationLease(
                    TestContext.Current.CancellationToken,
                    request.Operation.RequestTimeout,
                    request.Operation.OperationTimeout));
        }

        public async ValueTask DisposeAsync()
        {
            await Root.DisposeAsync();
            _client.Dispose();
        }

        private sealed class Fixed(PackageSourceAuthorization authorization)
            : IPackageSourceAuthorization
        {
            public PackageSourceAuthorization AuthorizeSourcesFor(string packageId) =>
                authorization;
        }
    }

    /// <summary>
    /// A v3 feed that serves one archive and honors single byte ranges with
    /// <c>206</c>, <c>Content-Range</c>, and an <c>ETag</c>, or ignores them.
    /// </summary>
    private sealed class RangeFeed(string id, string version, byte[] archive)
        : HttpMessageHandler
    {
        private int _rangedRequests;
        private int _fullRequests;

        public bool IgnoreRange { get; init; }

        public int RangedRequests => Volatile.Read(ref _rangedRequests);

        public int FullRequests => Volatile.Read(ref _fullRequests);

        public ConcurrentQueue<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string url = request.RequestUri!.AbsoluteUri;
            Requests.Enqueue($"{url} {request.Headers.Range}");
            if (url == Feed)
            {
                return Task.FromResult(Respond(request, HttpStatusCode.OK, new StringContent($$"""
                    {"version":"3.0.0","resources":[
                      {"@id":"{{Flat}}","@type":"PackageBaseAddress/3.0.0"}
                    ]}
                    """)));
            }

            string lower = id.ToLowerInvariant();
            if (url != $"{Flat}{lower}/{version}/{lower}.{version}.nupkg")
                return Task.FromResult(Respond(request, HttpStatusCode.NotFound, new ByteArrayContent([])));

            RangeItemHeaderValue? range = request.Headers.Range?.Ranges.SingleOrDefault();
            if (IgnoreRange || range is null)
            {
                Interlocked.Increment(ref _fullRequests);
                HttpResponseMessage full = Respond(
                    request, HttpStatusCode.OK, new ByteArrayContent(archive));
                full.Headers.ETag = new EntityTagHeaderValue("\"pcl\"");
                return Task.FromResult(full);
            }

            long start;
            long end;
            if (range.From is null)
            {
                long suffix = Math.Min(range.To!.Value, archive.Length);
                start = archive.Length - suffix;
                end = archive.Length - 1;
            }
            else
            {
                start = range.From.Value;
                end = Math.Min(range.To ?? archive.Length - 1, archive.Length - 1);
            }

            Interlocked.Increment(ref _rangedRequests);
            var content = new ByteArrayContent(
                archive, (int)start, checked((int)(end - start + 1)));
            content.Headers.ContentRange =
                new ContentRangeHeaderValue(start, end, archive.Length);
            HttpResponseMessage partial = Respond(
                request, HttpStatusCode.PartialContent, content);
            partial.Headers.ETag = new EntityTagHeaderValue("\"pcl\"");
            return Task.FromResult(partial);
        }

        private static HttpResponseMessage Respond(
            HttpRequestMessage request,
            HttpStatusCode status,
            HttpContent content) =>
            new(status) { Content = content, RequestMessage = request };
    }
}
