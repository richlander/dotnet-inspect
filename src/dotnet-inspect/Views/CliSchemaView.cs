using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public class CliSchemaView
{
    [MarkoutIgnore]
    public string Name { get; set; } = "";

    [MarkoutIgnore]
    public string Version { get; set; } = "";

    [MarkoutIgnore]
    public string Title => $"{Name} {Version}";

    [MarkoutIgnore]
    public string Description { get; set; } = "";

    [MarkoutIgnoreInTable]
    public List<TreeNode> Commands { get; set; } = [];
}

[MarkoutContext(typeof(CliSchemaView))]
public partial class CliSchemaContext : MarkoutSerializerContext
{
}
