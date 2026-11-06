using System.Collections.Immutable;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Selects nuspec dependency groups and projects version-range lower bounds.
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
    /// Selects the dependency group for a requested target framework.
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
