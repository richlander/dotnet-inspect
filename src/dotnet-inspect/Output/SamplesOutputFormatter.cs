using DotnetInspector.Commands;
using DotnetInspector.Services;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats samples command results for display.
/// </summary>
public static class SamplesOutputFormatter
{
    public static SamplesListView BuildListView(
        List<TypedSample> samples,
        string? packageName,
        string? packageVersion,
        string? assemblyName,
        bool browsableUrls)
    {
        var title = assemblyName ?? packageName ?? "Samples";
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null && assemblyName != packageName ? $" ({packageName})" : "";

        var rows = samples.Select(typedSample =>
        {
            var sample = typedSample.Sample;
            var description = sample.Description ?? Path.GetFileName(sample.RelativePath);
            var url = sample.ResolvedUrl != null
                ? GitHubUrlResolver.ConvertToGitHubRawUrl(sample.ResolvedUrl) ?? sample.ResolvedUrl
                : sample.RelativePath;

            if (browsableUrls && url != sample.RelativePath)
            {
                url = GitHubUrlResolver.ConvertRawToBlobUrl(url);
            }

            return new SampleRow(typedSample.FullTypeName, description, url);
        }).ToList();

        return new SamplesListView
        {
            Title = $"Samples: {title}{packageInfo}",
            Samples = rows.Count > 0 ? rows : null
        };
    }

    public static void WriteSamplesWithContent(
        MarkoutOrchestrator writer,
        int index,
        TypedSample typedSample,
        string? content)
    {
        var sample = typedSample.Sample;
        var description = sample.Description ?? Path.GetFileName(sample.RelativePath);

        writer.WriteHeading(2, $"{index + 1}. {typedSample.FullTypeName} - {description}");

        if (content != null)
        {
            var lang = GetLanguageFromPath(sample.RelativePath);
            writer.WriteCodeStart(lang);
            // WriteParagraph writes the content through the writer's underlying
            // TextWriter, replacing the previous Console.Out.WriteLine bypass.
            writer.WriteParagraph(content);
            writer.WriteCodeEnd();
        }
        else
        {
            writer.WriteCallout(CalloutSeverity.Warning, "Failed to fetch sample content");
        }
    }

    public static void WriteSamplesTitle(
        MarkoutOrchestrator writer,
        string? assemblyName,
        string? packageName,
        string? packageVersion)
    {
        var title = assemblyName ?? packageName ?? "Samples";
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null && assemblyName != packageName ? $" ({packageName})" : "";
        writer.WriteHeading(1, $"Samples: {title}{packageInfo}");
    }

    private static string GetLanguageFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "csharp",
            ".fs" => "fsharp",
            ".vb" => "vb",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" or ".yml" => "yaml",
            ".md" => "markdown",
            _ => ""
        };
    }
}
