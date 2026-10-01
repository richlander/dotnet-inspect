namespace DotnetInspector.Sections;

/// <summary>
/// A host-neutral automatic section view over one subject.
/// </summary>
public enum SectionViewLevel
{
    /// <summary>Compact identity or context only.</summary>
    Quiet,

    /// <summary>The subject's authored high-value section.</summary>
    Minimal,

    /// <summary>Bounded fixed, terse, and informative sections.</summary>
    Normal,

    /// <summary>Every applicable bounded-cost section.</summary>
    Detailed,
}
