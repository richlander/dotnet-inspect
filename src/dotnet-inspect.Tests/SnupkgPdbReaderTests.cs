using System.Collections.Immutable;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

public class SnupkgPdbReaderTests
{
    [Fact]
    public void ExtractPortablePdb_MatchingGuid_ReturnsPdbBytes()
    {
        var guid = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) = BuildPortablePdb(guid);
        var snupkg = MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Foo", guid);

        Assert.NotNull(result.PdbBytes);
        Assert.False(result.WindowsPdbDetected);
        Assert.False(result.InvalidOrMismatchedPdbDetected);
        Assert.Equal(pdbBytes, result.PdbBytes);
    }

    [Fact]
    public void ExtractPortablePdb_MismatchedGuid_ReturnsNull()
    {
        var guid = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) = BuildPortablePdb(guid);
        var snupkg = MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Foo", Guid.NewGuid());

        Assert.Null(result.PdbBytes);
        Assert.False(result.WindowsPdbDetected);
        Assert.True(result.InvalidOrMismatchedPdbDetected);
    }

    [Fact]
    public void ExtractPortablePdb_MatchingGuidWithMismatchedStamp_ReturnsNull()
    {
        var guid =
            Guid.Parse(
                "11112222-3333-4444-5555-666677778888");
        var (pdbBytes, _) =
            BuildPortablePdb(
                guid,
                stamp: 0x01020304);
        var snupkg =
            MakeSnupkg(
                ("lib/net8.0/Foo.pdb", pdbBytes));

        using var stream = new MemoryStream(snupkg);
        var result =
            SnupkgPdbReader.ExtractPortablePdb(
                stream,
                "Foo",
                guid,
                expectedStamp: 0x05060708);

        Assert.Null(result.PdbBytes);
        Assert.False(result.WindowsPdbDetected);
        Assert.True(result.InvalidOrMismatchedPdbDetected);
    }

    [Fact]
    public void ExtractPortablePdb_WindowsPdb_FlagsWindowsDetectedAndReturnsNull()
    {
        var windowsPdb = new byte[] { (byte)'M', (byte)'i', (byte)'c', (byte)'r', 0, 0, 0, 0 };
        var snupkg = MakeSnupkg(("lib/net8.0/Bar.pdb", windowsPdb));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Bar", Guid.NewGuid());

        Assert.Null(result.PdbBytes);
        Assert.True(result.WindowsPdbDetected);
        Assert.False(result.InvalidOrMismatchedPdbDetected);
    }

    [Fact]
    public void ExtractPortablePdb_NoEntryForAssembly_ReturnsNull()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        var snupkg = MakeSnupkg(("lib/net8.0/Other.pdb", pdbBytes));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Foo", guid);

        Assert.Null(result.PdbBytes);
        Assert.False(result.InvalidOrMismatchedPdbDetected);
    }

    [Fact]
    public void ExtractPortablePdb_MatchesNestedEntryByFileName()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        // Entry in an arbitrary nested directory; match is by file name.
        var snupkg = MakeSnupkg(("lib/netstandard2.0/subdir/Foo.pdb", pdbBytes));

        using var stream = new MemoryStream(snupkg);
        var result = SnupkgPdbReader.ExtractPortablePdb(stream, "Foo", guid);

        Assert.Equal(pdbBytes, result.PdbBytes);
    }

    [Fact]
    public void ExtractPortablePdb_EntryLimitRejectsArchiveBeforeExpansion()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        byte[] snupkg =
            MakeSnupkg(
                ("lib/net8.0/Foo.pdb", pdbBytes),
                ("package.nuspec", "<package />"u8.ToArray()));
        var limits = new SymbolAcquisitionLimits(
            maxSymbolPackageBytes: snupkg.Length,
            maxPortablePdbBytes: pdbBytes.Length,
            maxSymbolPackageEntries: 1);

        using var stream = new MemoryStream(snupkg);
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => SnupkgPdbReader.ExtractPortablePdb(
                stream,
                "Foo",
                guid,
                limits: limits));

        Assert.Contains("archive-entry limit", error.Message);
    }

    [Fact]
    public void ExtractPortablePdb_ExpandedPdbLimitRejectsEntryBeforeCopy()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        byte[] snupkg =
            MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes));
        var limits = new SymbolAcquisitionLimits(
            maxSymbolPackageBytes: snupkg.Length,
            maxPortablePdbBytes: pdbBytes.Length - 1,
            maxSymbolPackageEntries: 8);

        using var stream = new MemoryStream(snupkg);
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => SnupkgPdbReader.ExtractPortablePdb(
                stream,
                "Foo",
                guid,
                limits: limits));

        Assert.Contains("PDB exceeds", error.Message);
    }

    [Fact]
    public void ExtractPortablePdb_WithoutHostLimitsRejectsDeclaredExpansionAboveTransportCeiling()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        byte[] snupkg =
            MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes));
        int centralDirectory = FindCentralDirectoryFileHeader(snupkg);
        BinaryPrimitives.WriteUInt32LittleEndian(
            snupkg.AsSpan(centralDirectory + 24),
            checked((uint)SymbolPackageDownloader.DefaultMaximumSymbolBytes + 1));

        using var stream = new MemoryStream(snupkg);
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => SnupkgPdbReader.ExtractPortablePdb(
                stream,
                "Foo",
                guid));

        Assert.Contains("PDB exceeds", error.Message);
    }

    [Fact]
    public void ExtractPortablePdb_DirectCallRejectsOversizedArchive()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        byte[] snupkg =
            MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes));
        var limits = new SymbolAcquisitionLimits(
            maxSymbolPackageBytes: snupkg.Length - 1,
            maxPortablePdbBytes: pdbBytes.Length,
            maxSymbolPackageEntries: 8);

        using var stream = new MemoryStream(snupkg);
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => SnupkgPdbReader.ExtractPortablePdb(
                stream,
                "Foo",
                guid,
                limits: limits));

        Assert.Contains("symbol package exceeds", error.Message);
    }

    [Theory]
    [InlineData(4, 2)]
    [InlineData(6, 2)]
    [InlineData(8, 2)]
    [InlineData(10, 2)]
    [InlineData(12, 4)]
    [InlineData(16, 4)]
    public void ExtractPortablePdb_EveryZip64SentinelIsRejected(
        int fieldOffset,
        int fieldSize)
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        byte[] snupkg =
            MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes));
        int endRecord = FindEndOfCentralDirectory(snupkg);
        if (fieldSize == 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(
                snupkg.AsSpan(endRecord + fieldOffset),
                ushort.MaxValue);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                snupkg.AsSpan(endRecord + fieldOffset),
                uint.MaxValue);
        }
        var limits = new SymbolAcquisitionLimits(
            maxSymbolPackageBytes: snupkg.Length,
            maxPortablePdbBytes: pdbBytes.Length,
            maxSymbolPackageEntries: 8);

        using var stream = new MemoryStream(snupkg);
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => SnupkgPdbReader.ExtractPortablePdb(
                stream,
                "Foo",
                guid,
                limits: limits));

        Assert.Contains("ZIP64", error.Message);
    }

    [Fact]
    public void ExtractPortablePdb_AggregateExpansionRejectsRepeatedCandidates()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(Guid.NewGuid());
        byte[] snupkg =
            MakeSnupkg(
                ("a/Foo.pdb", pdbBytes),
                ("b/Foo.pdb", pdbBytes));
        var limits = new SymbolAcquisitionLimits(
            maxSymbolPackageBytes: snupkg.Length,
            maxPortablePdbBytes: pdbBytes.Length,
            maxSymbolPackageEntries: 8,
            maxExpandedPdbBytes: (2L * pdbBytes.Length) - 1);

        using var stream = new MemoryStream(snupkg);
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => SnupkgPdbReader.ExtractPortablePdb(
                stream,
                "Foo",
                guid,
                limits: limits));

        Assert.Contains("aggregate byte limit", error.Message);
    }

    [Fact]
    public void ExtractPortablePdb_ObservesCancellation()
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        byte[] snupkg =
            MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using var stream = new MemoryStream(snupkg);
        Assert.Throws<OperationCanceledException>(
            () => SnupkgPdbReader.ExtractPortablePdbCancelable(
                stream,
                "Foo",
                guid,
                log: null,
                expectedStamp: null,
                limits: null,
                cancellationToken: cancellation.Token));
    }

    /// <summary>
    /// Runtime-independent gate for the lower bound on a declared PDB length.
    /// <c>ZipArchiveEntry.Length</c> is a signed value taken verbatim from the
    /// archive's ZIP64 extra field; a negative one clears every <c>&gt;</c>
    /// ceiling and then narrows, unchecked, to a large positive allocation.
    /// </summary>
    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(unchecked((long)0xFFFFFFFF00000000UL) | (512L * 1024 * 1024))]
    public void ValidateDeclaredPdbLength_RejectsNegativeDeclaredLength(
        long declaredLength)
    {
        InvalidDataException error = Assert.Throws<InvalidDataException>(
            () => SnupkgPdbReader.ValidateDeclaredPdbLength(
                declaredLength,
                maxPdbBytes: SymbolPackageDownloader.DefaultMaximumSymbolBytes));

        Assert.Contains("PDB exceeds", error.Message);
    }

    /// <summary>
    /// End-to-end canary for the same property: a 200-byte archive whose ZIP64
    /// extra field declares a negative uncompressed size must be rejected
    /// without allocating the value that length narrows to. This is only
    /// load-bearing on runtimes whose <c>ZipArchive</c> surfaces the negative
    /// length — .NET 10, which official builds target — because .NET 11 rejects
    /// the archive while reading the central directory. Either way the outcome
    /// asserted here is the one that matters: rejection, and no large
    /// allocation.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtractPortablePdb_RejectsNegativeZip64DeclaredLength(
        bool withHostLimits)
    {
        var guid = Guid.NewGuid();
        var (pdbBytes, _) = BuildPortablePdb(guid);
        byte[] snupkg = InjectZip64UncompressedSize(
            MakeSnupkg(("lib/net8.0/Foo.pdb", pdbBytes)),
            declared: unchecked((long)0xFFFFFFFF00000000UL) | (512L * 1024 * 1024));
        var limits = withHostLimits
            ? new SymbolAcquisitionLimits(
                maxSymbolPackageBytes: 24L * 1024 * 1024,
                maxPortablePdbBytes: 8L * 1024 * 1024,
                maxSymbolPackageEntries: 8,
                maxExpandedPdbBytes: 24L * 1024 * 1024)
            : null;

        using var stream = new MemoryStream(snupkg);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(
            () => SnupkgPdbReader.ExtractPortablePdb(
                stream,
                "Foo",
                guid,
                limits: limits));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < 16L * 1024 * 1024,
            $"Rejecting the entry allocated {allocated} bytes; the declared "
                + "length must never reach the allocation site.");
    }

    static int FindEndOfCentralDirectory(byte[] archive)
    {
        for (int offset = archive.Length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    archive.AsSpan(offset))
                == 0x06054b50)
            {
                return offset;
            }
        }

        throw new InvalidDataException(
            "Test archive has no end-of-central-directory record.");
    }

    static int FindCentralDirectoryFileHeader(byte[] archive)
    {
        for (int offset = 0; offset <= archive.Length - 4; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(
                    archive.AsSpan(offset))
                == 0x02014b50)
            {
                return offset;
            }
        }

        throw new InvalidDataException(
            "Test archive has no central-directory file header.");
    }

    /// <summary>
    /// Rewrites the single central-directory file header to carry a ZIP64
    /// extra field (<c>0x0001</c>) holding <paramref name="declared"/> as the
    /// uncompressed size, leaving the end-of-central-directory record free of
    /// ZIP64 sentinels so the archive reaches per-entry inspection.
    /// </summary>
    static byte[] InjectZip64UncompressedSize(byte[] archive, long declared)
    {
        int header = FindCentralDirectoryFileHeader(archive);
        ushort nameLength =
            BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(header + 28));
        ushort extraLength =
            BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(header + 30));
        int insertAt = header + 46 + nameLength + extraLength;

        const int Zip64FieldLength = 12;
        byte[] rewritten =
            new byte[archive.Length + Zip64FieldLength];
        archive.AsSpan(0, insertAt).CopyTo(rewritten);
        BinaryPrimitives.WriteUInt16LittleEndian(
            rewritten.AsSpan(insertAt),
            0x0001);
        BinaryPrimitives.WriteUInt16LittleEndian(
            rewritten.AsSpan(insertAt + 2),
            8);
        BinaryPrimitives.WriteInt64LittleEndian(
            rewritten.AsSpan(insertAt + 4),
            declared);
        archive.AsSpan(insertAt).CopyTo(
            rewritten.AsSpan(insertAt + Zip64FieldLength));

        BinaryPrimitives.WriteUInt32LittleEndian(
            rewritten.AsSpan(header + 24),
            uint.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(
            rewritten.AsSpan(header + 30),
            checked((ushort)(extraLength + Zip64FieldLength)));

        int endRecord = FindEndOfCentralDirectory(rewritten);
        uint centralDirectorySize =
            BinaryPrimitives.ReadUInt32LittleEndian(
                rewritten.AsSpan(endRecord + 12));
        BinaryPrimitives.WriteUInt32LittleEndian(
            rewritten.AsSpan(endRecord + 12),
            centralDirectorySize + Zip64FieldLength);
        return rewritten;
    }

    internal static (byte[] Bytes, Guid Guid) BuildPortablePdb(
        Guid id,
        uint stamp = 0x04030201u)
    {
        var metadata = new MetadataBuilder();
        var contentId = new BlobContentId(id, stamp);
        var rowCounts = ImmutableArray.CreateRange(new int[MetadataTokens.TableCount]);
        var pdbBuilder = new PortablePdbBuilder(
            metadata,
            rowCounts,
            entryPoint: default,
            idProvider: _ => contentId);
        var blob = new BlobBuilder();
        pdbBuilder.Serialize(blob);
        return (blob.ToArray(), id);
    }

    internal static byte[] MakeSnupkg(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return ms.ToArray();
    }
}
