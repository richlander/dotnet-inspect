using NuGet.Frameworks;

namespace DotnetInspector.Packages;

public static class TfmResolver
{
    private static readonly string[] AssetRoots = ["ref", "lib", "tools"];

    public static string? ExtractTfmFromPath(string relativePath)
    {
        string[] parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (IsTfm(parts[i]))
                return parts[i];
        }

        return null;
    }

    public static int GetTfmPriority(string? tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm) || tfm.Equals("any", StringComparison.OrdinalIgnoreCase))
            return 0;

        NuGetFramework framework;
        try
        {
            framework = NuGetFramework.ParseFolder(tfm);
        }
        catch (ArgumentException)
        {
            return 0;
        }

        string frameworkName = framework.Framework;
        Version version = framework.Version;
        int versionScore = version.Major * 100 + version.Minor;

        return frameworkName switch
        {
            ".NETCoreApp" => 40_000 + versionScore,
            ".NETStandard" => 20_000 + versionScore,
            ".NETFramework" => 10_000 + versionScore,
            _ => versionScore,
        };
    }

    public static string? ResolvePackagePath(string extractPath, string? tfm)
    {
        if (!string.IsNullOrWhiteSpace(tfm))
        {
            foreach (string root in AssetRoots)
            {
                string candidate = Path.Combine(extractPath, root, tfm);
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        var candidates = new List<(string Path, string Tfm)>();
        foreach (string root in AssetRoots)
        {
            string rootPath = Path.Combine(extractPath, root);
            if (!Directory.Exists(rootPath))
                continue;

            foreach (string directory in Directory.GetDirectories(rootPath, "*", SearchOption.TopDirectoryOnly))
            {
                string candidateTfm = Path.GetFileName(directory);
                if (IsTfm(candidateTfm))
                    candidates.Add((directory, candidateTfm));
            }
        }

        return candidates
            .OrderByDescending(c => GetTfmPriority(c.Tfm))
            .ThenBy(c => c.Tfm, StringComparer.OrdinalIgnoreCase)
            .Select(c => c.Path)
            .FirstOrDefault();
    }

    private static bool IsTfm(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            NuGetFramework framework = NuGetFramework.ParseFolder(value);
            return !framework.IsUnsupported;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
