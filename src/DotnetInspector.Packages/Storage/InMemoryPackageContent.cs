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

    public InMemoryPackageContent(byte[] nupkgBytes, bool fromCache)
    {
        ArgumentNullException.ThrowIfNull(nupkgBytes);
        _nupkgBytes = nupkgBytes;
        FromCache = fromCache;
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
    public bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        using var archive = OpenArchive();
        // Zip entries are stored with '/' separators; match by full name.
        var entry = archive.GetEntry(relativePath);
        if (entry is null)
        {
            stream = null;
            return false;
        }

        // Copy into memory so the returned stream outlives the archive.
        using var entryStream = entry.Open();
        var buffer = new MemoryStream();
        entryStream.CopyTo(buffer);
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
