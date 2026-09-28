using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace DotnetInspector.Packages;

/// <summary>
/// Retained package content produced by a ranged acquisition: the complete
/// entry directory with declared expanded lengths, and the expanded bytes of
/// the entries the acquisition materialized. Every other entry is
/// directory-only; opening one is a visible
/// <see cref="PackageEntryNotMaterializedException"/>, never a missing entry.
/// There is no retained archive and nothing on disk, so
/// <see cref="RootPath"/> and <see cref="NupkgPath"/> are <c>null</c> and
/// <see cref="TryOpenArchive"/> returns <c>false</c>. It serves the House's
/// pull-based payload reads for its materialized entries, whose bytes the
/// archive reader has already checked against the directory's declared
/// length and CRC (docs/design/package-read-demand.md#document-demand).
/// </summary>
public sealed class RangedPackageContent :
    IPackageContent,
    IPackageContentEntryManifest,
    IPackageHousePayloadSource
{
    private readonly IReadOnlyList<PackageContentEntry> _entries;
    private readonly IReadOnlyDictionary<string, ReadOnlyMemory<byte>> _materialized;
    private readonly PackageContentGenerationIdentity _generationIdentity = new();

    private RangedPackageContent(
        IReadOnlyList<PackageContentEntry> entries,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> materialized,
        string producerKey)
    {
        _entries = entries;
        _materialized = materialized;
        ProducerKey = producerKey;
    }

    /// <summary>
    /// The directory-only view a ranged acquisition offers its entry selector:
    /// every entry is listed, none is readable.
    /// </summary>
    internal static RangedPackageContent CreateDirectory(
        IReadOnlyList<PackageContentEntry> entries,
        string producerKey)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrEmpty(producerKey);
        return new(
            entries,
            new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal),
            producerKey);
    }

    /// <summary>
    /// The retained content: this directory with the given entries
    /// materialized. Every key must name an entry of the directory.
    /// </summary>
    internal RangedPackageContent WithMaterialized(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> materialized)
    {
        ArgumentNullException.ThrowIfNull(materialized);
        foreach (KeyValuePair<string, ReadOnlyMemory<byte>> entry in materialized)
        {
            PackageContentEntry? declared = FindEntry(entry.Key);
            if (declared is null)
            {
                throw new ArgumentException(
                    $"The materialized entry '{entry.Key}' is not in the package directory.",
                    nameof(materialized));
            }
            if (declared.Value.Length != entry.Value.Length)
            {
                throw new ArgumentException(
                    $"The materialized entry '{entry.Key}' differs from its declared length.",
                    nameof(materialized));
            }
        }

        return new(_entries, materialized, ProducerKey);
    }

    /// <inheritdoc />
    public string? RootPath => null;

    /// <inheritdoc />
    public string? NupkgPath => null;

    /// <inheritdoc />
    public bool FromCache => false;

    /// <inheritdoc />
    public string ProducerKey { get; }

    /// <inheritdoc />
    public PackageContentGenerationIdentity GenerationIdentity =>
        _generationIdentity;

    /// <inheritdoc />
    /// <remarks>
    /// Ranged content retains no extracted tree and no archive; each
    /// materialized entry was checked against its declared length and CRC by
    /// the archive reader.
    /// </remarks>
    public bool RequiresArchiveTreeMatch => false;

    /// <summary>The paths whose expanded bytes this content holds.</summary>
    public IReadOnlyCollection<string> MaterializedEntries => _materialized.Keys.ToArray();

    /// <summary>The expanded bytes this content holds across every materialized entry.</summary>
    public long MaterializedBytes =>
        _materialized.Values.Sum(static content => (long)content.Length);

    /// <summary>Whether <paramref name="relativePath"/> names a materialized entry.</summary>
    public bool IsMaterialized(string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        return TryFindMaterialized(relativePath, out _);
    }

    /// <inheritdoc />
    public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream)
    {
        stream = null;
        return false;
    }

    /// <inheritdoc />
    public bool TryOpenEntry(
        string relativePath,
        [NotNullWhen(true)] out Stream? stream) =>
        TryOpenEntry(relativePath, long.MaxValue, out stream);

    /// <inheritdoc />
    public bool TryOpenEntry(
        string relativePath,
        long maxExpandedBytes,
        [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        ArgumentOutOfRangeException.ThrowIfNegative(maxExpandedBytes);
        stream = null;
        if (TryFindMaterialized(relativePath, out ReadOnlyMemory<byte> content))
        {
            if (content.Length > maxExpandedBytes)
            {
                throw new InvalidDataException(
                    "Package entry exceeds the configured byte limit.");
            }

            stream = MemoryMarshal.TryGetArray(content, out ArraySegment<byte> segment)
                ? new MemoryStream(
                    segment.Array!,
                    segment.Offset,
                    segment.Count,
                    writable: false,
                    publiclyVisible: true)
                : new MemoryStream(content.ToArray(), writable: false);
            return true;
        }

        PackageContentEntry? declared = FindEntry(relativePath);
        if (declared is null)
            return false;

        throw new PackageEntryNotMaterializedException(declared.Value.Path);
    }

    /// <summary>
    /// Opens a materialized entry for a House pull read. The House calls this
    /// on the read's first non-empty read, so creating the read does no work;
    /// the stream yields the already-checked expanded bytes as the caller
    /// pulls them. An entry the directory lists but the acquisition did not
    /// read is a visible <see cref="PackageEntryNotMaterializedException"/>.
    /// </summary>
    bool IPackageHousePayloadSource.TryOpenPayloadRead(
        string relativePath,
        long maxExpandedBytes,
        [NotNullWhen(true)] out Stream? stream) =>
        TryOpenEntry(relativePath, maxExpandedBytes, out stream);

    /// <inheritdoc />
    public bool TryGetEntryLength(string relativePath, out long length)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        PackageContentEntry? entry = FindEntry(relativePath);
        length = entry?.Length ?? 0;
        return entry is not null;
    }

    /// <inheritdoc />
    public IReadOnlyList<PackageContentEntry> EnumerateEntriesWithLengths() =>
        _entries;

    /// <inheritdoc />
    public PackageContentEntryScanner CreateEntryScanner() =>
        PackageContentEntryScanner.From(_entries);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateEntries() =>
        _entries.Select(static entry => entry.Path);

    private bool TryFindMaterialized(
        string relativePath,
        out ReadOnlyMemory<byte> content)
    {
        if (_materialized.TryGetValue(relativePath, out content))
            return true;
        foreach (KeyValuePair<string, ReadOnlyMemory<byte>> entry in _materialized)
        {
            if (entry.Key.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
            {
                content = entry.Value;
                return true;
            }
        }

        content = default;
        return false;
    }

    private PackageContentEntry? FindEntry(string relativePath)
    {
        foreach (PackageContentEntry entry in _entries)
        {
            if (entry.Path.Equals(relativePath, StringComparison.Ordinal))
                return entry;
        }
        foreach (PackageContentEntry entry in _entries)
        {
            if (entry.Path.Equals(relativePath, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }
}

/// <summary>
/// A package entry exists in ranged content's directory but its bytes were
/// not materialized by the acquisition that produced the content.
/// </summary>
public sealed class PackageEntryNotMaterializedException : InvalidOperationException
{
    public PackageEntryNotMaterializedException(string entryPath)
        : base(
            $"The package entry '{entryPath}' was not materialized by the ranged acquisition; "
            + "only the entries its realization selected are readable.")
    {
        EntryPath = entryPath;
    }

    /// <summary>The directory path of the entry that was not materialized.</summary>
    public string EntryPath { get; }
}
