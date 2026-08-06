namespace ILInspector.DecompilerHarness;

/// <summary>
/// Version of the authored-corpus attribution methodology.
/// </summary>
static class AuthoredCorpusMethodology
{
    /// <summary>
    /// v1 = failed-compile substitution control. v2 adds span attribution for
    /// shell-independent body errors. v3 adds the authored-body fidelity control
    /// for unaided RTS <c>ValidDifferent</c> IL-diff rows. The compiled control is
    /// gated by <c>ValidDifferentFaultIsolationTests</c>; the aggregate partition
    /// and version stamp are gated by
    /// <c>AuthoredCorpusFrontierAttributionTests</c>.
    /// </summary>
    internal const int Version = 3;
}
