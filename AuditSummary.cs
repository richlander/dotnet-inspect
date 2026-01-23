using MarkdownData;

namespace DotnetInspector;

[MdfSerializable]
public class AuditSummary
{
    [MdfPropertyName("Total Assemblies")]
    public int TotalAssemblies { get; set; }

    [MdfPropertyName("Deterministic")]
    public int DeterministicCount { get; set; }

    [MdfPropertyName("SourceLink")]
    public int SourceLinkCount { get; set; }

    [MdfPropertyName("Embedded PDB")]
    public int EmbeddedPdbCount { get; set; }

    [MdfPropertyName("All Deterministic")]
    public bool AllDeterministic { get; set; }

    [MdfPropertyName("All Have SourceLink")]
    public bool AllHaveSourceLink { get; set; }
}
