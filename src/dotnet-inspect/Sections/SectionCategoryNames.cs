namespace DotnetInspector.Sections;

/// <summary>
/// Well-known section category names used by <c>-S</c> and <c>-D</c>.
/// </summary>
public static class SectionCategoryNames
{
    /// <summary>
    /// The library command's ordinary identity, relationship, diagnostic, and dense-signal
    /// sections. Together with <see cref="Surface"/>, this forms the library base scope.
    /// </summary>
    public const string Library = "@Library";

    public const string Audit = "@Audit";

    /// <summary>
    /// Actual source content: decompiled, original, and annotated source views plus source diffs
    /// (API/member scope). Distinct from <see cref="SourceLink"/>, which is about SourceLink/PDB
    /// provenance and availability rather than the source text itself.
    /// </summary>
    public const string Source = "@Source";

    /// <summary>
    /// SourceLink / PDB provenance sections (library scope): the source-file listing derived from
    /// the PDB plus the <c>SourceLink: &lt;X&gt;</c> availability/integrity audit sections.
    /// </summary>
    public const string SourceLink = "@SourceLink";

    /// <summary>
    /// The command's ordinary API and metadata surface sections. At library scope this is a base
    /// category alongside <see cref="Library"/>.
    /// </summary>
    public const string Surface = "@Surface";

    /// <summary>
    /// Coordinate-scoped evidence produced for an IL offset. The members use the
    /// <c>Context: &lt;Leaf&gt;</c> family name and become effective only when the coordinate
    /// carrier is present.
    /// </summary>
    public const string Context = "@Context";

    /// <summary>
    /// Ecosystem integration sections (library scope): the <c>Integration: &lt;X&gt;</c> members
    /// plus <c>Integration Opportunities</c>. Unlike <see cref="Performance"/>, whose
    /// applicability is a capability predicate, each member's applicability is evidence-based
    /// (a cheap reference probe), so the whole category hyper-subscribes away for a library
    /// with no integrations.
    /// </summary>
    public const string Integrations = "@Integrations";

    /// <summary>
    /// Package file listings scoped to a layout root or document kind: the
    /// <c>Package &lt;X&gt; file(s)</c> members. The plain <c>Package files</c> section is the
    /// whole-package listing rather than a subset, so it is deliberately not a member;
    /// including it would render most rows twice.
    /// </summary>
    public const string Files = "@Files";

    /// <summary>
    /// Computed complement pole: sections surfaced by no listed category. Discovered only via
    /// <c>--schema</c> or exact name; excluded from the top-level <c>-D</c> catalog.
    /// </summary>
    public const string Hidden = "@Hidden";

    /// <summary>Curated group of the kind-scoped performance sections (library scope).</summary>
    public const string Performance = "@Performance";

    /// <summary>
    /// Raw ECMA-335 metadata sections (library scope): one <c>Metadata: &lt;Table&gt;</c> section
    /// per projected table, plus <c>Metadata: Image</c> for the image-level facts that are not
    /// rows. Every member is explicit-only, so this door is the discovery and selection
    /// affordance for the group, never the mechanism that keeps raw tables out of the default
    /// view -- that is <see cref="SectionEntry{TModel}.ExplicitOnly"/>.
    /// </summary>
    public const string Metadata = "@Metadata";
}
