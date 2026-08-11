using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace DotnetInspector.Packages;

/// <summary>
/// In-memory <see cref="IPackageContent"/> that keeps the nupkg bytes and reads
/// entries from an in-memory <see cref="ZipArchive"/>. For hosts without a
/// persistent filesystem (browser/WASM) and for tests. <see cref="RootPath"/>
/// and <see cref="NupkgPath"/> are always <c>null</c> because nothing is
/// materialized on disk.
/// </summary>
public sealed class InMemoryPackageContent : IPackageContent
{
    private readonly byte[] _nupkgBytes;

    public InMemoryPackageContent(
        byte[] nupkgBytes,
        bool fromCache,
        string producerKey)
    {
        ArgumentNullException.ThrowIfNull(nupkgBytes);
        ArgumentException.ThrowIfNullOrEmpty(producerKey);
        _nupkgBytes = nupkgBytes;
        FromCache = fromCache;
        ProducerKey = producerKey;
    }

    /// <summary>The raw nupkg bytes backing this content.</summary>
    public ReadOnlyMemory<byte> NupkgBytes => _nupkgBytes;

    /// <inheritdoc />
    public string? RootPath => null;

    /// <inheritdoc />
    public string? NupkgPath => null;

    /// <inheritdoc />
    public bool FromCache { get; }

    /// <inheritdoc />
    public string ProducerKey { get; }

    /// <inheritdoc />
    public bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream)
        => TryOpenEntry(relativePath, Array.MaxLength, out stream);

    /// <summary>
    /// Opens one expanded entry only when its declared and observed lengths fit within
    /// <paramref name="maxExpandedBytes"/>.
    /// </summary>
    public bool TryOpenEntry(
        string relativePath,
        long maxExpandedBytes,
        [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(maxExpandedBytes);

        using var archive = OpenArchive();
        ZipArchiveEntry? entry = FindEntry(archive, relativePath);
        if (entry is null)
        {
            stream = null;
            return false;
        }

        if (entry.Length > maxExpandedBytes || entry.Length > Array.MaxLength)
            throw new InvalidDataException("Package entry exceeds the configured byte limit.");

        byte[] bytes = GC.AllocateUninitializedArray<byte>((int)entry.Length);
        using var entryStream = entry.Open();
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = entryStream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
                throw new InvalidDataException("Package entry ended before its declared length.");
            offset += read;
        }
        if (entryStream.ReadByte() != -1)
            throw new InvalidDataException("Package entry exceeds its declared length.");

        stream = new MemoryStream(
            bytes,
            index: 0,
            count: bytes.Length,
            writable: false,
            publiclyVisible: true);
        return true;
    }

    /// <summary>Gets an entry's declared expanded length without expanding its body.</summary>
    public bool TryGetEntryLength(string relativePath, out long length)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        using var archive = OpenArchive();
        ZipArchiveEntry? entry = FindEntry(archive, relativePath);
        length = entry?.Length ?? 0;
        return entry is not null;
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateEntries()
    {
        using var archive = OpenArchive();
        // Materialize before the archive is disposed. Directory placeholder
        // entries (trailing '/') carry no content and are skipped.
        return archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => entry.FullName)
            .ToList();
    }

    private ZipArchive OpenArchive()
        => new(new MemoryStream(_nupkgBytes, writable: false), ZipArchiveMode.Read);

    static ZipArchiveEntry? FindEntry(ZipArchive archive, string relativePath)
        // Zip entries are stored with '/' separators. Prefer an exact match, then mirror the
        // case-insensitive filesystem lookup on Windows and macOS.
        => archive.GetEntry(relativePath)
            ?? archive.Entries.FirstOrDefault(entry =>
                string.Equals(
                    entry.FullName,
                    relativePath,
                    StringComparison.OrdinalIgnoreCase));
}
