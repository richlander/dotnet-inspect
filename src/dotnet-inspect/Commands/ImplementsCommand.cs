using DotnetInspector.Packages;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Finds types that implement an interface or extend a base class.
/// </summary>
public class ImplementsCommand
{
    public static async Task<int> ExecuteAsync(string targetType, ImplementsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var tempDirs = new List<string>();

        try
        {
            // Default to runtime framework if no scope specified
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to --framework runtime");
                options = options with { PlatformFrameworks = ["runtime"] };
            }

            var results = new List<ImplementerResult>();

            // Collect all assembly paths from various sources
            var assemblyInfos = await AssemblyCollector.CollectAsync(context.HttpClient, options, tempDirs, logger, "inspect-impl");

            logger.Log($"Scanning {assemblyInfos.Count} assemblies for types implementing {targetType}");

            // Scan assemblies for implementers
            foreach (var asmInfo in assemblyInfos)
            {
                var implementers = ScanForImplementers(asmInfo.Path, targetType, options.IncludeAll, logger);
                foreach (var impl in implementers)
                {
                    impl.Source = asmInfo.Source;
                    impl.SourceVersion = asmInfo.Version;
                }
                results.AddRange(implementers);
            }

            // Deduplicate by type name + source (same type from multiple TFM folders)
            results = results
                .GroupBy(r => (r.TypeName, r.Source))
                .Select(g => g.First())
                .ToList();

            // Apply limit
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
                WriteMarkoutOutput(targetType, results);
            }

            return 0;
        }
        finally
        {
            AssemblyCollector.CleanupTempDirs(tempDirs);
        }
    }

    private static List<ImplementerResult> ScanForImplementers(
        string assemblyPath,
        string targetType,
        bool includeAll,
        VerboseLogger logger)
    {
        var results = new List<ImplementerResult>();

        try
        {
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var relationship in AssemblyReader.FindImplementers(assemblyPath, targetType, includeAll))
            {
                results.Add(new ImplementerResult
                {
                    TypeName = relationship.TypeName,
                    Namespace = relationship.Namespace,
                    Kind = relationship.Kind,
                    Relationship = relationship.RelationshipKind.ToString().ToLowerInvariant(),
                    Assembly = assemblyName
                });
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning {assemblyPath}: {ex.Message}");
        }

        return results;
    }

    private static void WriteJsonOutput(List<ImplementerResult> results, bool compact)
    {
        var typeInfo = compact
            ? ImplementsCompactJsonContext.Default.ListImplementerResult
            : ImplementsJsonContext.Default.ListImplementerResult;
        Console.WriteLine(JsonSerializer.Serialize(results, typeInfo));
    }

    private static void WriteMarkoutOutput(string targetType, List<ImplementerResult> results)
    {
        var writer = new MarkoutWriter();
        
        writer.WriteHeading(1, $"Types Implementing {targetType}");

        if (results.Count == 0)
        {
            writer.WriteParagraph("No implementing types found.");
            Console.WriteLine(writer.ToString());
            return;
        }

        writer.WriteField("Matches", results.Count);

        // Group by source
        var bySource = results.GroupBy(r => r.Source).ToList();

        foreach (var sourceGroup in bySource)
        {
            var source = sourceGroup.Key;
            var version = sourceGroup.First().SourceVersion;
            var sourceDisplay = version != null ? $"{source}@{version}" : source;

            writer.WriteHeading(2, sourceDisplay ?? "Unknown");

            var headers = new[] { "Type", "Kind", "Relationship", "Assembly" };
            var rows = sourceGroup.OrderBy(r => r.TypeName)
                .Select(impl => new[] { impl.TypeName, impl.Kind, impl.Relationship, impl.Assembly ?? "" });
            writer.WriteTable(headers, rows);
        }

        Console.WriteLine(writer.ToString());
    }
}

/// <summary>
/// Result of implementer search.
/// </summary>
public class ImplementerResult
{
    [JsonPropertyName("type")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = "";

    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }
}

[JsonSerializable(typeof(List<ImplementerResult>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ImplementsJsonContext : JsonSerializerContext { }

[JsonSerializable(typeof(List<ImplementerResult>))]
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ImplementsCompactJsonContext : JsonSerializerContext { }
