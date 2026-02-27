using DotnetInspector.Packages;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
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
        List<string> tempDirs = [];
        var targetType = options.TargetType;

        try
        {
            // Discovery mode: bare --columns/--fields lists available names
            if (options.Columns is { Length: 0 } || options.Fields is { Length: 0 })
            {
                var schema = new MarkoutContext().GetSchemaInfo<ImplementsResultView>();
                SelectOutput.WriteDiscovery(SelectResolver.GetDiscoveryEntries(null, options.Columns, options.Fields, null, schema));
                return 0;
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

            List<ImplementerResult> results = [];

            // Collect all assembly paths from various sources
            var assemblyInfos = await AssemblyCollector.CollectAsync(context.HttpClient, options, tempDirs, logger, "inspect-impl");

            logger.Log($"Scanning {assemblyInfos.Count} libraries for types implementing {targetType}");

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
                WriteMarkoutOutput(targetType, results, options.OneLine, options.NoHeader, options.Columns, options.Fields);
            }

            return 0;
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

    private static void WriteMarkoutOutput(string targetType, List<ImplementerResult> results, bool oneLine, bool noHeader, string[]? columns, string[]? fields)
    {
        var view = ImplementsOutputFormatter.BuildView(targetType, results);

        if (oneLine)
        {
            var writerOpts = new MarkoutWriterOptions
            {
                Projection = OutputFormatter.BuildProjection(null, columns, fields)
            };
            new MarkoutContext().Serialize(view, Console.Out, new OneLineFormatter(showHeader: !noHeader), writerOpts);
        }
        else
        {
            Console.WriteLine(new MarkoutContext().Serialize(view));
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

