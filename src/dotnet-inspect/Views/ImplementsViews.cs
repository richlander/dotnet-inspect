using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Table)]
public class ImplementsResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }
    [MarkoutIgnore] public int Matches { get; set; }

    [MarkoutSection(Name = "Implementers")]
    public List<ImplementerRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record ImplementerRow(
    string Type, string Kind, string Relationship, string Library, string Source);
