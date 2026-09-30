using System.Collections.Immutable;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>
/// Collects types from multiple sources and executes typed inventory queries
/// through an invocation-owned inspection workspace.
/// </summary>
internal static class TypeSearchService
{
    /// <summary>
    /// Finds types matching one or more patterns, returning classified results with match kind and similarity.
    /// This is the primary entry point for the find command.
    /// </summary>
    public static async Task<FindSearchResult<TypeFindResult>> FindTypesAsync(
        FindOptions options,
        string[] patterns,
        VerboseLogger logger,
        HttpClient httpClient,
        CancellationToken cancellationToken = default,
        CommandContext? commandContext = null,
        PlatformFindSearchWorkspace? platformWorkspace = null,
        ExplicitFindSearchWorkspace? explicitWorkspace = null)
    {
        if (platformWorkspace is not null)
        {
            return await FindWithPlatformWorkspaceAsync(
                options,
                patterns,
                logger,
                platformWorkspace,
                cancellationToken);
        }

        AssemblySetRequest request =
            FindSourceCollector.BuildFindRequest(options);
        bool platformCatalogFailed = false;

        async Task<FindSearchResult<TypeFindResult>>
            FindWithCompatibilityAsync()
        {
            bool hasFailures = false;
            void MarkFailure() => hasFailures = true;
            if (ConfiguredPackageSearchWorkspace.IsEligible(
                    options.SourceSelection,
                    request,
                    options.Tfm,
                    resultLimit: options.Limit))
            {
                await using ConfiguredPackageSearchWorkspace? configured =
                    await ConfiguredPackageSearchWorkspace.OpenAsync(
                        httpClient,
                        request,
                        options.Tfm!,
                        logger.Log,
                        cancellationToken,
                        FindSourceCollector.CreateWorkspacePlan(options));
                if (configured is null)
                    return new([], HasFailures: true);

                async Task<List<TypeFindResult>>
                    InspectConfiguredNamespaceAsync(string pattern)
                {
                    if (!TryGetNamespaceSearchPattern(
                            pattern,
                            out NamespaceSearchPattern namespacePattern))
                    {
                        return [];
                    }

                    InspectionEnvelope<
                        PackageNamespaceDiscoveryOutcome>? inspection =
                            await configured.InspectNamespaceAsync(
                                namespacePattern.Namespace,
                                namespacePattern.Match,
                                cancellationToken);
                    if (inspection is null)
                    {
                        MarkFailure();
                        return [];
                    }

                    (
                        List<TypeFindResult> rows,
                        bool inspectionFailed) =
                            ProjectPackageNamespace(
                                inspection,
                                options,
                                namespacePattern.Pattern);
                    if (inspectionFailed)
                        MarkFailure();
                    return rows;
                }

                List<TypeFindResult> configuredResults =
                    patterns.Length == 1
                    ? await FindSinglePatternAsync(
                        patterns[0],
                        options,
                        (pattern, limit) => CollectTypesAsync(
                            options with { Limit = limit },
                            pattern,
                            logger,
                            configured,
                            MarkFailure,
                            cancellationToken,
                            findCandidates: true),
                        InspectConfiguredNamespaceAsync)
                    : await FindMultiPatternAsync(
                        patterns,
                        options,
                        (pattern, limit) => CollectTypesAsync(
                            options with { Limit = limit },
                            pattern,
                            logger,
                            configured,
                            MarkFailure,
                            cancellationToken,
                            findCandidates: true),
                        InspectConfiguredNamespaceAsync);
                return CreateSearchResult(
                    configuredResults,
                    hasFailures || platformCatalogFailed,
                    options.PackagePrefixLimitReached,
                    options.Limit);
            }

            Func<string, Task<List<TypeFindResult>>>?
                inspectImplicitNamespace = null;
            if (commandContext is not null
                && patterns.Any(pattern =>
                    CanUseImplicitPlatformNamespacePath(
                        options,
                        request,
                        pattern)))
            {
                Task<CliPlatformTypeCatalogOutcome>? catalogTask = null;
                bool catalogFailureReported = false;
                inspectImplicitNamespace = async pattern =>
                {
                    if (!TryGetNamespaceSearchPattern(
                            pattern,
                            out NamespaceSearchPattern namespacePattern))
                    {
                        return [];
                    }

                    CliPlatformTypeCatalogOutcome catalogOutcome =
                        await (catalogTask ??=
                            PlatformTypeCatalogRouting.LoadAsync(
                                    commandContext,
                                    options.SourceOptions ?? new(),
                                    cancellationToken)
                                .AsTask());
                    if (catalogOutcome
                        is not CliPlatformTypeCatalogOutcome.Completed
                            completed)
                    {
                        platformCatalogFailed = true;
                        var failure =
                            (CliPlatformTypeCatalogOutcome.NotCompleted)
                                catalogOutcome;
                        if (!catalogFailureReported)
                        {
                            catalogFailureReported = true;
                            CommandError.WriteWarning(
                                "Platform namespace discovery could not use "
                                    + "the PlatformHouse type catalog "
                                    + $"({failure.Kind}); continuing with "
                                    + "compatibility search.");
                        }
                        return [];
                    }

                    (
                        List<TypeFindResult> rows,
                        bool inspectionFailed) =
                            await FindImplicitNamespaceAsync(
                                options,
                                completed.Catalog,
                                namespacePattern,
                                logger,
                                httpClient,
                                cancellationToken);
                    platformCatalogFailed |= inspectionFailed;
                    return rows;
                };
            }

            FindSearchResult<TypeFindResult> legacy =
                await FindWithLegacyAsync(
                    options,
                    patterns,
                    logger,
                    httpClient,
                    inspectImplicitNamespace,
                    cancellationToken,
                    explicitWorkspace);
            return platformCatalogFailed
                ? legacy with { HasFailures = true }
                : legacy;
        }

        if (ConfiguredDeclarationLocatorWorkspace.IsEligible(
                request,
                options.Tfm))
        {
            List<TypeFindResult> located;
            bool locatorHasFailures;
            bool locatorHasLoadFailures;
            IReadOnlyList<
                InspectionEnvelope<TypeDeclarationLocatorSectionResult>>
                locatorInspections;
            await using (
                ConfiguredDeclarationLocatorWorkspace locator =
                    await ConfiguredDeclarationLocatorWorkspace.OpenAsync(
                        options,
                        logger,
                        httpClient,
                        cancellationToken))
            {
                if (locator.RequiresCompatibility)
                {
                    return await FindWithLegacyAsync(
                        options,
                        patterns,
                        logger,
                        httpClient,
                        cancellationToken: cancellationToken,
                        explicitWorkspace: explicitWorkspace);
                }

                located =
                    await FindWithLocatorAsync(
                        patterns,
                        options,
                        locator,
                        cancellationToken);
                locatorHasFailures = locator.HasFailures;
                locatorHasLoadFailures = locator.HasLoadFailures;
                locatorInspections = [.. locator.Inspections];
            }

            if (locatorHasLoadFailures
                || located.Any(
                    static row =>
                        row.Location is not null
                        && string.IsNullOrEmpty(row.Kind)))
            {
                FindSearchResult<TypeFindResult> compatibility =
                    await FindWithCompatibilityAsync();
                return compatibility with
                {
                    LocatorInspections = locatorInspections,
                };
            }

            FindSearchResult<TypeFindResult> search =
                CreateSearchResult(
                    located,
                    locatorHasFailures || platformCatalogFailed,
                    options.PackagePrefixLimitReached,
                    options.Limit);
            return search with
            {
                LocatorInspections = locatorInspections,
            };
        }

        return await FindWithCompatibilityAsync();
    }

    private static async Task<List<TypeFindResult>> FindWithLocatorAsync(
            string[] patterns,
            FindOptions options,
            ConfiguredDeclarationLocatorWorkspace workspace,
            CancellationToken cancellationToken)
    {
        TypeDeclarationLocatorSectionResult censusSection =
            await workspace.LocateAsync(
                ["*"],
                cancellationToken);
        if (censusSection
            is not TypeDeclarationLocatorSectionResult.Evaluated census)
        {
            return [];
        }

        TypeDeclarationLocatorSectionAnswer answer = census.Answers[0];
        List<TypeSearchResult> candidates =
        [
            .. InFindSourceOrder(
                ProjectCandidates(
                    answer.Candidates,
                    options.TypeFilter,
                    options.IncludeAll,
                    workspace)),
        ];
        return ClassifyPatterns(
            patterns,
            candidates,
            options.Limit,
            answer.IsComplete,
            options.TypeMatchIntent);
    }

    private static IOrderedEnumerable<TypeSearchResult>
        InFindSourceOrder(IEnumerable<TypeSearchResult> candidates) =>
            candidates
                .OrderBy(
                    static candidate =>
                        candidate.Location?.Observation.ContextOrder
                        ?? int.MaxValue)
                .ThenBy(
                    static candidate =>
                        candidate.Location?.Observation.MemberOrder
                        ?? int.MaxValue)
                .ThenBy(
                    static candidate =>
                        candidate.Location?.DeclarationOrder
                        ?? int.MaxValue);

    private static List<TypeSearchResult> ProjectCandidates(
        ImmutableArray<TypeDeclarationLocatorSectionCandidate> candidates,
        string? typeFilter,
        bool includeAll,
        ConfiguredDeclarationLocatorWorkspace workspace)
    {
        Dictionary<MetadataTypeDefinitionName, AssemblyTypeDefinitionKind?>
            definitionKinds =
                candidates
                    .Where(
                        static candidate =>
                            candidate.DefinitionKind is not null)
                    .GroupBy(static candidate => candidate.Name)
                    .ToDictionary(
                        static group => group.Key,
                        static group =>
                        {
                            AssemblyTypeDefinitionKind[] kinds =
                            [
                                .. group
                                    .Select(
                                        static candidate =>
                                            candidate.DefinitionKind!.Value)
                                    .Distinct(),
                            ];
                            return kinds.Length == 1
                                ? (AssemblyTypeDefinitionKind?)kinds[0]
                                : null;
                        });
        Dictionary<
            MetadataTypeDefinitionName,
            TypeDeclarationDiscoveryAttributes?> discoveryAttributes =
                candidates
                    .Where(
                        static candidate =>
                            candidate.DiscoveryAttributes is not null)
                    .GroupBy(static candidate => candidate.Name)
                    .ToDictionary(
                        static group => group.Key,
                        static group =>
                        {
                            TypeDeclarationDiscoveryAttributes[] attributes =
                            [
                                .. group
                                    .Select(
                                        static candidate =>
                                            candidate.DiscoveryAttributes!)
                                    .Distinct(),
                            ];
                            return attributes.Length == 1
                                ? attributes[0]
                                : null;
                        });
        var results = new List<TypeSearchResult>(candidates.Length);
        foreach (TypeDeclarationLocatorSectionCandidate candidate
            in candidates)
        {
            if (candidate.DeclarationKind
                is not AssemblyTypeDeclarationKind.Definition)
            {
                continue;
            }

            string fullName =
                candidate.Name.ToMetadataFullName();
            if (typeFilter is not null
                && !TypeMatcher.MatchesTypeFilter(
                    fullName,
                    typeFilter))
            {
                continue;
            }

            string typeName =
                candidate.Name.Segments.Length == 1
                    ? candidate.Name.Segments[0]
                    : string.Join(
                        ".",
                        candidate.Name.Segments);
            (string source, string? version) =
                candidate.Observation.Realization switch
                {
                    TypeDeclarationLocatorRealization.PackageRealization
                        package =>
                        (workspace.SourceFor(candidate), package.Version),
                    TypeDeclarationLocatorRealization.PlatformRealization
                        platform =>
                        (workspace.SourceFor(candidate), platform.Version),
                    _ => ("", null),
                };
            AssemblyTypeDefinitionKind? definitionKind =
                candidate.DefinitionKind
                ?? definitionKinds.GetValueOrDefault(candidate.Name);
            TypeDeclarationDiscoveryAttributes? attributes =
                candidate.DiscoveryAttributes
                ?? discoveryAttributes.GetValueOrDefault(candidate.Name);
            if (TypeFilters.IsCompilerGenerated(
                    candidate.Name.Segments[^1])
                || (!includeAll
                    && candidate.DeclarationKind
                        is AssemblyTypeDeclarationKind.Definition
                    && candidate.IsDefinitionPublic is not true)
                || (!includeAll
                && attributes
                    is { IsEditorBrowsableNever: true }
                        or { IsObsolete: true }))
            {
                continue;
            }
            results.Add(
                new TypeSearchResult
                {
                    TypeName = typeName,
                    Namespace = candidate.Name.Namespace,
                    FullName = fullName,
                    Kind = !includeAll && attributes is null
                        ? ""
                        : DisplayTypeKind(definitionKind),
                    Assembly = candidate.Observation.Selection switch
                    {
                        TypeDeclarationLocatorSelection.PackageSelection
                        {
                            AssetPath: { Length: > 0 } assetPath,
                        } =>
                            Path.GetFileNameWithoutExtension(assetPath),
                        _ =>
                            candidate.Observation.AssemblyIdentity.Name,
                    },
                    Source = source,
                    SourceVersion = version,
                    Location = candidate,
                });
        }

        return results;
    }

    private static string DisplayTypeKind(
        AssemblyTypeDefinitionKind? kind) =>
        kind switch
        {
            AssemblyTypeDefinitionKind.Class => "class",
            AssemblyTypeDefinitionKind.Interface => "interface",
            AssemblyTypeDefinitionKind.ValueType => "struct",
            AssemblyTypeDefinitionKind.Enum => "enum",
            AssemblyTypeDefinitionKind.Delegate => "delegate",
            null => "",
            _ => throw new InvalidOperationException(
                "Unknown assembly type-definition kind."),
        };

    private static void AddClassifiedResults(
            List<TypeFindResult> results,
            string pattern,
            TypeFindMatchKind match,
            IEnumerable<TypeSearchResult> candidates)
    {
        foreach (TypeSearchResult candidate in candidates)
        {
            results.Add(
                ToFindResult(
                    pattern,
                    match,
                    similarity: 1.0,
                    candidate));
        }
    }

    private static TypeFindResult ToFindResult(
            string pattern,
            TypeFindMatchKind match,
            double? similarity,
            TypeSearchResult candidate) =>
            new()
            {
                Pattern = pattern,
                Match = match,
                Similarity = similarity,
                Type = candidate.TypeName,
                Namespace = candidate.Namespace ?? "",
                FullName = candidate.FullName,
                Kind = candidate.Kind ?? "",
                Library = candidate.Assembly ?? "",
                Source = candidate.Source ?? "",
                SourceVersion = candidate.SourceVersion,
                Location = candidate.Location,
            };

    private static bool CanUseImplicitPlatformNamespacePath(
        FindOptions options,
        AssemblySetRequest request,
        string pattern) =>
        !options.IncludeAll
        && request.Packages.Count == 0
        && request.Assemblies.Count == 0
        && request.PlatformAssemblies.Count == 0
        && request.PlatformFrameworks.Count > 0
        && request.Projects.Count == 0
        && request.Directories.Count == 0
        && (options.SourceSelection is null
            || options.SourceSelection.UsesImplicitPlatform)
        && TryGetNamespaceSearchPattern(
            pattern,
            out _);

    private static async Task<(List<TypeFindResult> Rows, bool HasFailures)>
        FindImplicitNamespaceAsync(
            FindOptions options,
            PlatformTypeCatalog catalog,
            NamespaceSearchPattern pattern,
            VerboseLogger logger,
            HttpClient httpClient,
            CancellationToken cancellationToken)
    {
        (
            List<TypeFindResult> packageRows,
            bool packageFailures) =
                await FindPrunedPackageNamespaceAsync(
                    options,
                    catalog,
                    pattern,
                    logger,
                    httpClient,
                    cancellationToken);
        List<TypeFindResult> rows =
        [
            .. packageRows,
            .. ProjectPlatformNamespace(
                catalog,
                pattern,
                options.TypeFilter,
                cancellationToken),
        ];
        if (options.Limit is { } limit)
            rows = [.. rows.Take(limit)];
        return (rows, packageFailures);
    }

    private static List<TypeFindResult> ProjectPlatformNamespace(
        PlatformTypeCatalog catalog,
        NamespaceSearchPattern pattern,
        string? typeFilter,
        CancellationToken cancellationToken)
    {
        InspectionEnvelope<PlatformNamespaceDiscoveryOutcome> inspection =
            PlatformNamespaceDiscoveryInspection.Execute(
                catalog,
                new(
                    pattern.Namespace,
                    pattern.Match),
                cancellationToken);
        if (inspection.Content
            is not PlatformNamespaceDiscoveryOutcome.Found found)
        {
            return [];
        }

        var results = new List<TypeFindResult>();
        foreach (PlatformNamespaceDiscoveryHit hit in found.Hits)
        {
            foreach (PlatformNamespaceDiscoveryDeclaration declaration
                in hit.Declarations)
            {
                if (declaration.DeclarationKind
                        is not AssemblyTypeDeclarationKind.Definition
                    || declaration.DefinitionKind is not { } definitionKind)
                {
                    continue;
                }

                string fullName = declaration.Type.ToMetadataFullName();
                if (typeFilter is not null
                    && !TypeMatcher.MatchesTypeFilter(
                        fullName,
                        typeFilter))
                {
                    continue;
                }

                results.Add(
                    new()
                    {
                        Pattern = pattern.Pattern,
                        Match = TypeFindMatchKind.Namespace,
                        Similarity = 1.0,
                        Type =
                            declaration.Type.Segments.Length == 1
                                ? declaration.Type.Segments[0]
                                : string.Join(
                                    ".",
                                    declaration.Type.Segments),
                        Namespace = declaration.Type.Namespace,
                        FullName = fullName,
                        Kind = DisplayTypeKind(definitionKind),
                        Library = hit.Library,
                        Source = PlatformSource(hit.Target.Family),
                        SourceVersion = hit.Target.Version,
                    });
            }
        }
        return results;
    }

    private static async Task<(List<TypeFindResult> Rows, bool HasFailures)>
        FindPrunedPackageNamespaceAsync(
            FindOptions options,
            PlatformTypeCatalog catalog,
            NamespaceSearchPattern pattern,
            VerboseLogger logger,
            HttpClient httpClient,
            CancellationToken cancellationToken)
    {
        ImmutableArray<string> namesakeLibraries =
            LibraryNamespaceDiscovery.NamesakeLibraryCandidates(
                pattern.Namespace);
        if (namesakeLibraries.IsDefaultOrEmpty)
            return ([], false);

        var coordinates =
            new List<(
                string PackageId,
                string Version,
                string TargetFramework)>();
        var seenCoordinates =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasFailures = false;
        foreach (var target in catalog.Entries
            .Select(static entry => entry.Member.Target)
            .DistinctBy(static target =>
                $"{target.Family}|{target.TargetFramework}|"
                    + target.Version.Value))
        {
            string? framework = PlatformSourceOrNull(target.Family);
            if (framework is null)
                continue;

            InstalledPlatformPruneSource.Result read =
                InstalledPlatformPruneSource.Read(
                    $"{framework}@{target.Version.Value}");
            if (read.Inventory is not { } inventory)
            {
                hasFailures = true;
                CommandError.WriteWarning(
                    read.Error
                    ?? $"Could not read Platform prune inventory for "
                        + $"{framework}@{target.Version.Value}.");
                continue;
            }

            foreach (string library in namesakeLibraries)
            {
                if (!inventory.TryGetEntry(
                        library,
                        out PlatformPruneEntry entry)
                    || entry.Precision is not PlatformPrunePrecision.Exact)
                {
                    continue;
                }

                string version = entry.SuppliedVersion.ToString();
                string key =
                    $"{entry.PackageId}|{version}|"
                        + inventory.TargetFramework;
                if (seenCoordinates.Add(key))
                {
                    coordinates.Add(
                        (
                            entry.PackageId,
                            version,
                            inventory.TargetFramework));
                }
            }
        }

        var rows = new List<TypeFindResult>();
        foreach (var coordinate in coordinates)
        {
            var request = new AssemblySetRequest
            {
                Packages =
                [
                    $"{coordinate.PackageId}@{coordinate.Version}",
                ],
                SourceOptions = options.SourceOptions,
            };
            await using ConfiguredPackageSearchWorkspace? workspace =
                await ConfiguredPackageSearchWorkspace.OpenAsync(
                    httpClient,
                    request,
                    coordinate.TargetFramework,
                    logger.Log,
                    cancellationToken,
                    FindSourceCollector.CreateWorkspacePlan(options));
            if (workspace is null)
            {
                hasFailures = true;
                continue;
            }

            InspectionEnvelope<PackageNamespaceDiscoveryOutcome>? inspection =
                await workspace.InspectNamespaceAsync(
                    pattern.Namespace,
                    pattern.Match,
                    cancellationToken);
            if (inspection is null)
            {
                hasFailures = true;
                continue;
            }
            (List<TypeFindResult> projected, bool inspectionFailed) =
                ProjectPackageNamespace(
                    inspection,
                    options,
                    pattern.Pattern);
            hasFailures |= inspectionFailed;
            rows.AddRange(projected);
        }

        return (rows, hasFailures);
    }

    private static (
        List<TypeFindResult> Rows,
        bool HasFailures) ProjectPackageNamespace(
            InspectionEnvelope<PackageNamespaceDiscoveryOutcome> inspection,
            FindOptions options,
            string pattern)
    {
        bool hasFailures = !inspection.Content.IsComplete;
        foreach (InspectionDiagnostic diagnostic
            in inspection.Diagnostics)
        {
            if (diagnostic.Severity
                is InspectionDiagnosticSeverity.Error)
            {
                hasFailures = true;
            }
            CommandError.WriteWarning(
                diagnostic.Correspondence is { } correspondence
                    ? $"{diagnostic.Summary} ({correspondence})"
                    : diagnostic.Summary.ToString());
        }

        var rows = new List<TypeFindResult>();
        foreach (PackageNamespaceDiscoveryHit hit
            in inspection.Content.Hits)
        {
            foreach (LibraryTypeShape declaration
                in hit.Declarations)
            {
                if (declaration.DeclarationKind
                        is not LibraryTypeDeclarationKind.Definition
                    || declaration.DefinitionKind
                        is not { } definitionKind)
                {
                    continue;
                }

                string fullName =
                    declaration.Identity.ToMetadataFullName();
                if (options.TypeFilter is not null
                    && !TypeMatcher.MatchesTypeFilter(
                        fullName,
                        options.TypeFilter))
                {
                    continue;
                }

                rows.Add(
                    new()
                    {
                        Pattern = pattern,
                        Match = TypeFindMatchKind.Namespace,
                        Similarity = 1.0,
                        Type =
                            declaration.Identity.Segments.Length == 1
                                ? declaration.Identity.Segments[0]
                                : string.Join(
                                    ".",
                                    declaration.Identity.Segments),
                        Namespace =
                            declaration.Identity.Namespace,
                        FullName = fullName,
                        Kind = DisplayTypeKind(definitionKind),
                        Library = hit.Library,
                        Source = hit.PackageId,
                        SourceVersion = hit.PackageVersion,
                    });
            }
        }

        return (rows, hasFailures);
    }

    private static string PlatformSource(PlatformFamily family) =>
        PlatformSourceOrNull(family)
        ?? throw new InvalidOperationException(
            $"Unsupported Platform family '{family}'.");

    private static string? PlatformSourceOrNull(PlatformFamily family) =>
        family switch
        {
            PlatformFamily.DotNetRuntime => "runtime",
            PlatformFamily.AspNetCore => "aspnetcore",
            _ => null,
        };

    private static string DisplayTypeKind(ApiTypeInventoryKind kind) =>
        kind switch
        {
            ApiTypeInventoryKind.Class => "class",
            ApiTypeInventoryKind.Struct => "struct",
            ApiTypeInventoryKind.Interface => "interface",
            ApiTypeInventoryKind.Enum => "enum",
            ApiTypeInventoryKind.Delegate => "delegate",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Unknown API Type kind."),
        };

    private static async Task<FindSearchResult<TypeFindResult>>
        FindWithPlatformWorkspaceAsync(
            FindOptions options,
            string[] patterns,
            VerboseLogger logger,
            PlatformFindSearchWorkspace workspace,
            CancellationToken cancellationToken)
    {
        bool hasFailures = false;
        void MarkFailure() => hasFailures = true;
        cancellationToken.ThrowIfCancellationRequested();
        List<TypeSearchResult>? completeInventory = null;

        Task<List<TypeSearchResult>> Collect(
            string? pattern,
            int? resultLimit)
        {
            if (pattern is null && completeInventory is not null)
                return Task.FromResult(completeInventory);
            if (pattern is not null && completeInventory is not null)
            {
                IEnumerable<TypeSearchResult> selected =
                    completeInventory.Where(candidate =>
                            PrepareCandidateClassification(
                                pattern,
                                candidate,
                                options.TypeMatchIntent))
                        .DistinctBy(candidate =>
                            (
                                candidate.FullName,
                                ExactNamespaceSourceIdentity(candidate)));
                if (resultLimit is int limit)
                    selected = selected.Take(limit);
                return Task.FromResult(selected.ToList());
            }

            List<TypeSearchResult> results = [];
            HashSet<(
                string FullName,
                TypeCandidateSourceIdentity Source)>? identities =
                pattern is null
                    ? null
                    : [];
            List<TypeSearchResult>? observedInventory =
                pattern is null ? null : [];
            IReadOnlyList<string> searchPatterns =
                pattern is null ? ["*"] : [pattern];
            bool ReachedLimit() =>
                pattern is not null
                && resultLimit is int limit
                && results.Count >= limit;
            TypeHeadStopper? headStopper =
                pattern is not null
                && resultLimit is int maximum
                    ? new(
                        pattern,
                        maximum,
                        options.TypeFilter,
                        options.TypeMatchIntent,
                        identities!,
                        () => results.Count)
                    : null;
            bool complete = workspace.RunTypeInventories(
                options.IncludeAll,
                entry =>
                {
                    AddTypes(
                        results,
                        searchPatterns,
                        options.TypeFilter,
                        workspace.SourceFor(entry.Subject),
                        entry,
                        logger,
                        ReachedLimit,
                        MarkFailure,
                        observedInventory,
                        findCandidates: pattern is not null,
                        identities: identities,
                        intent: options.TypeMatchIntent,
                        headStopper: headStopper);
                    headStopper?.Commit();
                },
                pattern is not null && resultLimit.HasValue
                    ? ReachedLimit
                    : null,
                headStopper is null
                    ? null
                    : headStopper.Observe);
            if (pattern is null)
                completeInventory = results;
            else if (complete)
                completeInventory = observedInventory;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(results);
        }

        List<TypeFindResult> results =
            patterns.Length == 1
                ? await FindSinglePatternAsync(
                    patterns[0],
                    options,
                    Collect)
                : await FindMultiPatternAsync(
                    patterns,
                    options,
                    Collect);
        return CreateSearchResult(
            results,
            hasFailures,
            options.PackagePrefixLimitReached,
            options.Limit);
    }

    private static async Task<FindSearchResult<TypeFindResult>>
        FindWithLegacyAsync(
            FindOptions options,
            string[] patterns,
            VerboseLogger logger,
            HttpClient httpClient,
            Func<string, Task<List<TypeFindResult>>>?
                    inspectNamespace = null,
            CancellationToken cancellationToken = default,
            ExplicitFindSearchWorkspace? explicitWorkspace = null)
    {
        if (explicitWorkspace is not null)
        {
            return await FindWithLegacyCoreAsync(
                options,
                patterns,
                logger,
                explicitWorkspace,
                inspectNamespace);
        }

        await using var ownedWorkspace =
            new ExplicitFindSearchWorkspace(
                options,
                httpClient,
                logger.Log,
                cancellationToken);
        return await FindWithLegacyCoreAsync(
            options,
            patterns,
            logger,
            ownedWorkspace,
            inspectNamespace);
    }

    private static async Task<FindSearchResult<TypeFindResult>>
        FindWithLegacyCoreAsync(
            FindOptions options,
            string[] patterns,
            VerboseLogger logger,
            ExplicitFindSearchWorkspace workspace,
            Func<string, Task<List<TypeFindResult>>>?
                inspectNamespace)
    {
        bool hasFailures = false;
        void MarkFailure() => hasFailures = true;
        Task<List<TypeSearchResult>> Collect(
            string? pattern,
            int? resultLimit) =>
            CollectTypesAsync(
                options with { Limit = resultLimit },
                pattern,
                logger,
                workspace,
                MarkFailure,
                findCandidates: true);

        // A bounded single pattern classifies during metadata traversal.
        if (patterns.Length == 1)
        {
            return CreateSearchResult(
                await FindSinglePatternAsync(
                patterns[0],
                options,
                Collect,
                inspectNamespace),
                hasFailures,
                options.PackagePrefixLimitReached,
                options.Limit);
        }

        // An unbounded multi-pattern request shares one census. A bounded
        // request evaluates pattern groups in order until its hit budget is
        // satisfied.
        return CreateSearchResult(
            await FindMultiPatternAsync(
                patterns,
                options,
                Collect,
                inspectNamespace),
            hasFailures,
            options.PackagePrefixLimitReached,
            options.Limit);
    }

    private static FindSearchResult<TypeFindResult> CreateSearchResult(
        List<TypeFindResult> results,
        bool hasFailures,
        bool sourceSelectionIncomplete,
        int? resultLimit)
    {
        string[] unmatchedPatterns =
        [
            .. results
                .Where(static result =>
                    result.Match == TypeFindMatchKind.NotFound)
                .Select(static result => result.Pattern),
        ];
        return new(
            [
                .. results.Where(static result =>
                    result.Match != TypeFindMatchKind.NotFound),
            ],
            hasFailures,
            unmatchedPatterns)
        {
            SourceSelectionIncomplete =
                sourceSelectionIncomplete,
            Completion =
                resultLimit is int limit
                && results.Count(static result =>
                    result.Match != TypeFindMatchKind.NotFound) >= limit
                    ? FindSearchCompletion.ResultLimitReached
                    : hasFailures || sourceSelectionIncomplete
                        ? FindSearchCompletion.Incomplete
                        : FindSearchCompletion.Exhausted,
        };
    }

    private static async Task<List<TypeFindResult>> FindMultiPatternAsync(
        string[] patterns,
        FindOptions options,
        Func<string?, int?, Task<List<TypeSearchResult>>> collect,
        Func<string, Task<List<TypeFindResult>>>?
            inspectNamespace = null)
    {
        if (options.Limit is int limit)
        {
            var limited = new List<TypeFindResult>();
            int matched = 0;
            foreach (string pattern in patterns)
            {
                int remaining = limit - matched;
                if (remaining <= 0)
                    break;

                List<TypeFindResult> rows =
                    await FindSinglePatternAsync(
                        pattern,
                        options with { Limit = remaining },
                        collect,
                        inspectNamespace);
                foreach (TypeFindResult row in rows)
                {
                    if (row.Match == TypeFindMatchKind.NotFound)
                    {
                        limited.Add(row);
                        continue;
                    }
                    if (matched == limit)
                        break;
                    limited.Add(row);
                    matched++;
                }
            }
            return limited;
        }

        List<TypeSearchResult> allTypes = await collect(null, null);
        return ClassifyPatterns(
            patterns,
            allTypes,
            limit: null,
            sourceComplete: true,
            intent: options.TypeMatchIntent);
    }

    private static TypeSearchResult ToSearchResult(
        TypeFindResult result) =>
        new()
        {
            TypeName = result.Type,
            Namespace = result.Namespace,
            FullName = result.FullName,
            Kind = result.Kind,
            Assembly = result.Library,
            Source = result.Source,
            SourceVersion = result.SourceVersion,
            Location = result.Location,
        };

    private static async Task<List<TypeFindResult>> FindSinglePatternAsync(
        string pattern,
        FindOptions options,
        Func<string?, int?, Task<List<TypeSearchResult>>> collect,
        Func<string, Task<List<TypeFindResult>>>? inspectNamespace = null)
    {
        if (options.TypeMatchIntent == FindTypeMatchIntent.Ordinary
            && TryGetNamespaceDescendantPattern(
                pattern,
                out NamespaceSearchPattern descendantPattern))
        {
            if (inspectNamespace is not null)
            {
                List<TypeFindResult> inspected =
                    await inspectNamespace(pattern);
                if (inspected.Count > 0)
                    return inspected;
            }

            List<TypeSearchResult> allTypes = await collect(null, null);
            List<TypeSearchResult> descendants =
            [
                .. NamespaceCandidates(
                    descendantPattern.Namespace,
                    descendantPattern.Match,
                    allTypes),
            ];
            if (options.Limit.HasValue
                && descendants.Count > options.Limit.Value)
            {
                descendants =
                [
                    .. descendants.Take(options.Limit.Value),
                ];
            }

            return ConvertToFindResults(
                descendants.Count > 0
                    ? new Dictionary<string, List<TypeSearchResult>>
                    {
                        [pattern] = descendants,
                    }
                    : [],
                [],
                descendants.Count == 0 ? [pattern] : [],
                null,
                namespacePatterns:
                    new HashSet<string>(
                        [pattern],
                        StringComparer.Ordinal));
        }

        List<TypeSearchResult> candidates =
            await collect(pattern, options.Limit);
        return ClassifyPatterns(
            [pattern],
            candidates,
            options.Limit,
            sourceComplete: options.Limit is null
                || candidates.Count < options.Limit.Value,
            intent: options.TypeMatchIntent);
    }

    private static List<TypeFindResult> ClassifyPatterns(
        IReadOnlyList<string> patterns,
        IReadOnlyList<TypeSearchResult> candidates,
        int? limit,
        bool sourceComplete,
        FindTypeMatchIntent intent)
    {
        var results = new List<TypeFindResult>();
        int accepted = 0;
        foreach (string pattern in patterns)
        {
            if (limit is int maximum && accepted >= maximum)
                break;

            int? remaining =
                limit is int bounded
                    ? bounded - accepted
                    : null;
            List<TypeFindResult> classified =
                intent == FindTypeMatchIntent.Ordinary
                && TryGetNamespaceDescendantPattern(
                    pattern,
                    out NamespaceSearchPattern namespacePattern)
                    ? ClassifyNamespaceCandidates(
                        namespacePattern,
                        candidates,
                        remaining)
                    : ClassifyCandidates(
                        pattern,
                        candidates,
                        remaining,
                        intent);
            if (classified.Count == 0)
            {
                if (sourceComplete)
                {
                    results.Add(
                        new TypeFindResult
                        {
                            Pattern = pattern,
                            Match = TypeFindMatchKind.NotFound,
                        });
                }
                continue;
            }

            results.AddRange(classified);
            accepted += classified.Count;
        }
        return results;
    }

    private static List<TypeFindResult> ClassifyNamespaceCandidates(
        NamespaceSearchPattern pattern,
        IReadOnlyList<TypeSearchResult> candidates,
        int? limit)
    {
        IEnumerable<TypeSearchResult> selected =
            NamespaceCandidates(
                pattern.Namespace,
                pattern.Match,
                candidates);
        if (limit is int maximum)
            selected = selected.Take(maximum);
        return
        [
            .. selected.Select(candidate =>
                ToFindResult(
                    pattern.Pattern,
                    TypeFindMatchKind.Namespace,
                    similarity: 1.0,
                    candidate)),
        ];
    }

    private static List<TypeFindResult> ClassifyCandidates(
        string pattern,
        IReadOnlyList<TypeSearchResult> candidates,
        int? limit,
        FindTypeMatchIntent intent)
    {
        var results = new List<TypeFindResult>();
        var identities =
            new HashSet<(
                string FullName,
                TypeCandidateSourceIdentity Source)>();
        foreach (TypeSearchResult candidate in candidates)
        {
            TypeFindResult? classified =
                CandidateClassification(
                    pattern,
                    candidate,
                    intent);
            if (classified is null
                || !identities.Add(
                    (
                        candidate.FullName,
                        ExactNamespaceSourceIdentity(candidate))))
            {
                continue;
            }

            results.Add(classified);
            if (limit is int maximum
                && results.Count >= maximum)
            {
                break;
            }
        }
        return results;
    }

    private static TypeFindResult? ClassifyCandidate(
        string pattern,
        TypeSearchResult candidate,
        FindTypeMatchIntent intent)
    {
        TypeFindMatchKind match;
        double similarity = 1.0;
        string effectivePattern = pattern;

        if (IsExactCandidate(pattern, candidate))
        {
            match = TypeFindMatchKind.Exact;
        }
        else if (intent == FindTypeMatchIntent.ExactOnly)
        {
            return null;
        }
        else if (TypeMatcher.MatchesTypeFilter(
                candidate.FullName,
                pattern))
        {
            match = TypeMatcher.IsTypeGlobPattern(pattern)
                ? TypeFindMatchKind.Glob
                : TypeFindMatchKind.Direct;
        }
        else if (IsExactNamespaceCandidate(
                     pattern,
                     candidate))
        {
            match = TypeFindMatchKind.Namespace;
        }
        else
        {
            TypeNameMatchTier? tier =
                TypeNameMatchRanking.Classify(
                    candidate.FullName,
                    pattern);
            if (tier == TypeNameMatchTier.Prefix)
            {
                match = TypeFindMatchKind.Prefix;
                if (LooksLikeNamespacePrefix(pattern))
                    effectivePattern = $"{pattern}*";
            }
            else if (tier == TypeNameMatchTier.Substring)
            {
                match = TypeFindMatchKind.Substring;
            }
            else if (IsSimilarityEligible(pattern)
                && TypeMatcher.NameSimilarity(
                    candidate.FullName,
                    pattern) is >= 0.5 and var score)
            {
                match = TypeFindMatchKind.Partial;
                similarity = score;
            }
            else
            {
                return null;
            }
        }

        return ToFindResult(
            effectivePattern,
            match,
            similarity,
            candidate);
    }

    private static bool PrepareCandidateClassification(
        string pattern,
        TypeSearchResult candidate,
        FindTypeMatchIntent intent)
    {
        candidate.ClassifiedPattern = pattern;
        candidate.ClassifiedIntent = intent;
        candidate.Classification =
            ClassifyCandidate(pattern, candidate, intent);
        return candidate.Classification is not null;
    }

    private static TypeFindResult? CandidateClassification(
        string pattern,
        TypeSearchResult candidate,
        FindTypeMatchIntent intent) =>
        string.Equals(
            candidate.ClassifiedPattern,
            pattern,
            StringComparison.Ordinal)
        && candidate.ClassifiedIntent == intent
            ? candidate.Classification
            : ClassifyCandidate(pattern, candidate, intent);

    private static bool IsExactCandidate(
        string pattern,
        TypeSearchResult candidate) =>
        !TypeMatcher.IsTypeGlobPattern(pattern)
        && (string.Equals(
                candidate.TypeName,
                pattern,
                StringComparison.Ordinal)
            || string.Equals(
                candidate.FullName,
                pattern,
                StringComparison.Ordinal));

    private static bool IsExactNamespaceCandidate(
        string pattern,
        TypeSearchResult candidate) =>
        IsExactNamespaceCandidate(
            pattern,
            candidate.Namespace);

    private static bool IsExactNamespaceCandidate(
        string pattern,
        string? @namespace) =>
        HasNamesakeLibraryCandidate(pattern)
        && string.Equals(
            @namespace,
            pattern,
            StringComparison.Ordinal);

    private static bool LooksLikeNamespacePrefix(string pattern)
        => pattern.Contains('.') && !pattern.Contains('<') && !pattern.Contains('`');

    private static bool IsSimilarityEligible(string pattern)
        => !pattern.Contains('*') && !pattern.Contains('?');

    private static bool IsNamespacePrefixEligible(string pattern)
        => IsSimilarityEligible(pattern)
            && LooksLikeNamespacePrefix(pattern);

    private static bool HasNamesakeLibraryCandidate(string pattern) =>
        !LibraryNamespaceDiscovery
            .NamesakeLibraryCandidates(pattern)
            .IsDefaultOrEmpty;

    private static bool TryGetNamespaceSearchPattern(
        string pattern,
        out NamespaceSearchPattern namespacePattern)
    {
        if (TryGetNamespaceDescendantPattern(
                pattern,
                out namespacePattern))
        {
            return true;
        }

        if (IsNamespacePrefixEligible(pattern)
            && HasNamesakeLibraryCandidate(pattern))
        {
            namespacePattern =
                new(
                    pattern,
                    pattern,
                    MetadataNamespaceMatch.Exact);
            return true;
        }

        namespacePattern = default;
        return false;
    }

    private static bool TryGetNamespaceDescendantPattern(
        string pattern,
        out NamespaceSearchPattern namespacePattern)
    {
        const string Suffix = ".*";
        if (!pattern.EndsWith(Suffix, StringComparison.Ordinal))
        {
            namespacePattern = default;
            return false;
        }

        string @namespace = pattern[..^Suffix.Length];
        if (@namespace.Contains('*')
            || @namespace.Contains('?')
            || !LooksLikeNamespacePrefix(@namespace)
            || !HasNamesakeLibraryCandidate(@namespace))
        {
            namespacePattern = default;
            return false;
        }

        namespacePattern =
            new(
                pattern,
                @namespace,
                MetadataNamespaceMatch.ExactOrDescendant);
        return true;
    }

    private static IEnumerable<TypeSearchResult> NamespaceCandidates(
        string @namespace,
        MetadataNamespaceMatch namespaceMatch,
        IEnumerable<TypeSearchResult> candidates) =>
        HasNamesakeLibraryCandidate(@namespace)
            ? candidates.Where(candidate =>
                IsInNamespace(
                    candidate,
                    @namespace,
                    namespaceMatch))
                .DistinctBy(static candidate =>
                    (
                        candidate.FullName,
                        ExactNamespaceSourceIdentity(candidate)))
            : [];

    private static bool IsInNamespace(
        TypeSearchResult candidate,
        string @namespace,
        MetadataNamespaceMatch namespaceMatch)
    {
        if (candidate.Location is { } location)
        {
            return location.Name.IsInNamespace(
                @namespace,
                namespaceMatch);
        }

        return namespaceMatch switch
        {
            MetadataNamespaceMatch.Exact =>
                string.Equals(
                    candidate.Namespace,
                    @namespace,
                    StringComparison.Ordinal),
            MetadataNamespaceMatch.ExactOrDescendant =>
                string.Equals(
                    candidate.Namespace,
                    @namespace,
                    StringComparison.Ordinal)
                || candidate.Namespace?.StartsWith(
                    $"{@namespace}.",
                    StringComparison.Ordinal) is true,
            _ => throw new ArgumentOutOfRangeException(
                nameof(namespaceMatch),
                namespaceMatch,
                "Find namespace discovery supports exact or descendant matching."),
        };
    }

    private static TypeCandidateSourceIdentity
        ExactNamespaceSourceIdentity(TypeSearchResult candidate) =>
        candidate.Location is { } location
            ? new(
                location.ModuleVersionId,
                null,
                null)
            : new(
                Guid.Empty,
                candidate.AcquisitionRegistration,
                candidate.AcquisitionRegistration is null
                    ? candidate.Source?.ToUpperInvariant()
                    : null);

    private readonly record struct TypeCandidateSourceIdentity(
        Guid ModuleVersionId,
        AssemblyAcquisitionRegistration? AcquisitionRegistration,
        string? CompatibilitySource);

    private sealed class TypeHeadStopper(
        string pattern,
        int limit,
        string? typeFilter,
        FindTypeMatchIntent intent,
        IReadOnlySet<(
            string FullName,
            TypeCandidateSourceIdentity Source)> existingIdentities,
        Func<int> acceptedCount)
    {
        readonly HashSet<(
            string FullName,
            TypeCandidateSourceIdentity Source)> _provisionalIdentities = [];
        readonly Dictionary<
            (string FullName, TypeCandidateSourceIdentity Source),
            CachedTypeClassification?> _classifications = [];

        internal bool Observe(
            AssemblyContextSubject subject,
            AssemblyTypeInventoryEntry type)
        {
            if (typeFilter is not null
                && !TypeMatcher.MatchesTypeFilter(
                    type.FullName,
                    typeFilter))
            {
                return false;
            }

            var candidate = new TypeSearchResult
            {
                TypeName = type.TypeName,
                Namespace = type.Namespace,
                FullName = type.FullName,
                Kind = type.Kind,
                AcquisitionRegistration = subject.Registration,
            };
            var identity = (
                candidate.FullName,
                ExactNamespaceSourceIdentity(candidate));
            bool accepted = PrepareCandidateClassification(
                pattern,
                candidate,
                intent);
            TypeFindResult? classification =
                candidate.Classification;
            _classifications.TryAdd(
                identity,
                classification is null
                    ? null
                    : new(
                        classification.Pattern,
                        classification.Match,
                        classification.Similarity));
            if (!accepted
                || existingIdentities.Contains(identity)
                || !_provisionalIdentities.Add(identity))
            {
                return false;
            }

            return acceptedCount() + _provisionalIdentities.Count >= limit;
        }

        internal bool TryRestore(
            TypeSearchResult candidate,
            out bool accepted)
        {
            var identity = (
                candidate.FullName,
                ExactNamespaceSourceIdentity(candidate));
            if (!_classifications.Remove(
                    identity,
                    out CachedTypeClassification? classification))
            {
                accepted = false;
                return false;
            }

            candidate.ClassifiedPattern = pattern;
            candidate.ClassifiedIntent = intent;
            candidate.Classification =
                classification is { } cached
                    ? ToFindResult(
                        cached.Pattern,
                        cached.Match,
                        cached.Similarity,
                        candidate)
                    : null;
            accepted = classification is not null;
            return true;
        }

        internal void Commit()
        {
            _provisionalIdentities.Clear();
        }
    }

    private readonly record struct CachedTypeClassification(
        string Pattern,
        TypeFindMatchKind Match,
        double? Similarity);

    private readonly record struct NamespaceSearchPattern(
        string Pattern,
        string Namespace,
        MetadataNamespaceMatch Match);

    /// <summary>
    /// Converts separate result dictionaries into a unified flat list of TypeFindResult.
    /// </summary>
    private static List<TypeFindResult> ConvertToFindResults(
        Dictionary<string, List<TypeSearchResult>> exactMatches,
        Dictionary<string, List<TypeSearchResult>> partialMatches,
        List<string> notFoundPatterns,
        Dictionary<string, Dictionary<string, double>>?
            similarityByPattern = null,
        IReadOnlySet<string>? namespacePatterns = null,
        IReadOnlyDictionary<string, TypeFindMatchKind>? kindByPattern = null)
    {
        var results = new List<TypeFindResult>();

        foreach (var (pattern, types) in exactMatches)
        {
            TypeFindMatchKind match =
                kindByPattern is not null
                    && kindByPattern.TryGetValue(
                        pattern,
                        out TypeFindMatchKind broadenedKind)
                    ? broadenedKind
                    : namespacePatterns?.Contains(pattern) is true
                        ? TypeFindMatchKind.Namespace
                        : TypeMatcher.IsTypeGlobPattern(pattern)
                            ? TypeFindMatchKind.Glob
                            : TypeFindMatchKind.Direct;
            foreach (var t in types)
            {
                results.Add(new TypeFindResult
                {
                    Pattern = pattern,
                    Match = match,
                    Similarity = 1.0,
                    Type = t.TypeName,
                    Namespace = t.Namespace ?? "",
                    FullName = t.FullName,
                    Kind = t.Kind ?? "",
                    Library = t.Assembly ?? "",
                    Source = t.Source ?? "",
                    SourceVersion = t.SourceVersion,
                    Location = t.Location,
                });
            }
        }

        foreach (var (pattern, types) in partialMatches)
        {
            var simDict = similarityByPattern?.GetValueOrDefault(pattern);
            foreach (var t in types)
            {
                var similarity = simDict?.GetValueOrDefault(t.FullName, 0.5) ?? 0.5;
                results.Add(new TypeFindResult
                {
                    Pattern = pattern,
                    Match = TypeFindMatchKind.Partial,
                    Similarity = similarity,
                    Type = t.TypeName,
                    Namespace = t.Namespace ?? "",
                    FullName = t.FullName,
                    Kind = t.Kind ?? "",
                    Library = t.Assembly ?? "",
                    Source = t.Source ?? "",
                    SourceVersion = t.SourceVersion,
                    Location = t.Location,
                });
            }
        }

        foreach (var pattern in notFoundPatterns)
        {
            results.Add(new TypeFindResult
            {
                Pattern = pattern,
                Match = TypeFindMatchKind.NotFound,
                Similarity = null
            });
        }

        return results;
    }

    /// <summary>
    /// Collects types from all configured sources, optionally filtered by pattern.
    /// </summary>
    public static async Task<List<TypeSearchResult>> CollectTypesAsync(
        FindOptions options,
        string? pattern,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        await using var workspace =
            new ExplicitFindSearchWorkspace(
                options,
                httpClient,
                logger.Log,
                CancellationToken.None);
        return await CollectTypesAsync(
            options,
            pattern,
            logger,
            workspace,
            static () => { },
            findCandidates: false);
    }

    private static async Task<List<TypeSearchResult>> CollectTypesAsync(
        FindOptions options,
        string? pattern,
        VerboseLogger logger,
        ExplicitFindSearchWorkspace workspace,
        Action markFailure,
        bool findCandidates)
    {
        List<TypeSearchResult> results = [];
        HashSet<(
            string FullName,
            TypeCandidateSourceIdentity Source)>? identities =
            findCandidates
                ? []
                : null;

        // A specific pattern filters each typed inventory during collection; a
        // null pattern enumerates every type for callers that match later.
        IReadOnlyList<string> searchPatterns = pattern is null ? ["*"] : [pattern];
        bool ReachedLimit() =>
            pattern is not null
            && options.Limit.HasValue
            && results.Count >= options.Limit.Value;
        TypeHeadStopper? headStopper =
            findCandidates
            && pattern is not null
            && options.Limit is int limit
                ? new(
                    pattern,
                    limit,
                    options.TypeFilter,
                    options.TypeMatchIntent,
                    identities!,
                    () => results.Count)
                : null;

        await workspace.RunPerAssemblyAsync(
            AssemblyContextTypeInventoryQuery.Definition,
            group =>
            {
                var entries = ImmutableArray.CreateBuilder<
                    AssemblyContextEntry<AssemblyTypeInventory>>();
                AssemblyContextTypeInventoryQuery.ExecuteEach(
                    group,
                    options.IncludeAll,
                    entries.Add,
                    stop: null,
                    stopAfterType:
                        headStopper is null
                            ? null
                            : headStopper.Observe);
                return new(entries.ToImmutable());
            },
            (assembly, entry) =>
            {
                AddTypes(
                    results,
                    searchPatterns,
                    options.TypeFilter,
                    SearchAssemblySource.FromAssemblySet(assembly),
                    entry,
                    logger,
                    ReachedLimit,
                    markFailure,
                    findCandidates: findCandidates,
                    identities: identities,
                    intent: options.TypeMatchIntent,
                    headStopper: headStopper);
                headStopper?.Commit();
            },
            (assembly, failure) =>
            {
                markFailure();
                CommandError.WriteWarning(
                    $"Could not read {assembly.Path}: {failure}");
            },
            markFailure,
            pattern is not null && options.Limit.HasValue
                ? ReachedLimit
                : null);
        return results;
    }

    private static async Task<List<TypeSearchResult>> CollectTypesAsync(
        FindOptions options,
        string? pattern,
        VerboseLogger logger,
        ConfiguredPackageSearchWorkspace workspace,
        Action markFailure,
        CancellationToken cancellationToken,
        bool findCandidates)
    {
        List<TypeSearchResult> results = [];
        HashSet<(
            string FullName,
            TypeCandidateSourceIdentity Source)>? identities =
            findCandidates
                ? []
                : null;
        IReadOnlyList<string> searchPatterns =
            pattern is null ? ["*"] : [pattern];
        TypeHeadStopper? headStopper =
            findCandidates
            && pattern is not null
            && options.Limit is int limit
                ? new(
                    pattern,
                    limit,
                    options.TypeFilter,
                    options.TypeMatchIntent,
                    identities!,
                    static () => 0)
                : null;
        ConfiguredPackageSearchQueryResult<
            AssemblyContextResult<AssemblyTypeInventory>>? execution =
                await workspace.QuerySurfaceAsync(
                    context =>
                    {
                        var entries = ImmutableArray.CreateBuilder<
                            AssemblyContextEntry<AssemblyTypeInventory>>();
                        AssemblyContextTypeInventoryQuery.ExecuteEach(
                            context.Group,
                            options.IncludeAll,
                            entries.Add,
                            stopAfterType:
                                headStopper is null
                                    ? null
                                    : headStopper.Observe);
                        return new AssemblyContextResult<
                            AssemblyTypeInventory>(
                                entries.ToImmutable());
                    },
                    cancellationToken);
        if (execution is null)
        {
            markFailure();
            return results;
        }
        if (execution.Sources is not { } sources
            || execution.Result is not { } queryResult)
        {
            return results;
        }

        foreach (AssemblyContextEntry<AssemblyTypeInventory> entry
            in queryResult.Assemblies)
        {
            AddTypes(
                results,
                searchPatterns,
                options.TypeFilter,
                sources.SourceFor(entry.Subject),
                entry,
                logger,
                static () => false,
                markFailure,
                findCandidates: findCandidates,
                identities: identities,
                intent: options.TypeMatchIntent,
                headStopper: headStopper);
        }
        return results;
    }

    private static void AddTypes(
        List<TypeSearchResult> results,
        IReadOnlyList<string> searchPatterns,
        string? typeFilter,
        SearchAssemblySource assembly,
        AssemblyContextEntry<AssemblyTypeInventory> entry,
        VerboseLogger logger,
        Func<bool> reachedLimit,
        Action markFailure,
        List<TypeSearchResult>? inventory = null,
        bool findCandidates = false,
        HashSet<(
            string FullName,
            TypeCandidateSourceIdentity Source)>? identities = null,
        FindTypeMatchIntent intent = FindTypeMatchIntent.Ordinary,
        TypeHeadStopper? headStopper = null)
    {
        switch (entry)
        {
            case AssemblyContextEntry<
                AssemblyTypeInventory>.Available available:
                foreach (AssemblyTypeInventoryEntry type
                    in available.Value.Types)
                {
                    if (typeFilter is not null
                        && !TypeMatcher.MatchesTypeFilter(
                            type.FullName,
                            typeFilter))
                    {
                        continue;
                    }

                    var result = new TypeSearchResult
                    {
                        TypeName = type.TypeName,
                        Namespace = type.Namespace,
                        FullName = type.FullName,
                        Kind = type.Kind,
                        Assembly = assembly.Library,
                        Source = assembly.Source,
                        SourceVersion = assembly.SourceVersion,
                        AcquisitionRegistration =
                            available.Subject.Registration,
                    };
                    bool matches;
                    if (findCandidates)
                    {
                        if (headStopper?.TryRestore(
                                result,
                                out matches) is not true)
                        {
                            matches = searchPatterns.Any(searchPattern =>
                                PrepareCandidateClassification(
                                    searchPattern,
                                    result,
                                    intent));
                        }
                    }
                    else
                    {
                        matches = searchPatterns.Any(searchPattern =>
                            TypeMatcher.MatchesTypeFilter(
                                type.FullName,
                                searchPattern));
                    }
                    if (!matches && inventory is null)
                        continue;

                    inventory?.Add(result);
                    if (!matches)
                        continue;
                    if (identities is not null
                        && !identities.Add(
                            (
                                result.FullName,
                                ExactNamespaceSourceIdentity(result))))
                    {
                        continue;
                    }

                    results.Add(result);
                    if (reachedLimit())
                        break;
                }
                WriteInspectionFailures(
                    assembly,
                    available.Value.InspectionFailures,
                    logger,
                    markFailure);
                break;
            case AssemblyContextEntry<
                AssemblyTypeInventory>.Rejected rejected:
                markFailure();
                CommandError.WriteWarning(
                    $"Could not read {assembly.DiagnosticSubject}: "
                    + rejected.Failure.Detail);
                break;
            case AssemblyContextEntry<
                AssemblyTypeInventory>.Failed failed:
                markFailure();
                CommandError.WriteWarning(
                    $"Could not read {assembly.DiagnosticSubject}: "
                    + failed.Error.Message);
                break;
        }
    }

    private static void WriteInspectionFailures(
        SearchAssemblySource assembly,
        ImmutableArray<ApiSurfaceInspectionFailure> failures,
        VerboseLogger logger,
        Action markFailure)
    {
        if (failures.IsEmpty)
            return;

        markFailure();
        CommandError.WriteWarning(
            $"Type search in {assembly.DiagnosticSubject} skipped "
            + $"{failures.Length} metadata row(s).");
        foreach (ApiSurfaceInspectionFailure failure in failures)
        {
            logger.LogWarning(
                $"Type search rejected {failure.Operation} at 0x{failure.SubjectToken:X8}: "
                + $"{failure.Kind}: {failure.Detail}");
        }
    }
}
