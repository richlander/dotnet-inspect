namespace DotnetInspector.Sections;

/// <summary>
/// Declared <b>growth</b> class for a section — how its row count behaves across the entire
/// universe of packages, not the count for any one target. This is a stable, package-independent
/// declaration chosen by the section author: it lets the curated verbosity ladder decide
/// membership structurally (so a section's absence always means "not applicable", never "too long
/// for this package") without running the section first.
/// </summary>
/// <remarks>
/// The tiers describe growth, not a measured magnitude:
/// <list type="bullet">
///   <item><see cref="Fixed"/> — row set is structurally constant across all packages
///   (a fact/summary table). Example row counts do not vary with package content.</item>
///   <item><see cref="Terse"/> — grows with the package but stays small in practice
///   (≈ ≤ 12 rows).</item>
///   <item><see cref="Informative"/> — grows with the package, medium (≈ ≤ 24 rows).</item>
///   <item><see cref="Verbose"/> — grows without a meaningful bound (may greatly exceed 24 rows).</item>
/// </list>
/// Ordering matters: the curated ladder uses <c>&lt;=</c> comparisons, so <see cref="Fixed"/> must
/// sort below every other tier.
/// </remarks>
public enum SectionSizeClass
{
    /// <summary>
    /// Structurally constant across every package (identity/signal/summary tables). This is the
    /// bare <c>-S</c> overview tier: the row set does not grow with the target, so the overview
    /// cannot become long. Applicability still applies on top — a section with nothing to report
    /// drops out, so absence means "not applicable", never "too long for this package".
    /// </summary>
    Fixed,

    /// <summary>Grows with the package but stays small (≈ ≤ 12 rows). Shown from <c>-v:n</c> upward.</summary>
    Terse,

    /// <summary>Grows with the package, medium (≈ ≤ 24 rows). Shown from <c>-v:n</c> upward.</summary>
    Informative,

    /// <summary>Grows without a meaningful bound. Shown only at <c>-v:d</c> or by explicit selection.</summary>
    Verbose,
}
