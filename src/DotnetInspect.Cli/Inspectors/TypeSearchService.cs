using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
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
        CancellationToken cancellationToken = default)
    {
        bool hasFailures = false;
        void MarkFailure() => hasFailures = true;
        AssemblySetRequest request =
            FindSourceCollector.BuildFindRequest(options);
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
                    cancellationToken);
            if (configured is null)
                return new([], HasFailures: true);

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
                        cancellationToken))
                : await FindMultiPatternAsync(
                    patterns,
                    options,
                    pattern => CollectTypesAsync(
                        options,
                        pattern,
                        logger,
                        configured,
                        MarkFailure,
                        cancellationToken));
            return CreateSearchResult(
                configuredResults,
                hasFailures,
                options.PackagePrefixLimitReached);
        }

        using var workspace = new AssemblySetInspectionWorkspace();
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
                Collect),
                hasFailures,
                options.PackagePrefixLimitReached);
        }

        // Multi-pattern or tabular output: collect all types, then match each pattern
        return CreateSearchResult(
            await FindMultiPatternAsync(
                patterns,
                options,
                Collect),
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
                    result.Match == MatchKind.NotFound)
                .Select(static result => result.Pattern),
        ];
        return new(
            [
                .. results.Where(static result =>
                    result.Match != MatchKind.NotFound),
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
        Func<string?, Task<List<TypeSearchResult>>> collect)
    {
        var allTypes = await collect(null);
        var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();

        Dictionary<string, List<TypeSearchResult>> resultsByPattern = [];
        Dictionary<string, List<TypeSearchResult>> partialMatchesByPattern = [];
        Dictionary<string, Dictionary<string, double>> similarityByPattern = [];
        List<string> notFoundPatterns = [];

        foreach (var pattern in patterns)
        {
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

        return ConvertToFindResults(resultsByPattern, partialMatchesByPattern, notFoundPatterns, similarityByPattern);
    }

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
        Func<string?, Task<List<TypeSearchResult>>> collect)
    {
        var results = await collect(pattern);

        List<TypeSearchResult>? partialMatches = null;
        Dictionary<string, double>? partialSimilarities = null;
        if (results.Count == 0 && !pattern.Contains('*') && !pattern.Contains('?'))
        {
            var allTypes = await collect(null);
            var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();

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

    /// <summary>
    /// Converts separate result dictionaries into a unified flat list of TypeFindResult.
    /// </summary>
    private static List<TypeFindResult> ConvertToFindResults(
        Dictionary<string, List<TypeSearchResult>> exactMatches,
        Dictionary<string, List<TypeSearchResult>> partialMatches,
        List<string> notFoundPatterns,
        Dictionary<string, Dictionary<string, double>>? similarityByPattern = null)
    {
        var results = new List<TypeFindResult>();

        foreach (var (pattern, types) in exactMatches)
        {
            var isGlob = pattern.Contains('*') || pattern.Contains('?');
            foreach (var t in types)
            {
                results.Add(new TypeFindResult
                {
                    Pattern = pattern,
                    Match = isGlob ? MatchKind.Glob : MatchKind.Exact,
                    Similarity = 1.0,
                    Type = t.TypeName,
                    Namespace = t.Namespace ?? "",
                    FullName = t.FullName,
                    Kind = t.Kind ?? "",
                    Library = t.Assembly ?? "",
                    Source = t.Source ?? "",
                    SourceVersion = t.SourceVersion
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
                    Match = MatchKind.Partial,
                    Similarity = similarity,
                    Type = t.TypeName,
                    Namespace = t.Namespace ?? "",
                    FullName = t.FullName,
                    Kind = t.Kind ?? "",
                    Library = t.Assembly ?? "",
                    Source = t.Source ?? "",
                    SourceVersion = t.SourceVersion
                });
            }
        }

        foreach (var pattern in notFoundPatterns)
        {
            results.Add(new TypeFindResult
            {
                Pattern = pattern,
                Match = MatchKind.NotFound,
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
        using var workspace = new AssemblySetInspectionWorkspace();
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
