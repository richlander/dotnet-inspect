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
    public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
    {
        stream = new MemoryStream(_nupkgBytes, writable: false);
        return true;
    }

    /// <inheritdoc />
    public bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

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

        // Materialize under the same order of host memory budget the workspace
        // retained-image path uses (512 MiB). Unbounded CopyTo would let a
        // single declared entry force ~MaxExpandedBytes into RAM before any
        // group budget applies — especially painful on browser/WASM hosts.
        const long maxEntryMaterializationBytes = 512L * 1024 * 1024;
        if (entry.Length < 0 || entry.Length > maxEntryMaterializationBytes)
        {
            stream = null;
            return false;
        }

        using var entryStream = entry.Open();
        if (!TryReadBounded(
                entryStream,
                maxEntryMaterializationBytes,
                out byte[] bytes))
        {
            stream = null;
            return false;
        }

        stream = new MemoryStream(bytes, writable: false);
        return true;
    }

    /// <summary>
    /// Synchronous counterpart of admission's bounded reader for the sync
    /// <see cref="TryOpenEntry"/> contract. Probe-before-grow; null/false when
    /// the stream exceeds <paramref name="maxBytes"/>.
    /// </summary>
    static bool TryReadBounded(Stream source, long maxBytes, out byte[] bytes)
    {
        int max = maxBytes > int.MaxValue ? int.MaxValue : (int)maxBytes;
        int initial = max == 0 ? 0 : Math.Min(81920, max);
        if (source.CanSeek)
        {
            long remaining = source.Length - source.Position;
            if (remaining < 0)
                remaining = 0;
            if (remaining > max)
            {
                bytes = [];
                return false;
            }

            if (remaining > initial)
                initial = (int)remaining;
        }

        byte[] buffer = initial == 0 ? [] : new byte[initial];
        int total = 0;
        Span<byte> probe = stackalloc byte[1];
        while (true)
        {
            if (total == buffer.Length)
            {
                int extra = source.Read(probe);
                if (extra == 0)
                {
                    bytes = total == 0 ? [] : buffer;
                    return true;
                }

                if (total == max)
                {
                    bytes = [];
                    return false;
                }

                int growTo = (int)Math.Min(max, Math.Max((long)buffer.Length * 2, 81920));
                if (growTo <= buffer.Length)
                    growTo = max;
                Array.Resize(ref buffer, growTo);
                buffer[total++] = probe[0];
                continue;
            }

            int read = source.Read(buffer.AsSpan(total, buffer.Length - total));
            if (read == 0)
            {
                if (total == 0)
                {
                    bytes = [];
                    return true;
                }

                if (total != buffer.Length)
                    Array.Resize(ref buffer, total);
                bytes = buffer;
                return true;
            }

            total += read;
        }
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
