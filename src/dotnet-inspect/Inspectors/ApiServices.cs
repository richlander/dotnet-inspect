using DotnetInspector.Packages;
using ILInspector.Metadata;
using DotnetInspector.Models;
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
        IReadOnlyDictionary<
            ApiType,
            ResolvedAssemblyReference> SourceAssemblies,
        bool IsSummary = false)
    {
        internal string GetLibraryAssetPath(string? packageExtractPath) =>
            packageExtractPath is null
                ? Path.GetFullPath(ApiDllPath)
                : Path.GetRelativePath(packageExtractPath, ApiDllPath)
                    .Replace(Path.DirectorySeparatorChar, '/');

        internal ResolvedAssemblyReference GetSourceAssembly(
            ApiType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (SourceAssemblies.TryGetValue(type, out var assembly))
                return assembly;

            throw new InvalidOperationException(
                $"No acquisition descriptor was retained for selected type '{type.FullName}'.");
        }

        internal ResolvedAssemblyReference? TryGetSourceAssembly(
            ApiType type)
        {
            ArgumentNullException.ThrowIfNull(type);
            return SourceAssemblies.TryGetValue(
                type,
                out var assembly)
                ? assembly
                : null;
        }
    }

    internal static LoadedApiSurface? LoadTypeApi(
        ApiSourceResult source,
        ApiOptions options,
        bool summaryOnly = false) =>
        summaryOnly
            ? LoadPlatformApiSummary(
                source.SearchPath,
                source.RuntimeAssemblyPath!,
                source.ApiSource,
                source.ApiVersion,
                source.SelectedTfm,
                source.Context.Logger,
                source.PlatformFramework)
            : LoadFullApi(
                source.SearchPath,
                source.RuntimeAssemblyPath,
                source.ResolvedPackagePath,
                source.PackageName,
                source.ApiSource,
                source.ApiVersion,
                source.SelectedTfm,
                source.Context.Logger,
                options,
                source.PackageExtractPath,
                useTypedSelection: true,
                platformFramework: source.PlatformFramework);

    internal static LoadedApiSurface? LoadFullApi(
        string searchPath,
        string? runtimeAssemblyPath,
        string? packagePath,
        string? packageName,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        VerboseLogger logger,
        ApiOptions options,
        string? packageExtractPath = null,
        bool usePackageSourcePolicy = false,
        bool useTypedSelection = false,
        string? platformFramework = null)
    {
        string? apiDllPath = FindApiDll(searchPath, logger);
        if (apiDllPath is null)
            return null;

        var provenance = CreateRootProvenance(
            apiSource,
            apiVersion,
            packageName,
            selectedTfm,
            options,
            platformFramework);
        bool isPlatformAssembly = runtimeAssemblyPath is not null
            || useTypedSelection && apiSource == SourceKind.Platform;
        ResolvedAssemblyReference? rootAssembly =
            useTypedSelection
                ? SelectRootAssembly(apiDllPath, provenance)
                : TryCreateRootAssembly(apiDllPath, provenance);
        using TypeDefinitionResolutionSession? resolution =
            rootAssembly is null
                ? null
                : useTypedSelection
                ? new TypeDefinitionResolutionSession(
                    rootAssembly,
                    isPlatformAssembly,
                    options.ProjectAssetsPath,
                    options.Tfm ?? selectedTfm,
                    platformFramework ?? options.PlatformFramework,
                    packageExtractPath,
                    options.SourceOptions,
                    usePackageSourcePolicy:
                        usePackageSourcePolicy || packageExtractPath is not null)
                : TryCreateResolutionSession(
                    rootAssembly,
                    isPlatformAssembly:
                        runtimeAssemblyPath is not null,
                    options.ProjectAssetsPath,
                    options.Tfm ?? selectedTfm,
                    options.PlatformFramework,
                    packageExtractPath,
                    options.SourceOptions,
                    usePackageSourcePolicy:
                        usePackageSourcePolicy || packageExtractPath is not null);
        ApiSurface? api =
            resolution is not null
                ? resolution.ExtractApiSurface(
                    options.IncludeAll)
                : AssemblyReader.ExtractModuleApiSurface(
                    apiDllPath,
                    options.IncludeAll);
        if (api is null)
            return null;

        var sourceAssemblies =
            new Dictionary<ApiType, ResolvedAssemblyReference>(
                ReferenceEqualityComparer.Instance);
        if (rootAssembly is not null)
        {
            foreach (ApiType type in api.Types)
                sourceAssemblies.Add(type, rootAssembly);
        }

        if (resolution is not null)
        {
            ResolveForwardedTypes(
                api,
                apiDllPath,
                logger,
                options.IncludeAll,
                isPlatformAssembly,
                resolution: resolution,
                sourceAssemblies);
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
            sourceAssemblies);
    }

    static ResolvedAssemblyReference? SelectRootAssembly(
        string assemblyPath,
        AssemblyResolutionProvenance provenance) =>
        ResolvedAssemblyReference.SelectFromPath(assemblyPath, provenance)
            switch
            {
                AssemblyDescriptorSelectionResult.Ready ready =>
                    ready.Reference,
                AssemblyDescriptorSelectionResult.Descriptorless => null,
                AssemblyDescriptorSelectionResult.Rejected rejected =>
                    throw new BadImageFormatException(
                        $"Could not select API assembly '{assemblyPath}': "
                        + $"{rejected.Failure.Kind}: {rejected.Failure.Detail}"),
                _ => throw new System.Diagnostics.UnreachableException(),
            };

    static TypeDefinitionResolutionSession? TryCreateResolutionSession(
        ResolvedAssemblyReference rootAssembly,
        bool isPlatformAssembly,
        string? projectAssetsPath,
        string? targetFramework,
        string? platformFramework,
        string? packageExtractPath,
        NuGetSourceOptions? sourceOptions,
        bool usePackageSourcePolicy)
    {
        try
        {
            return new TypeDefinitionResolutionSession(
                rootAssembly,
                isPlatformAssembly,
                projectAssetsPath,
                targetFramework,
                platformFramework,
                packageExtractPath,
                sourceOptions,
                usePackageSourcePolicy);
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

    static ResolvedAssemblyReference? TryCreateRootAssembly(
        string assemblyPath,
        AssemblyResolutionProvenance provenance)
    {
        try
        {
            return ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                provenance);
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

    static AssemblyResolutionProvenance CreateRootProvenance(
        string? apiSource,
        string? apiVersion,
        string? packageName,
        string? selectedTfm,
        ApiOptions options,
        string? platformFramework = null)
    {
        if (string.Equals(
                apiSource,
                SourceKind.Library,
                StringComparison.Ordinal))
        {
            return AssemblyResolutionProvenance.Designated("ApiServices");
        }

        if (string.Equals(
                apiSource,
                SourceKind.Platform,
                StringComparison.Ordinal))
        {
            return AssemblyResolutionProvenance.Platform(
                platformFramework ?? options.PlatformFramework ?? "InstalledPlatform",
                apiVersion,
                "ApiServices");
        }

        if (string.Equals(
                apiSource,
                SourceKind.NuGet,
                StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(packageName)
            && !string.IsNullOrWhiteSpace(apiVersion))
        {
            return AssemblyResolutionProvenance.Package(
                packageName,
                apiVersion,
                selectedTfm,
                rid: null);
        }

        if (string.Equals(
                apiSource,
                SourceKind.Project,
                StringComparison.Ordinal))
        {
            return AssemblyResolutionProvenance.Project(
                options.ProjectPath
                    ?? options.ProjectAssetsPath
                    ?? "ApiServices",
                options.Tfm ?? selectedTfm,
                rid: null);
        }

        return AssemblyResolutionProvenance.Local("ApiServices");
    }

    internal static LoadedApiSurface? LoadPlatformApiSummary(
        string searchPath,
        string runtimeAssemblyPath,
        string? apiSource,
        string? apiVersion,
        string? selectedTfm,
        VerboseLogger logger,
        string? platformFramework = null)
    {
        logger.Log($"Extracting compact API summary from: {Path.GetFileName(searchPath)}");
        ResolvedAssemblyReference? rootAssembly =
            SelectRootAssembly(
                searchPath,
                AssemblyResolutionProvenance.Platform(
                    platformFramework ?? "InstalledPlatform",
                    apiVersion,
                    "ApiServices"));
        using Stream stream = rootAssembly is null
            ? File.OpenRead(searchPath)
            : rootAssembly.OpenRead();
        var api = AssemblyReader.ExtractApiSummarySurface(stream);
        if (api == null)
            return null;

        api.SetInspectionSourceAssemblyPath(searchPath);
        var sourceAssemblies =
            new Dictionary<ApiType, ResolvedAssemblyReference>(
                ReferenceEqualityComparer.Instance);
        if (rootAssembly is not null)
        {
            foreach (ApiType type in api.Types)
                sourceAssemblies.Add(type, rootAssembly);
        }

        if (rootAssembly is not null)
        {
            ResolveForwardedTypes(
                api,
                searchPath,
                logger,
                includeAll: false,
                isPlatformAssembly: true,
                targetFramework: selectedTfm,
                summaryOnly: true,
                summaryRootAssembly: rootAssembly,
                sourceAssemblies: sourceAssemblies);
        }

        api.Name = Path.GetFileNameWithoutExtension(searchPath);
        api.Tfm = selectedTfm;
        api.Source = apiSource;
        api.Version = apiVersion;
        api.Library = Path.GetFileName(searchPath);
        return new LoadedApiSurface(
            api,
            searchPath,
            runtimeAssemblyPath,
            sourceAssemblies,
            IsSummary: true);
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
        ResolvedAssemblyReference? summaryRootAssembly = null,
        IDictionary<ApiType, ResolvedAssemblyReference>?
            sourceAssemblies = null)
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
                summaryRootAssembly,
                sourceAssemblies);
            return;
        }

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
        TypeDefinitionResolutionSession resolution,
        IDictionary<ApiType, ResolvedAssemblyReference>?
            sourceAssemblies = null)
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
                    group.TypeTokens,
                    sourceAssemblies);
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
        IReadOnlyCollection<int>? forwardedTypeTokens = null,
        IDictionary<ApiType, ResolvedAssemblyReference>?
            sourceAssemblies = null)
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
            sourceAssemblies?.Add(type, targetAssembly);
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
        ResolvedAssemblyReference? rootAssembly,
        IDictionary<ApiType, ResolvedAssemblyReference>?
            sourceAssemblies)
    {
        TypeDefinitionResolutionSession? resolution = null;
        Dictionary<
            AssemblyAcquisitionRegistration,
            (ResolvedAssemblyReference Assembly,
                HashSet<MetadataTypeDefinitionName> Types)> byAssembly = [];
        Dictionary<
            AssemblyAcquisitionRegistration,
            ResolvedAssemblyReference> retainedAssemblies = [];
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

                resolution ??=
                    rootAssembly is null
                        ? new TypeDefinitionResolutionSession(
                            dllPath,
                            isPlatformAssembly,
                            options?.ProjectAssetsPath,
                            options?.Tfm ?? targetFramework,
                            options?.PlatformFramework)
                        : new TypeDefinitionResolutionSession(
                            rootAssembly,
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

                ResolvedAssemblyReference resolvedAssembly =
                    resolved.Definition.Assembly.Assembly;
                if (!retainedAssemblies.TryGetValue(
                        resolvedAssembly.Registration,
                        out ResolvedAssemblyReference? assembly))
                {
                    assembly =
                        CreatePlatformSummarySupplierDescriptor(
                            resolvedAssembly,
                            rootAssembly);
                    retainedAssemblies.Add(
                        resolvedAssembly.Registration,
                        assembly);
                }
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
                    using Stream stream = group.Assembly.OpenRead();
                    ApiSurface? targetApi =
                        AssemblyReader.ExtractApiSummarySurface(stream);
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
                            sourceAssemblies?.Add(
                                type,
                                group.Assembly);
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
            logger.Log(
                $"Resolved {resolvedCount} types from forwarded libraries.");
        }
    }

    static ResolvedAssemblyReference
        CreatePlatformSummarySupplierDescriptor(
            ResolvedAssemblyReference assembly,
            ResolvedAssemblyReference? rootAssembly)
    {
        if (rootAssembly?.Provenance
                is not AssemblyResolutionProvenance.PlatformAsset
                    platform
            || assembly.Provenance
                is not AssemblyResolutionProvenance.LocalAsset local
            || !local.ResolverSource.Equals(
                nameof(
                    AssemblyDependencyProvenance.SiblingAssembly),
                StringComparison.Ordinal))
        {
            return assembly;
        }

        return ResolvedAssemblyReference.Create(
            assembly.Identity,
            assembly.Path,
            assembly.OpenRead,
            platform,
            assembly.LastWriteTimeUtc);
    }

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
