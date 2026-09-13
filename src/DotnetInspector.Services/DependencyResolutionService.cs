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
    /// Resolves the full transitive dependency tree for a set of direct dependencies.
    /// </summary>
    public static async Task<List<DependencyNode>> ResolveDependencyTreeAsync(
        HttpClient client, List<PackageDependency> dependencies, string tfm,
        HashSet<string> globalSeen, Action<string>? log,
        NuGetSourceOptions? sourceOptions = null)
        => await ResolveDependencyTreeCoreAsync(
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
            includeTreeBranch: true).ConfigureAwait(false);

    /// <summary>
    /// Resolves package dependencies while retaining every direct relationship
    /// independently of expansion. A repeated exact coordinate in the same
    /// target-framework context stops recursive work but never deletes the
    /// edge that reached it.
    /// </summary>
    public static async Task<PackageDependencyGraph> ResolveDependencyGraphAsync(
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
            includeTreeBranch: true).ConfigureAwait(false);
        return graph.Build(tree);
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
        bool includeTreeBranch)
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
                includeTreeNode).ConfigureAwait(false);

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
        PackageDependencyIdentity? source = null,
        PackageDependencyGraphBuilder? graph = null,
        HashSet<PackageDependencyExpansionKey>?
            expandedGraphCoordinates = null,
        HashSet<string>? externallySeenPackageIds = null,
        bool includeTreeBranch = true)
    {
        try
        {
            string? version = ResolveVersionFromRange(versionRange);
            if (version == null)
            {
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
                includeTreeBranch).ConfigureAwait(false);
            return new PackageDependencyResolution(
                children,
                nuspec.Authors,
                PackageDependencyResolutionState.Resolved);
        }
        catch (NuspecParseException)
        {
            throw;
        }
        catch (PackageSourceMappingException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error resolving dependencies: {ex.Message}");
            return new PackageDependencyResolution(
                [],
                Author: null,
                PackageDependencyResolutionState.Unavailable);
        }
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
