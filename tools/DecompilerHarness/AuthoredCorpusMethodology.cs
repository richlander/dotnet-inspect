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
    /// unaided RTS <c>ValidDifferent</c> IL-diff rows. v4 starts the native RTS
    /// outcome lineage: references are frozen from the owner-selected closure,
    /// and source correspondence requires a product body graded <c>Full</c>.
    /// v5 starts a new invalid-attribution lineage: span attribution reads the
    /// product-issued body ranges of the decompiled unit and the authored-body
    /// substitution instead of re-locating each body by name in a Roslyn parse,
    /// so it can now attribute targets the name search could not uniquely find.
    /// The source-outcome lineage is unchanged.
    /// The compiled substitution and fidelity controls are gated by
    /// <c>ValidDifferentFaultIsolationTests</c>; the aggregate partition and
    /// version stamp are gated by <c>AuthoredCorpusFrontierAttributionTests</c>.
    /// </summary>
    internal const int Version = 5;

    /// <summary>
    /// Returns the lineage that defines the top-level <c>valid</c>,
    /// <c>correct</c>, and raw <c>invalid</c> populations. v4 completed the native
    /// RTS measurement transition and added the Full-fidelity admission boundary,
    /// so its source-outcome counts are not compared with legacy-shell runs.
    /// </summary>
    internal static int? SourceOutcomeLineage(int methodologyVersion)
        => methodologyVersion switch
        {
            1 or 2 or 3 => 1,
            4 or 5 => 2,
            _ => null,
        };

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
            4 => 4,
            5 => 5,
            _ => null,
        };

    internal static bool IsKnownVersion(int methodologyVersion)
        => SourceOutcomeLineage(methodologyVersion) is not null
            && InvalidAttributionLineage(methodologyVersion) is not null;
}
