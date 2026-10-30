using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

/// <summary>
/// Owner-issued descriptions of the Diff sections, per
/// <c>docs/design/section-model.md#description</c>. Each descriptor in
/// <see cref="DiffSections"/> returns its entry.
/// </summary>
internal static class DiffSectionDescriptions
{
    private const string BothVersions =
        "both compared versions of the package, platform library, or local "
        + "library.";

    private const string BothVersionsBodies =
        "Compiled IL method bodies of " + BothVersions;

    private const string ImplementationBasis =
        "Compiled IL method bodies of members present in either version, "
        + "decompiled to C#; Implementation Diff can add PDB-mapped source.";

    private const string NotComparedBoundary =
        "An endpoint that could not be inspected is reported as a diagnostic "
        + "or failure row, never as an empty result.";

    public static SectionDescription Changes { get; } = new(
        "Every public API difference between the two versions, one row per "
        + "changed type or member with its change kind, detail, old and new "
        + "signatures, and its classification as breaking, potentially "
        + "breaking, additive, or non-breaking, with any unclassified "
        + "change listed under Other API Changes.",
        "The API surface of each version, read from metadata: type and "
        + "member declarations, signatures, accessibility, and attributes, "
        + "at public visibility unless --all includes non-public, hidden, "
        + "and obsolete members.",
        "The two API surfaces have no difference within the selected types, "
        + "members, and visibility. " + NotComparedBoundary,
        "-S Changes keeps the complete population, including Other API "
        + "Changes. --breaking and --additive are assessed subsets: they "
        + "keep only rows classified breaking or additive, so potentially "
        + "breaking, non-breaking, and unclassified changes leave the "
        + "result. A classification is the product's compatibility "
        + "assessment from metadata, not a test against a particular "
        + "consumer.");

    public static SectionDescription AnalysisDiff { get; } = new(
        "Body-level analysis signal differences between the two versions, "
        + "such as allocations, unsafe operations, and exception handlers, "
        + "one row per differing signal of each added, removed, or modified "
        + "member, with old and new counts, delta, in-loop shape, and "
        + "IL-offset evidence.",
        BothVersionsBodies,
        "No compared member's analysis signals differ between the two "
        + "versions.",
        "Signals are body-level evidence, not public API compatibility "
        + "changes; --changed keeps members present in both versions, and "
        + "--alloc-regressions keeps their allocation increases.");

    public static SectionDescription ImplementationDiff { get; } = new(
        "Body-level C# and IL implementation differences between the two "
        + "versions, one evidence row per differing line, IL hunk, or "
        + "normal-flow complexity observation of each modified member, with "
        + "its mechanism, change, and stable Kind.",
        ImplementationBasis,
        "No compared member's C# or IL body differs between the two "
        + "versions. " + NotComparedBoundary,
        "Decompiled C# is a reading of compiled IL, not authored source. "
        + "A body that could not be diffed is reported as an evidence row "
        + "naming the failure.");

    public static SectionDescription AnnotatedSourceDiff { get; } = new(
        "The complete annotated source comparison of one exact package Type "
        + "and Member: its C# before and after, and its IL with --il, with "
        + "line maps and product-issued text comparisons.",
        "The selected Member's compiled IL in each package version, "
        + "decompiled to annotated C#, following type forwarders.",
        "A Member present in only one version has a typed absent side "
        + "rather than an empty comparison.",
        "JSON only: requires one package Type and Member and does not "
        + "compose with other sections.");

    public static SectionDescription ComplexityContext { get; } = new(
        "Normal-flow cyclomatic complexity differences between the two "
        + "versions, one row per member whose complexity differs, was added "
        + "or removed, or could not be compared, with old and new complexity, "
        + "delta, population size, and percentile rank.",
        BothVersionsBodies,
        "No compared member's normal-flow complexity changed, and none was "
        + "added, removed, or incomplete.",
        "The percentile rank is the inclusive percentage of delta-bearing "
        + "methods in this diff whose absolute delta is no greater than the "
        + "row's: positional context, not an unusualness or quality "
        + "judgment.");

    public static SectionDescription StructuralContext { get; } = new(
        "Structural differences between the two versions, one row per "
        + "complete, unambiguous member pair with its signed instruction, "
        + "complexity, loop, exception-region, direct-call, allocation, and "
        + "async deltas, their direction signature, and its local cohort "
        + "size.",
        BothVersionsBodies,
        "The diff has no complete, unambiguous member pair; added, removed, "
        + "incomplete, and ambiguous pairs never appear.",
        "Cohort Size is a local frequency within this diff, not an outlier "
        + "or quality judgment.");

    public static SectionDescription Summary { get; } = new(
        "One row per analysis selected with --analysis, with its outcome "
        + "(Compared, Unavailable, or Failed) and its Added, Removed, "
        + "Changed, and Present counts.",
        "The Transition rows each selected analysis emits over "
        + BothVersions,
        "Never empty: every selected analysis, including the default api, "
        + "reports its outcome, with zero counts when nothing changed.",
        "Counts are over each analysis's own Transition rows; "
        + "string-literals emits no Present rows.");

    public static SectionDescription Transitions { get; } = new(
        "Each selected analysis's per-Finding transitions between the two "
        + "versions, such as an added, removed, or changed Finding, in "
        + "selection order, with old and new values and each side's "
        + "inspection state.",
        "The keyed Findings each analysis selected with --analysis issues "
        + "for " + BothVersions,
        "No selected analysis has a Finding that differs between the two "
        + "versions.",
        "Available only where a selected analysis declares Transitions at "
        + "the request surface; string-literals requires one --where "
        + "predicate on Literal.");
}
