using System.IO.Compression;
using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
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
        bool forceLatest = false,
        Verbosity verbosity = Verbosity.Minimal,
        string? nupkgPath = null,
        bool fetchMetadata = false)
    {
        fetchMetadata = !isLocalFile && (fetchMetadata || verbosity >= Verbosity.Detailed);

        // Try package index cache (skips all filesystem scanning)
        if (!isLocalFile)
        {
            var cached = PackageIndexCache.TryGet(packageName, version);
            if (cached != null)
            {
                if (fetchMetadata)
                {
                    var metadata = await PackageMetadataService.FetchAllMetadataAsync(httpClient, packageName, version, logger.Log, forceLatest);
                    ApplyMetadata(cached, metadata);
                }
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
            result.ManifestVersion = nuspec.ManifestVersion;
            result.Version = nuspec.Version ?? result.Version;
            result.Description = nuspec.Description;
            result.Authors = nuspec.Authors;
            result.Repository = nuspec.Repository;
            result.RepositoryType = nuspec.RepositoryType;
            result.RepositoryCommit = nuspec.RepositoryCommit;
            result.License = nuspec.License;
            result.LicenseUrl = nuspec.LicenseUrl;
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
        PopulateLibraryFiles(extractPath, result);
        result.BinarySignals = await ScanBinarySignalsAsync(
            extractPath, packageName, version, httpClient, logger, acquirePdb: false);

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

        // Extract build date from nupkg (only on cache miss)
        if (nupkgPath != null && File.Exists(nupkgPath))
        {
            result.BuiltDate = GetNupkgBuildDate(nupkgPath);
        }

        // Cache the filesystem-derived result (before metadata overlay)
        if (!isLocalFile)
        {
            PackageIndexCache.Set(packageName, version, result);
        }

        // Fetch package metadata from NuGet (only at detailed verbosity)
        if (fetchMetadata)
        {
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

    internal static async Task<PackageBinarySignals?> ScanBinarySignalsAsync(
        string extractPath,
        string? packageName,
        string? packageVersion,
        HttpClient httpClient,
        VerboseLogger logger,
        bool acquirePdb)
    {
        var dlls = TfmSelector.GetPackageDlls(extractPath)
            .Where(f => !f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (dlls.Count == 0)
            return null;

        int symbols = 0;
        int sourceLink = 0;
        int embeddedPdbs = 0;
        int inPackagePdbs = 0;
        int snupkgPdbs = 0;
        int msdlPdbs = 0;
        int otherPdbs = 0;
        int embeddedSourceLinkPdbs = 0;
        int inPackageSourceLinkPdbs = 0;
        int snupkgSourceLinkPdbs = 0;
        int msdlSourceLinkPdbs = 0;
        int otherSourceLinkPdbs = 0;
        foreach (var dll in dlls)
        {
            try
            {
                using var service = SourceLinkService.Open(dll);
                if (acquirePdb && service.NeedsPdb)
                {
                    await SourceEnricher.AcquirePdbAsync(
                        service.Context, httpClient, packageName, packageVersion,
                        isPlatformAssembly: false, logger.Log).ConfigureAwait(false);
                }

                if (service.HasPdb)
                {
                    symbols++;
                    switch (GetPackagePdbSource(service.Context))
                    {
                        case PackagePdbSource.Embedded:
                            embeddedPdbs++;
                            if (service.HasSourceLink) embeddedSourceLinkPdbs++;
                            break;
                        case PackagePdbSource.InPackage:
                            inPackagePdbs++;
                            if (service.HasSourceLink) inPackageSourceLinkPdbs++;
                            break;
                        case PackagePdbSource.Snupkg:
                            snupkgPdbs++;
                            if (service.HasSourceLink) snupkgSourceLinkPdbs++;
                            break;
                        case PackagePdbSource.Msdl:
                            msdlPdbs++;
                            if (service.HasSourceLink) msdlSourceLinkPdbs++;
                            break;
                        default:
                            otherPdbs++;
                            if (service.HasSourceLink) otherSourceLinkPdbs++;
                            break;
                    }
                }
                if (service.HasSourceLink)
                    sourceLink++;
            }
            catch (Exception ex)
            {
                logger.Log($"Warning: Error scanning binary signals in {Path.GetFileName(dll)}: {ex.Message}");
            }
        }

        return new PackageBinarySignals
        {
            TotalBinaries = dlls.Count,
            SymbolsAvailable = symbols,
            SourceLinkAvailable = sourceLink,
            EmbeddedPdbs = embeddedPdbs,
            InPackagePdbs = inPackagePdbs,
            SnupkgPdbs = snupkgPdbs,
            MsdlPdbs = msdlPdbs,
            OtherPdbs = otherPdbs,
            EmbeddedSourceLinkPdbs = embeddedSourceLinkPdbs,
            InPackageSourceLinkPdbs = inPackageSourceLinkPdbs,
            SnupkgSourceLinkPdbs = snupkgSourceLinkPdbs,
            MsdlSourceLinkPdbs = msdlSourceLinkPdbs,
            OtherSourceLinkPdbs = otherSourceLinkPdbs
        };
    }

    private enum PackagePdbSource
    {
        Embedded,
        InPackage,
        Snupkg,
        Msdl,
        Other
    }

    private static PackagePdbSource GetPackagePdbSource(PdbContext context)
    {
        if (context.HasEmbeddedPdb || context.PdbLocation?.Equals("Embedded", StringComparison.OrdinalIgnoreCase) == true)
            return PackagePdbSource.Embedded;

        if (context.SymbolServer?.Equals("msdl.microsoft.com", StringComparison.OrdinalIgnoreCase) == true)
            return PackagePdbSource.Msdl;

        if (context.SymbolServer?.Equals("nuget.org", StringComparison.OrdinalIgnoreCase) == true)
            return PackagePdbSource.Snupkg;

        if (context.PdbLocation?.Equals("Standalone", StringComparison.OrdinalIgnoreCase) == true)
            return PackagePdbSource.InPackage;

        return PackagePdbSource.Other;
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

    private static void PopulateLibraryFiles(string extractPath, InspectionResult result)
    {
        var libDir = Path.Combine(extractPath, "lib");
        if (!Directory.Exists(libDir))
            return;

        var files = Directory.GetFiles(libDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(extractPath, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count > 0)
            result.LibraryFiles = files;
    }

    /// <summary>
    /// Extracts the build date from a .nupkg file by finding the newest content file timestamp.
    /// Excludes NuGet packaging artifacts (.signature.p7s, _rels/, [Content_Types].xml, .psmdcp)
    /// which may have signing/publish dates rather than build dates.
    /// </summary>
    private static DateTimeOffset? GetNupkgBuildDate(string nupkgPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(nupkgPath);
            DateTimeOffset newest = DateTimeOffset.MinValue;
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName;
                if (name.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("_rels/", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("package/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entry.LastWriteTime > newest)
                    newest = entry.LastWriteTime;
            }
            return newest > DateTimeOffset.MinValue ? newest : null;
        }
        catch
        {
            return null;
        }
    }
}
