using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(Title))]
public class TipsView
{
    [MarkoutIgnore]
    public string Title => "Tips";

    [MarkoutSection]
    public List<TipRow> Commands { get; set; } = [];
}

[MarkoutSerializable]
public record TipRow(string Command, string Description);

[MarkoutContext(typeof(TipsView))]
public partial class TipsViewContext : MarkoutSerializerContext
{
}
