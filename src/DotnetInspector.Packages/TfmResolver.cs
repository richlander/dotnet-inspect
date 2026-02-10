namespace DotnetInspector.Packages;

/// <summary>
/// Utilities for resolving Target Framework Monikers (TFMs) in packages.
/// </summary>
public static class TfmResolver
{
    /// <summary>
    /// Resolves the assembly path for a package, using the specified TFM or auto-selecting the highest.
    /// </summary>
    /// <param name="extractPath">Path to extracted package contents</param>
    /// <param name="tfm">Specific TFM to use, or null to auto-select highest</param>
    /// <returns>Path to the selected directory, or null if not found</returns>
    public static string? ResolvePackagePath(string extractPath, string? tfm)
    {
        if (!string.IsNullOrEmpty(tfm))
        {
            // Use specific TFM
            return FindAssemblyByTfm(extractPath, tfm);
        }
        else
        {
            // Auto-select highest TFM
            var dlls = GetPackageDlls(extractPath);
            var (highestPath, _) = SelectHighestTfmAssembly(dlls, extractPath);
            return highestPath;
        }
    }

    /// <summary>
    /// Finds an assembly path matching the specified TFM.
    /// </summary>
    public static string? FindAssemblyByTfm(string extractPath, string tfm)
    {
        var libDir = Path.Combine(extractPath, "lib");
        var toolsDir = Path.Combine(extractPath, "tools");

        // Check lib directory
        if (Directory.Exists(libDir))
        {
            var tfmDir = Path.Combine(libDir, tfm);
            if (Directory.Exists(tfmDir) && Directory.GetFiles(tfmDir, "*.dll").Length > 0)
            {
                return tfmDir;
            }

            // Case-insensitive fallback
            var dirs = Directory.GetDirectories(libDir);
            foreach (var dir in dirs)
            {
                if (Path.GetFileName(dir).Equals(tfm, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.GetFiles(dir, "*.dll").Length > 0)
                        return dir;
                }
            }
        }

        // Check tools directory
        if (Directory.Exists(toolsDir))
        {
            var tfmDir = Path.Combine(toolsDir, tfm);
            if (Directory.Exists(tfmDir) && Directory.GetFiles(tfmDir, "*.dll").Length > 0)
            {
                return tfmDir;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets all DLL files from a package extract path.
    /// </summary>
    public static List<string> GetPackageDlls(string extractPath)
    {
        List<string> result = [];
        var libDir = Path.Combine(extractPath, "lib");
        var toolsDir = Path.Combine(extractPath, "tools");

        if (Directory.Exists(libDir))
        {
            result.AddRange(Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories)
                .Where(p => !p.Contains("/runtimes/") && !p.Contains("\\runtimes\\")));
        }
        if (Directory.Exists(toolsDir))
        {
            result.AddRange(Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories)
                .Where(p => !p.Contains("/runtimes/") && !p.Contains("\\runtimes\\")));
        }
        // Root level DLLs
        result.AddRange(Directory.GetFiles(extractPath, "*.dll", SearchOption.TopDirectoryOnly));

        return result;
    }

    /// <summary>
    /// Selects the assembly with the highest TFM priority.
    /// </summary>
    public static (string? path, string? tfm) SelectHighestTfmAssembly(List<string> dlls, string basePath)
    {
        if (dlls.Count == 0) return (null, null);
        if (dlls.Count == 1) return (dlls[0], ExtractTfmFromPath(Path.GetRelativePath(basePath, dlls[0]).Replace('\\', '/')));

        var tfmGroups = dlls
            .Select(d => (path: d, tfm: ExtractTfmFromPath(Path.GetRelativePath(basePath, d).Replace('\\', '/'))))
            .Where(x => x.tfm != null)
            .GroupBy(x => x.tfm)
            .OrderByDescending(g => GetTfmPriority(g.Key!))
            .FirstOrDefault();

        if (tfmGroups != null)
        {
            // Return the directory containing the highest TFM assemblies
            var selected = tfmGroups.First();
            var dir = Path.GetDirectoryName(selected.path);
            return (dir, selected.tfm);
        }

        return (dlls[0], null);
    }

    /// <summary>
    /// Extracts TFM from a relative path (e.g., "lib/net8.0/Foo.dll" -> "net8.0").
    /// </summary>
    public static string? ExtractTfmFromPath(string relativePath)
    {
        var parts = relativePath.Split('/', '\\');
        if (parts.Length >= 2)
        {
            var potential = parts[^2];
            if (IsTfmLike(potential))
                return potential;
        }
        return null;
    }

    /// <summary>
    /// Checks if a string looks like a TFM.
    /// </summary>
    public static bool IsTfmLike(string s)
    {
        var lower = s.ToLowerInvariant();
        return lower.StartsWith("net") || lower.StartsWith("netstandard") || lower.StartsWith("netcoreapp");
    }

    /// <summary>
    /// Gets priority score for a TFM (higher = newer/preferred).
    /// </summary>
    public static int GetTfmPriority(string tfm)
    {
        var lower = tfm.ToLowerInvariant();

        // Modern .NET (net5.0, net6.0, net7.0, net8.0, net9.0, net10.0, etc.)
        if (lower.StartsWith("net") && !lower.StartsWith("netstandard") && !lower.StartsWith("netcoreapp"))
        {
            var versionPart = lower[3..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 10000 + (version.Major * 100) + version.Minor;
            }
            // Legacy .NET Framework (net45, net461, etc.)
            if (int.TryParse(versionPart.Replace(".", ""), out var legacyVersion))
            {
                return 1000 + legacyVersion;
            }
        }

        // .NET Core (netcoreapp2.1, netcoreapp3.1, etc.)
        if (lower.StartsWith("netcoreapp"))
        {
            var versionPart = lower[10..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 5000 + (version.Major * 100) + version.Minor;
            }
        }

        // .NET Standard (netstandard2.0, netstandard2.1, etc.)
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
