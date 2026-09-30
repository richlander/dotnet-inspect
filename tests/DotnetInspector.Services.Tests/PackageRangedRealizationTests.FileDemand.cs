using System.IO.Compression;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// File demand and pull reads over ranged content: the contract gates of
/// <c>docs/design/package-read-demand.md#exact-file-demand</c>, over the real
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
    public async Task FileDemand_NamedEntry_ReadsTheRootFolderAndTheEntryFolder()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        var store = new InMemoryPackageStore();

        PackageHouseSettlement settlement = await environment.AcquireFilesAsync(
            store,
            PackageFileDemand.Create(["lib/net45/PCLStorage.xml"]));

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
    public async Task FileDemand_NamedFolder_ReadsItsSubfoldersToo()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireFilesAsync(
                new InMemoryPackageStore(),
                PackageFileDemand.Create([], ["package/"])));

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
    public async Task FileDemand_PullRead_StreamsTheMaterializedEntry()
    {
        byte[] archive = ReadPclStorage();
        await using RangedEnvironment environment = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive));
        const string Path = "lib/net45/PCLStorage.xml";

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireFilesAsync(
                new InMemoryPackageStore(),
                PackageFileDemand.Create([Path])));
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
    public async Task FileDemand_PullReadOfAnUnreadEntry_IsAVisibleRefusal()
    {
        await using RangedEnvironment environment = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage()));
        const string Unread = "lib/sl5/PCLStorage.dll";

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireFilesAsync(
                new InMemoryPackageStore(),
                PackageFileDemand.Create(["lib/net45/PCLStorage.xml"])));
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
    public async Task FileDemand_UnlistedName_FailsVisibly(PackagePayloadAccess access)
    {
        await using RangedEnvironment environment = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage()));

        PackageHouseSettlement settlement = await environment.AcquireFilesAsync(
            new InMemoryPackageStore(),
            PackageFileDemand.Create(["README.md"], ["skills/demo/"]),
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
    public async Task FileDemand_WarmRead_MakesNoRequest()
    {
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        PackageFileDemand files = PackageFileDemand.Create(["lib/net45/PCLStorage.xml"]);
        await using (RangedEnvironment first = RangedEnvironment.Create(
            new RangeFeed(PclStorage, PclStorageVersion, archive)))
        {
            Transfer(
                await first.AcquireFilesAsync(store, files),
                PackagePayloadOrigin.Ranged);
        }

        var warm = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(warm);
        PackageTransferReceipt receipt = Transfer(
            await environment.AcquireFilesAsync(store, files),
            PackagePayloadOrigin.Cache);

        Assert.Equal(PackageTransferPath.EntryCache, receipt.Path);
        Assert.Empty(receipt.Requests);
        Assert.Equal(0, warm.RangedRequests + warm.FullRequests);
    }

    /// <summary>
    /// The host-neutral exact-file operation owns House demand, ranged
    /// acquisition, resolution, pull reading, and cache reuse. Missing files
    /// remain typed failures rather than empty content.
    /// </summary>
    [Fact]
    public async Task PackageFileAcquisition_ColdWarmAndMissing_AreOrchestrated()
    {
        const string Path = "lib/net45/PCLStorage.xml";
        byte[] archive = ReadPclStorage();
        var store = new InMemoryPackageStore();
        var cold = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using (RangedEnvironment first = RangedEnvironment.Create(cold))
        {
            var acquired = Assert.IsType<PackageFileAcquisitionResult.Acquired>(
                await first.AcquireFileAsync(store, Path));
            PackageTransferReceipt receipt = Transfer(
                acquired.Settlement,
                PackagePayloadOrigin.Ranged);
            Assert.Equal(PackageTransferPath.Ranged, receipt.Path);
            Assert.True(receipt.BytesReceived < archive.Length);
            Assert.IsType<PackageHouseContentNarrowing.PackageWide>(
                acquired.Settlement.Result.Request.ContentQuery!.Narrowing);
            Assert.Equal(
                [Path],
                acquired.Settlement.Payload.Content.EnumerateEntries());

            await using PackageHousePayloadRead read = acquired.OpenRead();
            using var output = new MemoryStream();
            await read.CopyToAsync(
                output,
                TestContext.Current.CancellationToken);
            using ZipArchive oracle = new(new MemoryStream(archive));
            using Stream expected = oracle.GetEntry(Path)!.Open();
            Assert.Equal(ReadAll(expected), output.ToArray());
        }

        var warm = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using (RangedEnvironment second = RangedEnvironment.Create(warm))
        {
            var acquired = Assert.IsType<PackageFileAcquisitionResult.Acquired>(
                await second.AcquireFileAsync(store, Path));
            PackageTransferReceipt receipt = Transfer(
                acquired.Settlement,
                PackagePayloadOrigin.Cache);
            Assert.Equal(PackageTransferPath.EntryCache, receipt.Path);
            Assert.Empty(receipt.Requests);
            Assert.Equal(0, warm.RangedRequests + warm.FullRequests);
        }

        await using RangedEnvironment missingEnvironment =
            RangedEnvironment.Create(
                new RangeFeed(PclStorage, PclStorageVersion, archive));
        PackageFileAcquisitionResult missing =
            await missingEnvironment.AcquireFileAsync(
                new InMemoryPackageStore(),
                "README.md");
        Assert.IsType<PackageFileAcquisitionResult.Unavailable>(missing);
        Assert.Equal(PackageFileAcquisitionStatus.Missing, missing.Status);
        Assert.IsType<PackageHouseResult.NoMatch>(missing.Settlement.Result);

        await using RangedEnvironment absentPackageEnvironment =
            RangedEnvironment.Create(
                new RangeFeed(PclStorage, PclStorageVersion, archive));
        PackageFileAcquisitionResult absentPackage =
            await absentPackageEnvironment.AcquireFileAsync(
                new InMemoryPackageStore(),
                "README.md",
                packageId: "Missing.Package");
        Assert.Equal(
            PackageFileAcquisitionStatus.NotSettled,
            absentPackage.Status);
        Assert.IsNotType<PackageHouseSettlement.Acquired>(
            absentPackage.Settlement);
    }

    /// <summary>
    /// A file demand is validated like an entry name and belongs to an
    /// Acquire operation.
    /// </summary>
    [Fact]
    public void FileDemand_ValidatesPathsAndRequiresAcquire()
    {
        Assert.Throws<ArgumentException>(() => PackageFileDemand.Create([]));
        Assert.Throws<ArgumentException>(() => PackageFileDemand.Create(["../README.md"]));
        Assert.Throws<ArgumentException>(() => PackageFileDemand.Create(["/README.md"]));
        Assert.Throws<ArgumentException>(() => PackageFileDemand.Create(["skills/a/../SKILL.md"]));
        Assert.Throws<ArgumentException>(() => PackageFileDemand.Create(["skills\\a\\SKILL.md"]));
        Assert.Throws<ArgumentException>(() => PackageFileDemand.Create(["C:README.md"]));
        Assert.Throws<ArgumentException>(() => PackageFileDemand.Create(["skills/"]));
        Assert.Throws<ArgumentException>(() => PackageFileDemand.Create([], ["/"]));
        Assert.Throws<ArgumentException>(() => PackageFileDemand.Create([], [".."]));

        PackageFileDemand files = PackageFileDemand.Create(
            ["README.md", "readme.md"],
            ["skills/demo", "skills/demo/"]);
        Assert.Equal(["README.md"], files.Entries);
        Assert.Equal(["skills/demo/"], files.Folders);

        var coordinate = new PackageHouseDemand.Exact(
            PackageSourceCoordinate.Create(PclStorage, PclStorageVersion));
        Assert.Throws<ArgumentException>(() => new PackageHouseRequest(
            coordinate,
            PackageHouseOperation.Create(PackageHouseOperationProfile.Settle),
            fileDemand: files));
        Assert.Throws<ArgumentException>(() => new PackageHouseRequest(
            coordinate,
            PackageHouseOperation.Create(PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact("net45"),
            PackageHouseAssetSelectionKind.Compile,
            fileDemand: files));
        Assert.Throws<ArgumentException>(() =>
            new PackageFileAcquisitionRequest(
                PackageSourceCoordinate.Create(
                    PclStorage,
                    PclStorageVersion),
                "README.md",
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Settle)));
    }
}
