using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for type dependency tree output.
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title))]
public class TypeDependenciesView
{
    [MarkoutIgnore]
    public string Title { get; set; } = "";

    [MarkoutIgnoreInTable]
    public List<TreeNode> Dependencies { get; set; } = [];
}

[MarkoutContext(typeof(TypeDependenciesView))]
public partial class TypeDependenciesContext : MarkoutSerializerContext
{
}
