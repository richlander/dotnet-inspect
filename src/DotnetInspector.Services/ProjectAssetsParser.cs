using System.Text.Json;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Parses project.assets.json to discover NuGet package assemblies in a project.
/// </summary>
public static class ProjectAssetsParser
{
    /// <summary>
    /// Parses a project.assets.json file and returns the assembly paths with package metadata.
    /// </summary>
    public static List<(string Path, string PackageName, string Version)> Parse(string assetsPath, string? tfmFilter, Action<string>? log)
    {
        var results = new List<(string Path, string PackageName, string Version)>();
        var nugetCache = NuGetCache.GetNuGetCachePath();

        try
        {
            var json = File.ReadAllText(assetsPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("targets", out var targets))
                return results;

            // Find the target TFM
            string? selectedTfm = null;
            foreach (var target in targets.EnumerateObject())
            {
                var tfmName = target.Name;
                var baseTfm = tfmName.Contains('/') ? tfmName[..tfmName.IndexOf('/')] : tfmName;

                if (!string.IsNullOrEmpty(tfmFilter))
                {
                    if (baseTfm.Equals(tfmFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedTfm = tfmName;
                        break;
                    }
                }
                else
                {
                    if (selectedTfm == null || TfmResolver.GetTfmPriority(baseTfm) > TfmResolver.GetTfmPriority(selectedTfm.Contains('/') ? selectedTfm[..selectedTfm.IndexOf('/')] : selectedTfm))
                    {
                        selectedTfm = tfmName;
                    }
                }
            }

            if (selectedTfm == null)
                return results;

            log?.Invoke($"Using target framework: {selectedTfm}");

            if (!doc.RootElement.TryGetProperty("libraries", out var libraries))
                return results;

            var targetDeps = targets.GetProperty(selectedTfm);

            foreach (var dep in targetDeps.EnumerateObject())
            {
                var parts = dep.Name.Split('/');
                if (parts.Length != 2) continue;

                var packageName = parts[0];
                var version = parts[1];

                if (!libraries.TryGetProperty(dep.Name, out var libInfo))
                    continue;

                if (libInfo.TryGetProperty("type", out var typeElem) && typeElem.GetString() == "project")
                    continue;

                if (!libInfo.TryGetProperty("path", out var pathElem))
                    continue;

                var packagePath = pathElem.GetString();
                if (string.IsNullOrEmpty(packagePath))
                    continue;

                if (dep.Value.TryGetProperty("compile", out var compile))
                {
                    foreach (var asm in compile.EnumerateObject())
                    {
                        if (!asm.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (asm.Name.Contains("_._"))
                            continue;

                        var fullPath = Path.Combine(nugetCache, packagePath, asm.Name.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullPath))
                        {
                            results.Add((fullPath, packageName, version));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Failed to parse project.assets.json: {ex.Message}");
        }

        return results;
    }
}
