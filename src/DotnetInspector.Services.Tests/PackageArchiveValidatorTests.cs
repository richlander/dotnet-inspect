using System.Buffers.Binary;
using System.IO.Compression;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

/// <summary>
/// What a downloaded archive must satisfy before any store may publish it:
/// a directory small enough to open, entry paths every store can address
/// safely, and content that actually decompresses within the configured
/// budget.
/// </summary>
public sealed class PackageArchiveValidatorTests
{
    [Fact]
    public void Validate_AcceptsAnOrdinaryPackage()
    {
        byte[] archive = TestPackageArchive.Create(
            "lib/net10.0/Sample.dll",
            "lib/net10.0/de/Sample.resources.dll",
            "_rels/.rels",
            "[Content_Types].xml",
            "Sample.nuspec");

        var valid = Assert.IsType<PackageArchiveValidation.Valid>(
            PackageArchiveValidator.Validate(
                archive,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(5, valid.EntryCount);
        Assert.Equal(15, valid.ExpandedBytes);
    }

    [Fact]
    public void Validate_AcceptsDirectoryEntries()
    {
        byte[] archive = ArchiveWithNames(
            ("lib/", []),
            ("lib/net10.0/", []),
            ("lib/net10.0/Sample.dll", [1, 2, 3]));

        var valid = Assert.IsType<PackageArchiveValidation.Valid>(
            PackageArchiveValidator.Validate(
                archive,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(3, valid.EntryCount);
        Assert.Equal(3, valid.ExpandedBytes);
    }

    /// <summary>
    /// A store treats a directory-shaped entry as a directory and never reads
    /// it, so content inside one is content no budget accounts for. Skipping
    /// such an entry by shape published exactly this archive as valid, with
    /// eight kilobytes of unaccounted content and an expanded total of zero.
    /// </summary>
    [Fact]
    public void Validate_RejectsADirectoryEntryDeclaringContent()
    {
        byte[] archive = ArchiveWithNames(("lib/", new byte[8192]));

        var rejected = Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxExpandedBytes = 16 },
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "directory-shaped",
            rejected.Reason,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same shape with the declared size rewritten to zero, so the
    /// declared-length check cannot see it. Opening the entry is what finds it:
    /// the decoder checks an entry's content against its declared size and CRC,
    /// and reaches that check only because directory-shaped entries are no
    /// longer skipped. Skipping them published this archive as valid.
    /// </summary>
    [Fact]
    public void Validate_RejectsADirectoryEntryHidingContentBehindAZeroLength()
    {
        byte[] archive = WithZeroedUncompressedSize(
            ArchiveWithNames(("lib/", new byte[8192])));

        Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxExpandedBytes = 16 },
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An undecodable compression method on a directory-shaped entry is found
    /// before publication too, because the entry is opened like any other.
    /// </summary>
    [Fact]
    public void Validate_RejectsADirectoryEntryWithUnsupportedCompression()
    {
        byte[] archive = WithCompressionMethod(
            ArchiveWithNames(("lib/", [])),
            method: 99);

        Assert.IsType<PackageArchiveValidation.Rejected>(Validate(archive));
    }

    /// <summary>
    /// The budget is a limit, not a threshold: content that lands exactly on it
    /// is publishable, and one byte more is not.
    /// </summary>
    [Fact]
    public void Validate_AcceptsContentExactlyAtTheExpandedLimit()
    {
        byte[] archive = TestPackageArchive.CreateWithContent(
            ("lib/net10.0/One.dll", new byte[8192]),
            ("lib/net10.0/Two.dll", new byte[8192]));

        var valid = Assert.IsType<PackageArchiveValidation.Valid>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxExpandedBytes = 16384 },
                TestContext.Current.CancellationToken));
        Assert.Equal(16384, valid.ExpandedBytes);

        Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxExpandedBytes = 16383 },
                TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Both accumulators compare against the remaining budget rather than
    /// summing first, so no declared length or read count can wrap a total past
    /// the limit. At the largest expressible budget an ordinary archive is
    /// still simply valid.
    /// </summary>
    [Fact]
    public void Validate_AccumulatesWithoutOverflowAtTheLargestBudget()
    {
        byte[] archive = TestPackageArchive.CreateWithContent(
            ("lib/net10.0/One.dll", new byte[8192]),
            ("lib/net10.0/Two.dll", new byte[8192]));

        var valid = Assert.IsType<PackageArchiveValidation.Valid>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxExpandedBytes = long.MaxValue },
                TestContext.Current.CancellationToken));
        Assert.Equal(16384, valid.ExpandedBytes);
    }

    /// <summary>
    /// The traversing entry is the finding: the filesystem store refuses it at
    /// extraction time while the in-memory store would have published it, so
    /// two hosts disagreed about what a package is. One rule decides now, and
    /// it decides before either store sees the bytes.
    /// </summary>
    [Theory]
    [InlineData("../ignored.txt")]
    [InlineData("lib/../../escape.dll")]
    [InlineData("/rooted.txt")]
    [InlineData("lib\\net10.0\\Sample.dll")]
    [InlineData("C:/absolute.txt")]
    [InlineData("lib/./Sample.dll")]
    [InlineData("lib//Sample.dll")]
    [InlineData("lib/net10.0/Sam\u0007ple.dll")]
    public void Validate_RejectsAnUnsafeEntryPath(string entryPath)
    {
        byte[] archive = ArchiveWithNames(
            ("lib/net10.0/Sample.dll", [1, 2, 3]),
            (entryPath, [4, 5, 6]));

        var rejected = Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                archive,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.DoesNotContain(entryPath, rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAnOverlongEntryPath()
    {
        string overlong = "lib/net10.0/"
            + new string('a', PackageArchiveValidator.MaxEntryPathLength);

        Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                ArchiveWithNames((overlong, [1])),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Validate_RejectsAnOverlongEntrySegment()
    {
        string segment = new('a', PackageArchiveValidator.MaxEntrySegmentLength + 1);

        Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                ArchiveWithNames(($"lib/{segment}/Sample.dll", [1])),
                cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A compression method this runtime cannot decode declares an ordinary
    /// length and only fails when something opens the entry. Without streaming
    /// every entry here, that failure lands after publication: the source that
    /// served the archive has already been credited, the cache holds it, and
    /// the next authorized source is never tried.
    /// </summary>
    [Fact]
    public void Validate_RejectsAnUnsupportedCompressionMethod()
    {
        byte[] archive = WithCompressionMethod(
            TestPackageArchive.Create("lib/net10.0/Sample.dll"),
            method: 99);

        Assert.IsType<PackageArchiveValidation.Rejected>(
            Validate(archive));
    }

    [Fact]
    public void Validate_RejectsContentThatExpandsBeyondTheLimit()
    {
        // Declared sizes stay inside the budget; the bytes that actually
        // emerge do not, which only streaming can see.
        byte[] archive = TestPackageArchive.CreateWithContent(
            ("lib/net10.0/Sample.dll", new byte[8192]));

        var rejected = Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxExpandedBytes = 4096 },
                TestContext.Current.CancellationToken));
        Assert.Contains("4096", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsADeclaredExpansionBeyondTheLimit()
    {
        byte[] archive = TestPackageArchive.CreateWithContent(
            ("lib/net10.0/Sample.dll", new byte[512 * 1024]));

        var rejected = Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxExpandedBytes = 4096 },
                TestContext.Current.CancellationToken));
        Assert.Contains("declares", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsMoreEntriesThanAllowed()
    {
        byte[] archive = TestPackageArchive.Create(
            "lib/net10.0/One.dll",
            "lib/net10.0/Two.dll",
            "lib/net10.0/Three.dll");

        // The honest archive is refused by the preflight, before the archive is
        // opened: the reason is the directory's declaration, not a count taken
        // by enumerating entries.
        var rejected = Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxEntryCount = 2 },
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "directory declares",
            rejected.Reason,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Opening an archive materializes its whole central directory, so a
    /// declared count above the limit has to be refused before the archive is
    /// constructed. The message is the proof of which check fired: a directory
    /// this inconsistent makes the decoder itself throw, so reaching the
    /// declaration message means nothing opened the archive.
    /// </summary>
    [Fact]
    public void Validate_RejectsADeclaredEntryCountBeforeOpeningTheArchive()
    {
        byte[] archive = WithDeclaredEntryCount(
            TestPackageArchive.Create("lib/net10.0/Sample.dll"),
            declared: 60_000);

        var rejected = Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxEntryCount = 50_000 },
                TestContext.Current.CancellationToken));
        Assert.Contains(
            "directory declares",
            rejected.Reason,
            StringComparison.Ordinal);

        // Raising the limit lets the preflight pass, and the decoder then
        // refuses the same archive for its own reason. Two different messages
        // for one payload is what shows the preflight is a separate stage.
        var opened = Assert.IsType<PackageArchiveValidation.Rejected>(
            PackageArchiveValidator.Validate(
                archive,
                new PackagePayloadLimits { MaxEntryCount = 60_000 },
                TestContext.Current.CancellationToken));
        Assert.DoesNotContain(
            "directory declares",
            opened.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAMultiDiskArchive()
    {
        byte[] archive = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        int end = EndOfCentralDirectory(archive);
        BinaryPrimitives.WriteUInt16LittleEndian(
            archive.AsSpan(end + 4),
            1);

        var rejected = Assert.IsType<PackageArchiveValidation.Rejected>(
            Validate(archive));
        Assert.Contains("disks", rejected.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAnInconsistentDirectoryRecord()
    {
        byte[] archive = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        int end = EndOfCentralDirectory(archive);

        // Entries-on-disk and total-entries disagree.
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(end + 8), 5);

        Assert.IsType<PackageArchiveValidation.Rejected>(Validate(archive));
    }

    [Fact]
    public void Validate_RejectsZip64FieldsWithoutALocator()
    {
        byte[] archive = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        int end = EndOfCentralDirectory(archive);
        BinaryPrimitives.WriteUInt16LittleEndian(
            archive.AsSpan(end + 10),
            ushort.MaxValue);

        var rejected = Assert.IsType<PackageArchiveValidation.Rejected>(
            Validate(archive));
        Assert.Contains("ZIP64", rejected.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not an archive at all")]
    [InlineData("")]
    public void Validate_RejectsAPayloadThatIsNotAnArchive(string payload)
    {
        Assert.IsType<PackageArchiveValidation.Rejected>(
            Validate(System.Text.Encoding.UTF8.GetBytes(payload)));
    }

    [Fact]
    public void Validate_RejectsATruncatedArchive()
    {
        byte[] archive = TestPackageArchive.Create("lib/net10.0/Sample.dll");

        Assert.IsType<PackageArchiveValidation.Rejected>(
            Validate(archive[..(archive.Length - 8)]));
    }

    /// <summary>
    /// A ZIP64 directory is a legitimate shape, not an attack, so the preflight
    /// reads it rather than failing closed on it. The archive here is a real
    /// one whose directory has been rewritten into the ZIP64 form, which is the
    /// form a large package arrives in.
    /// </summary>
    [Fact]
    public void Validate_AcceptsAZip64Directory()
    {
        byte[] archive = AsZip64(
            TestPackageArchive.Create(
                "lib/net10.0/Sample.dll",
                "lib/net10.0/Other.dll"));

        var valid = Assert.IsType<PackageArchiveValidation.Valid>(
            Validate(archive));
        Assert.Equal(2, valid.EntryCount);
    }

    [Fact]
    public void Validate_RejectsAZip64DirectoryDeclaringTooManyEntries()
    {
        byte[] archive = AsZip64(
            TestPackageArchive.Create("lib/net10.0/Sample.dll"),
            declaredEntries: 5_000_000);

        var rejected = Assert.IsType<PackageArchiveValidation.Rejected>(
            Validate(archive));
        Assert.Contains(
            "directory declares",
            rejected.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ObservesCancellation()
    {
        byte[] archive = TestPackageArchive.Create("lib/net10.0/Sample.dll");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => PackageArchiveValidator.Validate(
                archive,
                cancellationToken: cancellation.Token));
    }

    static PackageArchiveValidation Validate(byte[] archive) =>
        PackageArchiveValidator.Validate(
            archive,
            cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>
    /// Builds an archive with entry names a compliant writer would refuse to
    /// produce, which is exactly what an adversarial feed can serve.
    /// </summary>
    static byte[] ArchiveWithNames(
        params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string name, byte[] content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(name);
                using Stream stream = entry.Open();
                stream.Write(content, 0, content.Length);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Rewrites every compression-method field to <paramref name="method"/>.</summary>
    static byte[] WithCompressionMethod(byte[] archive, ushort method)
    {
        byte[] rewritten = (byte[])archive.Clone();
        for (int offset = 0; offset + 4 <= rewritten.Length; offset++)
        {
            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(
                rewritten.AsSpan(offset));
            if (signature == 0x04034B50)
            {
                // Local file header: method at +8.
                BinaryPrimitives.WriteUInt16LittleEndian(
                    rewritten.AsSpan(offset + 8),
                    method);
            }
            else if (signature == 0x02014B50)
            {
                // Central directory header: method at +10.
                BinaryPrimitives.WriteUInt16LittleEndian(
                    rewritten.AsSpan(offset + 10),
                    method);
            }
        }

        return rewritten;
    }

    static byte[] WithDeclaredEntryCount(byte[] archive, ushort declared)
    {
        byte[] rewritten = (byte[])archive.Clone();
        int end = EndOfCentralDirectory(rewritten);
        BinaryPrimitives.WriteUInt16LittleEndian(
            rewritten.AsSpan(end + 8),
            declared);
        BinaryPrimitives.WriteUInt16LittleEndian(
            rewritten.AsSpan(end + 10),
            declared);
        return rewritten;
    }

    /// <summary>
    /// Rewrites every declared uncompressed size to zero, leaving the entry's
    /// real content in place, so only reading the entry can find it.
    /// </summary>
    static byte[] WithZeroedUncompressedSize(byte[] archive)
    {
        byte[] rewritten = (byte[])archive.Clone();
        for (int offset = 0; offset + 4 <= rewritten.Length; offset++)
        {
            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(
                rewritten.AsSpan(offset));
            if (signature == 0x04034B50)
            {
                // Local file header: uncompressed size at +22.
                BinaryPrimitives.WriteUInt32LittleEndian(
                    rewritten.AsSpan(offset + 22),
                    0);
            }
            else if (signature == 0x02014B50)
            {
                // Central directory header: uncompressed size at +24.
                BinaryPrimitives.WriteUInt32LittleEndian(
                    rewritten.AsSpan(offset + 24),
                    0);
            }
        }

        return rewritten;
    }

    /// <summary>
    /// Rewrites an archive's directory into the ZIP64 form: a ZIP64 record and
    /// locator, with the classic record's saturated sentinel values.
    /// </summary>
    static byte[] AsZip64(byte[] archive, long? declaredEntries = null)
    {
        int end = EndOfCentralDirectory(archive);
        ushort entries = BinaryPrimitives.ReadUInt16LittleEndian(
            archive.AsSpan(end + 10));
        uint directorySize = BinaryPrimitives.ReadUInt32LittleEndian(
            archive.AsSpan(end + 12));
        uint directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(
            archive.AsSpan(end + 16));
        long total = declaredEntries ?? entries;

        var rewritten = new MemoryStream();
        rewritten.Write(archive, 0, end);
        long zip64Offset = rewritten.Length;

        Span<byte> zip64 = stackalloc byte[56];
        zip64.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(zip64, 0x06064B50);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[4..], 44);
        BinaryPrimitives.WriteUInt16LittleEndian(zip64[12..], 45);
        BinaryPrimitives.WriteUInt16LittleEndian(zip64[14..], 45);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[24..], (ulong)total);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[32..], (ulong)total);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[40..], directorySize);
        BinaryPrimitives.WriteUInt64LittleEndian(zip64[48..], directoryOffset);
        rewritten.Write(zip64);

        Span<byte> locator = stackalloc byte[20];
        locator.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(locator, 0x07064B50);
        BinaryPrimitives.WriteUInt64LittleEndian(locator[8..], (ulong)zip64Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(locator[16..], 1);
        rewritten.Write(locator);

        Span<byte> record = stackalloc byte[22];
        archive.AsSpan(end, 22).CopyTo(record);
        BinaryPrimitives.WriteUInt16LittleEndian(record[8..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(record[10..], ushort.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(record[16..], uint.MaxValue);
        rewritten.Write(record);
        return rewritten.ToArray();
    }

    static int EndOfCentralDirectory(byte[] archive)
    {
        for (int offset = archive.Length - 22; offset >= 0; offset--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(offset))
                == 0x06054B50)
            {
                return offset;
            }
        }

        throw new InvalidOperationException(
            "The fixture archive has no end-of-central-directory record.");
    }
}
