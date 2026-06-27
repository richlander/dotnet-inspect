using DotnetInspector.Packages;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Finds types that implement an interface or extend a base class.
/// </summary>
public class ImplementsCommand
{
    public static async Task<int> ExecuteAsync(ImplementsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var targetType = options.TargetType;

        try
        {
            // Discovery mode: -D/--discover lists schema
            if (options.Discover != null)
            {
                var schema = new DocumentSchema()
                    .Add("Implementers", "column", "Type", "Kind", "Relationship", "Library", "Source");
                return DiscoverOutput.Execute(options.Discover, schema,
                    tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl);
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

            var results = await AssemblyCollector.ScanAsync(
                context.HttpClient,
                options,
                logger,
                "inspect-impl",
                assemblyInfo => ScanForImplementers(assemblyInfo.Path, targetType, options.IncludeAll, logger),
                (result, assemblyInfo) =>
                {
                    result.Source = assemblyInfo.Source;
                    result.SourceVersion = assemblyInfo.Version;
                },
                assemblyInfos => logger.Log($"Scanning {assemblyInfos.Count} libraries for types implementing {targetType}"));

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

            if (results.Count == 0)
                NamespacePrefixHints.WriteIfLikelyNamespacePrefix(targetType);

            // Output results
            if (options.JsonOutput)
            {
                WriteJsonOutput(results, options.CompactJson);
            }
            else if (options.Count)
            {
                WriteCount(results);
            }
            else
            {
                WriteMarkoutOutput(targetType, results, options.OneLine, options.Tsv, options.Jsonl, options.NoHeader, options.Columns, options.Fields, options.Rows);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static List<ImplementerResult> ScanForImplementers(
        string assemblyPath,
        string targetType,
        bool includeAll,
        VerboseLogger logger)
    {
        List<ImplementerResult> results = [];

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
        JsonOutputHelper.Write(results, ImplementsJsonContext.Default.ListImplementerResult, ImplementsCompactJsonContext.Default.ListImplementerResult, compact);
    }

    private static void WriteCount(List<ImplementerResult> results)
    {
        Console.WriteLine(results.Count);
    }

    private static void WriteMarkoutOutput(string targetType, List<ImplementerResult> results, bool oneLine, bool tsv, bool jsonl, bool noHeader, string[]? columns, string[]? fields, int? rows)
    {
        var view = ImplementsOutputFormatter.BuildView(targetType, results);

        if (view.Rows == null && view.Description != null)
        {
            Console.Error.WriteLine(view.Description);
            return;
        }

        if (oneLine)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !noHeader, tsv, jsonl, columns, fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                rows);
        }
        else
        {
            OutputFormatter.WriteLimitedMarkdown(Console.Out,
                MarkoutSerializer.Serialize(view, SearchViewContext.Default), rows);
        }
    }
}

/// <summary>
/// Result of implementer search.
/// </summary>
public record class ImplementerResult
{
    [JsonPropertyName("type")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = "";

    [JsonPropertyName("library")]
    public string? Assembly { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }
}
