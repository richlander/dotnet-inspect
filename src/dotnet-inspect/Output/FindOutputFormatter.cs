using DotnetInspector.Models;
using DotnetInspector.Views;

namespace DotnetInspector.Output;

/// <summary>
/// Builds view models for find command results.
/// </summary>
public static class FindOutputFormatter
{
    /// <summary>
    /// Builds a unified view from raw find results. Works with both OneLineWriter and MarkdownWriter.
    /// </summary>
    public static FindResultView BuildView(
        List<TypeFindResult> results,
        string? title = null)
    {
        var matchCount = results.Count(r => r.Match != MatchKind.NotFound);

        return new FindResultView
        {
            Title = title ?? "Find Results",
            Matches = matchCount,
            Description = matchCount == 0 ? "No types found matching the pattern." : null,
            Results = results.Count == 0 ? null : results.Select(r => new FindRow(
                r.Pattern,
                r.Match == MatchKind.NotFound ? "-" : r.Type,
                r.Match == MatchKind.NotFound ? "-" : r.Namespace,
                r.Match == MatchKind.NotFound ? "-" : r.Kind,
                r.Match == MatchKind.NotFound ? "-" : r.Library,
                r.Match == MatchKind.NotFound ? "-" : FormatSource(r.Source, r.SourceVersion),
                r.Match.ToString().ToLowerInvariant(),
                r.Similarity.HasValue ? r.Similarity.Value.ToString("0.00") : "-"
            )).ToList()
        };
    }

    private static string FormatSource(string source, string? version)
        => string.IsNullOrEmpty(version) ? source : $"{source}@{version}";
}
