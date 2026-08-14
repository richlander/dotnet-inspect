using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Packages;

/// <summary>
/// Host-neutral view over the materialized contents of a NuGet package.
/// </summary>
/// <remarks>
/// A filesystem-backed content (<see cref="FileSystemPackageContent"/>) exposes
/// an extracted directory via <see cref="RootPath"/>, so the ~40 desktop
/// consumers that open files by path keep working unchanged. An in-memory
/// content (<see cref="InMemoryPackageContent"/>) keeps the nupkg bytes and
/// returns <c>null</c> for <see cref="RootPath"/>/<see cref="NupkgPath"/>; a
/// browser/WASM host reads entries as byte streams via
/// <see cref="TryOpenEntry"/> / <see cref="EnumerateEntries"/>.
/// </remarks>
public interface IPackageContent
{
    /// <summary>
    /// Filesystem directory holding the extracted package, or <c>null</c> when
    /// the content is not materialized on disk.
    /// </summary>
    string? RootPath { get; }

    /// <summary>
    /// Filesystem path to the retained <c>.nupkg</c>, or <c>null</c> when the
    /// archive is not persisted as a file.
    /// </summary>
    string? NupkgPath { get; }

    /// <summary>
    /// True when this content was served from a pre-existing cache entry rather
    /// than freshly downloaded and committed.
    /// </summary>
    bool FromCache { get; }

    /// <summary>
    /// Canonical identity of the source that produced this package payload.
    /// </summary>
    string ProducerKey { get; }

    /// <summary>
    /// When true, admission must require archive/tree matching for any
    /// extracted root paired with a retained archive. This is immutable store
    /// provenance (product-owned app-cache commit), not a re-read of the
    /// commit marker file — so concurrent marker deletion cannot downgrade
    /// product-owned content to foreign walk-only gates.
    /// </summary>
    bool RequiresArchiveTreeMatch { get; }

    /// <summary>
    /// Opens the retained package archive so a caller can apply its current
    /// admission limits. Returns <c>false</c> when the cache entry has no
    /// retained archive; admission then falls back to measuring the extracted
    /// tree when <see cref="RootPath"/> is present.
    /// </summary>
    bool TryOpenArchive([NotNullWhen(true)] out Stream? stream);

    /// <summary>
    /// Opens a package entry addressed by its <c>/</c>-separated, package-root
    /// relative path (for example <c>lib/net8.0/Foo.dll</c>). Returns
    /// <c>false</c> when no such entry exists.
    /// </summary>
    bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream);

    /// <summary>
    /// Opens a package entry only when the implementation can keep its expanded content within
    /// <paramref name="maxExpandedBytes"/>. Implementations should reject before expansion when
    /// they know the declared length. The default preserves compatibility for implementations
    /// that cannot preflight length; callers must still bound observed reads from the returned
    /// stream.
    /// </summary>
    bool TryOpenEntry(
        string relativePath,
        long maxExpandedBytes,
        [NotNullWhen(true)] out Stream? stream)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxExpandedBytes);
        return TryOpenEntry(relativePath, out stream);
    }

    /// <summary>
    /// Enumerates the <c>/</c>-separated, package-root relative paths of every
    /// entry in the package.
    /// </summary>
    IEnumerable<string> EnumerateEntries();
}

/// <summary>
/// Optional package-content capability that exposes a cached entry manifest with declared
/// expanded lengths. Hosts use it to reject an over-budget workspace before opening entry bodies.
/// </summary>
public interface IPackageContentEntryManifest
{
    /// <summary>Gets one entry's declared expanded length.</summary>
    bool TryGetEntryLength(string relativePath, out long length);

    /// <summary>Returns package entry paths and their declared expanded lengths.</summary>
    IReadOnlyList<PackageContentEntry> EnumerateEntriesWithLengths();
}

/// <summary>One package entry's path and declared expanded length.</summary>
public readonly record struct PackageContentEntry(string Path, long Length);
