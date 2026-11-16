namespace DotnetInspector.Packages;

/// <summary>
/// Filesystem-backed <see cref="IPackageStore"/> that delegates to
/// <see cref="NuGetCache"/>. Structurally admitted payloads are extracted once
/// through checked staging before transactional cache publication. If an
/// immutable slot already won, acquisition re-admits that returned generation
/// under the current caller's limits.
/// </summary>
public sealed class FileSystemPackageStore : IPackageStore, IPreparedPackageStore
{
    /// <inheritdoc />
    public IPackageContent? TryGetCached(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        Action<string>? log = null)
    {
        foreach (IPackageContent content in EnumerateCached(
                     packageName,
                     version,
                     allowedSourceKeys,
                     log))
        {
            return content;
        }

        return null;
    }

    /// <inheritdoc />
    public IEnumerable<IPackageContent> EnumerateCached(
        string packageName,
        string version,
        IReadOnlyList<string>? allowedSourceKeys,
        Action<string>? log = null)
    {
        var normalizedName = packageName.ToLowerInvariant();
        var normalizedVersion = version.ToLowerInvariant();

        // Lazy tiers: do not materialize global-packages until the caller
        // advances past earlier candidates (typically after admission reject).
        foreach (CachedPackage cached in NuGetCache.EnumerateCachedPackageContent(
                     normalizedName,
                     normalizedVersion,
                     allowedSourceKeys))
        {
            using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageLoad))
            {
                log?.Invoke($"Using cached package: {cached.ExtractPath}");
                var cachedNupkg = FindNupkgInDirectory(
                    cached.ExtractPath,
                    normalizedName,
                    normalizedVersion);
                yield return new FileSystemPackageContent(
                    cached.ExtractPath,
                    cachedNupkg,
                    fromCache: true,
                    cached.ProducerKey,
                    requiresArchiveTreeMatch: cached.RequiresArchiveTreeMatch);
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<IPackageContent> CommitAsync(
        string packageName,
        string version,
        string sourceKey,
        Stream nupkg,
        CancellationToken cancellationToken = default)
    {
        PreparedPackageCommit prepared = await CommitAsync(
            packageName,
            version,
            nupkg,
            (archive, token) => NuGetCache.CommitOwnedArchiveAsync(
                archive, packageName, version, sourceKey, token),
            cancellationToken).ConfigureAwait(false);
        if (prepared.RequiresAdmission
            && !await PackageContentAdmission.IsAdmissibleAsync(
                    prepared.Content,
                    PackagePayloadLimits.Default,
                    cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidDataException(
                "The existing package cache entry does not satisfy the current payload limits.");
        }

        return prepared.Content;
    }

    ValueTask<PreparedPackageCommit> IPreparedPackageStore.CommitPreparedAsync(
        string packageName,
        string version,
        string sourceKey,
        PackageArchivePayload archive,
        CancellationToken cancellationToken) =>
        CommitAsync(
            packageName,
            version,
            archive,
            (payload, token) => NuGetCache.CommitOwnedArchiveAsync(
                payload, packageName, version, sourceKey, token),
            cancellationToken);

    internal static async ValueTask<PreparedPackageCommit> CommitAsync(
        string packageName,
        string version,
        Stream nupkg,
        Func<PackageArchivePayload, CancellationToken, ValueTask<PackageCommitResult>> commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nupkg);
        byte[]? bytes = await PackageContentAdmission.ReadBoundedAsync(
                nupkg,
                PackagePayloadLimits.Default.MaxArchiveBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null)
            throw new InvalidDataException("Package archive exceeds the configured byte limit.");

        PackageArchiveValidation validation = PackageArchiveValidator.ValidateOwned(
            bytes,
            cancellationToken: cancellationToken);
        if (validation is PackageArchiveValidation.Rejected rejection)
            throw new InvalidDataException(rejection.Reason);

        return await CommitAsync(
                packageName,
                version,
                ((PackageArchiveValidation.Valid)validation).Archive,
                commit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async ValueTask<PreparedPackageCommit> CommitAsync(
        string packageName,
        string version,
        PackageArchivePayload archive,
        Func<PackageArchivePayload, CancellationToken, ValueTask<PackageCommitResult>> commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);

        NuGetCache.ValidatePathComponent(packageName, "package name");
        NuGetCache.ValidatePathComponent(version, "version");

        PackageCommitResult committed =
            await commit(archive, cancellationToken).ConfigureAwait(false);
        var content = new FileSystemPackageContent(
            committed.Package.ExtractPath,
            committed.Package.NupkgPath,
            fromCache: true,
            committed.Package.ProducerKey,
            requiresArchiveTreeMatch: true);
        if (committed.PublishedStagedContent)
            content.RememberArchiveEntries(archive);
        return new PreparedPackageCommit(
            content,
            RequiresAdmission: !committed.PublishedStagedContent);
    }

    internal static string? FindNupkgInDirectory(string cacheDir, string packageName, string version)
    {
        // Standard NuGet cache layout: {package}/{version}/{package}.{version}.nupkg
        // Only the expected retained archive name is admissible. Scanning for
        // any *.nupkg would let extracted package content (a decoy nupkg in the
        // tree) stand in for the archive PackageContentAdmission re-validates.
        var expectedPath = Path.Combine(cacheDir, $"{packageName}.{version}.nupkg");
        return File.Exists(expectedPath) ? expectedPath : null;
    }
}
