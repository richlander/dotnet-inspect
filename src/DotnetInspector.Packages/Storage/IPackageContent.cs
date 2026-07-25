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
    /// Opens a package entry addressed by its <c>/</c>-separated, package-root
    /// relative path (for example <c>lib/net8.0/Foo.dll</c>). Returns
    /// <c>false</c> when no such entry exists.
    /// </summary>
    bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream);

    /// <summary>
    /// Enumerates the <c>/</c>-separated, package-root relative paths of every
    /// entry in the package.
    /// </summary>
    IEnumerable<string> EnumerateEntries();
}
