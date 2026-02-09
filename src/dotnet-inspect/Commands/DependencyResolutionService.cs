using DotnetInspector.Inspectors;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Resolves transitive NuGet dependency trees from nuspec dependency groups.
/// </summary>
internal static class DependencyResolutionService
{
    /// <summary>
    /// Resolves the full transitive dependency tree for a set of direct dependencies.
    /// </summary>
    public static async Task<List<TreeNode>> ResolveDependencyTreeAsync(
        HttpClient client, List<PackageDependency> dependencies, string tfm,
        HashSet<string> globalSeen, VerboseLogger logger)
    {
        var nodes = new List<TreeNode>();

        foreach (var dep in dependencies.OrderBy(d => d.Id))
        {
            if (!globalSeen.Add(dep.Id))
                continue;

            logger.Log($"Resolving: {dep.Id} {dep.Version}");

            var (children, author) = await ResolveChildDependenciesAsync(
                client, dep.Id, dep.Version, tfm, globalSeen, logger);

            var label = !string.IsNullOrEmpty(author)
                ? $"{dep.Id} {dep.Version} [{author}]"
                : $"{dep.Id} {dep.Version}";

            nodes.Add(children.Count > 0 ? new TreeNode(label, children) : new TreeNode(label));
        }

        return nodes;
    }

    /// <summary>
    /// Finds the best matching TFM dependency group for a target TFM.
    /// </summary>
    public static DependencyGroup? FindBestMatchingTfmGroup(List<DependencyGroup> groups, string targetTfm)
    {
        var exact = groups.FirstOrDefault(g =>
            g.TargetFramework.Equals(targetTfm, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        var targetPriority = GetTfmPriority(targetTfm);
        var targetVersion = ExtractTfmVersion(targetTfm);

        return groups
            .Where(g => string.IsNullOrEmpty(g.TargetFramework) ||
                        g.TargetFramework.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                        (GetTfmPriority(g.TargetFramework) <= targetPriority &&
                         ExtractTfmVersion(g.TargetFramework) <= targetVersion))
            .OrderByDescending(g => GetTfmPriority(g.TargetFramework))
            .ThenByDescending(g => ExtractTfmVersion(g.TargetFramework))
            .FirstOrDefault();
    }

    private static async Task<(List<TreeNode> Children, string? Author)> ResolveChildDependenciesAsync(
        HttpClient client, string packageId, string versionRange, string tfm,
        HashSet<string> globalSeen, VerboseLogger logger)
    {
        try
        {
            string? version = ResolveVersionFromRange(versionRange);
            if (version == null) return ([], null);

            string packageRef = $"{packageId.ToLowerInvariant()}@{version}";
            var extractResult = await PackageExtractor.ExtractPackageAsync(client, packageRef, log: logger.Log);
            if (extractResult == null) return ([], null);

            try
            {
                var childResult = new InspectionResult();
                string[] nuspecFiles = Directory.GetFiles(extractResult.ExtractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
                if (nuspecFiles.Length > 0)
                {
                    NuspecParser.Parse(nuspecFiles[0], childResult);
                }

                var author = childResult.Authors;

                if (childResult.DependencyGroups is not { Count: > 0 }) return ([], author);

                var group = FindBestMatchingTfmGroup(childResult.DependencyGroups, tfm);
                if (group?.Dependencies is not { Count: > 0 }) return ([], author);

                var children = await ResolveDependencyTreeAsync(client, group.Dependencies, tfm, globalSeen, logger);
                return (children, author);
            }
            finally
            {
                if (extractResult.TempDir != null)
                {
                    try { Directory.Delete(extractResult.TempDir, recursive: true); } catch { }
                }
            }
        }
        catch
        {
            return ([], null);
        }
    }

    private static string? ResolveVersionFromRange(string versionRange)
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

    internal static int GetTfmPriority(string tfm)
    {
        var lower = tfm.ToLowerInvariant();
        if (lower.StartsWith("net") && !lower.StartsWith("netstandard") &&
            !lower.StartsWith("netcoreapp") && !lower.StartsWith("netframework") &&
            lower.Length > 3 && char.IsDigit(lower[3]) && lower.Contains('.'))
            return 4;
        if (lower.StartsWith("netcoreapp")) return 3;
        if (lower.StartsWith("netstandard")) return 2;
        if (lower.StartsWith("net4") || lower.StartsWith("netframework")) return 1;
        return 0;
    }

    internal static double ExtractTfmVersion(string tfm)
    {
        var versionPart = tfm.ToLowerInvariant()
            .Replace("netcoreapp", "").Replace("netstandard", "")
            .Replace("netframework", "").Replace("net", "").TrimStart('v');
        if (double.TryParse(versionPart, out var version)) return version;
        if (versionPart.Length == 3 && int.TryParse(versionPart, out var intVersion)) return intVersion / 100.0;
        return 0;
    }
}
