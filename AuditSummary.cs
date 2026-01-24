using MarkOut;

namespace DotnetInspector;

[MarkOutSerializable]
public class AuditSummary
{
    [MarkOutPropertyName("Total Assemblies")]
    public int TotalAssemblies { get; set; }

    [MarkOutPropertyName("Deterministic")]
    public int DeterministicCount { get; set; }

    [MarkOutPropertyName("SourceLink")]
    public int SourceLinkCount { get; set; }

    [MarkOutPropertyName("Embedded PDB")]
    public int EmbeddedPdbCount { get; set; }

    [MarkOutPropertyName("All Deterministic")]
    public bool AllDeterministic { get; set; }

    [MarkOutPropertyName("All Have SourceLink")]
    public bool AllHaveSourceLink { get; set; }
}
