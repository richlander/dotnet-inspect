using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Thin dependency-candidate adapter over explicit package authorization and
/// one caller-owned House source lease.
/// </summary>
public sealed class AuthorizedPackageDependencyCandidateSource
    : IPackageDependencyCandidateSource
{
    private readonly IPackageSourceAuthorization _authorization;
    private readonly PackageHouseSourceLease _sourceLease;

    internal PackageHouseSourceLease SourceLease => _sourceLease;

    public AuthorizedPackageDependencyCandidateSource(
        IPackageSourceAuthorization authorization,
        PackageHouseSourceLease sourceLease)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(sourceLease);
        _authorization = authorization;
        _sourceLease = sourceLease;
    }

    public ValueTask<PackageAcquisitionCandidateResult>
        ResolvePinnedCandidateAsync(
        PackageSourceCoordinate coordinate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        _sourceLease.ResolvePinnedCandidateAsync(
            _authorization,
            coordinate,
            cancellationToken,
            operationContext);

    public async Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            await _sourceLease.DiscoverDependencyVersionsAsync(
                packageId,
                _authorization,
                cancellationToken,
                operationContext).ConfigureAwait(false);
}
