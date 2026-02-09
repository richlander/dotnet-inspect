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
[MarkoutSerializable(TitleProperty = nameof(Title))]
public class PackageDependenciesView
{
    [MarkoutIgnore]
    public string Title { get; set; } = "";

    public string Package { get; set; } = "";

    public string Version { get; set; } = "";

    [MarkoutSkipNull]
    [MarkoutPropertyName("TFM")]
    public string? Tfm { get; set; }

    [MarkoutIgnoreInTable]
    public List<TreeNode> Dependencies { get; set; } = [];
}

[MarkoutContext(typeof(PackageDependenciesView))]
public partial class PackageDependenciesContext : MarkoutSerializerContext
{
}
