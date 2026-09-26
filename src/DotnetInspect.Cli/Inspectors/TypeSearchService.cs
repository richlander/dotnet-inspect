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
        CommandContext? commandContext = null)
    {
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
                        pattern => CollectTypesAsync(
                            options,
                            pattern,
                            logger,
                            configured,
                            MarkFailure,
                            cancellationToken),
                        InspectConfiguredNamespaceAsync)
                    : await FindMultiPatternAsync(
                        patterns,
                        options,
                        pattern => CollectTypesAsync(
                            options,
                            pattern,
                            logger,
                            configured,
                            MarkFailure,
                            cancellationToken),
                        InspectConfiguredNamespaceAsync);
                return CreateSearchResult(
                    configuredResults,
                    hasFailures || platformCatalogFailed,
                    options.PackagePrefixLimitReached);
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
                    inspectImplicitNamespace);
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
                        httpClient);
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
                    options.PackagePrefixLimitReached);
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
        TypeDeclarationLocatorSectionResult directSection =
            await workspace.LocateAsync(
                patterns,
                cancellationToken);
        if (directSection
            is not TypeDeclarationLocatorSectionResult.Evaluated direct)
        {
            return [];
        }

        var primaryResults =
            new List<TypeFindResult>?[patterns.Length];
        var deferredResults = new List<TypeFindResult>();
        var misses =
            new List<(
                int Index,
                string Pattern,
                bool DirectComplete)>();
        for (int index = 0; index < patterns.Length; index++)
        {
            string pattern = patterns[index];
            List<TypeSearchResult> candidates =
            [
                .. InFindSourceOrder(
                    ProjectCandidates(
                        direct.Answers[index].Candidates,
                        options.TypeFilter,
                        options.IncludeAll,
                        workspace)),
            ];
            if (TryGetNamespaceDescendantPattern(
                    pattern,
                    out NamespaceSearchPattern namespacePattern))
            {
                candidates =
                [
                    .. NamespaceCandidates(
                        namespacePattern.Namespace,
                        namespacePattern.Match,
                        candidates),
                ];
                if (options.Limit is { } namespaceLimit
                    && candidates.Count > namespaceLimit)
                {
                    candidates = [.. candidates.Take(namespaceLimit)];
                }

                if (candidates.Count == 0)
                {
                    misses.Add(
                        (index, pattern, direct.Answers[index].IsComplete));
                    continue;
                }

                var classifiedNamespace = new List<TypeFindResult>();
                AddClassifiedResults(
                    classifiedNamespace,
                    pattern,
                    TypeFindMatchKind.Namespace,
                    candidates);
                primaryResults[index] = classifiedNamespace;
                continue;
            }

            if (options.Limit is { } directLimit
                && candidates.Count > directLimit)
            {
                candidates = [.. candidates.Take(directLimit)];
            }

            if (candidates.Count == 0)
            {
                misses.Add(
                    (index, pattern, direct.Answers[index].IsComplete));
                continue;
            }

            var classified = new List<TypeFindResult>();
            AddClassifiedResults(
                classified,
                pattern,
                TypeMatcher.IsTypeGlobPattern(pattern)
                    ? TypeFindMatchKind.Glob
                    : TypeFindMatchKind.Direct,
                candidates);
            primaryResults[index] = classified;
        }

        var prefixRequests =
            misses
                .Where(static miss =>
                    IsPrefixFallbackEligible(miss.Pattern))
                .Select(static miss => $"{miss.Pattern}*")
                .ToArray();
        TypeDeclarationLocatorSectionResult.Evaluated? prefixSection = null;
        if (prefixRequests.Length > 0)
        {
            prefixSection =
                await workspace.LocateAsync(
                    prefixRequests,
                    cancellationToken)
                as TypeDeclarationLocatorSectionResult.Evaluated;
        }

        var prefixes = new Dictionary<string, List<TypeSearchResult>>(
            StringComparer.Ordinal);
        var prefixCompletion = new Dictionary<string, bool>(
            StringComparer.Ordinal);
        if (prefixSection is not null)
        {
            for (int index = 0; index < prefixRequests.Length; index++)
            {
                prefixes[prefixRequests[index]] =
                    ProjectCandidates(
                        prefixSection.Answers[index].Candidates,
                        options.TypeFilter,
                        options.IncludeAll,
                        workspace);
                prefixCompletion[prefixRequests[index]] =
                    prefixSection.Answers[index].IsComplete;
            }
        }

        bool needsCensus =
            misses.Any(
                miss =>
                    IsCompatibilityFallbackEligible(miss.Pattern)
                    && (!LooksLikeNamespacePrefix(miss.Pattern)
                        || !prefixes.TryGetValue(
                            $"{miss.Pattern}*",
                            out List<TypeSearchResult>? candidates)
                        || candidates.Count == 0));
        TypeDeclarationLocatorSectionResult.Evaluated? censusSection = null;
        if (needsCensus)
        {
            censusSection =
                await workspace.LocateAsync(
                    ["*"],
                    cancellationToken)
                as TypeDeclarationLocatorSectionResult.Evaluated;
        }

        TypeDeclarationLocatorSectionAnswer? censusAnswer =
            censusSection?.Answers[0];
        List<TypeSearchResult> census =
            censusAnswer is not null
                ? ProjectCandidates(
                    censusAnswer.Candidates,
                    options.TypeFilter,
                    options.IncludeAll,
                    workspace)
                : [];
        List<TypeSearchResult> orderedCensus =
            [.. InFindSourceOrder(census)];
        List<string> typeNames =
            orderedCensus
                .Select(static candidate => candidate.FullName)
                .Distinct(StringComparer.Ordinal)
                .ToList();

        foreach ((int patternIndex, string pattern, bool directComplete)
            in misses)
        {
            bool usesPrefixFallback =
                IsPrefixFallbackEligible(pattern);
            if (usesPrefixFallback
                && HasNamesakeLibraryCandidate(pattern)
                && prefixes.TryGetValue(
                    $"{pattern}*",
                    out List<TypeSearchResult>? namespaceCandidates))
            {
                IEnumerable<TypeSearchResult> exactNamespace =
                    NamespaceCandidates(
                        pattern,
                        MetadataNamespaceMatch.Exact,
                        InFindSourceOrder(namespaceCandidates));
                if (options.Limit is { } namespaceLimit)
                    exactNamespace = exactNamespace.Take(namespaceLimit);
                List<TypeSearchResult> selected =
                    exactNamespace.ToList();
                if (selected.Count > 0)
                {
                    var classified = new List<TypeFindResult>();
                    AddClassifiedResults(
                        classified,
                        pattern,
                        TypeFindMatchKind.Namespace,
                        selected);
                    primaryResults[patternIndex] = classified;
                    continue;
                }
            }

            if (usesPrefixFallback
                && prefixes.TryGetValue(
                    $"{pattern}*",
                    out List<TypeSearchResult>? prefixCandidates)
                && prefixCandidates.Count > 0)
            {
                string prefixPattern = $"{pattern}*";
                CommandError.WriteNote(
                    $"No exact matches for '{pattern}'. Showing prefix "
                    + $"matches for '{prefixPattern}'.");
                IEnumerable<TypeSearchResult> selected =
                    InFindSourceOrder(prefixCandidates).DistinctBy(
                        static candidate => candidate.FullName);
                if (options.Limit is { } prefixLimit)
                    selected = selected.Take(prefixLimit);
                var classified = new List<TypeFindResult>();
                AddClassifiedResults(
                    classified,
                    prefixPattern,
                    TypeFindMatchKind.Glob,
                    selected);
                primaryResults[patternIndex] = classified;
                continue;
            }

            if (IsCompatibilityFallbackEligible(pattern))
            {
                var suggestions =
                    TypeMatcher.FindClosest(
                            typeNames,
                            pattern,
                            minSimilarity: 0.5,
                            maxResults: 5)
                        .ToList();
                if (suggestions.Count > 0)
                {
                    Dictionary<string, double> similarities =
                        suggestions.ToDictionary(
                            static suggestion => suggestion.Name,
                            static suggestion => suggestion.Similarity);
                    HashSet<string> names =
                        suggestions.Select(static suggestion => suggestion.Name)
                            .ToHashSet(StringComparer.Ordinal);
                    foreach (TypeSearchResult candidate
                        in orderedCensus
                            .Where(
                                candidate => names.Contains(
                                    candidate.FullName))
                            .DistinctBy(
                                static candidate => candidate.FullName))
                    {
                        deferredResults.Add(
                            ToFindResult(
                                pattern,
                                TypeFindMatchKind.Partial,
                                similarities[candidate.FullName],
                                candidate));
                    }
                    continue;
                }
            }

            bool prefixIsComplete =
                !usesPrefixFallback
                || prefixCompletion.GetValueOrDefault(
                    $"{pattern}*");
            bool patternNeedsCensus =
                IsCompatibilityFallbackEligible(pattern)
                && (!LooksLikeNamespacePrefix(pattern)
                    || !prefixes.TryGetValue(
                        $"{pattern}*",
                        out List<TypeSearchResult>? candidates)
                    || candidates.Count == 0);
            bool censusIsComplete =
                !patternNeedsCensus
                || censusAnswer?.IsComplete is true;
            if (directComplete
                && prefixIsComplete
                && censusIsComplete)
            {
                deferredResults.Add(
                    new TypeFindResult
                    {
                        Pattern = pattern,
                        Match = TypeFindMatchKind.NotFound,
                    });
            }
        }

        var primaryResultsByEffectivePattern =
            new Dictionary<string, List<TypeFindResult>>(
                StringComparer.Ordinal);
        foreach (List<TypeFindResult>? rows in primaryResults)
        {
            if (rows is null || rows.Count == 0)
                continue;

            primaryResultsByEffectivePattern[rows[0].Pattern] = rows;
        }

        return
        [
            .. primaryResultsByEffectivePattern.Values.SelectMany(
                static rows => rows),
            .. deferredResults,
        ];
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
        FindWithLegacyAsync(
            FindOptions options,
            string[] patterns,
            VerboseLogger logger,
            HttpClient httpClient,
            Func<string, Task<List<TypeFindResult>>>?
                inspectNamespace = null)
    {
        bool hasFailures = false;
        void MarkFailure() => hasFailures = true;
        await using var workspace =
            new AssemblySetInspectionWorkspace(
                FindSourceCollector.CreateWorkspacePlan(options));
        Task<List<TypeSearchResult>> Collect(string? pattern) =>
            CollectTypesAsync(
                options,
                pattern,
                logger,
                httpClient,
                workspace,
                MarkFailure);

        // Optimized single-pattern path: collect with filtering, then partial match if empty
        if (patterns.Length == 1)
        {
            return CreateSearchResult(
                await FindSinglePatternAsync(
                patterns[0],
                options,
                Collect,
                inspectNamespace),
                hasFailures,
                options.PackagePrefixLimitReached);
        }

        // Multi-pattern or tabular output: collect all types, then match each pattern
        return CreateSearchResult(
            await FindMultiPatternAsync(
                patterns,
                options,
                Collect,
                inspectNamespace),
            hasFailures,
            options.PackagePrefixLimitReached);
    }

    private static FindSearchResult<TypeFindResult> CreateSearchResult(
        List<TypeFindResult> results,
        bool hasFailures,
        bool sourceSelectionIncomplete)
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
        };
    }

    private static async Task<List<TypeFindResult>> FindMultiPatternAsync(
        string[] patterns,
        FindOptions options,
        Func<string?, Task<List<TypeSearchResult>>> collect,
        Func<string, Task<List<TypeFindResult>>>?
            inspectNamespace = null)
    {
        var allTypes = await collect(null);
        var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();

        Dictionary<string, List<TypeSearchResult>> resultsByPattern = [];
        Dictionary<string, List<TypeSearchResult>> partialMatchesByPattern = [];
        Dictionary<string, Dictionary<string, double>> similarityByPattern = [];
        HashSet<string> namespacePatterns =
            new(StringComparer.Ordinal);
        List<string> notFoundPatterns = [];

        foreach (var pattern in patterns)
        {
            if (TryGetNamespaceDescendantPattern(
                    pattern,
                    out NamespaceSearchPattern namespacePattern))
            {
                if (inspectNamespace is not null)
                {
                    List<TypeFindResult> inspected =
                        await inspectNamespace(pattern);
                    if (inspected.Count > 0)
                    {
                        IEnumerable<TypeFindResult> selected =
                            inspected;
                        if (options.Limit is { } inspectedLimit)
                        {
                            selected = selected.Take(inspectedLimit);
                        }
                        resultsByPattern[pattern] =
                        [
                            .. selected.Select(
                                static result =>
                                    ToSearchResult(result)),
                        ];
                        namespacePatterns.Add(pattern);
                        continue;
                    }
                }

                List<TypeSearchResult> namespaceMatches =
                [
                    .. NamespaceCandidates(
                        namespacePattern.Namespace,
                        namespacePattern.Match,
                        allTypes),
                ];
                if (options.Limit.HasValue
                    && namespaceMatches.Count > options.Limit.Value)
                {
                    namespaceMatches =
                    [
                        .. namespaceMatches.Take(options.Limit.Value),
                    ];
                }

                if (namespaceMatches.Count > 0)
                {
                    resultsByPattern[pattern] = namespaceMatches;
                    namespacePatterns.Add(pattern);
                }
                else
                {
                    notFoundPatterns.Add(pattern);
                }
                continue;
            }

            List<TypeSearchResult> matches = [];
            foreach (var type in allTypes)
            {
                if (TypeMatcher.MatchesTypeFilter(type.FullName, pattern))
                {
                    matches.Add(type);
                }
            }

            if (options.Limit.HasValue && matches.Count > options.Limit.Value)
                matches = matches.Take(options.Limit.Value).ToList();

            if (matches.Count > 0)
            {
                resultsByPattern[pattern] = matches;
            }
            else if (!pattern.Contains('*') && !pattern.Contains('?'))
            {
                List<TypeSearchResult> namespaceMatches =
                [
                    .. NamespaceCandidates(
                        pattern,
                        MetadataNamespaceMatch.Exact,
                        allTypes),
                ];
                if (options.Limit.HasValue
                    && namespaceMatches.Count > options.Limit.Value)
                {
                    namespaceMatches =
                    [
                        .. namespaceMatches.Take(options.Limit.Value),
                    ];
                }
                if (namespaceMatches.Count > 0)
                {
                    resultsByPattern[pattern] = namespaceMatches;
                    namespacePatterns.Add(pattern);
                    continue;
                }

                if (TryGetNamespacePrefixMatches(pattern, allTypes, options, out var prefixPattern, out var prefixMatches))
                {
                    CommandError.WriteNote($"No exact matches for '{pattern}'. Showing prefix matches for '{prefixPattern}'.");
                    resultsByPattern[prefixPattern] = prefixMatches;
                    continue;
                }

                var suggestions = TypeMatcher.FindClosest(typeNames, pattern, minSimilarity: 0.5, maxResults: 5).ToList();
                if (suggestions.Count > 0)
                {
                    var simDict = suggestions.ToDictionary(s => s.Name, s => s.Similarity);
                    similarityByPattern[pattern] = simDict;

                    var suggestionSet = suggestions.Select(s => s.Name).ToHashSet();
                    var partialMatches = allTypes
                        .Where(t => suggestionSet.Contains(t.FullName))
                        .DistinctBy(t => t.FullName)
                        .ToList();
                    partialMatchesByPattern[pattern] = partialMatches;
                }
                else
                {
                    notFoundPatterns.Add(pattern);
                }
            }
            else
            {
                notFoundPatterns.Add(pattern);
            }
        }

        return ConvertToFindResults(
            resultsByPattern,
            partialMatchesByPattern,
            notFoundPatterns,
            similarityByPattern,
            namespacePatterns);
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

    private static bool TryGetNamespacePrefixMatches(
        string pattern,
        List<TypeSearchResult> allTypes,
        FindOptions options,
        out string prefixPattern,
        out List<TypeSearchResult> prefixMatches)
    {
        prefixPattern = $"{pattern}*";
        prefixMatches = [];
        if (!LooksLikeNamespacePrefix(pattern))
            return false;

        var localPrefixPattern = prefixPattern;
        prefixMatches = allTypes
            .Where(t => TypeMatcher.MatchesTypeFilter(t.FullName, localPrefixPattern))
            .DistinctBy(t => t.FullName)
            .ToList();

        if (options.Limit.HasValue && prefixMatches.Count > options.Limit.Value)
            prefixMatches = prefixMatches.Take(options.Limit.Value).ToList();

        return prefixMatches.Count > 0;
    }

    private static async Task<List<TypeFindResult>> FindSinglePatternAsync(
        string pattern,
        FindOptions options,
        Func<string?, Task<List<TypeSearchResult>>> collect,
        Func<string, Task<List<TypeFindResult>>>? inspectNamespace = null)
    {
        if (TryGetNamespaceDescendantPattern(
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

            List<TypeSearchResult> allTypes = await collect(null);
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

        var results = await collect(pattern);

        List<TypeSearchResult>? partialMatches = null;
        Dictionary<string, double>? partialSimilarities = null;
        if (results.Count == 0 && !pattern.Contains('*') && !pattern.Contains('?'))
        {
            if (inspectNamespace is not null
                && HasNamesakeLibraryCandidate(pattern))
            {
                List<TypeFindResult> namespaceRows =
                    await inspectNamespace(pattern);
                if (namespaceRows.Count > 0)
                    return namespaceRows;
            }

            var allTypes = await collect(null);
            var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();

            List<TypeSearchResult> namespaceMatches =
            [
                .. NamespaceCandidates(
                    pattern,
                    MetadataNamespaceMatch.Exact,
                    allTypes),
            ];
            if (options.Limit.HasValue
                && namespaceMatches.Count > options.Limit.Value)
            {
                namespaceMatches =
                [
                    .. namespaceMatches.Take(options.Limit.Value),
                ];
            }
            if (namespaceMatches.Count > 0)
            {
                return ConvertToFindResults(
                    new Dictionary<string, List<TypeSearchResult>>
                    {
                        [pattern] = namespaceMatches,
                    },
                    [],
                    [],
                    namespacePatterns:
                        new HashSet<string>(
                            [pattern],
                            StringComparer.Ordinal));
            }

            if (TryGetNamespacePrefixMatches(pattern, allTypes, options, out var prefixPattern, out var prefixResults))
            {
                CommandError.WriteNote($"No exact matches for '{pattern}'. Showing prefix matches for '{prefixPattern}'.");
                return ConvertToFindResults(
                    new Dictionary<string, List<TypeSearchResult>> { [prefixPattern] = prefixResults },
                    [],
                    [],
                    null);
            }

            if (options.Limit.HasValue && results.Count > options.Limit.Value)
                results = results.Take(options.Limit.Value).ToList();

            var suggestions = TypeMatcher.FindClosest(typeNames, pattern, minSimilarity: 0.5, maxResults: 5).ToList();

            if (suggestions.Count > 0)
            {
                partialSimilarities = suggestions.ToDictionary(s => s.Name, s => s.Similarity);
                var suggestionSet = suggestions.Select(s => s.Name).ToHashSet();
                partialMatches = allTypes
                    .Where(t => suggestionSet.Contains(t.FullName))
                    .DistinctBy(t => t.FullName)
                    .ToList();
            }
        }

        var similarityByPattern = partialSimilarities != null
            ? new Dictionary<string, Dictionary<string, double>> { [pattern] = partialSimilarities }
            : null;

        return ConvertToFindResults(
            new Dictionary<string, List<TypeSearchResult>> { [pattern] = results },
            partialMatches != null ? new Dictionary<string, List<TypeSearchResult>> { [pattern] = partialMatches } : [],
            [],
            similarityByPattern);
    }

    private static bool LooksLikeNamespacePrefix(string pattern)
        => pattern.Contains('.') && !pattern.Contains('<') && !pattern.Contains('`');

    private static bool IsCompatibilityFallbackEligible(string pattern)
        => !pattern.Contains('*') && !pattern.Contains('?');

    private static bool IsPrefixFallbackEligible(string pattern)
        => IsCompatibilityFallbackEligible(pattern)
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

        if (IsPrefixFallbackEligible(pattern)
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

    private static string ExactNamespaceSourceIdentity(
        TypeSearchResult candidate) =>
        candidate.Location?.Observation.Realization switch
        {
            TypeDeclarationLocatorRealization.PackageRealization package =>
                $"package:{package.PackageId.ToUpperInvariant()}",
            TypeDeclarationLocatorRealization.PlatformRealization platform =>
                $"platform:{platform.Family}",
            _ when candidate.Location is { } location =>
                $"context:{location.Observation.ContextOrder}",
            _ =>
                $"compatibility:{candidate.Source?.ToUpperInvariant()}",
        };

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
        IReadOnlySet<string>? namespacePatterns = null)
    {
        var results = new List<TypeFindResult>();

        foreach (var (pattern, types) in exactMatches)
        {
            bool isGlob = TypeMatcher.IsTypeGlobPattern(pattern);
            foreach (var t in types)
            {
                results.Add(new TypeFindResult
                {
                    Pattern = pattern,
                    Match =
                        namespacePatterns?.Contains(pattern) is true
                            ? TypeFindMatchKind.Namespace
                            : isGlob
                                ? TypeFindMatchKind.Glob
                                : TypeFindMatchKind.Direct,
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
            new AssemblySetInspectionWorkspace(
                FindSourceCollector.CreateWorkspacePlan(options));
        return await CollectTypesAsync(
            options,
            pattern,
            logger,
            httpClient,
            workspace,
            static () => { });
    }

    private static async Task<List<TypeSearchResult>> CollectTypesAsync(
        FindOptions options,
        string? pattern,
        VerboseLogger logger,
        HttpClient httpClient,
        AssemblySetInspectionWorkspace workspace,
        Action markFailure)
    {
        List<TypeSearchResult> results = [];

        // A specific pattern filters each typed inventory during collection; a
        // null pattern enumerates every type for callers that match later.
        IReadOnlyList<string> searchPatterns = pattern is null ? ["*"] : [pattern];
        bool ReachedLimit() =>
            pattern is not null
            && options.Limit.HasValue
            && results.Count >= options.Limit.Value;

        async Task CollectAndScanAsync(AssemblySetRequest request)
        {
            using var assemblySet = await AssemblySetResolver.CollectAsync(httpClient, request, logger.Log);
            AssemblySetDiagnosticWriter.Write(assemblySet);
            if (assemblySet.Diagnostics.Count > 0)
                markFailure();

            workspace.RunPerAssembly(
                assemblySet,
                AssemblyContextTypeInventoryQuery.Definition,
                group => AssemblyContextTypeInventoryQuery.Execute(
                    group,
                    options.IncludeAll),
                (assembly, entry) => AddTypes(
                    results,
                    searchPatterns,
                    options.TypeFilter,
                    SearchAssemblySource.FromAssemblySet(assembly),
                    entry,
                    logger,
                    ReachedLimit,
                    markFailure),
                (assembly, failure) =>
                {
                    markFailure();
                    CommandError.WriteWarning(
                        $"Could not read {assembly.Path}: {failure}");
                },
                ReachedLimit);
        }

        if (pattern is not null && options.Limit.HasValue)
        {
            await FindSourceCollector.StreamSourcesAsync(
                options,
                ReachedLimit,
                CollectAndScanAsync);
            return results;
        }

        await CollectAndScanAsync(FindSourceCollector.BuildFindRequest(options));
        return results;
    }

    private static async Task<List<TypeSearchResult>> CollectTypesAsync(
        FindOptions options,
        string? pattern,
        VerboseLogger logger,
        ConfiguredPackageSearchWorkspace workspace,
        Action markFailure,
        CancellationToken cancellationToken)
    {
        List<TypeSearchResult> results = [];
        IReadOnlyList<string> searchPatterns =
            pattern is null ? ["*"] : [pattern];
        ConfiguredPackageSearchQueryResult<
            AssemblyContextResult<AssemblyTypeInventory>>? execution =
                await workspace.QuerySurfaceAsync(
                    context =>
                        AssemblyContextTypeInventoryQuery.Execute(
                            context.Group,
                            options.IncludeAll),
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
                markFailure);
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
        Action markFailure)
    {
        switch (entry)
        {
            case AssemblyContextEntry<
                AssemblyTypeInventory>.Available available:
                foreach (AssemblyTypeInventoryEntry type
                    in available.Value.Types)
                {
                    if (!searchPatterns.Any(searchPattern =>
                            TypeMatcher.MatchesTypeFilter(
                                type.FullName,
                                searchPattern)))
                    {
                        continue;
                    }
                    if (typeFilter is not null
                        && !TypeMatcher.MatchesTypeFilter(
                            type.FullName,
                            typeFilter))
                    {
                        continue;
                    }

                    results.Add(new TypeSearchResult
                    {
                        TypeName = type.TypeName,
                        Namespace = type.Namespace,
                        FullName = type.FullName,
                        Kind = type.Kind,
                        Assembly = assembly.Library,
                        Source = assembly.Source,
                        SourceVersion = assembly.SourceVersion,
                    });
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
