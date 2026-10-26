using System.Collections.Immutable;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services;

/// <summary>
/// Resolves transitive NuGet dependency trees from nuspec dependency groups.
/// </summary>
public static class DependencyResolutionService
{
    public enum DependencyGroupSelectionStatus
    {
        Selected,
        NoDependencyGroups,
        NoMatchingTargetFramework
    }

    public sealed record DependencyGroupSelection(
        DependencyGroup? Group,
        string? TargetFramework,
        DependencyGroupSelectionStatus Status,
        IReadOnlyList<string> AvailableTargetFrameworks)
    {
        public bool IsSelected => Status == DependencyGroupSelectionStatus.Selected && Group != null;
    }

    /// <summary>
    /// Selection over immutable dependency declarations retained from one
    /// package manifest.
    /// </summary>
    public sealed record DeclaredDependencyGroupSelection(
        DeclaredPackageDependencyGroup? Group,
        int? GroupIndex,
        string? TargetFramework,
        DependencyGroupSelectionStatus Status);

    /// <summary>
    /// Resolves the full transitive dependency tree for a set of direct dependencies.
    /// </summary>
    public static async Task<DependencyResolutionResult<List<DependencyNode>>>
        ResolveDependencyTreeAsync(
        HttpClient client, List<PackageDependency> dependencies, string tfm,
        HashSet<string> globalSeen, Action<string>? log,
        NuGetSourceOptions? sourceOptions = null)
    {
        var diagnostics =
            ImmutableArray.CreateBuilder<DependencyResolutionDiagnostic>();
        List<DependencyNode> tree = await ResolveDependencyTreeCoreAsync(
            client,
            dependencies,
            tfm,
            globalSeen,
            log,
            sourceOptions,
            source: null,
            graph: null,
            expandedGraphCoordinates: null,
            externallySeenPackageIds: null,
            includeTreeBranch: true,
            diagnostics).ConfigureAwait(false);
        return new(tree, diagnostics.ToImmutable());
    }

    /// <summary>
    /// Resolves package dependencies while retaining every direct relationship
    /// independently of expansion. A repeated exact coordinate in the same
    /// target-framework context stops recursive work but never deletes the
    /// edge that reached it.
    /// </summary>
    public static async Task<DependencyResolutionResult<PackageDependencyGraph>>
        ResolveDependencyGraphAsync(
        HttpClient client,
        PackageDependencyIdentity root,
        string? rootAuthor,
        List<PackageDependency> dependencies,
        string tfm,
        HashSet<string> globalSeen,
        Action<string>? log,
        NuGetSourceOptions? sourceOptions = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var externallySeenPackageIds = new HashSet<string>(
            globalSeen,
            StringComparer.OrdinalIgnoreCase);
        var graph = new PackageDependencyGraphBuilder();
        graph.AddNode(root, rootAuthor);
        graph.SetNodeResolution(
            root,
            PackageDependencyResolutionState.Resolved);
        var diagnostics =
            ImmutableArray.CreateBuilder<DependencyResolutionDiagnostic>();
        globalSeen.Add(root.PackageId);
        var expandedGraphCoordinates =
            new HashSet<PackageDependencyExpansionKey>(
                PackageDependencyExpansionKeyComparer.Instance)
            {
                new(root, tfm),
            };
        List<DependencyNode> tree = await ResolveDependencyTreeCoreAsync(
            client,
            dependencies,
            tfm,
            globalSeen,
            log,
            sourceOptions,
            root,
            graph,
            expandedGraphCoordinates,
            externallySeenPackageIds,
            includeTreeBranch: true,
            diagnostics).ConfigureAwait(false);
        return new(graph.Build(tree), diagnostics.ToImmutable());
    }

    private static async Task<List<DependencyNode>>
        ResolveDependencyTreeCoreAsync(
        HttpClient client,
        List<PackageDependency> dependencies,
        string tfm,
        HashSet<string> globalSeen,
        Action<string>? log,
        NuGetSourceOptions? sourceOptions,
        PackageDependencyIdentity? source,
        PackageDependencyGraphBuilder? graph,
        HashSet<PackageDependencyExpansionKey>?
            expandedGraphCoordinates,
        HashSet<string>? externallySeenPackageIds,
        bool includeTreeBranch,
        ImmutableArray<DependencyResolutionDiagnostic>.Builder diagnostics)
    {
        List<DependencyNode> nodes = [];

        foreach (var dep in dependencies.OrderBy(d => d.Id))
        {
            string? resolvedVersion = ResolveVersionFromRange(dep.Version);
            var target = new PackageDependencyIdentity(
                dep.Id,
                resolvedVersion ?? dep.Version);
            graph?.AddNode(target, author: null);
            if (source is not null)
            {
                graph?.AddRelationship(
                    source,
                    target,
                    dep.Version);
            }

            bool includeTreeNode =
                includeTreeBranch
                && globalSeen.Add(dep.Id);
            bool expandGraph =
                graph is not null
                && !externallySeenPackageIds!.Contains(dep.Id)
                && expandedGraphCoordinates!.Add(
                    new PackageDependencyExpansionKey(
                        target,
                        tfm));
            if (!includeTreeNode && !expandGraph)
                continue;

            log?.Invoke($"Resolving: {dep.Id} {dep.Version}");

            var resolution = await ResolveChildDependenciesAsync(
                client,
                dep.Id,
                dep.Version,
                tfm,
                globalSeen,
                log,
                sourceOptions,
                target,
                expandGraph ? graph : null,
                expandGraph ? expandedGraphCoordinates : null,
                expandGraph ? externallySeenPackageIds : null,
                includeTreeNode,
                diagnostics).ConfigureAwait(false);

            if (expandGraph)
            {
                graph!.AddNode(target, resolution.Author);
                graph.SetNodeResolution(target, resolution.State);
            }
            if (includeTreeNode)
            {
                nodes.Add(
                    new DependencyNode(
                        dep.Id,
                        dep.Version,
                        resolution.Author,
                        resolution.Children));
            }
        }

        return nodes;
    }

    /// <summary>
    /// Finds the best matching TFM dependency group for a target TFM.
    /// </summary>
    public static DependencyGroup? FindBestMatchingTfmGroup(List<DependencyGroup> groups, string targetTfm)
    {
        List<DependencyGroup> legacyGroups = LegacyResolutionGroups(groups);
        var exact = FindNormalizedExactGroup(legacyGroups, targetTfm);
        if (exact != null) return exact;

        var targetPriority = TfmSelector.GetTfmPriority(targetTfm);

        return TfmSelector.OrderByTfmPriorityDescending(
                legacyGroups.Where(g => string.IsNullOrEmpty(g.TargetFramework) ||
                                        g.TargetFramework.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                                        TfmSelector.GetTfmPriority(g.TargetFramework) <= targetPriority),
                g => g.TargetFramework)
            .FirstOrDefault();
    }

    /// <summary>
    /// Selects the dependency group to use for a package dependency tree.
    /// </summary>
    public static DependencyGroupSelection SelectDependencyGroup(
        List<DependencyGroup>? groups,
        string? requestedTfm,
        bool allowCompatibleFallbackForRequestedTfm = true)
    {
        if (groups is not { Count: > 0 })
        {
            return new DependencyGroupSelection(null, requestedTfm, DependencyGroupSelectionStatus.NoDependencyGroups, []);
        }

        var availableTfms = groups.Select(g => g.TargetFramework).ToArray();
        if (requestedTfm != null)
        {
            var group = allowCompatibleFallbackForRequestedTfm
                ? FindBestMatchingTfmGroup(groups, requestedTfm)
                : FindExactOrUniversalGroup(groups, requestedTfm);

            return group == null
                ? new DependencyGroupSelection(null, requestedTfm, DependencyGroupSelectionStatus.NoMatchingTargetFramework, availableTfms)
                : new DependencyGroupSelection(group, requestedTfm, DependencyGroupSelectionStatus.Selected, availableTfms);
        }

        IEnumerable<DependencyGroup> candidates =
            allowCompatibleFallbackForRequestedTfm
                ? LegacyResolutionGroups(groups)
                : groups;
        var highest = TfmSelector.OrderByTfmPriorityDescending(
                candidates,
                g => g.TargetFramework)
            .First();
        return new DependencyGroupSelection(highest, highest.TargetFramework, DependencyGroupSelectionStatus.Selected, availableTfms);
    }

    /// <summary>
    /// Selects directly from immutable manifest declarations without
    /// projecting them through the legacy mutable dependency model.
    /// </summary>
    public static DeclaredDependencyGroupSelection SelectDependencyGroup(
        ImmutableArray<DeclaredPackageDependencyGroup> groups,
        string? requestedTfm,
        bool allowCompatibleFallbackForRequestedTfm = true)
    {
        if (groups.IsEmpty)
        {
            return new(
                Group: null,
                GroupIndex: null,
                requestedTfm,
                DependencyGroupSelectionStatus.NoDependencyGroups);
        }

        if (!allowCompatibleFallbackForRequestedTfm)
        {
            (DeclaredPackageDependencyGroup Group, int Index)? selected =
                requestedTfm is null
                    ? HighestPriorityGroup(groups)
                    : FindNormalizedExactGroup(groups, requestedTfm)
                        ?? FindUniversalGroup(groups);
            return selected is { } match
                ? new(
                    match.Group,
                    match.Index,
                    requestedTfm ?? match.Group.TargetFramework,
                    DependencyGroupSelectionStatus.Selected)
                : new(
                    Group: null,
                    GroupIndex: null,
                    requestedTfm,
                    DependencyGroupSelectionStatus
                        .NoMatchingTargetFramework);
        }

        List<(DeclaredPackageDependencyGroup Group, int Index)> candidates =
            LegacyResolutionGroups(groups);
        if (requestedTfm is not null)
        {
            (DeclaredPackageDependencyGroup Group, int Index)? selected =
                FindNormalizedExactGroup(candidates, requestedTfm);
            if (selected is null && allowCompatibleFallbackForRequestedTfm)
            {
                int targetPriority =
                    TfmSelector.GetTfmPriority(requestedTfm);
                selected = HighestPriorityGroup(
                    candidates,
                    group =>
                        string.IsNullOrEmpty(group.TargetFramework)
                        || group.TargetFramework.Equals(
                            "any",
                            StringComparison.OrdinalIgnoreCase)
                        || TfmSelector.GetTfmPriority(
                            group.TargetFramework) <= targetPriority);
            }

            return selected is { } match
                ? new(
                    match.Group,
                    match.Index,
                    requestedTfm,
                    DependencyGroupSelectionStatus.Selected)
                : new(
                    Group: null,
                    GroupIndex: null,
                    requestedTfm,
                    DependencyGroupSelectionStatus
                        .NoMatchingTargetFramework);
        }

        (DeclaredPackageDependencyGroup Group, int Index) highest =
            HighestPriorityGroup(candidates, static _ => true)!.Value;
        return new(
            highest.Group,
            highest.Index,
            highest.Group.TargetFramework,
            DependencyGroupSelectionStatus.Selected);
    }

    static List<DependencyGroup> LegacyResolutionGroups(
        IEnumerable<DependencyGroup> groups)
    {
        var result = new List<DependencyGroup>();
        List<PackageDependency>? implicitDependencies = null;
        foreach (DependencyGroup group in groups)
        {
            if (!group.IsImplicitManifestGroup)
            {
                result.Add(group);
                continue;
            }

            implicitDependencies ??= [];
            implicitDependencies.AddRange(group.Dependencies);
        }

        if (implicitDependencies is not null)
        {
            result.Add(new DependencyGroup
            {
                TargetFramework = "any",
                Dependencies = implicitDependencies,
                IsImplicitManifestGroup = true,
            });
        }

        return result;
    }

    static List<(DeclaredPackageDependencyGroup Group, int Index)>
        LegacyResolutionGroups(
            ImmutableArray<DeclaredPackageDependencyGroup> groups)
    {
        var result = new List<
            (DeclaredPackageDependencyGroup Group, int Index)>(
                groups.Length);
        ImmutableArray<DeclaredPackageDependency>.Builder?
            implicitDependencies = null;
        int implicitIndex = -1;
        for (int index = 0; index < groups.Length; index++)
        {
            DeclaredPackageDependencyGroup group = groups[index];
            if (!group.IsImplicitManifestGroup)
            {
                result.Add((group, index));
                continue;
            }

            if (implicitDependencies is null)
            {
                implicitDependencies =
                    ImmutableArray.CreateBuilder<
                        DeclaredPackageDependency>();
                implicitIndex = index;
            }
            implicitDependencies.AddRange(group.Dependencies);
        }

        if (implicitDependencies is not null)
        {
            result.Add(
                (
                    new DeclaredPackageDependencyGroup(
                        "any",
                        implicitDependencies.ToImmutable(),
                        IsImplicitManifestGroup: true),
                    implicitIndex));
        }

        return result;
    }

    static (DeclaredPackageDependencyGroup Group, int Index)?
        FindNormalizedExactGroup(
            ImmutableArray<DeclaredPackageDependencyGroup> groups,
            string requestedTargetFramework)
    {
        string normalizedRequest =
            TfmSelector.NormalizeTfm(requestedTargetFramework);
        for (int index = 0; index < groups.Length; index++)
        {
            if (TfmSelector.NormalizeTfm(
                    groups[index].TargetFramework).Equals(
                    normalizedRequest,
                    StringComparison.OrdinalIgnoreCase))
            {
                return (groups[index], index);
            }
        }

        return null;
    }

    static (DeclaredPackageDependencyGroup Group, int Index)?
        FindUniversalGroup(
            ImmutableArray<DeclaredPackageDependencyGroup> groups)
    {
        for (int index = 0; index < groups.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(groups[index].TargetFramework)
                || groups[index].TargetFramework.Equals(
                    "any",
                    StringComparison.OrdinalIgnoreCase))
            {
                return (groups[index], index);
            }
        }

        return null;
    }

    static (DeclaredPackageDependencyGroup Group, int Index)
        HighestPriorityGroup(
            ImmutableArray<DeclaredPackageDependencyGroup> groups)
    {
        int selectedIndex = 0;
        int selectedPriority = TfmSelector.GetTfmPriority(
            groups[0].TargetFramework);
        for (int index = 1; index < groups.Length; index++)
        {
            int priority = TfmSelector.GetTfmPriority(
                groups[index].TargetFramework);
            if (priority > selectedPriority)
            {
                selectedIndex = index;
                selectedPriority = priority;
            }
        }

        return (groups[selectedIndex], selectedIndex);
    }

    static (DeclaredPackageDependencyGroup Group, int Index)?
        FindNormalizedExactGroup(
            List<(DeclaredPackageDependencyGroup Group, int Index)> groups,
            string requestedTargetFramework)
    {
        string normalizedRequest =
            TfmSelector.NormalizeTfm(requestedTargetFramework);
        foreach (var candidate in groups)
        {
            if (TfmSelector.NormalizeTfm(
                    candidate.Group.TargetFramework).Equals(
                    normalizedRequest,
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    static (DeclaredPackageDependencyGroup Group, int Index)?
        HighestPriorityGroup(
            List<(DeclaredPackageDependencyGroup Group, int Index)> groups,
            Func<DeclaredPackageDependencyGroup, bool> predicate)
    {
        (DeclaredPackageDependencyGroup Group, int Index)? selected = null;
        int selectedPriority = int.MinValue;
        foreach (var candidate in groups)
        {
            if (!predicate(candidate.Group))
                continue;

            int priority = TfmSelector.GetTfmPriority(
                candidate.Group.TargetFramework);
            if (selected is null || priority > selectedPriority)
            {
                selected = candidate;
                selectedPriority = priority;
            }
        }

        return selected;
    }

    static DependencyGroup? FindExactOrUniversalGroup(
        IEnumerable<DependencyGroup> groups,
        string requestedTargetFramework)
        => FindNormalizedExactGroup(groups, requestedTargetFramework)
            ?? groups.FirstOrDefault(group =>
                string.IsNullOrWhiteSpace(group.TargetFramework)
                || group.TargetFramework.Equals(
                    "any",
                    StringComparison.OrdinalIgnoreCase));

    static DependencyGroup? FindNormalizedExactGroup(
        IEnumerable<DependencyGroup> groups,
        string requestedTargetFramework)
    {
        string normalizedRequest = TfmSelector.NormalizeTfm(
            requestedTargetFramework);
        return groups.FirstOrDefault(group =>
            TfmSelector.NormalizeTfm(group.TargetFramework).Equals(
                normalizedRequest,
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<PackageDependencyResolution>
        ResolveChildDependenciesAsync(
        HttpClient client, string packageId, string versionRange, string tfm,
        HashSet<string> globalSeen, Action<string>? log,
        NuGetSourceOptions? sourceOptions,
        PackageDependencyIdentity source,
        PackageDependencyGraphBuilder? graph,
        HashSet<PackageDependencyExpansionKey>?
            expandedGraphCoordinates,
        HashSet<string>? externallySeenPackageIds,
        bool includeTreeBranch,
        ImmutableArray<DependencyResolutionDiagnostic>.Builder diagnostics)
    {
        string? version = ResolveVersionFromRange(versionRange);
        if (version == null)
        {
            diagnostics.Add(new(
                source,
                PackageDependencyResolutionState.Declared,
                DependencyResolutionDiagnosticKind.InvalidVersionRange));
            return new PackageDependencyResolution(
                [],
                Author: null,
                PackageDependencyResolutionState.Declared);
        }

        // Resolving the tree only needs each package's dependency groups, so fetch just the
        // nuspec (from cache or the flat-container endpoint) instead of downloading and
        // extracting the whole .nupkg.
        string? nuspecXml = await DotnetInspector.Packages.PackageExtractor.TryGetNuspecXmlAsync(
            client,
            packageId,
            version,
            log,
            sourceOptions).ConfigureAwait(false);
        if (nuspecXml == null)
        {
            diagnostics.Add(new(
                source,
                PackageDependencyResolutionState.Unavailable,
                DependencyResolutionDiagnosticKind.ManifestUnavailable));
            return new PackageDependencyResolution(
                [],
                Author: null,
                PackageDependencyResolutionState.Unavailable);
        }

        var nuspec = NuspecParser.ParseContent(nuspecXml);

        if (nuspec.DependencyGroups is not { Count: > 0 })
        {
            return new PackageDependencyResolution(
                [],
                nuspec.Authors,
                PackageDependencyResolutionState.Resolved);
        }

        var selection = SelectDependencyGroup(nuspec.DependencyGroups, tfm);
        if (selection.Group?.Dependencies is not { Count: > 0 })
        {
            return new PackageDependencyResolution(
                [],
                nuspec.Authors,
                PackageDependencyResolutionState.Resolved);
        }

        var children = await ResolveDependencyTreeCoreAsync(
            client,
            selection.Group.Dependencies,
            selection.TargetFramework ?? tfm,
            globalSeen,
            log,
            sourceOptions,
            source,
            graph,
            expandedGraphCoordinates,
            externallySeenPackageIds,
            includeTreeBranch,
            diagnostics).ConfigureAwait(false);
        return new PackageDependencyResolution(
            children,
            nuspec.Authors,
            PackageDependencyResolutionState.Resolved);
    }

    public static string? ResolveVersionFromRange(string versionRange)
    {
        if (NuGet.Versioning.VersionRange.TryParse(versionRange, out var range))
        {
            return range.MinVersion?.ToNormalizedString();
        }
        if (NuGet.Versioning.NuGetVersion.TryParse(versionRange, out var ver))
        {
            return ver.ToNormalizedString();
        }
        return null;
    }

    private sealed class PackageDependencyGraphBuilder
    {
        private readonly List<PackageDependencyGraphNode> _nodes = [];
        private readonly Dictionary<PackageDependencyIdentity, int> _nodeIndexes =
            new(PackageDependencyIdentityComparer.Instance);
        private readonly Dictionary<
            PackageDependencyIdentity,
            PackageDependencyResolutionState> _nodeResolutions =
            new(PackageDependencyIdentityComparer.Instance);
        private readonly List<PackageDependencyRelationship> _relationships = [];

        public void AddNode(
            PackageDependencyIdentity identity,
            string? author)
        {
            if (_nodeIndexes.TryGetValue(identity, out int index))
            {
                if (_nodes[index].Author is null && author is not null)
                {
                    _nodes[index] =
                        _nodes[index] with { Author = author };
                }
                return;
            }

            _nodeIndexes.Add(identity, _nodes.Count);
            _nodes.Add(new PackageDependencyGraphNode(identity, author));
        }

        public void AddRelationship(
            PackageDependencyIdentity source,
            PackageDependencyIdentity target,
            string versionConstraint)
        {
            int ordinal = _relationships.Count;
            _relationships.Add(
                new PackageDependencyRelationship(
                    source,
                    target,
                    versionConstraint,
                    _nodeResolutions.GetValueOrDefault(
                        target,
                        PackageDependencyResolutionState.Declared),
                    ordinal));
        }

        public void SetNodeResolution(
            PackageDependencyIdentity identity,
            PackageDependencyResolutionState resolution)
        {
            _nodeResolutions[identity] = resolution;
            for (int i = 0; i < _relationships.Count; i++)
            {
                if (PackageDependencyIdentityComparer.Instance.Equals(
                        _relationships[i].Target,
                        identity))
                {
                    _relationships[i] =
                        _relationships[i] with
                        {
                            Resolution = resolution,
                        };
                }
            }
        }

        public PackageDependencyGraph Build(List<DependencyNode> tree) =>
            new(_nodes, _relationships, tree);
    }

    private sealed record PackageDependencyExpansionKey(
        PackageDependencyIdentity Identity,
        string TargetFramework);

    private sealed class PackageDependencyExpansionKeyComparer :
        IEqualityComparer<PackageDependencyExpansionKey>
    {
        public static PackageDependencyExpansionKeyComparer Instance { get; } =
            new();

        public bool Equals(
            PackageDependencyExpansionKey? x,
            PackageDependencyExpansionKey? y) =>
            ReferenceEquals(x, y)
            || x is not null
                && y is not null
                && PackageDependencyIdentityComparer.Instance.Equals(
                    x.Identity,
                    y.Identity)
                && StringComparer.OrdinalIgnoreCase.Equals(
                    TfmSelector.NormalizeTfm(x.TargetFramework),
                    TfmSelector.NormalizeTfm(y.TargetFramework));

        public int GetHashCode(PackageDependencyExpansionKey obj)
        {
            var hash = new HashCode();
            hash.Add(
                obj.Identity,
                PackageDependencyIdentityComparer.Instance);
            hash.Add(
                TfmSelector.NormalizeTfm(obj.TargetFramework),
                StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }

    private sealed class PackageDependencyIdentityComparer :
        IEqualityComparer<PackageDependencyIdentity>
    {
        public static PackageDependencyIdentityComparer Instance { get; } =
            new();

        public bool Equals(
            PackageDependencyIdentity? x,
            PackageDependencyIdentity? y) =>
            ReferenceEquals(x, y)
            || x is not null
                && y is not null
                && StringComparer.OrdinalIgnoreCase.Equals(
                    x.PackageId,
                    y.PackageId)
                && StringComparer.OrdinalIgnoreCase.Equals(
                    x.Version,
                    y.Version);

        public int GetHashCode(PackageDependencyIdentity obj)
        {
            var hash = new HashCode();
            hash.Add(obj.PackageId, StringComparer.OrdinalIgnoreCase);
            hash.Add(obj.Version, StringComparer.OrdinalIgnoreCase);
            return hash.ToHashCode();
        }
    }

    private sealed record PackageDependencyResolution(
        List<DependencyNode> Children,
        string? Author,
        PackageDependencyResolutionState State);
}
