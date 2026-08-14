using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using ILInspector.Findings;

namespace DotnetInspector.Inspectors;

/// <summary>
/// API surface extraction: finding types, extracting full APIs, resolving type forwarders.
/// Delegates enrichment (PDB, source, docs) to <see cref="SourceEnricher"/>.
/// </summary>
internal static class ApiServices
{
    // ===== Extraction Pipeline =====

    internal sealed record LoadedApiSurface(
        ApiSurface Api,
        string ApiDllPath,
        string PdbLookupPath,
        bool IsSummary = false);

    internal static LoadedApiSurface? LoadFullApi(
        string searchPath,
        string? runtimeAssemblyPath,
        string? packagePath,
        string? packageName,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        VerboseLogger logger,
        bool includeAll,
        InspectionQueryRegistry<ApiSurfaceQueryContext>? queryRegistry = null,
        IReadOnlySet<InspectionQueryDefinition>? requestedQueries = null)
    {
        var (api, apiDllPath) = ExtractFullApi(
            searchPath,
            logger,
            includeAll,
            queryRegistry,
            requestedQueries);
        if (api == null || apiDllPath == null)
            return null;

        ResolveForwardedTypes(
            api,
            apiDllPath,
            logger,
            includeAll,
            isPlatformAssembly: runtimeAssemblyPath is not null,
            targetFramework: selectedTfm);

        if (!string.IsNullOrEmpty(packagePath))
        {
            var (parsedPackageName, _) = PackageExtractor.ParsePackageReference(packagePath);
            api.Name = packageName ?? parsedPackageName;
        }
        else
        {
            api.Name = Path.GetFileNameWithoutExtension(apiDllPath);
        }

        api.Tfm = selectedTfm;
        api.Source = apiSource;
        api.Version = apiVersion;
        api.Library = Path.GetFileName(apiDllPath);

        return new LoadedApiSurface(api, apiDllPath, runtimeAssemblyPath ?? apiDllPath);
    }

    internal static LoadedApiSurface? LoadPlatformApiSummary(
        string searchPath,
        string runtimeAssemblyPath,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        VerboseLogger logger)
    {
        logger.Log($"Extracting compact API summary from: {Path.GetFileName(searchPath)}");
        var api = AssemblyReader.ExtractApiSummarySurface(searchPath);
        if (api == null)
            return null;

        ResolveForwardedTypes(
            api,
            searchPath,
            logger,
            includeAll: false,
            isPlatformAssembly: true,
            targetFramework: selectedTfm,
            summaryOnly: true);

        api.Name = Path.GetFileNameWithoutExtension(searchPath);
        api.Tfm = selectedTfm;
        api.Source = apiSource;
        api.Version = apiVersion;
        api.Library = Path.GetFileName(searchPath);

        return new LoadedApiSurface(api, searchPath, runtimeAssemblyPath, IsSummary: true);
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

    internal static (ApiSurface? api, string? dllPath) ExtractFullApi(
        string searchPath,
        VerboseLogger logger,
        bool includeAll,
        InspectionQueryRegistry<ApiSurfaceQueryContext>? queryRegistry = null,
        IReadOnlySet<InspectionQueryDefinition>? requestedQueries = null)
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

        var api = queryRegistry is null
            ? AssemblyReader.ExtractApiSurface(dllFile, includeAll)
            : ExtractApiSurface(
                dllFile,
                includeAll,
                queryRegistry,
                requestedQueries
                    ?? throw new InspectionQueryException(
                        "Typed API extraction requires an explicit query plan."),
                logger);
        return (api, api != null ? dllFile : null);
    }

    internal static ApiSurface? ExtractApiSurface(
        string assemblyPath,
        bool includeAll,
        InspectionQueryRegistry<ApiSurfaceQueryContext> queryRegistry,
        IReadOnlySet<InspectionQueryDefinition> requestedQueries,
        VerboseLogger logger)
    {
        using var session = AssemblyInspectionSession.Open(assemblyPath);
        var context = new ApiSurfaceQueryContext(session, includeAll);
        var results = queryRegistry.Run(requestedQueries, context);
        return results.Get(ApiSurfaceQuery.Definition) switch
        {
            ApiSurfaceResult.Available available => available.Surface,
            ApiSurfaceResult.Failed failed => LogFailure(failed.Error),
            _ => throw new InspectionQueryException(
                "API surface query returned an unknown result."),
        };

        ApiSurface? LogFailure(Exception error)
        {
            logger.LogWarning(
                $"Could not extract API from '{Path.GetFileName(assemblyPath)}': {error.Message}");
            return null;
        }
    }

    // ===== Type Forwarder Resolution =====

    /// <summary>
    /// Resolves types from forwarded assemblies and merges them into the API surface.
    /// Like curl -L, this follows type forwarders to their target assemblies.
    /// </summary>
    internal static void ResolveForwardedTypes(
        ApiSurface api,
        string dllPath,
        VerboseLogger logger,
        bool includeAll,
        bool isPlatformAssembly = false,
        ApiOptions? options = null,
        string? targetFramework = null,
        bool summaryOnly = false)
    {
        if (api.TypeForwarders.Count == 0)
            return;

        TypeDefinitionResolutionSession? resolution = null;
        Dictionary<string, ApiSurface?>? adjacentSummaries =
            summaryOnly ? new(StringComparer.OrdinalIgnoreCase) : null;
        HashSet<MetadataTypeDefinitionName>? adjacentEligibleTypes = summaryOnly
            ? api.TypeForwarders
                .Where(forwarder => forwarder.DefinitionName is not null)
                .GroupBy(forwarder => forwarder.DefinitionName!)
                .Where(group => group.Count() == 1)
                .Select(group => group.Key)
                .ToHashSet()
            : null;
        Dictionary<
            AssemblyAcquisitionRegistration,
            (ResolvedAssemblyReference Assembly,
                HashSet<MetadataTypeDefinitionName> Types)> byAssembly = [];
        int resolvedCount = 0;

        try
        {
            foreach (TypeForwarder forwarder in api.TypeForwarders)
            {
                if (forwarder.DefinitionName is null)
                {
                    logger.Log(
                        $"Forwarded type '{forwarder.TypeName}' has no valid structured metadata name.");
                    continue;
                }

                bool added = false;
                bool handledAdjacent = adjacentSummaries is not null
                    && adjacentEligibleTypes!.Contains(forwarder.DefinitionName)
                    && TryResolveAdjacentSummaryForwarder(
                        api,
                        dllPath,
                        forwarder,
                        adjacentSummaries,
                        [],
                        out added);
                if (handledAdjacent)
                {
                    if (added)
                        resolvedCount++;
                    continue;
                }

                resolution ??= new TypeDefinitionResolutionSession(
                    dllPath,
                    isPlatformAssembly,
                    options?.ProjectAssetsPath,
                    options?.Tfm ?? targetFramework,
                    options?.PlatformFramework);
                TypeResolutionOutcome outcome =
                    resolution.Resolve(forwarder.DefinitionName);
                if (outcome is not TypeResolutionOutcome.Resolved resolved
                    || resolved.Hops.IsDefaultOrEmpty)
                {
                    logger.Log(
                        $"Could not resolve forwarded type '{forwarder.TypeName}': {outcome.GetType().Name}.");
                    continue;
                }

                ResolvedAssemblyReference assembly =
                    resolved.Definition.Assembly.Assembly;
                if (!byAssembly.TryGetValue(
                        assembly.Registration,
                        out var group))
                {
                    group = (assembly, []);
                    byAssembly.Add(assembly.Registration, group);
                }
                group.Types.Add(resolved.Definition.Type);
            }
        }
        finally
        {
            resolution?.Dispose();
        }

        logger.Log(
            $"Resolving {api.TypeForwarders.Count} forwarded types from {byAssembly.Count} acquired libraries...");
        foreach (var (_, group) in byAssembly)
        {
            try
            {
                using Stream stream = group.Assembly.OpenRead();
                var targetApi = summaryOnly
                    ? AssemblyReader.ExtractApiSummarySurface(stream)
                    : AssemblyReader.ExtractApiSurface(stream, includeAll);
                if (targetApi == null)
                    continue;

                foreach (var type in targetApi.Types)
                {
                    if (type.DefinitionName is not null
                        && group.Types.Contains(type.DefinitionName))
                    {
                        AddForwardedType(api, type, group.Assembly.Path);
                        resolvedCount++;
                    }
                }
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or InvalidOperationException
                    or NotSupportedException
                    or ArgumentException)
            {
                logger.Log(
                    $"Error reading resolved assembly '{group.Assembly.Identity.Name}': {ex.Message}");
            }
        }

        if (resolvedCount > 0)
        {
            AssemblyResolutionProvenance provenance = isPlatformAssembly
                ? AssemblyResolutionProvenance.Platform(
                    "InstalledPlatform",
                    frameworkVersion: null,
                    "ApiServices")
                : AssemblyResolutionProvenance.Local("ApiServices");
            api.SurfaceClassification =
                AssemblySurfaceClassifier.Classify(dllPath, provenance);
            api.SurfaceClassificationInspection =
                MetadataFindings.InspectAssemblySurface(
                    api.SurfaceClassification,
                    new FindingSubject(
                        Path.GetFullPath(dllPath),
                        Path.GetFileName(dllPath)));
            api.IsTypeForwardingAssembly =
                api.SurfaceClassification
                    is AssemblySurfaceClassificationOutcome.Classified classified
                && classified.Classification.Kind
                    == AssemblySurfaceKind.Facade;
            if (api.SurfaceClassification
                is AssemblySurfaceClassificationOutcome.Rejected rejected)
            {
                logger.Log(
                    $"Could not classify the forwarding surface: {rejected.Failure.Kind}.");
            }
            api.PublicTypeCount = api.Types.Count;
            api.Types = api.Types.OrderBy(t => t.FullName).ToList();
            logger.Log($"Resolved {resolvedCount} types from forwarded libraries.");
        }
    }

    private static bool TryResolveAdjacentSummaryForwarder(
        ApiSurface api,
        string dllPath,
        TypeForwarder forwarder,
        Dictionary<string, ApiSurface?> adjacentSummaries,
        HashSet<string> visitedPaths,
        out bool added)
    {
        added = false;
        if (!IsSafeAdjacentAssemblyName(forwarder.TargetAssembly))
            return false;

        string? directory = Path.GetDirectoryName(dllPath);
        if (directory is null)
            return false;

        string targetPath = Path.Combine(directory, forwarder.TargetAssembly + ".dll");
        if (!File.Exists(targetPath) || !visitedPaths.Add(targetPath))
            return false;

        if (!adjacentSummaries.TryGetValue(targetPath, out var targetApi))
        {
            targetApi = AssemblyReader.ExtractApiSummarySurface(targetPath);
            adjacentSummaries.Add(targetPath, targetApi);
        }

        if (targetApi is null)
            return false;

        var matchingTypes = targetApi.Types
            .Where(candidate => candidate.DefinitionName == forwarder.DefinitionName)
            .Take(2)
            .ToArray();
        if (matchingTypes.Length == 1)
        {
            if (api.Types.Any(
                candidate => candidate.DefinitionName == forwarder.DefinitionName))
            {
                return true;
            }

            AddForwardedType(api, matchingTypes[0], targetPath);
            added = true;
            return true;
        }
        if (matchingTypes.Length > 1)
            return false;

        var matchingForwarders = targetApi.TypeForwarders
            .Where(candidate => candidate.DefinitionName == forwarder.DefinitionName)
            .Take(2)
            .ToArray();
        if (matchingForwarders.Length == 1)
        {
            return TryResolveAdjacentSummaryForwarder(
                api,
                targetPath,
                matchingForwarders[0],
                adjacentSummaries,
                visitedPaths,
                out added);
        }
        if (matchingForwarders.Length > 1)
            return false;

        // The adjacent target was readable and contains neither a visible definition nor another
        // hop. The full extractor would not add this forwarded type to the public surface either.
        return true;
    }

    private static bool IsSafeAdjacentAssemblyName(string assemblyName) =>
        !string.IsNullOrEmpty(assemblyName)
        && assemblyName is not "." and not ".."
        && assemblyName.IndexOfAny(['/', '\\']) < 0
        && !Path.IsPathRooted(assemblyName);

    private static void AddForwardedType(
        ApiSurface api,
        ApiType type,
        string? sourceAssemblyPath)
    {
        type.IsForwarded = true;
        type.SourceAssemblyPath = sourceAssemblyPath;
        api.Types.Add(type);
        api.PublicMethodCount += type.Members.Count(
            DotnetInspector.Sections.ApiMemberSectionDescriptors.IsMethodLike);
        api.PublicPropertyCount += type.Members.Count(m => m.Kind == "property");
        api.PublicEventCount += type.Members.Count(m => m.Kind == "event");
        api.PublicFieldCount += type.Members.Count(m => m.Kind == "field");
    }
}
