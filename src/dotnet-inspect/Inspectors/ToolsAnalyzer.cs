using DotnetInspector.Models;
using System.Xml.Linq;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Analyzes tools/, lib/, and runtimes/ directories in NuGet packages.
/// </summary>
public static class ToolsAnalyzer
{
    public static void AnalyzeToolsDirectory(string toolsDir, InspectionResult result)
    {
        // First, check for DotnetToolSettings.xml to detect RID-specific tool format
        string[] settingsFiles = Directory.GetFiles(toolsDir, "DotnetToolSettings.xml", SearchOption.AllDirectories);

        foreach (string settingsFile in settingsFiles)
        {
            try
            {
                var doc = XDocument.Load(settingsFile);
                var root = doc.Root;

                // Check for Version="2" (RID-specific tool format)
                string? version = root?.Attribute("Version")?.Value;
                if (version == "2")
                {
                    result.ToolFormat = "DotNetCliTool Version=\"2\" (RID-specific)";
                    result.IsRidSpecificPointerPackage = true;
                    result.IsFrameworkDependent = false; // RID packages are self-contained
                    result.HasRidSpecificAssets = true;

                    // Extract commands
                    var commands = root?.Element("Commands")?.Elements("Command");
                    if (commands != null)
                    {
                        result.ToolCommands = commands
                            .Select(c => c.Attribute("Name")?.Value)
                            .Where(n => n != null)
                            .Cast<string>()
                            .ToList();
                    }

                    // Extract RuntimeIdentifierPackages
                    var ridPackages = root?.Element("RuntimeIdentifierPackages")?.Elements("RuntimeIdentifierPackage");
                    if (ridPackages != null)
                    {
                        result.RuntimeIdentifierPackages = ridPackages
                            .Select(r => new RidPackageReference
                            {
                                RuntimeIdentifier = r.Attribute("RuntimeIdentifier")?.Value ?? "",
                                PackageId = r.Attribute("Id")?.Value ?? ""
                            })
                            .ToList();

                        // Also populate SupportedRids from the RuntimeIdentifierPackages
                        result.SupportedRids = result.RuntimeIdentifierPackages
                            .Select(r => r.RuntimeIdentifier)
                            .ToList();
                    }
                }
                else if (version == "1" || version == null)
                {
                    result.ToolFormat = "DotNetCliTool Version=\"1\" (portable)";

                    // Extract commands for v1 format too
                    var commands = root?.Element("Commands")?.Elements("Command");
                    if (commands != null)
                    {
                        result.ToolCommands = commands
                            .Select(c => c.Attribute("Name")?.Value)
                            .Where(n => n != null)
                            .Cast<string>()
                            .ToList();
                    }
                }
            }
            catch
            {
                // Ignore parse errors for settings files
            }
        }

        // Tools directory structure: tools/{tfm}/{rid}/ or tools/{tfm}/any/
        foreach (string tfmDir in Directory.GetDirectories(toolsDir))
        {
            string tfm = Path.GetFileName(tfmDir);
            result.TargetFrameworks ??= [];
            if (!result.TargetFrameworks.Contains(tfm))
            {
                result.TargetFrameworks.Add(tfm);
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
        var found = new List<string>();

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
