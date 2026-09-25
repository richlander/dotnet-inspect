using System.IO.Compression;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Document demand and pull reads over ranged content: the contract gates of
/// <c>docs/design/package-read-demand.md#document-demand</c>, over the real
/// asset PCLStorage 1.0.2.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    private static readonly string[] PclStorageRootFolder =
    [
        "PCLStorage.nuspec",
        "[Content_Types].xml",
        ".signature.p7s",
    ];

    /// <summary>
    /// A named entry reads the root folder and that entry's folder, whole,
    /// and nothing else; the transfer receipt is the size probe, the
    /// directory tail, and entry spans only.
    /// </summary>
    [Fact]
    public async Task DocumentDemand_NamedEntry_ReadsTheRootFolderAndTheEntryFolder()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        var store = new InMemoryPackageStore();

        PackageHouseSettlement settlement = await environment.AcquireDocumentsAsync(
            store,
            PackageDocumentDemand.Create(["lib/net45/PCLStorage.xml"]));

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(settlement);
        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Null(acquired.Result.Evidence.Realization);
        var content = Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        Assert.Equal(
            PclStorageRootFolder.Concat(Net45Folder).Order(StringComparer.Ordinal),
            content.MaterializedEntries.Order(StringComparer.Ordinal));
        Assert.Null(store.TryGetCached(PclStorage, PclStorageVersion, null));

        PackageTransferReceipt receipt = Transfer(settlement, PackagePayloadOrigin.Ranged);
        Assert.Equal(PackageTransferPath.Ranged, receipt.Path);
        Assert.Equal(server.FullRequests + server.RangedRequests, receipt.RequestCount);
        Assert.Equal(PackageTransferRequestPurpose.SizeProbe, receipt.Requests[0].Purpose);
        Assert.Equal(PackageTransferRequestPurpose.DirectoryTail, receipt.Requests[1].Purpose);
        Assert.All(
            receipt.Requests.Skip(2),
            request => Assert.Equal(PackageTransferRequestPurpose.EntrySpan, request.Purpose));
        Assert.True(receipt.BytesReceived < archive.Length);
    }

    /// <summary>
    /// A named folder is read with every entry beneath it, subfolders
    /// included, because a skill keeps references and assets in subfolders
    /// of its folder. A named entry's own folder is read as the folder unit
    /// reads it, without its subfolders.
    /// </summary>
    [Fact]
    public async Task DocumentDemand_NamedFolder_ReadsItsSubfoldersToo()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireDocumentsAsync(
                new InMemoryPackageStore(),
                PackageDocumentDemand.Create([], ["package/"])));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        var content = Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        using ZipArchive oracle = new(new MemoryStream(archive));
        Assert.Equal(
            PclStorageRootFolder
                .Concat(oracle.Entries
                    .Select(entry => entry.FullName)
                    .Where(path => path.StartsWith("package/", StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal),
            content.MaterializedEntries.Order(StringComparer.Ordinal));
        Assert.Contains(
            content.MaterializedEntries,
            path => path.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Pull reads over ranged content: the read is cold until its first
    /// non-empty read, is bound to the House acquisition, and streams the
    /// already-checked bytes of the materialized entry.
    /// </summary>
    [Fact]
    public async Task DocumentDemand_PullRead_StreamsTheMaterializedEntry()
    {
        byte[] archive = ReadPclStorage();
        await using RangedEnvironment environment = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive));
        const string Path = "lib/net45/PCLStorage.xml";

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireDocumentsAsync(
                new InMemoryPackageStore(),
                PackageDocumentDemand.Create([Path])));
        Assert.True(Assert.IsType<RangedPackageContent>(acquired.Payload.Content)
            .TryGetEntryLength(Path, out long length));
        await using PackageHousePayloadRead read = acquired.OpenPayloadRead(Path, length);

        Assert.False(read.HasStarted);
        Assert.Equal(0, read.Read(Span<byte>.Empty));
        Assert.False(read.HasStarted);
        using var output = new MemoryStream();
        await read.CopyToAsync(output, bufferSize: 1024, TestContext.Current.CancellationToken);

        Assert.True(read.HasStarted);
        Assert.Same(acquired.Result.Evidence.Acquisition, read.Acquisition);
        using ZipArchive oracle = new(new MemoryStream(archive));
        using Stream expected = oracle.GetEntry(Path)!.Open();
        Assert.Equal(ReadAll(expected), output.ToArray());
    }

    /// <summary>
    /// Gate 15: a pull read of a ranged entry the acquisition did not read
    /// is a visible refusal, raised by the first read, not by opening.
    /// </summary>
    [Fact]
    public async Task DocumentDemand_PullReadOfAnUnreadEntry_IsAVisibleRefusal()
    {
        await using RangedEnvironment environment = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage()));
        const string Unread = "lib/sl5/PCLStorage.dll";

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireDocumentsAsync(
                new InMemoryPackageStore(),
                PackageDocumentDemand.Create(["lib/net45/PCLStorage.xml"])));
        var content = Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        Assert.False(content.IsMaterialized(Unread));
        Assert.True(content.TryGetEntryLength(Unread, out long length));
        await using PackageHousePayloadRead read = acquired.OpenPayloadRead(Unread, length);
        Assert.False(read.HasStarted);

        PackageEntryNotMaterializedException refusal =
            await Assert.ThrowsAsync<PackageEntryNotMaterializedException>(
                async () => await read.ReadExactlyAsync(
                    new byte[16],
                    TestContext.Current.CancellationToken));
        Assert.Equal(Unread, refusal.EntryPath);

        // An entry the directory does not list at all is unavailable, not
        // an empty read.
        await using PackageHousePayloadRead missing =
            acquired.OpenPayloadRead("lib/net45/Missing.dll", 16);
        await Assert.ThrowsAsync<FileNotFoundException>(
            async () => await missing.ReadExactlyAsync(
                new byte[16],
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Gate 14: a named entry or folder the archive's directory does not list
    /// is a typed House failure, never an empty success, on the ranged and
    /// the complete path alike.
    /// </summary>
    [Theory]
    [InlineData(PackagePayloadAccess.Ranged)]
    [InlineData(PackagePayloadAccess.Complete)]
    public async Task DocumentDemand_UnlistedName_FailsVisibly(PackagePayloadAccess access)
    {
        await using RangedEnvironment environment = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage()));

        PackageHouseSettlement settlement = await environment.AcquireDocumentsAsync(
            new InMemoryPackageStore(),
            PackageDocumentDemand.Create(["README.md"], ["skills/demo/"]),
            access);

        var noMatch = Assert.IsType<PackageHouseResult.NoMatch>(settlement.Result);
        Assert.Contains("'README.md'", noMatch.Reason.ToString(), StringComparison.Ordinal);
        Assert.Contains("'skills/demo/'", noMatch.Reason.ToString(), StringComparison.Ordinal);
        PackageHouseFailure.Stage stage = Assert.IsType<PackageHouseFailure.Stage>(
            Assert.Single(noMatch.Evidence.Failures));
        Assert.Equal(PackageHouseFailureStage.Selection, stage.StageKind);
    }

    /// <summary>
    /// The same document read twice with an entry-keeping store: the second
    /// is answered by the entry cache with no request.
    /// </summary>
    [Fact]
    public async Task DocumentDemand_WarmRead_MakesNoRequest()
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        PackageDocumentDemand documents = PackageDocumentDemand.Create(["lib/net45/PCLStorage.xml"]);
        await using (RangedEnvironment first = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive)))
        {
            Transfer(
                await first.AcquireDocumentsAsync(store, documents),
                PackagePayloadOrigin.Ranged);
        }

        var warm = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(warm);
        PackageTransferReceipt receipt = Transfer(
            await environment.AcquireDocumentsAsync(store, documents),
            PackagePayloadOrigin.Cache);

        Assert.Equal(PackageTransferPath.EntryCache, receipt.Path);
        Assert.Empty(receipt.Requests);
        Assert.Equal(0, warm.RangedRequests + warm.FullRequests);
    }

    /// <summary>
    /// A document demand is validated like an entry name and belongs to an
    /// Acquire operation.
    /// </summary>
    [Fact]
    public void DocumentDemand_ValidatesPathsAndRequiresAcquire()
    {
        Assert.Throws<ArgumentException>(() => PackageDocumentDemand.Create([]));
        Assert.Throws<ArgumentException>(() => PackageDocumentDemand.Create(["../README.md"]));
        Assert.Throws<ArgumentException>(() => PackageDocumentDemand.Create(["/README.md"]));
        Assert.Throws<ArgumentException>(() => PackageDocumentDemand.Create(["skills/a/../SKILL.md"]));
        Assert.Throws<ArgumentException>(() => PackageDocumentDemand.Create(["skills\\a\\SKILL.md"]));
        Assert.Throws<ArgumentException>(() => PackageDocumentDemand.Create(["C:README.md"]));
        Assert.Throws<ArgumentException>(() => PackageDocumentDemand.Create(["skills/"]));
        Assert.Throws<ArgumentException>(() => PackageDocumentDemand.Create([], ["/"]));
        Assert.Throws<ArgumentException>(() => PackageDocumentDemand.Create([], [".."]));

        PackageDocumentDemand documents = PackageDocumentDemand.Create(
            ["README.md", "readme.md"],
            ["skills/demo", "skills/demo/"]);
        Assert.Equal(["README.md"], documents.Entries);
        Assert.Equal(["skills/demo/"], documents.Folders);

        var coordinate = new PackageHouseDemand.Exact(
            PackageSourceCoordinate.Create(PclStorage, PclStorageVersion));
        Assert.Throws<ArgumentException>(() => new PackageHouseRequest(
            coordinate,
            PackageHouseOperation.Create(PackageHouseOperationProfile.Settle),
            documentDemand: documents));
        Assert.Throws<ArgumentException>(() => new PackageHouseRequest(
            coordinate,
            PackageHouseOperation.Create(PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net45"),
            PackageHouseAssetSelectionKind.Compile,
            documentDemand: documents));
    }
}
