namespace DotnetInspector.Inspectors;

/// <summary>
/// TFM selection and assembly discovery within package layouts.
/// </summary>
internal static class TfmSelector
{
    internal static List<string> GetPackageDlls(string extractPath)
    {
        var toolsDir = Path.Combine(extractPath, "tools");
        var libDir = Path.Combine(extractPath, "lib");

        string[] candidates = [];
        if (Directory.Exists(toolsDir))
        {
            candidates = Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories);
        }

        if (candidates.Length == 0 && Directory.Exists(libDir))
        {
            candidates = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories);
        }

        if (candidates.Length == 0)
        {
            candidates = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
        }

        return candidates.OrderBy(f => f).ToList();
    }

    internal static (string? path, string? tfm) SelectHighestTfmAssembly(List<string> dlls, string extractPath, string? packageName = null)
    {
        dlls = dlls.Where(d => !d.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)).ToList();

        var byTfm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in dlls)
        {
            var relativePath = Path.GetRelativePath(extractPath, dll).Replace('\\', '/');
            var tfm = ExtractTfmFromPath(relativePath);
            if (tfm != null)
            {
                if (!byTfm.TryGetValue(tfm, out var list))
                {
                    list = [];
                    byTfm[tfm] = list;
                }
                list.Add(dll);
            }
        }

        if (byTfm.Count == 0)
            return (null, null);

        var sortedTfms = byTfm.Keys
            .Select(tfm => (tfm, priority: GetTfmPriority(tfm)))
            .OrderByDescending(x => x.priority)
            .ToList();

        var highestTfm = sortedTfms[0].tfm;
        var assemblies = byTfm[highestTfm];

        // Prefer assembly matching the package name
        if (packageName != null)
        {
            var match = assemblies.FirstOrDefault(d =>
                Path.GetFileNameWithoutExtension(d).Equals(packageName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return (match, highestTfm);
        }

        var directDll = assemblies.FirstOrDefault(d =>
        {
            var relativePath = Path.GetRelativePath(extractPath, d).Replace('\\', '/');
            var parts = relativePath.Split('/');
            return parts.Length <= 3;
        });

        return (directDll ?? assemblies[0], highestTfm);
    }

    internal static string? FindAssemblyByTfm(string extractPath, string tfm)
    {
        var libDir = Path.Combine(extractPath, "lib");
        var toolsDir = Path.Combine(extractPath, "tools");

        if (Directory.Exists(libDir))
        {
            var tfmDir = Path.Combine(libDir, tfm);
            if (Directory.Exists(tfmDir))
            {
                var dlls = Directory.GetFiles(tfmDir, "*.dll");
                if (dlls.Length > 0)
                    return dlls[0];
            }
        }

        if (Directory.Exists(toolsDir))
        {
            var dlls = Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories)
                .Where(f => f.Replace('\\', '/').Contains($"/{tfm}/", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dlls.Count > 0)
                return dlls[0];
        }

        return null;
    }

    internal static string? ExtractTfmFromPath(string relativePath)
    {
        var parts = relativePath.Split('/');
        foreach (var part in parts)
        {
            if (IsTfmFolder(part))
                return part;
        }
        return null;
    }

    private static bool IsTfmFolder(string folderName)
    {
        return folderName.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
               (folderName.Contains('.') || char.IsDigit(folderName[3]));
    }

    internal static int GetTfmPriority(string tfm)
    {
        var lower = tfm.ToLowerInvariant();

        if (lower.StartsWith("net") && !lower.StartsWith("netstandard") && !lower.StartsWith("netcoreapp") && !lower.StartsWith("netframework"))
        {
            var versionPart = lower[3..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 10000 + (version.Major * 100) + version.Minor;
            }
            if (int.TryParse(versionPart.Replace(".", ""), out var legacyVersion))
            {
                return 1000 + legacyVersion;
            }
        }

        if (lower.StartsWith("netcoreapp"))
        {
            var versionPart = lower[10..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 5000 + (version.Major * 100) + version.Minor;
            }
        }

        if (lower.StartsWith("netstandard"))
        {
            var versionPart = lower[11..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 3000 + (version.Major * 100) + version.Minor;
            }
        }

        return 0;
    }
}
