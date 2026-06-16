using DotnetInspector.Packages;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// API surface extraction: finding types, extracting full APIs, resolving type forwarders.
/// Delegates enrichment (PDB, source, docs) to <see cref="SourceEnricher"/>.
/// </summary>
internal static class ApiServices
{
    // ===== Extraction Pipeline =====

    /// <summary>
    /// Extracts a specific type from a package or assembly, with full path info.
    /// Used by api command and source command.
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
                var outcome = await PackageExtractor.ExtractPackageAsync(httpClient, options.PackagePath, logger.Log, "inspect-api", options.SourceOptions);
                if (!outcome.IsSuccess)
                {
                    Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
                    return (null, null, null);
                }
                var extracted = outcome.Result!;

                (searchPath, tempDir, var packageName, _) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);

                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = TfmSelector.FindAssemblyByTfm(searchPath, options.Tfm, packageName);
                    if (tfmAssembly != null)
                        searchPath = tfmAssembly;
                }
                else
                {
                    var (highestPath, _) = TfmSelector.SelectHighestTfmAssembly(TfmSelector.GetPackageDlls(searchPath), searchPath, packageName);
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
                var (assemblyPath, framework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                    options.PlatformAssembly,
                    httpClient,
                    logger.Log,
                    options.PlatformFramework);

                if (error != null)
                {
                    Console.Error.WriteLine($"Error: {error}");
                    return (null, null, null);
                }

                searchPath = assemblyPath!;
                logger.Log($"Using platform ref library: {framework} {version}");

                var (runtimePath, _, _, runtimeError) = PlatformResolver.ResolveAssembly(
                    options.PlatformAssembly,
                    options.PlatformFramework,
                    packsDirectory: null,
                    useRuntimeAssemblies: true);

                if (runtimeError == null && runtimePath != null)
                {
                    runtimeAssemblyPath = runtimePath;
                    logger.Log($"Using runtime library for PDB lookup: {runtimePath}");
                }
            }
            else
            {
                return (null, null, null);
            }

            var (apiType, foundIn, dllPath, surface) = FindType(typeName, searchPath, logger, options.IncludeAll);

            if (apiType != null && dllPath != null)
            {
                var pdbLookupPath = runtimeAssemblyPath ?? dllPath;
                await SourceEnricher.EnrichDocsAsync(apiType, typeName, pdbLookupPath, options, logger, httpClient);
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
    /// Used by source command for assembly-wide sample collection and DiffCommand.
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
                var outcome = await PackageExtractor.ExtractPackageAsync(httpClient, options.PackagePath, logger.Log, "inspect-api", options.SourceOptions);
                if (!outcome.IsSuccess)
                {
                    Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
                    return (null, null);
                }
                var extracted = outcome.Result!;
                (searchPath, tempDir, packageName, _) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);

                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = TfmSelector.FindAssemblyByTfm(searchPath, options.Tfm, packageName);
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
                await SourceEnricher.EnrichDocsAsync(api.Types.ToList(), dllPath, options, logger, httpClient);
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

    // ===== Merged API Extraction (multi-library packages) =====

    /// <summary>
    /// Extracts and merges API surfaces from ALL DLLs in a package at the highest TFM.
    /// Used by diff to compare multi-library packages.
    /// </summary>
    internal static async Task<(ApiSurface? api, string? selectedTfm)> ExtractMergedApiSurfaceAsync(ApiOptions options, VerboseLogger logger, HttpClient httpClient)
    {
        string? tempDir = null;
        try
        {
            if (string.IsNullOrEmpty(options.PackagePath))
                return (null, null);

            var outcome = await PackageExtractor.ExtractPackageAsync(httpClient, options.PackagePath, logger.Log, "inspect-api", options.SourceOptions);
            if (!outcome.IsSuccess)
            {
                Console.Error.WriteLine($"Error: {outcome.ErrorMessage}");
                return (null, null);
            }
            var extracted = outcome.Result!;
            var (searchPath, packageName) = (extracted.ExtractPath, extracted.PackageName);
            tempDir = extracted.TempDir;

            var dlls = TfmSelector.GetPackageDlls(searchPath);
            if (dlls.Count == 0)
                return (null, null);

            // Single DLL — fast path
            if (dlls.Count == 1)
                return await ExtractApiSurfaceAsync(options, logger, httpClient);

            var (tfmDlls, selectedTfm) = TfmSelector.SelectHighestTfmAssemblies(dlls, searchPath);
            if (tfmDlls.Count == 0)
                return (null, null);

            // Single DLL at this TFM — fast path
            if (tfmDlls.Count == 1)
            {
                var singleApi = AssemblyReader.ExtractApiSurface(tfmDlls[0], options.IncludeAll);
                if (singleApi != null)
                {
                    singleApi.Name = packageName ?? Path.GetFileNameWithoutExtension(tfmDlls[0]);
                    singleApi.Tfm = selectedTfm;
                }
                return (singleApi, selectedTfm);
            }

            // Multiple DLLs — merge all API surfaces
            logger.Log($"Merging API surfaces from {tfmDlls.Count} libraries at {selectedTfm}");

            var merged = new ApiSurface
            {
                Name = packageName,
                Tfm = selectedTfm
            };

            foreach (var dll in tfmDlls)
            {
                var surface = AssemblyReader.ExtractApiSurface(dll, options.IncludeAll);
                if (surface == null)
                    continue;

                var libName = Path.GetFileNameWithoutExtension(dll);
                logger.Log($"  + {libName}: {surface.PublicTypeCount} types");

                merged.Types.AddRange(surface.Types);
                merged.PublicTypeCount += surface.PublicTypeCount;
                merged.PublicMethodCount += surface.PublicMethodCount;
                merged.PublicPropertyCount += surface.PublicPropertyCount;
                merged.PublicEventCount += surface.PublicEventCount;
                merged.PublicFieldCount += surface.PublicFieldCount;
            }

            merged.Types = merged.Types.OrderBy(t => t.FullName).ToList();
            return (merged, selectedTfm);
        }
        finally
        {
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    // ===== Type Lookup =====

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

            var match = api.Types.FirstOrDefault(t => TypeMatcher.Matches(t.FullName, typeName));

            if (match != null)
            {
                logger.Log($"Found in: {Path.GetFileName(dllFile)}");
                return (match, Path.GetFileName(dllFile), dllFile, api);
            }
        }

        return (null, null, null, null);
    }

    // ===== Full API Extraction =====

    internal static (ApiSurface? api, string? dllPath) ExtractFullApi(string searchPath, VerboseLogger logger, bool includeAll)
    {
        string? dllFile;
        if (File.Exists(searchPath) && searchPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            dllFile = searchPath;
        }
        else if (Directory.Exists(searchPath))
        {
            // Check ref/ (ref packages) then lib/
            string? contentDir = null;
            foreach (var subdir in new[] { "ref", "lib" })
            {
                var candidate = Path.Combine(searchPath, subdir);
                if (Directory.Exists(candidate))
                {
                    contentDir = candidate;
                    break;
                }
            }

            if (contentDir != null)
            {
                var dlls = Directory.GetFiles(contentDir, "*.dll", SearchOption.AllDirectories).ToList();
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

    // ===== Type Forwarder Resolution =====

    /// <summary>
    /// Resolves types from forwarded assemblies and merges them into the API surface.
    /// Like curl -L, this follows type forwarders to their target assemblies.
    /// </summary>
    internal static void ResolveForwardedTypes(ApiSurface api, string dllPath, VerboseLogger logger, bool includeAll)
    {
        if (api.TypeForwarders.Count == 0)
            return;

        var assemblyDir = Path.GetDirectoryName(dllPath);
        if (assemblyDir == null)
            return;

        var byAssembly = api.TypeForwarders
            .GroupBy(f => f.TargetAssembly)
            .ToDictionary(g => g.Key, g => g.Select(f => f.TypeName).ToHashSet(StringComparer.OrdinalIgnoreCase));

        logger.Log($"Resolving {api.TypeForwarders.Count} forwarded types from {byAssembly.Count} libraries...");

        int resolvedCount = 0;

        foreach (var (targetAssembly, forwardedTypeNames) in byAssembly)
        {
            var targetPath = Path.Combine(assemblyDir, targetAssembly + ".dll");
            if (!File.Exists(targetPath))
            {
                logger.Log($"Target library '{targetAssembly}' not found, skipping.");
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
                        type.IsForwarded = true;
                        type.SourceAssemblyPath = targetPath;
                        api.Types.Add(type);
                        api.PublicMethodCount += type.Members.Count(DotnetInspector.Sections.ApiMemberSectionDescriptors.IsMethodLike);
                        api.PublicPropertyCount += type.Members.Count(m => m.Kind == "property");
                        api.PublicEventCount += type.Members.Count(m => m.Kind == "event");
                        api.PublicFieldCount += type.Members.Count(m => m.Kind == "field");
                        resolvedCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Log($"Error reading '{targetAssembly}': {ex.Message}");
            }
        }

        if (resolvedCount > 0)
        {
            api.IsTypeForwardingAssembly = true;
            api.PublicTypeCount = api.Types.Count;
            api.Types = api.Types.OrderBy(t => t.FullName).ToList();
            logger.Log($"Resolved {resolvedCount} types from forwarded libraries.");
        }
    }
}
