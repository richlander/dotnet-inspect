using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.LineBreaksDoubleSpace)]
public class DiffOneLineView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    public string Versions { get; set; } = "";
    public string Summary { get; set; } = "";

    [MarkoutSection(Name = "Changes")]
    public List<DiffOneLineRow>? Rows { get; set; }
}

[MarkoutSerializable]
public record DiffOneLineRow(string Change, string Type, string Detail);
