using System.Collections.Concurrent;
namespace DotnetInspector.Packages;

/// <summary>
/// In-memory <see cref="IPackageStore"/> for hosts without a persistent
/// filesystem (browser/WASM) and for tests. Caches nupkg bytes keyed by
/// lowercase <c>{name}@{version}</c> and the identity of the source that served
/// them; selected content is read from a retained structural archive index.
/// No files are ever written.
/// </summary>
public sealed class InMemoryPackageStore : IPackageStore, IPreparedPackageStore
{
    private readonly ConcurrentDictionary<string, InMemoryPackageContent> _packages =
        new(StringComparer.Ordinal);

    private static string Key(string packageName, string version, string sourceKey)
        => $"{packageName.ToLowerInvariant()}@{version.ToLowerInvariant()}@{sourceKey}";

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
        foreach (var sourceKey in allowedSourceKeys ?? [])
        {
            if (!_packages.TryGetValue(Key(packageName, version, sourceKey), out var content))
                continue;

            log?.Invoke($"Using cached package: {packageName} {version}");
            yield return content.AsCacheHit();
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
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentNullException.ThrowIfNull(nupkg);

        byte[]? bytes = await PackageContentAdmission.ReadBoundedAsync(
                nupkg,
                PackagePayloadLimits.Default.MaxArchiveBytes,
                cancellationToken)
            .ConfigureAwait(false);
        if (bytes is null)
        {
            throw new InvalidDataException(
                "Package archive exceeds the configured byte limit.");
        }

        PackageArchiveValidation validation = PackageArchiveValidator.ValidateOwned(
            bytes,
            cancellationToken: cancellationToken);
        if (validation is PackageArchiveValidation.Rejected rejection)
        {
            throw new InvalidDataException(rejection.Reason);
        }

        PreparedPackageCommit prepared = await CommitPreparedAsync(
            packageName,
            version,
            sourceKey,
            ((PackageArchiveValidation.Valid)validation).Archive,
            cancellationToken).ConfigureAwait(false);
        return prepared.Content;
    }

    ValueTask<PreparedPackageCommit> IPreparedPackageStore.CommitPreparedAsync(
        string packageName,
        string version,
        string sourceKey,
        PackageArchivePayload archive,
        CancellationToken cancellationToken) =>
        CommitPreparedAsync(
            packageName,
            version,
            sourceKey,
            archive,
            cancellationToken);

    private ValueTask<PreparedPackageCommit> CommitPreparedAsync(
        string packageName,
        string version,
        string sourceKey,
        PackageArchivePayload archive,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentNullException.ThrowIfNull(archive);
        cancellationToken.ThrowIfCancellationRequested();

        var content = InMemoryPackageContent.CreateOwned(
            archive,
            fromCache: true,
            sourceKey);
        _packages[Key(packageName, version, sourceKey)] = content;
        return ValueTask.FromResult(
            new PreparedPackageCommit(
                content,
                RequiresAdmission: false));
    }

}
