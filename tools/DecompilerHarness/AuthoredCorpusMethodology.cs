namespace ILInspector.DecompilerHarness;

/// <summary>
/// Version of the authored-corpus attribution methodology.
/// </summary>
static class AuthoredCorpusMethodology
{
    /// <summary>
    /// v1 = failed-compile substitution control. v2 adds span attribution for
    /// shell-independent body errors. v3 preserves the final target metadata in
    /// the shared authored-body substitution and adds the fidelity control for
    /// unaided RTS <c>ValidDifferent</c> IL-diff rows. The compiled substitution
    /// and fidelity controls are gated by <c>ValidDifferentFaultIsolationTests</c>;
    /// the aggregate partition and version stamp are gated by
    /// <c>AuthoredCorpusFrontierAttributionTests</c>.
    /// </summary>
    internal const int Version = 3;

    /// <summary>
    /// Returns the invalid-row attribution lineage for a known methodology.
    /// This mapping is deliberately explicit: v3 changed the shared substitution
    /// shell by preserving constructor-chain and modifier metadata, so its invalid
    /// product count is not ratcheted against v2 even when a particular corpus has
    /// no affected constructors. Unknown methodologies have no defined lineage.
    /// <c>AuthoredCorpusRatchetTests.InvalidAttributionLineages_AreExplicit</c>
    /// and <c>Ratchet_CompleteUnknownMethodologyBaselineIsRefused</c> gate the map.
    /// </summary>
    internal static int? InvalidAttributionLineage(int methodologyVersion)
        => methodologyVersion switch
        {
            1 => 1,
            2 => 2,
            3 => 3,
            _ => null,
        };

    internal static bool IsKnownVersion(int methodologyVersion)
        => InvalidAttributionLineage(methodologyVersion) is not null;
}
