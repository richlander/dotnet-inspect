using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Thin dependency-candidate adapter over explicit package authorization and
/// one caller-owned package-source settlement lease.
/// </summary>
public sealed class AuthorizedPackageDependencyCandidateSource
    : IPackageDependencyCandidateSource
{
    private readonly IPackageSourceAuthorization _authorization;
    private readonly PackageSourceSettlementLease _sourceLease;

    internal PackageSourceSettlementLease SourceLease => _sourceLease;

    public AuthorizedPackageDependencyCandidateSource(
        IPackageSourceAuthorization authorization,
        PackageSourceSettlementLease sourceLease)
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
