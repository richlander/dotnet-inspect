namespace DotnetInspector.Sections;

/// <summary>
/// Declared size hint for a section, chosen by the section author from the section's expected
/// and stress-tested row count. This is a stable declaration, not a measured value: it lets the
/// curated verbosity ladder keep bounded views bounded without running the section first.
/// </summary>
/// <remarks>
/// Thresholds are guidance for the author, not enforced at runtime:
/// <list type="bullet">
///   <item><see cref="Terse"/> — typically ≤ 12 rows.</item>
///   <item><see cref="Informative"/> — typically ≤ 24 rows.</item>
///   <item><see cref="Verbose"/> — may exceed 24 rows (bounded or effectively unbounded).</item>
/// </list>
/// </remarks>
public enum SectionSizeClass
{
    /// <summary>Small, high-signal section (≈ ≤ 12 rows). Shown from bare <c>-S</c> upward.</summary>
    Terse,

    /// <summary>Medium section (≈ ≤ 24 rows). Shown from <c>-v:n</c> upward.</summary>
    Informative,

    /// <summary>Large section (&gt; 24 rows). Shown only at <c>-v:d</c> or by explicit selection.</summary>
    Verbose,
}
