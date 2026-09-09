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
            preserveSharedEdges: false).ConfigureAwait(false);

    /// <summary>
    /// Resolves a dependency graph as a branch-expanded tree. Repeated package
    /// targets remain as leaves on each incoming edge, while ancestry-local
    /// cycle detection bounds recursion.
    /// </summary>
    public static async Task<List<DependencyNode>> ResolveDependencyGraphAsync(
        HttpClient client, List<PackageDependency> dependencies, string tfm,
        HashSet<string> ancestry, Action<string>? log,
        NuGetSourceOptions? sourceOptions = null)
        => await ResolveDependencyTreeCoreAsync(
            client,
            dependencies,
            tfm,
            ancestry,
            log,
            sourceOptions,
            preserveSharedEdges: true,
            expanded: new HashSet<string>(
                StringComparer.OrdinalIgnoreCase),
            resolutionStates:
                new Dictionary<
                    string,
                    DependencyNodeResolutionState>(
                    StringComparer.OrdinalIgnoreCase))
            .ConfigureAwait(false);

    private static async Task<List<DependencyNode>>
        ResolveDependencyTreeCoreAsync(
            HttpClient client,
            List<PackageDependency> dependencies,
            string tfm,
            HashSet<string> seen,
            Action<string>? log,
            NuGetSourceOptions? sourceOptions,
            bool preserveSharedEdges,
            HashSet<string>? expanded = null,
            Dictionary<string, DependencyNodeResolutionState>?
                resolutionStates = null)
    {
        List<DependencyNode> nodes = [];

        foreach (var dep in dependencies.OrderBy(d => d.Id))
        {
            string? resolvedVersion =
                ResolveVersionFromRange(dep.Version);
            string identity =
                PackageTraversalIdentity(
                    dep.Id,
                    resolvedVersion ?? dep.Version);
            HashSet<string> branchSeen;
            if (preserveSharedEdges)
            {
                if (seen.Contains(identity)
                    || !expanded!.Add(identity))
                {
                    nodes.Add(
                        new DependencyNode(
                            dep.Id,
                            dep.Version,
                            null,
                            [])
                        {
                            ResolvedVersion = resolvedVersion,
                            Resolution =
                                resolutionStates?.GetValueOrDefault(
                                    identity,
                                    DependencyNodeResolutionState
                                        .Resolved)
                                ?? DependencyNodeResolutionState
                                    .Resolved,
                        });
                    continue;
                }

                branchSeen =
                    new HashSet<string>(
                        seen,
                        StringComparer.OrdinalIgnoreCase)
                    {
                        identity,
                    };
            }
            else
            {
                if (!seen.Add(dep.Id))
                    continue;

                branchSeen = seen;
            }

            log?.Invoke($"Resolving: {dep.Id} {dep.Version}");

            var (children, author, resolution) =
                await ResolveChildDependenciesAsync(
                client,
                dep.Id,
                dep.Version,
                tfm,
                branchSeen,
                log,
                sourceOptions,
                preserveSharedEdges,
                expanded,
                resolutionStates).ConfigureAwait(false);
            if (resolutionStates is not null)
                resolutionStates[identity] = resolution;

            nodes.Add(
                new DependencyNode(
                    dep.Id,
                    dep.Version,
                    author,
                    children)
                {
                    ResolvedVersion = resolvedVersion,
                    Resolution = resolution,
                });
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

    private static async Task<(
        List<DependencyNode> Children,
        string? Author,
        DependencyNodeResolutionState Resolution)>
        ResolveChildDependenciesAsync(
        HttpClient client, string packageId, string versionRange, string tfm,
        HashSet<string> seen, Action<string>? log,
        NuGetSourceOptions? sourceOptions,
        bool preserveSharedEdges,
        HashSet<string>? expanded,
        Dictionary<string, DependencyNodeResolutionState>?
            resolutionStates)
    {
        try
        {
            string? version = ResolveVersionFromRange(versionRange);
            if (version == null)
            {
                return (
                    [],
                    null,
                    DependencyNodeResolutionState.Unresolved);
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
                return (
                    [],
                    null,
                    DependencyNodeResolutionState.Unresolved);
            }

            var nuspec = NuspecParser.ParseContent(nuspecXml);

            if (nuspec.DependencyGroups is not { Count: > 0 })
            {
                return (
                    [],
                    nuspec.Authors,
                    DependencyNodeResolutionState.Resolved);
            }

            var selection = SelectDependencyGroup(nuspec.DependencyGroups, tfm);
            if (selection.Group?.Dependencies is not { Count: > 0 })
            {
                return (
                    [],
                    nuspec.Authors,
                    DependencyNodeResolutionState.Resolved);
            }

            var children = await ResolveDependencyTreeCoreAsync(
                client,
                selection.Group.Dependencies,
                selection.TargetFramework ?? tfm,
                seen,
                log,
                sourceOptions,
                preserveSharedEdges,
                expanded,
                resolutionStates).ConfigureAwait(false);
            return (
                children,
                nuspec.Authors,
                DependencyNodeResolutionState.Resolved);
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
            return (
                [],
                null,
                DependencyNodeResolutionState.Unresolved);
        }
    }

    public static string PackageTraversalIdentity(
        string packageId,
        string version) =>
        $"{packageId}@{NormalizePackageVersion(version)}";

    public static string NormalizePackageVersion(string version) =>
        NuGet.Versioning.NuGetVersion.TryParse(
            version,
            out NuGet.Versioning.NuGetVersion? parsed)
            ? parsed.ToNormalizedString()
            : version;

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
}
