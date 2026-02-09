using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Searches for types across packages, assemblies, and platform frameworks.
/// </summary>
public class FindCommand
{
    public const string Name = "find";
    public static async Task<int> ExecuteAsync(string patternInput, FindOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var tempDirs = new List<string>();

        try
        {
            // Split comma-separated patterns
            var patterns = patternInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0)
            {
                Console.Error.WriteLine("Error: No pattern specified.");
                return 1;
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
        // Default to runtime framework if no scope specified
        if (!options.HasAnyScope)
        {
            logger.Log("No scope specified, defaulting to --framework runtime");
            options = options with { PlatformFrameworks = ["runtime"] };
        }

        // Collect all types first (without pattern filtering)
        var allTypes = await CollectAllTypesAsync(options, logger, tempDirs, httpClient);

        // Match types against each pattern
        var resultsByPattern = new Dictionary<string, List<TypeSearchResult>>();
        foreach (var pattern in patterns)
        {
            var matches = new List<TypeSearchResult>();
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
            WriteNameOnlyOutput(resultsByPattern);
        }
        else if (options.OneLine)
        {
            WriteOneLineOutput(resultsByPattern, options.Grouped);
        }
        else
        {
            WriteMultiPatternOutput(resultsByPattern, options.Limit);
        }

        return 0;
    }

    private static void WriteNameOnlyOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern)
    {
        Console.WriteLine(FormatNameOnlyOutput(resultsByPattern));
    }

    private static async Task<int> ExecuteSinglePatternAsync(string pattern, FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
            // Default to runtime framework if no scope specified
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to --framework runtime");
                options = options with { PlatformFrameworks = ["runtime"] };
            }

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
                WriteMarkoutOutput(results, pattern, totalCount, options.Limit);
            }

            return 0;
    }

    private static async Task<List<TypeSearchResult>> CollectAllTypesAsync(FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
        return await TypeSearchService.CollectTypesAsync(options, null, logger, tempDirs, httpClient);
    }

    private static void WriteOneLineOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern, bool grouped)
    {
        Console.WriteLine(FormatOneLineOutput(resultsByPattern, grouped));
    }

    internal static string FormatOneLineOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern, bool grouped)
    {
        if (grouped)
        {
            // Grouped: one line per pattern with matching type names
            var lines = new List<string>();
            foreach (var (pattern, results) in resultsByPattern)
            {
                var typeNames = results.Select(r => r.TypeName).Distinct().OrderBy(n => n);
                lines.Add($"{pattern}: {string.Join(", ", typeNames)}");
            }
            return string.Join(Environment.NewLine, lines);
        }
        else
        {
            // Flat: all type names space-separated on one line
            var allTypeNames = resultsByPattern.Values
                .SelectMany(r => r)
                .Select(r => r.TypeName)
                .Distinct()
                .OrderBy(n => n);
            return string.Join(" ", allTypeNames);
        }
    }

    internal static string FormatNameOnlyOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern)
    {
        var allTypeNames = resultsByPattern.Values
            .SelectMany(r => r)
            .Select(r => r.TypeName)
            .Distinct()
            .OrderBy(n => n);
        return string.Join(Environment.NewLine, allTypeNames);
    }

    private static void WriteMultiPatternOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern, int? limit)
    {
        var writer = new MarkoutWriter();
        writer.WriteHeading(1, "Find Results");

        foreach (var (pattern, results) in resultsByPattern)
        {
            writer.WriteHeading(2, pattern);
            writer.WriteField("Matches", results.Count);

            if (results.Count == 0)
            {
                writer.WriteParagraph("*No types found.*");
            }
            else
            {
                var headers = new[] { "Type", "Namespace", "Kind", "Assembly", "Source" };
                var rows = results.Select(result =>
                {
                    var ns = result.Namespace ?? "";
                    var source = result.SourceVersion != null
                        ? $"{result.Source}@{result.SourceVersion}"
                        : result.Source ?? "";
                    return new[] { result.TypeName, ns, result.Kind ?? "", result.Assembly ?? "", source };
                });
                writer.WriteTable(headers, rows);
            }
        }

        Console.WriteLine(writer.ToString());
    }

    private static void WriteJsonOutput(List<TypeSearchResult> results, bool compact)
    {
        var typeInfo = compact
            ? FindCompactJsonContext.Default.ListTypeSearchResult
            : FindJsonContext.Default.ListTypeSearchResult;
        Console.WriteLine(JsonSerializer.Serialize(results, typeInfo));
    }

    private static void WriteMarkoutOutput(List<TypeSearchResult> results, string pattern, int totalCount, int? limit)
    {
        var writer = new MarkoutWriter();
        writer.WriteHeading(1, $"Find: {pattern}");
        writer.WriteField("Matches", totalCount);

        if (results.Count == 0)
        {
            writer.WriteParagraph("*No types found matching the pattern.*");
        }
        else
        {
            var headers = new[] { "Type", "Namespace", "Kind", "Assembly", "Source" };
            var rows = results.Select(result =>
            {
                var ns = result.Namespace ?? "";
                var source = result.SourceVersion != null 
                    ? $"{result.Source}@{result.SourceVersion}" 
                    : result.Source ?? "";
                return new[] { result.TypeName, ns, result.Kind ?? "", result.Assembly ?? "", source };
            });
            writer.WriteTable(headers, rows);

            if (limit.HasValue && totalCount > limit.Value)
            {
                writer.WriteParagraph($"... *and {totalCount - limit.Value} more types*");
            }
        }

        Console.WriteLine(writer.ToString());
    }
}

/// <summary>
/// Represents a type found during search.
/// </summary>
public class TypeSearchResult
{
    [JsonPropertyName("type")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("assembly")]
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
