using System.Text.Json.Serialization;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Views;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Commands;

/// <summary>
/// Searches NuGet for packages by keyword.
/// </summary>
public class PackageSearchCommand
{
    public const string Name = "search";
    private static readonly string[] ResultColumns =
        ["Package", "Version", "Downloads", "Description"];

    public static async Task<int> ExecuteAsync(PackageSearchOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            if (options.Count)
            {
                if (!LensProjection.TryResolveColumns(
                        options,
                        "package search",
                        ResultColumns,
                        out _))
                {
                    return 1;
                }

                CommandError.Write(
                    "package search does not support --count because search providers limit results.");
                return 1;
            }

            if (LensProjection.TryProject(
                    options,
                    "package search",
                    rowCount: 0,
                    out var preflightExitCode,
                    ResultColumns))
            {
                return preflightExitCode;
            }

            if (options.Fields is { Length: > 0 }
                || options.Columns is { Length: > 0 })
            {
                CommandError.Write(
                    "--fields/--columns are not available with package search.");
                return 1;
            }

            if (options.OutputPath is not null)
            {
                CommandError.Write(
                    "--out is not available with package search.");
                return 1;
            }

            var outcome = await NuGetSearchService.SearchAsync(
                context.HttpClient,
                options.Query,
                options.Take,
                options.Prerelease,
                logger.Log,
                options.SourceOptions,
                NuGetFetchOptions.FromRequestTimeout(
                    context.HttpClient.Timeout));

            var results = RowWindow.Apply(options.Rows, outcome.Results);

            // Sources that could not be searched are reported even when other sources
            // succeeded: a partial answer must not read like a complete one.
            foreach (var failure in outcome.Failures)
            {
                CommandError.WriteWarning($"could not search {failure}");
            }

            // A genuine zero-result search succeeded; an incomplete one did not.
            var exitCode = outcome.Failures.Count > 0 ? 1 : 0;

            if (!options.JsonOutput && results.Count == 0)
            {
                if (outcome.Results.Count == 0)
                    CommandError.WriteLine($"No packages found for \"{options.Query}\".");
                else
                    CommandError.WriteLine(
                        $"No packages are in the requested row window for \"{options.Query}\".");
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
            CommandError.Write(ex);
            return 1;
        }
    }
}

/// <summary>
/// Options for the package search command.
/// </summary>
public record PackageSearchOptions : IProjectionOptions
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

    /// <summary>Inherited printable-payload projection.</summary>
    public bool Print { get; init; }

    /// <summary>Inherited scalar-value projection.</summary>
    public bool Value { get; init; }

    /// <summary>Inherited URL projection.</summary>
    public bool Urls { get; init; }

    /// <summary>Inherited path projection.</summary>
    public bool Paths { get; init; }

    /// <summary>Inherited output destination.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Inherited result-row window.</summary>
    public RowWindow? Rows { get; init; }

    /// <summary>Field projection inherited from the package command.</summary>
    public string[]? Fields { get; init; }

    /// <summary>Column projection inherited from the package command.</summary>
    public string[]? Columns { get; init; }

    /// <summary>NuGet sources to search. Defaults to nuget.org when unset.</summary>
    public NuGetSourceOptions? SourceOptions { get; init; }
}
