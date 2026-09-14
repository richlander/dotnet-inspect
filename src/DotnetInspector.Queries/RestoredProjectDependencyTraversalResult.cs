using System.Collections.Immutable;
using InertText;

namespace DotnetInspector.Queries;

/// <summary>
/// The stable semantic identity of one traversal answer: the facts owner's selection identity plus
/// a deterministic digest over the admitted root-relative topology.
/// </summary>
/// <remarks>
/// The selection identity is unchanged by this owner. Two selections whose package facts are equal
/// but whose project-reference topology differs share a selection identity and differ here, so a
/// consumer cannot mistake one traversal answer for another.
/// </remarks>
public sealed record RestoredProjectTraversalIdentity
{
    public RestoredProjectTraversalIdentity(
        RestoredProjectSelectionIdentity selection,
        string topologyDigest)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (topologyDigest is not { Length: 64 } || !RestoredProjectIdentityText.IsLowerHex(topologyDigest))
        {
            throw new ArgumentException(
                "A traversal topology digest must be a lowercase 64-character SHA-256 hex string.",
                nameof(topologyDigest));
        }

        Selection = selection;
        TopologyDigest = topologyDigest;
    }

    /// <summary>The facts owner's unchanged selection identity.</summary>
    public RestoredProjectSelectionIdentity Selection { get; }

    /// <summary>Lowercase hex SHA-256 over the canonical admitted-topology encoding.</summary>
    public string TopologyDigest { get; }
}

/// <summary>
/// One node admitted by the traversal, with its minimum root-relative distance.
/// </summary>
/// <param name="Identity">
/// The facts owner's closed node union — the explicit restored root, a resolved project node, or a
/// resolved package node. This owner mints no parallel node identity.
/// </param>
/// <param name="MinimumDistance">
/// The fewest admitted relationships between the root and this node. The root is always <c>0</c>.
/// </param>
/// <param name="SourceProjectSpelling">
/// The exact authored target-entry spelling of a project node, contained as
/// <see cref="InertString"/>. It is present for exactly the project variant, and is never identity.
/// </param>
public sealed record RestoredProjectTraversalNode(
    RestoredProjectGraphParentIdentity Identity,
    int MinimumDistance,
    InertString? SourceProjectSpelling)
{
    public RestoredProjectGraphParentIdentity Identity { get; } =
        Identity ?? throw new ArgumentNullException(nameof(Identity));

    public int MinimumDistance { get; } = MinimumDistance >= 0
        ? MinimumDistance
        : throw new ArgumentOutOfRangeException(
            nameof(MinimumDistance),
            MinimumDistance,
            "A node distance cannot be negative.");

    public InertString? SourceProjectSpelling { get; } =
        (Identity is RestoredProjectGraphParentIdentity.Project) == (SourceProjectSpelling is not null)
            ? SourceProjectSpelling
            : throw new ArgumentException(
                "A project node requires its authored spelling, and a non-project node cannot carry one.",
                nameof(SourceProjectSpelling));
}

/// <summary>Identifies one project-resolving relationship by its parent node and project dependency.</summary>
public readonly record struct RestoredProjectProjectRelationshipIdentity(
    RestoredProjectGraphParentIdentity Parent,
    RestoredProjectProjectNodeIdentity Dependency);

/// <summary>
/// One admitted project-resolving relationship: the root or a graph node depending on a resolved
/// project node.
/// </summary>
/// <param name="Distance">
/// The admitted root-relative distance of this relationship — one more than the parent's minimum
/// distance.
/// </param>
public sealed record RestoredProjectTraversalProjectRelationship(
    RestoredProjectProjectRelationshipIdentity Identity,
    RestoredProjectGraphParentIdentity Parent,
    RestoredProjectProjectNodeIdentity Dependency,
    InertString SourceDependencySpelling,
    int Distance);

/// <summary>
/// One admitted package-resolving relationship. It carries the facts owner's exact
/// <see cref="RestoredProjectGraphEdge"/>; this owner never remints package edge identity, role,
/// constraint, or declaration association.
/// </summary>
public sealed record RestoredProjectTraversalPackageRelationship(
    RestoredProjectGraphEdge Edge,
    int Distance);

/// <summary>
/// One node whose outgoing relationships exist in the selected graph but were not admitted because
/// the request's maximum depth ends there.
/// </summary>
public sealed record RestoredProjectTraversalDepthBoundary(
    RestoredProjectGraphParentIdentity Node,
    int MaximumDepth);

/// <summary>The traversal's stated completion. It is carried explicitly, never inferred.</summary>
public enum RestoredProjectTraversalCompletion
{
    /// <summary>Every relationship the selected graph offers was admitted, with no failure in scope.</summary>
    Complete,

    /// <summary>The only unadmitted frontier is the request's explicit maximum depth.</summary>
    DepthBounded,

    /// <summary>Usable topology exists, but typed graph evidence inside the admitted depth is incomplete.</summary>
    Partial,
}

/// <summary>
/// One immutable root-relative traversal over an exact restored-project selection: the facts it was
/// projected from, typed nodes and relationships, explicit depth boundaries, in-scope typed
/// failures, and stated completion.
/// </summary>
public sealed record RestoredProjectDependencyTraversal
{
    public RestoredProjectDependencyTraversal(
        RestoredProjectDependencyFacts facts,
        RestoredProjectTraversalIdentity identity,
        int? maximumDepth,
        ImmutableArray<RestoredProjectTraversalNode> nodes,
        ImmutableArray<RestoredProjectTraversalProjectRelationship> projectRelationships,
        ImmutableArray<RestoredProjectTraversalPackageRelationship> packageRelationships,
        ImmutableArray<RestoredProjectTraversalDepthBoundary> depthBoundaries,
        ImmutableArray<RestoredProjectGraphFailure> failures,
        RestoredProjectTraversalCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(identity);
        if (nodes.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "An available traversal always contains at least its explicit root node.",
                nameof(nodes));
        }

        bool hasFailures = !failures.IsDefaultOrEmpty;
        bool hasBoundaries = !depthBoundaries.IsDefaultOrEmpty;
        bool consistent = completion switch
        {
            RestoredProjectTraversalCompletion.Complete => !hasFailures && !hasBoundaries,
            RestoredProjectTraversalCompletion.DepthBounded => !hasFailures && hasBoundaries,
            RestoredProjectTraversalCompletion.Partial => hasFailures,
            _ => false,
        };

        if (!consistent)
        {
            throw new ArgumentException(
                "Traversal completion must agree with its depth boundaries and in-scope failures.",
                nameof(completion));
        }

        Facts = facts;
        Identity = identity;
        MaximumDepth = maximumDepth;
        Nodes = nodes;
        ProjectRelationships = projectRelationships.IsDefault ? [] : projectRelationships;
        PackageRelationships = packageRelationships.IsDefault ? [] : packageRelationships;
        DepthBoundaries = depthBoundaries.IsDefault ? [] : depthBoundaries;
        Failures = failures.IsDefault ? [] : failures;
        Completion = completion;
    }

    /// <summary>The facts projection this traversal was computed from, unchanged.</summary>
    public RestoredProjectDependencyFacts Facts { get; }

    public RestoredProjectTraversalIdentity Identity { get; }

    /// <summary>The explicit restored project this traversal is relative to.</summary>
    public RestoredProjectRootIdentity Root => Facts.Root;

    /// <summary>The requested maximum depth, or <see langword="null"/> for the whole selected graph.</summary>
    public int? MaximumDepth { get; }

    public ImmutableArray<RestoredProjectTraversalNode> Nodes { get; }

    public ImmutableArray<RestoredProjectTraversalProjectRelationship> ProjectRelationships { get; }

    public ImmutableArray<RestoredProjectTraversalPackageRelationship> PackageRelationships { get; }

    public ImmutableArray<RestoredProjectTraversalDepthBoundary> DepthBoundaries { get; }

    /// <summary>The facts owner's typed graph failures whose occurrence lies inside the admitted depth.</summary>
    public ImmutableArray<RestoredProjectGraphFailure> Failures { get; }

    public RestoredProjectTraversalCompletion Completion { get; }

    public bool IsComplete => Completion == RestoredProjectTraversalCompletion.Complete;
}

/// <summary>Why a traversal could not be produced, preserving the owning phase's typed failure.</summary>
public abstract record RestoredProjectDependencyTraversalFailure
{
    private RestoredProjectDependencyTraversalFailure()
    {
    }

    /// <summary>The whole assets document could not be admitted.</summary>
    public sealed record Document(RestoredProjectDependencyFailure Failure) :
        RestoredProjectDependencyTraversalFailure;

    /// <summary>The document was admitted, but the selected graph itself failed.</summary>
    public sealed record Graph(
        RestoredProjectDependencyFacts Facts,
        RestoredProjectGraphFailure Failure) : RestoredProjectDependencyTraversalFailure;
}

/// <summary>The closed outcome of executing the restored-project dependency traversal query.</summary>
public abstract record RestoredProjectDependencyTraversalResult
{
    private RestoredProjectDependencyTraversalResult()
    {
    }

    public sealed record Available(RestoredProjectDependencyTraversal Value) :
        RestoredProjectDependencyTraversalResult;

    /// <summary>
    /// The document is usable, but the selection offers no restored graph to traverse. The facts
    /// remain available so a consumer does not reproject them.
    /// </summary>
    public sealed record Unavailable(RestoredProjectDependencyFacts Facts) :
        RestoredProjectDependencyTraversalResult;

    public sealed record Failed(RestoredProjectDependencyTraversalFailure Failure) :
        RestoredProjectDependencyTraversalResult;
}
