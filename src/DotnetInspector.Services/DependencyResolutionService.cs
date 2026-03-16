using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services;

/// <summary>
/// Resolves transitive NuGet dependency trees from nuspec dependency groups.
/// </summary>
public static class DependencyResolutionService
{
    /// <summary>
    /// Resolves the full transitive dependency tree for a set of direct dependencies.
    /// </summary>
    public static async Task<List<DependencyNode>> ResolveDependencyTreeAsync(
        HttpClient client, List<PackageDependency> dependencies, string tfm,
        HashSet<string> globalSeen, Action<string>? log)
    {
        List<DependencyNode> nodes = [];

        foreach (var dep in dependencies.OrderBy(d => d.Id))
        {
            if (!globalSeen.Add(dep.Id))
                continue;

            log?.Invoke($"Resolving: {dep.Id} {dep.Version}");

            var (children, author) = await ResolveChildDependenciesAsync(
                client, dep.Id, dep.Version, tfm, globalSeen, log).ConfigureAwait(false);

            nodes.Add(new DependencyNode(dep.Id, dep.Version, author, children));
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

        var targetPriority = TfmResolver.GetTfmPriority(targetTfm);

        return groups
            .Where(g => string.IsNullOrEmpty(g.TargetFramework) ||
                        g.TargetFramework.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                        TfmResolver.GetTfmPriority(g.TargetFramework) <= targetPriority)
            .OrderByDescending(g => TfmResolver.GetTfmPriority(g.TargetFramework))
            .FirstOrDefault();
    }

    private static async Task<(List<DependencyNode> Children, string? Author)> ResolveChildDependenciesAsync(
        HttpClient client, string packageId, string versionRange, string tfm,
        HashSet<string> globalSeen, Action<string>? log)
    {
        try
        {
            string? version = ResolveVersionFromRange(versionRange);
            if (version == null) return ([], null);

            string packageRef = $"{packageId.ToLowerInvariant()}@{version}";
            var outcome = await DotnetInspector.Packages.PackageExtractor.ExtractPackageAsync(client, packageRef, log: log).ConfigureAwait(false);
            if (!outcome.IsSuccess)
            {
                log?.Invoke(outcome.ErrorMessage!);
                return ([], null);
            }
            var extractResult = outcome.Result!;

            try
            {
                string[] nuspecFiles = Directory.GetFiles(extractResult.ExtractPath, "*.nuspec", SearchOption.TopDirectoryOnly);
                if (nuspecFiles.Length == 0) return ([], null);

                var nuspec = NuspecParser.Parse(nuspecFiles[0]);

                if (nuspec.DependencyGroups is not { Count: > 0 }) return ([], nuspec.Authors);

                var group = FindBestMatchingTfmGroup(nuspec.DependencyGroups, tfm);
                if (group?.Dependencies is not { Count: > 0 }) return ([], nuspec.Authors);

                var children = await ResolveDependencyTreeAsync(client, group.Dependencies, tfm, globalSeen, log).ConfigureAwait(false);
                return (children, nuspec.Authors);
            }
            finally
            {
                if (extractResult.TempDir != null)
                {
                    try { Directory.Delete(extractResult.TempDir, recursive: true); } catch { }
                }
            }
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
