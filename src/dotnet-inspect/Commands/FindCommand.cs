using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;

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

        // Match types against each pattern
        Dictionary<string, List<TypeSearchResult>> resultsByPattern = [];
        Dictionary<string, List<TypeSearchResult>> partialMatchesByPattern = [];
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
                // No exact matches and not a glob pattern - find partial matches
                var suggestions = TypeMatcher.FindClosest(typeNames, pattern, minSimilarity: 0.5, maxResults: 5).ToList();
                if (suggestions.Count > 0)
                {
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
        if (options.JsonOutput)
        {
            var allResults = resultsByPattern.Values.SelectMany(r => r).Distinct().ToList();
            WriteJsonOutput(allResults, options.CompactJson);
        }
        else
        {
            WriteMarkoutOutput(
                FindOutputFormatter.BuildMultiPatternView(
                    resultsByPattern,
                    partialMatchesByPattern.Count > 0 ? partialMatchesByPattern : null,
                    notFoundPatterns.Count > 0 ? notFoundPatterns : null),
                options.OneLine,
                options.NoHeader);
        }

        return 0;
    }

    private static async Task<int> ExecuteSinglePatternAsync(string pattern, FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
            var results = await TypeSearchService.CollectTypesAsync(options, pattern, logger, tempDirs, httpClient);

            // If no results and not a glob pattern, find partial matches using similarity
            List<TypeSearchResult>? partialMatches = null;
            if (results.Count == 0 && !pattern.Contains('*') && !pattern.Contains('?'))
            {
                var allTypes = await CollectAllTypesAsync(options, logger, tempDirs, httpClient);
                var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();
                var suggestions = TypeMatcher.FindClosest(typeNames, pattern, minSimilarity: 0.5, maxResults: 5).ToList();

                if (suggestions.Count > 0)
                {
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
            if (options.JsonOutput)
            {
                WriteJsonOutput(results, options.CompactJson);
            }
            else
            {
                WriteMarkoutOutput(FindOutputFormatter.BuildView(results, pattern, totalCount, options.Limit, partialMatches), options.OneLine, options.NoHeader);
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

    private static void WriteMarkoutOutput(FindResultView view, bool oneLine, bool noHeader)
    {
        if (oneLine)
        {
            var writer = new OneLineWriter(Console.Out, showHeader: !noHeader);
            new MarkoutContext().Serialize(view, writer);
        }
        else
        {
            Console.WriteLine(new MarkoutContext().Serialize(view));
        }
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

