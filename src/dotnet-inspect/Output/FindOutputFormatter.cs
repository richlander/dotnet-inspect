using DotnetInspector.Commands;
using DotnetInspector.Views;

namespace DotnetInspector.Output;

/// <summary>
/// Builds view models for find command results.
/// </summary>
public static class FindOutputFormatter
{
    public static FindResultView BuildView(
        List<TypeSearchResult> results,
        string pattern,
        int totalCount,
        int? limit,
        List<TypeSearchResult>? partialMatches = null)
    {
        var showing = (limit.HasValue && totalCount > limit.Value) ? (int?)results.Count : null;

        return new FindResultView
        {
            Title = $"Find: {pattern}",
            Matches = totalCount,
            Showing = showing,
            Description = results.Count == 0 && (partialMatches == null || partialMatches.Count == 0)
                ? "No types found matching the pattern."
                : null,
            Rows = results.Count == 0 ? null : results
                .Select(r => new FindRow(
                    pattern, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r)))
                .ToList(),
            PartialMatches = partialMatches == null || partialMatches.Count == 0 ? null : partialMatches
                .Select(r => new FindRow(
                    pattern, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r)))
                .ToList()
        };
    }

    public static FindResultView BuildMultiPatternView(
        Dictionary<string, List<TypeSearchResult>> resultsByPattern,
        Dictionary<string, List<TypeSearchResult>>? partialMatchesByPattern = null,
        List<string>? notFoundPatterns = null)
    {
        var totalCount = resultsByPattern.Values.Sum(r => r.Count);
        var partialCount = partialMatchesByPattern?.Values.Sum(r => r.Count) ?? 0;
        var notFoundCount = notFoundPatterns?.Count ?? 0;

        return new FindResultView
        {
            Title = "Find Results",
            Matches = totalCount,
            Description = totalCount == 0 && partialCount == 0 && notFoundCount == 0
                ? "No types found matching the patterns."
                : null,
            MultiPatternRows = totalCount == 0 ? null : resultsByPattern
                .SelectMany(kvp => kvp.Value.Select(r => new FindRow(
                    kvp.Key, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r))))
                .ToList(),
            MultiPatternPartialMatches = partialCount == 0 ? null : partialMatchesByPattern!
                .SelectMany(kvp => kvp.Value.Select(r => new FindRow(
                    kvp.Key, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r))))
                .ToList(),
            NotFoundPatterns = notFoundCount == 0 ? null : notFoundPatterns
        };
    }

    private static string FormatSource(TypeSearchResult r)
        => r.SourceVersion != null ? $"{r.Source}@{r.SourceVersion}" : r.Source ?? "";
}
