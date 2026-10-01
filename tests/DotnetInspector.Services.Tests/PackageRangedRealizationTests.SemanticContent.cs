using System.IO.Compression;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using NuGetFetch;
using ZipFetch;

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SemanticFilesAndFileList_RangedInadmissibleDirectoryIsRejected(
        bool cacheDirectory,
        bool unsafePath)
    {
        const string PackageId = "Rejected.Directory";
        const string Version = "1.0.0";
        byte[] archive = unsafePath
            ? TestPackageArchive.CreateWithContent(
                ("README.md", "valid"u8.ToArray()),
                ("../escape.txt", "invalid"u8.ToArray()),
                ($"{PackageId}.nuspec", "<package />"u8.ToArray()))
            : TestPackageArchive.CreateWithContent(
                ("README.md", "valid"u8.ToArray()),
                ("content", "file"u8.ToArray()),
                ("content/readme.txt", "descendant"u8.ToArray()),
                ($"{PackageId}.nuspec", "<package />"u8.ToArray()));
        var store = new InMemoryPackageStore();
        IPackageEntryStore entryStore = store;
        if (cacheDirectory)
        {
            ZipDirectory directory =
                ZipArchiveReader.ReadDirectoryFromRegion(
                    archive,
                    archive.LongLength,
                    ZipReadLimits.Default);
            await entryStore.PublishDirectoryAsync(
                PackageId,
                Version,
                directory.Region,
                directory.ArchiveLength);
        }

        var server = new RangeFeed(PackageId, Version, archive);
        await using RangedEnvironment environment =
            RangedEnvironment.Create(server);
        var settlement =
            Assert.IsType<PackageHouseSettlement.ResourceFree>(
                await environment.AcquireContentAsync(
                    store,
                    PackageHouseContentQuery.PackageFilesWithFileList(
                        ["README.md"]),
                    packageId: PackageId,
                    version: Version));

        PackageHouseResult.Rejected rejected =
            Assert.IsType<PackageHouseResult.Rejected>(settlement.Result);
        Assert.DoesNotContain(
            "../escape.txt",
            rejected.Reason.ToString(),
            StringComparison.Ordinal);
        Assert.Null(settlement.Result.Evidence.FileList);
        Assert.Equal(1, server.FullRequests);
        Assert.Equal(1, server.RangedRequests);
        Assert.Null(
            await entryStore.ReadEntryAsync(
                PackageId,
                Version,
                "README.md"));
        Assert.Equal(
            cacheDirectory,
            await entryStore.ReadDirectoryAsync(PackageId, Version) is not null);
        Assert.Equal(
            cacheDirectory,
            environment.Log.Any(
                static message => message.Contains(
                    "cannot be admitted",
                    StringComparison.Ordinal)));
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
    public async Task SemanticFiles_CaseAliasedArchiveIsRejectedBeforeSelection()
    {
        const string PackageId = "Ambiguous.Files";
        const string Version = "1.0.0";
        byte[] archive = CreateCaseAmbiguousArchive();
        await using RangedEnvironment environment =
            RangedEnvironment.Create(
                new RangeFeed(PackageId, Version, archive));

        var houseResult = Assert.IsType<PackageHouseSettlement.ResourceFree>(
            await environment.AcquireContentAsync(
                new InMemoryPackageStore(),
                PackageHouseContentQuery.PackageFiles(["README.md"]),
                packageId: PackageId,
                version: Version));
        Assert.IsType<PackageHouseResult.Rejected>(houseResult.Result);

        PackageFileAcquisitionResult result =
            await environment.AcquireFileAsync(
                new InMemoryPackageStore(),
                "README.md",
                packageId: PackageId,
                version: Version);

        Assert.IsType<PackageFileAcquisitionResult.Unavailable>(result);
        Assert.Equal(
            PackageFileAcquisitionStatus.NotSettled,
            result.Status);
        Assert.IsType<PackageHouseSettlement.ResourceFree>(
            result.Settlement);
    }

    [Fact]
    public void SemanticFiles_AdmittedCaseAmbiguityIsReported()
    {
        var files = new PackageHouseContentTerminal.Files(["README.md"]);

        PackageHouseFilesResolution resolution =
            files.Resolve(["README.md", "readme.md"]);

        Assert.Empty(resolution.SelectedEntries);
        Assert.Empty(resolution.MissingEntries);
        Assert.Equal(["README.md"], resolution.AmbiguousEntries);
    }

    [Fact]
    public async Task SemanticFiles_GlobalPackagesOmissionReadsFromRetainedArchive()
    {
        const string Path = "[Content_Types].xml";
        byte[] archiveBytes = TestPackageArchive.Create(
            "lib/net10.0/Sample.dll",
            "PCLStorage.nuspec",
            Path,
            "_rels/.rels");
        string root = Directory.CreateTempSubdirectory(
            "inspect-global-package-").FullName;
        try
        {
            string nupkg = System.IO.Path.Combine(
                root,
                $"{PclStorage}.{PclStorageVersion}.nupkg");
            File.WriteAllBytes(nupkg, archiveBytes);
            Directory.CreateDirectory(
                System.IO.Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                System.IO.Path.Combine(root, "lib", "net10.0", "Sample.dll"),
                [1, 2, 3]);
            File.WriteAllText(
                System.IO.Path.Combine(root, "pclstorage.nuspec"),
                """<?xml version="1.0"?><package />""");
            File.WriteAllText(
                System.IO.Path.Combine(root, ".nupkg.metadata"),
                "{}");

            await using RangedEnvironment environment =
                RangedEnvironment.Create(
                    new RangeFeed(
                        PclStorage,
                        PclStorageVersion,
                        archiveBytes));
            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: PackageSourceClientFactory
                    .GetProducerIdentity(
                        Assert.Single(
                            environment.Authorization
                                .AuthorizeSourcesFor(PclStorage)
                                .Authorities)
                            .Source)
                    .Key);
            Assert.False(content.TryOpenEntry(Path, out _));

            PackageFileAcquisitionResult.Acquired acquired =
                Assert.IsType<PackageFileAcquisitionResult.Acquired>(
                    await environment.AcquireFileAsync(
                        new CachedContentStore(content),
                        Path));
            Assert.Equal(
                PackagePayloadOrigin.Cache,
                acquired.Settlement.Payload.Origin);
            Assert.Contains(
                acquired.FileList.Entries,
                entry => entry.Path == Path);

            Assert.True(
                acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    out Stream? selected));
            using (selected)
            {
                Assert.Equal(
                    ReadEntry(archiveBytes, Path),
                    ReadAllBytes(selected));
            }

            PackageContentEntry selectedEntry =
                Assert.Single(
                    Assert.IsAssignableFrom<
                            IPackageContentEntryManifest>(
                            acquired.Settlement.Payload.Content)
                        .EnumerateEntriesWithLengths());
            Assert.True(
                acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    selectedEntry.Length,
                    out Stream? bounded));
            using (bounded)
            {
                Assert.Equal(
                    ReadEntry(archiveBytes, Path),
                    ReadAllBytes(bounded));
            }
            Assert.Throws<InvalidDataException>(
                () => acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    selectedEntry.Length - 1,
                    out _));

            await using PackageHousePayloadRead read =
                acquired.OpenRead();
            using var actual = new MemoryStream();
            await read.CopyToAsync(
                actual,
                TestContext.Current.CancellationToken);
            using var oracle = new ZipArchive(
                new MemoryStream(archiveBytes));
            ZipArchiveEntry? expectedEntry =
                oracle.GetEntry(Path);
            Assert.NotNull(expectedEntry);
            using Stream expected = expectedEntry.Open();
            using var expectedBytes = new MemoryStream();
            expected.CopyTo(expectedBytes);
            Assert.Equal(
                expectedBytes.ToArray(),
                actual.ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SemanticFiles_GlobalPackagesDivergentPhysicalEntryIsRejected()
    {
        const string Path = "lib/net10.0/Sample.dll";
        byte[] archiveBytes = TestPackageArchive.Create(
            Path,
            "PCLStorage.nuspec");
        string root = Directory.CreateTempSubdirectory(
            "inspect-global-package-divergent-").FullName;
        try
        {
            string nupkg = System.IO.Path.Combine(
                root,
                $"{PclStorage}.{PclStorageVersion}.nupkg");
            File.WriteAllBytes(nupkg, archiveBytes);
            Directory.CreateDirectory(
                System.IO.Path.Combine(root, "lib", "net10.0"));
            File.WriteAllBytes(
                System.IO.Path.Combine(
                    root,
                    "lib",
                    "net10.0",
                    "Sample.dll"),
                [3, 2, 1]);
            File.WriteAllText(
                System.IO.Path.Combine(root, "pclstorage.nuspec"),
                """<?xml version="1.0"?><package />""");

            await using RangedEnvironment environment =
                RangedEnvironment.Create(
                    new RangeFeed(
                        PclStorage,
                        PclStorageVersion,
                        archiveBytes));
            var content = new FileSystemPackageContent(
                root,
                nupkg,
                fromCache: true,
                producerKey: PackageSourceClientFactory
                    .GetProducerIdentity(
                        Assert.Single(
                            environment.Authorization
                                .AuthorizeSourcesFor(PclStorage)
                                .Authorities)
                            .Source)
                    .Key);

            PackageFileAcquisitionResult.Acquired acquired =
                Assert.IsType<PackageFileAcquisitionResult.Acquired>(
                    await environment.AcquireFileAsync(
                        new CachedContentStore(content),
                        Path));
            Assert.Equal(
                PackagePayloadOrigin.Cache,
                acquired.Settlement.Payload.Origin);

            Assert.True(
                acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    out Stream? selected));
            using (selected)
            {
                Assert.Throws<InvalidDataException>(
                    () => ReadAllBytes(selected));
            }

            PackageContentEntry selectedEntry =
                Assert.Single(
                    Assert.IsAssignableFrom<
                            IPackageContentEntryManifest>(
                            acquired.Settlement.Payload.Content)
                        .EnumerateEntriesWithLengths());
            Assert.True(
                acquired.Settlement.Payload.Content.TryOpenEntry(
                    Path,
                    selectedEntry.Length,
                    out Stream? bounded));
            using (bounded)
            {
                Assert.Throws<InvalidDataException>(
                    () => ReadAllBytes(bounded));
            }

            InspectionEnvelope<PackageFileContentDocument> inspection =
                await PackageFileContentInspection.ExecuteAsync(
                    new(
                        acquired,
                        PackageDocumentContentLimits.MaxDecodedBytes),
                    TestContext.Current.CancellationToken);
            Assert.Equal(
                PackageFileContentStatus.Failed,
                inspection.Content.Status);
            Assert.Empty(inspection.Content.Content);
            Assert.Equal(
                "package-file-content.read-failed",
                Assert.Single(inspection.Diagnostics).Code);
            Assert.Contains(
                "declared size or checksum",
                inspection.Content.Detail?.ToString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    private static byte[] ReadEntry(
        byte[] archiveBytes,
        string path)
    {
        using var archive = new ZipArchive(
            new MemoryStream(archiveBytes));
        ZipArchiveEntry? entry = archive.GetEntry(path);
        Assert.NotNull(entry);
        using Stream stream = entry.Open();
        return ReadAllBytes(stream);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    private sealed class CachedContentStore(IPackageContent content) :
        IPackageStore
    {
        public IPackageContent? TryGetCached(
            string packageName,
            string version,
            IReadOnlyList<string>? allowedSourceKeys,
            Action<string>? log = null) =>
            allowedSourceKeys?.Contains(
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
                "The test store serves only cached package content.");
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
