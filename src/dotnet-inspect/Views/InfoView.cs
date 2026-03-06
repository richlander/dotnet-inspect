using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(Title), FieldLayout = FieldLayout.Table)]
public class InfoView
{
    [MarkoutIgnore]
    public string Title => "Info";

    public string Output { get; set; } = "";
    public string Time { get; set; } = "";

    [MarkoutSkipNull]
    public string? HTTP { get; set; }

    [MarkoutSkipNull]
    public string? Cache { get; set; }
}

[MarkoutContext(typeof(InfoView))]
public partial class InfoViewContext : MarkoutSerializerContext
{
}
