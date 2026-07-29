using System.Text.Json.Serialization;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Searches NuGet for packages by keyword.
/// </summary>
public class PackageSearchCommand
{
    public const string Name = "search";

    public static async Task<int> ExecuteAsync(PackageSearchOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            var outcome = await NuGetSearchService.SearchAsync(
                context.HttpClient,
                options.Query,
                options.Take,
                options.Prerelease,
                logger.Log,
                options.SourceOptions);

            var results = outcome.Results;

            // Sources that could not be searched are reported even when other sources
            // succeeded: a partial answer must not read like a complete one.
            foreach (var failure in outcome.Failures)
            {
                Console.Error.WriteLine($"Warning: could not search {failure}");
            }

            // A genuine zero-result search succeeded; an incomplete one did not.
            var exitCode = outcome.Failures.Count > 0 ? 1 : 0;

            // --count reduces the payload, so it is resolved before the format flags that
            // render it. Ordering these the other way lets --json answer a count request
            // with the full unprojected result set.
            if (options.Count)
            {
                CountOutput.WriteCount(results.Count);
                return exitCode;
            }

            if (options.JsonOutput)
            {
                foreach (var result in results)
                {
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                        result, PackageSearchJsonlContext.Default.NuGetSearchResult));
                }
                return exitCode;
            }

            if (results.Count == 0)
            {
                Console.Error.WriteLine($"No packages found for \"{options.Query}\".");
                return exitCode;
            }

            var view = new PackageSearchResultView
            {
                Title = $"NuGet Search: {options.Query}",
                Results = results.Select(r => new PackageSearchRow(
                    r.PackageId,
                    r.Version,
                    PackageSearchOutputFormatter.FormatDownloads(r.TotalDownloads),
                    PackageSearchOutputFormatter.TruncateDescription(r.Description, 60)
                )).ToList()
            };
            MarkoutSerializer.Serialize(view, Console.Out, new MarkdownFormatter(), PackageSearchResultContext.Default);
            return exitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

/// <summary>
/// Options for the package search command.
/// </summary>
public record PackageSearchOptions
{
    /// <summary>Search query (keyword or package name prefix).</summary>
    public string Query { get; init; } = "";

    /// <summary>Maximum number of results.</summary>
    public int Take { get; init; } = 20;

    /// <summary>Include prerelease versions.</summary>
    public bool Prerelease { get; init; }

    /// <summary>Output as JSON.</summary>
    public bool JsonOutput { get; init; }

    /// <summary>Minified JSON output.</summary>
    public bool CompactJson { get; init; }

    /// <summary>Show verbose progress messages.</summary>
    public bool Verbose { get; init; }

    /// <summary>Reduce the result table to a single row count.</summary>
    public bool Count { get; init; }

    /// <summary>NuGet sources to search. Defaults to nuget.org when unset.</summary>
    public NuGetSourceOptions? SourceOptions { get; init; }
}
