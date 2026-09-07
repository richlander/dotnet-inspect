using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Adapts <see cref="DesktopPackageSourceComposition"/>'s candidate-authorized exact
/// manifest capability to the traversal-owned
/// <see cref="IPackageDependencyTraversalManifestAcquirer"/> boundary. This is the CLI
/// desktop host's thin path.
/// </summary>
public sealed class DesktopPackageDependencyTraversalManifestSource(
    DesktopPackageSourceComposition composition) :
    IPackageDependencyTraversalManifestAcquirer
{
    private readonly DesktopPackageSourceComposition _composition =
        composition ?? throw new ArgumentNullException(nameof(composition));

    public async Task<PackageDependencyTraversalManifestResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ConfiguredPackageManifestResult result =
            await _composition.AcquireCandidateManifestAsync(
                candidate,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        return result.Manifest is { } manifest
            ? new PackageDependencyTraversalManifestResult.Acquired(
                manifest,
                [.. result.Failures])
            : ClassifyFailure(result.Failures);
    }

    internal static PackageDependencyTraversalManifestResult ClassifyFailure(
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

/// <summary>
/// Host-neutral candidate-authorized exact manifest capability over explicit caller-
/// owned source clients, mirroring
/// <see cref="AuthorizedPackageDependencyCandidateSource"/>. This is the Browser/Wasm
/// host's thin path: the caller supplies the source client for each configured
/// authority instead of a desktop transport composition.
/// </summary>
public sealed class AuthorizedPackageDependencyManifestSource(
    AuthorizedPackageDependencyCandidateSource candidateSource) :
    IPackageDependencyTraversalManifestAcquirer
{
    private readonly PackageAcquisitionCandidateManifestAcquirer _acquirer =
        candidateSource is null
            ? throw new ArgumentNullException(nameof(candidateSource))
            : new(
                candidateSource.CandidateIssuer,
                candidateSource.GetClient);

    public async Task<PackageDependencyTraversalManifestResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ConfiguredPackageManifestResult result =
            await _acquirer.AcquireAsync(
                candidate,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        return result.Manifest is { } manifest
            ? new PackageDependencyTraversalManifestResult.Acquired(
                manifest,
                [.. result.Failures])
            : DesktopPackageDependencyTraversalManifestSource.ClassifyFailure(
                result.Failures);
    }
}
