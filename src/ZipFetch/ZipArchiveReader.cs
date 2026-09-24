using System.Buffers.Binary;
using System.IO.Compression;
using BinaryFetch;

namespace ZipFetch;

/// <summary>
/// Reads a ZIP archive's central directory and individual entries over a
/// <see cref="RandomAccessSource"/>, remote or local, under caller-supplied
/// bounds. Structure failures are <see cref="ZipReadException"/>s; source
/// failures propagate as the source raises them.
/// </summary>
/// <remarks>
/// <para>
/// The directory read fetches the archive's tail (the end-of-central-directory
/// record plus the largest comment the format allows), derives the archive
/// length from the record, confirms it with the source, and reads the central
/// directory from the tail or with one more ranged read. Caps are applied
/// before the directory is parsed.
/// </para>
/// <para>
/// An entry read fetches the local header and compressed data in one ranged
/// read sized from the directory entry plus the configured slack, clamped to
/// the directory offset, with exactly one follow-up when the local header
/// shows a longer extra field than the directory declared. Expansion proceeds
/// up to the caller's bound; crossing it is <see cref="ZipReadFailure.OverBound"/>,
/// and a finished expansion whose length or CRC differs from the directory's
/// declaration is <see cref="ZipReadFailure.Malformed"/>.
/// </para>
/// </remarks>
public static class ZipArchiveReader
{
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint CentralDirectoryEntrySignature = 0x02014b50;
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const int EndOfCentralDirectoryLength = 22;
    private const int CentralDirectoryEntryLength = 46;
    private const int LocalFileHeaderLength = 30;
    private const int MaximumZipCommentLength = ushort.MaxValue;
    private const int TailLength = EndOfCentralDirectoryLength + MaximumZipCommentLength;

    // Bits 1 and 2 (deflate options), 3 (data descriptor), 11 (UTF-8 names).
    private const ushort SupportedFlags = (1 << 1) | (1 << 2) | (1 << 3) | (1 << 11);
    private const ushort CorrespondingFlags = (1 << 3) | (1 << 11);

    /// <summary>Reads the central directory and derives the archive length.</summary>
    public static async Task<ZipDirectory> ReadDirectoryAsync(
        RandomAccessSource source,
        ZipReadLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(limits);

        ReadOnlyMemory<byte> tail = await source
            .ReadTailAsync(TailLength, cancellationToken)
            .ConfigureAwait(false);
        if (tail.Length < EndOfCentralDirectoryLength)
            throw Malformed("The archive has no end-of-central-directory record.");

        ReadOnlySpan<byte> tailSpan = tail.Span;
        for (int offset = tail.Length - EndOfCentralDirectoryLength; offset >= 0; offset--)
        {
            ReadOnlySpan<byte> record = tailSpan[offset..];
            if (BinaryPrimitives.ReadUInt32LittleEndian(record) != EndOfCentralDirectorySignature)
                continue;

            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(record[20..]);
            if (offset + EndOfCentralDirectoryLength + commentLength != tail.Length)
                continue;

            ushort disk = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
            ushort centralDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
            ushort entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
            ushort entryCount = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            uint directoryLength = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
            uint directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);

            // Caps first, so an over-cap declaration is refused as a bound
            // whether or not it is also a Zip64 sentinel.
            if (entryCount > limits.MaxEntryCount || directoryLength > limits.MaxDirectoryBytes)
                throw OverBound("The archive's central directory exceeds the caller's caps.");
            if (entryCount == ushort.MaxValue
                || directoryLength == uint.MaxValue
                || directoryOffset == uint.MaxValue)
            {
                throw Unsupported("The archive uses Zip64, which the reader does not support.");
            }

            if (disk != 0 || centralDisk != 0 || entriesOnDisk != entryCount)
                throw Malformed("The archive spans disks or declares inconsistent entry counts.");

            // The record sits immediately after the directory, so the archive
            // length follows from the record alone. The source confirms it
            // first, so a length the source can refute is malformed rather
            // than over a bound; the bound applies to a confirmed length.
            long archiveLength = (long)directoryOffset + directoryLength
                + EndOfCentralDirectoryLength + commentLength;
            if (archiveLength < tail.Length)
                throw Malformed("The archive declares a length shorter than the bytes read from its end.");
            source.ConfirmLength(archiveLength);
            if (archiveLength > limits.MaxArchiveBytes)
                throw OverBound("The archive exceeds the caller's archive bound.");

            byte[] directoryBytes;
            long tailStart = archiveLength - tail.Length;
            if (directoryOffset >= tailStart)
            {
                directoryBytes = tailSpan.Slice(
                    checked((int)(directoryOffset - tailStart)),
                    checked((int)directoryLength)).ToArray();
            }
            else
            {
                directoryBytes = new byte[directoryLength];
                await source.ReadRangeAsync(directoryOffset, directoryBytes, cancellationToken)
                    .ConfigureAwait(false);
            }

            return new ZipDirectory(
                ParseEntries(directoryBytes, entryCount),
                archiveLength,
                directoryOffset,
                directoryLength,
                tail);
        }

        throw Malformed("The archive has no valid end-of-central-directory record.");
    }

    /// <summary>
    /// Reads and expands one entry. <paramref name="maxExpandedBytes"/> bounds
    /// the expansion; when <see langword="null"/>, the limits' per-entry bound
    /// applies.
    /// </summary>
    public static async Task<byte[]> ReadEntryAsync(
        RandomAccessSource source,
        ZipDirectory directory,
        ZipEntry entry,
        ZipReadLimits limits,
        long? maxExpandedBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(limits);
        long expandedBound = maxExpandedBytes ?? limits.MaxExpandedBytes;
        ArgumentOutOfRangeException.ThrowIfNegative(expandedBound);

        (_, long firstEnd) = Preflight(directory, entry, limits, expandedBound);
        long firstLength = firstEnd - entry.LocalHeaderOffset;
        var first = new byte[checked((int)firstLength)];
        await ReadAsync(source, directory, entry.LocalHeaderOffset, first, cancellationToken)
            .ConfigureAwait(false);

        LocalHeader header = ParseLocalHeader(first);
        if (HasUnsupportedFlags(header.Flags, header.Method))
            throw Unsupported("The entry's local header uses flags the reader does not support.");
        if ((header.Flags & CorrespondingFlags) != (entry.Flags & CorrespondingFlags)
            || header.Method != entry.Method)
        {
            throw Malformed("The entry's local header disagrees with the central directory.");
        }

        ushort localNameLength = header.NameLength;
        ushort localExtraLength = header.ExtraLength;
        long dataOffset = (long)entry.LocalHeaderOffset
            + LocalFileHeaderLength
            + localNameLength
            + localExtraLength;
        long dataEnd = dataOffset + entry.CompressedLength;
        if (dataEnd > directory.DirectoryOffset)
            throw Malformed("The entry's data lies past the central directory.");
        if (LocalFileHeaderLength + localNameLength > first.Length)
        {
            // The name did not fit the first read; fetch it with the data.
            first = await ExtendAsync(source, directory, entry.LocalHeaderOffset, first, dataEnd, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!first.AsSpan(LocalFileHeaderLength, localNameLength).SequenceEqual(entry.NameBytes.Span))
            throw Malformed("The entry's local name disagrees with the central directory.");
        if (!entry.HasDataDescriptor
            && (header.Crc != entry.Crc
                || header.CompressedLength != entry.CompressedLength
                || header.ExpandedLength != entry.ExpandedLength))
        {
            throw Malformed("The entry's local declaration disagrees with the central directory.");
        }

        long fetchedEnd = entry.LocalHeaderOffset + first.Length;
        if (dataEnd > fetchedEnd)
        {
            // Exactly one follow-up: the local extra field was longer than the
            // directory declared.
            first = await ExtendAsync(source, directory, entry.LocalHeaderOffset, first, dataEnd, cancellationToken)
                .ConfigureAwait(false);
        }

        int dataStart = checked((int)(dataOffset - entry.LocalHeaderOffset));
        var compressed = new ReadOnlyMemory<byte>(first, dataStart, checked((int)entry.CompressedLength));
        byte[] content = await ExpandAsync(compressed, entry.Method, expandedBound, cancellationToken)
            .ConfigureAwait(false);
        if (content.LongLength != entry.ExpandedLength)
            throw Malformed("The entry expanded to a length other than it declares.");
        if (Crc32.Compute(content) != entry.Crc)
            throw Malformed("The entry's content does not match its CRC.");
        return content;
    }

    /// <summary>
    /// Reads and expands several entries with as few ranged requests as their
    /// placement allows. Every entry is checked against the directory and the
    /// bounds before any transfer; entries whose request extents lie within
    /// <see cref="ZipReadLimits.EntryMergeGap"/> of each other share one
    /// request; up to <see cref="ZipReadLimits.MaxConcurrentReads"/> requests
    /// are in flight at once. Each entry is then read exactly as
    /// <see cref="ReadEntryAsync"/> reads it, over the fetched bytes, so a
    /// longer local extra field still costs exactly one follow-up.
    /// </summary>
    /// <param name="maxTotalExpandedBytes">
    /// The bound on the entries' expansion together; each entry is also
    /// bounded by <see cref="ZipReadLimits.MaxExpandedBytes"/>. A declared
    /// total above it is <see cref="ZipReadFailure.OverBound"/> before any
    /// transfer.
    /// </param>
    /// <returns>The expanded entries, in the order requested.</returns>
    public static async Task<IReadOnlyList<byte[]>> ReadEntriesAsync(
        RandomAccessSource source,
        ZipDirectory directory,
        IReadOnlyList<ZipEntry> entries,
        ZipReadLimits limits,
        long? maxTotalExpandedBytes = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(limits);
        long totalBound = maxTotalExpandedBytes ?? long.MaxValue;
        ArgumentOutOfRangeException.ThrowIfNegative(totalBound);
        if (entries.Count == 0)
            return [];

        var extents = new (long Start, long End)[entries.Count];
        long declaredTotal = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ZipEntry entry = entries[i] ?? throw new ArgumentException(
                "The entry list contains a null entry.",
                nameof(entries));
            extents[i] = Preflight(directory, entry, limits, limits.MaxExpandedBytes);
            declaredTotal += entry.ExpandedLength;
            if (declaredTotal > totalBound)
                throw OverBound("The entries' declared expansion exceeds the caller's total bound.");
        }

        List<(long Start, long End)> spans = PlanSpans(extents, limits.EntryMergeGap);
        var buffers = new byte[spans.Count][];
        using (var gate = new SemaphoreSlim(limits.MaxConcurrentReads))
        {
            var reads = new Task[spans.Count];
            for (int i = 0; i < spans.Count; i++)
            {
                int index = i;
                reads[i] = FetchSpanAsync(index);
            }

            await Task.WhenAll(reads).ConfigureAwait(false);

            async Task FetchSpanAsync(int index)
            {
                (long start, long end) = spans[index];
                var buffer = new byte[checked((int)(end - start))];
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await ReadAsync(source, directory, start, buffer, cancellationToken)
                        .ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }

                buffers[index] = buffer;
            }
        }

        var fetched = new FetchedSpansSource(source, spans, buffers);
        var results = new byte[entries.Count][];
        long remaining = totalBound;
        for (int i = 0; i < entries.Count; i++)
        {
            byte[] content = await ReadEntryAsync(
                fetched,
                directory,
                entries[i],
                limits,
                Math.Min(limits.MaxExpandedBytes, remaining),
                cancellationToken).ConfigureAwait(false);
            remaining -= content.LongLength;
            results[i] = content;
        }

        return results;
    }

    /// <summary>The largest single request a batch read plans, so one span stays one array.</summary>
    private const long MaxSpanBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The entry's first-request extent, after every check that must pass
    /// before any transfer.
    /// </summary>
    private static (long Start, long End) Preflight(
        ZipDirectory directory,
        ZipEntry entry,
        ZipReadLimits limits,
        long expandedBound)
    {
        if (HasUnsupportedFlags(entry.Flags, entry.Method))
            throw Unsupported("The entry uses encryption or flags the reader does not support.");
        if (entry.Method is not 0 and not 8)
            throw Unsupported("The entry uses a compression method other than stored or deflate.");
        if (entry.ExpandedLength > expandedBound)
            throw OverBound("The entry's declared expansion exceeds the caller's bound.");

        long declaredExtent = (long)entry.LocalHeaderOffset
            + LocalFileHeaderLength
            + entry.NameBytes.Length
            + entry.ExtraLength
            + entry.CompressedLength;
        if (declaredExtent > directory.DirectoryOffset)
            throw Malformed("The entry's declared extent lies past the central directory.");

        // First read: the declared extent plus slack, clamped to the directory.
        long end = Math.Min(
            declaredExtent + limits.EntryReadSlack,
            directory.DirectoryOffset);
        return (entry.LocalHeaderOffset, end);
    }

    /// <summary>
    /// Joins request extents, in archive order, whose gap is at most
    /// <paramref name="gap"/> bytes, up to <see cref="MaxSpanBytes"/> a span.
    /// </summary>
    private static List<(long Start, long End)> PlanSpans(
        (long Start, long End)[] extents,
        int gap)
    {
        var ordered = extents.OrderBy(extent => extent.Start).ToArray();
        var spans = new List<(long Start, long End)>();
        foreach ((long start, long end) in ordered)
        {
            if (spans.Count > 0)
            {
                (long spanStart, long spanEnd) = spans[^1];
                long joinedEnd = Math.Max(spanEnd, end);
                if (start - spanEnd <= gap && joinedEnd - spanStart <= MaxSpanBytes)
                {
                    spans[^1] = (spanStart, joinedEnd);
                    continue;
                }
            }

            spans.Add((start, end));
        }

        return spans;
    }

    /// <summary>
    /// Serves reads that lie within a fetched span from its bytes and sends
    /// every other read, such as a follow-up for a longer local extra field,
    /// to the underlying source.
    /// </summary>
    private sealed class FetchedSpansSource(
        RandomAccessSource inner,
        List<(long Start, long End)> spans,
        byte[][] buffers) : RandomAccessSource
    {
        public override ValueTask<ReadOnlyMemory<byte>> ReadTailAsync(
            int maxLength,
            CancellationToken cancellationToken) =>
            inner.ReadTailAsync(maxLength, cancellationToken);

        public override ValueTask ReadRangeAsync(
            long offset,
            Memory<byte> destination,
            CancellationToken cancellationToken)
        {
            long end = offset + destination.Length;
            for (int i = 0; i < spans.Count; i++)
            {
                (long start, long spanEnd) = spans[i];
                if (offset >= start && end <= spanEnd)
                {
                    buffers[i].AsMemory(checked((int)(offset - start)), destination.Length)
                        .CopyTo(destination);
                    return ValueTask.CompletedTask;
                }
            }

            return inner.ReadRangeAsync(offset, destination, cancellationToken);
        }
    }

    private static async Task<byte[]> ExtendAsync(
        RandomAccessSource source,
        ZipDirectory directory,
        long start,
        byte[] fetched,
        long requiredEnd,
        CancellationToken cancellationToken)
    {
        long fetchedEnd = start + fetched.Length;
        var extended = new byte[checked((int)(requiredEnd - start))];
        fetched.CopyTo(extended, 0);
        await ReadAsync(
            source,
            directory,
            fetchedEnd,
            extended.AsMemory(fetched.Length),
            cancellationToken).ConfigureAwait(false);
        return extended;
    }

    /// <summary>
    /// Reads archive bytes, from the retained tail when the range lies within
    /// it (so a small archive never needs a second transfer) and from the
    /// source otherwise.
    /// </summary>
    private static ValueTask ReadAsync(
        RandomAccessSource source,
        ZipDirectory directory,
        long offset,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        long tailStart = directory.TailStart;
        if (offset >= tailStart && offset + destination.Length <= directory.ArchiveLength)
        {
            directory.Tail.Slice(checked((int)(offset - tailStart)), destination.Length)
                .CopyTo(destination);
            return ValueTask.CompletedTask;
        }

        return source.ReadRangeAsync(offset, destination, cancellationToken);
    }

    private static IReadOnlyList<ZipEntry> ParseEntries(byte[] bytes, int entryCount)
    {
        var entries = new List<ZipEntry>(entryCount);
        int offset = 0;
        for (int index = 0; index < entryCount; index++)
        {
            if (offset > bytes.Length - CentralDirectoryEntryLength
                || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset))
                    != CentralDirectoryEntrySignature)
            {
                throw Malformed("The archive's central directory is malformed.");
            }

            ReadOnlySpan<byte> record = bytes.AsSpan(offset);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            uint compressedLength = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
            uint expandedLength = BinaryPrimitives.ReadUInt32LittleEndian(record[24..]);
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(record[28..]);
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(record[30..]);
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(record[32..]);
            ushort disk = BinaryPrimitives.ReadUInt16LittleEndian(record[34..]);
            uint localHeaderOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[42..]);
            int recordLength = CentralDirectoryEntryLength + nameLength + extraLength + commentLength;
            if (recordLength > bytes.Length - offset || disk != 0)
                throw Malformed("The archive's central-directory entry is inconsistent.");
            if (compressedLength == uint.MaxValue
                || expandedLength == uint.MaxValue
                || localHeaderOffset == uint.MaxValue)
            {
                throw Unsupported("The archive uses Zip64 entry fields, which the reader does not support.");
            }

            entries.Add(new ZipEntry(
                bytes.AsSpan(offset + CentralDirectoryEntryLength, nameLength).ToArray(),
                flags,
                method,
                crc,
                compressedLength,
                expandedLength,
                extraLength,
                localHeaderOffset));
            offset += recordLength;
        }

        if (offset != bytes.Length)
            throw Malformed("The archive's central-directory extent is inconsistent.");
        return entries;
    }

    private readonly record struct LocalHeader(
        ushort Flags,
        ushort Method,
        uint Crc,
        uint CompressedLength,
        uint ExpandedLength,
        ushort NameLength,
        ushort ExtraLength);

    private static LocalHeader ParseLocalHeader(byte[] bytes)
    {
        ReadOnlySpan<byte> header = bytes.AsSpan(0, LocalFileHeaderLength);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LocalFileHeaderSignature)
            throw Malformed("The entry's local header is missing.");
        return new LocalHeader(
            BinaryPrimitives.ReadUInt16LittleEndian(header[6..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[14..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[18..]),
            BinaryPrimitives.ReadUInt32LittleEndian(header[22..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[26..]),
            BinaryPrimitives.ReadUInt16LittleEndian(header[28..]));
    }

    private static bool HasUnsupportedFlags(ushort flags, ushort method) =>
        (flags & ~SupportedFlags) != 0
        || method != 8 && (flags & ((1 << 1) | (1 << 2))) != 0;

    private static async Task<byte[]> ExpandAsync(
        ReadOnlyMemory<byte> compressed,
        ushort method,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (method == 0)
        {
            if (compressed.Length > maximumBytes)
                throw OverBound("The entry's expansion crossed the caller's bound.");
            return compressed.ToArray();
        }

        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var inflate = new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            int read;
            try
            {
                read = await inflate.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException exception)
            {
                throw Malformed("The entry's deflate stream is malformed.", exception);
            }

            if (read == 0)
                break;
            if (output.Length + read > maximumBytes)
                throw OverBound("The entry's expansion crossed the caller's bound.");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static ZipReadException Malformed(string message, Exception? inner = null) =>
        new(ZipReadFailure.Malformed, message, inner);

    private static ZipReadException OverBound(string message) =>
        new(ZipReadFailure.OverBound, message);

    private static ZipReadException Unsupported(string message) =>
        new(ZipReadFailure.Unsupported, message);
}
