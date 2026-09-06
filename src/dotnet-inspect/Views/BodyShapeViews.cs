using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable]
public sealed record BodyShapeSummaryRow(string Kind, string Match, int Count)
{
    public string Kind { get; init; } = CSharpIdentifier.ContainRenderedText(Kind);
    public string Match { get; init; } = MarkoutInline.Code(Match);

    internal static List<BodyShapeSummaryRow> FromMatches(
        IEnumerable<ILInspector.Decompiler.BodyShapeMatch> matches)
        => BodyShapeSummary.FromMatches(matches)
            .Select(summary => new BodyShapeSummaryRow(summary.Kind, summary.Match, summary.Count))
            .ToList();
}

[MarkoutSerializable]
public sealed record BodyShapeRow(
    string Kind,
    string Member,
    string Token,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string Match)
{
    public string Kind { get; init; } = CSharpIdentifier.ContainRenderedText(Kind);
    public string Member { get; init; } = MarkoutInline.Code(Member);
    public string Token { get; init; } = MarkoutInline.Code(Token);
    [MarkoutPropertyName("Start Line")]
    public int StartLine { get; init; } = StartLine;
    [MarkoutPropertyName("Start Column")]
    public int StartColumn { get; init; } = StartColumn;
    [MarkoutPropertyName("End Line")]
    public int EndLine { get; init; } = EndLine;
    [MarkoutPropertyName("End Column")]
    public int EndColumn { get; init; } = EndColumn;
    public string Match { get; init; } = MarkoutInline.Code(Match);

    internal static BodyShapeRow FromMatch(ILInspector.Decompiler.BodyShapeMatch match)
        => new(
            match.Kind,
            match.Member,
            $"0x{match.MethodToken:X8}",
            match.Extent.StartLine + 1,
            match.Extent.StartColumn + 1,
            match.Extent.EndLine + 1,
            match.Extent.EndColumn + 1,
            match.Text);
}
