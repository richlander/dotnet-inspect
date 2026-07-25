using System.Collections.Concurrent;
using NuGet.Versioning;

namespace DotnetInspector.Packages;

/// <summary>
/// In-memory <see cref="IPackageStore"/> for hosts without a persistent
/// filesystem (browser/WASM) and for tests. Caches nupkg bytes keyed by
/// lowercase <c>{name}@{version}</c>; content is read back via an in-memory
/// <see cref="ZipArchive"/>. No files are ever written.
/// </summary>
public sealed class InMemoryPackageStore : IPackageStore
{
    private readonly ConcurrentDictionary<string, byte[]> _packages = new(StringComparer.Ordinal);

    private static string Key(string packageName, string version)
        => $"{packageName.ToLowerInvariant()}@{version.ToLowerInvariant()}";

    /// <inheritdoc />
    public IPackageContent? TryGetCached(string packageName, string version, Action<string>? log = null)
    {
        if (!_packages.TryGetValue(Key(packageName, version), out var bytes))
            return null;

        log?.Invoke($"Using cached package: {packageName} {version}");
        return new InMemoryPackageContent(bytes, fromCache: true);
    }

    /// <inheritdoc />
    public async ValueTask<IPackageContent> CommitAsync(
        string packageName,
        string version,
        Stream nupkg,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(nupkg);

        using var buffer = new MemoryStream();
        await nupkg.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        _packages[Key(packageName, version)] = bytes;
        return new InMemoryPackageContent(bytes, fromCache: true);
    }

    /// <inheritdoc />
    public string? TryGetLatestCachedVersion(string packageName)
    {
        var prefix = packageName.ToLowerInvariant() + "@";
        NuGetVersion? best = null;
        string? bestRaw = null;

        foreach (var key in _packages.Keys)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            var raw = key[prefix.Length..];
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
