using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Binds dependency candidate resolution to one desktop package-source
/// composition and source policy.
/// </summary>
public sealed class DesktopPackageDependencyCandidateSource(
    DesktopPackageSourceComposition composition,
    NuGetSourceOptions? sourceOptions = null,
    Action<string>? log = null) : IPackageDependencyCandidateSource
{
    private readonly DesktopPackageSourceComposition _composition =
        composition ?? throw new ArgumentNullException(nameof(composition));

    public ValueTask<PackageAcquisitionCandidateResult>
        ResolvePinnedCandidateAsync(
            PackageSourceCoordinate coordinate,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
        ValueTask.FromResult(
            _composition.ResolvePinnedCandidate(
                coordinate,
                sourceOptions,
                cancellationToken,
                operationContext));

    public Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsAsync(
            string packageId,
            CancellationToken cancellationToken = default,
            NuGetOperationContext? operationContext = null) =>
        _composition.GetDependencyVersionsAsync(
            packageId,
            sourceOptions,
            log,
            cancellationToken,
            operationContext);
}
