using System.Collections.Immutable;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// Whether one root occurrence authorizes recursive candidate resolution and exact
/// manifest acquisition, or only its own owner-issued direct declaration edges.
/// </summary>
public enum PackageDependencyTraversalExpansionAuthority
{
    /// <summary>
    /// Selected declarations may use #5765 candidate resolution and exact manifest
    /// acquisition within the request's source context.
    /// </summary>
    RecursiveSources,

    /// <summary>
    /// Only owner-issued direct declaration edges are authorized; no candidate or
    /// manifest operation is performed for this root's declarations.
    /// </summary>
    DirectDeclarationsOnly,
}

/// <summary>
/// Whether an exact dependency may close onto root-supplied evidence without
/// owner-issued candidate correspondence.
/// </summary>
public enum PackageDependencyTraversalRootRecurrenceAuthority
{
    /// <summary>
    /// The root owner has not authorized coordinate-only recurrence.
    /// </summary>
    None,

    /// <summary>
    /// The root owner authorizes its admitted evidence for an exact recurrence of
    /// the same canonical coordinate.
    /// </summary>
    ExactCoordinate,
}

/// <summary>
/// The owner-issued evidence source for one admitted package traversal root.
/// </summary>
public abstract record PackageDependencyTraversalRootSource
{
    private protected PackageDependencyTraversalRootSource()
    {
    }

    internal abstract PackageDependencyEvidenceRoot Evidence { get; }

    /// <summary>
    /// A package dependency-evidence root whose association is owned by its
    /// supplying adapter rather than by a realized package participant.
    /// </summary>
    public sealed record ProjectedEvidence :
        PackageDependencyTraversalRootSource
    {
        internal ProjectedEvidence(PackageDependencyEvidenceRoot evidence) =>
            Root = evidence ?? throw new ArgumentNullException(nameof(evidence));

        public PackageDependencyEvidenceRoot Root { get; }

        internal override PackageDependencyEvidenceRoot Evidence => Root;
    }

    /// <summary>
    /// Dependency evidence associated with one exact realized package
    /// participant.
    /// </summary>
    public sealed record RealizedPackage :
        PackageDependencyTraversalRootSource
    {
        internal RealizedPackage(RealizedPackageDependencyContext context) =>
            Context = context ?? throw new ArgumentNullException(nameof(context));

        public RealizedPackageDependencyContext Context { get; }

        internal override PackageDependencyEvidenceRoot Evidence =>
            Context.Evidence;
    }
}

/// <summary>One admitted package root and the expansion authority it carries.</summary>
public sealed record PackageDependencyTraversalRootOccurrence
{
    public PackageDependencyTraversalRootOccurrence(
        PackageDependencyEvidenceRoot root,
        PackageDependencyTraversalExpansionAuthority authority,
        PackageDependencyTraversalRootRecurrenceAuthority recurrenceAuthority =
            PackageDependencyTraversalRootRecurrenceAuthority.None)
        : this(
            new PackageDependencyTraversalRootSource.ProjectedEvidence(root),
            authority,
            recurrenceAuthority)
    {
    }

    public PackageDependencyTraversalRootOccurrence(
        RealizedPackageDependencyContext context,
        PackageDependencyTraversalExpansionAuthority authority,
        PackageDependencyTraversalRootRecurrenceAuthority recurrenceAuthority =
            PackageDependencyTraversalRootRecurrenceAuthority.None)
        : this(
            new PackageDependencyTraversalRootSource.RealizedPackage(context),
            authority,
            recurrenceAuthority)
    {
    }

    private PackageDependencyTraversalRootOccurrence(
        PackageDependencyTraversalRootSource source,
        PackageDependencyTraversalExpansionAuthority authority,
        PackageDependencyTraversalRootRecurrenceAuthority recurrenceAuthority)
    {
        ArgumentNullException.ThrowIfNull(source);
        PackageDependencyEvidenceRoot root = source.Evidence;
        if (root.Identity is not PackageDependencyEvidenceRootIdentity.Package)
        {
            throw new ArgumentException(
                "Package dependency traversal roots must carry an exact package identity.",
                nameof(root));
        }

        if (root.Declaration is not PackageDependencyEvidenceDeclarationResult
                .Available)
        {
            throw new ArgumentException(
                "Package dependency traversal roots must carry an available declaration projection.",
                nameof(root));
        }

        Source = source;
        Authority = authority;
        RecurrenceAuthority = recurrenceAuthority;
    }

    public PackageDependencyTraversalRootSource Source { get; }

    public PackageDependencyEvidenceRoot Root => Source.Evidence;

    public PackageDependencyTraversalExpansionAuthority Authority { get; }

    public PackageDependencyTraversalRootRecurrenceAuthority
        RecurrenceAuthority
    { get; }

    internal PackageSourceCoordinate Coordinate =>
        ((PackageDependencyEvidenceRootIdentity.Package)Root.Identity).Coordinate;
}

/// <summary>
/// The finite traversal work budget. Depth is user intent; this budget is a
/// resource-safety limit whose exhaustion produces typed partial completion.
/// </summary>
public sealed record PackageDependencyTraversalWorkBudget
{
    public PackageDependencyTraversalWorkBudget(
        int maxManifestProjections,
        int maxDeclarationResolutions)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maxManifestProjections,
            0);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maxDeclarationResolutions,
            0);
        MaxManifestProjections = maxManifestProjections;
        MaxDeclarationResolutions = maxDeclarationResolutions;
    }

    /// <summary>The maximum number of exact manifests this operation may acquire.</summary>
    public int MaxManifestProjections { get; }

    /// <summary>The maximum number of declarations this operation may submit for
    /// candidate resolution.</summary>
    public int MaxDeclarationResolutions { get; }
}

/// <summary>Which finite work budget a traversal frontier could not admit.</summary>
public enum PackageDependencyTraversalWorkBudgetKind
{
    /// <summary>The declaration-resolution budget could not admit a #5765 attempt.</summary>
    DeclarationResolution,
}

/// <summary>The complete immutable request for one package dependency traversal operation.</summary>
public sealed record PackageDependencyTraversalRequest
{
    public PackageDependencyTraversalRequest(
        ImmutableArray<PackageDependencyTraversalRootOccurrence> roots,
        TraversalTargetFrameworkPolicy traversalTargetPolicy,
        IPackageDependencyTraversalCandidateResolver candidateResolver,
        IPackageDependencyTraversalManifestAcquirer manifestAcquirer,
        PackageDependencyTraversalWorkBudget workBudget,
        int? maxDepth = null)
    {
        if (roots.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Package dependency traversal requires at least one root occurrence.",
                nameof(roots));
        }

        ArgumentNullException.ThrowIfNull(traversalTargetPolicy);
        ArgumentNullException.ThrowIfNull(candidateResolver);
        ArgumentNullException.ThrowIfNull(manifestAcquirer);
        ArgumentNullException.ThrowIfNull(workBudget);
        if (maxDepth is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDepth),
                maxDepth,
                "A maximum depth cannot be negative.");
        }

        Roots = roots;
        TraversalTargetPolicy = traversalTargetPolicy;
        CandidateResolver = candidateResolver;
        ManifestAcquirer = manifestAcquirer;
        WorkBudget = workBudget;
        MaxDepth = maxDepth;
    }

    public ImmutableArray<PackageDependencyTraversalRootOccurrence> Roots { get; }

    public TraversalTargetFrameworkPolicy TraversalTargetPolicy { get; }

    public IPackageDependencyTraversalCandidateResolver CandidateResolver { get; }

    public IPackageDependencyTraversalManifestAcquirer ManifestAcquirer { get; }

    public PackageDependencyTraversalWorkBudget WorkBudget { get; }

    public int? MaxDepth { get; }
}
