namespace DotnetInspector;

public class AuditSummary
{
    public int TotalAssemblies { get; set; }
    public int DeterministicCount { get; set; }
    public int SourceLinkCount { get; set; }
    public int EmbeddedPdbCount { get; set; }
    public bool AllDeterministic { get; set; }
    public bool AllHaveSourceLink { get; set; }
}
