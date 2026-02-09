using DotnetInspector.Commands;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats samples command results for display.
/// </summary>
public static class SamplesOutputFormatter
{
    public static string FormatSamplesList(
        List<TypedSample> samples,
        string? packageName,
        string? packageVersion,
        string? assemblyName,
        bool browsableUrls)
    {
        var writer = new MarkoutWriter();

        var title = assemblyName ?? packageName ?? "Samples";
        var packageInfo = packageName != null && packageVersion != null
            ? $" ({packageName} {packageVersion})"
            : packageName != null && assemblyName != packageName ? $" ({packageName})" : "";
        writer.WriteHeading(1, $"Samples: {title}{packageInfo}");

        var items = samples.Select((typedSample, i) =>
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

            return $"{typedSample.FullTypeName} - {description}: {url}";
        });

        writer.WriteArray(items);

        return writer.ToString().TrimEnd();
    }

    public static void WriteSamplesWithContent(
        MarkoutWriter writer,
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
            writer.WriteCodeBlockStart(lang);
            Console.Out.WriteLine(content);
            writer.WriteCodeBlockEnd();
        }
        else
        {
            writer.WriteParagraph("*Failed to fetch sample content*");
        }
    }

    public static void WriteSamplesTitle(
        MarkoutWriter writer,
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
