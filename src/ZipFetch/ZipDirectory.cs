using System.Text;

namespace ZipFetch;

/// <summary>One entry as the central directory declares it.</summary>
public sealed class ZipEntry
{
    internal ZipEntry(
        byte[] nameBytes,
        ushort flags,
        ushort method,
        uint crc,
        uint compressedLength,
        uint expandedLength,
        ushort extraLength,
        uint localHeaderOffset)
    {
        NameBytes = nameBytes;
        Name = Encoding.UTF8.GetString(nameBytes);
        Flags = flags;
        Method = method;
        Crc = crc;
        CompressedLength = compressedLength;
        ExpandedLength = expandedLength;
        ExtraLength = extraLength;
        LocalHeaderOffset = localHeaderOffset;
    }

    /// <summary>The entry path as UTF-8 text; archive separators are forward slashes.</summary>
    public string Name { get; }

    /// <summary>The entry path exactly as the directory stores it.</summary>
    public ReadOnlyMemory<byte> NameBytes { get; }

    public ushort Flags { get; }

    /// <summary>The compression method: 0 stored, 8 deflate; anything else is unsupported.</summary>
    public ushort Method { get; }

    public uint Crc { get; }

    public uint CompressedLength { get; }

    public uint ExpandedLength { get; }

    /// <summary>The extra-field length the directory declares; the local header's may differ.</summary>
    public ushort ExtraLength { get; }

    public uint LocalHeaderOffset { get; }

    /// <summary>Whether the entry's CRC and sizes are deferred to a data descriptor after the data.</summary>
    public bool HasDataDescriptor => (Flags & (1 << 3)) != 0;
}

/// <summary>
/// The archive's central directory as read from its end: every entry, the
/// directory's own extent, and the archive length derived from the
/// end-of-central-directory record.
/// </summary>
public sealed class ZipDirectory
{
    internal ZipDirectory(
        IReadOnlyList<ZipEntry> entries,
        long archiveLength,
        long directoryOffset,
        long directoryLength,
        ReadOnlyMemory<byte> tail)
    {
        Entries = entries;
        ArchiveLength = archiveLength;
        DirectoryOffset = directoryOffset;
        DirectoryLength = directoryLength;
        Tail = tail;
    }

    /// <summary>
    /// The bytes the directory read fetched from the archive's end, retained so
    /// an entry that lies within them (every entry of a small archive) is
    /// served without another transfer.
    /// </summary>
    internal ReadOnlyMemory<byte> Tail { get; }

    internal long TailStart => ArchiveLength - Tail.Length;

    public IReadOnlyList<ZipEntry> Entries { get; }

    /// <summary>The archive length derived from the end-of-central-directory record.</summary>
    public long ArchiveLength { get; }

    /// <summary>The offset of the central directory; no entry's data lies at or beyond it.</summary>
    public long DirectoryOffset { get; }

    public long DirectoryLength { get; }

    /// <summary>Finds an entry by its exact archive path (ordinal comparison).</summary>
    public ZipEntry? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (ZipEntry entry in Entries)
        {
            if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                return entry;
        }

        return null;
    }
}
