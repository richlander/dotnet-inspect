using DotnetInspector.Models;
using System.Xml.Linq;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Analyzes tools/, lib/, and runtimes/ directories in NuGet packages.
/// </summary>
public static class ToolsAnalyzer
{
    public sealed record ToolSettings(
        string? Version,
        string? ToolFormat,
        bool IsRidSpecificPointerPackage,
        List<string>? Commands,
        List<RidPackageReference>? RuntimeIdentifierPackages);

    public static void AnalyzeToolsDirectory(string toolsDir, InspectionResult result)
    {
        // Check for DotnetToolSettings.xml: root, one level deep, or two levels deep
        // Standard locations: tools/, tools/{tfm}/, or tools/{tfm}/{rid}/
        var settings = ReadToolSettings(toolsDir);
        if (settings != null)
        {
            ApplyToolSettings(settings, result);
        }

        // Tools directory structure: tools/{tfm}/{rid}/ or tools/{tfm}/any/
        // RID-specific binary packages use tools/any/{rid}/ where "any" means any framework
        foreach (string tfmDir in Directory.GetDirectories(toolsDir))
        {
            string tfm = Path.GetFileName(tfmDir);
            if (!tfm.Equals("any", StringComparison.OrdinalIgnoreCase))
            {
                result.TargetFrameworks ??= [];
                if (!result.TargetFrameworks.Contains(tfm))
                {
                    result.TargetFrameworks.Add(tfm);
                }
            }

            foreach (string ridDir in Directory.GetDirectories(tfmDir))
            {
                string rid = Path.GetFileName(ridDir);
                result.SupportedRids ??= [];
                if (!result.SupportedRids.Contains(rid))
                {
                    result.SupportedRids.Add(rid);
                }

                // Check if 'any' means framework-dependent
                // But don't override if we already detected this is a RID-specific pointer package
                if (rid.Equals("any", StringComparison.OrdinalIgnoreCase))
                {
                    if (!result.IsRidSpecificPointerPackage)
                    {
                        result.IsFrameworkDependent = true;
                    }
                }
                else
                {
                    result.HasRidSpecificAssets = true;
                }

                // Look for executables and native files
                AnalyzeDirectoryContents(ridDir, result, rid);
            }
        }
    }

    public static void AnalyzeLibDirectory(string libDir, InspectionResult result)
    {
        foreach (string tfmDir in Directory.GetDirectories(libDir))
        {
            string tfm = Path.GetFileName(tfmDir);
            result.TargetFrameworks ??= [];
            if (!result.TargetFrameworks.Contains(tfm))
            {
                result.TargetFrameworks.Add(tfm);
            }
        }
    }

    /// <summary>
    /// Detects standard NuGet package content directories and populates ContentDirectories.
    /// </summary>
    public static void AnalyzeContentDirectories(string extractPath, InspectionResult result)
    {
        var standardDirs = new[] { "lib", "tools", "analyzers", "build", "buildTransitive", "contentFiles", "ref", "runtimes" };
        List<string> found = [];

        foreach (var dir in standardDirs)
        {
            var fullPath = Path.Combine(extractPath, dir);
            if (Directory.Exists(fullPath))
            {
                found.Add(dir);
            }
        }

        if (found.Count > 0)
        {
            result.ContentDirectories = found;
        }
    }

    /// <summary>
    /// Counts library assemblies (DLLs) in the package, excluding resource assemblies.
    /// </summary>
    public static int CountAssemblies(string extractPath)
    {
        var dlls = TfmSelector.GetPackageDlls(extractPath)
            .Where(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase));

        // Count unique assembly names (deduplicated across TFMs)
        return dlls
            .Select(f => Path.GetFileName(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    public static void AnalyzeRuntimesDirectory(string runtimesDir, InspectionResult result)
    {
        // runtimes/{rid}/native/ or runtimes/{rid}/lib/{tfm}/
        foreach (string ridDir in Directory.GetDirectories(runtimesDir))
        {
            string rid = Path.GetFileName(ridDir);
            result.SupportedRids ??= [];
            if (!result.SupportedRids.Contains(rid))
            {
                result.SupportedRids.Add(rid);
            }
            result.HasRidSpecificAssets = true;

            // Check for native subdirectory
            string nativeDir = Path.Combine(ridDir, "native");
            if (Directory.Exists(nativeDir))
            {
                result.HasNativeDependencies = true;
                var nativeFiles = Directory.GetFiles(nativeDir);
                foreach (var file in nativeFiles)
                {
                    result.NativeFiles ??= [];
                    result.NativeFiles.Add($"{rid}: {Path.GetFileName(file)}");
                }
            }
        }
    }

    /// <summary>
    /// Searches for DotnetToolSettings.xml up to 2 levels deep in tools/.
    /// </summary>
    private static string? FindSettingsFile(string toolsDir)
    {
        const string fileName = "DotnetToolSettings.xml";
        var path = Path.Combine(toolsDir, fileName);
        if (File.Exists(path)) return path;

        foreach (var level1 in Directory.GetDirectories(toolsDir))
        {
            path = Path.Combine(level1, fileName);
            if (File.Exists(path)) return path;

            foreach (var level2 in Directory.GetDirectories(level1))
            {
                path = Path.Combine(level2, fileName);
                if (File.Exists(path)) return path;
            }
        }

        return null;
    }

    public static ToolSettings? ReadToolSettings(string toolsDir)
    {
        var settingsFile = FindSettingsFile(toolsDir);
        return settingsFile == null ? null : ParseToolSettings(settingsFile);
    }

    private static ToolSettings? ParseToolSettings(string settingsFile)
    {
        try
        {
            var doc = XDocument.Load(settingsFile);
            var root = doc.Root;

            string? version = root?.Attribute("Version")?.Value;
            if (version == "2")
            {
                var commands = root?.Element("Commands")?.Elements("Command");
                List<string>? commandNames = null;
                if (commands != null)
                {
                    commandNames = commands
                        .Select(c => c.Attribute("Name")?.Value)
                        .Where(n => n != null)
                        .Cast<string>()
                        .ToList();
                }

                var ridPackages = root?.Element("RuntimeIdentifierPackages")?.Elements("RuntimeIdentifierPackage");
                List<RidPackageReference>? runtimeIdentifierPackages = null;
                if (ridPackages != null)
                {
                    runtimeIdentifierPackages = ridPackages
                        .Select(r => new RidPackageReference
                        {
                            RuntimeIdentifier = r.Attribute("RuntimeIdentifier")?.Value ?? "",
                            PackageId = r.Attribute("Id")?.Value ?? ""
                        })
                        .ToList();
                }

                return new ToolSettings(
                    version,
                    "DotNetCliTool Version=\"2\" (RID-specific)",
                    IsRidSpecificPointerPackage: true,
                    commandNames,
                    runtimeIdentifierPackages);
            }
            else if (version == "1" || version == null)
            {
                var commands = root?.Element("Commands")?.Elements("Command");
                List<string>? commandNames = null;
                if (commands != null)
                {
                    commandNames = commands
                        .Select(c => c.Attribute("Name")?.Value)
                        .Where(n => n != null)
                        .Cast<string>()
                        .ToList();
                }

                return new ToolSettings(
                    version,
                    "DotNetCliTool Version=\"1\" (portable)",
                    IsRidSpecificPointerPackage: false,
                    commandNames,
                    RuntimeIdentifierPackages: null);
            }
        }
        catch
        {
            // Ignore parse errors
        }

        return null;
    }

    private static void ApplyToolSettings(ToolSettings settings, InspectionResult result)
    {
        result.ManifestVersion = settings.Version;
        result.ToolFormat = settings.ToolFormat;
        result.ToolCommands = settings.Commands;

        if (settings.IsRidSpecificPointerPackage)
        {
            result.IsRidSpecificPointerPackage = true;
            result.IsFrameworkDependent = false;
            result.HasRidSpecificAssets = true;
            result.RuntimeIdentifierPackages = settings.RuntimeIdentifierPackages;
            result.SupportedRids = settings.RuntimeIdentifierPackages?
                .Select(r => r.RuntimeIdentifier)
                .ToList();
        }
    }

    private static void AnalyzeDirectoryContents(string dir, InspectionResult result, string rid)
    {
        string[] files = Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly);

        foreach (string file in files)
        {
            string fileName = Path.GetFileName(file);
            string ext = Path.GetExtension(file).ToLowerInvariant();

            // Detect native executables
            if (ext == ".exe" || ext == "" && !fileName.Contains('.'))
            {
                // Could be native executable (especially on non-Windows RIDs)
                if (!rid.Equals("any", StringComparison.OrdinalIgnoreCase))
                {
                    result.HasNativeDependencies = true;
                }
            }

            // Detect native libraries
            if (ext is ".dll" or ".so" or ".dylib")
            {
                // Check if it's in a RID-specific folder (not 'any')
                if (!rid.Equals("any", StringComparison.OrdinalIgnoreCase))
                {
                    // Could be native - would need PE inspection to be sure
                }
            }
        }
    }
}
