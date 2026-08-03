using System.Collections.Concurrent;
namespace DotnetInspector.Packages;

/// <summary>
/// In-memory <see cref="IPackageStore"/> for hosts without a persistent
/// filesystem (browser/WASM) and for tests. Caches nupkg bytes keyed by
/// lowercase <c>{name}@{version}</c> and the identity of the source that served
/// them; content is read back via an in-memory
/// <see cref="ZipArchive"/>. No files are ever written.
/// </summary>
public sealed class InMemoryPackageStore : IPackageStore
{
    private readonly ConcurrentDictionary<string, byte[]> _packages = new(StringComparer.Ordinal);

    private static string Key(string packageName, string version, string sourceKey)
        => $"{packageName.ToLowerInvariant()}@{version.ToLowerInvariant()}@{sourceKey}";

    /// <inheritdoc />
    public IPackageContent? TryGetCached(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        Action<string>? log = null)
    {
        foreach (var sourceKey in allowedSourceKeys ?? [])
        {
            if (!_packages.TryGetValue(Key(packageName, version, sourceKey), out var bytes))
                continue;

            log?.Invoke($"Using cached package: {packageName} {version}");
            return new InMemoryPackageContent(bytes, fromCache: true, sourceKey);
        }

        return null;
    }

    /// <inheritdoc />
    public async ValueTask<IPackageContent> CommitAsync(
        string packageName,
        string version,
        string sourceKey,
        Stream nupkg,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentNullException.ThrowIfNull(nupkg);

        using var buffer = new MemoryStream();
        await nupkg.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        _packages[Key(packageName, version, sourceKey)] = bytes;
        return new InMemoryPackageContent(bytes, fromCache: true, sourceKey);
    }

}
