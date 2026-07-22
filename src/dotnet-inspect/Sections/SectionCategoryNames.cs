namespace DotnetInspector.Sections;

/// <summary>
/// Well-known section category names used by <c>-S</c> and <c>-D</c>.
/// </summary>
public static class SectionCategoryNames
{
    public const string Audit = "@Audit";
    public const string Source = "@Source";

    /// <summary>Cheap-but-verbose surface sections (Async Methods, Custom Attributes, etc.).</summary>
    public const string Surface = "@Surface";

    /// <summary>Manifest-resource sections (Resources, Resource Triage).</summary>
    public const string Resources = "@Resources";

    /// <summary>
    /// Computed complement pole: sections surfaced by no listed category. Discovered only via
    /// <c>--schema</c> or exact name; excluded from the top-level <c>-D</c> catalog.
    /// </summary>
    public const string Hidden = "@Hidden";

    /// <summary>Curated group of the kind-scoped performance sections (library scope).</summary>
    public const string Performance = "@Performance";
}
