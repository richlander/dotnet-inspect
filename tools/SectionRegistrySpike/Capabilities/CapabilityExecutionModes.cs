namespace SectionRegistrySpike.Capabilities;

/// <summary>
/// Execution strategies that may authorize a static plan entry. Describe and structural discovery
/// never execute plans, so they do not need enum values.
/// </summary>
[Flags]
public enum CapabilityExecutionModes
{
    None = 0,
    Probe = 1 << 0,
    Detailed = 1 << 1,
    Explicit = 1 << 2,
    All = Probe | Detailed | Explicit,
}
