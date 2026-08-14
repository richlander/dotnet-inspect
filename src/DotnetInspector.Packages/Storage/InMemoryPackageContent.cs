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
public sealed class InMemoryPackageContent : IPackageContent, IPackageContentEntryManifest
{
    const long MaxEntryMaterializationBytes = 512L * 1024 * 1024;

    private readonly byte[] _nupkgBytes;
    private readonly Lazy<IReadOnlyList<PackageContentEntry>> _entries;

    public InMemoryPackageContent(
        byte[] nupkgBytes,
        bool fromCache,
        string producerKey)
    {
        ArgumentNullException.ThrowIfNull(nupkgBytes);
        ArgumentException.ThrowIfNullOrEmpty(producerKey);
        _nupkgBytes = nupkgBytes;
        _entries = new(ReadEntries);
        FromCache = fromCache;
        ProducerKey = producerKey;
    }

    /// <summary>The raw nupkg bytes backing this content.</summary>
    public ReadOnlyMemory<byte> NupkgBytes => _nupkgBytes;

    internal byte[] RetainedArchive => _nupkgBytes;

    /// <inheritdoc />
    public string? RootPath => null;

    /// <inheritdoc />
    public string? NupkgPath => null;

    /// <inheritdoc />
    public bool FromCache { get; }

    /// <inheritdoc />
    public string ProducerKey { get; }

    /// <inheritdoc />
    /// <remarks>
    /// In-memory content has no extracted tree; archive validation alone
    /// admits the payload.
    /// </remarks>
    public bool RequiresArchiveTreeMatch => false;

    /// <inheritdoc />
    public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
    {
        stream = new MemoryStream(_nupkgBytes, writable: false);
        return true;
    }

    /// <inheritdoc />
    public bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream)
        => TryOpenEntry(relativePath, MaxEntryMaterializationBytes, out stream);

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

        long effectiveLimit = Math.Min(
            maxExpandedBytes,
            MaxEntryMaterializationBytes);
        if (entry.Length < 0
            || entry.Length > effectiveLimit
            || entry.Length > Array.MaxLength)
        {
            throw new InvalidDataException("Package entry exceeds the configured byte limit.");
        }

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
        PackageContentEntry? entry = FindEntry(
            EnumerateEntriesWithLengths(),
            relativePath);
        length = entry?.Length ?? 0;
        return entry is not null;
    }

    /// <summary>
    /// Returns one cached snapshot of package entry paths and declared expanded lengths.
    /// Directory placeholders are omitted.
    /// </summary>
    public IReadOnlyList<PackageContentEntry> EnumerateEntriesWithLengths()
        => _entries.Value;
    /// <inheritdoc />
    public IEnumerable<string> EnumerateEntries() =>
        EnumerateEntriesWithLengths().Select(entry => entry.Path);

    private IReadOnlyList<PackageContentEntry> ReadEntries()
    {
        using var archive = OpenArchive();
        return archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => new PackageContentEntry(entry.FullName, entry.Length))
            .ToList()
            .AsReadOnly();
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

    static PackageContentEntry? FindEntry(
        IReadOnlyList<PackageContentEntry> entries,
        string relativePath)
    {
        foreach (PackageContentEntry entry in entries)
        {
            if (entry.Path.Equals(relativePath, StringComparison.Ordinal))
                return entry;
        }
        foreach (PackageContentEntry entry in entries)
        {
            if (entry.Path.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }
}
