using ILInspector.Metadata;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// Handles PDB acquisition and source-location enrichment for API types.
/// </summary>
internal static class SourceEnricher
{
    // ===== PDB Acquisition =====

    /// <summary>
    /// Orchestrates PDB download for a PdbContext. Shared by LibraryCommand and API enrichment.
    /// </summary>
    internal static async Task AcquirePdbAsync(
        PdbContext context, HttpClient httpClient,
        string? packageName, string? packageVersion,
        bool isPlatformAssembly, Action<string>? log,
        bool cacheOnly = false,
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default)
        => await PdbAcquisitionService.AcquireAsync(
            context,
            httpClient,
            packageName,
            packageVersion,
            isPlatformAssembly,
            log,
            cacheOnly,
            sourceOptions,
            cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Acquires symbols using the provenance of the descriptor that supplied the
    /// authoritative assembly bytes.
    /// </summary>
    internal static Task AcquirePdbAsync(
        PdbContext context,
        ResolvedAssemblyReference assembly,
        HttpClient httpClient,
        Action<string>? log,
        bool cacheOnly = false,
        NuGetSourceOptions? sourceOptions = null,
        CancellationToken cancellationToken = default,
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
        => PdbAcquisitionService.AcquireAsync(
            context,
            assembly,
            httpClient,
            log,
            cacheOnly,
            sourceOptions,
            cancellationToken,
            fallbackPackageName,
            fallbackPackageVersion);

    // ===== Single-Type Enrichment =====

    internal static async Task EnrichTypeWithSourceInfoAsync(
        ApiType apiType,
        string dllPath,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient,
        ResolvedAssemblyReference? sourceAssembly = null,
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
    {
        string typeName = apiType.FullName;

        try
        {
            using var service = sourceAssembly is null
                ? SourceLinkService.Open(dllPath, logger.Log)
                : SourceLinkService.Open(sourceAssembly, logger.Log);
            var context = service.Context;

            if (!context.HasMetadata)
            {
                logger.Log("No metadata in library, cannot resolve source.");
                return;
            }

            var (packageName, packageVersion) = ResolvePackageInfo(options, dllPath);
            packageName = fallbackPackageName ?? packageName;
            packageVersion = fallbackPackageVersion ?? packageVersion;

            if (sourceAssembly is null)
            {
                await AcquirePdbAsync(
                    context,
                    httpClient,
                    packageName,
                    packageVersion,
                    isPlatformAssembly:
                        !string.IsNullOrEmpty(
                            options.PlatformAssembly),
                    logger.Log,
                    sourceOptions: options.SourceOptions);
            }
            else
            {
                await AcquirePdbAsync(
                    context,
                    sourceAssembly,
                    httpClient,
                    logger.Log,
                    sourceOptions: options.SourceOptions,
                    fallbackPackageName: packageName,
                    fallbackPackageVersion: packageVersion);
            }

            if (!context.HasPdb)
            {
                CommandError.WriteBlankLine();
                if (context.WindowsPdbDetected)
                {
                    CommandError.WriteWarning("PDB could not be read (Windows PDB format is not supported).");
                    CommandError.WriteLine("         Only Portable PDBs are supported. Consider asking the maintainer");
                    CommandError.WriteLine("         to publish Portable PDBs (embedded or in .snupkg).");
                }
                else
                {
                    CommandError.WriteWarning("No readable PDB found.");
                }
                CommandError.WriteLine("         Use 'library <target> -S \"SourceLink: Availability\"' for full source reachability.");
                CommandError.WriteBlankLine();
                return;
            }

            if (!service.HasSourceLink)
            {
                logger.Log("No SourceLink information found in PDB.");
                return;
            }

            var sourceInfo =
                apiType.DefinitionName is { } definitionName
                    ? service.ResolveTypeSource(definitionName)
                    : service.ResolveTypeSource(typeName);
            if (sourceInfo == null)
            {
                if (sourceAssembly is null
                    && apiType.DefinitionName is not null
                    && await TryEnrichFromForwardedAssemblyAsync(
                        apiType,
                        typeName,
                        apiType.DefinitionName,
                        dllPath,
                        options,
                        logger,
                        httpClient))
                {
                    return;
                }

                logger.Log($"Could not find type definition for '{typeName}'.");
                return;
            }
            await ApplySourceInfoAsync(
                apiType,
                sourceInfo,
                options,
                logger);
        }
        catch (Exception ex) when (sourceAssembly is null)
        {
            logger.Log($"Error enriching source info: {ex.Message}");
        }
    }

    // ===== Batched Enrichment =====

    internal static async Task EnrichTypesWithSourceInfoBatchedAsync(
        List<ApiType> types,
        string dllPath,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var (packageName, packageVersion) = ResolvePackageInfo(options, dllPath);

        using var service = SourceLinkService.Open(dllPath, logger.Log);
        var context = service.Context;

        if (!context.HasMetadata)
        {
            logger.Log("No metadata in library, cannot resolve source.");
            return;
        }

        await AcquirePdbAsync(context, httpClient, packageName, packageVersion,
            isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log,
            sourceOptions: options.SourceOptions);

        if (!context.HasPdb)
        {
            if (context.WindowsPdbDetected)
            {
                CommandError.WriteBlankLine();
                CommandError.WriteWarning("PDB could not be read (Windows PDB format is not supported).");
                CommandError.WriteLine("         Only Portable PDBs are supported.");
                CommandError.WriteBlankLine();
            }
            return;
        }

        if (!service.HasSourceLink)
        {
            logger.Log("No SourceLink information found in PDB.");
            return;
        }

        int resolvedTypes = 0;
        foreach (var apiType in types)
        {
            var typeName = apiType.FullName;
            var sourceInfo = service.ResolveTypeSource(typeName);
            if (sourceInfo is not null
                && ProjectSourceInfo(apiType, sourceInfo) is not null)
                resolvedTypes++;
        }
        logger.Log(
            $"Resolved source locations for {resolvedTypes} of "
                + $"{types.Count} types.");
    }

    // ===== Repository URL Extraction =====

    /// <summary>
    /// Extracts the repository URL from SourceLink information in the assembly's PDB.
    /// </summary>
    internal static async Task<string?> ExtractRepositoryUrlAsync(string dllPath, ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        try
        {
            using var service = SourceLinkService.Open(dllPath, logger.Log);
            var context = service.Context;

            if (!context.HasMetadata)
                return null;

            var (packageName, packageVersion) = ResolvePackageInfo(options, dllPath);

            await AcquirePdbAsync(context, httpClient, packageName, packageVersion,
                isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log,
                sourceOptions: options.SourceOptions);

            return service.RepositoryUrl;
        }
        catch (Exception ex)
        {
            logger.Log($"Error extracting repository URL: {ex.Message}");
        }
        return null;
    }

    // ===== Private Helpers =====

    /// <summary>
    /// Resolves package name and version from options and DLL path.
    /// Shared by enrichment methods that need package info for PDB acquisition.
    /// </summary>
    private static (string? packageName, string? packageVersion) ResolvePackageInfo(ApiOptions options, string dllPath)
    {
        if (string.IsNullOrEmpty(options.PackagePath))
            return (null, null);

        var (packageName, packageVersion) = PackageExtractor.ParsePackageReference(options.PackagePath);
        if (packageVersion == null && !string.IsNullOrEmpty(packageName))
        {
            // Try extracting version from dllPath (works when path contains /packagename/version/)
            packageVersion = PackageExtractor.ExtractVersionFromPath(dllPath, packageName);

            // Fallback: dllPath may be in a temp dir (e.g., /tmp/inspect-api-.../extracted/...)
            // that doesn't encode the version. Check the cache directory instead.
            if (packageVersion == null)
            {
                packageVersion = FindCachedPackageVersion(packageName, options);
            }
        }
        return (packageName, packageVersion);
    }

    /// <summary>
    /// Finds the latest cached version candidate reported by an active source.
    /// </summary>
    internal static string? FindCachedPackageVersion(string packageName, ApiOptions options)
        => PackageExtractor.TryGetLatestCachedCandidateVersion(
            packageName,
            SourceResolver.ResolveSourceKeysForProbe(
                options.SourceOptions,
                packageName));

    private static async Task<bool> TryEnrichFromForwardedAssemblyAsync(
        ApiType apiType,
        string typeName,
        MetadataTypeDefinitionName definitionName,
        string originalDllPath,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        using var resolution = new TypeDefinitionResolutionSession(
            originalDllPath,
            isPlatformAssembly: !string.IsNullOrEmpty(
                options.PlatformAssembly),
            options);
        TypeResolutionOutcome outcome = resolution.Resolve(definitionName);
        if (outcome is not TypeResolutionOutcome.Resolved resolved
            || resolved.Hops.IsDefaultOrEmpty)
        {
            if (outcome is not TypeResolutionOutcome.NotFound)
            {
                logger.Log(
                    $"Could not resolve forwarded definition for '{typeName}': {outcome.GetType().Name}.");
            }
            return false;
        }

        ResolvedAssemblyReference implementation =
            resolved.Definition.Assembly.Assembly;
        logger.Log(
            $"Following {outcome.Hops.Length} type-forwarding hop(s) to '{implementation.Identity.Name}'.");

        using var service = SourceLinkService.Open(implementation, logger.Log);
        await AcquirePdbAsync(
            service.Context,
            implementation,
            httpClient,
            logger.Log,
            sourceOptions: options.SourceOptions);
        if (!service.HasPdb || !service.HasSourceLink)
            return false;

        SourceLinkResolver.TypeSourceInfo? sourceInfo =
            service.ResolveTypeSource(typeName);
        if (sourceInfo is null)
            return false;

        await ApplySourceInfoAsync(apiType, sourceInfo, options, logger);
        return true;
    }

    internal static Task ApplySourceInfoAsync(
        ApiType apiType,
        SourceLinkResolver.TypeSourceInfo sourceInfo,
        ApiOptions options,
        VerboseLogger logger)
    {
        SourceLinkResolver.TypeSourceDocument? defaultDocument =
            ProjectSourceInfo(apiType, sourceInfo);
        if (defaultDocument is null)
            return Task.CompletedTask;

        if (apiType.AdditionalSourceFiles.Count > 0)
        {
            logger.Log(
                $"Found type with {sourceInfo.Documents.Length} source files");
        }

        logger.Log(
            $"Source ({defaultDocument.ResolutionMethod}) resolved.");
        return Task.CompletedTask;
    }

    private static SourceLinkResolver.TypeSourceDocument? ProjectSourceInfo(
        ApiType apiType,
        SourceLinkResolver.TypeSourceInfo sourceInfo)
    {
        SourceLinkResolver.TypeSourceDocument? defaultDocument =
            TypeSourceDocumentSelection.SelectDefault(sourceInfo);
        if (defaultDocument is null)
            return null;

        apiType.SourceFilePath = defaultDocument.FilePath;
        apiType.SourceUrl = defaultDocument.SourceUrl;
        apiType.GitHubBrowseUrl = defaultDocument.GitHubBrowseUrl;
        apiType.SourceLineNumber = null;
        apiType.SourceResolution = defaultDocument.ResolutionMethod.ToString();
        apiType.SourceChecksum = defaultDocument.Checksum;
        apiType.SourceChecksumAlgorithm = defaultDocument.ChecksumAlgorithm;
        apiType.AdditionalSourceFiles =
        [
            .. sourceInfo.Documents
                .Where(document => !ReferenceEquals(
                    document,
                    defaultDocument))
                .Select(static document => new PartialSourceFileInfo
                {
                    FilePath = document.FilePath,
                    SourceUrl = document.SourceUrl,
                    GitHubBrowseUrl = document.GitHubBrowseUrl,
                    SourceChecksum = document.Checksum,
                    SourceChecksumAlgorithm = document.ChecksumAlgorithm,
                }),
        ];
        return defaultDocument;
    }

}
