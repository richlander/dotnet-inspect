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
    {
        List<DependencyNode> nodes = [];

        foreach (var dep in dependencies.OrderBy(d => d.Id))
        {
            if (!globalSeen.Add(dep.Id))
                continue;

            log?.Invoke($"Resolving: {dep.Id} {dep.Version}");

            var (children, author) = await ResolveChildDependenciesAsync(
                client,
                dep.Id,
                dep.Version,
                tfm,
                globalSeen,
                log,
                sourceOptions).ConfigureAwait(false);

            nodes.Add(new DependencyNode(dep.Id, dep.Version, author, children));
        }

        return nodes;
    }

    /// <summary>
    /// Finds the best matching TFM dependency group for a target TFM.
    /// </summary>
    public static DependencyGroup? FindBestMatchingTfmGroup(List<DependencyGroup> groups, string targetTfm)
    {
        var exact = FindNormalizedExactGroup(groups, targetTfm);
        if (exact != null) return exact;

        var targetPriority = TfmSelector.GetTfmPriority(targetTfm);

        return TfmSelector.OrderByTfmPriorityDescending(
                groups.Where(g => string.IsNullOrEmpty(g.TargetFramework) ||
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

        var highest = TfmSelector.OrderByTfmPriorityDescending(groups, g => g.TargetFramework)
            .First();
        return new DependencyGroupSelection(highest, highest.TargetFramework, DependencyGroupSelectionStatus.Selected, availableTfms);
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

    private static async Task<(List<DependencyNode> Children, string? Author)> ResolveChildDependenciesAsync(
        HttpClient client, string packageId, string versionRange, string tfm,
        HashSet<string> globalSeen, Action<string>? log,
        NuGetSourceOptions? sourceOptions)
    {
        try
        {
            string? version = ResolveVersionFromRange(versionRange);
            if (version == null) return ([], null);

            // Resolving the tree only needs each package's dependency groups, so fetch just the
            // nuspec (from cache or the flat-container endpoint) instead of downloading and
            // extracting the whole .nupkg.
            string? nuspecXml = await DotnetInspector.Packages.PackageExtractor.TryGetNuspecXmlAsync(
                client,
                packageId,
                version,
                log,
                sourceOptions).ConfigureAwait(false);
            if (nuspecXml == null) return ([], null);

            var nuspec = NuspecParser.ParseContent(nuspecXml);

            if (nuspec.DependencyGroups is not { Count: > 0 }) return ([], nuspec.Authors);

            var selection = SelectDependencyGroup(nuspec.DependencyGroups, tfm);
            if (selection.Group?.Dependencies is not { Count: > 0 }) return ([], nuspec.Authors);

            var children = await ResolveDependencyTreeAsync(
                client,
                selection.Group.Dependencies,
                selection.TargetFramework ?? tfm,
                globalSeen,
                log,
                sourceOptions).ConfigureAwait(false);
            return (children, nuspec.Authors);
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
            return ([], null);
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
}
