using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(Title))]
public class SamplesListView
{
    [MarkoutIgnore] public string Title { get; set; } = "";

    [MarkoutSection(Name = "Samples")]
    public List<string>? Items { get; set; }
}
