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

    private static readonly string[] Net45Folder =
    [
        .. Net45Assemblies,
        "lib/net45/PCLStorage.xml",
        "lib/net45/PCLStorage.Abstractions.xml",
    ];

    /// <summary>
    /// Design gate 14a: PCLStorage 1.0.2 (real asset; its local extra fields
    /// are longer than its central records) realized for net45 by range with
    /// the Packages layer's slack and merge gap reads both selected
    /// assemblies in one request with no follow-up, retains only those
    /// entries, and commits nothing.
    /// </summary>
    [Fact]
    public async Task RangedRealize_RealAsset_ReadsTheSelectedEntriesInOneRequest()
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
        // The read fetches whole folders: the selected assemblies and the
        // documentation beside them.
        Assert.Equal(
            Net45Folder.Order(StringComparer.Ordinal),
            content.MaterializedEntries.Order(StringComparer.Ordinal));
        var compile = Assert.IsType<PackageHouseRealizationReceipt.Compile>(
            acquired.Result.Evidence.Realization);
        Assert.Equal(
            Net45Assemblies.Order(StringComparer.Ordinal),
            compile.Receipt.Selection.Assets
                .Select(asset => asset.Path)
                .Order(StringComparer.Ordinal));
        Assert.Same(content.GenerationIdentity, compile.Receipt.Generation);

        // One tail read for the directory, then one request for both
        // assemblies: the merge gap bridges the XML doc between them, and the
        // slack covers the longer local extra fields, so no follow-up.
        Assert.Equal(2, server.RangedRequests);
        // The one full request is the size probe, abandoned before its body.
        Assert.Equal(1, server.FullRequests);
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
    /// Size first: an archive at or under the cut is acquired complete and
    /// committed, with no ranged request; above it, the complete response is
    /// abandoned before its body and the archive is read by range.
    /// </summary>
    [Fact]
    public async Task SizeFirst_ArchiveAtOrUnderTheCut_IsAcquiredComplete()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        var store = new InMemoryPackageStore();
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                store,
                PackagePayloadAccess.Ranged,
                "net45",
                sizeCut: archive.Length));

        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.Equal(1, server.FullRequests);
        Assert.Equal(0, server.RangedRequests);
        Assert.NotNull(store.TryGetCached(
            PclStorage,
            PclStorageVersion,
            [acquired.Payload.ProducerKey]));
    }

    [Fact]
    public async Task SizeFirst_ArchiveAboveTheCut_AbandonsTheCompleteResponseAndReadsByRange()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net45",
                sizeCut: archive.Length - 1));

        Assert.Equal(PackagePayloadOrigin.Ranged, acquired.Payload.Origin);
        Assert.Equal(1, server.FullRequests);
        Assert.Equal(2, server.RangedRequests);
    }

    /// <summary>
    /// Size first: a complete response that advertises no length cannot be
    /// judged against the cut, so it is taken complete, not abandoned.
    /// </summary>
    [Fact]
    public async Task SizeFirst_NoAdvertisedLength_IsAcquiredComplete()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive) { OmitLength = true };
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net45",
                sizeCut: 1));

        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.Equal(1, server.FullRequests);
        Assert.Equal(0, server.RangedRequests);
    }

    /// <summary>
    /// Entry cache: a second realization of the same selection reads the
    /// cached directory and entries and makes no request.
    /// </summary>
    [Fact]
    public async Task EntryCache_WarmReadOfTheSameSelection_MakesNoRequest()
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        var cold = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using (RangedEnvironment first = RangedEnvironment.Create(cold))
        {
            Assert.Equal(
                PackagePayloadOrigin.Ranged,
                Assert.IsType<PackageHouseSettlement.Acquired>(
                    await first.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"))
                    .Payload.Origin);
        }

        var warm = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(warm);
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"));

        Assert.Equal(PackagePayloadOrigin.Cache, acquired.Payload.Origin);
        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Equal(0, warm.RangedRequests + warm.FullRequests);
        var content = Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        Assert.Equal(
            Net45Folder.Order(StringComparer.Ordinal),
            content.MaterializedEntries.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Entry cache: a later read missing an entry reads only that entry by
    /// range after the tail; the cached directory spares the size probe.
    /// </summary>
    [Fact]
    public async Task EntryCache_WarmReadMissingAnEntry_ReadsOnlyThatEntry()
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        await using (RangedEnvironment first = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive)))
        {
            Assert.IsType<PackageHouseSettlement.Acquired>(
                await first.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"));
        }
        store.RemoveEntryForTesting(PclStorage, PclStorageVersion, "lib/net45/PCLStorage.xml");

        var warm = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(warm);
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"));

        Assert.Equal(PackagePayloadOrigin.Ranged, acquired.Payload.Origin);
        Assert.Equal(0, warm.FullRequests);
        // The tail confirms the archive is unchanged, then the one missing
        // entry; the other three come from the entry cache.
        Assert.Equal(2, warm.RangedRequests);
        Assert.Equal(
            Net45Folder.Order(StringComparer.Ordinal),
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content)
                .MaterializedEntries.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Entry cache: a cached entry or directory that fails its checks is kept
    /// and bypassed, with a verbose diagnostic; the complete fetch answers and
    /// publishes to the complete store, which answers later reads
    /// (docs/design/package-cache-policy.md, case 5b).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EntryCache_InvalidItem_TakesTheCompleteFetch(bool directory)
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        await using (RangedEnvironment first = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive)))
        {
            Assert.IsType<PackageHouseSettlement.Acquired>(
                await first.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"));
        }
        if (directory)
            store.CorruptDirectoryForTesting(PclStorage, PclStorageVersion, [1, 2, 3]);
        else
            store.CorruptEntryForTesting(
                PclStorage, PclStorageVersion, "lib/net45/PCLStorage.dll", [1, 2, 3]);

        var warm = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(warm);
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"));

        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.Equal(1, warm.FullRequests);
        Assert.Equal(0, warm.RangedRequests);
        Assert.Contains(environment.Log, line => line.Contains("kept and bypassed", StringComparison.Ordinal));

        // The invalid item is left in place.
        IPackageEntryStore entries = store;
        if (directory)
        {
            Assert.True(entries.TryReadDirectory(PclStorage, PclStorageVersion, out ReadOnlyMemory<byte> region, out _));
            Assert.Equal(new byte[] { 1, 2, 3 }, region.ToArray());
        }
        else
        {
            Assert.True(entries.TryReadEntry(
                PclStorage, PclStorageVersion, "lib/net45/PCLStorage.dll", out byte[] content));
            Assert.Equal(new byte[] { 1, 2, 3 }, content);
        }

        await AssertLaterReadIsServedFromTheCompleteStoreAsync(store, archive);
    }

    private static async Task AssertLaterReadIsServedFromTheCompleteStoreAsync(
        InMemoryPackageStore store,
        byte[] archive)
    {
        var later = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(later);
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"));

        Assert.Equal(PackagePayloadOrigin.Cache, acquired.Payload.Origin);
        Assert.Equal(0, later.FullRequests);
        Assert.Equal(0, later.RangedRequests);
    }

    /// <summary>
    /// Entry cache: when the archive changed since its directory was cached,
    /// the fresh directory differs; the complete fetch answers with the whole
    /// archive and publishes to the complete store, which answers later reads,
    /// and no entry-cache item is replaced
    /// (docs/design/package-cache-policy.md, case 5c).
    /// </summary>
    [Fact]
    public async Task EntryCache_ChangedArchive_TakesTheCompleteFetch()
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        await using (RangedEnvironment first = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive)))
        {
            Assert.IsType<PackageHouseSettlement.Acquired>(
                await first.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"));
        }

        // The warm read is partial, and the coordinate was republished with
        // an unrelated entry removed: a private feed or mirror that is not
        // immutable.
        store.RemoveEntryForTesting(PclStorage, PclStorageVersion, "lib/net45/PCLStorage.xml");
        byte[] changed = Republish(archive, "lib/sl5/PCLStorage.xml");
        var warm = new RangeFeed(PclStorage, PclStorageVersion, changed);
        await using RangedEnvironment environment = RangedEnvironment.Create(warm);
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(store, PackagePayloadAccess.Ranged, "net45"));

        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.Equal(1, warm.FullRequests);
        Assert.IsNotType<RangedPackageContent>(acquired.Payload.Content);
        Assert.NotNull(store.TryGetCached(
            PclStorage,
            PclStorageVersion,
            [acquired.Payload.ProducerKey]));

        // The cached directory still describes the original archive, and the
        // entry the changed read lacked was not published from it.
        IPackageEntryStore entries = store;
        Assert.True(entries.TryReadDirectory(PclStorage, PclStorageVersion, out ReadOnlyMemory<byte> region, out long length));
        Assert.Equal(archive.Length, length);
        Assert.NotNull(ZipFetch.ZipArchiveReader.ReadDirectoryFromRegion(
                region, length, new ZipFetch.ZipReadLimits())
            .Find("lib/sl5/PCLStorage.xml"));
        Assert.False(entries.TryReadEntry(
            PclStorage, PclStorageVersion, "lib/net45/PCLStorage.xml", out _));

        await AssertLaterReadIsServedFromTheCompleteStoreAsync(store, changed);
    }

    private static byte[] Republish(byte[] archive, string removedEntry)
    {
        using var output = new MemoryStream();
        using (var source = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read))
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                if (entry.FullName == removedEntry)
                    continue;
                ZipArchiveEntry copy = target.CreateEntry(entry.FullName);
                using Stream from = entry.Open();
                using Stream to = copy.Open();
                from.CopyTo(to);
            }
        }
        return output.ToArray();
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
        // The size probe, the ignored range answered whole, and the complete
        // fetch.
        Assert.Equal(3, server.FullRequests);
        Assert.NotNull(store.TryGetCached(
            PclStorage,
            PclStorageVersion,
            [acquired.Payload.ProducerKey]));
    }

    /// <summary>
    /// A source that refuses ranged requests with an error status, or answers
    /// them with a malformed partial response, still serves the whole
    /// archive: the realization falls back to the complete fetch on the same
    /// authority and commits it.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable, false)]
    [InlineData(HttpStatusCode.NotImplemented, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(null, true)]
    public async Task RangedRealize_RangeErrorOrMalformedPartial_FallsBackToComplete(
        HttpStatusCode? rangedStatus,
        bool truncate)
    {
        var server = new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage())
        {
            RangedStatus = rangedStatus,
            TruncateRanges = truncate,
        };
        var store = new InMemoryPackageStore();
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                store,
                PackagePayloadAccess.Ranged,
                "net45"));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.True(server.RangedRequests >= 1);
        // The size probe, then the complete fetch the fallback takes.
        Assert.Equal(2, server.FullRequests);
        Assert.NotNull(store.TryGetCached(
            PclStorage,
            PclStorageVersion,
            [acquired.Payload.ProducerKey]));
    }

    /// <summary>
    /// A refused credential is not a range problem: the complete fetch would
    /// be refused the same way, so the source's failure is reported and no
    /// complete request is made.
    /// </summary>
    [Fact]
    public async Task RangedRealize_AuthenticationRefused_DoesNotFallBack()
    {
        var server = new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage())
        {
            RangedStatus = HttpStatusCode.Unauthorized,
        };
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        PackageHouseSettlement settlement = await environment.RealizeAsync(
            new InMemoryPackageStore(),
            PackagePayloadAccess.Ranged,
            "net45");

        Assert.IsNotType<PackageHouseSettlement.Acquired>(settlement);
        Assert.Contains(
            settlement.Result.Evidence.Failures,
            failure => failure is PackageHouseFailure.Authority
                {
                    Failure.Kind: PackageAuthorityFailureKind.AuthenticationRequired,
                }
                || failure is PackageHouseFailure.Source
                {
                    Failure.Kind: PackageSourceFailureKind.AuthenticationRequired,
                });
        // Only the size probe, abandoned; no complete fetch follows.
        Assert.Equal(1, server.FullRequests);
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
    /// bounds and chooses a one-KiB entry-read slack, a 64 KiB merge gap, and
    /// six requests in flight.
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
        Assert.Equal(1024, mapped.EntryReadSlack);
        Assert.Equal(64 * 1024, mapped.EntryMergeGap);
        Assert.Equal(6, mapped.MaxConcurrentReads);
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

        public ConcurrentQueue<string> Log { get; } = new();

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
            string framework,
            long sizeCut = 0)
        {
            // The real assets here are small, so the ranged gates set a zero
            // size cut; size first itself is gated separately.
            var house = new PackageHouse(
                Authorization,
                new PackagePayloadAcquisitionPlan(
                    (_, _) => store,
                    access: access,
                    log: Log.Enqueue,
                    rangedSizeCut: sizeCut));
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

        /// <summary>Answer complete requests without a Content-Length.</summary>
        public bool OmitLength { get; init; }

        /// <summary>A status every ranged request is answered with instead of 206.</summary>
        public HttpStatusCode? RangedStatus { get; init; }

        /// <summary>Answer ranged requests with a 206 one byte short of the range.</summary>
        public bool TruncateRanges { get; init; }

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
            if (range is not null && RangedStatus is { } refused)
            {
                Interlocked.Increment(ref _rangedRequests);
                return Task.FromResult(Respond(request, refused, new ByteArrayContent([])));
            }

            if (IgnoreRange || range is null)
            {
                Interlocked.Increment(ref _fullRequests);
                HttpResponseMessage full = Respond(
                    request,
                    HttpStatusCode.OK,
                    OmitLength ? new UnknownLengthContent(archive) : new ByteArrayContent(archive));
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
            if (TruncateRanges)
                end--;
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

    /// <summary>
    /// A body with no Content-Length, as a chunked response has. The read
    /// stream is created directly, because the base class would buffer the
    /// body to create it and so learn its length.
    /// </summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
