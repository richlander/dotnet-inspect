using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Handles PDB acquisition and source/documentation enrichment for API types.
/// Orchestrates symbol download, SourceLink resolution, source fetching, and doc comment parsing.
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

    // ===== Verbosity-Aware Enrichment Gateways =====

    /// <summary>
    /// Enriches a single type with documentation based on verbosity:
    /// Detailed+ or ShowSamples → full remote (PDB/SourceLink); Normal + ShowDocs → local XML only.
    /// </summary>
    internal static async Task EnrichDocsAsync(
        ApiType apiType, string typeName, string dllPath,
        ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        if (options.ShowSamples || options.Verbosity >= Verbosity.Detailed)
            await EnrichTypeWithSourceInfoAsync(apiType, typeName, dllPath, options, logger, httpClient);
        else if (options.ShowDocs && options.Verbosity >= Verbosity.Normal)
            EnrichFromLocalXmlDocs(apiType, dllPath, options, logger);
    }

    /// <summary>
    /// Enriches a list of types with documentation based on verbosity:
    /// Detailed+ or ShowSamples → full remote (PDB/SourceLink); Normal + ShowDocs → local XML only.
    /// Platform assemblies always use local XML (ref assemblies lack PDBs).
    /// </summary>
    internal static async Task EnrichDocsAsync(
        List<ApiType> types, string dllPath,
        ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        bool fullRemote = options.ShowSamples || options.Verbosity >= Verbosity.Detailed;
        bool localDocs = options.ShowDocs && options.Verbosity >= Verbosity.Normal;

        if (!fullRemote && !localDocs) return;

        if (!string.IsNullOrEmpty(options.PlatformAssembly))
        {
            // Platform: always local XML (ref assemblies don't have PDBs)
            EnrichTypesFromXmlDoc(types, options, logger);
        }
        else if (fullRemote)
        {
            await EnrichTypesWithSourceInfoBatchedAsync(types, dllPath, options, logger, httpClient);
        }
        else
        {
            EnrichFromLocalXmlDocs(types, dllPath, options, logger);
        }
    }

    // ===== Single-Type Enrichment =====

    internal static async Task EnrichTypeWithSourceInfoAsync(
        ApiType apiType,
        string typeName,
        string dllPath,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient,
        ResolvedAssemblyReference? sourceAssembly = null,
        string? fallbackPackageName = null,
        string? fallbackPackageVersion = null)
    {
        if (!string.IsNullOrEmpty(options.PlatformAssembly) && (options.UseLocalDocs || options.ShowDocs))
        {
            EnrichFromXmlDocFile(apiType, typeName, options, logger);
            return;
        }

        try
        {
            using var service = SourceLinkService.Open(dllPath, logger.Log);
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
                if (!string.IsNullOrEmpty(options.PlatformAssembly) && options.ShowDocs)
                {
                    logger.Log("No PDB available, falling back to XML documentation from packs directory");
                    EnrichFromXmlDocFile(apiType, typeName, options, logger);
                    return;
                }

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

            var sourceInfo = service.ResolveTypeSource(typeName);
            if (sourceInfo == null)
            {
                if (apiType.DefinitionName is not null
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
        catch (Exception ex)
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
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

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

        List<(ApiType Type, string TypeName, SourceLinkResolver.TypeSourceInfo? SourceInfo)> typeSourceInfo = [];
        Dictionary<string, (string Url, string? Algorithm, byte[]? Checksum)> allSourcesToFetch = [];

        foreach (var apiType in types)
        {
            var typeName = apiType.FullName;
            var sourceInfo = service.ResolveTypeSource(typeName);
            typeSourceInfo.Add((apiType, typeName, sourceInfo));

            if (sourceInfo?.SourceUrl != null)
            {
                AddSourceFetch(
                    allSourcesToFetch,
                    sourceInfo.SourceUrl,
                    sourceInfo.ChecksumAlgorithm,
                    sourceInfo.Checksum);
                if (sourceInfo.AdditionalSourceFiles != null)
                {
                    foreach (var additional in sourceInfo.AdditionalSourceFiles)
                    {
                        if (additional.SourceUrl != null)
                        {
                            AddSourceFetch(
                                allSourcesToFetch,
                                additional.SourceUrl,
                                additional.ChecksumAlgorithm,
                                additional.Checksum);
                        }
                    }
                }
            }
        }

        logger.Log($"Phase 1: Resolved {typeSourceInfo.Count} types, {allSourcesToFetch.Count} unique source documents ({stopwatch.ElapsedMilliseconds}ms)");

        var fetcher = new SourceFetcher(DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
        var fetchList = allSourcesToFetch
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => entry.Value)
            .ToList();
        var contentCache = new ConcurrentDictionary<string, string?>();

        logger.Log($"Phase 2: Fetching {fetchList.Count} source documents (max 16 concurrent)");
        await Parallel.ForEachAsync(fetchList,
            new ParallelOptions { MaxDegreeOfParallelism = 16 },
            async (sourceFetch, ct) =>
            {
                var result = await PdbSourceAcquisition.FetchVerifiedSourceTextAsync(
                    fetcher,
                    sourceFetch.Url,
                    sourceFetch.Algorithm,
                    sourceFetch.Checksum,
                    ct);
                contentCache[SourceFetchKey(
                    sourceFetch.Url,
                    sourceFetch.Algorithm,
                    sourceFetch.Checksum)] = result.Text;
                if (result.Failure is not null)
                    logger.LogWarning(result.Failure);
            });

        logger.Log($"Phase 2: Fetched {contentCache.Count(kv => kv.Value != null)} of {contentCache.Count} URLs ({stopwatch.ElapsedMilliseconds}ms)");

        var parser = new DocCommentParser();
        foreach (var (apiType, typeName, sourceInfo) in typeSourceInfo)
        {
            if (sourceInfo == null)
                continue;

            apiType.SourceFilePath = sourceInfo.SourceFilePath;
            apiType.SourceUrl = sourceInfo.SourceUrl;
            apiType.GitHubBrowseUrl = sourceInfo.GitHubBrowseUrl;
            apiType.SourceLineNumber = sourceInfo.LineNumber;
            apiType.SourceResolution = sourceInfo.ResolutionMethod.ToString();
            apiType.SourceChecksum = sourceInfo.Checksum;
            apiType.SourceChecksumAlgorithm = sourceInfo.ChecksumAlgorithm;

            if (sourceInfo.AdditionalSourceFiles?.Count > 0)
            {
                apiType.AdditionalSourceFiles = sourceInfo.AdditionalSourceFiles
                    .Select(f => new PartialSourceFileInfo
                    {
                        FilePath = f.FilePath,
                        SourceUrl = f.SourceUrl,
                        GitHubBrowseUrl = f.GitHubBrowseUrl,
                        SourceChecksum = f.Checksum,
                        SourceChecksumAlgorithm = f.ChecksumAlgorithm,
                    })
                    .ToList();
            }

            if ((options.ShowDocs || options.ShowSamples) && sourceInfo.SourceUrl != null)
            {
                List<(string Content, string Url, string FilePath)> sourceContents = [];

                if (contentCache.TryGetValue(
                        SourceFetchKey(
                            sourceInfo.SourceUrl,
                            sourceInfo.ChecksumAlgorithm,
                            sourceInfo.Checksum),
                        out var primaryContent)
                    && primaryContent != null)
                {
                    sourceContents.Add((primaryContent, sourceInfo.SourceUrl, sourceInfo.SourceFilePath ?? ""));
                }

                if (sourceInfo.AdditionalSourceFiles != null)
                {
                    foreach (var additional in sourceInfo.AdditionalSourceFiles)
                    {
                        if (additional.SourceUrl != null &&
                            contentCache.TryGetValue(
                                SourceFetchKey(
                                    additional.SourceUrl,
                                    additional.ChecksumAlgorithm,
                                    additional.Checksum),
                                out var additionalContent) &&
                            additionalContent != null)
                        {
                            sourceContents.Add((additionalContent, additional.SourceUrl, additional.FilePath));
                        }
                    }
                }

                if (sourceContents.Count > 0)
                {
                    MergePartialTypeDocumentation(apiType, sourceContents, parser, options, logger);
                }
            }
        }

        logger.Log($"Phase 3: Parsed docs for {typeSourceInfo.Count} types ({stopwatch.ElapsedMilliseconds}ms total)");
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

    /// <summary>
    /// Enriches multiple types from a single XML doc file (loaded once).
    /// </summary>
    internal static void EnrichTypesFromXmlDoc(IEnumerable<ApiType> types, ApiOptions options, VerboseLogger logger)
    {
        if (string.IsNullOrEmpty(options.PlatformAssembly))
        {
            logger.Log("XML doc fallback only available for platform libraries");
            return;
        }

        var (refPath, version, error) = PlatformResolver.ResolveFramework(
            options.PlatformFramework ?? "runtime");

        if (error != null || refPath == null)
        {
            logger.Log($"Could not resolve framework for XML docs: {error}");
            return;
        }

        var xmlDocPath = Path.Combine(refPath, $"{options.PlatformAssembly}.xml");
        if (!File.Exists(xmlDocPath))
        {
            logger.Log($"XML doc file not found: {xmlDocPath}");
            return;
        }

        logger.Log($"Loading XML documentation from: {xmlDocPath}");

        var xmlParser = new XmlDocFileParser();
        if (!xmlParser.Load(xmlDocPath))
        {
            logger.Log("Failed to load XML documentation file");
            return;
        }

        foreach (var apiType in types)
        {
            EnrichTypeFromXmlDoc(apiType, xmlParser, options, logger);
        }
    }

    private static void EnrichTypeFromXmlDoc(ApiType apiType, XmlDocFileParser xmlParser, ApiOptions options, VerboseLogger logger)
    {
        var typeDoc = xmlParser.GetTypeDocumentation(apiType.FullName);
        if (typeDoc != null)
        {
            apiType.Documentation = new DocComment
            {
                Summary = typeDoc.Summary,
                Remarks = typeDoc.Remarks
            };
        }

        if (options.ShowDocs)
        {
            foreach (var member in apiType.Members)
            {
                var memberDoc = xmlParser.GetMemberDocumentation(apiType, member);
                if (memberDoc != null)
                {
                    member.Documentation = new DocComment
                    {
                        Summary = memberDoc.Summary,
                        Remarks = memberDoc.Remarks,
                        Parameters = memberDoc.Parameters,
                        Returns = memberDoc.Returns
                    };
                }
            }
        }

        apiType.SourceResolution = "XmlDoc";
    }

    /// <summary>
    /// Enriches multiple types from local XML doc files (no network).
    /// For packages/libraries, looks for XML alongside the DLL.
    /// </summary>
    internal static void EnrichFromLocalXmlDocs(IEnumerable<ApiType> types, string dllPath, ApiOptions options, VerboseLogger logger)
    {
        var xmlDocPath = Path.ChangeExtension(dllPath, ".xml");
        if (!File.Exists(xmlDocPath))
        {
            logger.Log($"XML doc file not found alongside DLL: {xmlDocPath}");
            return;
        }

        logger.Log($"Loading XML documentation from: {xmlDocPath}");

        var xmlParser = new XmlDocFileParser();
        if (!xmlParser.Load(xmlDocPath))
        {
            logger.Log("Failed to load XML documentation file");
            return;
        }

        foreach (var apiType in types)
        {
            EnrichTypeFromXmlDoc(apiType, xmlParser, options, logger);
        }
    }

    /// <summary>
    /// Enriches a type from local XML doc files only (no network).
    /// Works for both platform assemblies (ref packs) and NuGet packages (alongside DLL).
    /// </summary>
    internal static void EnrichFromLocalXmlDocs(ApiType apiType, string dllPath, ApiOptions options, VerboseLogger logger)
    {
        // Platform path: use ref packs directory
        if (!string.IsNullOrEmpty(options.PlatformAssembly))
        {
            EnrichFromXmlDocFile(apiType, apiType.FullName, options, logger);
            return;
        }

        // Package/library path: look for XML alongside the DLL
        var xmlDocPath = Path.ChangeExtension(dllPath, ".xml");
        if (!File.Exists(xmlDocPath))
        {
            logger.Log($"XML doc file not found alongside DLL: {xmlDocPath}");
            return;
        }

        logger.Log($"Loading XML documentation from: {xmlDocPath}");

        var xmlParser = new XmlDocFileParser();
        if (!xmlParser.Load(xmlDocPath))
        {
            logger.Log("Failed to load XML documentation file");
            return;
        }

        EnrichTypeFromXmlDoc(apiType, xmlParser, options, logger);
    }

    private static void EnrichFromXmlDocFile(ApiType apiType, string typeName, ApiOptions options, VerboseLogger logger)
    {
        if (string.IsNullOrEmpty(options.PlatformAssembly))
        {
            logger.Log("XML doc fallback only available for platform libraries");
            return;
        }

        var (refPath, version, error) = PlatformResolver.ResolveFramework(
            options.PlatformFramework ?? "runtime");

        if (error != null || refPath == null)
        {
            logger.Log($"Could not resolve framework for XML docs: {error}");
            return;
        }

        var xmlDocPath = Path.Combine(refPath, $"{options.PlatformAssembly}.xml");
        if (!File.Exists(xmlDocPath))
        {
            logger.Log($"XML doc file not found: {xmlDocPath}");
            return;
        }

        logger.Log($"Loading XML documentation from: {xmlDocPath}");

        var xmlParser = new XmlDocFileParser();
        if (!xmlParser.Load(xmlDocPath))
        {
            logger.Log("Failed to load XML documentation file");
            return;
        }

        EnrichTypeFromXmlDoc(apiType, xmlParser, options, logger);
    }

    internal static void MergePartialTypeDocumentation(
        ApiType apiType,
        List<(string Content, string Url, string FilePath)> sourceContents,
        DocCommentParser parser,
        ApiOptions options,
        VerboseLogger logger)
    {

        DocComment? mergedTypeDoc = null;
        List<SampleReference> allSamples = [];

        foreach (var (content, url, _) in sourceContents)
        {
            var typeDoc = parser.ExtractTypeDocComment(content, apiType.Name);
            if (typeDoc != null)
            {
                if (mergedTypeDoc == null)
                {
                    mergedTypeDoc = new DocComment
                    {
                        Summary = typeDoc.Summary,
                        Remarks = typeDoc.Remarks,
                        Parameters = typeDoc.Parameters,
                        Returns = typeDoc.Returns
                    };
                    logger.Log("Found type documentation.");
                }
                else
                {
                    mergedTypeDoc.Summary ??= typeDoc.Summary;
                    mergedTypeDoc.Remarks ??= typeDoc.Remarks;
                    mergedTypeDoc.Returns ??= typeDoc.Returns;
                    if (typeDoc.Parameters != null)
                    {
                        mergedTypeDoc.Parameters ??= new Dictionary<string, string>();
                        foreach (var (key, value) in typeDoc.Parameters)
                        {
                            mergedTypeDoc.Parameters.TryAdd(key, value);
                        }
                    }
                    logger.Log("Merged additional type documentation.");
                }

                if (typeDoc.Samples != null)
                {
                    allSamples.AddRange(typeDoc.Samples.Select(s => new SampleReference
                    {
                        RelativePath = s.RelativePath,
                        Description = s.Description,
                        Region = s.Region,
                        ResolvedUrl = GitHubUrlResolver.ResolveSampleUrl(url, s.RelativePath)
                    }));
                }
            }

            var membersToDocument = options.MemberFilter.Count > 0
                ? apiType.Members.Where(m => options.MemberFilter.Contains(m.Name))
                : apiType.Members;

            foreach (var member in membersToDocument)
            {
                if (member.Documentation.Summary == null)
                {
                    var memberDoc = parser.ExtractMemberDocComment(content, apiType.Name, member.Name);
                    if (memberDoc != null)
                    {
                        member.Documentation = new DocComment
                        {
                            Summary = memberDoc.Summary,
                            Remarks = memberDoc.Remarks,
                            Parameters = memberDoc.Parameters,
                            Returns = memberDoc.Returns,
                            Samples = memberDoc.Samples?.Select(s => new SampleReference
                            {
                                RelativePath = s.RelativePath,
                                Description = s.Description,
                                Region = s.Region,
                                ResolvedUrl = GitHubUrlResolver.ResolveSampleUrl(url, s.RelativePath)
                            }).ToList() ?? []
                        };
                    }
                }
            }
        }

        if (mergedTypeDoc != null)
        {
            mergedTypeDoc.Samples = allSamples;
            apiType.Documentation = mergedTypeDoc;
        }
    }

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

    internal static async Task ApplySourceInfoAsync(
        ApiType apiType,
        SourceLinkResolver.TypeSourceInfo sourceInfo,
        ApiOptions options,
        VerboseLogger logger)
    {
        apiType.SourceFilePath = sourceInfo.SourceFilePath;
        apiType.SourceUrl = sourceInfo.SourceUrl;
        apiType.GitHubBrowseUrl = sourceInfo.GitHubBrowseUrl;
        apiType.SourceLineNumber = sourceInfo.LineNumber;
        apiType.SourceResolution = sourceInfo.ResolutionMethod.ToString();
        apiType.SourceChecksum = sourceInfo.Checksum;
        apiType.SourceChecksumAlgorithm = sourceInfo.ChecksumAlgorithm;

        if (sourceInfo.AdditionalSourceFiles.Count > 0)
        {
            apiType.AdditionalSourceFiles = sourceInfo.AdditionalSourceFiles
                .Select(f => new PartialSourceFileInfo
                {
                    FilePath = f.FilePath,
                    SourceUrl = f.SourceUrl,
                    GitHubBrowseUrl = f.GitHubBrowseUrl,
                    SourceChecksum = f.Checksum,
                    SourceChecksumAlgorithm = f.ChecksumAlgorithm,
                })
                .ToList();
            logger.Log(
                $"Found partial type with {sourceInfo.AdditionalSourceFiles.Count + 1} source files");
        }

        logger.Log(
            $"Source ({sourceInfo.ResolutionMethod}) resolved"
                + (sourceInfo.LineNumber is { } line ? $" at line {line}." : "."));

        if (!(options.ShowDocs || options.ShowSamples)
            || sourceInfo.SourceUrl is null)
        {
            return;
        }

        var fetcher = new SourceFetcher(
            DotnetInspector.Core.HttpClientFactory.SharedUntrustedFetch);
        var parser = new DocCommentParser();
        List<(string Url, string FilePath, string? Algorithm, byte[]? Checksum)> sourceFilesToFetch =
        [
            (
                sourceInfo.SourceUrl,
                sourceInfo.SourceFilePath ?? "",
                sourceInfo.ChecksumAlgorithm,
                sourceInfo.Checksum)
        ];

        foreach (var additionalFile in sourceInfo.AdditionalSourceFiles)
        {
            if (additionalFile.SourceUrl is not null)
                sourceFilesToFetch.Add(
                    (
                        additionalFile.SourceUrl,
                        additionalFile.FilePath,
                        additionalFile.ChecksumAlgorithm,
                        additionalFile.Checksum));
        }

        List<(string Content, string Url, string FilePath)> allSourceContents = [];
        string? primaryNamespace = null;
        bool isPrimaryPartial = false;

        foreach ((string url, string filePath, string? algorithm, byte[]? checksum) in sourceFilesToFetch)
        {
            logger.Log("Fetching SourceLink source.");
            var fetch = await PdbSourceAcquisition.FetchVerifiedSourceTextAsync(
                fetcher,
                url,
                algorithm,
                checksum);
            string? content = fetch.Text;
            if (content is null)
            {
                logger.LogWarning(fetch.Failure ?? "Could not fetch SourceLink source.");
                continue;
            }

            logger.Log($"Fetched {content.Length} source bytes.");
            if (allSourceContents.Count == 0)
            {
                isPrimaryPartial =
                    IsPartialTypeDeclaration(content, apiType.Name);
                primaryNamespace = ExtractNamespace(content);
                allSourceContents.Add((content, url, filePath));
            }
            else if (isPrimaryPartial)
            {
                bool isMatchingPartial =
                    IsPartialTypeDeclaration(content, apiType.Name);
                string? fileNamespace = ExtractNamespace(content);
                if (isMatchingPartial && fileNamespace == primaryNamespace)
                {
                    allSourceContents.Add((content, url, filePath));
                    logger.Log("Validated matching partial source file.");
                }
                else
                {
                    logger.Log("Skipping non-matching partial source file.");
                }
            }
        }

        if (allSourceContents.Count > 0)
        {
            MergePartialTypeDocumentation(
                apiType,
                allSourceContents,
                parser,
                options,
                logger);
        }
    }

    private static void AddSourceFetch(
        Dictionary<string, (string Url, string? Algorithm, byte[]? Checksum)> sources,
        string url,
        string? algorithm,
        byte[]? checksum)
        => sources.TryAdd(
            SourceFetchKey(url, algorithm, checksum),
            (url, algorithm, checksum));

    private static string SourceFetchKey(
        string url,
        string? algorithm,
        byte[]? checksum)
        => $"{url}\n{algorithm}\n{(checksum is null ? "" : Convert.ToHexString(checksum))}";

    private static bool IsPartialTypeDeclaration(string sourceContent, string typeName)
    {
        var pattern = $@"\bpartial\s+(?:class|struct|interface|record)\s+{Regex.Escape(typeName)}\b";
        return Regex.IsMatch(sourceContent, pattern);
    }

    private static string? ExtractNamespace(string sourceContent)
    {
        var match = Regex.Match(sourceContent, @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }
}
