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
        NuspecData? nuspec,
        HttpClient httpClient,
        VerboseLogger logger,
        bool forceLatest = false)
    {
        // Try package index cache (skips all filesystem scanning)
        if (!isLocalFile)
        {
            var cached = PackageIndexCache.TryGet(packageName, version);
            if (cached != null)
            {
                // Apply live metadata (cached separately with its own TTL)
                var metadata = await PackageMetadataService.FetchAllMetadataAsync(httpClient, packageName, version, logger.Log, forceLatest);
                ApplyMetadata(cached, metadata);
                return cached;
            }
        }

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
            // TargetFrameworks populated by AnalyzeToolsDirectory implies DLLs exist.
            result.IsToolPackage = hasToolsDir && !hasLibDir
                && result.TargetFrameworks is { Count: > 0 };
        }

        // Analyze content directories and count assemblies
        ToolsAnalyzer.AnalyzeContentDirectories(extractPath, result);
        result.AssemblyCount = ToolsAnalyzer.CountAssemblies(extractPath);

        // Parse deps.json files (present in tool packages, typically in tools/{tfm}/{rid}/)
        if (hasToolsDir)
        {
            foreach (string depsFile in Directory.GetFiles(toolsDir, "*.deps.json", SearchOption.AllDirectories))
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
            // Cache the filesystem-derived result before applying metadata
            PackageIndexCache.Set(packageName, version, result);

            var metadata = await PackageMetadataService.FetchAllMetadataAsync(httpClient, packageName, version, logger.Log, forceLatest);
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
