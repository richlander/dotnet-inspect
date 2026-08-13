namespace DotnetInspector.Packages;

/// <summary>
/// Host-neutral persistence for resolved NuGet package payloads. Separates the
/// host-neutral coordinate resolution and payload transport in
/// <see cref="PackageCoordinateResolver"/> and
/// <see cref="PackagePayloadAcquisition"/> from where a downloaded package is
/// cached and how its contents are read back.
/// </summary>
/// <remarks>
/// The filesystem implementation (<see cref="FileSystemPackageStore"/>) delegates
/// to <see cref="NuGetCache"/> and preserves the exact on-disk cache layout,
/// transactional commit, and returned paths the desktop CLI has always used. An
/// in-memory implementation (<see cref="InMemoryPackageStore"/>) lets a
/// browser/WASM host reuse the same download/acquire flow without a filesystem.
/// </remarks>
public interface IPackageStore
{
    /// <summary>
    /// Returns the cached content for <paramref name="packageName"/> at
    /// <paramref name="version"/> without touching the network, or <c>null</c>
    /// when the package is not cached.
    /// </summary>
    /// <param name="allowedSourceKeys">
    /// Identities of the sources the caller is configured to read from.
    /// Content committed by a source outside this set is not returned.
    /// </param>
    IPackageContent? TryGetCached(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        Action<string>? log = null);

    /// <summary>
    /// Yields every cache tier that may hold the coordinate for the allowed
    /// producers, preferred order first. Callers that admit content should
    /// walk this sequence so a rejected global-packages slot does not mask a
    /// usable app-cache entry for the same producer.
    /// </summary>
    /// <remarks>
    /// Default: at most the single <see cref="TryGetCached"/> result. Filesystem
    /// stores override to surface app-cache then global-packages tiers.
    /// </remarks>
    IEnumerable<IPackageContent> EnumerateCached(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        Action<string>? log = null)
    {
        if (TryGetCached(packageName, version, allowedSourceKeys, log) is { } one)
            yield return one;
    }

    /// <summary>
    /// Persists a freshly downloaded package from its <paramref name="nupkg"/>
    /// payload stream and returns a handle to the committed content.
    /// </summary>
    /// <param name="sourceKey">
    /// Identity of the source that served <paramref name="nupkg"/>, recorded so
    /// the content is not later served to a caller configured for other sources.
    /// </param>
    ValueTask<IPackageContent> CommitAsync(
        string packageName,
        string version,
        string sourceKey,
        Stream nupkg,
        CancellationToken cancellationToken = default);

}
