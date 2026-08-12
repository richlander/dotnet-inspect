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
        => TryOpenEntry(relativePath, long.MaxValue, out stream);

    /// <inheritdoc />
    public bool TryOpenEntry(
        string relativePath,
        long maximumBytes,
        [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        using var archive = OpenArchive();
        // Zip entries are stored with '/' separators; match by full name.
        // Prefer an exact match, then fall back to a case-insensitive one so a
        // WASM host mirrors the case-insensitive filesystem lookup on Windows
        // and macOS rather than the strict-ordinal ZipArchive.GetEntry default.
        var entry = archive.GetEntry(relativePath)
            ?? archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, relativePath, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            stream = null;
            return false;
        }
        if (entry.Length > maximumBytes)
        {
            throw new InvalidDataException(
                $"Package entry exceeds the {maximumBytes}-byte limit.");
        }

        // Copy into memory so the returned stream outlives the archive.
        using var entryStream = entry.Open();
        var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;
        while (true)
        {
            int read = entryStream.Read(chunk);
            if (read == 0)
                break;
            total += read;
            if (total > maximumBytes)
            {
                buffer.Dispose();
                throw new InvalidDataException(
                    $"Package entry exceeds the {maximumBytes}-byte limit.");
            }
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        stream = buffer;
        return true;
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
}
