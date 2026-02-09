using DotnetInspector.Packages;
using System.Text.RegularExpressions;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Commands;

/// <summary>
/// Shared extraction, enrichment, and utility methods used by API-related commands.
/// </summary>
internal static class ApiServices
{
    // ===== Extraction Pipeline =====

    /// <summary>
    /// Extracts a specific type from a package or assembly, with full path info.
    /// Used by api command and SamplesCommand.
    /// </summary>
    internal static async Task<(ApiType? type, string? foundIn, string? dllPath)> ExtractTypeWithPathAsync(string typeName, ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        string? tempDir = null;
        string? runtimeAssemblyPath = null;
        try
        {
            string searchPath;

            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                var extracted = await PackageExtractor.ExtractPackageAsync(httpClient, options.PackagePath, logger.Log, "inspect-api", options.SourceOptions);
                if (extracted == null)
                    return (null, null, null);

                (searchPath, tempDir, _, _) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);

                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = TfmSelector.FindAssemblyByTfm(searchPath, options.Tfm);
                    if (tfmAssembly != null)
                        searchPath = tfmAssembly;
                }
                else
                {
                    var (highestPath, _) = TfmSelector.SelectHighestTfmAssembly(TfmSelector.GetPackageDlls(searchPath), searchPath);
                    if (highestPath != null)
                        searchPath = highestPath;
                }
            }
            else if (!string.IsNullOrEmpty(options.AssemblyPath))
            {
                searchPath = options.AssemblyPath;
            }
            else if (!string.IsNullOrEmpty(options.PlatformAssembly))
            {
                var (assemblyPath, framework, version, error) = PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly,
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: false);

                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return (null, null, null);
                }

                searchPath = assemblyPath!;
                logger.Log($"Using platform ref assembly: {framework} {version}");

                var (runtimePath, _, _, runtimeError) = PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly,
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: true);

                if (runtimeError == null && runtimePath != null)
                {
                    runtimeAssemblyPath = runtimePath;
                    logger.Log($"Using runtime assembly for PDB lookup: {runtimePath}");
                }
            }
            else
            {
                return (null, null, null);
            }

            var (apiType, foundIn, dllPath, surface) = FindType(typeName, searchPath, logger, options.IncludeAll);

            if (apiType != null && dllPath != null && options.ShowDocs)
            {
                var pdbLookupPath = runtimeAssemblyPath ?? dllPath;
                await EnrichTypeWithSourceInfoAsync(apiType, typeName, pdbLookupPath, options, logger, httpClient);
            }

            return (apiType, foundIn, dllPath);
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    /// <summary>
    /// Extracts a specific type from a package or assembly. Used by api command.
    /// </summary>
    internal static async Task<(ApiType? type, string? foundIn)> ExtractTypeAsync(string typeName, ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        var (type, foundIn, _) = await ExtractTypeWithPathAsync(typeName, options, logger, httpClient);
        return (type, foundIn);
    }

    /// <summary>
    /// Extracts the full API surface from a package or assembly, enriching types with source info.
    /// Used by SamplesCommand for assembly-wide sample collection and DiffCommand.
    /// </summary>
    internal static async Task<(ApiSurface? api, string? selectedTfm)> ExtractApiSurfaceAsync(ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        string? tempDir = null;
        try
        {
            string searchPath;
            string? packageName = null;

            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                var extracted = await PackageExtractor.ExtractPackageAsync(httpClient, options.PackagePath, logger.Log, "inspect-api", options.SourceOptions);
                if (extracted == null)
                    return (null, null);
                (searchPath, tempDir, packageName, _) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);

                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = TfmSelector.FindAssemblyByTfm(searchPath, options.Tfm);
                    if (tfmAssembly != null)
                        searchPath = tfmAssembly;
                }
            }
            else if (!string.IsNullOrEmpty(options.AssemblyPath))
            {
                searchPath = options.AssemblyPath;
            }
            else
            {
                return (null, null);
            }

            string? selectedTfm = null;
            if (Directory.Exists(searchPath))
            {
                var dlls = TfmSelector.GetPackageDlls(searchPath);
                if (dlls.Count > 1)
                {
                    var (selectedPath, tfm) = TfmSelector.SelectHighestTfmAssembly(dlls, searchPath);
                    if (selectedPath != null)
                    {
                        searchPath = selectedPath;
                        selectedTfm = tfm;
                    }
                }
            }

            var (api, dllPath) = ExtractFullApi(searchPath, logger, options.IncludeAll);
            if (api == null || dllPath == null)
                return (null, null);

            api.Name = packageName ?? Path.GetFileNameWithoutExtension(dllPath);
            api.Tfm = selectedTfm;

            if (options.ShowDocs || options.ShowSamples)
            {
                await EnrichTypesWithSourceInfoBatchedAsync(api.Types.ToList(), dllPath, options, logger, httpClient);
            }

            return (api, selectedTfm);
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    internal static (ApiType? type, string? assembly, string? dllPath, ApiSurface? surface) FindType(string typeName, string searchPath, VerboseLogger logger, bool includeAll)
    {
        string[] dllFiles;
        if (File.Exists(searchPath) && searchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            dllFiles = [searchPath];
        }
        else if (Directory.Exists(searchPath))
        {
            dllFiles = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories);
        }
        else
        {
            return (null, null, null, null);
        }

        foreach (var dllFile in dllFiles)
        {
            var api = AssemblyReader.ExtractApiSurface(dllFile, includeAll);
            if (api == null)
                continue;

            var match = api.Types.FirstOrDefault(t =>
                t.FullName.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                logger.Log($"Found in: {Path.GetFileName(dllFile)}");
                return (match, Path.GetFileName(dllFile), dllFile, api);
            }
        }

        return (null, null, null, null);
    }

    internal static (ApiSurface? api, string? dllPath) ExtractFullApi(string searchPath, VerboseLogger logger, bool includeAll)
    {
        string? dllFile;
        if (File.Exists(searchPath) && searchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            dllFile = searchPath;
        }
        else if (Directory.Exists(searchPath))
        {
            var libDir = Path.Combine(searchPath, "lib");
            if (Directory.Exists(libDir))
            {
                var dlls = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories).ToList();
                var (selectedPath, selectedTfm) = TfmSelector.SelectHighestTfmAssembly(dlls, searchPath);
                dllFile = selectedPath;
                if (selectedTfm != null)
                {
                    logger.Log($"Auto-selected TFM: {selectedTfm}");
                }
            }
            else
            {
                var dlls = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories).ToList();
                var (selectedPath, _) = TfmSelector.SelectHighestTfmAssembly(dlls, searchPath);
                dllFile = selectedPath ?? dlls.FirstOrDefault();
            }
        }
        else
        {
            return (null, null);
        }

        if (dllFile == null)
        {
            return (null, null);
        }

        logger.Log($"Extracting API from: {Path.GetFileName(dllFile)}");

        var api = AssemblyReader.ExtractApiSurface(dllFile, includeAll);
        return (api, api != null ? dllFile : null);
    }

    /// <summary>
    /// Resolves types from forwarded assemblies when the primary assembly is a type-forwarding assembly.
    /// </summary>
    internal static void ResolveForwardedTypes(ApiSurface api, string dllPath, VerboseLogger logger, bool includeAll)
    {
        if (api.Types.Count > 0 || api.TypeForwarders.Count == 0)
            return;

        var assemblyDir = Path.GetDirectoryName(dllPath);
        if (assemblyDir == null)
            return;

        var byAssembly = api.TypeForwarders
            .GroupBy(f => f.TargetAssembly)
            .ToDictionary(g => g.Key, g => g.Select(f => f.TypeName).ToHashSet(StringComparer.OrdinalIgnoreCase));

        logger.Log($"Resolving {api.TypeForwarders.Count} forwarded types from {byAssembly.Count} assemblies...");

        foreach (var (targetAssembly, forwardedTypeNames) in byAssembly)
        {
            var targetPath = Path.Combine(assemblyDir, targetAssembly + ".dll");
            if (!File.Exists(targetPath))
            {
                logger.Log($"Target assembly '{targetAssembly}' not found, skipping.");
                continue;
            }

            try
            {
                var targetApi = AssemblyReader.ExtractApiSurface(targetPath, includeAll);
                if (targetApi == null)
                    continue;

                foreach (var type in targetApi.Types)
                {
                    if (forwardedTypeNames.Contains(type.FullName))
                    {
                        api.Types.Add(type);
                        api.PublicMethodCount += type.Members?.Count(m => m.Kind == "method" || m.Kind == "constructor") ?? 0;
                        api.PublicPropertyCount += type.Members?.Count(m => m.Kind == "property") ?? 0;
                        api.PublicEventCount += type.Members?.Count(m => m.Kind == "event") ?? 0;
                        api.PublicFieldCount += type.Members?.Count(m => m.Kind == "field") ?? 0;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Error reading '{targetAssembly}': {ex.Message}");
            }
        }

        if (api.Types.Count > 0)
        {
            api.IsTypeForwardingAssembly = true;
            api.PublicTypeCount = api.Types.Count;
            api.Types = api.Types.OrderBy(t => t.FullName).ToList();
            logger.Log($"Resolved {api.Types.Count} types from forwarded assemblies.");
        }
    }

    // ===== PDB Acquisition Helper =====

    /// <summary>
    /// Orchestrates PDB download for a PdbContext. Shared by AssemblyCommand and API enrichment.
    /// </summary>
    internal static async Task AcquirePdbAsync(
        PdbContext context, HttpClient httpClient,
        string? packageName, string? packageVersion,
        bool isPlatformAssembly, Action<string>? log)
    {
        if (!context.NeedsPdb) return;

        var downloader = new SymbolPackageDownloader(httpClient);
        var result = await downloader.DownloadPdbAsync(
            context.PdbId!.Guid, context.PdbId.Age, context.PdbId.PdbFileName,
            context.PdbId.IsPortable, context.AssemblyPath,
            packageName, packageVersion, log, isPlatformAssembly);

        if (result.PdbFilePath != null)
            context.LoadPdbFromFile(result.PdbFilePath, "Symbol Package", result.SymbolServer);
        else if (result.WindowsPdbDetected)
            context.WindowsPdbDetected = true;
    }

    // ===== Enrichment Pipeline =====

    internal static async Task EnrichTypeWithSourceInfoAsync(ApiType apiType, string typeName, string dllPath, ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        if (options.UseLocalDocs && !string.IsNullOrEmpty(options.PlatformAssembly))
        {
            await EnrichFromXmlDocFileAsync(apiType, typeName, options, logger);
            return;
        }

        try
        {
            using var context = PdbContext.Open(dllPath, logger.Log);

            if (!context.HasMetadata)
            {
                logger.Log("No metadata in assembly, cannot resolve source.");
                return;
            }

            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = PackageReferenceParser.ParsePackageReference(options.PackagePath);

                if (packageVersion == null && !string.IsNullOrEmpty(packageName))
                {
                    packageVersion = PackageReferenceParser.ExtractVersionFromPath(dllPath, packageName);
                }
            }

            await AcquirePdbAsync(context, httpClient, packageName, packageVersion,
                isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log);

            if (!context.HasPdb)
            {
                if (!string.IsNullOrEmpty(options.PlatformAssembly) && options.ShowDocs)
                {
                    logger.Log("No PDB available, falling back to XML documentation from packs directory");
                    await EnrichFromXmlDocFileAsync(apiType, typeName, options, logger);
                    return;
                }

                Console.Error.WriteLine();
                if (context.WindowsPdbDetected)
                {
                    Console.Error.WriteLine("Warning: PDB could not be read (Windows PDB format is not supported).");
                    Console.Error.WriteLine("         Only Portable PDBs are supported. Consider asking the maintainer");
                    Console.Error.WriteLine("         to publish Portable PDBs (embedded or in .snupkg).");
                }
                else
                {
                    Console.Error.WriteLine("Warning: No readable PDB found.");
                }
                Console.Error.WriteLine("         Run 'assembly --sourcelink-audit' for more details.");
                Console.Error.WriteLine();
                return;
            }

            if (!context.HasSourceLink)
            {
                logger.Log("No SourceLink information found in PDB.");
                return;
            }

            var sourceInfo = context.ResolveTypeSource(typeName);
            if (sourceInfo == null)
            {
                var forwardTarget = context.FindTypeForwarder(typeName);
                if (forwardTarget != null)
                {
                    logger.Log($"Type '{typeName}' is forwarded to '{forwardTarget}'.");

                    var forwardedResult = await TryEnrichFromForwardedAssemblyAsync(
                        apiType, typeName, forwardTarget, dllPath, options, logger, httpClient);
                    if (forwardedResult)
                        return;
                }

                logger.Log($"Could not find type definition for '{typeName}'.");
                return;
            }
            if (sourceInfo != null)
            {
                apiType.SourceFilePath = sourceInfo.SourceFilePath;
                apiType.SourceUrl = sourceInfo.SourceUrl;
                apiType.GitHubBrowseUrl = sourceInfo.GitHubBrowseUrl;
                apiType.SourceLineNumber = sourceInfo.LineNumber;
                apiType.SourceResolution = sourceInfo.ResolutionMethod.ToString();

                if (sourceInfo.AdditionalSourceFiles?.Count > 0)
                {
                    apiType.AdditionalSourceFiles = sourceInfo.AdditionalSourceFiles
                        .Select(f => new PartialSourceFileInfo
                        {
                            FilePath = f.FilePath,
                            SourceUrl = f.SourceUrl,
                            GitHubBrowseUrl = f.GitHubBrowseUrl
                        })
                        .ToList();
                    logger.Log($"Found partial type with {sourceInfo.AdditionalSourceFiles.Count + 1} source files");
                }

                logger.Log($"Source ({sourceInfo.ResolutionMethod}): {sourceInfo.SourceFilePath}:{sourceInfo.LineNumber}");
            }

            if ((options.ShowDocs || options.ShowSamples) && sourceInfo?.SourceUrl != null)
            {
                var fetcher = new SourceFetcher(httpClient);
                var parser = new DocCommentParser();

                var sourceFilesToFetch = new List<(string Url, string FilePath)>
                {
                    (sourceInfo.SourceUrl, sourceInfo.SourceFilePath ?? "")
                };

                if (sourceInfo.AdditionalSourceFiles != null)
                {
                    foreach (var additionalFile in sourceInfo.AdditionalSourceFiles)
                    {
                        if (additionalFile.SourceUrl != null)
                        {
                            sourceFilesToFetch.Add((additionalFile.SourceUrl, additionalFile.FilePath));
                        }
                    }
                }

                var allSourceContents = new List<(string Content, string Url, string FilePath)>();
                string? primaryNamespace = null;
                bool isPrimaryPartial = false;

                foreach (var (url, filePath) in sourceFilesToFetch)
                {
                    logger.Log($"Fetching source from: {url}");
                    string? content = await fetcher.FetchSourceAsync(url);

                    if (content != null)
                    {
                        logger.Log($"Fetched {content.Length} bytes from {Path.GetFileName(filePath)}");

                        if (allSourceContents.Count == 0)
                        {
                            isPrimaryPartial = IsPartialTypeDeclaration(content, apiType.Name);
                            primaryNamespace = ExtractNamespace(content);
                            allSourceContents.Add((content, url, filePath));
                        }
                        else if (isPrimaryPartial)
                        {
                            bool isMatchingPartial = IsPartialTypeDeclaration(content, apiType.Name);
                            string? fileNamespace = ExtractNamespace(content);

                            if (isMatchingPartial && fileNamespace == primaryNamespace)
                            {
                                allSourceContents.Add((content, url, filePath));
                                logger.Log($"Validated matching partial in {Path.GetFileName(filePath)}");
                            }
                            else
                            {
                                logger.Log($"Skipping {Path.GetFileName(filePath)} - not a matching partial type");
                            }
                        }
                    }
                    else
                    {
                        logger.Log($"Could not fetch source from: {url}");
                    }
                }

                if (allSourceContents.Count > 0)
                {
                    await MergePartialTypeDocumentation(apiType, allSourceContents, parser, options, logger);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Error enriching source info: {ex.Message}");
        }
    }

    private static async Task EnrichTypesWithSourceInfoBatchedAsync(
        List<ApiType> types,
        string dllPath,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        string? packageName = null;
        string? packageVersion = null;
        if (!string.IsNullOrEmpty(options.PackagePath))
        {
            (packageName, packageVersion) = PackageReferenceParser.ParsePackageReference(options.PackagePath);
            if (packageVersion == null && !string.IsNullOrEmpty(packageName))
            {
                packageVersion = PackageReferenceParser.ExtractVersionFromPath(dllPath, packageName);
            }
        }

        using var context = PdbContext.Open(dllPath, logger.Log);

        if (!context.HasMetadata)
        {
            logger.Log("No metadata in assembly, cannot resolve source.");
            return;
        }

        await AcquirePdbAsync(context, httpClient, packageName, packageVersion,
            isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log);

        if (!context.HasPdb)
        {
            if (context.WindowsPdbDetected)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Warning: PDB could not be read (Windows PDB format is not supported).");
                Console.Error.WriteLine("         Only Portable PDBs are supported.");
                Console.Error.WriteLine();
            }
            return;
        }

        var resolver = context.GetResolver();
        var pdbReader = context.GetPdbReader();
        var metadataReader = context.GetMetadataReader();

        if (resolver == null || pdbReader == null || metadataReader == null)
        {
            logger.Log("No SourceLink information found in PDB.");
            return;
        }

        var typeSourceInfo = new List<(ApiType Type, string TypeName, SourceLinkResolver.TypeSourceInfo? SourceInfo)>();
        var allUrlsToFetch = new HashSet<string>();

        foreach (var apiType in types)
        {
            var typeName = apiType.FullName;
            var sourceInfo = resolver.ResolveTypeSource(metadataReader, pdbReader, typeName);
            typeSourceInfo.Add((apiType, typeName, sourceInfo));

            if (sourceInfo?.SourceUrl != null)
            {
                allUrlsToFetch.Add(sourceInfo.SourceUrl);
                if (sourceInfo.AdditionalSourceFiles != null)
                {
                    foreach (var additional in sourceInfo.AdditionalSourceFiles)
                    {
                        if (additional.SourceUrl != null)
                            allUrlsToFetch.Add(additional.SourceUrl);
                    }
                }
            }
        }

        logger.Log($"Phase 1: Resolved {typeSourceInfo.Count} types, {allUrlsToFetch.Count} unique source URLs ({stopwatch.ElapsedMilliseconds}ms)");

        var fetcher = new SourceFetcher(httpClient);
        var urlList = allUrlsToFetch.OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList();
        var contentCache = new Dictionary<string, string?>();

        logger.Log($"Phase 2: Fetching {urlList.Count} URLs in parallel");
        var fetchTasks = urlList.Select(async url =>
        {
            var content = await fetcher.FetchSourceAsync(url);
            return (Url: url, Content: content);
        });

        var results = await Task.WhenAll(fetchTasks);
        foreach (var result in results)
        {
            contentCache[result.Url] = result.Content;
        }

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

            if (sourceInfo.AdditionalSourceFiles?.Count > 0)
            {
                apiType.AdditionalSourceFiles = sourceInfo.AdditionalSourceFiles
                    .Select(f => new PartialSourceFileInfo
                    {
                        FilePath = f.FilePath,
                        SourceUrl = f.SourceUrl,
                        GitHubBrowseUrl = f.GitHubBrowseUrl
                    })
                    .ToList();
            }

            if ((options.ShowDocs || options.ShowSamples) && sourceInfo.SourceUrl != null)
            {
                var sourceContents = new List<(string Content, string Url, string FilePath)>();

                if (contentCache.TryGetValue(sourceInfo.SourceUrl, out var primaryContent) && primaryContent != null)
                {
                    sourceContents.Add((primaryContent, sourceInfo.SourceUrl, sourceInfo.SourceFilePath ?? ""));
                }

                if (sourceInfo.AdditionalSourceFiles != null)
                {
                    foreach (var additional in sourceInfo.AdditionalSourceFiles)
                    {
                        if (additional.SourceUrl != null &&
                            contentCache.TryGetValue(additional.SourceUrl, out var additionalContent) &&
                            additionalContent != null)
                        {
                            sourceContents.Add((additionalContent, additional.SourceUrl, additional.FilePath));
                        }
                    }
                }

                if (sourceContents.Count > 0)
                {
                    await MergePartialTypeDocumentation(apiType, sourceContents, parser, options, logger);
                }
            }
        }

        logger.Log($"Phase 3: Parsed docs for {typeSourceInfo.Count} types ({stopwatch.ElapsedMilliseconds}ms total)");
    }

    private static async Task EnrichFromXmlDocFileAsync(ApiType apiType, string typeName, ApiOptions options, VerboseLogger logger)
    {
        await Task.CompletedTask;

        if (string.IsNullOrEmpty(options.PlatformAssembly))
        {
            logger.Log("XML doc fallback only available for platform assemblies");
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

        var typeDoc = xmlParser.GetTypeDocumentation(apiType.FullName);
        if (typeDoc != null)
        {
            apiType.Documentation = new DocComment
            {
                Summary = typeDoc.Summary,
                Remarks = typeDoc.Remarks
            };
            logger.Log($"Found type documentation for {apiType.FullName}");
        }

        if (options.ShowDocs && apiType.Members != null)
        {
            foreach (var member in apiType.Members)
            {
                var memberDoc = xmlParser.GetMemberDocumentation(apiType.FullName, member.Name, member.Kind);
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
            logger.Log($"Enriched {apiType.Members.Count} members with documentation");
        }

        apiType.SourceResolution = "XmlDoc";
    }

    private static async Task MergePartialTypeDocumentation(
        ApiType apiType,
        List<(string Content, string Url, string FilePath)> sourceContents,
        DocCommentParser parser,
        ApiOptions options,
        VerboseLogger logger)
    {
        await Task.CompletedTask;

        DocComment? mergedTypeDoc = null;
        var allSamples = new List<SampleReference>();

        foreach (var (content, url, filePath) in sourceContents)
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
                    logger.Log($"Found type docs in {Path.GetFileName(filePath)}");
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
                    logger.Log($"Merged additional type docs from {Path.GetFileName(filePath)}");
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

            if (apiType.Members != null)
            {
                var membersToDocument = options.MemberFilter?.Count > 0
                    ? apiType.Members.Where(m => options.MemberFilter.Contains(m.Name))
                    : apiType.Members;

                foreach (var member in membersToDocument)
                {
                    if (member.Documentation == null)
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
                                }).ToList()
                            };
                        }
                    }
                }
            }
        }

        if (mergedTypeDoc != null)
        {
            mergedTypeDoc.Samples = allSamples.Count > 0 ? allSamples : null;
            apiType.Documentation = mergedTypeDoc;
        }
    }

    /// <summary>
    /// Extracts the repository URL from SourceLink information in the assembly's PDB.
    /// </summary>
    internal static async Task<string?> ExtractRepositoryUrlAsync(string dllPath, ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        try
        {
            using var context = PdbContext.Open(dllPath, logger.Log);

            if (!context.HasMetadata)
                return null;

            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = PackageReferenceParser.ParsePackageReference(options.PackagePath);
                if (packageVersion == null && !string.IsNullOrEmpty(packageName))
                {
                    packageVersion = PackageReferenceParser.ExtractVersionFromPath(dllPath, packageName);
                }
            }

            await AcquirePdbAsync(context, httpClient, packageName, packageVersion,
                isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly), logger.Log);

            return context.ExtractRepositoryUrl();
        }
        catch (Exception ex)
        {
            logger.Log($"Error extracting repository URL: {ex.Message}");
        }
        return null;
    }

    // ===== Private Enrichment Helpers =====

    private static async Task<bool> TryEnrichFromForwardedAssemblyAsync(
        ApiType apiType,
        string typeName,
        string targetAssemblyName,
        string originalDllPath,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var runtimeDir = Path.GetDirectoryName(originalDllPath);
        if (runtimeDir == null)
            return false;

        var targetDllPath = Path.Combine(runtimeDir, targetAssemblyName + ".dll");
        if (!File.Exists(targetDllPath))
        {
            logger.Log($"Target assembly '{targetAssemblyName}' not found at '{targetDllPath}'.");
            return false;
        }

        logger.Log($"Following type forwarder to '{targetAssemblyName}'...");

        await EnrichTypeWithSourceInfoAsync(apiType, typeName, targetDllPath, options, logger, httpClient);
        return true;
    }

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
