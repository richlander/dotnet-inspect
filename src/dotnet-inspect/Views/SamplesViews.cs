using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(Title))]
public class SamplesListView
{
    [MarkoutIgnore] public string Title { get; set; } = "";

    public List<SampleRow>? Samples { get; set; }
}

[MarkoutSerializable]
public record SampleRow(string Type, string Description, string Url);
