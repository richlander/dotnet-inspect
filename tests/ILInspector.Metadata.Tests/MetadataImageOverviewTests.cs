using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for <see cref="MetadataImageInspector.Describe"/> and
/// <see cref="MetadataTableProjector.ReadHeapValue"/> — the image-level facts and
/// heap random access a metadata browser needs alongside the table projection
/// (issue #3341, gap 5).
///
/// Two claims carry most of the weight here. First, the overview reports what is
/// physically present rather than what the projection covers, so an unmodelled
/// table with rows is visible as a gap instead of reading as empty. Second, a
/// heap address round-trips: the address a projected cell publishes is the
/// address this reader accepts, which is only true if the GUID heap's 1-based
/// index addressing is respected rather than treated as a byte offset.
/// </summary>
public class MetadataImageOverviewTests
{
    static string SelfPath => typeof(MetadataImageOverviewTests).Assembly.Location;

    static PEReader OpenSelfFromBytes() => new(new MemoryStream(File.ReadAllBytes(SelfPath)));

    static MetadataImageOverview DescribeSelf(PEReader peReader)
    {
        var overview = MetadataImageInspector.Describe(peReader);
        Assert.NotNull(overview);
        return overview;
    }

    static MetadataTableProjection FullProjection(PEReader peReader)
        => MetadataTableProjector.Project(
            peReader,
            new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue });

    /// <summary>
    /// A copy of this assembly with the CLI (COR20) data directory zeroed, which
    /// is how a PE stops being a managed image. Used to exercise the "no
    /// metadata" contract on a real image rather than asserting it in theory.
    /// </summary>
    static byte[] SelfWithoutCliHeader()
    {
        byte[] bytes = File.ReadAllBytes(SelfPath);

        using var peReader = new PEReader(new MemoryStream(bytes));
        var peHeader = peReader.PEHeaders.PEHeader!;

        // The data directories follow the optional header's fixed part, whose
        // size differs between PE32 and PE32+. COR20 is directory 14.
        int directoryBase = peReader.PEHeaders.PEHeaderStartOffset + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        Array.Clear(bytes, directoryBase + (14 * 8), 8);

        return bytes;
    }

    /// <summary>
    /// A copy of this assembly whose <c>#~</c> stream is declared too small to
    /// hold its own table header. The CLI header is left intact, so this is a
    /// corrupt table stream rather than an absent one.
    /// </summary>
    static byte[] SelfWithCorruptTableStream()
    {
        byte[] bytes = File.ReadAllBytes(SelfPath);

        int metadataStart;
        using (var peReader = new PEReader(new MemoryStream(bytes)))
        {
            metadataStart = peReader.PEHeaders.MetadataStartOffset;
        }

        // Metadata root: signature, version pair and reserved (12 bytes), then a
        // length-prefixed version string padded to 4, then flags and stream count.
        int versionLength = BitConverter.ToInt32(bytes, metadataStart + 12);
        int cursor = metadataStart + 16 + AlignTo4(versionLength);
        int streamCount = BitConverter.ToUInt16(bytes, cursor + 2);
        cursor += 4;

        for (int i = 0; i < streamCount; i++)
        {
            // Stream header: offset, size, then a null-terminated name padded to 4.
            int sizeOffset = cursor + 4;
            int nameStart = cursor + 8;
            int nameEnd = Array.IndexOf(bytes, (byte)0, nameStart);
            string name = Encoding.ASCII.GetString(bytes, nameStart, nameEnd - nameStart);

            if (name == "#~")
            {
                BitConverter.GetBytes(4).CopyTo(bytes, sizeOffset);
                return bytes;
            }

            cursor = nameStart + AlignTo4(nameEnd - nameStart + 1);
        }

        throw new InvalidOperationException("This assembly has no #~ stream to corrupt.");
    }

    static int AlignTo4(int value) => (value + 3) & ~3;

    // --- Metadata root -----------------------------------------------------

    [Fact]
    public void Describe_ReportsTheMetadataRootIdentity()
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);

        Assert.StartsWith("v", overview.MetadataVersion);
        Assert.Equal(MetadataKind.Ecma335, overview.Kind);
        Assert.True(overview.IsAssembly);
    }

    [Fact]
    public void Describe_ReportsAMetadataExtentThatContainsEveryHeap()
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);

        Assert.True(overview.MetadataOffset > 0);

        // The heaps are streams inside the metadata root, so their total can
        // never exceed it. This catches an offset/size pair read from the wrong
        // header far more cheaply than restating the header layout.
        long heapBytes = overview.Heaps.Sum(static heap => (long)heap.SizeInBytes);
        Assert.True(
            heapBytes < overview.MetadataSize,
            $"heaps total {heapBytes} bytes but the metadata root is only {overview.MetadataSize}");
    }

    [Fact]
    public void Describe_MatchesTheReaderItDescribes()
    {
        using var peReader = OpenSelfFromBytes();
        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);

        var overview = DescribeSelf(peReader);

        Assert.Equal(reader.MetadataVersion, overview.MetadataVersion);
        Assert.Equal(peReader.PEHeaders.MetadataStartOffset, overview.MetadataOffset);
        Assert.Equal(peReader.PEHeaders.MetadataSize, overview.MetadataSize);
    }

    // --- Heaps -------------------------------------------------------------

    [Fact]
    public void Describe_ReportsEveryHeapExactlyOnce()
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);

        Assert.Equal(Enum.GetValues<HeapKind>(), overview.Heaps.Select(static heap => heap.Heap));
    }

    [Fact]
    public void Describe_ReportsHeapSizesInBytes()
    {
        using var peReader = OpenSelfFromBytes();
        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);

        var overview = DescribeSelf(peReader);

        Assert.Equal(reader.GetHeapSize(HeapIndex.String), Size(overview, HeapKind.String));
        Assert.Equal(reader.GetHeapSize(HeapIndex.Blob), Size(overview, HeapKind.Blob));
        Assert.Equal(reader.GetHeapSize(HeapIndex.Guid), Size(overview, HeapKind.Guid));
        Assert.Equal(reader.GetHeapSize(HeapIndex.UserString), Size(overview, HeapKind.UserString));

        // A real assembly must exercise the heaps the round-trip tests below
        // walk, or those tests could pass over nothing.
        Assert.True(Size(overview, HeapKind.String) > 0);
        Assert.True(Size(overview, HeapKind.Blob) > 0);
        Assert.True(Size(overview, HeapKind.Guid) > 0);

        static int Size(MetadataImageOverview overview, HeapKind heap)
            => overview.Heaps.Single(entry => entry.Heap == heap).SizeInBytes;
    }

    [Fact]
    public void Describe_MarksOnlyTheGuidHeapAsIndexAddressed()
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);

        foreach (var heap in overview.Heaps)
        {
            var expected = heap.Heap is HeapKind.Guid
                ? MetadataHeapAddressing.Index
                : MetadataHeapAddressing.ByteOffset;

            Assert.Equal(expected, heap.Addressing);
        }
    }

    [Fact]
    public void GuidHeapSize_IsAWholeNumberOfEntries()
    {
        using var peReader = OpenSelfFromBytes();

        var guid = DescribeSelf(peReader).Heaps.Single(static heap => heap.Heap == HeapKind.Guid);

        Assert.Equal(0, guid.SizeInBytes % MetadataHeapAddressingSizes.GuidSize);
        Assert.Equal(guid.SizeInBytes / MetadataHeapAddressingSizes.GuidSize, guid.MaxAddress);
    }

    [Theory]
    [InlineData(HeapKind.String, 0, 0)]
    [InlineData(HeapKind.String, 64, 63)]
    [InlineData(HeapKind.Blob, 1, 0)]
    [InlineData(HeapKind.Guid, 0, 0)]
    [InlineData(HeapKind.Guid, 16, 1)]
    [InlineData(HeapKind.Guid, 48, 3)]
    public void MaxAddress_FollowsTheHeapAddressing(HeapKind heap, int sizeInBytes, int expected)
    {
        var addressing = heap is HeapKind.Guid
            ? MetadataHeapAddressing.Index
            : MetadataHeapAddressing.ByteOffset;

        Assert.Equal(expected, new MetadataHeapSummary(heap, sizeInBytes, addressing).MaxAddress);
    }

    // --- Tables ------------------------------------------------------------

    [Fact]
    public void Describe_ReportsEveryTableInTableOrder()
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);

        Assert.Equal(Enum.GetValues<TableIndex>(), overview.Tables.Select(static table => table.Index));
    }

    [Fact]
    public void Describe_ReportsPhysicalRowCountsMatchingTheProjection()
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);
        var projection = FullProjection(peReader);

        Assert.NotEmpty(projection.Tables);

        foreach (var view in projection.Tables)
        {
            var summary = overview.Tables.Single(table => table.Index == view.Index);

            Assert.Equal(view.RowCount, summary.RowCount);
            Assert.Equal(view.Name, summary.Name);
            Assert.True(summary.IsProjected);
        }
    }

    [Fact]
    public void IsProjected_AgreesWithTheProjectorsOwnTableSet()
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);

        foreach (var table in overview.Tables)
        {
            Assert.Equal(MetadataTableProjector.ProjectedTables.Contains(table.Index), table.IsProjected);
        }
    }

    /// <summary>
    /// The reason the overview lists every table rather than only the projected
    /// ones: real assemblies carry content the projection does not yet model, and
    /// a browser must be able to tell that gap apart from an empty table.
    /// </summary>
    [Fact]
    public void UnprojectedTablesWithRows_AreVisibleAsAGap()
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);

        var gaps = overview.Tables
            .Where(static table => table.RowCount > 0 && !table.IsProjected)
            .Select(static table => table.Name)
            .ToImmutableArray();

        Assert.NotEmpty(gaps);
        Assert.Contains(nameof(TableIndex.NestedClass), gaps);
        Assert.Contains(nameof(TableIndex.MethodSemantics), gaps);
    }

    [Fact]
    public void ProjectedTables_IsTheProjectorsSupportedSet()
    {
        using var peReader = OpenSelfFromBytes();

        var projected = MetadataTableProjector.ProjectedTables;

        Assert.NotEmpty(projected);
        Assert.Equal(projected.Distinct().Count(), projected.Length);

        // Every table the projector actually emits must be in the advertised set.
        foreach (var view in FullProjection(peReader).Tables)
        {
            Assert.Contains(view.Index, projected);
        }
    }

    // --- Headers -----------------------------------------------------------

    [Fact]
    public void Describe_ReportsPeAndCliHeaderFacts()
    {
        using var peReader = OpenSelfFromBytes();
        var headers = peReader.PEHeaders;

        var actual = DescribeSelf(peReader).Headers;

        Assert.Equal(headers.CoffHeader.Machine, actual.Machine);
        Assert.Equal(headers.CoffHeader.Characteristics, actual.ImageCharacteristics);
        Assert.Equal(headers.PEHeader!.Subsystem, actual.Subsystem);
        Assert.Equal(headers.PEHeader.DllCharacteristics, actual.DllCharacteristics);
        Assert.Equal(headers.PEHeader.Magic == PEMagic.PE32Plus, actual.IsPE32Plus);

        Assert.NotNull(actual.Cor);
        Assert.Equal(headers.CorHeader!.Flags, actual.Cor.Flags);
        Assert.Equal(headers.CorHeader.MajorRuntimeVersion, actual.Cor.MajorRuntimeVersion);
        Assert.Equal(
            headers.CorHeader.EntryPointTokenOrRelativeVirtualAddress,
            actual.Cor.EntryPointTokenOrRelativeVirtualAddress);
    }

    [Fact]
    public void Describe_ReportsAManagedImageAsIlOnly()
    {
        using var peReader = OpenSelfFromBytes();

        var cor = DescribeSelf(peReader).Headers.Cor;

        Assert.NotNull(cor);
        Assert.True(cor.Flags.HasFlag(CorFlags.ILOnly));
    }

    [Theory]
    [InlineData(0, CorFlags.ILOnly, null)]
    [InlineData(0x06000001, CorFlags.ILOnly, 0x06000001)]
    [InlineData(0x00001234, CorFlags.NativeEntryPoint, null)]
    public void EntryPointToken_IsNullUnlessTheHeaderCarriesAManagedOne(
        int raw,
        CorFlags flags,
        int? expected)
    {
        var cor = new MetadataCorHeaderSummary(2, 5, flags, raw);

        Assert.Equal(expected, cor.EntryPointToken);
    }

    // --- No metadata -------------------------------------------------------

    [Fact]
    public void Describe_ReturnsNullForAnImageWithoutMetadata()
    {
        using var peReader = new PEReader(new MemoryStream(SelfWithoutCliHeader()));

        // Canary: if zeroing the CLI directory ever stops producing an image
        // without metadata, the assertion below would pass for the wrong reason.
        Assert.False(peReader.HasMetadata);

        Assert.Null(MetadataImageInspector.Describe(peReader));
    }

    [Fact]
    public void ReadHeapValue_ReturnsNullForAnImageWithoutMetadata()
    {
        using var peReader = new PEReader(new MemoryStream(SelfWithoutCliHeader()));

        Assert.False(peReader.HasMetadata);

        Assert.Null(MetadataTableProjector.ReadHeapValue(peReader, HeapKind.String, 1));
    }

    /// <summary>
    /// Corrupt metadata must fail visibly. The failure mode this guards against
    /// is an overview that reports every table as zero rows, which is
    /// indistinguishable from a legitimately empty image and so hides the
    /// corruption completely. "No metadata" is null; "unreadable metadata" throws.
    /// </summary>
    [Fact]
    public void Describe_FailsVisiblyForACorruptTableStream()
    {
        using (var intact = OpenSelfFromBytes())
        {
            // Canary: the intact image has rows that a zero-row overview would lose.
            var typeDef = DescribeSelf(intact).Tables.Single(table => table.Index == TableIndex.TypeDef);
            Assert.True(typeDef.RowCount > 0, "The intact image should report TypeDef rows.");
        }

        using var peReader = new PEReader(new MemoryStream(SelfWithCorruptTableStream()));

        // Canary: the CLI header is still intact, so this is corruption rather
        // than the absent-metadata case covered above.
        Assert.True(peReader.HasMetadata);

        Assert.Throws<BadImageFormatException>(() => MetadataImageInspector.Describe(peReader));
    }

    [Fact]
    public void Describe_RejectsANullReader()
        => Assert.Throws<ArgumentNullException>(() => MetadataImageInspector.Describe(null!));

    // --- Heap random access ------------------------------------------------

    /// <summary>
    /// The load-bearing round trip: every heap address the projection publishes
    /// must read back through <see cref="MetadataTableProjector.ReadHeapValue"/>
    /// as the same value. This is what makes a cell's offset a usable address
    /// rather than a display detail.
    ///
    /// Note what this cannot prove on its own: both sides construct their handle
    /// from the same address, so a round trip stays green even if that shared
    /// convention were wrong. <see cref="ReadHeapValue_ReadsTheGuidHeapByIndexNotByteOffset"/>
    /// anchors the GUID convention to an externally-known value for that reason.
    /// </summary>
    [Fact]
    public void ReadHeapValue_RoundTripsEveryProjectedHeapAddress()
    {
        using var peReader = OpenSelfFromBytes();
        var options = new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue };

        int checkedCells = 0;
        var seenHeaps = new HashSet<HeapKind>();

        foreach (var view in MetadataTableProjector.Project(peReader, options).Tables)
        {
            foreach (var row in view.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell is not MetadataValue.HeapReference expected)
                        continue;

                    var actual = MetadataTableProjector.ReadHeapValue(
                        peReader, expected.Heap, expected.Offset, options);

                    var reread = Assert.IsType<MetadataValue.HeapReference>(actual);

                    Assert.Equal(expected.Heap, reread.Heap);
                    Assert.Equal(expected.Offset, reread.Offset);
                    Assert.Equal(expected.Length, reread.Length);
                    Assert.Equal(expected.Text, reread.Text);
                    Assert.Equal(expected.Preview, reread.Preview);
                    Assert.Equal(expected.Truncated, reread.Truncated);

                    seenHeaps.Add(expected.Heap);
                    checkedCells++;
                }
            }
        }

        // Floor and coverage guards: without them a projection that stopped
        // emitting heap cells, or one that only ever emitted strings, would let
        // this walk pass without exercising the addressing rule it exists to pin.
        Assert.True(checkedCells > 1_000, $"only {checkedCells} heap cells were round-tripped");
        Assert.Contains(HeapKind.String, seenHeaps);
        Assert.Contains(HeapKind.Blob, seenHeaps);
        Assert.Contains(HeapKind.Guid, seenHeaps);
    }

    [Fact]
    public void ReadHeapValue_ReadsTheUserStringHeapTheTablesNeverPointAt()
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);
        int size = overview.Heaps.Single(static heap => heap.Heap == HeapKind.UserString).SizeInBytes;

        // The #US heap opens with a single nil byte, so the first literal starts
        // at offset 1. Guard the premise rather than assume it.
        Assert.True(size > 1, "this assembly carries no user strings to read");

        var value = MetadataTableProjector.ReadHeapValue(peReader, HeapKind.UserString, 1);

        var heapValue = Assert.IsType<MetadataValue.HeapReference>(value);
        Assert.Equal(HeapKind.UserString, heapValue.Heap);
        Assert.Equal(1, heapValue.Offset);
        Assert.NotNull(heapValue.Text);
        Assert.NotEmpty(heapValue.Text);
    }

    [Theory]
    [InlineData(HeapKind.String)]
    [InlineData(HeapKind.Blob)]
    [InlineData(HeapKind.Guid)]
    [InlineData(HeapKind.UserString)]
    public void ReadHeapValue_TreatsAddressZeroAsNil(HeapKind heap)
    {
        using var peReader = OpenSelfFromBytes();

        Assert.IsType<MetadataValue.Nil>(MetadataTableProjector.ReadHeapValue(peReader, heap, 0));
    }

    [Theory]
    [InlineData(HeapKind.String)]
    [InlineData(HeapKind.Blob)]
    [InlineData(HeapKind.Guid)]
    [InlineData(HeapKind.UserString)]
    public void ReadHeapValue_ReportsAnAddressPastTheEndAsMalformed(HeapKind heap)
    {
        using var peReader = OpenSelfFromBytes();

        var overview = DescribeSelf(peReader);
        int beyond = overview.Heaps.Single(entry => entry.Heap == heap).MaxAddress + 1;

        var value = MetadataTableProjector.ReadHeapValue(peReader, heap, beyond);

        Assert.IsType<MetadataValue.Malformed>(value);
    }

    /// <summary>
    /// Anchors the GUID heap's addressing to a value known independently of the
    /// projection: the module's MVID, read through SRM's typed API. A reader that
    /// treated the address as a byte offset would resolve a different (or
    /// unreadable) entry here, which no round trip against the projection could
    /// detect, since both sides of that round trip share one convention.
    /// </summary>
    [Fact]
    public void ReadHeapValue_ReadsTheGuidHeapByIndexNotByteOffset()
    {
        using var peReader = OpenSelfFromBytes();
        var reader = peReader.GetMetadataReader(MetadataReaderOptions.None);

        var mvid = reader.GetModuleDefinition().Mvid;
        int address = MetadataTokens.GetHeapOffset(mvid);

        // The premise of the byte-offset confusion: the MVID sits at address 1,
        // which is entry one, not byte one.
        Assert.Equal(1, address);

        var value = MetadataTableProjector.ReadHeapValue(peReader, HeapKind.Guid, address);

        var heapValue = Assert.IsType<MetadataValue.HeapReference>(value);
        Assert.Equal(reader.GetGuid(mvid).ToString(), heapValue.Text);
        Assert.NotEqual(Guid.Empty.ToString(), heapValue.Text);
    }

    /// <summary>
    /// The GUID heap is the one place where an address can look valid under the
    /// wrong convention: on a single-entry heap, index 2 is past the end while
    /// byte offset 2 is well inside. Both the overview's declared
    /// <see cref="MetadataHeapSummary.MaxAddress"/> and the read itself must
    /// treat those addresses as out of range.
    /// </summary>
    [Fact]
    public void ReadHeapValue_RejectsAGuidIndexPastTheEndOfAByteAddressableRange()
    {
        using var peReader = OpenSelfFromBytes();

        var guid = DescribeSelf(peReader).Heaps.Single(static heap => heap.Heap == HeapKind.Guid);
        Assert.Equal(MetadataHeapAddressingSizes.GuidSize, guid.SizeInBytes);
        Assert.Equal(1, guid.MaxAddress);

        Assert.IsType<MetadataValue.HeapReference>(
            MetadataTableProjector.ReadHeapValue(peReader, HeapKind.Guid, 1));

        // Byte-offset thinking would accept 2 through 15 here.
        for (int address = 2; address < MetadataHeapAddressingSizes.GuidSize; address++)
        {
            Assert.IsType<MetadataValue.Malformed>(
                MetadataTableProjector.ReadHeapValue(peReader, HeapKind.Guid, address));
        }
    }

    [Fact]
    public void ReadHeapValue_HonoursTheBlobPreviewBudget()
    {
        using var peReader = OpenSelfFromBytes();

        var options = new MetadataProjectionOptions { MaxRowsPerTable = int.MaxValue };
        var large = MetadataTableProjector.Project(peReader, options).Tables
            .SelectMany(static view => view.Rows)
            .SelectMany(static row => row.Cells)
            .OfType<MetadataValue.HeapReference>()
            .Where(static cell => cell.Heap == HeapKind.Blob && cell.Length > 8)
            .First();

        var bounded = Assert.IsType<MetadataValue.HeapReference>(
            MetadataTableProjector.ReadHeapValue(
                peReader, HeapKind.Blob, large.Offset, new MetadataProjectionOptions { MaxPreviewBytes = 4 }));

        Assert.True(bounded.Truncated);
        Assert.Equal(8, bounded.Preview.Length);
        Assert.Equal(large.Length, bounded.Length);

        var whole = Assert.IsType<MetadataValue.HeapReference>(
            MetadataTableProjector.ReadHeapValue(
                peReader, HeapKind.Blob, large.Offset, new MetadataProjectionOptions { MaxPreviewBytes = int.MaxValue }));

        Assert.False(whole.Truncated);
        Assert.Equal(large.Length * 2, whole.Preview.Length);
    }

    [Fact]
    public void ReadHeapValue_RejectsANegativeAddress()
    {
        using var peReader = OpenSelfFromBytes();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MetadataTableProjector.ReadHeapValue(peReader, HeapKind.String, -1));
    }

    [Fact]
    public void ReadHeapValue_RejectsANullReader()
        => Assert.Throws<ArgumentNullException>(
            () => MetadataTableProjector.ReadHeapValue(null!, HeapKind.String, 1));

    // --- Session facets ----------------------------------------------------

    [Fact]
    public void SessionExposesTheImageOverview()
    {
        using var peReader = OpenSelfFromBytes();
        using var session = AssemblyInspectionSession.Open(SelfPath);

        var direct = DescribeSelf(peReader);
        var viaSession = session.MetadataImage();

        Assert.NotNull(viaSession);
        Assert.Equal(direct.MetadataVersion, viaSession.MetadataVersion);
        Assert.Equal(direct.Tables.Length, viaSession.Tables.Length);
        Assert.Equal(direct.Heaps, viaSession.Heaps);
    }

    [Fact]
    public void SessionExposesHeapRandomAccess()
    {
        using var peReader = OpenSelfFromBytes();
        using var session = AssemblyInspectionSession.Open(SelfPath);

        var direct = MetadataTableProjector.ReadHeapValue(peReader, HeapKind.Guid, 1);
        var viaSession = session.MetadataHeapValue(HeapKind.Guid, 1);

        Assert.Equal(direct, viaSession);
        Assert.IsType<MetadataValue.HeapReference>(viaSession);
    }
}
