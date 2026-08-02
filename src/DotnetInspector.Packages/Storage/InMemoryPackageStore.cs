using System.Collections.Concurrent;
using NuGet.Versioning;

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
        IReadOnlyCollection<string>? allowedSourceKeys,
        Action<string>? log = null)
    {
        foreach (var sourceKey in allowedSourceKeys ?? [])
        {
            if (!_packages.TryGetValue(Key(packageName, version, sourceKey), out var bytes))
                continue;

            log?.Invoke($"Using cached package: {packageName} {version}");
            return new InMemoryPackageContent(bytes, fromCache: true);
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
        return new InMemoryPackageContent(bytes, fromCache: true);
    }

    /// <inheritdoc />
    public string? TryGetLatestCachedVersion(
        string packageName,
        IReadOnlyCollection<string>? allowedSourceKeys)
    {
        var prefix = packageName.ToLowerInvariant() + "@";
        NuGetVersion? best = null;
        string? bestRaw = null;

        foreach (var key in _packages.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var remainder = key[prefix.Length..];
            var separator = remainder.LastIndexOf('@');
            if (separator < 0)
                continue;

            var sourceKey = remainder[(separator + 1)..];
            if (allowedSourceKeys is null || !allowedSourceKeys.Contains(sourceKey))
                continue;

            var raw = remainder[..separator];
            if (!NuGetVersion.TryParse(raw, out var parsed) || parsed.IsPrerelease)
                continue;

            if (best is null || parsed > best)
            {
                best = parsed;
                bestRaw = raw;
            }
        }

        return bestRaw;
    }
}
