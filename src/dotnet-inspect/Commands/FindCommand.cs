using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Searches for types across packages, assemblies, and platform frameworks.
/// </summary>
public class FindCommand
{
    public const string Name = "find";
    public static async Task<int> ExecuteAsync(FindOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        List<string> tempDirs = [];

        try
        {
            // Split comma-separated patterns
            var patterns = options.Pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0)
            {
                Console.Error.WriteLine("Error: No pattern specified.");
                return 1;
            }

            // Safety fallback — default to all platform frameworks
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            // For oneline/multi-pattern mode, collect results per pattern
            if (options.OneLine || patterns.Length > 1)
            {
                return await ExecuteMultiPatternAsync(patterns, options, logger, tempDirs, context.HttpClient);
            }

            // Single pattern - use original logic
            return await ExecuteSinglePatternAsync(patterns[0], options, logger, tempDirs, context.HttpClient);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
        finally
        {
            AssemblyCollector.CleanupTempDirs(tempDirs);
        }
    }

    private static async Task<int> ExecuteMultiPatternAsync(string[] patterns, FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
        // Collect all types first (without pattern filtering)
        var allTypes = await CollectAllTypesAsync(options, logger, tempDirs, httpClient);
        var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();

        // Match types against each pattern, tracking similarity scores
        Dictionary<string, List<TypeSearchResult>> resultsByPattern = [];
        Dictionary<string, List<TypeSearchResult>> partialMatchesByPattern = [];
        Dictionary<string, Dictionary<string, double>> similarityByPattern = [];
        List<string> notFoundPatterns = [];

        foreach (var pattern in patterns)
        {
            List<TypeSearchResult> matches = [];
            foreach (var type in allTypes)
            {
                // Use MatchesTypeFilter which handles both glob patterns and exact matches
                // (including generic type base names like SortedDictionary -> SortedDictionary`2)
                if (TypeMatcher.MatchesTypeFilter(type.FullName, pattern))
                {
                    matches.Add(type);
                }
            }

            // Apply limit per pattern
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
                // No exact matches and not a glob pattern - find partial matches with similarity scores
                var suggestions = TypeMatcher.FindClosest(typeNames, pattern, minSimilarity: 0.5, maxResults: 5).ToList();
                if (suggestions.Count > 0)
                {
                    // Build similarity lookup for this pattern
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
                    // No exact matches and no partial matches - pattern not found
                    notFoundPatterns.Add(pattern);
                }
            }
            else
            {
                // Glob pattern with no matches - pattern not found
                notFoundPatterns.Add(pattern);
            }
        }

        // Output results
        var rawResults = ConvertToRawData(resultsByPattern, partialMatchesByPattern, notFoundPatterns, similarityByPattern);

        if (options.JsonOutput)
        {
            var writer = new FindJsonWriter(compact: options.CompactJson);
            writer.Write(rawResults, new WriterOptions(), Console.Out);
        }
        else
        {
            WriteOutput(rawResults, "Find Results", options);
        }

        return 0;
    }

    private static async Task<int> ExecuteSinglePatternAsync(string pattern, FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
            var results = await TypeSearchService.CollectTypesAsync(options, pattern, logger, tempDirs, httpClient);

            // If no results and not a glob pattern, find partial matches using similarity
            List<TypeSearchResult>? partialMatches = null;
            Dictionary<string, double>? partialSimilarities = null;
            if (results.Count == 0 && !pattern.Contains('*') && !pattern.Contains('?'))
            {
                var allTypes = await CollectAllTypesAsync(options, logger, tempDirs, httpClient);
                var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();
                var suggestions = TypeMatcher.FindClosest(typeNames, pattern, minSimilarity: 0.5, maxResults: 5).ToList();

                if (suggestions.Count > 0)
                {
                    // Build similarity lookup
                    partialSimilarities = suggestions.ToDictionary(s => s.Name, s => s.Similarity);

                    // Map suggestions back to full TypeSearchResult objects
                    var suggestionSet = suggestions.Select(s => s.Name).ToHashSet();
                    partialMatches = allTypes
                        .Where(t => suggestionSet.Contains(t.FullName))
                        .DistinctBy(t => t.FullName)
                        .ToList();
                }
            }

            // Apply limit
            int totalCount = results.Count;
            if (options.Limit.HasValue && results.Count > options.Limit.Value)
            {
                results = results.Take(options.Limit.Value).ToList();
            }

            // Output results
            var similarityByPattern = partialSimilarities != null
                ? new Dictionary<string, Dictionary<string, double>> { [pattern] = partialSimilarities }
                : null;
            var rawResults = ConvertToRawData(
                new Dictionary<string, List<TypeSearchResult>> { [pattern] = results },
                partialMatches != null ? new Dictionary<string, List<TypeSearchResult>> { [pattern] = partialMatches } : [],
                [],
                similarityByPattern);

            if (options.JsonOutput)
            {
                var writer = new FindJsonWriter(compact: options.CompactJson);
                writer.Write(rawResults, new WriterOptions(), Console.Out);
            }
            else
            {
                WriteOutput(rawResults, $"Find: {pattern}", options, totalCount, options.Limit);
            }

            return 0;
    }

    private static async Task<List<TypeSearchResult>> CollectAllTypesAsync(FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
        return await TypeSearchService.CollectTypesAsync(options, null, logger, tempDirs, httpClient);
    }

    private static void WriteJsonOutput(List<TypeSearchResult> results, bool compact)
    {
        JsonOutputHelper.Write(results, FindJsonContext.Default.ListTypeSearchResult, FindCompactJsonContext.Default.ListTypeSearchResult, compact);
    }

    private static void WriteOutput(List<TypeFindResult> rawData, string title, FindOptions options, int? totalCount = null, int? limit = null)
    {
        var view = FindOutputFormatter.BuildView(rawData, title, totalCount, limit);

        if (options.OneLine)
        {
            var writer = new OneLineWriter(Console.Out, showHeader: !options.NoHeader);
            new MarkoutContext().Serialize(view, writer);
        }
        else
        {
            Console.WriteLine(new MarkoutContext().Serialize(view));
        }
    }

    /// <summary>
    /// Converts separate result dictionaries into a unified flat list of TypeFindResult.
    /// Each result gets a MatchKind (Exact, Partial, NotFound) and similarity score.
    /// </summary>
    private static List<TypeFindResult> ConvertToRawData(
        Dictionary<string, List<TypeSearchResult>> exactMatches,
        Dictionary<string, List<TypeSearchResult>> partialMatches,
        List<string> notFoundPatterns,
        Dictionary<string, Dictionary<string, double>>? similarityByPattern = null)
    {
        var results = new List<TypeFindResult>();

        // Add exact/glob matches (similarity = 1.0)
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

        // Add partial matches with their similarity scores
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

        // Add not found patterns (similarity = null)
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
}

/// <summary>
/// Represents a type found during search.
/// </summary>
public record class TypeSearchResult
{
    [JsonPropertyName("type")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("library")]
    public string? Assembly { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }
}

