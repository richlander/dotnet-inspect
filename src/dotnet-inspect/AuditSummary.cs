using Markout;

namespace DotnetInspector;

public class AuditSummary
{
    [MarkoutPropertyName("Total Assemblies")]
    public int TotalAssemblies { get; set; }

    [MarkoutPropertyName("Deterministic")]
    public int DeterministicCount { get; set; }

    [MarkoutPropertyName("SourceLink")]
    public int SourceLinkCount { get; set; }

    [MarkoutPropertyName("Embedded PDB")]
    public int EmbeddedPdbCount { get; set; }

    [MarkoutPropertyName("All Deterministic")]
    public bool AllDeterministic { get; set; }

    [MarkoutPropertyName("All Have SourceLink")]
    public bool AllHaveSourceLink { get; set; }
}
