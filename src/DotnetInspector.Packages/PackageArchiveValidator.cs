using System.Buffers.Binary;
using System.IO.Compression;

namespace DotnetInspector.Packages;

/// <summary>The result of validating a candidate package archive.</summary>
public abstract record PackageArchiveValidation
{
    private protected PackageArchiveValidation()
    {
    }

    /// <summary>The archive may be published into a package store.</summary>
    public sealed record Valid : PackageArchiveValidation
    {
        internal Valid(int entryCount, long expandedBytes)
        {
            EntryCount = entryCount;
            ExpandedBytes = expandedBytes;
        }

        /// <summary>File entries the archive actually contains.</summary>
        public int EntryCount { get; }

        /// <summary>Bytes actually produced by decompressing every entry.</summary>
        public long ExpandedBytes { get; }
    }

    /// <summary>
    /// The archive may not be published, for the stated reason.
    /// </summary>
    /// <remarks>
    /// The reason names the rule and the configured limit only. No entry name,
    /// header field, or other byte read out of the archive appears in it: the
    /// rejected value is by construction the most hostile text the validator
    /// has seen, and a message is a sink like any other.
    /// </remarks>
    public sealed record Rejected : PackageArchiveValidation
    {
        internal Rejected(string reason) => Reason = reason;

        public string Reason { get; }
    }
}

/// <summary>
/// Decides whether a downloaded archive may become package content, before any
/// store publishes it.
/// </summary>
/// <remarks>
/// <para>
/// A payload arrives from a remote feed, so nothing it declares about itself is
/// evidence. Validation runs in two stages, both bounded:
/// </para>
/// <list type="number">
/// <item>
/// a preflight that parses only the end-of-central-directory records to learn
/// how many entries the archive <em>claims</em>, and rejects an excessive claim
/// before <see cref="ZipArchive"/> is constructed. This matters because opening
/// an archive materializes the whole central directory: a few hundred megabytes
/// of tiny entries is a few million <see cref="ZipArchiveEntry"/> objects, which
/// is an allocation the byte cap alone does not bound; and
/// </item>
/// <item>
/// a full pass that opens the archive, validates every entry path against the
/// same containment rules the stores apply, and <em>streams every entry through
/// a decompressor</em>, counting the bytes that actually emerge.
/// </item>
/// </list>
/// <para>
/// Every entry means every entry, including the directory-shaped ones. A store
/// treats an entry whose name ends in <c>/</c> as a directory and never reads
/// it, so content hidden inside one is content no budget would ever account
/// for; such an entry is refused rather than skipped, while an ordinary empty
/// directory entry passes. Skipping them by shape is what a payload would
/// exploit to publish expansion or an undecodable compression method.
/// </para>
/// <para>
/// The second stage streams rather than trusting <see cref="ZipArchiveEntry.Length"/>
/// for two reasons. An entry using a compression method this runtime does not
/// implement declares a perfectly ordinary length and only fails when something
/// opens it — which, without this pass, is after the payload is published, so
/// the failure lands on a consumer instead of on the source that served it, and
/// the cache keeps the poison. And a declared length is a claim: reading is what
/// checks it against the entry's CRC. The declared sizes are still summed first,
/// as a cheap precheck that rejects an obvious bomb without decompressing it.
/// </para>
/// <para>
/// Every rejection is a failure of one source. The caller tries the next
/// authorized source, and nothing reaches a store.
/// </para>
/// <para>
/// Gated by <c>PackageArchiveValidatorTests</c> and, end to end, by
/// <c>PackagePayloadAcquisitionTests</c>.
/// <c>Validate_RejectsTheSameTrailingDirectoryRecordTheDecoderWouldRead</c>
/// keeps the allocation-free preflight on the same EOCD record as
/// <see cref="ZipArchive"/>.
/// </para>
/// </remarks>
public static class PackageArchiveValidator
{
    /// <summary>
    /// The longest entry path an archive may carry.
    /// </summary>
    /// <remarks>
    /// A store maps an entry path onto a filesystem path or a dictionary key.
    /// Bounding it here keeps one rule in front of both, rather than leaving
    /// each store to discover its own platform limit at write time.
    /// </remarks>
    public const int MaxEntryPathLength = 1024;

    /// <summary>The longest single segment of an entry path.</summary>
    public const int MaxEntrySegmentLength = 255;

    /// <summary>
    /// Validates <paramref name="archive"/> against <paramref name="limits"/>.
    /// </summary>
    public static PackageArchiveValidation Validate(
        byte[] archive,
        PackagePayloadLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        limits ??= PackagePayloadLimits.Default;
        cancellationToken.ThrowIfCancellationRequested();

        if (ZipDirectoryPreflight.TryGetDeclaredEntryCount(
                archive,
                out long declaredEntries) is { } preflightProblem)
        {
            return new PackageArchiveValidation.Rejected(preflightProblem);
        }

        if (declaredEntries > limits.MaxEntryCount)
        {
            return new PackageArchiveValidation.Rejected(
                $"the archive directory declares more than {limits.MaxEntryCount} entries");
        }

        try
        {
            return Inspect(archive, limits, cancellationToken);
        }
        catch (Exception ex) when (
            ex is InvalidDataException or NotSupportedException)
        {
            // The archive decoder refused the payload: a corrupt directory, a
            // truncated entry, a lying declared size caught by its CRC, or a
            // compression method this runtime does not implement. That is a
            // rejection like any other, and it is typed here so a caller does
            // not have to catch it — the whole point of validating before
            // publication is that the failure lands on the source that served
            // the bytes. The decoder's message can name an archive entry, so
            // it is not reproduced.
            return new PackageArchiveValidation.Rejected(
                "the payload is not a readable archive, or uses an archive feature this host cannot decode");
        }
    }

    static PackageArchiveValidation Inspect(
        byte[] archive,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(archive, writable: false);
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);

        int entryCount = 0;
        long declaredBytes = 0;
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++entryCount > limits.MaxEntryCount)
            {
                // Defense in depth behind the preflight, which has already
                // refused a directory declaring more than this. It fires only
                // if an archive's directory under-reports its own contents,
                // which the decoder independently refuses.
                return new PackageArchiveValidation.Rejected(
                    $"the archive contains more than {limits.MaxEntryCount} entries");
            }

            if (!IsPublishableEntryPath(entry.FullName, out bool isDirectory))
            {
                return new PackageArchiveValidation.Rejected(
                    "an archive entry has a path that cannot address stored content safely");
            }

            // A directory-shaped entry is a name, not content: every store
            // treats it as a directory and no consumer reads it. An entry that
            // declares content while shaped like a directory is therefore
            // content nothing will account for, which is exactly how a payload
            // would smuggle expansion past a budget — so it is refused rather
            // than skipped. Ordinary empty directory entries still pass.
            if (isDirectory && entry.Length != 0)
            {
                return new PackageArchiveValidation.Rejected(
                    "a directory-shaped archive entry declares content");
            }

            // Compared rather than added: the sum of attacker-declared lengths
            // is the one place this validator could overflow, and a wrapped
            // total would compare below the limit.
            if (entry.Length > limits.MaxExpandedBytes - declaredBytes)
            {
                return new PackageArchiveValidation.Rejected(
                    $"the archive declares more than {limits.MaxExpandedBytes} bytes of expanded content");
            }

            declaredBytes += entry.Length;
        }

        long expandedBytes = 0;
        byte[] chunk = new byte[81920];
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool isDirectory = IsDirectoryEntry(entry.FullName);

            // Every entry is opened, including a directory-shaped one. A
            // declared length is a claim; opening is what checks it, and it is
            // also what finds a compression method this runtime cannot decode.
            // Skipping any entry would leave both unchecked until after
            // publication.
            using Stream content = entry.Open();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = content.Read(chunk, 0, chunk.Length);
                if (read == 0)
                    break;

                if (isDirectory)
                {
                    // Defense in depth behind the declared-length check above.
                    // It cannot fire while the decoder enforces an entry's
                    // declared size and CRC — a directory entry that declares
                    // nothing and yields something is refused there first — but
                    // the alternative to checking is accounting for those bytes
                    // nowhere.
                    return new PackageArchiveValidation.Rejected(
                        "a directory-shaped archive entry carries content");
                }

                if (read > limits.MaxExpandedBytes - expandedBytes)
                {
                    return new PackageArchiveValidation.Rejected(
                        $"the archive expands to more than {limits.MaxExpandedBytes} bytes");
                }

                expandedBytes += read;
            }
        }

        return new PackageArchiveValidation.Valid(entryCount, expandedBytes);
    }

    /// <summary>
    /// True when an archive entry path can address stored content safely under
    /// the same rules <see cref="StorePath"/> applies to a store key.
    /// </summary>
    static bool IsPublishableEntryPath(string entryPath, out bool isDirectory)
    {
        isDirectory = IsDirectoryEntry(entryPath);
        if (entryPath.Length is 0 or > MaxEntryPathLength)
            return false;

        // A directory entry is the one legal trailing separator; its content is
        // nothing, and no store writes it.
        string path = isDirectory ? entryPath[..^1] : entryPath;
        if (path.Length == 0 || path.Any(char.IsControl))
            return false;

        foreach (string segment in path.Split('/'))
        {
            if (segment.Length > MaxEntrySegmentLength
                || !StorePath.IsSafeSegment(segment))
            {
                return false;
            }
        }

        return true;
    }

    static bool IsDirectoryEntry(string entryPath) =>
        entryPath.EndsWith('/');
}

/// <summary>
/// Reads how many entries a ZIP archive's directory claims, without opening the
/// archive.
/// </summary>
/// <remarks>
/// <para>
/// The parse is deliberately minimal and fail-closed: it locates the
/// end-of-central-directory record, follows the ZIP64 locator when the classic
/// record is saturated, and reports the declared total entry count. Anything it
/// cannot read as a single-disk archive with a consistent directory — a missing
/// or truncated record, a multi-disk archive, a ZIP64 locator pointing outside
/// the payload, a record whose disk fields disagree — is a rejection, not a
/// guess.
/// </para>
/// <para>
/// It reads fixed-size fields at bounds-checked offsets and allocates nothing
/// proportional to the archive, so a hostile header cannot amplify work here.
/// Its answer is a claim by the archive, used only to refuse an archive whose
/// own directory is already too large to open; the authoritative count is the
/// one <see cref="PackageArchiveValidator"/> takes afterwards by enumeration.
/// </para>
/// </remarks>
static class ZipDirectoryPreflight
{
    const int EndOfCentralDirectorySize = 22;
    const int MaxCommentLength = 0xFFFF;
    const uint EndOfCentralDirectorySignature = 0x06054B50;
    const uint Zip64LocatorSignature = 0x07064B50;
    const uint Zip64EndOfCentralDirectorySignature = 0x06064B50;
    const int Zip64LocatorSize = 20;
    const int Zip64EndOfCentralDirectoryMinimumSize = 56;

    /// <summary>
    /// Returns null and sets <paramref name="declaredEntries"/> when the
    /// directory is readable, or the rejection reason when it is not.
    /// </summary>
    internal static string? TryGetDeclaredEntryCount(
        ReadOnlySpan<byte> archive,
        out long declaredEntries)
    {
        declaredEntries = 0;
        if (archive.Length < EndOfCentralDirectorySize)
            return "the payload is too small to be an archive";

        // ZipArchive uses the first signature found while scanning backwards.
        // Use that same record or reject it: continuing to an earlier signature
        // would let the preflight approve a decoy while the decoder opens a
        // later directory and materializes a different entry count.
        int earliest = Math.Max(
            0,
            archive.Length - EndOfCentralDirectorySize - MaxCommentLength);
        for (int end = archive.Length - EndOfCentralDirectorySize;
            end >= earliest;
            end--)
        {
            ReadOnlySpan<byte> record = archive[end..];
            if (ReadUInt32(record, 0) != EndOfCentralDirectorySignature)
                continue;

            ushort commentLength = ReadUInt16(record, 20);
            if (end + EndOfCentralDirectorySize + commentLength != archive.Length)
            {
                return "the archive directory record does not account for the payload tail";
            }

            return ReadRecord(archive, end, out declaredEntries);
        }

        return "the payload has no readable archive directory";
    }

    static string? ReadRecord(
        ReadOnlySpan<byte> archive,
        int end,
        out long declaredEntries)
    {
        declaredEntries = 0;
        ReadOnlySpan<byte> record = archive[end..];
        ushort thisDisk = ReadUInt16(record, 4);
        ushort directoryDisk = ReadUInt16(record, 6);
        ushort entriesOnDisk = ReadUInt16(record, 8);
        ushort totalEntries = ReadUInt16(record, 10);
        uint directoryOffset = ReadUInt32(record, 16);

        bool saturated = thisDisk == ushort.MaxValue
            || directoryDisk == ushort.MaxValue
            || totalEntries == ushort.MaxValue
            || entriesOnDisk == ushort.MaxValue
            || directoryOffset == uint.MaxValue;
        if (!saturated)
        {
            if (thisDisk != 0 || directoryDisk != 0)
                return "the archive spans multiple disks";

            if (entriesOnDisk != totalEntries)
                return "the archive directory record is internally inconsistent";

            declaredEntries = totalEntries;
            return null;
        }

        return ReadZip64(archive, end, out declaredEntries);
    }

    static string? ReadZip64(
        ReadOnlySpan<byte> archive,
        int end,
        out long declaredEntries)
    {
        declaredEntries = 0;
        int locator = end - Zip64LocatorSize;
        if (locator < 0
            || ReadUInt32(archive[locator..], 0) != Zip64LocatorSignature)
        {
            return "the archive directory declares ZIP64 fields without a ZIP64 locator";
        }

        ReadOnlySpan<byte> locatorRecord = archive[locator..];
        uint zip64Disk = ReadUInt32(locatorRecord, 4);
        ulong zip64Offset = ReadUInt64(locatorRecord, 8);
        uint totalDisks = ReadUInt32(locatorRecord, 16);
        if (zip64Disk != 0 || totalDisks != 1)
            return "the archive spans multiple disks";

        if (archive.Length < Zip64EndOfCentralDirectoryMinimumSize
            || zip64Offset
                > (ulong)(archive.Length - Zip64EndOfCentralDirectoryMinimumSize))
        {
            return "the archive ZIP64 directory record lies outside the payload";
        }

        ReadOnlySpan<byte> zip64 = archive[(int)zip64Offset..];
        if (ReadUInt32(zip64, 0) != Zip64EndOfCentralDirectorySignature)
            return "the archive has no readable ZIP64 directory record";

        uint zip64ThisDisk = ReadUInt32(zip64, 16);
        uint zip64DirectoryDisk = ReadUInt32(zip64, 20);
        ulong entriesOnDisk = ReadUInt64(zip64, 24);
        ulong totalEntries = ReadUInt64(zip64, 32);
        if (zip64ThisDisk != 0 || zip64DirectoryDisk != 0)
            return "the archive spans multiple disks";

        if (entriesOnDisk != totalEntries)
            return "the archive ZIP64 directory record is internally inconsistent";

        if (totalEntries > long.MaxValue)
            return "the archive directory declares an unreadable entry count";

        declaredEntries = (long)totalEntries;
        return null;
    }

    static ushort ReadUInt16(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));

    static uint ReadUInt32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));

    static ulong ReadUInt64(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8));
}
