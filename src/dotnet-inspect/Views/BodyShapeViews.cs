using Markout;
using DotnetInspector.Output;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public sealed class BodyShapeResultView
{
    [MarkoutIgnore]
    public string Title
    {
        get => field;
        init => field = CSharpIdentifier.ContainRenderedText(value);
    } = "";

    [MarkoutIgnore]
    [MarkoutSkipNull]
    public string? Description
    {
        get => field;
        init => field = value is null ? null : CSharpIdentifier.ContainRenderedText(value);
    }

    [MarkoutSection(Name = "Matches")]
    public List<BodyShapeRow>? Matches { get; init; }
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
