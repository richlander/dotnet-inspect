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
    [MarkoutIgnoreColumnWhen(nameof(KindIsUniform), nameof(BodyShapeRow.Kind))]
    public List<BodyShapeRow>? Matches { get; init; }

    public static bool KindIsUniform(List<BodyShapeRow>? rows)
        => rows?.Select(row => row.Kind).Distinct(StringComparer.Ordinal).Count() <= 1;
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
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(BodyShapeResultView))]
[MarkoutContext(typeof(BodyShapeRow))]
public partial class BodyShapeViewContext : MarkoutSerializerContext
{
}
