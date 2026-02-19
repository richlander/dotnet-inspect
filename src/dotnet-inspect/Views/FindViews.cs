using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.LineBreaksDoubleSpace)]
public class FindResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }
    public int Matches { get; set; }
    [MarkoutSkipNull] public int? Showing { get; set; }

    [MarkoutSection(Name = "Results", IgnoreProperty = nameof(FindRow.Pattern))]
    public List<FindRow>? Rows { get; set; }

    [MarkoutSection(Name = "Results")]
    public List<FindRow>? MultiPatternRows { get; set; }
}

[MarkoutSerializable]
public record FindRow(
    string Pattern,
    string Type,
    string Namespace,
    string Kind,
    string Library,
    string Source);
