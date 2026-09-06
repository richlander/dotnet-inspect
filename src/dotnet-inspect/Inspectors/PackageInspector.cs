using System.IO.Compression;
using ILInspector.Metadata;
using DotnetInspector.Core;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Builds an InspectionResult for a NuGet package by running nuspec, directory,
/// deps.json, RID-verification, and NuGet metadata inspections.
/// </summary>
internal static class PackageInspector
{
    public static async Task<InspectionResult> InspectAsync(
        PackageExtractionResult resolution,
        string fallbackPackageName,
        string fallbackVersion,
        bool isLocalFile,
        string? localFilePath,
        NuspecData? nuspec,
        HttpClient httpClient,
        VerboseLogger logger,
        bool forceLatest = false,
        Verbosity verbosity = Verbosity.Minimal,
        bool fetchMetadata = false,
        bool requireIdentifierMetadata = false,
        bool verifyRidPackageAvailability = false,
        NuGetSourceOptions? sourceOptions = null)
    {
        string extractPath = resolution.ExtractPath;
        string packageName = resolution.PackageName ?? fallbackPackageName;
        string version = resolution.Version ?? fallbackVersion;
        string? nupkgPath = resolution.NupkgPath;
        fetchMetadata = !isLocalFile
            && resolution.Authority?.Kind != ConfiguredPackageAuthorityKind.LocalFolder
            && (fetchMetadata || verbosity >= Verbosity.Detailed);
        NuGetSourceOptions? metadataSourceOptions = resolution.Authority is { } authority
            ? NuGetSourceResolver.RestrictToResolvedSources(sourceOptions, [authority.Source])
            : resolution.ProducerKey is null
                ? sourceOptions
                : NuGetSourceResolver.RestrictToSourceKeys(
                    sourceOptions,
                    [resolution.ProducerKey]);

        // Try package index cache (skips all filesystem scanning)
        if (!isLocalFile && resolution.CacheScopeKey is { } cacheScopeKey)
        {
            InspectionResult? cached;
            using (NetworkTelemetry.Scope(NetworkTrafficKind.PackageLoad))
            {
                cached = PackageIndexCache.TryGet(
                    packageName,
                    version,
                    cacheScopeKey);
            }
            if (cached != null)
            {
                if (resolution.ToolWrapperChain.Count == 0
                    && verifyRidPackageAvailability
                    && cached.IsRidSpecificPointerPackage
                    && PackageIndexCache.RequiresRidReverification(cached))
                {
                    await RidPackageVerifier.VerifyAsync(
                        httpClient,
                        cached,
                        cached.Version ?? version,
                        localDir: null,
                        logger,
                        sourceOptions);
                }

                if (fetchMetadata)
                {
                    var metadata = await PackageMetadataService.FetchAllMetadataAsync(
                        httpClient,
                        packageName,
                        version,
                        logger.Log,
                        forceLatest,
                        metadataSourceOptions);
                    ApplyMetadata(
                        cached,
                        metadata,
                        requireIdentifierMetadata);
                }
                await ApplyToolWrapperClassificationAsync(
                    cached,
                    resolution,
                    isLocalFile,
                    localFilePath,
                    httpClient,
                    logger,
                    verifyRidPackageAvailability,
                    sourceOptions);
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

        result.PackageReadmeFile = PackageFileLister.ResolvePackageReadme(extractPath, result.ReadmeFile);
        result.HasReadme = result.PackageReadmeFile != null;
        result.HasAgentDocumentation = File.Exists(Path.Combine(extractPath, "AGENTS.md"));

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
            extractPath, packageName, version, httpClient, logger,
            acquirePdb: false, sourceOptions);

        // Parse deps.json files (present in tool packages, typically in tools/{tfm}/{rid}/)
        if (hasToolsDir)
        {
            foreach (string depsFile in Directory.GetFiles(toolsDir, "*.deps.json", SearchOption.AllDirectories))
            {
                ApplyDepsJson(DepsJsonParser.Parse(depsFile), result);
            }
        }

        // Verify RID-specific packages exist (always do this for RID pointer packages)
        if (resolution.ToolWrapperChain.Count == 0
            && verifyRidPackageAvailability
            && result.IsRidSpecificPointerPackage
            && result.RuntimeIdentifierPackages is { Count: > 0 })
        {
            string? localDir = isLocalFile ? Path.GetDirectoryName(Path.GetFullPath(localFilePath!)) : null;
            await RidPackageVerifier.VerifyAsync(
                httpClient, result, result.Version, localDir, logger, sourceOptions);
        }

        // Extract build date from nupkg (only on cache miss)
        if (nupkgPath != null && File.Exists(nupkgPath))
        {
            result.BuiltDate = GetNupkgBuildDate(nupkgPath);
        }

        // Cache the filesystem-derived result (before metadata overlay)
        if (!isLocalFile && resolution.CacheScopeKey is { } writeCacheScopeKey)
        {
            using var cacheScope = NetworkTelemetry.Scope(NetworkTrafficKind.PackageLoad);
            PackageIndexCache.Set(
                packageName,
                version,
                writeCacheScopeKey,
                result);
        }

        // Fetch package metadata from NuGet (only at detailed verbosity)
        if (fetchMetadata)
        {
            var metadata = await PackageMetadataService.FetchAllMetadataAsync(
                httpClient,
                packageName,
                version,
                logger.Log,
                forceLatest,
                metadataSourceOptions);
            ApplyMetadata(
                result,
                metadata,
                requireIdentifierMetadata);
        }

        await ApplyToolWrapperClassificationAsync(
            result,
            resolution,
            isLocalFile,
            localFilePath,
            httpClient,
            logger,
            verifyRidPackageAvailability,
            sourceOptions);
        return result;
    }

    private static async Task ApplyToolWrapperClassificationAsync(
        InspectionResult result,
        PackageExtractionResult resolution,
        bool isLocalFile,
        string? localFilePath,
        HttpClient httpClient,
        VerboseLogger logger,
        bool verifyRidPackageAvailability,
        NuGetSourceOptions? sourceOptions)
    {
        ToolWrapperPackage? wrapper = resolution.ToolWrapperChain.FirstOrDefault();
        if (wrapper is null)
            return;

        string wrapperVersion = wrapper.Version ?? result.Version;
        NuspecProbeResult wrapperNuspecProbe =
            await PackageExtractor.ProbeExtractedPackageNuspecAsync(
                wrapper.ExtractPath,
                wrapper.PackageName,
                wrapperVersion,
                logger.Log).ConfigureAwait(false);
        NuspecData? wrapperNuspec =
            wrapperNuspecProbe is
            {
                Status: NuspecProbeStatus.Present,
                Xml: { } wrapperNuspecXml,
            }
                ? NuspecParser.ParseContent(wrapperNuspecXml)
                : null;
        result.PackageTypes = wrapperNuspec?.PackageTypes;
        result.IsToolPackage |= wrapperNuspec?.IsToolPackage == true;

        string toolsDir = Path.Combine(wrapper.ExtractPath, "tools");
        if (!Directory.Exists(toolsDir))
            return;

        var wrapperTool = new InspectionResult();
        ToolsAnalyzer.AnalyzeToolsDirectory(toolsDir, wrapperTool);
        if (string.IsNullOrWhiteSpace(wrapperTool.ToolFormat))
            return;

        result.IsToolPackage = true;
        result.ManifestVersion = wrapperTool.ManifestVersion;
        result.ToolFormat = wrapperTool.ToolFormat;
        result.ToolCommands = wrapperTool.ToolCommands;
        result.IsRidSpecificPointerPackage = wrapperTool.IsRidSpecificPointerPackage;
        result.RuntimeIdentifierPackages = wrapperTool.RuntimeIdentifierPackages;

        if (verifyRidPackageAvailability
            && result.IsRidSpecificPointerPackage
            && result.RuntimeIdentifierPackages is { Count: > 0 })
        {
            IReadOnlyDictionary<string, NuspecProbeStatus> acquiredEvidence =
                await MarkAcquiredRidPackagesAsync(
                result,
                resolution,
                wrapperVersion,
                logger.Log);

            string? localDir = isLocalFile
                ? Path.GetDirectoryName(Path.GetFullPath(localFilePath!))
                : null;
            await RidPackageVerifier.VerifyAsync(
                httpClient,
                result,
                wrapperVersion,
                localDir,
                logger,
                sourceOptions,
                acquiredEvidence);
        }
    }

    internal static async Task<IReadOnlyDictionary<string, NuspecProbeStatus>>
        MarkAcquiredRidPackagesAsync(
        InspectionResult result,
        PackageExtractionResult resolution,
        string? wrapperVersion,
        Action<string>? log = null)
    {
        if (result.RuntimeIdentifierPackages is not { Count: > 0 })
        {
            return new Dictionary<string, NuspecProbeStatus>(
                StringComparer.OrdinalIgnoreCase);
        }

        List<(
            string ExtractPath,
            string? PackageName,
            string? Version)> acquiredPackages =
            resolution.ToolWrapperChain
                .Skip(1)
                .Select(package => (
                    package.ExtractPath,
                    (string?)package.PackageName,
                    package.Version))
                .Append((
                    resolution.ExtractPath,
                    resolution.PackageName,
                    resolution.Version))
                .ToList();
        HashSet<string> requestedPackageIds = result.RuntimeIdentifierPackages
            .Select(package => package.PackageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, NuspecProbeStatus> acquiredEvidence =
            new(StringComparer.OrdinalIgnoreCase);

        foreach (var acquired in acquiredPackages)
        {
            if (acquired.PackageName is not null
                && acquired.Version is not null
                && requestedPackageIds.Contains(acquired.PackageName)
                && VersionsEqual(
                    wrapperVersion,
                    acquired.Version))
            {
                NuspecProbeStatus status =
                    (await PackageExtractor.ProbeExtractedPackageNuspecAsync(
                        acquired.ExtractPath,
                        acquired.PackageName,
                        acquired.Version,
                        log).ConfigureAwait(false)).Status;
                acquiredEvidence[acquired.PackageName] =
                    acquiredEvidence.TryGetValue(
                        acquired.PackageName,
                        out NuspecProbeStatus previous)
                        ? RidPackageVerifier.CombineEvidence(previous, status)
                        : status;
            }
        }

        foreach (RidPackageReference package in result.RuntimeIdentifierPackages)
        {
            if (acquiredEvidence.TryGetValue(
                    package.PackageId,
                    out NuspecProbeStatus status)
                && status == NuspecProbeStatus.Present)
            {
                package.Exists = true;
            }
        }

        return acquiredEvidence;
    }

    private static bool VersionsEqual(string? left, string? right)
        => PackageExtractor.TryNormalizePackageVersion(
               left,
               out string leftVersion)
           && PackageExtractor.TryNormalizePackageVersion(
               right,
               out string rightVersion)
           && string.Equals(
               leftVersion,
               rightVersion,
               StringComparison.OrdinalIgnoreCase);

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
        bool acquirePdb,
        NuGetSourceOptions? sourceOptions = null)
    {
        var dlls = TfmSelector.GetPackageAssemblies(extractPath);
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
                        isPlatformAssembly: false, logger.Log,
                        sourceOptions: sourceOptions).ConfigureAwait(false);
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
                logger.LogWarning($"Error scanning binary signals in {Path.GetFileName(dll)}: {ex.Message}");
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

    private static void ApplyMetadata(
        InspectionResult result,
        PackageMetadata metadata,
        bool requireIdentifierMetadata)
    {
        result.Published = metadata.Published;
        result.TotalDownloads = metadata.TotalDownloads;
        result.VersionDownloads = metadata.VersionDownloads;
        result.VersionCount = metadata.VersionCount;
        result.PackageSize = metadata.PackageSize;
        result.IsVerified = metadata.IsVerified;
        result.Listed = metadata.Listed;
        result.Owners = metadata.Owners;
        result.Deprecation = metadata.Deprecation;
        result.Vulnerabilities = metadata.Vulnerabilities;
        result.IdentifierConfusionFailure =
            requireIdentifierMetadata
            && !metadata.DeprecationMetadataAvailable
                ? IdentifierConfusionAuditFailureKind
                    .PackageMetadataUnavailable
                : null;
        result.IdentifierConfusionRegistryScopeLimited =
            requireIdentifierMetadata
            && metadata.DeprecationMetadataAvailable
            && !metadata.DeprecationMetadataSupported;
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
