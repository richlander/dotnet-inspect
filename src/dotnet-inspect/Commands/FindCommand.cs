using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;

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

            // Default to runtime framework if no scope specified
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to --framework runtime");
                options = options with { PlatformFrameworks = ["runtime"] };
            }

            // For oneline/name-only/multi-pattern mode, collect results per pattern
            if (options.OneLine || options.NameOnly || patterns.Length > 1)
            {
                return await ExecuteMultiPatternAsync(patterns, options, logger, tempDirs, context.HttpClient);
            }

            // Single pattern - use original logic
            return await ExecuteSinglePatternAsync(patterns[0], options, logger, tempDirs, context.HttpClient);
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

        // Match types against each pattern
        Dictionary<string, List<TypeSearchResult>> resultsByPattern = [];
        foreach (var pattern in patterns)
        {
            List<TypeSearchResult> matches = [];
            foreach (var type in allTypes)
            {
                if (TypeMatcher.MatchesGlob(type.FullName, pattern) || TypeMatcher.MatchesGlob(type.TypeName, pattern))
                {
                    matches.Add(type);
                }
            }

            // Apply limit per pattern
            if (options.Limit.HasValue && matches.Count > options.Limit.Value)
            {
                matches = matches.Take(options.Limit.Value).ToList();
            }

            resultsByPattern[pattern] = matches;
        }

        // Output results
        if (options.JsonOutput)
        {
            var allResults = resultsByPattern.Values.SelectMany(r => r).Distinct().ToList();
            WriteJsonOutput(allResults, options.CompactJson);
        }
        else if (options.NameOnly)
        {
            Console.WriteLine(FindOutputFormatter.FormatNameOnlyOutput(resultsByPattern));
        }
        else if (options.OneLine)
        {
            Console.WriteLine(FindOutputFormatter.FormatOneLineOutput(resultsByPattern, options.Grouped));
        }
        else
        {
            Console.WriteLine(FindOutputFormatter.FormatMultiPatternOutput(resultsByPattern));
        }

        return 0;
    }

    private static async Task<int> ExecuteSinglePatternAsync(string pattern, FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
            var results = await TypeSearchService.CollectTypesAsync(options, pattern, logger, tempDirs, httpClient);

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
                Console.WriteLine(FindOutputFormatter.FormatMarkoutOutput(results, pattern, totalCount, options.Limit));
            }

            return 0;
    }

    private static async Task<List<TypeSearchResult>> CollectAllTypesAsync(FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
        return await TypeSearchService.CollectTypesAsync(options, null, logger, tempDirs, httpClient);
    }

    private static void WriteJsonOutput(List<TypeSearchResult> results, bool compact)
    {
        var typeInfo = compact
            ? FindCompactJsonContext.Default.ListTypeSearchResult
            : FindJsonContext.Default.ListTypeSearchResult;
        Console.WriteLine(JsonSerializer.Serialize(results, typeInfo));
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

[JsonSerializable(typeof(List<TypeSearchResult>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class FindJsonContext : JsonSerializerContext { }

[JsonSerializable(typeof(List<TypeSearchResult>))]
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class FindCompactJsonContext : JsonSerializerContext { }
