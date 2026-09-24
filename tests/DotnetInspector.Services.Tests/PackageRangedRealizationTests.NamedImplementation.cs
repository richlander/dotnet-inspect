using System.Buffers.Binary;
using System.Text;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// Named implementation demand and aligned blocks: gates 6, 7, and 9 of
/// <c>docs/design/package-read-demand.md#pathological-cases-and-gates</c>.
/// </summary>
public sealed partial class PackageRangedRealizationTests
{
    private const string Avalonia = "Avalonia";
    private const string AvaloniaVersion = "12.1.2";
    private const string AvaloniaSurface = "ref/net10.0/";
    private const string AvaloniaImplementation = "lib/net10.0/";

    /// <summary>
    /// Gate 6: a Realize naming one implementation assembly of the real
    /// <c>Avalonia</c> 12.1.2 archive reads the surface folder whole and, of
    /// the implementation folder, only the aligned block that holds the
    /// named assembly, as one request; the receipt and the Root name only the
    /// named implementation asset, and roles realize over what was read.
    /// </summary>
    [Fact]
    public async Task NamedImplementation_RealAvalonia_ReadsTheSurfaceAndOnlyTheNamedBlock()
    {
        byte[] archive = ReadAvalonia();
        ArchiveOracle oracle = ArchiveOracle.Read(archive);
        IReadOnlyList<string> block = oracle.BlockOf(
            AvaloniaImplementation + "Avalonia.Dialogs.dll",
            PackageRangedRead.DefaultSizeCut);
        // The block the design rule gives this archive: from the
        // DesignerSupport documentation through Avalonia.Markup's, 15 entries.
        Assert.Equal(15, block.Count);
        Assert.Equal(AvaloniaImplementation + "Avalonia.DesignerSupport.xml", block[0]);
        Assert.Equal(AvaloniaImplementation + "Avalonia.Markup.xml", block[^1]);
        (long blockStart, long blockEnd) = oracle.Extent(block);
        // The archive interleaves lib/net8.0 with lib/net10.0: the 14 net8.0
        // entries between the block's first and last entries lie wholly
        // inside its request, and are read and kept with it.
        IReadOnlyList<string> covered = oracle.Between(block[0], block[^1], except: block);
        Assert.Equal(14, covered.Count);
        Assert.All(covered, name => Assert.StartsWith("lib/net8.0/", name, StringComparison.Ordinal));

        var store = new InMemoryPackageStore();
        var server = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment environment = RangedEnvironment.Create(server);
        PackageHouseSettlement settlement = await environment.RealizeAsync(
            store,
            PackagePayloadAccess.Ranged,
            "net10.0",
            PackageRangedRead.DefaultSizeCut,
            Avalonia,
            AvaloniaVersion,
            ["avalonia.dialogs.DLL"]);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(settlement);
        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        var content = Assert.IsType<RangedPackageContent>(acquired.Payload.Content);
        Assert.Equal(
            oracle.Folder(AvaloniaSurface).Concat(block).Concat(covered).Order(StringComparer.Ordinal),
            content.MaterializedEntries.Order(StringComparer.Ordinal));
        IPackageEntryStore entries = store;
        Assert.All(covered, name =>
        {
            Assert.True(entries.TryReadEntry(Avalonia, AvaloniaVersion, name, out byte[] cached));
            Assert.True(content.TryOpenEntry(name, out Stream? stream));
            using (stream)
                Assert.Equal(ReadAll(stream!), cached);
        });

        // The receipt names only what was read: the surface and the named
        // implementation asset.
        var compile = Assert.IsType<PackageHouseRealizationReceipt.Compile>(
            acquired.Result.Evidence.Realization);
        Assert.Empty(compile.UnmatchedImplementationNames);
        Assert.Equal(
            [AvaloniaImplementation + "Avalonia.Dialogs.dll"],
            compile.Selection.ImplementationAssets.Select(asset => asset.Path));
        Assert.All(
            compile.Selection.Assets.Concat(compile.Selection.ImplementationAssets),
            asset => Assert.True(content.IsMaterialized(asset.Path)));

        // One request reads the whole block; every other entry span lies in
        // the surface folder.
        PackageTransferReceipt receipt = Transfer(settlement, PackagePayloadOrigin.Ranged);
        PackageTransferRange[] spans =
        [
            .. receipt.Requests
                .Where(request => request.Purpose == PackageTransferRequestPurpose.EntrySpan)
                .Select(request => request.Range!.Value),
        ];
        PackageTransferRange blockSpan = Assert.Single(
            spans,
            span => span.Start < blockEnd && span.Start + span.Length > blockStart);
        Assert.Equal(blockStart, blockSpan.Start);
        Assert.InRange(
            blockSpan.Start!.Value + blockSpan.Length,
            oracle.DeclaredEnd(block[^1]),
            blockEnd + PackageAcquisitionCandidatePayloadAcquirer.RangedEntryReadSlack);
        (long surfaceStart, long surfaceEnd) = oracle.Extent(oracle.Folder(AvaloniaSurface));
        Assert.All(
            spans.Where(span => span != blockSpan),
            span =>
            {
                Assert.True(span.Start >= surfaceStart);
                Assert.True(
                    span.Start + span.Length
                    <= surfaceEnd + PackageAcquisitionCandidatePayloadAcquirer.RangedEntryReadSlack);
            });
        Assert.True(
            blockSpan.Length < oracle.StoredBytes(oracle.Folder(AvaloniaImplementation)));

        // The Root prepares an implementation role only for the named asset.
        PackageRootBinding binding = PackageRootBinding
            .CreateFromSource(acquired.Payload, "net10.0")
            .WithAssetDemand(
                PackageAssetDemand.SurfaceAndImplementation,
                PackageImplementationNames.Create(["Avalonia.Dialogs.dll"]));
        await using var workspace = new InspectionWorkspace();
        using PackageAssemblyContextRealization roles =
            workspace.RealizePackageAssemblyContextRoles(
                [binding.Root],
                cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(11, roles.SurfaceParticipants.Length);
        PackageAssemblyRoleParticipant implementation =
            Assert.Single(roles.ImplementationParticipants);
        Assert.Equal(
            AvaloniaImplementation + "Avalonia.Dialogs.dll",
            implementation.Asset.Path);

        // A later read that selects a covered entry makes no request for it:
        // net8.0 naming Avalonia.Dialogs.dll reads its own surface and the
        // rest of its block, never the bytes already held.
        string kept = "lib/net8.0/Avalonia.Dialogs.dll";
        Assert.Contains(kept, covered);
        (long keptStart, long keptEnd) = oracle.Extent([kept]);
        var later = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment laterEnvironment = RangedEnvironment.Create(later);
        PackageHouseSettlement net8 = await laterEnvironment.RealizeAsync(
            store,
            PackagePayloadAccess.Ranged,
            "net8.0",
            PackageRangedRead.DefaultSizeCut,
            Avalonia,
            AvaloniaVersion,
            ["Avalonia.Dialogs.dll"]);
        Assert.IsType<PackageHouseResult.Settled>(
            Assert.IsType<PackageHouseSettlement.Acquired>(net8).Result);
        PackageTransferReceipt laterReceipt = Transfer(net8, PackagePayloadOrigin.Ranged);
        Assert.DoesNotContain(
            laterReceipt.Requests,
            request => request.Purpose == PackageTransferRequestPurpose.EntrySpan
                && request.Range!.Value.Start < keptEnd
                && request.Range.Value.Start + request.Range.Value.Length > keptStart);
        Assert.True(
            Assert.IsType<RangedPackageContent>(
                Assert.IsType<PackageHouseSettlement.Acquired>(net8).Payload.Content)
                .IsMaterialized(kept));

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"materialized {content.MaterializedEntries.Count}; "
            + $"later net8.0 read: requests {laterReceipt.RequestCount}, received {laterReceipt.BytesReceived}; "
            + $"block {blockStart}-{blockEnd} ({block.Count} entries, {blockEnd - blockStart} bytes); "
            + $"span {blockSpan.Start}+{blockSpan.Length}; "
            + $"requests {receipt.RequestCount}, received {receipt.BytesReceived}; "
            + $"implementation folder stored {oracle.StoredBytes(oracle.Folder(AvaloniaImplementation))}, "
            + $"extent {oracle.Extent(oracle.Folder(AvaloniaImplementation))}");
    }

    /// <summary>
    /// Gate 7: after a named read, a realization naming a neighbour in the
    /// same block is answered by the entry cache with no request; one naming
    /// an assembly of another block reads only that block.
    /// </summary>
    [Fact]
    public async Task NamedImplementation_NeighbourInACachedBlock_MakesNoRequest()
    {
        byte[] archive = ReadAvalonia();
        ArchiveOracle oracle = ArchiveOracle.Read(archive);
        var store = new InMemoryPackageStore();
        await using (RangedEnvironment first = RangedEnvironment.Create(
            new RangeFeed(Avalonia, AvaloniaVersion, archive)))
        {
            Assert.IsType<PackageHouseResult.Settled>(
                Assert.IsType<PackageHouseSettlement.Acquired>(
                    await RealizeAvaloniaAsync(first, store, "Avalonia.Dialogs.dll")).Result);
        }

        var warm = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using (RangedEnvironment environment = RangedEnvironment.Create(warm))
        {
            PackageHouseSettlement neighbour =
                await RealizeAvaloniaAsync(environment, store, "Avalonia.Vulkan.dll");

            Assert.Equal(0, warm.RangedRequests + warm.FullRequests);
            PackageTransferReceipt receipt = Transfer(neighbour, PackagePayloadOrigin.Cache);
            Assert.Equal(PackageTransferPath.EntryCache, receipt.Path);
            var compile = Assert.IsType<PackageHouseRealizationReceipt.Compile>(
                Assert.IsType<PackageHouseSettlement.Acquired>(neighbour)
                    .Result.Evidence.Realization);
            Assert.Equal(
                [AvaloniaImplementation + "Avalonia.Vulkan.dll"],
                compile.Selection.ImplementationAssets.Select(asset => asset.Path));
        }

        // Another block: the cached directory spares the probe, the tail
        // confirms the archive, and the one block is one request.
        IReadOnlyList<string> baseBlock = oracle.BlockOf(
            AvaloniaImplementation + "Avalonia.Base.dll",
            PackageRangedRead.DefaultSizeCut);
        (long baseStart, _) = oracle.Extent(baseBlock);
        var later = new RangeFeed(Avalonia, AvaloniaVersion, archive);
        await using RangedEnvironment again = RangedEnvironment.Create(later);
        PackageTransferReceipt other = Transfer(
            await RealizeAvaloniaAsync(again, store, "Avalonia.Base.dll"),
            PackagePayloadOrigin.Ranged);
        Assert.Equal(
            [PackageTransferRequestPurpose.DirectoryTail, PackageTransferRequestPurpose.EntrySpan],
            other.Requests.Select(request => request.Purpose));
        Assert.Equal(baseStart, other.Requests[1].Range!.Value.Start);
    }

    /// <summary>
    /// Gate 9: a name that selects no implementation asset is a visible,
    /// typed realization failure, for the House and for the Root.
    /// </summary>
    [Fact]
    public async Task NamedImplementation_NameSelectingNothing_FailsVisibly()
    {
        var server = new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage());
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net45",
                sizeCut: 0,
                PclStorage,
                PclStorageVersion,
                ["PCLStorage.dll", "Missing.dll"]));

        var noMatch = Assert.IsType<PackageHouseResult.NoMatch>(acquired.Result);
        Assert.Equal(
            ["Missing.dll"],
            noMatch.Evidence.Realization!.UnmatchedImplementationNames);
        Assert.Contains("Missing.dll", noMatch.Reason.ToString(), StringComparison.Ordinal);
        Assert.Empty(noMatch.Evidence.Realization.LibraryHandoffs);

        PackageRootBinding binding =
            PackageRootBinding.CreateFromSource(acquired.Payload, "net45");
        PackageImplementationNameException failure =
            Assert.Throws<PackageImplementationNameException>(() =>
                binding.WithAssetDemand(
                    PackageAssetDemand.SurfaceAndImplementation,
                    PackageImplementationNames.Create(["Missing.dll"])));
        Assert.Equal(["Missing.dll"], failure.UnmatchedNames);
    }

    /// <summary>
    /// A package with no <c>ref/</c> folder reads its compile surface from the
    /// implementation folder itself, whole. Naming an implementation assembly
    /// there adds no block: in an archive whose target-framework folders are
    /// interleaved, the block would otherwise fetch only the other folder's
    /// entries. The named read costs exactly what the unnamed read costs. The
    /// archive is a boundary fixture: every lib-only real asset in the suite
    /// keeps its folders contiguous.
    /// </summary>
    [Fact]
    public async Task NamedImplementation_FolderAlreadyReadAsSurface_AddsNoBlock()
    {
        const string Id = "Interleaved.Fixture";
        const string Version = "1.0.0";
        byte[] archive = InterleavedLibOnlyArchive(Id, Version);
        // Above the cut, so the read is ranged, and a block budget wide enough
        // to span the whole net10.0 folder with its interleaved neighbours.
        long sizeCut = archive.Length / 2;

        async Task<(PackageHouseSettlement.Acquired Acquired, RangeFeed Server)> RealizeAsync(
            IEnumerable<string>? names)
        {
            var server = new RangeFeed(Id, Version, archive);
            await using RangedEnvironment environment = RangedEnvironment.Create(server);
            var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
                await environment.RealizeAsync(
                    new InMemoryPackageStore(),
                    PackagePayloadAccess.Ranged,
                    "net10.0",
                    sizeCut,
                    Id,
                    Version,
                    names));
            return (acquired, server);
        }

        var (named, namedServer) = await RealizeAsync(["A.dll"]);
        var (surface, surfaceServer) = await RealizeAsync(null);

        Assert.IsType<PackageHouseResult.Settled>(named.Result);
        IEnumerable<string> namedEntries =
            Assert.IsType<RangedPackageContent>(named.Payload.Content).MaterializedEntries;
        Assert.DoesNotContain(namedEntries, entry => entry.StartsWith("lib/net8.0/", StringComparison.Ordinal));
        Assert.Equal(
            Assert.IsType<RangedPackageContent>(surface.Payload.Content)
                .MaterializedEntries.Order(StringComparer.Ordinal),
            namedEntries.Order(StringComparer.Ordinal));
        Assert.Equal(surfaceServer.RangedRequests, namedServer.RangedRequests);
    }

    /// <summary>
    /// A lib-only package whose net8.0 and net10.0 entries alternate in archive
    /// order, as Avalonia's do: 4 incompressible assemblies per folder.
    /// </summary>
    private static byte[] InterleavedLibOnlyArchive(string id, string version)
    {
        using var output = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(
            output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, byte[] content)
            {
                using Stream entry = zip.CreateEntry(
                    path, System.IO.Compression.CompressionLevel.NoCompression).Open();
                entry.Write(content);
            }

            Add($"{id}.nuspec", System.Text.Encoding.UTF8.GetBytes(
                $"<?xml version=\"1.0\"?><package><metadata><id>{id}</id><version>{version}</version>"
                + "<authors>test</authors><description>test</description></metadata></package>"));
            var random = new Random(8478);
            foreach (string name in new[] { "A", "B", "C", "D" })
            {
                foreach (string folder in new[] { "lib/net10.0", "lib/net8.0" })
                {
                    byte[] content = new byte[10_000];
                    random.NextBytes(content);
                    Add($"{folder}/{name}.dll", content);
                }
            }
        }
        return output.ToArray();
    }

    /// <summary>
    /// A runtime realization with names filters its universe the same way:
    /// only the named asset's block is read and selected.
    /// </summary>
    [Fact]
    public async Task NamedImplementation_RuntimeSelection_ReadsOnlyTheNamedBlock()
    {
        var server = new RangeFeed(PclStorage, PclStorageVersion, ReadPclStorage());
        await using RangedEnvironment environment = RangedEnvironment.Create(server);

        var acquired = Assert.IsType<PackageHouseSettlement.Acquired>(
            await environment.RealizeAsync(
                new InMemoryPackageStore(),
                PackagePayloadAccess.Ranged,
                "net45",
                sizeCut: 0,
                PclStorage,
                PclStorageVersion,
                ["PCLStorage.dll"],
                PackageHouseAssetSelectionKind.Runtime));

        Assert.IsType<PackageHouseResult.Settled>(acquired.Result);
        // A zero budget makes every entry its own block.
        Assert.Equal(
            ["lib/net45/PCLStorage.dll"],
            Assert.IsType<RangedPackageContent>(acquired.Payload.Content).MaterializedEntries);
        var runtime = Assert.IsType<PackageHouseRealizationReceipt.Runtime>(
            acquired.Result.Evidence.Realization);
        var selected = Assert.IsType<PackageAssetSelection.Selected>(runtime.Selection);
        Assert.Equal(
            ["lib/net45/PCLStorage.dll"],
            selected.Universe.Assets.Select(asset => asset.EntryPath));
    }

    [Fact]
    public void NamedImplementation_RequestRequiresRealizeAndSurfaceAndImplementation()
    {
        PackageHouseDemand demand = new PackageHouseDemand.Exact(
            PackageSourceCoordinate.Create(PclStorage, PclStorageVersion));
        PackageHouseOperation realize =
            PackageHouseOperation.Create(PackageHouseOperationProfile.Realize);

        Assert.Throws<ArgumentException>(() => new PackageHouseRequest(
            demand,
            realize,
            PackageHouseTargetContext.Exact("net45"),
            PackageHouseAssetSelectionKind.Compile,
            assetDemand: PackageAssetDemand.Surface,
            implementationNames: ["PCLStorage.dll"]));
        Assert.Throws<ArgumentException>(() => new PackageHouseRequest(
            demand,
            PackageHouseOperation.Create(PackageHouseOperationProfile.Acquire),
            implementationNames: ["PCLStorage.dll"]));
        Assert.Throws<ArgumentException>(() => new PackageHouseRequest(
            demand,
            realize,
            PackageHouseTargetContext.Exact("net45"),
            PackageHouseAssetSelectionKind.Compile,
            implementationNames: ["lib/net45/PCLStorage.dll"]));
        Assert.Throws<ArgumentException>(() => new PackageHouseRequest(
            demand,
            realize,
            PackageHouseTargetContext.Exact("net45"),
            PackageHouseAssetSelectionKind.Compile,
            implementationNames: []));

        var named = new PackageHouseRequest(
            demand,
            realize,
            PackageHouseTargetContext.Exact("net45"),
            PackageHouseAssetSelectionKind.Compile,
            implementationNames: ["PCLStorage.dll", "pclstorage.DLL"]);
        Assert.Equal(["PCLStorage.dll"], named.ImplementationNames!.Names);
        Assert.True(named.ImplementationNames.MatchesPath("lib/net45/pclstorage.dll"));
        Assert.Null(new PackageHouseRequest(
            demand,
            realize,
            PackageHouseTargetContext.Exact("net45"),
            PackageHouseAssetSelectionKind.Compile).ImplementationNames);
    }

    private static Task<PackageHouseSettlement> RealizeAvaloniaAsync(
        RangedEnvironment environment,
        IPackageStore store,
        string name) =>
        environment.RealizeAsync(
            store,
            PackagePayloadAccess.Ranged,
            "net10.0",
            PackageRangedRead.DefaultSizeCut,
            Avalonia,
            AvaloniaVersion,
            [name]);

    private static byte[] ReadAvalonia() =>
        File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageReadDemand",
            "avalonia.12.1.2.nupkg"));

    /// <summary>
    /// An independent reading of the archive's central directory, and the
    /// design's block rule applied to it, as the oracle for the product's
    /// planner and reads.
    /// </summary>
    private sealed class ArchiveOracle
    {
        private readonly List<(string Name, long Offset, long Declared, long Stored)> _entries;
        private readonly long _directoryOffset;

        private ArchiveOracle(
            List<(string Name, long Offset, long Declared, long Stored)> entries,
            long directoryOffset)
        {
            _entries = entries;
            _directoryOffset = directoryOffset;
        }

        public static ArchiveOracle Read(byte[] zip)
        {
            int end = zip.Length - 22;
            while (BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(end)) != 0x06054b50)
                end--;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(end + 10));
            long directory = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(end + 16));
            var entries = new List<(string, long, long, long)>();
            int at = (int)directory;
            for (int i = 0; i < count; i++)
            {
                Assert.Equal(0x02014b50u, BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(at)));
                long compressed = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(at + 20));
                int nameLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(at + 28));
                int extraLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(at + 30));
                int commentLength = BinaryPrimitives.ReadUInt16LittleEndian(zip.AsSpan(at + 32));
                long offset = BinaryPrimitives.ReadUInt32LittleEndian(zip.AsSpan(at + 42));
                string name = Encoding.UTF8.GetString(zip, at + 46, nameLength);
                entries.Add((name, offset, offset + 30 + nameLength + extraLength + compressed, compressed));
                at += 46 + nameLength + extraLength + commentLength;
            }
            entries.Sort((left, right) => left.Item2.CompareTo(right.Item2));
            return new ArchiveOracle(entries, directory);
        }

        /// <summary>The folder's direct file entries, in archive order.</summary>
        public IReadOnlyList<string> Folder(string folder) =>
        [
            .. _entries
                .Where(entry =>
                    entry.Name.StartsWith(folder, StringComparison.Ordinal)
                    && entry.Name.Length > folder.Length
                    && entry.Name.IndexOf('/', folder.Length) < 0)
                .Select(entry => entry.Name),
        ];

        /// <summary>
        /// The design's rule: walk the folder in archive order, each entry
        /// measured to the folder's next local header (the last to the next
        /// local header in the archive), closing a block before the entry
        /// that would take it past the budget.
        /// </summary>
        public IReadOnlyList<string> BlockOf(string anchor, long budget)
        {
            string folder = anchor[..(anchor.LastIndexOf('/') + 1)];
            IReadOnlyList<string> members = Folder(folder);
            var blocks = new List<List<string>> { new() };
            long size = 0;
            for (int i = 0; i < members.Count; i++)
            {
                long start = Offset(members[i]);
                long length = (i + 1 < members.Count
                    ? Offset(members[i + 1])
                    : NextHeader(start)) - start;
                if (blocks[^1].Count > 0 && size + length > budget)
                {
                    blocks.Add([]);
                    size = 0;
                }
                blocks[^1].Add(members[i]);
                size += length;
            }
            return blocks.Single(block => block.Contains(anchor));
        }

        /// <summary>
        /// File entries whose local header lies from <paramref name="first"/>'s
        /// through <paramref name="last"/>'s, other than <paramref name="except"/>.
        /// </summary>
        public IReadOnlyList<string> Between(string first, string last, IReadOnlyList<string> except) =>
        [
            .. _entries
                .Where(entry =>
                    entry.Offset >= Offset(first)
                    && entry.Offset <= Offset(last)
                    && !entry.Name.EndsWith('/')
                    && !except.Contains(entry.Name))
                .Select(entry => entry.Name),
        ];

        /// <summary>From the first entry's local header to the header after the last.</summary>
        public (long Start, long End) Extent(IReadOnlyList<string> names) =>
            (names.Min(Offset), names.Max(name => NextHeader(Offset(name))));

        public long DeclaredEnd(string name) =>
            _entries.Single(entry => entry.Name == name).Declared;

        public long StoredBytes(IEnumerable<string> names) =>
            names.Sum(name => _entries.Single(entry => entry.Name == name).Stored);

        private long Offset(string name) =>
            _entries.Single(entry => entry.Name == name).Offset;

        private long NextHeader(long offset) =>
            _entries.Where(entry => entry.Offset > offset)
                .Select(entry => entry.Offset)
                .DefaultIfEmpty(_directoryOffset)
                .Min();
    }
}
