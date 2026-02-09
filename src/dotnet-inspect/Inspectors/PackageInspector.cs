using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Builds an InspectionResult for a NuGet package by running nuspec, directory,
/// deps.json, RID-verification, and NuGet metadata inspections.
/// </summary>
internal static class PackageInspector
{
    public static async Task<InspectionResult> InspectAsync(
        string extractPath,
        string packageName,
        string version,
        bool isLocalFile,
        string? localFilePath,
        bool includeDeps,
        NuspecData? nuspec,
        HttpClient httpClient,
        VerboseLogger logger)
    {
        var result = new InspectionResult
        {
            PackageName = packageName,
            Version = version
        };

        // Apply nuspec metadata (already parsed by caller)
        if (nuspec != null)
        {
            result.PackageName = nuspec.PackageName ?? result.PackageName;
            result.Version = nuspec.Version ?? result.Version;
            result.Description = nuspec.Description;
            result.Authors = nuspec.Authors;
            result.Repository = nuspec.Repository;
            result.License = nuspec.License;
            result.PackageTypes = nuspec.PackageTypes;
            result.IsToolPackage = nuspec.IsToolPackage;
            result.ReadmeFile = nuspec.ReadmeFile;
            result.DependencyGroups = nuspec.DependencyGroups;
        }

        // Check for README (use nuspec-specified file or fall back to README.md)
        string readmeFileName = result.ReadmeFile ?? "README.md";
        result.HasReadme = File.Exists(Path.Combine(extractPath, readmeFileName));

        // Analyze directory structure
        string toolsDir = Path.Combine(extractPath, "tools");
        string libDir = Path.Combine(extractPath, "lib");
        bool hasToolsDir = Directory.Exists(toolsDir);
        bool hasLibDir = Directory.Exists(libDir);

        if (hasToolsDir)
        {
            ToolsAnalyzer.AnalyzeToolsDirectory(toolsDir, result);
        }

        if (hasLibDir)
        {
            ToolsAnalyzer.AnalyzeLibDirectory(libDir, result);

            string runtimesDir = Path.Combine(extractPath, "runtimes");
            if (Directory.Exists(runtimesDir))
            {
                ToolsAnalyzer.AnalyzeRuntimesDirectory(runtimesDir, result);
            }
        }

        // Determine package type if not already set by nuspec PackageTypes
        if (result.PackageTypes is not { Count: > 0 })
        {
            // Only classify as tool if tools/ has actual DLLs and there's no lib/ dir.
            // Packages like AWSSDK.* have tools/ with only .ps1 scripts alongside lib/.
            result.IsToolPackage = hasToolsDir && !hasLibDir
                && Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories).Length > 0;
        }

        // Analyze content directories and count assemblies
        ToolsAnalyzer.AnalyzeContentDirectories(extractPath, result);
        result.AssemblyCount = ToolsAnalyzer.CountAssemblies(extractPath);

        // Parse deps.json files if deps flag is set
        if (includeDeps)
        {
            string[] depsFiles = Directory.GetFiles(extractPath, "*.deps.json", SearchOption.AllDirectories);
            foreach (string depsFile in depsFiles)
            {
                ApplyDepsJson(DepsJsonParser.Parse(depsFile), result);
            }
        }

        // Verify RID-specific packages exist (always do this for RID pointer packages)
        if (result.IsRidSpecificPointerPackage && result.RuntimeIdentifierPackages is { Count: > 0 })
        {
            string? localDir = isLocalFile ? Path.GetDirectoryName(Path.GetFullPath(localFilePath!)) : null;
            await RidPackageVerifier.VerifyAsync(httpClient, result, result.Version, localDir, logger);
        }

        // Fetch package metadata from NuGet (only for remote packages)
        if (!isLocalFile)
        {
            var metadata = await PackageMetadataService.FetchAllMetadataAsync(httpClient, packageName, version, logger.Log);
            ApplyMetadata(result, metadata);
        }

        return result;
    }

    private static void ApplyDepsJson(DepsJsonData depsJson, InspectionResult result)
    {
        if (depsJson.RuntimeTargetRid != null)
        {
            result.RuntimeTargetRid = depsJson.RuntimeTargetRid;
        }

        if (depsJson.RuntimeDependencies != null)
        {
            result.RuntimeDependencies ??= [];
            result.RuntimeDependencies.AddRange(depsJson.RuntimeDependencies);
        }
    }

    private static void ApplyMetadata(InspectionResult result, PackageMetadata metadata)
    {
        result.Published = metadata.Published;
        result.TotalDownloads = metadata.TotalDownloads;
        result.VersionDownloads = metadata.VersionDownloads;
        result.VersionCount = metadata.VersionCount;
        result.PackageSize = metadata.PackageSize;
        result.IsVerified = metadata.IsVerified;
        result.Owners = metadata.Owners;
        result.Deprecation = metadata.Deprecation;
        result.Vulnerabilities = metadata.Vulnerabilities;
    }

    /// <summary>
    /// Populates the Files list for detailed verbosity output.
    /// </summary>
    private static void PopulateFilesForDetailedView(string extractPath, InspectionResult result)
    {
        // Get DLLs from tools or lib directory
        string toolsDir = Path.Combine(extractPath, "tools");
        string libDir = Path.Combine(extractPath, "lib");

        string searchPath;
        if (Directory.Exists(toolsDir))
        {
            searchPath = toolsDir;
        }
        else if (Directory.Exists(libDir))
        {
            searchPath = libDir;
        }
        else
        {
            return;
        }

        var files = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(extractPath, f))
            .Where(p => !p.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p)
            .ToList();

        if (files.Count > 0)
        {
            result.Files = files;
        }
    }
}
