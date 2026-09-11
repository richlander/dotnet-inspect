using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Thin dependency-candidate adapter over explicit package authorization and
/// revocable permission for one caller-owned package-source generation.
/// </summary>
public sealed class AuthorizedPackageDependencyCandidateSource
    : IPackageDependencyCandidateSource
{
    private readonly IPackageSourceAuthorization _authorization;
    private readonly PackageSourceSettlementAuthorization _sourceAuthorization;

    internal PackageSourceSettlementAuthorization SourceAuthorization => _sourceAuthorization;

    public AuthorizedPackageDependencyCandidateSource(
        IPackageSourceAuthorization authorization,
        PackageSourceSettlementAuthorization sourceAuthorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        _authorization = authorization;
        _sourceAuthorization = sourceAuthorization;
    }

    public ValueTask<PackageAcquisitionCandidateResult>
        ResolvePinnedCandidateAsync(
        PackageSourceCoordinate coordinate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        new(PackageSourceSettlementCompatibility.RunAsync(
            _sourceAuthorization, cancellationToken, operationContext,
            (generation, context) => generation.ResolvePinnedCandidateAsync(
                _authorization, coordinate, operationContext: context).AsTask()));

    public Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
            PackageSourceSettlementCompatibility.RunAsync(
                _sourceAuthorization, cancellationToken, operationContext,
                (generation, context) => generation.DiscoverDependencyVersionsAsync(
                    packageId, _authorization, operationContext: context));
}
