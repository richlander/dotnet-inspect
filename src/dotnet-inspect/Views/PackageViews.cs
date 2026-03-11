using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// View model for file tree output (minimal wrapper for tree serialization).
/// </summary>
public class FileTreeView
{
    [MarkoutIgnoreInTable]
    public List<TreeNode> Files { get; set; } = [];
}

[MarkoutContext(typeof(FileTreeView))]
public partial class FileTreeContext : MarkoutSerializerContext
{
}

/// <summary>
/// View model for package dependency tree output (--dependencies).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), Layout = DocumentLayout.Tree)]
public class PackageDependenciesView
{
    [MarkoutIgnore]
    public string Title { get; set; } = "";

    [MarkoutIgnoreInTable]
    public List<TreeNode> Dependencies { get; set; } = [];
}

[MarkoutContext(typeof(PackageDependenciesView))]
public partial class PackageDependenciesContext : MarkoutSerializerContext
{
}

/// <summary>
/// View model for empty dependency tree output (--dependencies with zero deps for a TFM).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title), DescriptionProperty = nameof(Description))]
public class EmptyDepsView
{
    [MarkoutIgnore] public string Title { get; set; } = "";
    [MarkoutIgnore] public string? Description { get; set; }
}
