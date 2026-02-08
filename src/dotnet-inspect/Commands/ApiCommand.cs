using System.IO.Compression;
using DotnetInspector.Packages;
using Markout;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays the public API shape of a specific type.
/// </summary>
public class ApiCommand
{
    public static async Task<int> ExecuteAsync(string? typeName, ApiOptions options)
    {
        if (options.MemberFilter?.Count > 0 && string.IsNullOrEmpty(typeName))
        {
            Console.Error.WriteLine("Error: --member requires a type argument.");
            Console.Error.WriteLine("Usage: dotnet-inspect api <type> --package <pkg> --member <name>");
            return 1;
        }

        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        string? tempDir = null;

        try
        {
            string searchPath;
            string? runtimeAssemblyPath = null;  // For platform assemblies with --docs, use runtime path for PDB lookup
            string? packageName = null;
            string? packageVersion = null;

            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                // Extract from package
                var extracted = await PackageExtractor.ExtractPackageAsync(context.HttpClient, options.PackagePath, context.Logger.Log, "inspect-api", options.SourceOptions);
                if (extracted == null)
                {
                    return 1;
                }
                (searchPath, tempDir, packageName, packageVersion) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);

                // If --tfm is specified, find assembly by TFM
                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = FindAssemblyByTfm(searchPath, options.Tfm);
                    if (tfmAssembly == null)
                    {
                        Console.Error.WriteLine($"Error: No assembly found for TFM '{options.Tfm}'.");
                        Console.Error.WriteLine("Available TFMs:");
                        var dlls = GetPackageDlls(searchPath);
                        var tfms = dlls
                            .Select(d => ExtractTfmFromPath(Path.GetRelativePath(searchPath, d).Replace('\\', '/')))
                            .Where(t => t != null)
                            .Distinct()
                            .OrderByDescending(t => GetTfmPriority(t!));
                        foreach (var tfm in tfms)
                        {
                            Console.Error.WriteLine($"  {tfm}");
                        }
                        return 1;
                    }
                    searchPath = tfmAssembly;
                    logger.Log($"Using TFM: {options.Tfm}");
                }
                // If --assembly is also specified, use it to select a specific DLL within the package
                else if (!string.IsNullOrEmpty(options.AssemblyPath))
                {
                    var targetPath = Path.Combine(searchPath, options.AssemblyPath.Replace('\\', '/'));
                    if (!File.Exists(targetPath))
                    {
                        Console.Error.WriteLine($"Error: Assembly '{options.AssemblyPath}' not found in package.");
                        Console.Error.WriteLine("Available assemblies:");
                        foreach (var dll in GetPackageDlls(searchPath))
                        {
                            Console.Error.WriteLine($"  {Path.GetRelativePath(searchPath, dll)}");
                        }
                        return 1;
                    }
                    searchPath = targetPath;
                }
            }
            else if (!string.IsNullOrEmpty(options.AssemblyPath))
            {
                // Use local assembly
                if (!File.Exists(options.AssemblyPath))
                {
                    Console.Error.WriteLine($"Error: File not found: {options.AssemblyPath}");
                    return 1;
                }
                searchPath = options.AssemblyPath;
            }
            else if (!string.IsNullOrEmpty(options.PlatformAssembly))
            {
                // Use platform assembly
                // Always use ref assemblies for API extraction (they have the public types)
                var (assemblyPath, framework, version, error) = PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly, 
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: false);
                
                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return 1;
                }
                
                searchPath = assemblyPath!;
                logger.Log($"Using platform ref assembly: {framework} {version}");
                
                // Always resolve runtime assembly for PDB lookup (ref assemblies have no debug info)
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
                Console.Error.WriteLine("Error: Must specify --package, --assembly, or --platform.");
                Console.Error.WriteLine("Run 'dotnet-inspect api --help' for usage.");
                return 1;
            }

            string? selectedTfm = null;
            if (string.IsNullOrEmpty(typeName))
            {
                // No type specified - check if we need to auto-select TFM
                if (Directory.Exists(searchPath))
                {
                    var dlls = GetPackageDlls(searchPath);
                    if (dlls.Count > 1)
                    {
                        // Auto-select highest TFM
                        var (selectedPath, tfm) = SelectHighestTfmAssembly(dlls, searchPath);
                        if (selectedPath != null)
                        {
                            searchPath = selectedPath;
                            selectedTfm = tfm;
                            logger.Log($"Auto-selected TFM: {tfm}");
                        }
                        else
                        {
                            // No TFM pattern found - require explicit selection
                            Console.Error.WriteLine("Error: Multiple assemblies found. Please specify one with --assembly or --tfm:");
                            foreach (var dll in dlls)
                            {
                                Console.Error.WriteLine($"  {Path.GetRelativePath(searchPath, dll)}");
                            }
                            Console.Error.WriteLine();
                            Console.Error.WriteLine("Usage: dotnet-inspect api [type] --package <pkg> --assembly <path>");
                            return 1;
                        }
                    }
                }

                // List all types in the assembly
                var (api, apiDllPath) = ExtractFullApi(searchPath, logger, options.IncludeAll);
                if (api == null)
                {
                    Console.Error.WriteLine("Error: Could not extract API from assembly.");
                    return 1;
                }

                // If this is a type-forwarding assembly, resolve types from target assemblies
                if (api.Types.Count == 0 && api.TypeForwarders.Count > 0 && apiDllPath != null)
                {
                    ResolveForwardedTypes(api, apiDllPath, logger, options.IncludeAll);
                }

                // Set package/assembly name
                if (!string.IsNullOrEmpty(options.PackagePath))
                {
                    var (pkgName, _) = ParsePackageReference(options.PackagePath);
                    api.Name = pkgName;
                }
                else if (apiDllPath != null)
                {
                    api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
                }

                // Extract SourceLink repository URL (use runtime assembly if available for better SourceLink data)
                var pdbLookupPath = runtimeAssemblyPath ?? apiDllPath;
                if (pdbLookupPath != null)
                {
                    api.RepositoryUrl = await ExtractRepositoryUrlAsync(pdbLookupPath, options, logger, context.HttpClient);
                }
                api.Tfm = selectedTfm;

                // Enrich types with source info if docs, samples, or sourcelink-only requested
                if ((options.ShowDocs || options.ShowSamples || options.SourceLinkOnly) && pdbLookupPath != null)
                {
                    logger.Log("Enriching types with source info...");
                    foreach (var type in api.Types)
                    {
                        var fullTypeName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
                        await EnrichTypeWithSourceInfoAsync(type, fullTypeName, pdbLookupPath, options, logger, context.HttpClient);
                    }
                }

                WriteFullApiOutput(api, options, selectedTfm);
            }
            else
            {
                // Convert C#-style generic syntax to CLR backtick notation
                typeName = ConvertGenericTypeName(typeName);

                // Find specific type
                var (apiType, foundIn, dllPath, surface) = FindType(typeName, searchPath, logger, options.IncludeAll);
                if (apiType == null || dllPath == null)
                {
                    Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
                    return 1;
                }

                // Populate derived types if --hierarchy is enabled
                if (options.ShowHierarchy && surface != null)
                {
                    ApiSurfaceExtractor.PopulateDerivedTypes(surface, apiType);
                }

                // Enrich with source info (SourceLink URL, docs)
                // Use runtime assembly path for PDB lookup if available
                var pdbLookupPath = runtimeAssemblyPath ?? dllPath;
                await EnrichTypeWithSourceInfoAsync(apiType, typeName, pdbLookupPath, options, logger, context.HttpClient);

                WriteTypeOutput(apiType, foundIn, packageName, packageVersion, options);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
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
    /// Extracts a specific type from a package or assembly. Used by TypeCommand and SamplesCommand.
    /// </summary>
    internal static async Task<(ApiType? type, string? foundIn, string? dllPath)> ExtractTypeWithPathAsync(string typeName, ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        string? tempDir = null;
        string? runtimeAssemblyPath = null;  // For platform assemblies, use runtime path for PDB lookup
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
                // Resolve platform assembly (ref assembly for API extraction)
                var (assemblyPath, framework, version, error) = Inspectors.PlatformResolver.ResolveAssembly(
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
                
                // Always resolve runtime assembly for PDB lookup (ref assemblies have no debug info)
                var (runtimePath, _, _, runtimeError) = Inspectors.PlatformResolver.ResolveAssembly(
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
            
            // Populate derived types if --hierarchy is enabled
            if (options.ShowHierarchy && surface != null && apiType != null)
            {
                ApiSurfaceExtractor.PopulateDerivedTypes(surface, apiType);
            }

            // Enrich with source info and docs if requested
            // Use runtime assembly path for PDB lookup if available
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
    /// Used by SamplesCommand for assembly-wide sample collection.
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

            // Enrich types with source info if docs or samples requested
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

    /// <summary>
    /// Enriches all types with source info using batched parallel fetching.
    /// Phase 1: Resolve all source URLs (reusing PDB reader)
    /// Phase 2: Batch parallel fetch all source content
    /// Phase 3: Parse docs using pre-fetched content
    /// </summary>
    private static async Task EnrichTypesWithSourceInfoBatchedAsync(
        List<ApiType> types, 
        string dllPath, 
        ApiOptions options, 
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        // Parse package info once
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

        // Open PDB once for all types
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

        // Phase 1: Collect all source URLs across all types
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

        // Phase 2: Batch parallel fetch all source content
        var fetcher = new SourceFetcher(httpClient);
        var urlList = allUrlsToFetch.OrderBy(u => u, StringComparer.OrdinalIgnoreCase).ToList();
        var contentCache = new Dictionary<string, string?>();

        // Fetch all URLs in parallel (disk cache makes this fast)
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

        // Phase 3: Parse docs using pre-fetched content
        var parser = new DocCommentParser();
        foreach (var (apiType, typeName, sourceInfo) in typeSourceInfo)
        {
            if (sourceInfo == null)
                continue;

            // Set source info on the type
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

            // Parse docs if we have source content
            if ((options.ShowDocs || options.ShowSamples) && sourceInfo.SourceUrl != null)
            {
                var sourceContents = new List<(string Content, string Url, string FilePath)>();

                // Primary source file
                if (contentCache.TryGetValue(sourceInfo.SourceUrl, out var primaryContent) && primaryContent != null)
                {
                    sourceContents.Add((primaryContent, sourceInfo.SourceUrl, sourceInfo.SourceFilePath ?? ""));
                }

                // Additional source files for partial types
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

    internal static (ApiType? type, string? assembly, string? dllPath, ApiSurface? surface) FindType(string typeName, string searchPath, VerboseLogger logger, bool includeAll)
    {
        // Determine if searchPath is a file or directory
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

                // Search for the type by full name or simple name
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

    internal static async Task EnrichTypeWithSourceInfoAsync(ApiType apiType, string typeName, string dllPath, ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        // If --use-local-docs is specified for platform assemblies, use XML docs directly
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

            // Parse package name and version for symbol package download
            string? packageName = null;
            string? packageVersion = null;
            if (!string.IsNullOrEmpty(options.PackagePath))
            {
                (packageName, packageVersion) = ParsePackageReference(options.PackagePath);

                // If version wasn't specified, try to extract from the DLL path (cached packages have version in path)
                if (packageVersion == null && !string.IsNullOrEmpty(packageName))
                {
                    packageVersion = ExtractVersionFromPath(dllPath, packageName);
                }
            }

            // Try to get PDB reader (embedded, standalone, or from symbol package)
            var symbolDownloader = new SymbolPackageDownloader(httpClient);
            var pdbResult = await symbolDownloader.GetPdbReaderAsync(
                peReader, dllPath, packageName, packageVersion, logger.Log,
                isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly));

            if (pdbResult.Reader == null || pdbResult.Provider == null)
            {
                // For platform assemblies, fall back to XML docs from packs directory
                if (!string.IsNullOrEmpty(options.PlatformAssembly) && options.ShowDocs)
                {
                    logger.Log("No PDB available, falling back to XML documentation from packs directory");
                    await EnrichFromXmlDocFileAsync(apiType, typeName, options, logger);
                    return;
                }

                // Show warning about PDB issue
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

            // Create source link resolver
            var resolver = SourceLinkResolver.Create(pdbReader);
            if (resolver == null)
            {
                logger.Log("No SourceLink information found in PDB.");
                return;
            }

            // Find the TypeDefinitionHandle for the type
            TypeDefinitionHandle? typeHandle = FindTypeDefinitionHandle(metadataReader, typeName);
            if (typeHandle == null)
            {
                // Type not defined in this assembly - check if it's forwarded
                var forwardTarget = FindTypeForwarderTarget(metadataReader, typeName);
                if (forwardTarget != null)
                {
                    logger.Log($"Type '{typeName}' is forwarded to '{forwardTarget}'.");
                    
                    // Try to resolve the target assembly and get docs from there
                    var forwardedResult = await TryEnrichFromForwardedAssemblyAsync(
                        apiType, typeName, forwardTarget, dllPath, options, logger, httpClient);
                    if (forwardedResult)
                        return;
                }
                
                logger.Log($"Could not find type definition for '{typeName}'.");
                return;
            }

            // Resolve source info for the type
            var sourceInfo = resolver.ResolveTypeSource(metadataReader, pdbReader, typeHandle.Value);
            if (sourceInfo != null)
            {
                apiType.SourceFilePath = sourceInfo.SourceFilePath;
                apiType.SourceUrl = sourceInfo.SourceUrl;
                apiType.GitHubBrowseUrl = sourceInfo.GitHubBrowseUrl;
                apiType.SourceLineNumber = sourceInfo.LineNumber;
                apiType.SourceResolution = sourceInfo.ResolutionMethod.ToString();
                
                // Copy additional source files for partial types
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

            // Fetch docs/samples if requested
            if ((options.ShowDocs || options.ShowSamples) && sourceInfo?.SourceUrl != null)
            {
                var fetcher = new SourceFetcher(httpClient);
                var parser = new DocCommentParser();
                
                // Collect all source files to fetch (primary + additional for partial types)
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
                
                // Fetch and parse all source files
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
                        
                        // For the first file (primary), check if it's partial and get namespace
                        if (allSourceContents.Count == 0)
                        {
                            isPrimaryPartial = IsPartialTypeDeclaration(content, apiType.Name);
                            primaryNamespace = ExtractNamespace(content);
                            allSourceContents.Add((content, url, filePath));
                        }
                        else if (isPrimaryPartial)
                        {
                            // For additional files, validate they match the same partial type
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
                    // Parse and merge documentation from all source files
                    await MergePartialTypeDocumentation(apiType, allSourceContents, parser, options, logger);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Error enriching source info: {ex.Message}");
        }
    }

    /// <summary>
    /// Enriches a type with documentation from XML doc files in the packs directory.
    /// Used as fallback when SourceLink/PDB is not available for platform assemblies.
    /// </summary>
    private static async Task EnrichFromXmlDocFileAsync(ApiType apiType, string typeName, ApiOptions options, VerboseLogger logger)
    {
        await Task.CompletedTask; // Sync operation but keep async signature for consistency

        if (string.IsNullOrEmpty(options.PlatformAssembly))
        {
            logger.Log("XML doc fallback only available for platform assemblies");
            return;
        }

        // Resolve the XML doc file path from the packs directory
        var (refPath, version, error) = PlatformResolver.ResolveFramework(
            options.PlatformFramework ?? "runtime");

        if (error != null || refPath == null)
        {
            logger.Log($"Could not resolve framework for XML docs: {error}");
            return;
        }

        // XML doc file is next to the DLL in the ref directory
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

        // Get the full type name including namespace
        var fullTypeName = string.IsNullOrEmpty(apiType.Namespace) 
            ? apiType.Name 
            : $"{apiType.Namespace}.{apiType.Name}";

        // Get type documentation
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

        // Get member documentation if ShowDocs is enabled
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

        // Set source resolution to indicate XML docs were used
        apiType.SourceResolution = "XmlDoc";
    }
    
    /// <summary>
    /// Checks if the source content contains a partial type declaration for the given type name.
    /// </summary>
    private static bool IsPartialTypeDeclaration(string sourceContent, string typeName)
    {
        // Match patterns like "partial class JObject", "partial struct Foo", "partial interface IBar"
        var pattern = $@"\bpartial\s+(?:class|struct|interface|record)\s+{Regex.Escape(typeName)}\b";
        return Regex.IsMatch(sourceContent, pattern);
    }
    
    /// <summary>
    /// Extracts the namespace from source content.
    /// </summary>
    private static string? ExtractNamespace(string sourceContent)
    {
        // Match "namespace Foo.Bar" or "namespace Foo.Bar;" (file-scoped)
        var match = Regex.Match(sourceContent, @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline);
        return match.Success ? match.Groups[1].Value : null;
    }
    
    /// <summary>
    /// Merges documentation from multiple partial type source files.
    /// </summary>
    private static async Task MergePartialTypeDocumentation(
        ApiType apiType,
        List<(string Content, string Url, string FilePath)> sourceContents,
        DocCommentParser parser,
        ApiOptions options,
        VerboseLogger logger)
    {
        await Task.CompletedTask; // Make async for future enhancements
        
        DocComment? mergedTypeDoc = null;
        var allSamples = new List<SampleReference>();
        
        // Process each source file
        foreach (var (content, url, filePath) in sourceContents)
        {
            // Try to extract type documentation
            var typeDoc = parser.ExtractTypeDocComment(content, apiType.Name);
            if (typeDoc != null)
            {
                // First type doc found becomes the base, or merge if we already have one
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
                    // Merge: prefer existing values, but fill in blanks
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
                
                // Collect samples from this file
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
            
            // Parse member documentation
            if (apiType.Members != null)
            {
                var membersToDocument = options.MemberFilter?.Count > 0
                    ? apiType.Members.Where(m => options.MemberFilter.Contains(m.Name))
                    : apiType.Members;

                foreach (var member in membersToDocument)
                {
                    // Only set if not already documented (first file wins for each member)
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
        
        // Set the merged type documentation
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

            // Parse package name and version for symbol package download
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

            // Try to get PDB reader
            var symbolDownloader = new SymbolPackageDownloader(httpClient);
            var pdbResult = await symbolDownloader.GetPdbReaderAsync(
                peReader, dllPath, packageName, packageVersion, logger.Log,
                isPlatformAssembly: !string.IsNullOrEmpty(options.PlatformAssembly));

            if (pdbResult.Reader == null || pdbResult.Provider == null)
                return null;

            using var _ = pdbResult.Provider;

            // Extract SourceLink JSON
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

            // Parse to extract repository URL
            using var doc = JsonDocument.Parse(sourceLinkJson);
            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var prop in documents.EnumerateObject())
                {
                    string url = prop.Value.GetString() ?? "";
                    // SourceLink URLs use raw.githubusercontent.com, not github.com
                    if (url.Contains("githubusercontent.com", StringComparison.OrdinalIgnoreCase))
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(url,
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

    /// <summary>
    /// Finds the target assembly for a forwarded type.
    /// </summary>
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

    /// <summary>
    /// Tries to enrich a type with docs from a forwarded assembly.
    /// </summary>
    private static async Task<bool> TryEnrichFromForwardedAssemblyAsync(
        ApiType apiType, 
        string typeName, 
        string targetAssemblyName,
        string originalDllPath,
        ApiOptions options,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        // Resolve the target assembly in the same runtime directory
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

        // Recursively call the enrichment with the target assembly
        await EnrichTypeWithSourceInfoAsync(apiType, typeName, targetDllPath, options, logger, httpClient);
        return true;
    }

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
        // Filter out resource DLLs
        dlls = dlls.Where(d => !d.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase)).ToList();

        // Group DLLs by TFM extracted from path (e.g., lib/net8.0/Foo.dll -> net8.0)
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

        // Sort TFMs by version (highest first)
        var sortedTfms = byTfm.Keys
            .Select(tfm => (tfm, priority: GetTfmPriority(tfm)))
            .OrderByDescending(x => x.priority)
            .ToList();

        var highestTfm = sortedTfms[0].tfm;
        var assemblies = byTfm[highestTfm];

        // Prefer DLLs directly in the TFM folder (not in locale subdirectories)
        var directDll = assemblies.FirstOrDefault(d =>
        {
            var relativePath = Path.GetRelativePath(extractPath, d).Replace('\\', '/');
            var parts = relativePath.Split('/');
            // lib/net8.0/Foo.dll has 3 parts, lib/net8.0/cs/Foo.dll has 4
            return parts.Length <= 3;
        });

        return (directDll ?? assemblies[0], highestTfm);
    }

    private static string? ExtractTfmFromPath(string relativePath)
    {
        // Patterns: lib/net8.0/Foo.dll, tools/net8.0/any/Foo.dll
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
        // Common TFM patterns: net8.0, net9.0, net10.0, netstandard2.0, netcoreapp3.1, net472, etc.
        return folderName.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
               (folderName.Contains('.') || char.IsDigit(folderName[3]));
    }

    private static int GetTfmPriority(string tfm)
    {
        // Higher number = higher priority (newer/preferred)
        var lower = tfm.ToLowerInvariant();

        // .NET (net5.0+) - highest priority
        if (lower.StartsWith("net") && !lower.StartsWith("netstandard") && !lower.StartsWith("netcoreapp") && !lower.StartsWith("netframework"))
        {
            // Extract version: net8.0 -> 8.0, net10.0 -> 10.0
            var versionPart = lower[3..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 10000 + (version.Major * 100) + version.Minor;
            }
            // net472, net48, etc. (old .NET Framework)
            if (int.TryParse(versionPart.Replace(".", ""), out var legacyVersion))
            {
                return 1000 + legacyVersion;
            }
        }

        // .NET Core
        if (lower.StartsWith("netcoreapp"))
        {
            var versionPart = lower[10..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 5000 + (version.Major * 100) + version.Minor;
            }
        }

        // .NET Standard
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

    internal static string? FindAssemblyByTfm(string extractPath, string tfm)
    {
        var libDir = Path.Combine(extractPath, "lib");
        var toolsDir = Path.Combine(extractPath, "tools");

        // Try lib directory first
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

        // Try tools directory
        if (Directory.Exists(toolsDir))
        {
            // tools/net8.0/any/Foo.dll pattern
            var searchPattern = $"*{tfm}*";
            var dlls = Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories)
                .Where(f => f.Replace('\\', '/').Contains($"/{tfm}/", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (dlls.Count > 0)
                return dlls[0];
        }

        return null;
    }

    internal static (ApiSurface? api, string? dllPath) ExtractFullApi(string searchPath, VerboseLogger logger, bool includeAll)
    {
        // Determine if searchPath is a file or directory
        string? dllFile;
        if (File.Exists(searchPath) && searchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            dllFile = searchPath;
        }
        else if (Directory.Exists(searchPath))
        {
            // Find the first DLL in lib directory, preferring highest TFM
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
                // Fall back to first DLL found if no lib directory
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

        // Group forwarders by target assembly
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

                // Only include types that were forwarded from the original assembly
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
            // Sort types by full name
            api.Types = api.Types.OrderBy(t => string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}").ToList();
            logger.Log($"Resolved {api.Types.Count} types from forwarded assemblies.");
        }
    }

    private static string SerializeJson<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.Serialize(value, typeInfo);
    }

    private static void WriteFullApiOutput(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        // Apply type filter if specified
        if (!string.IsNullOrEmpty(options.TypeFilter))
        {
            api.Types = api.Types.Where(t =>
            {
                var fullName = string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Namespace}.{t.Name}";
                return MatchesGlobPattern(fullName, options.TypeFilter) ||
                       MatchesGlobPattern(t.Name, options.TypeFilter);
            }).ToList();
            api.PublicTypeCount = api.Types.Count;
        }
        
        // Apply sourcelink-only filter - only show types with sourcelink resolution
        if (options.SourceLinkOnly)
        {
            api.Types = api.Types.Where(t => !string.IsNullOrEmpty(t.SourceUrl)).ToList();
            api.PublicTypeCount = api.Types.Count;
            api.PublicMethodCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind is "method" or "constructor") ?? 0);
            api.PublicPropertyCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "property") ?? 0);
            api.PublicFieldCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "field") ?? 0);
            api.PublicEventCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "event") ?? 0);
        }

        // Apply unsafe filter - filter members and only show types with unsafe members
        if (options.UnsafeOnly)
        {
            foreach (var type in api.Types)
            {
                if (type.Members != null)
                {
                    type.Members = type.Members.Where(m => m.IsUnsafe).ToList();
                }
            }
            api.Types = api.Types.Where(t => t.Members?.Count > 0).ToList();
            api.PublicTypeCount = api.Types.Count;
            api.PublicMethodCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind is "method" or "constructor") ?? 0);
            api.PublicPropertyCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "property") ?? 0);
            api.PublicFieldCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "field") ?? 0);
            api.PublicEventCount = api.Types.Sum(t => t.Members?.Count(m => m.Kind == "event") ?? 0);
        }

        if (options.JsonOutput)
        {
            Console.WriteLine(SerializeJson(api, ApiJsonContext.Default.ApiSurface));
        }
        else
        {
            Console.WriteLine(RenderFullApiMarkdown(api, options, selectedTfm));
        }
    }

    private static string RenderFullApiMarkdown(ApiSurface api, ApiOptions options, string? selectedTfm = null)
    {
        var writer = new MarkoutWriter();

        // H1 header
        if (!string.IsNullOrEmpty(api.Name))
        {
            writer.WriteHeading(1, api.Name);
        }

        var types = api.Types.AsEnumerable();
        var totalCount = api.Types.Count;

        // Summary fields
        writer.WriteField("Types", totalCount);
        writer.WriteField("Methods", api.PublicMethodCount);
        writer.WriteField("Properties", api.PublicPropertyCount);
        if (selectedTfm != null || api.Tfm != null)
        {
            writer.WriteField("TFM", selectedTfm ?? api.Tfm);
        }
        if (!string.IsNullOrEmpty(api.RepositoryUrl))
        {
            writer.WriteField("Repository", api.RepositoryUrl);
        }

        // In fields-only mode, stop here
        if (options.FieldsOnly)
        {
            return writer.ToString().TrimEnd();
        }

        // If no types and couldn't resolve forwarders, show a note
        if (totalCount == 0)
        {
            writer.WriteParagraph("This assembly contains no public types.");

            if (api.TypeForwarders.Count > 0)
            {
                writer.WriteParagraph("Type forwarders could not be resolved. Target assemblies:");

                // Group forwarders by target assembly
                var byAssembly = api.TypeForwarders
                    .GroupBy(f => f.TargetAssembly)
                    .OrderBy(g => g.Key)
                    .ToList();

                var headers = new[] { "Target Assembly", "Types" };
                var rows = byAssembly.Select(g => new[] { g.Key, g.Count().ToString() });
                writer.WriteTable(headers, rows);
            }

            return writer.ToString().TrimEnd();
        }

        // Show note for type-forwarding assemblies
        if (api.IsTypeForwardingAssembly)
        {
            writer.WriteParagraph("*This is a type-forwarding assembly. Types shown are resolved from target assemblies.*");
        }

        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            types = types.Take(options.Limit.Value);
        }

        if (options.ShowDocs)
        {
            var headers = new[] { "Type", "Kind", "Members", "Description" };
            var rows = types.Select(type =>
            {
                var memberCount = type.Members?.Count ?? 0;
                var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
                var summary = type.Documentation?.Summary ?? "";
                summary = summary.Replace("\n", " ").Replace("\r", "");
                if (summary.Length > 80)
                {
                    summary = summary[..77] + "...";
                }
                return new[] { fullName, type.Kind, memberCount.ToString(), summary };
            });
            writer.WriteTable(headers, rows);
        }
        else
        {
            var headers = new[] { "Type", "Kind", "Members" };
            var rows = types.Select(type =>
            {
                var memberCount = type.Members?.Count ?? 0;
                var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
                return new[] { fullName, type.Kind, memberCount.ToString() };
            });
            writer.WriteTable(headers, rows);
        }

        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            var remaining = totalCount - options.Limit.Value;
            writer.WriteParagraph($"*... and {remaining} more types*");
        }

        return writer.ToString().TrimEnd();
    }

    private static bool MatchesGlobPattern(string text, string pattern)
    {
        // Convert glob pattern to regex
        // * matches any characters, ? matches single character
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static void WriteTypeOutput(ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options)
    {
        // Check for member filter miss and warn
        if (options.MemberFilter?.Count > 0 && type.Members != null)
        {
            var matchingMembers = type.Members
                .Where(m => options.MemberFilter.Contains(m.Name))
                .ToList();

            if (matchingMembers.Count == 0)
            {
                Console.Error.WriteLine($"Warning: No members matched filter '{string.Join(", ", options.MemberFilter)}'");
            }
        }

        if (options.JsonOutput)
        {
            var outputType = type;
            var members = type.Members;

            // Apply member filter
            if (options.MemberFilter?.Count > 0 && members != null)
            {
                members = members
                    .Where(m => options.MemberFilter.Contains(m.Name))
                    .ToList();
            }

            // Apply unsafe filter
            if (options.UnsafeOnly && members != null)
            {
                members = members.Where(m => m.IsUnsafe).ToList();
            }

            // Apply -n limit to JSON output
            if (options.Limit.HasValue && members != null && members.Count > options.Limit.Value)
            {
                members = members.Take(options.Limit.Value).ToList();
            }

            if (members != type.Members)
            {
                outputType = new ApiType
                {
                    Namespace = type.Namespace,
                    Name = type.Name,
                    Kind = type.Kind,
                    IsSealed = type.IsSealed,
                    IsAbstract = type.IsAbstract,
                    IsStatic = type.IsStatic,
                    BaseType = type.BaseType,
                    Interfaces = type.Interfaces,
                    Members = members,
                    // Preserve source info
                    SourceFilePath = type.SourceFilePath,
                    SourceUrl = type.SourceUrl,
                    GitHubBrowseUrl = type.GitHubBrowseUrl,
                    SourceLineNumber = type.SourceLineNumber,
                    Documentation = type.Documentation
                };
            }

            if (options.CompactJson)
            {
                Console.WriteLine(SerializeJson(outputType, ApiTypeCompactJsonContext.Default.ApiType));
            }
            else
            {
                Console.WriteLine(SerializeJson(outputType, ApiTypeJsonContext.Default.ApiType));
            }
        }
        else
        {
            Console.WriteLine(RenderTypeMarkdown(type, foundIn, packageName, packageVersion, options));
        }
    }

    private static string RenderTypeMarkdown(ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options)
    {
        return options.Verbosity switch
        {
            Verbosity.Quiet => RenderTypeQuiet(type, foundIn, packageName, packageVersion, options),
            Verbosity.Minimal => RenderTypeMinimal(type, foundIn, packageName, packageVersion, options),
            _ => RenderTypeNormalOrDetailed(type, foundIn, packageName, packageVersion, options)
        };
    }

    private static void RenderTypeHeader(MarkoutWriter writer, ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options, int? memberCount = null)
    {
        var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";

        // H1 title with package info
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null ? $" ({packageName})" : "";
        writer.WriteHeading(1, $"{fullName}{packageInfo}");

        // Description paragraph (plain text, not blockquote)
        if (options.ShowDocs && type.Documentation?.Summary != null)
        {
            writer.WriteParagraph(type.Documentation.Summary);
        }

        // Key-value fields
        writer.WriteField("Kind", type.Kind);

        // Modifiers field (only if there are modifiers)
        var modifiers = new List<string>();
        if (type.IsStatic) modifiers.Add("static");
        if (type.IsAbstract && type.Kind == "class") modifiers.Add("abstract");
        if (type.IsSealed && type.Kind == "class") modifiers.Add("sealed");
        if (modifiers.Count > 0)
        {
            writer.WriteField("Modifiers", string.Join(", ", modifiers));
        }

        // Base type (if non-trivial)
        if (!string.IsNullOrEmpty(type.BaseType) && type.BaseType != "System.Object" && type.BaseType != "System.ValueType" && type.BaseType != "System.Enum")
        {
            writer.WriteField("Base", type.BaseType);
        }

        // Type parameters with constraints (compact inline format for q/m verbosity)
        // At n/d verbosity, a table section is shown instead
        if (type.TypeParameters is { Count: > 0 } &&
            (options.Verbosity == Verbosity.Quiet || options.Verbosity == Verbosity.Minimal))
        {
            var paramDescriptions = type.TypeParameters
                .Select(tp => tp.Constraints.Count > 0
                    ? $"{tp.DisplayName} : {tp.ConstraintsSummary}"
                    : tp.DisplayName);
            writer.WriteField("Type Parameters", string.Join(", ", paramDescriptions));
        }

        // Interfaces
        if (options.ShowInterfaces && type.Interfaces is { Count: > 0 })
        {
            writer.WriteField("Implements", string.Join(", ", type.Interfaces));
        }

        // Assembly
        if (foundIn != null)
        {
            writer.WriteField("Assembly", foundIn);
        }

        // Package fields
        if (packageName != null)
        {
            writer.WriteField("Package", packageName);
        }
        if (packageVersion != null)
        {
            writer.WriteField("Version", packageVersion);
        }

        // Source URL
        if (type.GitHubBrowseUrl != null)
        {
            var sourceUrl = options.BrowsableUrls ? ConvertRawToBlobUrl(type.GitHubBrowseUrl) : type.GitHubBrowseUrl;
            writer.WriteField("Source", sourceUrl);
            if (type.SourceResolution != null)
            {
                writer.WriteField("Source Resolution", type.SourceResolution);
            }

            // Show additional source files for partial types
            if (type.IsPartialType && type.AdditionalSourceFiles != null)
            {
                writer.WriteField("Partial Type", $"{type.AdditionalSourceFiles.Count + 1} source files");
            }
        }

        // Samples count
        if ((options.ShowDocs || options.ShowSamples) && type.Documentation?.Samples?.Count > 0)
        {
            writer.WriteField("Samples", $"{type.Documentation.Samples.Count} available");
        }
    }

    private static Dictionary<string, List<ApiMember>> GroupMembersByKind(ApiType type, HashSet<string>? memberFilter = null, bool unsafeOnly = false)
    {
        var members = type.Members?
            .Where(m => !IsCompilerGenerated(m.Name))
            .ToList() ?? [];

        if (memberFilter?.Count > 0)
        {
            members = members.Where(m => memberFilter.Contains(m.Name)).ToList();
        }

        if (unsafeOnly)
        {
            members = members.Where(m => m.IsUnsafe).ToList();
        }

        return members
            .GroupBy(m => m.Kind)
            .OrderBy(g => GetMemberSortOrder(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private static string PluralizeKind(string kind) => kind switch
    {
        "property" => "Properties",
        "method" => "Methods",
        "field" => "Fields",
        "event" => "Events",
        "constructor" => "Constructors",
        _ => char.ToUpper(kind[0]) + kind[1..] + "s"
    };

    private static string ExtractFirstParamType(string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return "";

        // Find the opening parenthesis for parameters
        var openParen = signature.IndexOf('(');
        if (openParen < 0) return "";

        var closeParen = signature.IndexOf(')', openParen);
        if (closeParen <= openParen + 1) return ""; // Empty params

        var paramsPart = signature.Substring(openParen + 1, closeParen - openParen - 1);
        if (string.IsNullOrWhiteSpace(paramsPart)) return "";

        // Get first parameter (before first comma)
        var firstParam = paramsPart.Split(',')[0].Trim();

        // Extract just the type name (last word, or handle generics)
        var parts = firstParam.Split(' ');
        var typePart = parts[0];

        // Simplify type name: remove namespace, keep just the simple name
        var dotIndex = typePart.LastIndexOf('.');
        if (dotIndex >= 0)
        {
            typePart = typePart[(dotIndex + 1)..];
        }

        // Handle generic types - take just the base name
        var genericIndex = typePart.IndexOf('<');
        if (genericIndex >= 0)
        {
            typePart = typePart[..genericIndex];
        }

        return typePart;
    }

    private static string RenderTypeQuiet(ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options)
    {
        var writer = new MarkoutWriter();
        RenderTypeHeader(writer, type, foundIn, packageName, packageVersion, options);

        // Skip member output in fields-only mode
        if (options.FieldsOnly) return writer.ToString().TrimEnd();

        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly);
        if (grouped.Count == 0) return writer.ToString().TrimEnd();

        // When filtering by member, show compact signatures instead of just names
        if (options.MemberFilter?.Count > 0)
        {
            foreach (var (kind, members) in grouped)
            {
                foreach (var member in members.OrderBy(m => m.Signature ?? m.Name))
                {
                    writer.WriteListItem($"`{member.Signature ?? member.Name}`");

                    // Show member documentation if available (when --docs is used with member filter)
                    if (options.ShowDocs && member.Documentation?.Summary != null)
                    {
                        writer.WriteParagraph($"  > {member.Documentation.Summary}");
                    }
                }
            }
        }
        else
        {
            foreach (var (kind, members) in grouped)
            {
                var names = members.Select(m => m.Name).Distinct().OrderBy(n => n);
                writer.WriteField(PluralizeKind(kind), string.Join(", ", names));
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderTypeMinimal(ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options)
    {
        var writer = new MarkoutWriter();
        var allMembers = type.Members?.Where(m => !IsCompilerGenerated(m.Name)).ToList() ?? [];
        RenderTypeHeader(writer, type, foundIn, packageName, packageVersion, options, allMembers.Count);

        // Skip member output in fields-only mode
        if (options.FieldsOnly) return writer.ToString().TrimEnd();

        // Render type hierarchy when --hierarchy is enabled
        if (options.ShowHierarchy)
        {
            RenderTypeHierarchy(writer, type);
        }

        var grouped = GroupMembersByKind(type, options.MemberFilter, options.UnsafeOnly);
        if (grouped.Count == 0) return writer.ToString().TrimEnd();

        // When using member filter with --docs, show detailed output with docs
        if (options.MemberFilter?.Count > 0 && options.ShowDocs)
        {
            foreach (var (kind, members) in grouped)
            {
                foreach (var member in members.OrderBy(m => m.Signature ?? m.Name))
                {
                    writer.WriteListItem($"`{member.Signature ?? member.Name}`");

                    if (member.Documentation?.Summary != null)
                    {
                        writer.WriteParagraph($"  > {member.Documentation.Summary}");
                    }
                }
            }
            return writer.ToString().TrimEnd();
        }

        foreach (var (kind, members) in grouped)
        {
            // Group by name to find overloads
            var byName = members.GroupBy(m => m.Name).OrderBy(g => g.Key).ToList();

            if (kind == "method" || kind == "constructor")
            {
                // Add section header for methods/constructors
                writer.WriteParagraph($"**{PluralizeKind(kind)}:**");
                foreach (var nameGroup in byName)
                {
                    var overloads = nameGroup.ToList();
                    if (overloads.Count > 1)
                    {
                        // Multiple overloads - show count and parameter hints
                        var paramHints = overloads
                            .Select(m => ExtractFirstParamType(m.Signature))
                            .Where(p => !string.IsNullOrEmpty(p))
                            .Distinct()
                            .Take(4)
                            .ToList();

                        var hintText = paramHints.Count > 0 ? $" ({string.Join(", ", paramHints)}, ...)" : "";
                        writer.WriteListItem($"**{nameGroup.Key}**: {overloads.Count} overloads{hintText}");
                    }
                    else
                    {
                        // Single method
                        writer.WriteListItem($"**{nameGroup.Key}**");
                    }
                }
            }
            else
            {
                // Properties, fields, events - just list names
                var names = byName.Select(g => g.Key);
                writer.WriteField(PluralizeKind(kind), string.Join(", ", names));
            }
        }

        return writer.ToString().TrimEnd();
    }

    private static string RenderTypeNormalOrDetailed(ApiType type, string? foundIn, string? packageName, string? packageVersion, ApiOptions options)
    {
        var writer = new MarkoutWriter();

        // In signatures-only mode, skip the header entirely
        if (!options.SignaturesOnly)
        {
            RenderTypeHeader(writer, type, foundIn, packageName, packageVersion, options);
        }

        // Skip member output in fields-only mode
        if (options.FieldsOnly) return writer.ToString().TrimEnd();

        // Render type parameters table for generic types
        if (!options.SignaturesOnly && type.TypeParameters is { Count: > 0 })
        {
            RenderTypeParametersTable(writer, type.TypeParameters);
        }

        // Render type hierarchy when --hierarchy is enabled
        if (!options.SignaturesOnly && options.ShowHierarchy)
        {
            RenderTypeHierarchy(writer, type);
        }

        if (type.Members is { Count: > 0 })
        {
            // Filter out compiler-generated members and sort by kind for readability
            var members = type.Members
                .Where(m => !IsCompilerGenerated(m.Name))
                .OrderBy(m => GetMemberSortOrder(m.Kind))
                .ThenBy(m => m.Name)
                .ToList();

            // Apply member filter if specified
            if (options.MemberFilter?.Count > 0)
            {
                members = members.Where(m => options.MemberFilter.Contains(m.Name)).ToList();
            }

            // Apply unsafe filter if specified
            if (options.UnsafeOnly)
            {
                members = members.Where(m => m.IsUnsafe).ToList();
            }

            // Use enhanced constructor output when --ctor is specified
            if (options.CtorOnly && members.Any(m => m.Kind == "constructor"))
            {
                RenderConstructorEmphasis(writer, type, members.Where(m => m.Kind == "constructor").ToList());
                return writer.ToString().TrimEnd();
            }

            // In signatures-only mode, output plain signatures (not markdown)
            if (options.SignaturesOnly)
            {
                var sb = new StringBuilder();
                var displayMembers = members.AsEnumerable();
                if (options.Limit.HasValue && options.Limit.Value < members.Count)
                {
                    displayMembers = displayMembers.Take(options.Limit.Value);
                }

                if (type.Kind == "enum")
                {
                    // Enum signatures: "Name = Value" format
                    var enumMembers = members
                        .Where(m => m.Kind == "field" && m.EnumValue.HasValue)
                        .OrderBy(m => m.EnumValue);
                    foreach (var member in options.Limit.HasValue && options.Limit.Value < members.Count
                        ? enumMembers.Take(options.Limit.Value)
                        : enumMembers)
                    {
                        sb.AppendLine($"{member.Name} = {member.EnumValue}");
                    }
                }
                else
                {
                    foreach (var member in displayMembers)
                    {
                        sb.AppendLine(member.Signature ?? member.ReturnType ?? "");
                    }
                }
                return sb.ToString().TrimEnd();
            }

            // Use specialized enum output for enum types
            if (type.Kind == "enum")
            {
                RenderEnumValues(writer, members, options);
                return writer.ToString().TrimEnd();
            }

            // Check if we should include a description column
            bool hasAnyDocs = options.ShowDocs && members.Any(m => m.Documentation?.Summary != null);

            writer.WriteHeading(2, "Members");

            var totalCount = members.Count;
            var displayMembersList = options.Limit.HasValue && options.Limit.Value < totalCount
                ? members.Take(options.Limit.Value).ToList()
                : members;

            if (hasAnyDocs)
            {
                var headers = new[] { "Member", "Kind", "Signature", "Description" };
                var rows = displayMembersList.Select(member =>
                {
                    string sig = member.Signature ?? member.ReturnType ?? "";
                    string desc = member.Documentation?.Summary ?? "";
                    return new[] { member.Name, member.Kind, $"`{sig}`", desc };
                });
                writer.WriteTable(headers, rows);
            }
            else
            {
                var headers = new[] { "Member", "Kind", "Signature" };
                var rows = displayMembersList.Select(member =>
                {
                    string sig = member.Signature ?? member.ReturnType ?? "";
                    return new[] { member.Name, member.Kind, $"`{sig}`" };
                });
                writer.WriteTable(headers, rows);
            }

            if (options.Limit.HasValue && options.Limit.Value < totalCount)
            {
                var remaining = totalCount - options.Limit.Value;
                writer.WriteParagraph($"*... and {remaining} more members*");
            }
        }

        return writer.ToString().TrimEnd();
    }

    /// <summary>
    /// Renders a table of generic type parameters with their constraints.
    /// </summary>
    private static void RenderTypeParametersTable(MarkoutWriter writer, List<TypeParameter> typeParameters)
    {
        writer.WriteHeading(2, "Type Parameters");

        var headers = new[] { "Parameter", "Constraints" };
        var rows = typeParameters.Select(param => new[] { param.DisplayName, param.ConstraintsSummary ?? "" });
        writer.WriteTable(headers, rows);
    }

    private static void RenderTypeHierarchy(MarkoutWriter writer, ApiType type)
    {
        var hasBase = !string.IsNullOrEmpty(type.BaseType) &&
                      type.BaseType != "System.Object" &&
                      type.BaseType != "System.ValueType" &&
                      type.BaseType != "System.Enum";
        var hasInterfaces = type.Interfaces is { Count: > 0 };
        var hasDerived = type.DerivedTypes is { Count: > 0 };

        if (!hasBase && !hasInterfaces && !hasDerived)
            return;

        writer.WriteHeading(2, "Type Hierarchy");

        var rows = new List<string[]>();

        if (hasBase)
        {
            rows.Add(new[] { "Base", type.BaseType! });
        }

        if (hasInterfaces)
        {
            foreach (var iface in type.Interfaces!)
            {
                rows.Add(new[] { "Implements", iface });
            }
        }

        if (hasDerived)
        {
            foreach (var derived in type.DerivedTypes!)
            {
                rows.Add(new[] { "Derived", derived });
            }
        }

        writer.WriteTable(new[] { "Relationship", "Type" }, rows);
    }

    /// <summary>
    /// Renders enum values with a specialized format: Name | Value | Description
    /// </summary>
    private static void RenderEnumValues(MarkoutWriter writer, List<ApiMember> members, ApiOptions options)
    {
        // Sort by enum value (numeric order)
        var enumMembers = members
            .Where(m => m.Kind == "field" && m.EnumValue.HasValue)
            .OrderBy(m => m.EnumValue)
            .ToList();

        if (enumMembers.Count == 0)
            return;

        bool hasAnyDocs = options.ShowDocs && enumMembers.Any(m => m.Documentation?.Summary != null);

        var totalCount = enumMembers.Count;
        var displayMembers = options.Limit.HasValue && options.Limit.Value < totalCount
            ? enumMembers.Take(options.Limit.Value).ToList()
            : enumMembers;

        writer.WriteHeading(2, "Values");

        if (hasAnyDocs)
        {
            var headers = new[] { "Name", "Value", "Description" };
            var rows = displayMembers.Select(member =>
            {
                string desc = member.Documentation?.Summary ?? "";
                return new[] { member.Name, member.EnumValue.ToString()!, desc };
            });
            writer.WriteTable(headers, rows);
        }
        else
        {
            var headers = new[] { "Name", "Value" };
            var rows = displayMembers.Select(member => new[] { member.Name, member.EnumValue.ToString()! });
            writer.WriteTable(headers, rows);
        }

        if (options.Limit.HasValue && options.Limit.Value < totalCount)
        {
            var remaining = totalCount - options.Limit.Value;
            writer.WriteParagraph($"*... and {remaining} more values*");
        }
    }

    private static bool IsCompilerGenerated(string name)
    {
        // Filter out compiler-generated and internal members
        return name.StartsWith('<') ||           // Lambda/iterator state machines
               name.StartsWith('_') ||           // Private backing fields (convention)
               name.Contains("__BackingField") || // Auto-property backing fields
               name == "value__";                // Enum underlying value field
    }

    private static int GetMemberSortOrder(string kind) => kind switch
    {
        "constructor" => 0,
        "field" => 1,
        "property" => 2,
        "method" => 3,
        "event" => 4,
        _ => 5
    };

    /// <summary>
    /// Renders enhanced constructor output for --ctor mode.
    /// Shows all overloads prominently with parameter details.
    /// </summary>
    private static void RenderConstructorEmphasis(MarkoutWriter writer, ApiType type, List<ApiMember> constructors)
    {
        writer.WriteHeading(2, $"Constructors ({constructors.Count} overload{(constructors.Count != 1 ? "s" : "")})");

        // Sort by parameter count for logical ordering
        var sorted = constructors
            .OrderBy(c => CountParameters(c.Signature))
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            var ctor = sorted[i];
            var paramCount = CountParameters(ctor.Signature);
            var paramInfo = ExtractParameterInfo(ctor.Signature);

            writer.WriteHeading(3, $"Overload {i + 1}: {paramCount} parameter{(paramCount != 1 ? "s" : "")}");

            writer.WriteCodeBlockStart("csharp");
            writer.WriteParagraph($"new {type.Name}{FormatConstructorCall(ctor.Signature)}");
            writer.WriteCodeBlockEnd();

            if (paramInfo.Count > 0)
            {
                var headers = new[] { "Parameter", "Type", "Notes" };
                var rows = paramInfo.Select(p => new[] { p.name, $"`{p.type}`", p.hasDefault ? "optional" : "required" });
                writer.WriteTable(headers, rows);
            }
        }
    }

    /// <summary>
    /// Counts parameters in a method signature.
    /// </summary>
    private static int CountParameters(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return 0;

        int parenStart = signature.IndexOf('(');
        int parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return 0;

        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return 0;

        // Count commas at depth 0 (handling nested generics)
        int count = 1;
        int depth = 0;
        foreach (char c in paramSection)
        {
            if (c == '<' || c == '(')
                depth++;
            else if (c == '>' || c == ')')
                depth--;
            else if (c == ',' && depth == 0)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Extracts parameter information from a method signature.
    /// Returns list of (name, type, hasDefaultValue).
    /// </summary>
    private static List<(string name, string type, bool hasDefault)> ExtractParameterInfo(string? signature)
    {
        var result = new List<(string, string, bool)>();
        if (string.IsNullOrEmpty(signature))
            return result;

        int parenStart = signature.IndexOf('(');
        int parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return result;

        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return result;

        // Split by comma at depth 0
        var params_ = new List<string>();
        int depth = 0;
        int lastSplit = 0;
        for (int i = 0; i < paramSection.Length; i++)
        {
            char c = paramSection[i];
            if (c == '<' || c == '(')
                depth++;
            else if (c == '>' || c == ')')
                depth--;
            else if (c == ',' && depth == 0)
            {
                params_.Add(paramSection[lastSplit..i].Trim());
                lastSplit = i + 1;
            }
        }
        params_.Add(paramSection[lastSplit..].Trim());

        foreach (var p in params_)
        {
            // Check for default value (contains '=')
            bool hasDefault = p.Contains('=');
            string clean = hasDefault ? p[..p.IndexOf('=')].Trim() : p;

            // Last word is parameter name, rest is type
            int lastSpace = clean.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                string type = clean[..lastSpace].Trim();
                string name = clean[(lastSpace + 1)..].Trim();
                result.Add((name, type, hasDefault));
            }
        }

        return result;
    }

    /// <summary>
    /// Formats the constructor signature for display as a call pattern.
    /// </summary>
    private static string FormatConstructorCall(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "()";

        int parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return "()";

        return signature[parenStart..];
    }

    /// <summary>
    /// Converts C#-style generic type names to CLR backtick notation.
    /// e.g., "Dictionary&lt;TKey,TValue&gt;" → "Dictionary`2", "Argument&lt;T&gt;" → "Argument`1"
    /// </summary>
    internal static string ConvertGenericTypeName(string typeName)
    {
        // Check if this looks like C#-style generic syntax
        int angleBracketStart = typeName.IndexOf('<');
        if (angleBracketStart < 0)
            return typeName;

        int angleBracketEnd = typeName.LastIndexOf('>');
        if (angleBracketEnd < angleBracketStart)
            return typeName; // Malformed, return as-is

        // Extract the base name
        string baseName = typeName[..angleBracketStart];

        // Count type parameters by counting commas + 1 at the top level
        string typeParamSection = typeName[(angleBracketStart + 1)..angleBracketEnd];
        int arity = CountTypeParameters(typeParamSection);

        return $"{baseName}`{arity}";
    }

    /// <summary>
    /// Counts type parameters, handling nested generics correctly.
    /// </summary>
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

    /// <summary>
    /// Parses a package reference string into name and version.
    /// Handles formats: "PackageName", "PackageName@1.0.0", "path/to/package.nupkg"
    /// </summary>
    internal static (string? name, string? version) ParsePackageReference(string packageSource)
    {
        // Local file - can't determine package name/version reliably
        if (packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            // Try to extract from filename: PackageName.1.0.0.nupkg
            var fileName = Path.GetFileNameWithoutExtension(packageSource);
            return ParsePackageFileName(fileName);
        }

        // Package name with optional version: "PackageName@1.0.0" or "PackageName"
        int atIndex = packageSource.IndexOf('@');
        if (atIndex > 0)
        {
            return (packageSource[..atIndex], packageSource[(atIndex + 1)..]);
        }

        // Just package name - version will need to be resolved
        return (packageSource, null);
    }

    /// <summary>
    /// Attempts to parse a package file name like "Microsoft.Extensions.Logging.8.0.0"
    /// </summary>
    private static (string? name, string? version) ParsePackageFileName(string fileName)
    {
        // Find the version part (first segment that starts with a digit)
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

    /// <summary>
    /// Extracts version from a cached package path.
    /// Path format: .../packages/packagename/version/lib/tfm/assembly.dll
    /// </summary>
    private static string? ExtractVersionFromPath(string dllPath, string packageName)
    {
        var normalizedPath = dllPath.Replace('\\', '/');
        var normalizedPackageName = packageName.ToLowerInvariant();

        // Look for pattern: /packagename/version/
        var searchPattern = $"/{normalizedPackageName}/";
        var index = normalizedPath.ToLowerInvariant().IndexOf(searchPattern, StringComparison.Ordinal);
        if (index < 0)
            return null;

        // Extract what comes after the package name
        var afterPackage = normalizedPath[(index + searchPattern.Length)..];
        var nextSlash = afterPackage.IndexOf('/');
        if (nextSlash > 0)
        {
            var possibleVersion = afterPackage[..nextSlash];
            // Verify it looks like a version (starts with digit)
            if (possibleVersion.Length > 0 && char.IsDigit(possibleVersion[0]))
            {
                return possibleVersion;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a relative sample path to a full URL based on the source file URL.
    /// Handles both simple relative paths and Sandcastle-style paths.
    /// </summary>
    internal static string? ResolveSampleUrl(string sourceUrl, string relativePath)
    {
        // sourceUrl format: https://raw.githubusercontent.com/user/repo/commit/path/to/file.cs
        // We need to resolve relativePath relative to the source file's directory
        
        try
        {
            var uri = new Uri(sourceUrl);
            
            // Get the path portion (e.g., /user/repo/commit/path/to/file.cs)
            var pathSegments = uri.AbsolutePath.Split('/').ToList();
            
            // Remove the filename to get the directory
            if (pathSegments.Count > 0)
                pathSegments.RemoveAt(pathSegments.Count - 1);
            
            // Process the relative path
            var relativeSegments = relativePath.Split('/');
            int i = 0;
            
            // First, handle all the ".." segments
            while (i < relativeSegments.Length && relativeSegments[i] == "..")
            {
                if (pathSegments.Count > 0)
                    pathSegments.RemoveAt(pathSegments.Count - 1);
                i++;
            }
            
            // Handle Sandcastle-style paths where the relative path may reference a parent directory
            // that we've already traversed to. For example:
            // - Source: /user/repo/commit/Src/Newtonsoft.Json/Serialization/File.cs
            // - After ../: /user/repo/commit/Src/Newtonsoft.Json
            // - Relative continues with: Src/Newtonsoft.Json.Tests/...
            // - "Src" is a parent we should navigate back to, not append
            if (i < relativeSegments.Length)
            {
                var firstSegment = relativeSegments[i];
                // Look for this segment in our current path (excluding the metadata prefix segments)
                // The first 4 segments are typically: "", "user", "repo", "commit"
                int metadataEnd = Math.Min(4, pathSegments.Count);
                for (int j = metadataEnd; j < pathSegments.Count; j++)
                {
                    if (pathSegments[j] == firstSegment)
                    {
                        // Found the segment in our path - truncate to that point
                        pathSegments = pathSegments.Take(j).ToList();
                        break;
                    }
                }
            }
            
            // Now add the remaining path segments
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
            
            // Reconstruct the URL
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

    /// <summary>
    /// Converts a raw GitHub URL to a github.com/raw URL.
    /// Uses /raw/ format which redirects (302) to raw content but is easy to convert to /blob/ for browsing.
    /// </summary>
    private static string? ConvertToGitHubRawUrl(string rawUrl)
    {
        // https://raw.githubusercontent.com/user/repo/commit/path
        // -> https://github.com/user/repo/raw/commit/path
        if (rawUrl.StartsWith("https://raw.githubusercontent.com/"))
        {
            return rawUrl
                .Replace("https://raw.githubusercontent.com/", "https://github.com/")
                .Replace($"/{GetCommitFromUrl(rawUrl)}/", $"/raw/{GetCommitFromUrl(rawUrl)}/");
        }
        return rawUrl;
    }

    /// <summary>
    /// Converts a github.com/raw URL to a github.com/blob URL for browser viewing.
    /// </summary>
    private static string ConvertRawToBlobUrl(string url)
    {
        // https://github.com/user/repo/raw/commit/path -> https://github.com/user/repo/blob/commit/path
        return url.Replace("/raw/", "/blob/");
    }

    private static string? GetCommitFromUrl(string url)
    {
        // Extract commit SHA from URL like https://raw.githubusercontent.com/user/repo/COMMIT/path
        var match = System.Text.RegularExpressions.Regex.Match(url, @"githubusercontent\.com/[^/]+/[^/]+/([^/]+)/");
        return match.Success ? match.Groups[1].Value : null;
    }

}

/// <summary>
/// Options for the api command.
/// </summary>
public record ApiOptions
{
    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? PlatformAssembly { get; init; }
    public string? PlatformFramework { get; init; }
    public string? Tfm { get; init; }
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Verbose { get; init; }
    public int? Limit { get; init; }
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;
    public HashSet<string>? MemberFilter { get; init; }
    public bool ShowDocs { get; init; }
    public bool UseLocalDocs { get; init; }
    public bool ShowSamples { get; init; }
    public bool SourceLinkOnly { get; init; }
    public bool BrowsableUrls { get; init; }
    public bool ShowInterfaces { get; init; }
    public bool ShowHierarchy { get; init; }
    public bool IncludeAll { get; init; }
    public string? TypeFilter { get; init; }
    public bool SignaturesOnly { get; init; }
    public bool UnsafeOnly { get; init; }
    public bool CtorOnly { get; init; }
    public bool FieldsOnly { get; init; }
    public HashSet<string>? IncludeSections { get; init; }
    public HashSet<string>? ExcludeSections { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// Returns true if the named section should be rendered.
    /// IncludeSections takes precedence: if set, only listed sections appear.
    /// Otherwise ExcludeSections hides listed sections.
    /// Matching is case-insensitive.
    /// </summary>
    public bool ShouldRenderSection(string name)
    {
        if (IncludeSections != null)
            return IncludeSections.Contains(name, StringComparer.OrdinalIgnoreCase);
        if (ExcludeSections != null)
            return !ExcludeSections.Contains(name, StringComparer.OrdinalIgnoreCase);
        return true;
    }
}
