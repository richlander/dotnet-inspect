using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Acquires candidate-authorized exact manifests through the traversal's
/// transferred Package Source operation.
/// </summary>
public sealed class AuthorizedPackageDependencyManifestSource :
    IPackageDependencyTraversalManifestAcquirer
{
    public async Task<PackageDependencyTraversalManifestResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        PackageSourceOperationLease operation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(operation);
        ConfiguredPackageManifestResult result =
            await operation.AcquireCandidateManifestAsync(
                candidate).ConfigureAwait(false);
        return result.Manifest is { } manifest
            ? new PackageDependencyTraversalManifestResult.Acquired(
                manifest,
                [.. result.Failures])
            : ClassifyFailure(result.Failures);
    }

    private static PackageDependencyTraversalManifestResult ClassifyFailure(
        IReadOnlyList<PackageAuthorityFailure> failures)
    {
        bool timedOut = failures.Any(
            failure => failure.Timeout?.Kind
                == PackageSourceTimeoutKind.Operation);
        return timedOut
            ? new PackageDependencyTraversalManifestResult.Incomplete(
                [.. failures])
            : new PackageDependencyTraversalManifestResult.Failed([.. failures]);
    }
}
