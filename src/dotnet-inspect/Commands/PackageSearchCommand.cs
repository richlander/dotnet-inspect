using System.Text.Json.Serialization;
using DotnetInspector.Output;
using DotnetInspector.Packages;

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
            var results = await NuGetSearchService.SearchAsync(
                context.HttpClient,
                options.Query,
                options.Take,
                options.Prerelease,
                logger.Log);

            if (options.JsonOutput)
            {
                foreach (var result in results)
                {
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
                        result, PackageSearchJsonlContext.Default.NuGetSearchResult));
                }
                return 0;
            }

            if (results.Count == 0)
            {
                Console.Error.WriteLine($"No packages found for \"{options.Query}\".");
                return 0;
            }

            Console.WriteLine(PackageSearchOutputFormatter.FormatResults(results, options.Query));
            return 0;
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
}
