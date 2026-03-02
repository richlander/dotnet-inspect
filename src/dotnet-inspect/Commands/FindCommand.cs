using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
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
            // Discovery mode: bare --columns/--fields lists available names
            if (options.Columns is { Length: 0 } || options.Fields is { Length: 0 })
            {
                var schema = new MarkoutContext().GetSchemaInfo<FindResultView>();
                SelectOutput.WriteDiscovery(SelectResolver.GetDiscoveryEntries(null, options.Columns, options.Fields, null, schema));
                return 0;
            }

            var patterns = options.Pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0)
            {
                Console.Error.WriteLine("Error: No pattern specified.");
                return 1;
            }

            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            var results = await TypeSearchService.FindTypesAsync(options, patterns, logger, tempDirs, context.HttpClient);
            var title = patterns.Length == 1 ? $"Find: {patterns[0]}" : "Find Results";

            if (options.JsonOutput)
            {
                var writer = new FindJsonWriter();
                writer.Write(results, new WriterOptions(), Console.Out);
            }
            else
            {
                WriteOutput(results, title, options);
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

    private static void WriteOutput(List<TypeFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildView(rawData, title);

        if (options.OneLine)
        {
            var writerOpts = new MarkoutWriterOptions
            {
                Projection = OutputFormatter.BuildProjection(options.Columns, options.Fields)
            };
            new MarkoutContext().Serialize(view, Console.Out, new OneLineFormatter(showHeader: !options.NoHeader), writerOpts);
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

