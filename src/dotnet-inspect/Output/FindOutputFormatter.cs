using DotnetInspector.Models;
using DotnetInspector.Views;
using InertText;

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

        return new FindResultView(
            Field(title ?? "Find Results"),
            matchCount == 0 ? Prose("No types found matching the pattern.") : null)
        {
            Matches = matchCount,
            Results = matchCount == 0 ? null : results.Select(r => new FindRow(
                Field(r.Pattern),
                Field(r.Match == MatchKind.NotFound ? "-" : r.Type),
                Field(r.Match == MatchKind.NotFound ? "-" : r.Namespace ?? ""),
                Field(r.Match == MatchKind.NotFound ? "-" : r.Kind),
                Field(r.Match == MatchKind.NotFound ? "-" : r.Library),
                r.Match == MatchKind.NotFound ? Field("-") : Source(r.Source, r.SourceVersion),
                Field(r.Match.ToString().ToLowerInvariant()),
                Field(r.Similarity.HasValue ? r.Similarity.Value.ToString("0.00") : "-")
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
        return new FindMembersResultView(
            Field(title ?? "Find Members"),
            results.Count == 0 ? Prose("No members found matching the pattern.") : null)
        {
            Matches = results.Count,
            Results = results.Count == 0 ? null : results.Select(r => new FindMemberRow(
                Field(r.Pattern),
                Field(r.Member),
                Field(r.Kind),
                Field(r.DeclaringType),
                // The signature is composed from type names, so it carries a
                // hostile type spelling even when the member name is benign.
                Field(r.Signature ?? ""),
                Field(r.Library),
                Source(r.Source, r.SourceVersion)
            )).ToList()
        };
    }

    private static InertString Field(string value) => new(TextPolicy.Field, value);

    private static InertString Prose(string value) => new(TextPolicy.Prose, value);

    private static InertString Source(string? source, string? version)
        => string.IsNullOrEmpty(version)
            ? Field(source ?? "")
            : InertString.Format(TextPolicy.Field, $"{source}@{version}");
}
