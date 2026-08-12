using DotnetInspector.Packages;
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
        string PdbLookupPath);

    internal static LoadedApiSurface? LoadFullApi(
        string searchPath,
        string? runtimeAssemblyPath,
        string? packagePath,
        string? packageName,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        VerboseLogger logger,
        ApiOptions options)
    {
        string? apiDllPath = FindApiDll(searchPath, logger);
        if (apiDllPath is null)
            return null;

        using TypeDefinitionResolutionSession? resolution =
            TryCreateResolutionSession(
                apiDllPath,
                isPlatformAssembly: runtimeAssemblyPath is not null,
                options.ProjectAssetsPath,
                options.Tfm ?? selectedTfm,
                options.PlatformFramework);
        ApiSurface? api =
            resolution is not null
                ? resolution.ExtractApiSurface(
                    options.IncludeAll)
                : AssemblyReader.ExtractModuleApiSurface(
                    apiDllPath,
                    options.IncludeAll);
        if (api is null)
            return null;

        if (resolution is not null)
        {
            ResolveForwardedTypes(
                api,
                apiDllPath,
                logger,
                options.IncludeAll,
                isPlatformAssembly:
                    runtimeAssemblyPath is not null,
                resolution: resolution);
        }

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

    static TypeDefinitionResolutionSession? TryCreateResolutionSession(
        string assemblyPath,
        bool isPlatformAssembly,
        string? projectAssetsPath,
        string? targetFramework,
        string? platformFramework)
    {
        try
        {
            return new TypeDefinitionResolutionSession(
                assemblyPath,
                isPlatformAssembly,
                projectAssetsPath,
                targetFramework,
                platformFramework);
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or ArgumentException)
        {
            return null;
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

    static string? FindApiDll(
        string searchPath,
        VerboseLogger logger)
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
            return null;
        }

        if (dllFile == null)
            return null;

        logger.Log($"Extracting API from: {Path.GetFileName(dllFile)}");
        return dllFile;
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
        string? targetFramework = null)
    {
        if (api.TypeForwarders.Count == 0)
            return;

        using var resolution = new TypeDefinitionResolutionSession(
            dllPath,
            isPlatformAssembly,
            options?.ProjectAssetsPath,
            options?.Tfm ?? targetFramework,
            options?.PlatformFramework);
        ResolveForwardedTypes(
            api,
            dllPath,
            logger,
            includeAll,
            isPlatformAssembly,
            resolution);
    }

    static void ResolveForwardedTypes(
        ApiSurface api,
        string dllPath,
        VerboseLogger logger,
        bool includeAll,
        bool isPlatformAssembly,
        TypeDefinitionResolutionSession resolution)
    {
        Dictionary<
            AssemblyAcquisitionRegistration,
            (ResolvedAssemblyReference Assembly,
                HashSet<MetadataTypeDefinitionName> Types)> byAssembly = [];

        foreach (TypeForwarder forwarder in api.TypeForwarders)
        {
            if (forwarder.DefinitionName is null)
            {
                logger.Log(
                    $"Forwarded type '{forwarder.TypeName}' has no valid structured metadata name.");
                continue;
            }

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

        logger.Log(
            $"Resolving {api.TypeForwarders.Count} forwarded types from {byAssembly.Count} acquired libraries...");
        int resolvedCount = 0;
        foreach (var (_, group) in byAssembly)
        {
            try
            {
                ApiSurface? targetApi =
                    resolution.ExtractApiSurface(
                    group.Assembly,
                    includeAll);
                if (targetApi == null)
                    continue;

                List<ApiType> forwardedTypes =
                [
                    .. targetApi.Types.Where(type =>
                        type.DefinitionName is not null
                        && group.Types.Contains(type.DefinitionName)),
                ];
                var forwardedByToken = new Dictionary<int, ApiType>();
                foreach (ApiType type in forwardedTypes)
                {
                    Add(type.MetadataToken, type);
                    foreach (ApiMember member in type.Members)
                    {
                        Add(member.MetadataToken, type);
                        Add(member.GetterToken, type);
                        Add(member.SetterToken, type);
                        Add(member.AdderToken, type);
                        Add(member.RemoverToken, type);
                    }
                }

                api.MergeInspectionFailuresFrom(
                    targetApi,
                    subject => forwardedByToken.ContainsKey(
                        subject.SubjectToken),
                    includeNonConstraintFailures: false);

                foreach (ApiType type in forwardedTypes)
                {
                    type.IsForwarded = true;
                    type.SourceAssemblyPath = group.Assembly.Path;
                    api.Types.Add(type);
                    api.PublicMethodCount += type.Members.Count(DotnetInspector.Sections.ApiMemberSectionDescriptors.IsMethodLike);
                    api.PublicPropertyCount += type.Members.Count(m => m.Kind == "property");
                    api.PublicEventCount += type.Members.Count(m => m.Kind == "event");
                    api.PublicFieldCount += type.Members.Count(m => m.Kind == "field");
                    resolvedCount++;
                }

                void Add(
                    int? token,
                    ApiType type)
                {
                    if (token is int value)
                        forwardedByToken[value] = type;
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
}
