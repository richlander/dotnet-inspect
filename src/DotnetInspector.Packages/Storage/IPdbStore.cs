namespace DotnetInspector.Packages;

/// <summary>
/// Host-neutral persistence for resolved Portable PDB payloads, keyed by a
/// store-relative identity (for example
/// <c>{package}/{version}/{contentIdentity}/{assembly}.pdb</c> or
/// <c>servers/{providerHost}/{pdbName}/{contentIdentity}/{pdbName}</c>).
/// </summary>
/// <remarks>
/// The filesystem implementation (<see cref="FileSystemPdbStore"/>) maps keys to
/// files under the symbol cache and can surface a real path via
/// <see cref="TryGetLocalPath"/>; an in-memory implementation keeps bytes in a
/// dictionary and returns <c>null</c> for the local path. This lets the snupkg
/// download / symbol-server acquisition logic run unchanged on desktop while a
/// browser/WASM host supplies an in-memory cache.
/// </remarks>
public interface IPdbStore
{
    /// <summary>
    /// Opens the cached PDB payload for <paramref name="key"/>, or returns
    /// <c>null</c> when the entry is absent. A returned stream must be fresh,
    /// readable, seekable, and positioned at zero; the caller owns it.
    /// </summary>
    ValueTask<Stream?> TryOpenAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="content"/> under <paramref name="key"/>,
    /// overwriting any prior entry.
    /// </summary>
    ValueTask PutAsync(string key, Stream content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a filesystem path for the cached PDB when this store is
    /// filesystem-backed and the entry exists, otherwise <c>null</c>. Desktop
    /// callers that must hand a real file path to a PDB reader use this;
    /// host-neutral callers ignore it and read bytes via <see cref="TryOpenAsync"/>.
    /// </summary>
    string? TryGetLocalPath(string key);
}
