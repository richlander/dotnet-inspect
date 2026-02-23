using DotnetInspector.Commands;
using DotnetInspector.Views;

namespace DotnetInspector.Output;

/// <summary>
/// Builds view models for find command results.
/// </summary>
public static class FindOutputFormatter
{
    /// <summary>
    /// Builds a view for single-pattern results.
    /// </summary>
    public static FindResultView BuildView(
        List<TypeSearchResult> results,
        string pattern,
        int totalCount,
        int? limit,
        List<TypeSearchResult>? partialMatches = null,
        Dictionary<string, double>? partialSimilarities = null)
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
            Results = results.Count == 0 ? null : results
                .Select(r => new FindRow(
                    pattern, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r), "1.00"))
                .ToList(),
            PartialMatches = partialMatches == null || partialMatches.Count == 0 ? null : partialMatches
                .Select(r => new FindRow(
                    pattern, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r),
                    FormatSimilarity(partialSimilarities?.GetValueOrDefault(r.FullName, 0.5) ?? 0.5)))
                .ToList()
        };
    }

    /// <summary>
    /// Builds a view for multi-pattern results.
    /// </summary>
    public static FindResultView BuildMultiPatternView(
        Dictionary<string, List<TypeSearchResult>> resultsByPattern,
        Dictionary<string, List<TypeSearchResult>>? partialMatchesByPattern = null,
        List<string>? notFoundPatterns = null,
        Dictionary<string, Dictionary<string, double>>? similarityByPattern = null)
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
            Results = totalCount == 0 ? null : resultsByPattern
                .SelectMany(kvp => kvp.Value.Select(r => new FindRow(
                    kvp.Key, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r), "1.00")))
                .ToList(),
            PartialMatches = partialCount == 0 ? null : partialMatchesByPattern!
                .SelectMany(kvp => kvp.Value.Select(r => new FindRow(
                    kvp.Key, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r),
                    FormatSimilarity(similarityByPattern?.GetValueOrDefault(kvp.Key)?.GetValueOrDefault(r.FullName, 0.5) ?? 0.5))))
                .ToList(),
            NotFoundPatterns = notFoundCount == 0 ? null : notFoundPatterns
        };
    }

    private static string FormatSource(TypeSearchResult r)
        => r.SourceVersion != null ? $"{r.Source}@{r.SourceVersion}" : r.Source ?? "";

    private static string FormatSimilarity(double similarity)
        => similarity.ToString("0.00");
}
