using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Services;

/// <summary>
/// TFM selection and assembly discovery within package layouts.
/// </summary>
public static class TfmSelector
{
    private static List<string> FilterResourceAssemblies(IEnumerable<string> dlls)
        => dlls.Where(d => !d.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)).ToList();

    public static List<string> GetPackageDlls(string extractPath)
    {
        var toolsDir = Path.Combine(extractPath, "tools");
        var refDir = Path.Combine(extractPath, "ref");
        var libDir = Path.Combine(extractPath, "lib");

        string[] candidates = [];
        if (Directory.Exists(toolsDir))
        {
            candidates = Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories);
        }

        // Ref packages (e.g. Microsoft.NETCore.App.Ref) put assemblies in ref/
        if (candidates.Length == 0 && Directory.Exists(refDir))
        {
            candidates = Directory.GetFiles(refDir, "*.dll", SearchOption.AllDirectories);
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

    public static (string? path, string? tfm) SelectHighestTfmAssembly(List<string> dlls, string extractPath, string? packageName = null)
    {
        dlls = FilterResourceAssemblies(dlls);

        var byTfm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in dlls)
        {
            var relativePath = Path.GetRelativePath(extractPath, dll).Replace('\\', '/');
            var tfm = TfmResolver.ExtractTfmFromPath(relativePath);
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
            .Select(tfm => (tfm, priority: TfmResolver.GetTfmPriority(tfm)))
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

    /// <summary>
    /// Returns ALL assemblies at the highest TFM (for multi-library packages).
    /// Filters out resource assemblies.
    /// </summary>
    public static (List<string> paths, string? tfm) SelectHighestTfmAssemblies(List<string> dlls, string extractPath)
    {
        dlls = FilterResourceAssemblies(dlls);

        var byTfm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in dlls)
        {
            var relativePath = Path.GetRelativePath(extractPath, dll).Replace('\\', '/');
            var tfm = TfmResolver.ExtractTfmFromPath(relativePath);
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
            return ([], null);

        var highestTfm = byTfm.Keys
            .Select(tfm => (tfm, priority: TfmResolver.GetTfmPriority(tfm)))
            .OrderByDescending(x => x.priority)
            .First().tfm;

        return (byTfm[highestTfm], highestTfm);
    }

    public static (string? path, string? tfm) FindAssemblyInPackage(string extractPath, string assemblyName, string? tfm = null)
    {
        var dlls = FilterResourceAssemblies(GetPackageDlls(extractPath));
        if (dlls.Count == 0)
            return (null, null);

        var normalizedAssemblyName = assemblyName.Replace('\\', '/');
        var assemblyLeaf = Path.GetFileName(assemblyName);
        var bareName = assemblyLeaf.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(assemblyLeaf)
            : assemblyLeaf;
        var fileName = assemblyLeaf.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? assemblyLeaf
            : $"{bareName}.dll";

        var matchingFiles = dlls
            .Where(dll =>
            {
                var relativePath = Path.GetRelativePath(extractPath, dll).Replace('\\', '/');
                return relativePath.Equals(normalizedAssemblyName, StringComparison.OrdinalIgnoreCase)
                    || relativePath.Equals(normalizedAssemblyName + ".dll", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(dll).Equals(fileName, StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileNameWithoutExtension(dll).Equals(bareName, StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (matchingFiles.Count == 0)
            return (null, null);

        if (!string.IsNullOrEmpty(tfm))
        {
            matchingFiles = matchingFiles
                .Where(dll => string.Equals(
                    TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, dll).Replace('\\', '/')),
                    tfm,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (matchingFiles.Count == 0)
                return (null, tfm);
        }

        var (selectedPath, selectedTfm) = SelectHighestTfmAssembly(matchingFiles, extractPath);
        return (selectedPath ?? matchingFiles[0], selectedTfm ?? tfm);
    }

    public static (string? path, string? tfm) FindAssemblyContainingType(string extractPath, string typeName, string? tfm = null)
    {
        var dlls = FilterResourceAssemblies(GetPackageDlls(extractPath));
        if (dlls.Count == 0)
            return (null, null);

        string? selectedTfm = tfm;
        var candidateDlls = new List<string>();

        if (!string.IsNullOrEmpty(tfm))
        {
            candidateDlls = dlls
                .Where(dll => string.Equals(
                    TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, dll).Replace('\\', '/')),
                    tfm,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else
        {
            var (highestTfmDlls, highestTfm) = SelectHighestTfmAssemblies(dlls, extractPath);
            if (highestTfmDlls.Count > 0)
            {
                candidateDlls = highestTfmDlls;
                selectedTfm = highestTfm;
            }
        }

        foreach (var dll in candidateDlls)
        {
            if (PlatformResolver.HasType(dll, typeName))
            {
                selectedTfm ??= TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, dll).Replace('\\', '/'));
                return (dll, selectedTfm);
            }
        }

        // Fallback: if the highest-TFM scan misses, search the remaining DLLs so
        // `find` results from multi-library packages still lead to a working follow-up.
        foreach (var dll in dlls.Except(candidateDlls))
        {
            if (PlatformResolver.HasType(dll, typeName))
            {
                var matchedTfm = TfmResolver.ExtractTfmFromPath(Path.GetRelativePath(extractPath, dll).Replace('\\', '/'));
                return (dll, matchedTfm ?? selectedTfm);
            }
        }

        return (null, selectedTfm);
    }

    public static string? FindAssemblyByTfm(string extractPath, string tfm, string? packageName = null)
    {
        var refDir = Path.Combine(extractPath, "ref");
        var libDir = Path.Combine(extractPath, "lib");
        var toolsDir = Path.Combine(extractPath, "tools");

        // Check ref/ first (ref packages), then lib/
        foreach (var dir in new[] { refDir, libDir })
        {
            if (Directory.Exists(dir))
            {
                var tfmDir = Path.Combine(dir, tfm);
                if (Directory.Exists(tfmDir))
                {
                    var dlls = Directory.GetFiles(tfmDir, "*.dll");
                    if (dlls.Length > 0)
                    {
                        if (packageName != null)
                        {
                            var match = dlls.FirstOrDefault(d =>
                                Path.GetFileNameWithoutExtension(d).Equals(packageName, StringComparison.OrdinalIgnoreCase));
                            if (match != null)
                                return match;
                        }
                        return dlls[0];
                    }
                }
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
}
