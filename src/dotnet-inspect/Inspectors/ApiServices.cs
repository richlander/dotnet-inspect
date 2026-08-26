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
    internal enum AssemblyReferenceRole
    {
        TokenOrigin,
        Surface,
        RuntimeOrPdb,
    }

    // ===== Extraction Pipeline =====

    internal sealed record LoadedApiSurface(
        ApiSurface Api,
        string ApiDllPath,
        string PdbLookupPath,
        ResolvedAssemblyReference? AssemblyReference,
        ResolvedAssemblyReference? RuntimeAssemblyReference,
        bool IsSummary = false,
        string? ProjectAssetsPath = null,
        string? PlatformFramework = null)
    {
        internal Dictionary<MetadataTypeDefinitionName, ResolvedAssemblyReference>
            RuntimeImplementationAssemblies { get; } = [];
    }

    internal static LoadedApiSurface? LoadFullApi(
        string searchPath,
        ResolvedAssemblyReference? assemblyReference,
        ResolvedAssemblyReference? runtimeAssemblyReference,
        string? runtimeAssemblyPath,
        string? packagePath,
        string? packageName,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        VerboseLogger logger,
        ApiOptions options)
    {
        string apiDllPath = assemblyReference?.Path ?? searchPath;
        logger.Log($"Extracting API from: {Path.GetFileName(apiDllPath)}");

        using TypeDefinitionResolutionSession? resolution =
            assemblyReference is null || !assemblyReference.IsAssembly
                ? null
                : TryCreateResolutionSession(
                assemblyReference,
                options.ProjectAssetsPath,
                options.Tfm ?? selectedTfm,
                options.PlatformFramework);
        ApiSurface? api =
            resolution is not null
                ? resolution.ExtractApiSurface(
                    options.IncludeAll)
                : assemblyReference is not null
                        ? assemblyReference.IsAssembly
                            ? AssemblyReader.ExtractApiSurface(
                                assemblyReference,
                                options.IncludeAll)
                            : AssemblyReader.ExtractModuleApiSurface(
                                assemblyReference,
                                options.IncludeAll)
                        : AssemblyReader.ExtractModuleApiSurface(
                            apiDllPath,
                            options.IncludeAll);
        if (api is null)
            return null;

        if (resolution is null
            && assemblyReference is { IsAssembly: true }
            && assemblyReference.Path is null
            && api.TypeForwarders.Count > 0)
        {
            logger.Log(
                "Pathless forwarding assemblies require a rooted "
                + "type-resolution context.");
            return null;
        }

        if (resolution is not null)
        {
            ResolveForwardedTypes(
                api,
                apiDllPath,
                logger,
                options.IncludeAll,
                isPlatformAssembly:
                    runtimeAssemblyPath is not null,
                resolution: resolution,
                rootAssembly: assemblyReference);
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

        return new LoadedApiSurface(
            api,
            apiDllPath,
            runtimeAssemblyPath ?? apiDllPath,
            assemblyReference,
            runtimeAssemblyReference,
            ProjectAssetsPath: options.ProjectAssetsPath,
            PlatformFramework: options.PlatformFramework);
    }

    static TypeDefinitionResolutionSession? TryCreateResolutionSession(
        ResolvedAssemblyReference assemblyReference,
        string? projectAssetsPath,
        string? targetFramework,
        string? platformFramework)
    {
        try
        {
            return new TypeDefinitionResolutionSession(
                assemblyReference,
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

    internal static LoadedApiSurface? LoadPlatformApiSummary(
        string searchPath,
        ResolvedAssemblyReference assemblyReference,
        string runtimeAssemblyPath,
        ResolvedAssemblyReference? runtimeAssemblyReference,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        VerboseLogger logger)
    {
        logger.Log($"Extracting compact API summary from: {Path.GetFileName(searchPath)}");
        var api = AssemblyReader.ExtractApiSummarySurface(assemblyReference);
        if (api == null)
            return null;

        ResolveForwardedTypes(
            api,
            searchPath,
            logger,
            includeAll: false,
            isPlatformAssembly: true,
            targetFramework: selectedTfm,
            summaryOnly: true,
            rootAssembly: assemblyReference);

        api.Name = Path.GetFileNameWithoutExtension(searchPath);
        api.Tfm = selectedTfm;
        api.Source = apiSource;
        api.Version = apiVersion;
        api.Library = Path.GetFileName(searchPath);
        return new LoadedApiSurface(
            api,
            searchPath,
            runtimeAssemblyPath,
            assemblyReference,
            runtimeAssemblyReference,
            IsSummary: true);
    }

    internal static ResolvedAssemblyReference? AssemblyReferenceForRole(
        LoadedApiSurface loaded,
        ApiType type,
        AssemblyReferenceRole role)
    {
        return role switch
        {
            AssemblyReferenceRole.TokenOrigin =>
                type.SourceAssemblyReference
                    ?? loaded.AssemblyReference,
            AssemblyReferenceRole.Surface =>
                loaded.AssemblyReference,
            AssemblyReferenceRole.RuntimeOrPdb =>
                RuntimeAssemblyReference(loaded, type),
            _ => throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "Unknown assembly-reference role."),
        };
    }

    static ResolvedAssemblyReference? RuntimeAssemblyReference(
        LoadedApiSurface loaded,
        ApiType type)
    {
        if (IsForwardedTypeAcquisition(loaded, type))
            return type.SourceAssemblyReference;

        ResolvedAssemblyReference? runtime =
            loaded.RuntimeAssemblyReference
                ?? loaded.AssemblyReference
                ?? type.SourceAssemblyReference;
        if (runtime is null || type.DefinitionName is not { } definitionName)
            return runtime;

        if (loaded.RuntimeImplementationAssemblies.TryGetValue(
                definitionName,
                out ResolvedAssemblyReference? implementation))
        {
            return implementation;
        }

        using (AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(runtime))
        {
            if (session.ProbeDeclaration(definitionName)
                is not TypeDeclarationResult.Forwarded)
            {
                loaded.RuntimeImplementationAssemblies.Add(
                    definitionName,
                    runtime);
                return runtime;
            }
        }

        using var resolution = new TypeDefinitionResolutionSession(
            runtime,
            projectAssetsPath: loaded.ProjectAssetsPath,
            targetFramework: loaded.Api.Tfm,
            platformFramework: loaded.PlatformFramework);
        TypeResolutionOutcome outcome = resolution.Resolve(definitionName);
        if (outcome is not TypeResolutionOutcome.Resolved resolved)
        {
            string terminal =
                outcome.TerminalAssemblyIdentity?.Name
                ?? "unknown assembly";
            throw new InvalidOperationException(
                $"Cannot resolve runtime implementation of "
                + $"'{definitionName.ToEscapedFullName()}' from "
                + $"'{runtime.Identity.Name}'; resolution ended at "
                + $"'{terminal}' with {outcome.GetType().Name}.");
        }

        implementation = resolved.Definition.Assembly.Assembly;
        loaded.RuntimeImplementationAssemblies.Add(
            definitionName,
            implementation);
        return implementation;
    }

    static bool IsForwardedTypeAcquisition(
        LoadedApiSurface loaded,
        ApiType type) =>
        type.SourceAssemblyReference is { } source
        && loaded.AssemblyReference is { } surface
        && !ReferenceEquals(
            source.Registration,
            surface.Registration);

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

            var lookup = ApiTypeLookupService.LookupType(api, typeName);
            if (lookup.Found)
            {
                logger.Log($"Found in: {Path.GetFileName(dllFile)}");
                return (lookup.Type, Path.GetFileName(dllFile), dllFile, api);
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
        string? targetFramework = null,
        bool summaryOnly = false,
        ResolvedAssemblyReference? rootAssembly = null)
    {
        if (api.TypeForwarders.Count == 0)
            return;

        if (summaryOnly)
        {
            ResolveSummaryForwardedTypes(
                api,
                dllPath,
                logger,
                isPlatformAssembly,
                options,
                targetFramework,
                rootAssembly);
            return;
        }

        using var resolution = rootAssembly is null
            ? new TypeDefinitionResolutionSession(
                dllPath,
                isPlatformAssembly,
                options?.ProjectAssetsPath,
                options?.Tfm ?? targetFramework,
                options?.PlatformFramework)
            : new TypeDefinitionResolutionSession(
                rootAssembly,
                options?.ProjectAssetsPath,
                options?.Tfm ?? targetFramework,
                options?.PlatformFramework);
        ResolveForwardedTypes(
            api,
            dllPath,
            logger,
            includeAll,
            isPlatformAssembly,
            resolution,
            rootAssembly);
    }

    static void ResolveForwardedTypes(
        ApiSurface api,
        string dllPath,
        VerboseLogger logger,
        bool includeAll,
        bool isPlatformAssembly,
        TypeDefinitionResolutionSession resolution,
        ResolvedAssemblyReference? rootAssembly)
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
            api.SurfaceClassification =
                rootAssembly is null
                    ? AssemblySurfaceClassifier.Classify(
                        dllPath,
                        isPlatformAssembly
                            ? AssemblyResolutionProvenance.Platform(
                                "InstalledPlatform",
                                frameworkVersion: null,
                                "ApiServices")
                            : AssemblyResolutionProvenance.Local(
                                "ApiServices"))
                    : AssemblySurfaceClassifier.Classify(
                        rootAssembly);
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

    static void ResolveSummaryForwardedTypes(
        ApiSurface api,
        string dllPath,
        VerboseLogger logger,
        bool isPlatformAssembly,
        ApiOptions? options,
        string? targetFramework,
        ResolvedAssemblyReference? rootAssembly)
    {
        TypeDefinitionResolutionSession? resolution = null;
        var adjacentSummaries =
            new Dictionary<string, ApiSurface?>(
                StringComparer.OrdinalIgnoreCase);
        HashSet<MetadataTypeDefinitionName> adjacentEligibleTypes =
            api.TypeForwarders
                .Where(forwarder => forwarder.DefinitionName is not null)
                .GroupBy(forwarder => forwarder.DefinitionName!)
                .Where(group => group.Count() == 1)
                .Select(group => group.Key)
                .ToHashSet();
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
                bool handledAdjacent =
                    adjacentEligibleTypes.Contains(forwarder.DefinitionName)
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

                resolution ??= rootAssembly is null
                    ? new TypeDefinitionResolutionSession(
                        dllPath,
                        isPlatformAssembly,
                        options?.ProjectAssetsPath,
                        options?.Tfm ?? targetFramework,
                        options?.PlatformFramework)
                    : new TypeDefinitionResolutionSession(
                        rootAssembly,
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

            logger.Log(
                $"Resolving {api.TypeForwarders.Count} forwarded types from {byAssembly.Count} acquired libraries...");
            foreach (var (_, group) in byAssembly)
            {
                try
                {
                    ApiSurface? targetApi =
                        AssemblyReader.ExtractApiSummarySurface(group.Assembly);
                    if (targetApi is null)
                        continue;

                    foreach (ApiType type in targetApi.Types)
                    {
                        if (type.DefinitionName is not null
                            && group.Types.Contains(type.DefinitionName))
                        {
                            AddForwardedType(
                                api,
                                type,
                                group.Assembly.Path);
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
        }
        finally
        {
            resolution?.Dispose();
        }

        if (resolvedCount > 0)
        {
            api.SurfaceClassification =
                rootAssembly is null
                    ? AssemblySurfaceClassifier.Classify(
                        dllPath,
                        isPlatformAssembly
                            ? AssemblyResolutionProvenance.Platform(
                                "InstalledPlatform",
                                frameworkVersion: null,
                                "ApiServices")
                            : AssemblyResolutionProvenance.Local(
                                "ApiServices"))
                    : AssemblySurfaceClassifier.Classify(
                        rootAssembly);
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
            logger.Log(
                $"Resolved {resolvedCount} types from forwarded libraries.");
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
