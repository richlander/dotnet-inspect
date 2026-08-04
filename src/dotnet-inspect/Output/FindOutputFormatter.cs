using DotnetInspector.Models;
using DotnetInspector.Views;
using ILInspector.CSharp;

namespace DotnetInspector.Output;

/// <summary>
/// Builds view models for find command results.
/// </summary>
public static class FindOutputFormatter
{
    /// <summary>
    /// Builds a unified view from raw find results. Works with both table and Markdown formatters.
    /// </summary>
    public static FindResultView BuildView(
        List<TypeFindResult> results,
        string? title = null)
    {
        var matchCount = results.Count(r => r.Match != MatchKind.NotFound);

        return new FindResultView
        {
            Title = CSharpIdentifier.ContainRenderedText(title ?? "Find Results"),
            Matches = matchCount,
            Description = matchCount == 0 ? "No types found matching the pattern." : null,
            Results = matchCount == 0 ? null : results.Select(r => new FindRow(
                CSharpIdentifier.ContainRenderedText(r.Pattern),
                r.Match == MatchKind.NotFound ? "-" : CSharpIdentifier.ContainRenderedText(r.Type),
                r.Match == MatchKind.NotFound ? "-" : CSharpIdentifier.ContainRenderedText(r.Namespace ?? ""),
                r.Match == MatchKind.NotFound ? "-" : r.Kind,
                r.Match == MatchKind.NotFound ? "-" : CSharpIdentifier.ContainRenderedText(r.Library),
                r.Match == MatchKind.NotFound ? "-" : SourceColumn.Format(r.Source, r.SourceVersion),
                r.Match.ToString().ToLowerInvariant(),
                r.Similarity.HasValue ? r.Similarity.Value.ToString("0.00") : "-"
            )).ToList()
        };
    }

    /// <summary>
    /// Builds a unified member view from raw member-search results. Works with both table and Markdown
    /// formatters. The declaring type's full name is shown in the Type column.
    /// </summary>
    public static FindMembersResultView BuildMemberView(
        List<MemberFindResult> results,
        string? title = null)
    {
        return new FindMembersResultView
        {
            Title = CSharpIdentifier.ContainRenderedText(title ?? "Find Members"),
            Matches = results.Count,
            Description = results.Count == 0 ? "No members found matching the pattern." : null,
            Results = results.Count == 0 ? null : results.Select(r => new FindMemberRow(
                CSharpIdentifier.ContainRenderedText(r.Pattern),
                CSharpIdentifier.ContainRenderedText(r.Member),
                r.Kind,
                CSharpIdentifier.ContainRenderedText(r.DeclaringType),
                // The signature is composed from type names, so it carries a
                // hostile type spelling even when the member name is benign.
                CSharpIdentifier.ContainRenderedText(r.Signature ?? ""),
                CSharpIdentifier.ContainRenderedText(r.Library),
                SourceColumn.Format(r.Source, r.SourceVersion)
            )).ToList()
        };
    }
}
