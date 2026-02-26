using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    DescriptionProperty = nameof(Description),
    FieldLayout = FieldLayout.Vertical)]
public class ExtensionsResultView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] [MarkoutSkipNull] public string? Description { get; set; }

    [MarkoutSection(Name = "Summary")]
    public List<ExtensionCountRow>? Counts { get; set; }

    [MarkoutSection(Name = "Extensions")]
    public List<ExtensionRow>? Extensions { get; set; }
}

[MarkoutSerializable]
public record ExtensionCountRow(string Type, string Extensions, string Via);

[MarkoutSerializable]
public record ExtensionRow(string Name, string Kind, string Class, string Library, string Source, string Type, string Via);
