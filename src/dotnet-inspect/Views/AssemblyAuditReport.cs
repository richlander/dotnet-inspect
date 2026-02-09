using DotnetInspector.Models;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(TitleProperty = nameof(Title))]
public class AssemblyAuditReport
{
    public string Title { get; set; } = "";

    [MarkoutSection(Name = "Assemblies")]
    public List<AssemblyAuditView> Assemblies { get; set; } = [];
}
