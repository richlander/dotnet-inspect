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

    private readonly DesktopPackageSourceComposition _composition =
        composition;

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

        ConfiguredPackageManifestResult manifest =
            await _composition.AcquireCandidateManifestAsync(
                candidate.Candidate
                    ?? throw new InvalidOperationException(
                        "A resolved package candidate did not carry a candidate."),
                cancellationToken,
                operationContext).ConfigureAwait(false);
        if (manifest.Manifest is { } acquired)
        {
            return new PackageLicenseManifestResult.Acquired(
                acquired.Content.ToArray());
        }

        bool operationTimedOut = manifest.Failures.Any(
            failure => failure.Timeout?.Kind
                == PackageSourceTimeoutKind.Operation);
        return operationTimedOut
                ? new PackageLicenseManifestResult.Unavailable(
                    PackageLicenseManifestFailureReason
                        .AcquisitionIncomplete)
                : new PackageLicenseManifestResult.Unavailable(
                    PackageLicenseManifestFailureReason.AcquisitionFailed);
    }
}
