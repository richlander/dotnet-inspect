using Markout;

namespace DotnetInspector;

[MarkoutSerializable(TitleProperty = nameof(Title))]
public class AssemblyAuditReport
{
    public string Title { get; set; } = "";

    [MarkoutSection(Name = "Assemblies")]
    public List<AssemblyAudit> Assemblies { get; set; } = [];
}
