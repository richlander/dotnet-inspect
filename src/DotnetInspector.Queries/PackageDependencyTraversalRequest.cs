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
/// The typed framework-selection mode every admitted root and transitive manifest
/// projection is constructed under. This is structural request currency, not a
/// reconstruction of <see cref="PackageDependencyEvidenceSelection.RequestedFramework"/>.
/// </summary>
public abstract record PackageDependencyTraversalFrameworkMode
{
    private PackageDependencyTraversalFrameworkMode()
    {
    }

    /// <summary>Gets the requested framework text to project every manifest against, or
    /// <see langword="null"/> for <see cref="ManifestDefault"/>.</summary>
    internal abstract string? RequestedFramework { get; }

    /// <summary>One validated canonical NuGet framework identity.</summary>
    public sealed record Exact :
        PackageDependencyTraversalFrameworkMode
    {
        internal Exact(string canonicalFramework)
        {
            CanonicalFramework = canonicalFramework;
        }

        public string CanonicalFramework { get; }

        internal override string? RequestedFramework => CanonicalFramework;
    }

    /// <summary>
    /// Each manifest uses the package dependency-group owner's explicit no-request
    /// selection policy; the result retains each node's independently selected
    /// framework rather than claiming one graph-wide target framework.
    /// </summary>
    public sealed record ManifestDefault : PackageDependencyTraversalFrameworkMode
    {
        internal static ManifestDefault Instance { get; } = new();

        internal override string? RequestedFramework => null;
    }

    /// <summary>
    /// Validates and canonicalizes <paramref name="framework"/> through the same
    /// canonical NuGet framework identity every other exact-framework request uses.
    /// </summary>
    public static bool TryCreateExact(string framework, out Exact mode)
    {
        if (NuGetTargetFrameworkIdentity.TryNormalize(
                framework,
                out string canonical))
        {
            mode = new Exact(canonical);
            return true;
        }

        mode = null!;
        return false;
    }
}

/// <summary>One admitted package root and the expansion authority it carries.</summary>
public sealed record PackageDependencyTraversalRootOccurrence
{
    public PackageDependencyTraversalRootOccurrence(
        PackageDependencyEvidenceRoot root,
        PackageDependencyTraversalExpansionAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(root);
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

        Root = root;
        Authority = authority;
    }

    public PackageDependencyEvidenceRoot Root { get; }

    public PackageDependencyTraversalExpansionAuthority Authority { get; }

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
        PackageDependencyTraversalFrameworkMode frameworkMode,
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

        ArgumentNullException.ThrowIfNull(frameworkMode);
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
        FrameworkMode = frameworkMode;
        CandidateResolver = candidateResolver;
        ManifestAcquirer = manifestAcquirer;
        WorkBudget = workBudget;
        MaxDepth = maxDepth;
    }

    public ImmutableArray<PackageDependencyTraversalRootOccurrence> Roots { get; }

    public PackageDependencyTraversalFrameworkMode FrameworkMode { get; }

    public IPackageDependencyTraversalCandidateResolver CandidateResolver { get; }

    public IPackageDependencyTraversalManifestAcquirer ManifestAcquirer { get; }

    public PackageDependencyTraversalWorkBudget WorkBudget { get; }

    public int? MaxDepth { get; }
}
