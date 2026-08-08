using DotnetInspector.Commands;
using ILInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Collects types from multiple sources: packages, assemblies, platform frameworks,
/// projects, and bin directories. Handles the 6-source iteration pattern.
/// </summary>
internal static class TypeSearchService
{
    /// <summary>
    /// Finds types matching one or more patterns, returning classified results with match kind and similarity.
    /// This is the primary entry point for the find command.
    /// </summary>
    public static async Task<List<TypeFindResult>> FindTypesAsync(
        FindOptions options,
        string[] patterns,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        // Optimized single-pattern path: collect with filtering, then partial match if empty
        if (patterns.Length == 1 && !options.Tabular)
        {
            return await FindSinglePatternAsync(patterns[0], options, logger, httpClient);
        }

        // Multi-pattern or tabular output: collect all types, then match each pattern
        return await FindMultiPatternAsync(patterns, options, logger, httpClient);
    }

    private static async Task<List<TypeFindResult>> FindMultiPatternAsync(
        string[] patterns,
        FindOptions options,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var allTypes = await CollectTypesAsync(options, null, logger, httpClient);
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
            {
                matches = matches.Take(options.Limit.Value).ToList();
            }

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
        VerboseLogger logger,
        HttpClient httpClient)
    {
        var results = await CollectTypesAsync(options, pattern, logger, httpClient);

        List<TypeSearchResult>? partialMatches = null;
        Dictionary<string, double>? partialSimilarities = null;
        if (results.Count == 0 && !pattern.Contains('*') && !pattern.Contains('?'))
        {
            var allTypes = await CollectTypesAsync(options, null, logger, httpClient);
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

        int totalCount = results.Count;
        if (options.Limit.HasValue && results.Count > options.Limit.Value)
        {
            results = results.Take(options.Limit.Value).ToList();
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
    /// When pattern is provided, matching happens during collection for early-exit with limit.
    /// </summary>
    public static async Task<List<TypeSearchResult>> CollectTypesAsync(
        FindOptions options,
        string? pattern,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        List<TypeSearchResult> results = [];

        // Search the resolved closed set through the corpus. A specific pattern filters during the
        // scan; a null pattern enumerates every type ("*" matches all) for the callers that collect
        // first and match later (the multi-pattern and fuzzy-fallback paths).
        IReadOnlyList<string> searchPatterns = pattern is null ? ["*"] : [pattern];

        bool ReachedLimit() => pattern != null && options.Limit.HasValue && results.Count >= options.Limit.Value;

        async Task CollectAndScanAsync(AssemblySetRequest request)
        {
            using var assemblySet = await AssemblySetResolver.CollectAsync(httpClient, request, logger.Log);
            AssemblySetDiagnosticWriter.Write(assemblySet);

            // Streaming (pattern + limit) caps each source at the remaining budget so later sources are
            // never resolved once the limit is met; the unbounded path passes null so the corpus scans
            // the whole set in parallel.
            int? remaining = pattern != null && options.Limit.HasValue
                ? options.Limit.Value - results.Count
                : null;

            var corpus = CorpusProducer.ToCorpus(assemblySet);
            var outcome = corpus.SearchTypes(searchPatterns, options.IncludeAll, remaining);

            foreach (var skippedPath in outcome.SkippedAssemblies)
                logger.LogWarning($"Could not read {skippedPath}");

            foreach (var match in outcome.Results)
            {
                results.Add(new TypeSearchResult
                {
                    TypeName = match.TypeName,
                    Namespace = match.Namespace,
                    FullName = match.FullName,
                    Kind = match.Kind,
                    Assembly = match.Assembly,
                    Source = match.Source,
                    SourceVersion = match.Version,
                });
            }
        }

        if (pattern != null && options.Limit.HasValue)
        {
            await FindSourceCollector.StreamSourcesAsync(options, ReachedLimit, CollectAndScanAsync);
            return results;
        }

        await CollectAndScanAsync(FindSourceCollector.BuildFindRequest(options));
        return results;
    }
}
