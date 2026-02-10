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
        List<MarkoutField> fields = [];
        if (!string.IsNullOrEmpty(AssemblyName))
            fields.Add(new("Library", AssemblyName));
        if (!string.IsNullOrEmpty(Version))
            fields.Add(new("Version", Version));
        if (!string.IsNullOrEmpty(Tfm))
            fields.Add(new("TFM", Tfm));
        return fields;
    }

    public static AssemblyDependenciesView FromInspection(LibraryInspection inspection)
    {
        var view = new LibraryInspectionView(inspection);
        return new AssemblyDependenciesView
        {
            Title = inspection.FileName,
            AssemblyName = inspection.AssemblyInfo?.AssemblyName,
            Version = inspection.AssemblyInfo?.AssemblyVersion,
            Tfm = inspection.Tfm ?? inspection.AssemblyInfo?.TargetFramework,
            Dependencies = view.DependenciesSection ?? []
        };
    }
}

[MarkoutContext(typeof(AssemblyDependenciesView))]
public partial class AssemblyDependenciesContext : MarkoutSerializerContext
{
}
