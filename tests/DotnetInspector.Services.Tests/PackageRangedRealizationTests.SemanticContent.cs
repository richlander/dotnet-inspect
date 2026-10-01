using System.IO.Compression;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Semantic exact-entry acquisition gates for
/// <c>docs/design/package-house-semantic-content-demand.md#content-query</c>.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    [Fact]
    public async Task SemanticFiles_RangedReadPublishesOnlyExactEntries()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        string[] entries =
        [
            "lib/net45/PCLStorage.xml",
            "lib/sl5/PCLStorage.dll",
        ];
        PackageHouseContentQuery query =
            PackageHouseContentQuery.PackageFiles(entries);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                query));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Same(query, acquired.Result.Request.ContentQuery);
        Assert.Equal(
            entries.Order(StringComparer.Ordinal),
            acquired.Payload.Content
                .EnumerateEntries()
                .Order(StringComparer.Ordinal));
        Assert.Null(acquired.Payload.Content.RootPath);
        Assert.Null(acquired.Payload.Content.NupkgPath);
        Assert.False(acquired.Payload.Content.TryOpenArchive(out _));
        Assert.False(acquired.Payload.Content.TryOpenEntry(
            "lib/net45/PCLStorage.dll",
            out _));

        PackageTransferReceipt receipt = Transfer(
            acquired,
            PackagePayloadOrigin.Ranged);
        Assert.Equal(PackageTransferPath.Ranged, receipt.Path);
        Assert.True(receipt.BytesReceived < archive.Length);
    }

    [Fact]
    public async Task SemanticFiles_CompleteFallbackRetainsArchiveButPublishesOnlyExactEntries()
    {
        const string Path = "lib/net45/PCLStorage.xml";
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        var store = new InMemoryPackageStore();
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                PackageHouseContentQuery.PackageFilesWithFileList([Path]),
                sizeCut: archive.Length));

        Assert.Equal(PackagePayloadOrigin.Download, acquired.Payload.Origin);
        Assert.Equal([Path], acquired.Payload.Content.EnumerateEntries());
        Assert.False(acquired.Payload.Content.TryOpenEntry(
            "PCLStorage.nuspec",
            out _));
        IPackageContent? cached = store.TryGetCached(
            PclStorage,
            PclStorageVersion,
            [acquired.Payload.ProducerKey]);
        Assert.NotNull(cached);
        Assert.Contains("PCLStorage.nuspec", cached.EnumerateEntries());
        Assert.Contains(
            acquired.Result.Evidence.FileList!.Entries,
            entry => entry.Path == "PCLStorage.nuspec");
        PackageTransferReceipt receipt = Transfer(
            acquired,
            PackagePayloadOrigin.Download);
        Assert.Equal(PackageTransferPath.Download, receipt.Path);
        Assert.Equal(1, server.FullRequests);
        Assert.Equal(0, server.RangedRequests);
    }

    [Fact]
    public async Task SemanticFileList_CompleteAndCacheExcludeStoreSidecars()
    {
        const string Path = "lib/net45/PCLStorage.xml";
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        using var store = new TemporaryFileSystemPackageStore();
        PackageHouseContentQuery query =
            PackageHouseContentQuery.PackageFilesWithFileList([Path]);

        var first = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                query,
                sizeCut: archive.Length));
        var second = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                store,
                query,
                sizeCut: archive.Length));

        using var oracle = new ZipArchive(new MemoryStream(archive));
        string[] expected =
        [
            .. oracle.Entries
                .Where(static entry => !string.IsNullOrEmpty(entry.Name))
                .Select(static entry => entry.FullName),
        ];
        AssertFileList(first);
        AssertFileList(second);
        Assert.Equal(1, server.FullRequests);

        void AssertFileList(PackageHouseSettlement.Acquired acquired)
        {
            Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
            Assert.Equal([Path], acquired.Payload.Content.EnumerateEntries());
            PackageHouseFileList fileList =
                Assert.IsType<PackageHouseFileList>(
                    acquired.Result.Evidence.FileList);
            Assert.Equal(
                expected,
                fileList.Entries.Select(static entry => entry.Path));
            Assert.DoesNotContain(
                fileList.Entries,
                static entry => entry.Path.EndsWith(
                    ".nupkg",
                    StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(
                fileList.Entries,
                static entry => entry.Path.Equals(
                    NuGetCache.CommitMarkerFileName,
                    StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task SemanticFileList_RangedReadPublishesOnlyDirectoryEvidence()
    {
        byte[] archive = ReadPclStorage();
        var server = new RangeFeed(PclStorage, PclStorageVersion, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var query = new PackageHouseContentQuery(
            new PackageHouseContentNarrowing.PackageWide(),
            [new PackageHouseContentTerminal.FileList()]);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                query));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
        PackageHouseFileList fileList =
            Assert.IsType<PackageHouseFileList>(
                acquired.Result.Evidence.FileList);
        using var oracle = new ZipArchive(new MemoryStream(archive));
        Assert.Equal(
            oracle.Entries.Select(static entry => entry.FullName),
            fileList.Entries.Select(static entry => entry.Path));
        PackageTransferReceipt receipt = Transfer(
            acquired,
            PackagePayloadOrigin.Ranged);
        Assert.Equal(
            [
                PackageTransferRequestPurpose.SizeProbe,
                PackageTransferRequestPurpose.DirectoryTail,
            ],
            receipt.Requests.Select(static request => request.Purpose));
    }

    [Fact]
    public async Task SemanticFiles_MissingEntryFailsVisibly()
    {
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    PclStorage,
                    PclStorageVersion,
                    ReadPclStorage()));

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.PackageFiles(["README.md"])));

        var noMatch = Assert.IsType<PackageHouseResult.NoMatch>(
            acquired.Result);
        Assert.Contains(
            "'README.md'",
            noMatch.Reason.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
        PackageHouseFailure.Stage failure =
            Assert.IsType<PackageHouseFailure.Stage>(
                Assert.Single(noMatch.Evidence.Failures));
        Assert.Equal(
            PackageHouseFailureStage.Selection,
            failure.StageKind);
    }

    [Fact]
    public async Task SemanticFiles_CaseAmbiguousEntryIsReported()
    {
        const string PackageId = "Ambiguous.Files";
        const string Version = "1.0.0";
        byte[] archive = CreateCaseAmbiguousArchive();
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(PackageId, Version, archive));

        var houseResult = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.PackageFiles(["README.md"]),
                packageId: PackageId,
                version: Version));
        PackageHouseResult.NoMatch noMatch =
            Assert.IsType<PackageHouseResult.NoMatch>(houseResult.Result);
        Assert.Contains(
            "more than one entry",
            noMatch.Reason.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(houseResult.Payload.Content.EnumerateEntries());

        PackageFileAcquisitionResult result =
            await environment.AcquireFileAsync(
                new InMemoryPackageStore(),
                "README.md",
                packageId: PackageId,
                version: Version);

        Assert.IsType<PackageFileAcquisitionResult.Unavailable>(result);
        Assert.Equal(
            PackageFileAcquisitionStatus.Ambiguous,
            result.Status);
        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            result.Settlement);
        Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        Assert.Empty(acquired.Payload.Content.EnumerateEntries());
    }

    [Fact]
    public async Task SemanticFiles_RequiresHouseOwnedPayloadPlanning()
    {
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(
                    PclStorage,
                    PclStorageVersion,
                    ReadPclStorage()));
        var request = new PackageHouseRequest(
            new PackageHouseDemand.Exact(
                PackageSourceCoordinate.Create(
                    PclStorage,
                    PclStorageVersion)),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Acquire),
            contentQuery:
                PackageHouseContentQuery.PackageFiles(["README.md"]));
        var callerPlanned = new PackageHouse(
            environment.Authorization,
            new PackagePayloadAcquisitionPlan(
                static (_, _) => new InMemoryPackageStore(),
                access: PackagePayloadAccess.Ranged));

        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => callerPlanned.ExecuteAsync(
                    request,
                    environment.Root.IssueOperationLease(
                        TestContext.Current.CancellationToken,
                        request.Operation.RequestTimeout,
                        request.Operation.OperationTimeout)));
        Assert.Contains(
            "House-planned",
            failure.Message,
            StringComparison.Ordinal);
    }

    private static byte[] CreateCaseAmbiguousArchive()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            Write("README.md", "upper");
            Write("readme.md", "lower");

            void Write(string path, string text)
            {
                using StreamWriter writer =
                    new(archive.CreateEntry(path).Open());
                writer.Write(text);
            }
        }
        return output.ToArray();
    }

    private sealed class TemporaryFileSystemPackageStore :
        IPackageStore,
        IPreparedPackageStore,
        IDisposable
    {
        private readonly string _root =
            Directory.CreateTempSubdirectory(
                "inspect-semantic-content-").FullName;
        private IPackageContent? _content;

        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            _content is { } content
            && allowedSourceKeys?.Contains(
                content.ProducerKey,
                StringComparer.Ordinal) == true
                ? content
                : null;

        public ValueTask<IPackageContent> CommitAsync(
            string packageName,
            string version,
            string sourceKey,
            Stream nupkg,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException(
                "The test store requires prepared package commits.");

        async ValueTask<PreparedPackageCommit>
            IPreparedPackageStore.CommitPreparedAsync(
            string packageName,
            string version,
            string sourceKey,
            PackageArchivePayload archive,
            CancellationToken cancellationToken)
        {
            PreparedPackageCommit prepared =
                await FileSystemPackageStore.CommitAsync(
                    packageName,
                    version,
                    archive,
                    () => Directory.CreateDirectory(
                        Path.Combine(
                            _root,
                            $"staging-{Guid.NewGuid():N}")).FullName,
                    (extractedPath, nupkgPath) =>
                        NuGetCache.CommitPackageToSlotWithDisposition(
                            extractedPath,
                            nupkgPath,
                            packageName,
                            version,
                            sourceKey,
                            Path.Combine(_root, "slot"),
                            "semantic-content-test",
                            useAppCache: false),
                    cancellationToken);
            _content = prepared.Content;
            return prepared;
        }

        public void Dispose() =>
            Directory.Delete(_root, recursive: true);
    }
}
