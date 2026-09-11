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
/// Host-neutral candidate-authorized exact manifest adapter over the same
/// caller-owned package-source settlement generation as
/// <see cref="AuthorizedPackageDependencyCandidateSource"/>. This is the
/// Browser/Wasm host's thin path: the host supplies source capabilities
/// instead of a desktop transport composition.
/// </summary>
public sealed class AuthorizedPackageDependencyManifestSource(
    AuthorizedPackageDependencyCandidateSource candidateSource) :
    IPackageDependencyTraversalManifestAcquirer
{
    private readonly PackageSourceSettlementAuthorization _sourceAuthorization =
        candidateSource is null
            ? throw new ArgumentNullException(nameof(candidateSource))
            : candidateSource.SourceAuthorization;

    public Task<PackageDependencyTraversalManifestResult> AcquireAsync(
        PackageAcquisitionCandidate candidate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        PackageSourceSettlementCompatibility.RunAsync(
            _sourceAuthorization, cancellationToken, operationContext,
            (generation, context) => AcquireCoreAsync(generation, candidate, context));

    private static async Task<PackageDependencyTraversalManifestResult> AcquireCoreAsync(
        PackageSourceSettlementGeneration generation,
        PackageAcquisitionCandidate candidate,
        NuGetOperationContext context)
    {
        ConfiguredPackageManifestResult result =
            await generation.AcquireCandidateManifestAsync(
                candidate,
                operationContext: context).ConfigureAwait(false);
        return result.Manifest is { } manifest
            ? new PackageDependencyTraversalManifestResult.Acquired(
                manifest,
                [.. result.Failures])
            : DesktopPackageDependencyTraversalManifestSource.ClassifyFailure(
                result.Failures);
    }
}
