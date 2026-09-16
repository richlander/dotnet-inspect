using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Adapts package-owned dependency-candidate resolution to
/// the traversal-owned <see cref="IPackageDependencyTraversalCandidateResolver"/>
/// boundary. <c>DotnetInspector.Queries</c> cannot reference this assembly because
/// this assembly already references <c>DotnetInspector.Queries</c>; this thin adapter
/// is the seam that lets the traversal query consume #5765 without duplicating source
/// authorization or version-selection logic.
/// </summary>
public sealed class PackageDependencyTraversalCandidateAdapter(
    IPackageDependencyCandidateSource source) :
    IPackageDependencyTraversalCandidateResolver
{
    private readonly IPackageDependencyCandidateSource _source =
        source ?? throw new ArgumentNullException(nameof(source));

    public async ValueTask<PackageDependencyTraversalCandidateResult> ResolveAsync(
        PackageDependencyEvidenceDeclaration declaration,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        PackageDependencyCandidateResult result =
            await PackageDependencyCandidateQuery.ExecuteAsync(
                new PackageDependencyCandidateRequest.Declared(declaration),
                _source,
                cancellationToken,
                operationContext).ConfigureAwait(false);
        return result switch
        {
            PackageDependencyCandidateResult.Resolved resolved =>
                new PackageDependencyTraversalCandidateResult.Resolved(
                    resolved.Candidate,
                    [.. resolved.Diagnostics]),
            PackageDependencyCandidateResult.Failed failed =>
                new PackageDependencyTraversalCandidateResult.Failed(
                    ConvertFailure(failed.Failure)),
            PackageDependencyCandidateResult.Incomplete incomplete =>
                new PackageDependencyTraversalCandidateResult.Incomplete(
                    ConvertIncomplete(incomplete.Evidence)),
            _ => throw new InvalidOperationException(
                "Unknown package dependency candidate result."),
        };
    }

    private static PackageDependencyTraversalCandidateFailure ConvertFailure(
        PackageDependencyCandidateFailure failure) => failure switch
    {
        PackageDependencyCandidateFailure.AuthorizationDenied denied =>
            new PackageDependencyTraversalCandidateFailure.AuthorizationDenied(
                [.. denied.Failures]),
        PackageDependencyCandidateFailure.NoMatchingVersion =>
            new PackageDependencyTraversalCandidateFailure.NoMatchingVersion(),
        PackageDependencyCandidateFailure.ResolvedCoordinateMismatch =>
            throw new InvalidOperationException(
                "Package dependency traversal never submits a restored-project resolution request."),
        _ => throw new InvalidOperationException(
            "Unknown package dependency candidate failure."),
    };

    private static PackageDependencyTraversalCandidateIncomplete ConvertIncomplete(
        PackageDependencyCandidateIncomplete evidence) => evidence switch
    {
        PackageDependencyCandidateIncomplete.PinnedAuthorization pinned =>
            new PackageDependencyTraversalCandidateIncomplete.PinnedAuthorization(
                [.. pinned.Failures]),
        PackageDependencyCandidateIncomplete.VersionDiscovery discovery =>
            new PackageDependencyTraversalCandidateIncomplete.VersionDiscovery(
                discovery.State,
                discovery.Contract,
                discovery.CandidateObservationCount,
                [.. discovery.Failures]),
        _ => throw new InvalidOperationException(
            "Unknown package dependency candidate incomplete evidence."),
    };
}
