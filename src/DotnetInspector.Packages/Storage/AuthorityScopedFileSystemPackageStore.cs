using DotnetInspector.Core;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// A desktop payload store owned by one configured authority. Producer keys
/// remain provenance; legacy source-key arguments cannot select another slot.
/// </summary>
internal sealed class AuthorityScopedFileSystemPackageStore : IPackageStore
{
    private readonly ConfiguredPackageAuthority _authority;
    private readonly PackageProducerIdentity _producer;
    private readonly Func<string> _getTemporaryRoot;
    private readonly Lazy<string> _temporaryCacheRoot;

    internal AuthorityScopedFileSystemPackageStore(
        ConfiguredPackageAuthority authority,
        PackageProducerIdentity producer,
        Func<string> getTemporaryRoot)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(getTemporaryRoot);
        _authority = authority;
        _producer = producer;
        _getTemporaryRoot = getTemporaryRoot;
        _temporaryCacheRoot = new(() => Path.Combine(
            getTemporaryRoot(), $"package-authority-{Guid.NewGuid():N}"));
    }

    public IPackageContent? TryGetCached(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        Action<string>? log = null) =>
        EnumerateCached(packageName, version, allowedSourceKeys, log).FirstOrDefault();

    public IEnumerable<IPackageContent> EnumerateCached(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        Action<string>? log = null)
    {
        NuGetCache.ValidatePathComponent(packageName, "package name");
        NuGetCache.ValidatePathComponent(version, "version");
        if (allowedSourceKeys?.Contains(_producer.Key, StringComparer.Ordinal) != true)
            yield break;

        string normalizedName = packageName.ToLowerInvariant();
        string normalizedVersion = version.ToLowerInvariant();
        string cacheKey = $"{normalizedName}@{normalizedVersion}";
        bool any = false;

        string? slot = GetSlotPath(normalizedName, normalizedVersion, create: false);
        if (slot is not null
            && NuGetCache.IsCommittedPackageSlotPresent(
                slot, GetMarkerContent(normalizedName, normalizedVersion)))
        {
            any = true;
            RecordHit("packages");
            yield return OpenContent(slot, requiresArchiveTreeMatch: true);
        }

        // No durable HTTP authority identity exists in this slice. Producer
        // equality cannot authorize a NuGet global-packages replica.
        if (_authority.PersistentCacheKey is not null
            && NuGetCache.UsesGlobalPackages)
        {
            foreach (string root in NuGetCache.GetNuGetPackageRoots())
            {
                string directory = Path.Combine(root, normalizedName, normalizedVersion);
                if (!Directory.Exists(directory)
                    || !NuGetCache.TryReadGlobalPackageSource(
                        directory, out string? source, rawSource: true)
                    || !MatchesLocalAuthority(source))
                {
                    continue;
                }

                if (!any)
                {
                    any = true;
                    RecordHit("nuget-global-packages");
                }
                yield return OpenContent(directory, requiresArchiveTreeMatch: false);
            }
        }

        if (!any)
        {
            InfoTracker.RecordCacheMiss();
            CacheTelemetry.Record("packages", cacheKey, CacheAccessResult.Miss);
        }

        void RecordHit(string category)
        {
            InfoTracker.RecordCacheHit();
            CacheTelemetry.Record(category, cacheKey, CacheAccessResult.Hit);
        }

        IPackageContent OpenContent(string directory, bool requiresArchiveTreeMatch)
        {
            using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageLoad))
            {
                log?.Invoke($"Using cached package: {directory}");
                return new FileSystemPackageContent(
                    directory,
                    FileSystemPackageStore.FindNupkgInDirectory(
                        directory, normalizedName, normalizedVersion),
                    fromCache: true,
                    _producer.Key,
                    requiresArchiveTreeMatch);
            }
        }
    }

    public ValueTask<IPackageContent> CommitAsync(
        string packageName,
        string version,
        string sourceKey,
        Stream nupkg,
        CancellationToken cancellationToken = default)
    {
        NuGetCache.ValidatePathComponent(packageName, "package name");
        NuGetCache.ValidatePathComponent(version, "version");
        if (!string.Equals(sourceKey, _producer.Key, StringComparison.Ordinal))
            throw new ArgumentException("The payload producer does not match the store's producer.", nameof(sourceKey));

        string normalizedName = packageName.ToLowerInvariant();
        string normalizedVersion = version.ToLowerInvariant();
        return FileSystemPackageStore.CommitAsync(
            packageName,
            version,
            nupkg,
            () => Directory.CreateDirectory(Path.Combine(
                _getTemporaryRoot(), $"package-commit-{Guid.NewGuid():N}")).FullName,
            (extractedPath, nupkgPath) => NuGetCache.CommitPackageToSlot(
                extractedPath,
                nupkgPath,
                normalizedName,
                normalizedVersion,
                _producer.Key,
                GetSlotPath(normalizedName, normalizedVersion, create: true)!,
                GetMarkerContent(normalizedName, normalizedVersion),
                useAppCache: _authority.PersistentCacheKey is not null),
            cancellationToken);
    }

    private string? GetSlotPath(string packageName, string version, bool create)
    {
        if (_authority.PersistentCacheKey is { } key)
        {
            NuGetCache.ValidatePathComponent(key, "authority key");
            if (!create && !NuGetCache.TryGetPackageContentCachePath(out _))
                return null;
            return Path.Combine(
                CoreCache.GetCategoryPath(NuGetCache.AuthorityPackageContentCategory),
                packageName, version, key);
        }

        if (!create && !_temporaryCacheRoot.IsValueCreated)
            return null;
        return Path.Combine(_temporaryCacheRoot.Value, packageName, version);
    }

    private string GetMarkerContent(string packageName, string version) =>
        $"{NuGetCache.AuthorityPackageContentCategory}:{packageName}@{version}:"
        + $"{_authority.PersistentCacheKey}:{_producer.Key}";

    private bool MatchesLocalAuthority(string source)
    {
        try
        {
            return _authority.LocalIdentity is { } local
                && local.Equals(LocalPackageSourceIdentity.CreateAbsolute(source));
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
