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
                HashSet<MetadataTypeDefinitionName> Types,
                HashSet<int> TypeTokens)> byAssembly = [];

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
                AddForwardedResolutionFailure(
                    api,
                    dllPath,
                    forwarder,
                    outcome);
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
                group = (assembly, [], []);
                byAssembly.Add(assembly.Registration, group);
            }
            group.Types.Add(resolved.Definition.Type);
            group.TypeTokens.Add(
                resolved.Definition.Address.Definition.Value);
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
                        includeAll,
                        typesOnly: false,
                        out TypeDefinitionApiSurfaceFailure?
                            extractionFailure);
                if (targetApi == null)
                {
                    AddForwardedTargetFailure(
                        api,
                        group.Assembly,
                        group.Types,
                        extractionFailure?.Kind
                            ?? "Unavailable",
                        extractionFailure?.Detail
                            ?? "The forwarded target API surface could not be extracted.");
                    continue;
                }

                resolvedCount += MergeForwardedTypes(
                    api,
                    targetApi,
                    group.Types,
                    group.Assembly,
                    group.TypeTokens);
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or InvalidOperationException
                    or NotSupportedException
                    or ArgumentException)
            {
                AddForwardedTargetFailure(
                    api,
                    group.Assembly,
                    group.Types,
                    ex.GetType().Name,
                    ex.Message);
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

    internal static int MergeForwardedTypes(
        ApiSurface api,
        ApiSurface targetApi,
        IReadOnlySet<MetadataTypeDefinitionName> forwardedTypes,
        ResolvedAssemblyReference targetAssembly,
        IReadOnlyCollection<int>? forwardedTypeTokens = null)
    {
        List<ApiType> copiedTypes =
        [
            .. targetApi.Types.Where(type =>
                type.DefinitionName is not null
                && forwardedTypes.Contains(type.DefinitionName)),
        ];
        var copiedByToken = forwardedTypeTokens is null
            ? []
            : new HashSet<int>(forwardedTypeTokens);
        foreach (ApiType type in copiedTypes)
        {
            Add(type.MetadataToken);
            foreach (ApiMember member in type.Members)
            {
                Add(member.MetadataToken);
                Add(member.GetterToken);
                Add(member.SetterToken);
                Add(member.AdderToken);
                Add(member.RemoverToken);
            }
        }

        api.MergeInspectionFailuresFrom(
            targetApi,
            subject => copiedByToken.Contains(subject.SubjectToken),
            includeNonConstraintFailures: false);
        foreach (ApiSurfaceInspectionFailure failure
            in targetApi.InspectionFailures)
        {
            if (failure.Operation
                    == ApiSurfaceInspectionFailure
                        .GenericParameterConstraintResolutionOperation
                || !IncludesFailure(failure))
            {
                continue;
            }

            api.InspectionFailures.Add(
                failure.SubjectToken == 0
                    ? failure with
                    {
                        AffectedTypeDefinitions =
                            [.. forwardedTypes],
                    }
                    : failure);
        }

        if (copiedTypes.Count == 0)
            return 0;

        foreach (ApiType type in copiedTypes)
        {
            type.IsForwarded = true;
            type.SourceAssemblyPath = targetAssembly.Path;
            api.Types.Add(type);
            api.PublicMethodCount +=
                type.Members.Count(
                    DotnetInspector.Sections
                        .ApiMemberSectionDescriptors.IsMethodLike);
            api.PublicPropertyCount +=
                type.Members.Count(
                    static member => member.Kind == "property");
            api.PublicEventCount +=
                type.Members.Count(
                    static member => member.Kind == "event");
            api.PublicFieldCount +=
                type.Members.Count(
                    static member => member.Kind == "field");
        }

        return copiedTypes.Count;

        void Add(int? token)
        {
            if (token is int value)
                copiedByToken.Add(value);
        }

        bool IncludesFailure(
            ApiSurfaceInspectionFailure failure) =>
            failure.SubjectToken == 0
            || copiedByToken.Contains(
                failure.OwningTypeToken
                    ?? failure.SubjectToken);
    }

    static void AddForwardedTargetFailure(
        ApiSurface api,
        ResolvedAssemblyReference targetAssembly,
        IReadOnlySet<MetadataTypeDefinitionName>
            affectedTypes,
        string kind,
        string detail)
    {
        api.InspectionFailures.Add(
            new ApiSurfaceInspectionFailure(
                "extract forwarded API surface",
                0,
                MetadataTypeNameFailureMechanism.Metadata,
                kind,
                detail,
                targetAssembly.Identity)
            {
                SourceAssemblyPath = targetAssembly.Path,
                AffectedTypeDefinitions =
                    [.. affectedTypes],
            });
    }

    static void AddForwardedResolutionFailure(
        ApiSurface api,
        string sourceAssemblyPath,
        TypeForwarder forwarder,
        TypeResolutionOutcome outcome)
    {
        string kind =
            outcome switch
            {
                TypeResolutionOutcome.Rejected rejected =>
                    rejected.Failure.GetType().Name,
                TypeResolutionOutcome.Unavailable unavailable =>
                    unavailable.Failure.Kind.ToString(),
                TypeResolutionOutcome.Resolved =>
                    "MissingForwardingEvidence",
                _ => outcome.GetType().Name,
            };
        AssemblyReferenceIdentity? subjectAssembly =
            outcome.Hops.IsDefaultOrEmpty
                ? null
                : outcome.Hops[0]
                    .SourceAssembly.Assembly.Identity;

        api.InspectionFailures.Add(
            new ApiSurfaceInspectionFailure(
                "resolve forwarded type",
                0,
                MetadataTypeNameFailureMechanism.Metadata,
                kind,
                $"Forwarded type '{forwarder.TypeName}' "
                    + $"could not be resolved: {kind}.",
                subjectAssembly,
                outcome.TerminalAssemblyIdentity)
            {
                SourceAssemblyPath = sourceAssemblyPath,
                AffectedTypeDefinitions =
                    forwarder.DefinitionName is null
                        ? []
                        : [forwarder.DefinitionName],
            });
    }
}
