using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

public sealed class DesktopPackageLicenseManifestSource(
    DesktopPackageSourceComposition composition,
    NuGetSourceOptions? sourceOptions = null,
    Action<string>? log = null) : IPackageLicenseManifestSource
{
    private readonly DesktopPackageDependencyCandidateSource _candidateSource =
        new(
            composition
                ?? throw new ArgumentNullException(nameof(composition)),
            sourceOptions,
            log);

    private readonly DesktopPackageDependencyTraversalManifestSource
        _manifestSource = new(composition);

    public async Task<PackageLicenseManifestResult> AcquireAsync(
        PackageSourceCoordinate coordinate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        PackageAcquisitionCandidateResult candidate =
            await _candidateSource.ResolvePinnedCandidateAsync(
                coordinate,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        if (candidate.State
                == PackageAcquisitionCandidateResultState.Incomplete)
        {
            return new PackageLicenseManifestResult.Unavailable(
                PackageLicenseManifestFailureReason.AuthorizationIncomplete);
        }
        if (candidate.State == PackageAcquisitionCandidateResultState.Denied)
        {
            return new PackageLicenseManifestResult.Unavailable(
                PackageLicenseManifestFailureReason.AuthorizationDenied);
        }

        PackageDependencyTraversalManifestResult manifest =
            await _manifestSource.AcquireAsync(
                candidate.Candidate
                    ?? throw new InvalidOperationException(
                        "A resolved package candidate did not carry a candidate."),
                cancellationToken,
                operationContext).ConfigureAwait(false);
        return manifest switch
        {
            PackageDependencyTraversalManifestResult.Acquired acquired =>
                new PackageLicenseManifestResult.Acquired(
                    acquired.Manifest.Content.ToArray()),
            PackageDependencyTraversalManifestResult.Incomplete =>
                new PackageLicenseManifestResult.Unavailable(
                    PackageLicenseManifestFailureReason
                        .AcquisitionIncomplete),
            PackageDependencyTraversalManifestResult.Failed =>
                new PackageLicenseManifestResult.Unavailable(
                    PackageLicenseManifestFailureReason.AcquisitionFailed),
            _ => throw new InvalidOperationException(
                "Unknown package manifest acquisition result."),
        };
    }
}
