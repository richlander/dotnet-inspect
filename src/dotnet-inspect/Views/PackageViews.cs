using System.Text.Json.Serialization;
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

    [MarkoutIgnore]
    public string Package { get; set; } = "";

    [MarkoutIgnore]
    public string Version { get; set; } = "";

    [MarkoutIgnore]
    public string? Tfm { get; set; }

    [JsonIgnore]
    [MarkoutIgnoreInTable]
    public List<MarkoutField> Identity => GetIdentityFields();

    [MarkoutIgnoreInTable]
    public List<TreeNode> Dependencies { get; set; } = [];

    private List<MarkoutField> GetIdentityFields()
    {
        var fields = new List<MarkoutField>();
        if (!string.IsNullOrEmpty(Package))
            fields.Add(new("Package", Package));
        if (!string.IsNullOrEmpty(Version))
            fields.Add(new("Version", Version));
        if (!string.IsNullOrEmpty(Tfm))
            fields.Add(new("TFM", Tfm));
        return fields;
    }
}

[MarkoutContext(typeof(PackageDependenciesView))]
public partial class PackageDependenciesContext : MarkoutSerializerContext
{
}
