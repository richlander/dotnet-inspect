using System.IO.Compression;
using DotnetInspector.Packages;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Shared extraction, enrichment, and utility methods used by API-related commands.
/// </summary>
internal static class ApiServices
{
    // ===== Extraction Pipeline =====

    /// <summary>
    /// Extracts a specific type from a package or assembly, with full path info.
    /// Used by TypeCommand and SamplesCommand.
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
                    var tfmAssembly = FindAssemblyByTfm(searchPath, options.Tfm);
                    if (tfmAssembly != null)
                        searchPath = tfmAssembly;
                }
                else
                {
                    var (highestPath, _) = SelectHighestTfmAssembly(GetPackageDlls(searchPath), searchPath);
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

            if (options.ShowHierarchy && surface != null && apiType != null)
            {
                ApiSurfaceExtractor.PopulateDerivedTypes(surface, apiType);
            }

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
    /// Extracts a specific type from a package or assembly. Used by TypeCommand.
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
                    var tfmAssembly = FindAssemblyByTfm(searchPath, options.Tfm);
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
                var dlls = GetPackageDlls(searchPath);
                if (dlls.Count > 1)
                {
                    var (selectedPath, tfm) = SelectHighestTfmAssembly(dlls, searchPath);
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
            try
            {
                using FileStream stream = File.OpenRead(dllFile);
                using PEReader peReader = new(stream);

                if (!peReader.HasMetadata)
                    continue;

                var api = ApiSurfaceExtractor.Extract(peReader, includeAll);

                var match = api.Types.FirstOrDefault(t =>
                {
                    var fullName = string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}";
                    return fullName.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                           t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase);
                });

                if (match != null)
                {
                    logger.Log($"Found in: {Path.GetFileName(dllFile)}");
                    return (match, Path.GetFileName(dllFile), dllFile, api);
                }
            }
            catch
            {
                // Skip unreadable files
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
                var (selectedPath, selectedTfm) = SelectHighestTfmAssembly(dlls, searchPath);
                dllFile = selectedPath;
                if (selectedTfm != null)
                {
                    logger.Log($"Auto-selected TFM: {selectedTfm}");
                }
            }
            else
            {
                var dlls = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories).ToList();
                var (selectedPath, _) = SelectHighestTfmAssembly(dlls, searchPath);
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

        try
        {
            using FileStream stream = File.OpenRead(dllFile);
            using PEReader peReader = new(stream);

            if (!peReader.HasMetadata)
                return (null, null);

            return (ApiSurfaceExtractor.Extract(peReader, includeAll), dllFile);
        }
        catch
        {
            return (null, null);
        }
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
                using FileStream stream = File.OpenRead(targetPath);
                using PEReader peReader = new(stream);

                if (!peReader.HasMetadata)
                    continue;

                var targetApi = ApiSurfaceExtractor.Extract(peReader, includeAll);

                foreach (var type in targetApi.Types)
                {
                    var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
                    if (forwardedTypeNames.Contains(fullName))
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
            api.Types = api.Types.OrderBy(t => string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}").ToList();
            logger.Log($"Resolved {api.Types.Count} types from forwarded assemblies.");
        }
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
            using FileStream stream = File.OpenRead(dllPath);
            using PEReader peReader = new(stream);

            if (!peReader.HasMetadata)
            {
                logger.Log("No metadata in assembly, cannot resolve source.");
                return;
            }

            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = ParsePackageReference(options.PackagePath);

                if (packageVersion == null && !string.IsNullOrEmpty(packageName))
                {
                    packageVersion = ExtractVersionFromPath(dllPath, packageName);
                }
            }

            var symbolDownloader = new SymbolPackageDownloader(httpClient);
            var pdbResult = await symbolDownloader.GetPdbReaderAsync(
                peReader, dllPath, packageName, packageVersion, logger.Log,
                isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly));

            if (pdbResult.Reader == null || pdbResult.Provider == null)
            {
                if (!string.IsNullOrEmpty(options.PlatformAssembly) && options.ShowDocs)
                {
                    logger.Log("No PDB available, falling back to XML documentation from packs directory");
                    await EnrichFromXmlDocFileAsync(apiType, typeName, options, logger);
                    return;
                }

                Console.Error.WriteLine();
                if (pdbResult.WindowsPdbDetected)
                {
                    Console.Error.WriteLine("Warning: PDB could not be read (Windows PDB format is not supported).");
                    Console.Error.WriteLine("         Only Portable PDBs are supported. Consider asking the maintainer");
                    Console.Error.WriteLine("         to publish Portable PDBs (embedded or in .snupkg).");
                }
                else
                {
                    Console.Error.WriteLine("Warning: No readable PDB found.");
                }
                Console.Error.WriteLine("         Run 'assembly --audit' for more details.");
                Console.Error.WriteLine();
                return;
            }

            var pdbReader = pdbResult.Reader;
            var pdbProvider = pdbResult.Provider;

            using var _ = pdbProvider;
            var metadataReader = peReader.GetMetadataReader();

            var resolver = SourceLinkResolver.Create(pdbReader);
            if (resolver == null)
            {
                logger.Log("No SourceLink information found in PDB.");
                return;
            }

            TypeDefinitionHandle? typeHandle = FindTypeDefinitionHandle(metadataReader, typeName);
            if (typeHandle == null)
            {
                var forwardTarget = FindTypeForwarderTarget(metadataReader, typeName);
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

            var sourceInfo = resolver.ResolveTypeSource(metadataReader, pdbReader, typeHandle.Value);
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
            (packageName, packageVersion) = ParsePackageReference(options.PackagePath);
            if (packageVersion == null && !string.IsNullOrEmpty(packageName))
            {
                packageVersion = ExtractVersionFromPath(dllPath, packageName);
            }
        }

        using FileStream stream = File.OpenRead(dllPath);
        using PEReader peReader = new(stream);

        if (!peReader.HasMetadata)
        {
            logger.Log("No metadata in assembly, cannot resolve source.");
            return;
        }

        var symbolDownloader = new SymbolPackageDownloader(httpClient);
        var pdbResult = await symbolDownloader.GetPdbReaderAsync(
            peReader, dllPath, packageName, packageVersion, logger.Log,
            isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly));

        if (pdbResult.Reader == null || pdbResult.Provider == null)
        {
            if (pdbResult.WindowsPdbDetected)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Warning: PDB could not be read (Windows PDB format is not supported).");
                Console.Error.WriteLine("         Only Portable PDBs are supported.");
                Console.Error.WriteLine();
            }
            return;
        }

        using var _ = pdbResult.Provider;
        var pdbReader = pdbResult.Reader;
        var metadataReader = peReader.GetMetadataReader();

        var resolver = SourceLinkResolver.Create(pdbReader);
        if (resolver == null)
        {
            logger.Log("No SourceLink information found in PDB.");
            return;
        }

        var typeSourceInfo = new List<(ApiType Type, string TypeName, SourceLinkResolver.TypeSourceInfo? SourceInfo)>();
        var allUrlsToFetch = new HashSet<string>();

        foreach (var apiType in types)
        {
            var typeName = string.IsNullOrEmpty(apiType.Namespace) ? apiType.Name : $"{apiType.Namespace}.{apiType.Name}";
            TypeDefinitionHandle? typeHandle = FindTypeDefinitionHandle(metadataReader, typeName);

            if (typeHandle == null)
                continue;

            var sourceInfo = resolver.ResolveTypeSource(metadataReader, pdbReader, typeHandle.Value);
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

        var fullTypeName = string.IsNullOrEmpty(apiType.Namespace)
            ? apiType.Name
            : $"{apiType.Namespace}.{apiType.Name}";

        var typeDoc = xmlParser.GetTypeDocumentation(fullTypeName);
        if (typeDoc != null)
        {
            apiType.Documentation = new DocComment
            {
                Summary = typeDoc.Summary,
                Remarks = typeDoc.Remarks
            };
            logger.Log($"Found type documentation for {fullTypeName}");
        }

        if (options.ShowDocs && apiType.Members != null)
        {
            foreach (var member in apiType.Members)
            {
                var memberDoc = xmlParser.GetMemberDocumentation(fullTypeName, member.Name, member.Kind);
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
                        ResolvedUrl = ResolveSampleUrl(url, s.RelativePath)
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
                                    ResolvedUrl = ResolveSampleUrl(url, s.RelativePath)
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
            using FileStream stream = File.OpenRead(dllPath);
            using PEReader peReader = new(stream);

            if (!peReader.HasMetadata)
                return null;

            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = ParsePackageReference(options.PackagePath);
                if (packageVersion == null && !string.IsNullOrEmpty(packageName))
                {
                    packageVersion = ExtractVersionFromPath(dllPath, packageName);
                }
            }

            var symbolDownloader = new SymbolPackageDownloader(httpClient);
            var pdbResult = await symbolDownloader.GetPdbReaderAsync(
                peReader, dllPath, packageName, packageVersion, logger.Log,
                isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly));

            if (pdbResult.Reader == null || pdbResult.Provider == null)
                return null;

            using var _ = pdbResult.Provider;

            string? sourceLinkJson = null;
            foreach (var handle in pdbResult.Reader.CustomDebugInformation)
            {
                var info = pdbResult.Reader.GetCustomDebugInformation(handle);
                var guid = pdbResult.Reader.GetGuid(info.Kind);
                if (guid == new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A"))
                {
                    var bytes = pdbResult.Reader.GetBlobBytes(info.Value);
                    sourceLinkJson = System.Text.Encoding.UTF8.GetString(bytes);
                    break;
                }
            }

            if (sourceLinkJson == null)
                return null;

            using var doc = JsonDocument.Parse(sourceLinkJson);
            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var prop in documents.EnumerateObject())
                {
                    string url = prop.Value.GetString() ?? "";
                    if (url.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = Regex.Match(url,
                            @"https://raw\.githubusercontent\.com/([^/]+)/([^/]+)/");
                        if (match.Success)
                        {
                            return $"https://github.com/{match.Groups[1].Value}/{match.Groups[2].Value}";
                        }
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Error extracting repository URL: {ex.Message}");
        }
        return null;
    }

    // ===== Private Enrichment Helpers =====

    private static TypeDefinitionHandle? FindTypeDefinitionHandle(MetadataReader reader, string typeName)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            string name = reader.GetString(typeDef.Name);
            string ns = reader.GetString(typeDef.Namespace);
            string fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

            if (fullName.Equals(typeName, StringComparison.OrdinalIgnoreCase) ||
                name.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                return typeHandle;
            }
        }
        return null;
    }

    private static string? FindTypeForwarderTarget(MetadataReader reader, string typeName)
    {
        foreach (var exportedTypeHandle in reader.ExportedTypes)
        {
            var exportedType = reader.GetExportedType(exportedTypeHandle);
            if (!exportedType.IsForwarder)
                continue;

            var name = reader.GetString(exportedType.Name);
            var ns = reader.GetString(exportedType.Namespace);
            var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

            if (fullName.Equals(typeName, StringComparison.OrdinalIgnoreCase))
            {
                if (exportedType.Implementation.Kind == HandleKind.AssemblyReference)
                {
                    var assemblyRef = reader.GetAssemblyReference((AssemblyReferenceHandle)exportedType.Implementation);
                    return reader.GetString(assemblyRef.Name);
                }
            }
        }
        return null;
    }

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

    // ===== Package/Assembly Utilities =====

    internal static List<string> GetPackageDlls(string extractPath)
    {
        var toolsDir = Path.Combine(extractPath, "tools");
        var libDir = Path.Combine(extractPath, "lib");

        string[] candidates;
        if (Directory.Exists(toolsDir))
        {
            candidates = Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories);
        }
        else if (Directory.Exists(libDir))
        {
            candidates = Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories);
        }
        else
        {
            candidates = Directory.GetFiles(extractPath, "*.dll", SearchOption.AllDirectories);
        }

        return candidates.OrderBy(f => f).ToList();
    }

    internal static (string? path, string? tfm) SelectHighestTfmAssembly(List<string> dlls, string extractPath)
    {
        dlls = dlls.Where(d => !d.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)).ToList();

        var byTfm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var dll in dlls)
        {
            var relativePath = Path.GetRelativePath(extractPath, dll).Replace('\\', '/');
            var tfm = ExtractTfmFromPath(relativePath);
            if (tfm != null)
            {
                if (!byTfm.TryGetValue(tfm, out var list))
                {
                    list = [];
                    byTfm[tfm] = list;
                }
                list.Add(dll);
            }
        }

        if (byTfm.Count == 0)
            return (null, null);

        var sortedTfms = byTfm.Keys
            .Select(tfm => (tfm, priority: GetTfmPriority(tfm)))
            .OrderByDescending(x => x.priority)
            .ToList();

        var highestTfm = sortedTfms[0].tfm;
        var assemblies = byTfm[highestTfm];

        var directDll = assemblies.FirstOrDefault(d =>
        {
            var relativePath = Path.GetRelativePath(extractPath, d).Replace('\\', '/');
            var parts = relativePath.Split('/');
            return parts.Length <= 3;
        });

        return (directDll ?? assemblies[0], highestTfm);
    }

    internal static string? FindAssemblyByTfm(string extractPath, string tfm)
    {
        var libDir = Path.Combine(extractPath, "lib");
        var toolsDir = Path.Combine(extractPath, "tools");

        if (Directory.Exists(libDir))
        {
            var tfmDir = Path.Combine(libDir, tfm);
            if (Directory.Exists(tfmDir))
            {
                var dlls = Directory.GetFiles(tfmDir, "*.dll");
                if (dlls.Length > 0)
                    return dlls[0];
            }
        }

        if (Directory.Exists(toolsDir))
        {
            var dlls = Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories)
                .Where(f => f.Replace('\\', '/').Contains($"/{tfm}/", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dlls.Count > 0)
                return dlls[0];
        }

        return null;
    }

    internal static string? ExtractTfmFromPath(string relativePath)
    {
        var parts = relativePath.Split('/');
        foreach (var part in parts)
        {
            if (IsTfmFolder(part))
                return part;
        }
        return null;
    }

    private static bool IsTfmFolder(string folderName)
    {
        return folderName.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
               (folderName.Contains('.') || char.IsDigit(folderName[3]));
    }

    internal static int GetTfmPriority(string tfm)
    {
        var lower = tfm.ToLowerInvariant();

        if (lower.StartsWith("net") && !lower.StartsWith("netstandard") && !lower.StartsWith("netcoreapp") && !lower.StartsWith("netframework"))
        {
            var versionPart = lower[3..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 10000 + (version.Major * 100) + version.Minor;
            }
            if (int.TryParse(versionPart.Replace(".", ""), out var legacyVersion))
            {
                return 1000 + legacyVersion;
            }
        }

        if (lower.StartsWith("netcoreapp"))
        {
            var versionPart = lower[10..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 5000 + (version.Major * 100) + version.Minor;
            }
        }

        if (lower.StartsWith("netstandard"))
        {
            var versionPart = lower[11..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 3000 + (version.Major * 100) + version.Minor;
            }
        }

        return 0;
    }

    internal static (string? name, string? version) ParsePackageReference(string packageSource)
    {
        if (packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileNameWithoutExtension(packageSource);
            return ParsePackageFileName(fileName);
        }

        int atIndex = packageSource.IndexOf('@');
        if (atIndex > 0)
        {
            return (packageSource[..atIndex], packageSource[(atIndex + 1)..]);
        }

        return (packageSource, null);
    }

    private static (string? name, string? version) ParsePackageFileName(string fileName)
    {
        var parts = fileName.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0 && char.IsDigit(parts[i][0]))
            {
                var name = string.Join(".", parts.Take(i));
                var version = string.Join(".", parts.Skip(i));
                return (name, version);
            }
        }
        return (fileName, null);
    }

    private static string? ExtractVersionFromPath(string dllPath, string packageName)
    {
        var normalizedPath = dllPath.Replace('\\', '/');
        var normalizedPackageName = packageName.ToLowerInvariant();

        var searchPattern = $"/{normalizedPackageName}/";
        var index = normalizedPath.ToLowerInvariant().IndexOf(searchPattern, StringComparison.Ordinal);
        if (index < 0)
            return null;

        var afterPackage = normalizedPath[(index + searchPattern.Length)..];
        var nextSlash = afterPackage.IndexOf('/');
        if (nextSlash > 0)
        {
            var possibleVersion = afterPackage[..nextSlash];
            if (possibleVersion.Length > 0 && char.IsDigit(possibleVersion[0]))
            {
                return possibleVersion;
            }
        }

        return null;
    }

    // ===== Generic Type Name Conversion =====

    /// <summary>
    /// Converts C#-style generic type names to CLR backtick notation.
    /// e.g., "Dictionary&lt;TKey,TValue&gt;" → "Dictionary`2"
    /// </summary>
    internal static string ConvertGenericTypeName(string typeName)
    {
        int angleBracketStart = typeName.IndexOf('<');
        if (angleBracketStart < 0)
            return typeName;

        int angleBracketEnd = typeName.LastIndexOf('>');
        if (angleBracketEnd < angleBracketStart)
            return typeName;

        string baseName = typeName[..angleBracketStart];

        string typeParamSection = typeName[(angleBracketStart + 1)..angleBracketEnd];
        int arity = CountTypeParameters(typeParamSection);

        return $"{baseName}`{arity}";
    }

    private static int CountTypeParameters(string typeParams)
    {
        if (string.IsNullOrWhiteSpace(typeParams))
            return 0;

        int count = 1;
        int depth = 0;

        foreach (char c in typeParams)
        {
            if (c == '<')
                depth++;
            else if (c == '>')
                depth--;
            else if (c == ',' && depth == 0)
                count++;
        }

        return count;
    }

    // ===== URL Utilities =====

    /// <summary>
    /// Resolves a relative sample path to a full URL based on the source file URL.
    /// </summary>
    internal static string? ResolveSampleUrl(string sourceUrl, string relativePath)
    {
        try
        {
            var uri = new Uri(sourceUrl);

            var pathSegments = uri.AbsolutePath.Split('/').ToList();

            if (pathSegments.Count > 0)
                pathSegments.RemoveAt(pathSegments.Count - 1);

            var relativeSegments = relativePath.Split('/');
            int i = 0;

            while (i < relativeSegments.Length && relativeSegments[i] == "..")
            {
                if (pathSegments.Count > 0)
                    pathSegments.RemoveAt(pathSegments.Count - 1);
                i++;
            }

            if (i < relativeSegments.Length)
            {
                var firstSegment = relativeSegments[i];
                int metadataEnd = Math.Min(4, pathSegments.Count);
                for (int j = metadataEnd; j < pathSegments.Count; j++)
                {
                    if (pathSegments[j] == firstSegment)
                    {
                        pathSegments = pathSegments.Take(j).ToList();
                        break;
                    }
                }
            }

            while (i < relativeSegments.Length)
            {
                var segment = relativeSegments[i];

                if (segment == "." || string.IsNullOrEmpty(segment))
                {
                    i++;
                    continue;
                }

                pathSegments.Add(segment);
                i++;
            }

            var newPath = string.Join("/", pathSegments);
            var resolvedUri = new UriBuilder(uri.Scheme, uri.Host)
            {
                Path = newPath
            };

            return resolvedUri.Uri.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal static string ConvertRawToBlobUrl(string url)
    {
        return url.Replace("/raw/", "/blob/");
    }

    internal static string? ConvertToGitHubRawUrl(string rawUrl)
    {
        if (rawUrl.StartsWith("https://raw.githubusercontent.com/"))
        {
            return rawUrl
                .Replace("https://raw.githubusercontent.com/", "https://github.com/")
                .Replace($"/{GetCommitFromUrl(rawUrl)}/", $"/raw/{GetCommitFromUrl(rawUrl)}/");
        }
        return rawUrl;
    }

    private static string? GetCommitFromUrl(string url)
    {
        var match = Regex.Match(url, @"githubusercontent\.com/[^/]+/[^/]+/([^/]+)/");
        return match.Success ? match.Groups[1].Value : null;
    }
}
