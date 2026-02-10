using System.Text.Json.Serialization;
using DotnetInspector.Models;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// Standalone view model for assembly dependencies (--dependencies).
/// </summary>
[MarkoutSerializable(TitleProperty = nameof(Title))]
public class AssemblyDependenciesView
{
    [MarkoutIgnore]
    public string Title { get; set; } = "";

    [MarkoutIgnore]
    public string? AssemblyName { get; set; }

    [MarkoutIgnore]
    public string? Version { get; set; }

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
        if (!string.IsNullOrEmpty(AssemblyName))
            fields.Add(new("Library", AssemblyName));
        if (!string.IsNullOrEmpty(Version))
            fields.Add(new("Version", Version));
        if (!string.IsNullOrEmpty(Tfm))
            fields.Add(new("TFM", Tfm));
        return fields;
    }

    public static AssemblyDependenciesView FromAudit(AssemblyAudit audit)
    {
        var view = new AssemblyAuditView(audit);
        return new AssemblyDependenciesView
        {
            Title = audit.FileName,
            AssemblyName = audit.AssemblyInfo?.AssemblyName,
            Version = audit.AssemblyInfo?.AssemblyVersion,
            Tfm = audit.Tfm ?? audit.AssemblyInfo?.TargetFramework,
            Dependencies = view.DependenciesSection ?? []
        };
    }
}

[MarkoutContext(typeof(AssemblyDependenciesView))]
public partial class AssemblyDependenciesContext : MarkoutSerializerContext
{
}
