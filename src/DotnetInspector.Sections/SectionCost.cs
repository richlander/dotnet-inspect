using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Declared latency/output budget for a section. Governs which curated verbosity views may
/// auto-run the section. Independent of <see cref="SectionSizeClass"/> (how many rows) — this
/// axis is about how costly the section is to produce.
/// </summary>
public enum SectionCost
{
    /// <summary>
    /// Cheap and bounded to produce. Eligible for every view including bare <c>-S</c>. A host may
    /// still plan a section's declared optional enrichment, such as acquiring a missing PDB
    /// outside <c>--offline</c>, under its network policy.
    /// </summary>
    NetworkFree,

    /// <summary>
    /// Bounded work that may touch the network or warm a PDB but stays within the default latency
    /// budget (roughly sub-second). Auto-runs only at <c>-v:d</c> (detailed); not at bare
    /// <c>-S</c> or <c>-v:n</c>.
    /// </summary>
    Moderated,

    /// <summary>
    /// Potentially large or slow: network fan-out, source-content download, or a whole-assembly
    /// scan that can produce thousands of rows. Never auto-run by any verbosity (not even
    /// <c>-v:d</c>); reachable only by exact name or an explicit category door.
    /// </summary>
    Unbounded,
}

public static class InspectionCostMapping
{
    public static SectionCost ToSectionCost(
        this InspectionCost cost,
        InspectionQueryDefinition query)
        => cost switch
        {
            InspectionCost.NetworkFree => SectionCost.NetworkFree,
            InspectionCost.Moderated => SectionCost.Moderated,
            InspectionCost.Unbounded => SectionCost.Unbounded,
            _ => throw new InvalidOperationException(
                $"Unknown inspection query cost for '{query.Name}'."),
        };
}
