namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Well-known section category names used by <c>-S</c> and <c>-D</c>.
/// </summary>
public static class SectionCategoryNames
{
    public const string Relations = "@Relations";

    /// <summary>
    /// The library command's ordinary identity, relationship, diagnostic, and dense-signal
    /// sections. Together with <see cref="Surface"/>, this forms the library base scope.
    /// </summary>
    public const string Library = "@Library";

    /// <summary>
    /// The package command's ordinary identity, relationship, registry, diagnostic, and
    /// whole-package evidence. Together with <see cref="Files"/>, this forms the package base
    /// scope.
    /// </summary>
    public const string Package = "@Package";

    /// <summary>
    /// Ordinary type/member identity, signature, inventory, and bounded implementation evidence.
    /// This is the base category for the member command's broad, overload, and detail catalogs.
    /// </summary>
    public const string Member = "@Member";

    /// <summary>
    /// Composable API, analysis, and implementation comparison evidence.
    /// This is the diff command's base category.
    /// </summary>
    public const string Diff = "@Diff";

    /// <summary>
    /// Package-authored documents exposed from a restored project's direct dependencies.
    /// This is the project command's base category.
    /// </summary>
    public const string Project = "@Project";

    /// <summary>
    /// Product-owned query vocabularies. This is the vocabulary command's base category.
    /// </summary>
    public const string Vocabulary = "@Vocabulary";

    /// <summary>
    /// Product-configured knowledge available on the selected ecosystem route.
    /// This is the ecosystem command's base category.
    /// </summary>
    public const string Ecosystem = "@Ecosystem";

    /// <summary>
    /// Pairwise library call-use projections that compose without an additional coordinate.
    /// This is the <c>graph libraries</c> command's base category.
    /// </summary>
    public const string Libraries = "@Libraries";

    /// <summary>
    /// Matched packages and query-settlement evidence. This is the
    /// <c>package query</c> command's base category.
    /// </summary>
    public const string Query = "@Query";

    /// <summary>Vocabularies consumed by API type and member queries.</summary>
    public const string Api = "@API";

    /// <summary>
    /// Safety, provenance, integrity, and vulnerability evidence at package, library, type, or
    /// member scope. Members that are also ordinary command evidence remain cross-listed in their
    /// base category.
    /// </summary>
    public const string Audit = "@Audit";

    /// <summary>
    /// Direct package dependencies and runtime-specific package dependencies.
    /// </summary>
    public const string Dependencies = "@Dependencies";

    /// <summary>
    /// Direct and reverse member-call relationships plus composed call graphs.
    /// </summary>
    public const string Calls = "@Calls";

    /// <summary>
    /// Actual source content: decompiled, original, and annotated source views plus source diffs
    /// (API/member scope). Distinct from <see cref="SourceLink"/>, which is about SourceLink/PDB
    /// provenance and availability rather than the source text itself.
    /// </summary>
    public const string Source = "@Source";

    /// <summary>
    /// SourceLink / PDB provenance sections: source-file and member-location evidence plus the
    /// library/package <c>SourceLink: &lt;X&gt;</c> availability and integrity sections.
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
    /// Ecosystem integration sections. At library scope these are observed <c>Integrations</c> plus
    /// <c>Integration Opportunities</c>. Unlike <see cref="Performance"/>, whose applicability
    /// is a capability predicate, each member's applicability is evidence-based (a cheap
    /// reference probe), so the whole category hyper-subscribes away for a library with no
    /// integrations.
    /// </summary>
    public const string Integrations = "@Integrations";

    /// <summary>
    /// Package file listings scoped to a layout root or document kind. This is a package base
    /// category alongside <see cref="Package"/>. Its members are the
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

    /// <summary>Curated performance evidence at library, type, or member scope.</summary>
    public const string Performance = "@Performance";

    /// <summary>
    /// Decompiler-produced source and IL plus supporting fidelity, fact, and overlay evidence.
    /// </summary>
    public const string Decompiler = "@Decompiler";

    /// <summary>
    /// Raw ECMA-335 metadata sections (library scope): one <c>Metadata: &lt;Table&gt;</c> section
    /// per projected table, plus <c>Metadata: Image</c> for the image-level facts that are not
    /// rows. Every member is explicit-only, so this door is the discovery and selection
    /// affordance for the group, never the mechanism that keeps raw tables out of the default
    /// view -- that is <see cref="SectionEntry{TModel}.ExplicitOnly"/>.
    /// </summary>
    public const string Metadata = "@Metadata";

    /// <summary>
    /// Validated ReadyToRun image and section-directory facts. Members are
    /// explicit-only and remain outside the ordinary library view.
    /// </summary>
    public const string ReadyToRun = "@ReadyToRun";
}
