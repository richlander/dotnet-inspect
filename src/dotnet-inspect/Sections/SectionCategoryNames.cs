namespace DotnetInspector.Sections;

/// <summary>
/// Well-known section category names used by <c>-S</c> and <c>-D</c>.
/// </summary>
public static class SectionCategoryNames
{
    public const string Audit = "@Audit";
    public const string Source = "@Source";

    /// <summary>Cheap-but-verbose surface sections (Async Methods, Custom Attributes, Resources, etc.).</summary>
    public const string Surface = "@Surface";

    /// <summary>
    /// Resource escape / exception-safety sections (Resource Escape Triage family). Members are
    /// <c>Escape: &lt;Resource&gt;</c> findings where a resource escapes its safe cleanup scope on an
    /// exception path (today only <c>Escape: Array Pool</c>).
    /// </summary>
    public const string Escape = "@Escape";

    /// <summary>
    /// Computed complement pole: sections surfaced by no listed category. Discovered only via
    /// <c>--schema</c> or exact name; excluded from the top-level <c>-D</c> catalog.
    /// </summary>
    public const string Hidden = "@Hidden";

    /// <summary>Curated group of the kind-scoped performance sections (library scope).</summary>
    public const string Performance = "@Performance";
}
