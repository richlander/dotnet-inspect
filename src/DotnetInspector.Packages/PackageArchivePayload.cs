using System.Buffers;
using System.IO.Compression;

namespace DotnetInspector.Packages;

internal sealed class PackageArchivePayload
{
    private const int CopyBufferSize = 81920;

    private readonly byte[] _bytes;
    private readonly IReadOnlyList<PackageArchiveEntry> _entries;
    private readonly Dictionary<string, PackageArchiveEntry> _entriesByPath;

    internal PackageArchivePayload(
        byte[] bytes,
        IReadOnlyList<PackageArchiveEntry> entries,
        long declaredExpandedBytes,
        int uniqueDirectoryCount,
        PackagePayloadLimits limits)
    {
        _bytes = bytes;
        _entries = entries;
        _entriesByPath = entries
            .Where(entry => !entry.IsDirectory)
            .ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        DeclaredExpandedBytes = declaredExpandedBytes;
        UniqueDirectoryCount = uniqueDirectoryCount;
        Limits = limits;
    }

    internal int ArchiveLength => _bytes.Length;

    internal byte[] OwnedBytes => _bytes;

    internal int EntryCount => _entries.Count;

    internal long DeclaredExpandedBytes { get; }

    internal int UniqueDirectoryCount { get; }

    internal PackagePayloadLimits Limits { get; }

    internal bool Satisfies(PackagePayloadLimits limits) =>
        ArchiveLength <= limits.MaxArchiveBytes
        && DeclaredExpandedBytes <= limits.MaxExpandedBytes
        && EntryCount <= limits.MaxEntryCount
        && UniqueDirectoryCount <= limits.MaxUniqueDirectories;

    internal bool References(ReadOnlyMemory<byte> bytes) =>
        new ReadOnlyMemory<byte>(_bytes).Equals(bytes);

    internal byte[] CopyBytes() => _bytes.ToArray();

    internal Stream OpenArchive() =>
        new MemoryStream(_bytes, writable: false);

    internal IReadOnlyList<PackageContentEntry> GetEntries() =>
        _entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => new PackageContentEntry(
                entry.Path,
                checked((long)entry.UncompressedSize)))
            .ToList()
            .AsReadOnly();

    internal bool TryOpenEntry(
        string relativePath,
        long maxExpandedBytes,
        out Stream? stream)
    {
        if (!_entriesByPath.TryGetValue(relativePath, out PackageArchiveEntry entry))
        {
            stream = null;
            return false;
        }

        if (entry.UncompressedSize > (ulong)maxExpandedBytes
            || entry.UncompressedSize > (ulong)Array.MaxLength)
        {
            throw new InvalidDataException(
                "Package entry exceeds the configured byte limit.");
        }

        byte[] bytes = new byte[(int)entry.UncompressedSize];
        using var output = new MemoryStream(bytes, writable: true);
        CopyEntryTo(
            entry,
            output,
            maxExpandedBytes,
            CancellationToken.None);
        stream = new MemoryStream(
            bytes,
            index: 0,
            count: bytes.Length,
            writable: false,
            publiclyVisible: true);
        return true;
    }

    internal void ExtractToDirectory(
        string root,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        long expandedBytes = 0;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            foreach (PackageArchiveEntry entry in _entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = entry.IsDirectory
                    ? entry.Path[..^1]
                    : entry.Path;
                string destination = StorePath.ResolveUnderRoot(root, relativePath);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(destination);
                    CopyEntryTo(
                        entry,
                        Stream.Null,
                        maxExpandedBytes: 0,
                        buffer,
                        cancellationToken);
                    continue;
                }

                string? directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                long remaining = Limits.MaxExpandedBytes - expandedBytes;
                using FileStream output = new(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None);
                long written = CopyEntryTo(
                    entry,
                    output,
                    remaining,
                    buffer,
                    cancellationToken);
                expandedBytes += written;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private long CopyEntryTo(
        PackageArchiveEntry entry,
        Stream destination,
        long maxExpandedBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            return CopyEntryTo(
                entry,
                destination,
                maxExpandedBytes,
                buffer,
                cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private long CopyEntryTo(
        PackageArchiveEntry entry,
        Stream destination,
        long maxExpandedBytes,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        using Stream compressed = new MemoryStream(
            _bytes,
            entry.DataOffset,
            checked((int)entry.CompressedSize),
            writable: false);
        using Stream content = entry.CompressionMethod switch
        {
            0 => compressed,
            8 => new DeflateStream(compressed, CompressionMode.Decompress),
            _ => throw new InvalidDataException(
                "Package entry uses an unsupported compression method."),
        };

        var crc = new ZipCrc32();
        long expandedBytes = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = content.Read(buffer, 0, CopyBufferSize);
            if (read == 0)
                break;

            if (read > maxExpandedBytes - expandedBytes
                || (ulong)read > entry.UncompressedSize - (ulong)expandedBytes)
            {
                throw new InvalidDataException(
                    "Package entry exceeds its declared or configured byte limit.");
            }

            destination.Write(buffer, 0, read);
            crc.Append(buffer.AsSpan(0, read));
            expandedBytes += read;
        }

        if ((ulong)expandedBytes != entry.UncompressedSize
            || crc.Value != entry.Crc32)
        {
            throw new InvalidDataException(
                "Package entry does not match its declared size or checksum.");
        }

        return expandedBytes;
    }
}

internal readonly record struct PackageArchiveEntry(
    string Path,
    bool IsDirectory,
    ushort CompressionMethod,
    uint Crc32,
    ulong CompressedSize,
    ulong UncompressedSize,
    int DataOffset);
