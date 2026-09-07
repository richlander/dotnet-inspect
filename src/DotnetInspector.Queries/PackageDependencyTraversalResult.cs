using System.Collections.Immutable;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>How one manifest projection's evidence reached the traversal graph.</summary>
public enum PackageDependencyTraversalProjectionKind
{
    /// <summary>An explicit root occurrence supplied this projection's evidence directly.</summary>
    RootSupplied,

    /// <summary>A #5765 candidate correspondence authorized exact manifest acquisition.</summary>
    CandidateAcquired,
}

/// <summary>Whether a candidate-acquired projection has been expanded into declarations.</summary>
public enum PackageDependencyTraversalProjectionExpansion
{
    /// <summary>No root within depth and work-budget bounds has required expansion yet.</summary>
    NotExpanded,

    /// <summary>Declarations and outgoing edges were computed, or acquisition/projection failed.</summary>
    Expanded,
}

/// <summary>A semantic package node identified by exact canonical coordinate.</summary>
public sealed record PackageDependencyTraversalNode(
    PackageSourceCoordinate Coordinate,
    ImmutableArray<int> ProjectionIndexes);

/// <summary>
/// One authority-bearing observation of an exact package coordinate: a root-bound
/// supplied projection, or a #5765 candidate correspondence plus exact manifest.
/// Two feeds may publish different bytes for the same coordinate, so non-equivalent
/// projections remain separate beneath their shared semantic node.
/// </summary>
public sealed record PackageDependencyTraversalProjection(
    int NodeIndex,
    PackageDependencyTraversalProjectionKind Kind,
    PackageDependencyTraversalProjectionExpansion Expansion,
    PackageDependencyEvidenceRoot? Evidence,
    PackageAcquisitionCandidate? Candidate,
    int? RootOccurrenceIndex,
    ImmutableArray<PackageAuthorityFailure> Diagnostics,
    ImmutableArray<int> OutgoingEdgeIndexes);

/// <summary>
/// A dependency target for which the root's authority permits direct evidence but not
/// exact candidate resolution. Boundary nodes never participate in cycle coalescing or
/// recursive expansion.
/// </summary>
public sealed record PackageDependencyTraversalDeclarationBoundaryNode(
    int SourceProjectionIndex,
    PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
    string CanonicalPackageId,
    string CanonicalVersionConstraint,
    ImmutableArray<int> AffectedRootOccurrences);

/// <summary>
/// A recursively authorized dependency declaration for which #5765 did not issue an
/// exact candidate. It is not a guessed package coordinate and has no outgoing expansion.
/// </summary>
public sealed record PackageDependencyTraversalFailedResolutionNode(
    int SourceProjectionIndex,
    PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
    string CanonicalPackageId,
    string CanonicalVersionConstraint,
    PackageDependencyTraversalCandidateResult Outcome,
    ImmutableArray<int> AffectedRootOccurrences);

/// <summary>
/// A recursively authorized normalized declaration selected from an acquired manifest
/// that could not be submitted to #5765 because the declaration-resolution budget was
/// exhausted. It does not claim a resolution attempt or source failure.
/// </summary>
public sealed record PackageDependencyTraversalWorkBudgetNode(
    int SourceProjectionIndex,
    PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
    string CanonicalPackageId,
    string CanonicalVersionConstraint,
    PackageDependencyTraversalWorkBudgetKind BudgetKind,
    int Limit,
    ImmutableArray<int> AffectedRootOccurrences);

/// <summary>How one declaration edge was emitted.</summary>
public enum PackageDependencyTraversalEdgeEmissionAuthority
{
    /// <summary>Produced by recursive source authorization that issued an exact candidate.</summary>
    ResolvedCandidate,

    /// <summary>Produced by recursive source authorization when #5765 issued no exact candidate.</summary>
    FailedResolution,

    /// <summary>Produced by recursive source authorization when the declaration-resolution
    /// budget could not admit the #5765 attempt.</summary>
    WorkBudgetBoundary,

    /// <summary>Produced for one exact direct-only root occurrence.</summary>
    DirectBoundary,
}

/// <summary>The closed target of one declaration edge.</summary>
public abstract record PackageDependencyTraversalEdgeTarget
{
    private PackageDependencyTraversalEdgeTarget()
    {
    }

    public sealed record Node(int NodeIndex, int ProjectionIndex) :
        PackageDependencyTraversalEdgeTarget;

    public sealed record DeclarationBoundary(int BoundaryNodeIndex) :
        PackageDependencyTraversalEdgeTarget;

    public sealed record FailedResolution(int NodeIndex) :
        PackageDependencyTraversalEdgeTarget;

    public sealed record WorkBudget(int NodeIndex) :
        PackageDependencyTraversalEdgeTarget;
}

/// <summary>
/// One graph edge representing one normalized selected declaration. Every distinct
/// directed declaration edge remains present; shared targets, cycles, and revisits
/// never justify deleting an edge.
/// </summary>
public sealed record PackageDependencyTraversalEdge(
    int SourceProjectionIndex,
    PackageSourceCoordinate SourceCoordinate,
    PackageDependencyEvidenceDeclaration Declaration,
    PackageDependencyTraversalEdgeTarget Target,
    PackageDependencyTraversalEdgeEmissionAuthority Authority,
    ImmutableArray<PackageAuthorityFailure> Diagnostics);

/// <summary>
/// Which projection-scoped phase failed. Per-declaration candidate-resolution and
/// declaration-resolution-budget failures are instead retained by
/// <see cref="PackageDependencyTraversalFailedResolutionNode"/> and
/// <see cref="PackageDependencyTraversalWorkBudgetNode"/> beside their edge.
/// </summary>
public abstract record PackageDependencyTraversalManifestFailureDetail
{
    private PackageDependencyTraversalManifestFailureDetail()
    {
    }

    /// <summary>No admitted authority could supply the candidate-authorized manifest.</summary>
    public sealed record Acquisition(
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyTraversalManifestFailureDetail;

    /// <summary>The shared operation deadline expired before manifest acquisition settled.</summary>
    public sealed record IncompleteAcquisition(
        ImmutableArray<PackageAuthorityFailure> Failures) :
        PackageDependencyTraversalManifestFailureDetail;

    /// <summary>The acquired manifest failed identity, decode, or dependency-contract validation.</summary>
    public sealed record Identity(PackageManifestFailure Failure) :
        PackageDependencyTraversalManifestFailureDetail;

    /// <summary>The selected declaration group carries an owner-issued declaration failure.</summary>
    public sealed record Declaration(
        PackageDependencyEvidenceDeclarationFailure Failure) :
        PackageDependencyTraversalManifestFailureDetail;

    /// <summary>The manifest-projection work budget could not admit exact acquisition.</summary>
    public sealed record ManifestProjectionBudgetExhausted(int Limit) :
        PackageDependencyTraversalManifestFailureDetail;
}

/// <summary>
/// One projection-scoped failure. A shared-node failure may affect several roots while
/// remaining one failed operation.
/// </summary>
public sealed record PackageDependencyTraversalFailure(
    int NodeIndex,
    int ProjectionIndex,
    PackageDependencyTraversalManifestFailureDetail Detail,
    ImmutableArray<int> AffectedRootOccurrences);

/// <summary>
/// One known package projection whose selected declaration frontier remains
/// intentionally unexpanded at the requested maximum depth.
/// </summary>
public sealed record PackageDependencyTraversalDepthBoundary(
    int NodeIndex,
    int ProjectionIndex,
    int MaximumDepth,
    ImmutableArray<int> AffectedRootOccurrences);

/// <summary>Each root occurrence's traversal completion.</summary>
public enum PackageDependencyTraversalRootCompletion
{
    /// <summary>Every reachable declaration was resolved and every required manifest
    /// projection completed within the requested boundary.</summary>
    Complete,

    /// <summary>The only unexpanded frontier is caused by the explicit maximum depth.</summary>
    DepthBounded,

    /// <summary>One or more direct declaration boundaries remain because the root did not
    /// authorize recursive source work.</summary>
    SourceBounded,

    /// <summary>Usable graph evidence exists, but candidate, source, manifest, projection, or
    /// work-budget evidence inside the requested boundary is incomplete or failed.</summary>
    Partial,
}

/// <summary>One root occurrence's admitted node, projection, and traversal completion.</summary>
public sealed record PackageDependencyTraversalRootResult(
    int OccurrenceIndex,
    PackageDependencyTraversalRootOccurrence Occurrence,
    int NodeIndex,
    int ProjectionIndex,
    PackageDependencyTraversalRootCompletion Completion);

/// <summary>
/// Root-relative reachability for one root occurrence: the minimum discovered edge
/// distance for each reachable node and manifest projection, and each edge's admitted
/// occurrence distance.
/// </summary>
public sealed record PackageDependencyTraversalReachability(
    ImmutableDictionary<int, int> NodeDistances,
    ImmutableDictionary<int, int> ProjectionDistances,
    ImmutableDictionary<int, int> EdgeDistances)
{
    public bool IsNodeReachable(int nodeIndex) =>
        NodeDistances.ContainsKey(nodeIndex);

    public bool IsEdgeAdmitted(int edgeIndex, out int distance) =>
        EdgeDistances.TryGetValue(edgeIndex, out distance);
}

/// <summary>Aggregate per-root completion counts and overall success.</summary>
public sealed record PackageDependencyTraversalSummary(
    int CompleteRoots,
    int DepthBoundedRoots,
    int SourceBoundedRoots,
    int PartialRoots)
{
    /// <summary>Every root occurrence is <see cref="PackageDependencyTraversalRootCompletion.Complete"/>.</summary>
    public bool IsComplete =>
        DepthBoundedRoots == 0 && SourceBoundedRoots == 0 && PartialRoots == 0;

    /// <summary>Every root occurrence is <c>Complete</c>, <c>DepthBounded</c>, or <c>SourceBounded</c>.</summary>
    public bool IsSuccessful => PartialRoots == 0;
}

/// <summary>
/// The immutable, depth-bounded directed graph produced by one package dependency
/// traversal operation: root-relative reachability, typed failures, and completion.
/// </summary>
public sealed record PackageDependencyTraversalOutcome(
    ImmutableArray<PackageDependencyTraversalRootResult> Roots,
    ImmutableArray<PackageDependencyTraversalReachability> RootReachability,
    ImmutableArray<PackageDependencyTraversalNode> Nodes,
    ImmutableArray<PackageDependencyTraversalProjection> Projections,
    ImmutableArray<PackageDependencyTraversalEdge> Edges,
    ImmutableArray<PackageDependencyTraversalDeclarationBoundaryNode>
        DeclarationBoundaries,
    ImmutableArray<PackageDependencyTraversalFailedResolutionNode>
        FailedResolutions,
    ImmutableArray<PackageDependencyTraversalWorkBudgetNode>
        WorkBudgetDeclarations,
    ImmutableArray<PackageDependencyTraversalFailure> Failures,
    ImmutableArray<PackageDependencyTraversalDepthBoundary> DepthBoundaries,
    PackageDependencyTraversalSummary Summary)
{
    public bool IsComplete => Summary.IsComplete;

    public bool IsSuccessful => Summary.IsSuccessful;
}
