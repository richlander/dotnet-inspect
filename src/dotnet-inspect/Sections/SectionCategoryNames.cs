namespace DotnetInspector.Sections;

/// <summary>
/// Well-known section category names used by <c>-S</c> and <c>-D</c>.
/// </summary>
public static class SectionCategoryNames
{
    public const string Audit = "@Audit";

    /// <summary>
    /// Actual source content: decompiled, original, and annotated source views plus source diffs
    /// (API/member scope). Distinct from <see cref="SourceLink"/>, which is about SourceLink/PDB
    /// provenance and availability rather than the source text itself.
    /// </summary>
    public const string Source = "@Source";

    /// <summary>
    /// SourceLink / PDB provenance sections (library scope): the source-file listing derived from
    /// the PDB plus the <c>Source Link: &lt;X&gt;</c> availability/integrity audit sections.
    /// </summary>
    public const string SourceLink = "@SourceLink";

    /// <summary>Cheap-but-verbose surface sections (Async Methods, Custom Attributes, Resources, etc.).</summary>
    public const string Surface = "@Surface";

    /// <summary>
    /// Resource escape / exception-safety sections (Resource Escape Triage family). Members are
    /// <c>Escape: &lt;Resource&gt;</c> findings where a resource escapes its safe cleanup scope on an
    /// exception path (today only <c>Escape: Array Pool</c>).
    /// </summary>
    public const string Escape = "@Escape";

    /// <summary>
    /// Ecosystem integration sections (library scope): the <c>Integration: &lt;X&gt;</c> members
    /// plus <c>Integration Opportunities</c>. Unlike <see cref="Escape"/> and
    /// <see cref="Performance"/>, whose applicability is a capability predicate, each member's
    /// applicability is evidence-based (a cheap reference probe), so the whole category
    /// hyper-subscribes away for a library with no integrations.
    /// </summary>
    public const string Integrations = "@Integrations";

    /// <summary>
    /// Computed complement pole: sections surfaced by no listed category. Discovered only via
    /// <c>--schema</c> or exact name; excluded from the top-level <c>-D</c> catalog.
    /// </summary>
    public const string Hidden = "@Hidden";

    /// <summary>Curated group of the kind-scoped performance sections (library scope).</summary>
    public const string Performance = "@Performance";
}
