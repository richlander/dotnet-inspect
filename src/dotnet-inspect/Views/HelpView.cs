using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable]
public class HelpView
{
    [MarkoutPropertyName("Description")]
    [MarkoutIgnoreInTable]
    public List<string>? Description { get; set; }

    [MarkoutPropertyName("Usage")]
    [MarkoutIgnoreInTable]
    public List<string>? Usage { get; set; }

    [MarkoutPropertyName("Arguments")]
    [MarkoutIgnoreInTable]
    public List<string>? Arguments { get; set; }

    [MarkoutPropertyName("Options")]
    [MarkoutIgnoreInTable]
    public List<string>? Options { get; set; }

    [MarkoutPropertyName("Commands")]
    [MarkoutIgnoreInTable]
    public List<string>? Commands { get; set; }
}

[MarkoutContext(typeof(HelpView))]
public partial class HelpViewContext : MarkoutSerializerContext
{
}
