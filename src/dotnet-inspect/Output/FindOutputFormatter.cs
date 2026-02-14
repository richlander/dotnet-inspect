using DotnetInspector.Commands;
using DotnetInspector.Views;

namespace DotnetInspector.Output;

/// <summary>
/// Builds view models for find command results.
/// </summary>
public static class FindOutputFormatter
{
    public static FindResultView BuildView(List<TypeSearchResult> results, string pattern, int totalCount, int? limit)
    {
        var showing = (limit.HasValue && totalCount > limit.Value) ? (int?)results.Count : null;

        return new FindResultView
        {
            Title = $"Find: {pattern}",
            Matches = totalCount,
            Showing = showing,
            Description = results.Count == 0 ? "No types found matching the pattern." : null,
            Rows = results.Count == 0 ? null : results
                .Select(r => new FindRow(
                    pattern, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r)))
                .ToList()
        };
    }

    public static FindResultView BuildMultiPatternView(Dictionary<string, List<TypeSearchResult>> resultsByPattern)
    {
        var totalCount = resultsByPattern.Values.Sum(r => r.Count);

        return new FindResultView
        {
            Title = "Find Results",
            Matches = totalCount,
            Description = totalCount == 0 ? "No types found matching the pattern." : null,
            MultiPatternRows = totalCount == 0 ? null : resultsByPattern
                .SelectMany(kvp => kvp.Value.Select(r => new FindRow(
                    kvp.Key, r.TypeName, r.Namespace ?? "", r.Kind ?? "",
                    r.Assembly ?? "", FormatSource(r))))
                .ToList()
        };
    }

    private static string FormatSource(TypeSearchResult r)
        => r.SourceVersion != null ? $"{r.Source}@{r.SourceVersion}" : r.Source ?? "";
}
