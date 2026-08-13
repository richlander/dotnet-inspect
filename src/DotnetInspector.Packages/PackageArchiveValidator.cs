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
/// a full pass that validates every entry path against the same containment
/// rules the stores apply, then reads each central record's exact compressed
/// byte range and <em>streams it through an independent decompressor</em>,
/// counting the bytes that actually emerge and checking their CRC.
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
/// The second stage does not trust <see cref="ZipArchiveEntry.Open"/> to expose
/// bytes beyond an attacker-declared length: supported runtime versions differ
/// there. It bounds the raw compressed slice from the local and central
/// records, independently inflates stored or DEFLATE content, and compares the
/// actual length and CRC with the directory claims. The declared sizes are
/// still summed first, as a cheap precheck that rejects an obvious bomb without
/// decompressing it.
/// </para>
/// <para>
/// Every rejection is a failure of one source. The caller tries the next
/// authorized source, and nothing reaches a store.
/// </para>
/// <para>
/// Gated by <c>PackageArchiveValidatorTests</c> and, end to end, by
/// <c>PackagePayloadAcquisitionTests</c>.
/// <c>Validate_RejectsHiddenContentWhoseCrcIsZero</c> is the
/// runtime-independent decompression gate and runs under the shipped
/// <c>net10.0</c> target as well as the development target.
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

        if (ZipDirectoryPreflight.TryGetDirectory(
                archive,
                out ZipDirectoryRecord directory) is { } preflightProblem)
        {
            return new PackageArchiveValidation.Rejected(preflightProblem);
        }

        if (directory.EntryCount > limits.MaxEntryCount)
        {
            return new PackageArchiveValidation.Rejected(
                $"the archive directory declares more than {limits.MaxEntryCount} entries");
        }

        try
        {
            return Inspect(
                archive,
                directory,
                limits,
                cancellationToken);
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
        ZipDirectoryRecord directory,
        PackagePayloadLimits limits,
        CancellationToken cancellationToken)
    {
        if (ZipEntryDescriptorReader.TryRead(
                archive,
                directory,
                out IReadOnlyList<ZipEntryDescriptor> descriptors)
            is { } directoryProblem)
        {
            return new PackageArchiveValidation.Rejected(directoryProblem);
        }

        using var buffer = new MemoryStream(archive, writable: false);
        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        if (zip.Entries.Count != descriptors.Count)
        {
            return new PackageArchiveValidation.Rejected(
                "the archive directory entry count is inconsistent");
        }

        int entryCount = 0;
        long declaredBytes = 0;
        var explicitDestinations = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var fileDestinations = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var requiredDirectories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        for (int entryIndex = 0;
            entryIndex < zip.Entries.Count;
            entryIndex++)
        {
            ZipArchiveEntry entry = zip.Entries[entryIndex];
            ZipEntryDescriptor descriptor = descriptors[entryIndex];
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

            string destination = isDirectory
                ? entry.FullName[..^1]
                : entry.FullName;
            if (HasDestinationCollision(
                    destination,
                    isDirectory,
                    explicitDestinations,
                    fileDestinations,
                    requiredDirectories))
            {
                return new PackageArchiveValidation.Rejected(
                    "archive entries resolve to colliding portable destinations");
            }

            // A directory-shaped entry is a name, not content: every store
            // treats it as a directory and no consumer reads it. An entry that
            // declares content while shaped like a directory is therefore
            // content nothing will account for, which is exactly how a payload
            // would smuggle expansion past a budget — so it is refused rather
            // than skipped. Ordinary empty directory entries still pass.
            if (isDirectory && descriptor.UncompressedSize != 0)
            {
                return new PackageArchiveValidation.Rejected(
                    "a directory-shaped archive entry declares content");
            }

            // Compared rather than added: the sum of attacker-declared lengths
            // is the one place this validator could overflow, and a wrapped
            // total would compare below the limit.
            if (descriptor.UncompressedSize
                > (ulong)(limits.MaxExpandedBytes - declaredBytes))
            {
                return new PackageArchiveValidation.Rejected(
                    $"the archive declares more than {limits.MaxExpandedBytes} bytes of expanded content");
            }

            declaredBytes += (long)descriptor.UncompressedSize;
        }

        long expandedBytes = 0;
        byte[] chunk = new byte[81920];
        for (int entryIndex = 0;
            entryIndex < zip.Entries.Count;
            entryIndex++)
        {
            ZipArchiveEntry entry = zip.Entries[entryIndex];
            ZipEntryDescriptor descriptor = descriptors[entryIndex];
            cancellationToken.ThrowIfCancellationRequested();
            bool isDirectory = IsDirectoryEntry(entry.FullName);

            using Stream compressed = new MemoryStream(
                archive,
                descriptor.DataOffset,
                checked((int)descriptor.CompressedSize),
                writable: false);
            using Stream content = descriptor.CompressionMethod switch
            {
                0 => compressed,
                8 => new DeflateStream(
                    compressed,
                    CompressionMode.Decompress),
                _ => throw new NotSupportedException(),
            };
            var crc = new ZipCrc32();
            long entryBytes = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int read = content.Read(chunk, 0, chunk.Length);
                if (read == 0)
                    break;

                if (read > long.MaxValue - entryBytes)
                {
                    return new PackageArchiveValidation.Rejected(
                        "an archive entry expands to an unreadable size");
                }

                entryBytes += read;
                crc.Append(chunk.AsSpan(0, read));
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

            if ((ulong)entryBytes != descriptor.UncompressedSize
                || crc.Value != descriptor.Crc32)
            {
                return new PackageArchiveValidation.Rejected(
                    "an archive entry does not match its declared size or checksum");
            }
        }

        return new PackageArchiveValidation.Valid(entryCount, expandedBytes);
    }

    static bool HasDestinationCollision(
        string destination,
        bool isDirectory,
        HashSet<string> explicitDestinations,
        HashSet<string> fileDestinations,
        HashSet<string> requiredDirectories)
    {
        if (!explicitDestinations.Add(destination))
            return true;

        if (isDirectory)
        {
            if (fileDestinations.Contains(destination))
                return true;
        }
        else
        {
            if (requiredDirectories.Contains(destination))
                return true;

            fileDestinations.Add(destination);
        }

        int separator = destination.IndexOf('/');
        while (separator >= 0)
        {
            string ancestor = destination[..separator];
            if (fileDestinations.Contains(ancestor))
                return true;

            requiredDirectories.Add(ancestor);
            separator = destination.IndexOf('/', separator + 1);
        }

        return false;
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
internal readonly record struct ZipDirectoryRecord(
    long EntryCount,
    int Offset,
    int Size,
    long ArchiveOffset);

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
    /// Returns null and sets <paramref name="directory"/> when the directory is
    /// readable, or the rejection reason when it is not.
    /// </summary>
    internal static string? TryGetDirectory(
        ReadOnlySpan<byte> archive,
        out ZipDirectoryRecord directory)
    {
        directory = default;
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

            return ReadRecord(archive, end, out directory);
        }

        return "the payload has no readable archive directory";
    }

    static string? ReadRecord(
        ReadOnlySpan<byte> archive,
        int end,
        out ZipDirectoryRecord directory)
    {
        directory = default;
        ReadOnlySpan<byte> record = archive[end..];
        ushort thisDisk = ReadUInt16(record, 4);
        ushort directoryDisk = ReadUInt16(record, 6);
        ushort entriesOnDisk = ReadUInt16(record, 8);
        ushort totalEntries = ReadUInt16(record, 10);
        uint directorySize = ReadUInt32(record, 12);
        uint directoryOffset = ReadUInt32(record, 16);

        bool saturated = thisDisk == ushort.MaxValue
            || directoryDisk == ushort.MaxValue
            || totalEntries == ushort.MaxValue
            || entriesOnDisk == ushort.MaxValue
            || directorySize == uint.MaxValue
            || directoryOffset == uint.MaxValue;
        if (!saturated)
        {
            if (thisDisk != 0 || directoryDisk != 0)
                return "the archive spans multiple disks";

            if (entriesOnDisk != totalEntries)
                return "the archive directory record is internally inconsistent";

            return CreateDirectoryRecord(
                archive,
                end,
                totalEntries,
                directorySize,
                directoryOffset,
                out directory);
        }

        return ReadZip64(archive, end, out directory);
    }

    static string? ReadZip64(
        ReadOnlySpan<byte> archive,
        int end,
        out ZipDirectoryRecord directory)
    {
        directory = default;
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
        ulong directorySize = ReadUInt64(zip64, 40);
        ulong directoryOffset = ReadUInt64(zip64, 48);
        if (zip64ThisDisk != 0 || zip64DirectoryDisk != 0)
            return "the archive spans multiple disks";

        if (entriesOnDisk != totalEntries)
            return "the archive ZIP64 directory record is internally inconsistent";

        if (totalEntries > long.MaxValue)
            return "the archive directory declares an unreadable entry count";

        return CreateDirectoryRecord(
            archive,
            checked((long)zip64Offset),
            (long)totalEntries,
            directorySize,
            directoryOffset,
            out directory);
    }

    static string? CreateDirectoryRecord(
        ReadOnlySpan<byte> archive,
        long actualDirectoryEnd,
        long entryCount,
        ulong directorySize,
        ulong declaredDirectoryOffset,
        out ZipDirectoryRecord directory)
    {
        directory = default;
        if (directorySize > int.MaxValue
            || directorySize > (ulong)actualDirectoryEnd)
        {
            return "the archive directory lies outside the payload";
        }

        long centralDirectoryEnd = FindCentralDirectoryEnd(
            archive,
            actualDirectoryEnd,
            directorySize,
            entryCount);
        long actualOffset = centralDirectoryEnd - (long)directorySize;
        if (declaredDirectoryOffset > (ulong)actualOffset)
        {
            return "the archive directory offset is inconsistent";
        }

        directory = new ZipDirectoryRecord(
            entryCount,
            checked((int)actualOffset),
            (int)directorySize,
            actualOffset - (long)declaredDirectoryOffset);
        return null;
    }

    static long FindCentralDirectoryEnd(
        ReadOnlySpan<byte> archive,
        long recordStart,
        ulong directorySize,
        long entryCount)
    {
        const uint CentralDirectorySignature = 0x02014B50;
        const uint DigitalSignature = 0x05054B50;
        const int DigitalSignatureHeaderSize = 6;

        long unsignedDirectoryStart = recordStart - (long)directorySize;
        if (entryCount > 0
            && unsignedDirectoryStart <= archive.Length - sizeof(uint)
            && ReadUInt32(
                archive[(int)unsignedDirectoryStart..],
                0) == CentralDirectorySignature)
        {
            return recordStart;
        }

        int latest = checked((int)recordStart) - DigitalSignatureHeaderSize;
        int earliest = Math.Max(
            0,
            latest - ushort.MaxValue);
        for (int candidate = earliest; candidate <= latest; candidate++)
        {
            ReadOnlySpan<byte> possible = archive[candidate..];
            if (ReadUInt32(possible, 0) != DigitalSignature)
                continue;

            ushort dataLength = ReadUInt16(possible, 4);
            if (candidate + DigitalSignatureHeaderSize + dataLength
                != recordStart)
            {
                continue;
            }

            long signedDirectoryStart =
                candidate - (long)directorySize;
            if (signedDirectoryStart < 0)
                continue;

            if (entryCount == 0
                || (signedDirectoryStart
                        <= archive.Length - sizeof(uint)
                    && ReadUInt32(
                        archive[(int)signedDirectoryStart..],
                        0) == CentralDirectorySignature))
            {
                return candidate;
            }
        }

        return recordStart;
    }

    static ushort ReadUInt16(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, 2));

    static uint ReadUInt32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(offset, 4));

    static ulong ReadUInt64(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt64LittleEndian(source.Slice(offset, 8));
}

internal readonly record struct ZipEntryDescriptor(
    ushort CompressionMethod,
    uint Crc32,
    ulong CompressedSize,
    ulong UncompressedSize,
    int DataOffset);

static class ZipEntryDescriptorReader
{
    const uint CentralDirectorySignature = 0x02014B50;
    const uint LocalFileHeaderSignature = 0x04034B50;
    const int CentralDirectoryHeaderSize = 46;
    const int LocalFileHeaderSize = 30;
    const ushort Zip64ExtraFieldId = 0x0001;

    internal static string? TryRead(
        ReadOnlySpan<byte> archive,
        ZipDirectoryRecord directory,
        out IReadOnlyList<ZipEntryDescriptor> descriptors)
    {
        descriptors = [];
        if (directory.EntryCount > int.MaxValue)
            return "the archive directory declares an unreadable entry count";

        int directoryEnd = checked(directory.Offset + directory.Size);
        int offset = directory.Offset;
        var entries = new List<ZipEntryDescriptor>(
            (int)directory.EntryCount);
        for (long index = 0; index < directory.EntryCount; index++)
        {
            if (directoryEnd - offset < CentralDirectoryHeaderSize
                || ReadUInt32(archive, offset)
                    != CentralDirectorySignature)
            {
                return "the archive central directory is truncated or malformed";
            }

            ushort flags = ReadUInt16(archive, offset + 8);
            ushort method = ReadUInt16(archive, offset + 10);
            uint crc32 = ReadUInt32(archive, offset + 16);
            uint compressed32 = ReadUInt32(archive, offset + 20);
            uint uncompressed32 = ReadUInt32(archive, offset + 24);
            ushort nameLength = ReadUInt16(archive, offset + 28);
            ushort extraLength = ReadUInt16(archive, offset + 30);
            ushort commentLength = ReadUInt16(archive, offset + 32);
            ushort diskStart = ReadUInt16(archive, offset + 34);
            uint localOffset32 = ReadUInt32(archive, offset + 42);
            int variableLength;
            try
            {
                variableLength = checked(
                    nameLength + extraLength + commentLength);
            }
            catch (OverflowException)
            {
                return "the archive central directory is malformed";
            }

            if (directoryEnd - offset - CentralDirectoryHeaderSize
                < variableLength)
            {
                return "the archive central directory is truncated or malformed";
            }

            ReadOnlySpan<byte> extra = archive.Slice(
                offset + CentralDirectoryHeaderSize + nameLength,
                extraLength);
            if (TryResolveZip64(
                    extra,
                    compressed32,
                    uncompressed32,
                    localOffset32,
                    diskStart,
                    out ulong compressedSize,
                    out ulong uncompressedSize,
                    out ulong localOffset,
                    out uint resolvedDisk)
                is { } zip64Problem)
            {
                return zip64Problem;
            }

            if (resolvedDisk != 0)
                return "the archive spans multiple disks";

            if ((flags & 0x0001) != 0 || (flags & 0x0040) != 0)
                return "the archive contains an encrypted entry";

            if (method is not 0 and not 8)
                return "the archive uses an unsupported compression method";

            if (localOffset > long.MaxValue)
                return "an archive entry points outside the payload";

            long adjustedLocalOffset =
                (long)localOffset + directory.ArchiveOffset;
            if (adjustedLocalOffset < 0
                || adjustedLocalOffset
                    > archive.Length - LocalFileHeaderSize)
            {
                return "an archive entry points outside the payload";
            }

            int local = (int)adjustedLocalOffset;
            if (ReadUInt32(archive, local) != LocalFileHeaderSignature)
                return "an archive entry has no readable local header";

            ushort localFlags = ReadUInt16(archive, local + 6);
            ushort localMethod = ReadUInt16(archive, local + 8);
            ushort localNameLength = ReadUInt16(archive, local + 26);
            ushort localExtraLength = ReadUInt16(archive, local + 28);
            if (localFlags != flags || localMethod != method)
            {
                return "an archive entry disagrees with its local header";
            }

            int dataOffset;
            try
            {
                dataOffset = checked(
                    local
                    + LocalFileHeaderSize
                    + localNameLength
                    + localExtraLength);
            }
            catch (OverflowException)
            {
                return "an archive entry points outside the payload";
            }

            if (dataOffset > directory.Offset
                || compressedSize
                    > (ulong)(directory.Offset - dataOffset))
            {
                return "an archive entry's compressed data lies outside the payload";
            }

            ReadOnlySpan<byte> centralName = archive.Slice(
                offset + CentralDirectoryHeaderSize,
                nameLength);
            ReadOnlySpan<byte> localName = archive.Slice(
                local + LocalFileHeaderSize,
                localNameLength);
            if (!centralName.SequenceEqual(localName))
                return "an archive entry disagrees with its local header";

            entries.Add(
                new ZipEntryDescriptor(
                    method,
                    crc32,
                    compressedSize,
                    uncompressedSize,
                    dataOffset));
            offset += CentralDirectoryHeaderSize + variableLength;
        }

        if (offset != directoryEnd)
            return "the archive central directory has trailing records";

        descriptors = entries;
        return null;
    }

    static string? TryResolveZip64(
        ReadOnlySpan<byte> extra,
        uint compressed32,
        uint uncompressed32,
        uint localOffset32,
        ushort diskStart16,
        out ulong compressedSize,
        out ulong uncompressedSize,
        out ulong localOffset,
        out uint diskStart)
    {
        compressedSize = compressed32;
        uncompressedSize = uncompressed32;
        localOffset = localOffset32;
        diskStart = diskStart16;
        bool needsZip64 = compressed32 == uint.MaxValue
            || uncompressed32 == uint.MaxValue
            || localOffset32 == uint.MaxValue
            || diskStart16 == ushort.MaxValue;
        if (!needsZip64)
            return null;

        ReadOnlySpan<byte> values = default;
        int offset = 0;
        while (extra.Length - offset >= 4)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(
                extra.Slice(offset, 2));
            ushort size = BinaryPrimitives.ReadUInt16LittleEndian(
                extra.Slice(offset + 2, 2));
            offset += 4;
            if (size > extra.Length - offset)
                return "an archive entry has a malformed ZIP64 field";

            if (id == Zip64ExtraFieldId)
            {
                values = extra.Slice(offset, size);
                break;
            }

            offset += size;
        }

        if (values.IsEmpty)
            return "an archive entry is missing its ZIP64 field";

        int valueOffset = 0;
        if (uncompressed32 == uint.MaxValue
            && !TryReadUInt64(
                values,
                ref valueOffset,
                out uncompressedSize))
        {
            return "an archive entry has a truncated ZIP64 field";
        }

        if (compressed32 == uint.MaxValue
            && !TryReadUInt64(
                values,
                ref valueOffset,
                out compressedSize))
        {
            return "an archive entry has a truncated ZIP64 field";
        }

        if (localOffset32 == uint.MaxValue
            && !TryReadUInt64(
                values,
                ref valueOffset,
                out localOffset))
        {
            return "an archive entry has a truncated ZIP64 field";
        }

        if (diskStart16 == ushort.MaxValue)
        {
            if (values.Length - valueOffset < 4)
                return "an archive entry has a truncated ZIP64 field";

            diskStart = BinaryPrimitives.ReadUInt32LittleEndian(
                values.Slice(valueOffset, 4));
        }

        return null;
    }

    static bool TryReadUInt64(
        ReadOnlySpan<byte> source,
        ref int offset,
        out ulong value)
    {
        value = 0;
        if (source.Length - offset < 8)
            return false;

        value = BinaryPrimitives.ReadUInt64LittleEndian(
            source.Slice(offset, 8));
        offset += 8;
        return true;
    }

    static ushort ReadUInt16(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(
            source.Slice(offset, 2));

    static uint ReadUInt32(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(
            source.Slice(offset, 4));
}

struct ZipCrc32
{
    static readonly uint[] Table = CreateTable();
    uint _value;

    public ZipCrc32() => _value = uint.MaxValue;

    internal uint Value => ~_value;

    internal void Append(ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            _value = Table[(byte)(_value ^ value)]
                ^ (_value >> 8);
        }
    }

    static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0
                    ? value >> 1
                    : (value >> 1) ^ 0xEDB88320;
            }

            table[index] = value;
        }

        return table;
    }
}
