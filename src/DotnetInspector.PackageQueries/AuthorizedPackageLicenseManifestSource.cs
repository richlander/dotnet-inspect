using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Acquires exact license manifests through caller-owned Package Source
/// operations and explicit source authorization.
/// </summary>
public sealed class AuthorizedPackageLicenseManifestSource(
    IPackageSourceAuthorization sourceAuthorization) :
    IPackageLicenseManifestSource
{
    private readonly IPackageSourceAuthorization _sourceAuthorization =
        sourceAuthorization
        ?? throw new ArgumentNullException(nameof(sourceAuthorization));

    public async Task<PackageLicenseManifestResult> AcquireAsync(
        PackageSourceCoordinate coordinate,
        PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(operation);
        PackageAcquisitionCandidateResult candidate =
            await operation.ResolvePinnedCandidateAsync(
                _sourceAuthorization,
                coordinate).ConfigureAwait(false);
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
            await operation.AcquireCandidateManifestAsync(
                candidate.Candidate
                    ?? throw new InvalidOperationException(
                        "A resolved package candidate did not carry a candidate."))
                .ConfigureAwait(false);
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
                PackageLicenseManifestFailureReason.AcquisitionIncomplete)
            : new PackageLicenseManifestResult.Unavailable(
                PackageLicenseManifestFailureReason.AcquisitionFailed);
    }
}
