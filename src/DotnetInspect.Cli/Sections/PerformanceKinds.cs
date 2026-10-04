namespace DotnetInspect.Cli.Sections;

using System.Collections.Immutable;
using ILInspector.Analysis;

/// <summary>
/// Maps optimization-opportunity shapes onto the kind-scoped performance sections and
/// provides the canonical ordered section list plus the structured (JSON) key per section.
/// This is the single source of truth shared by the view (bucketing), the model projection
/// (nested <c>performance</c> JSON), and the <c>--count</c> summary.
/// </summary>
public static class PerformanceKinds
{
    /// <summary>The kind-scoped performance sections, in curated display order.</summary>
    public static readonly string[] Sections =
    [
        SectionNames.PerformanceBoxing,
        SectionNames.PerformanceArrays,
        SectionNames.PerformanceClosures,
        SectionNames.PerformanceEnumerators,
        SectionNames.PerformanceStrings,
        SectionNames.PerformanceLoops,
        SectionNames.PerformanceHotspots,
        SectionNames.PerformanceAsync,
        SectionNames.PerformanceOther,
    ];

    /// <summary>
    /// Resolves the section that renders a given opportunity shape. Unmapped shapes route to
    /// <see cref="SectionNames.PerformanceOther"/> so the scan is never silently lossy.
    /// </summary>
    public static string SectionForShape(string? shape) =>
        SectionForKind(
            OptimizationOpportunityRowSpace.KindForShape(shape));

    public static OptimizationOpportunityCuratedQuery QueryForSection(
        string section) =>
        section switch
        {
            SectionNames.PerformanceBoxing =>
                OptimizationOpportunityRowSpace.Boxing,
            SectionNames.PerformanceArrays =>
                OptimizationOpportunityRowSpace.Arrays,
            SectionNames.PerformanceClosures =>
                OptimizationOpportunityRowSpace.ClosuresAndDelegates,
            SectionNames.PerformanceEnumerators =>
                OptimizationOpportunityRowSpace.Enumerators,
            SectionNames.PerformanceStrings =>
                OptimizationOpportunityRowSpace.Strings,
            SectionNames.PerformanceLoops =>
                OptimizationOpportunityRowSpace.LoopHotPaths,
            SectionNames.PerformanceHotspots =>
                OptimizationOpportunityRowSpace.AllocationHotspots,
            SectionNames.PerformanceAsync =>
                OptimizationOpportunityRowSpace.Async,
            SectionNames.PerformanceOther =>
                OptimizationOpportunityRowSpace.Other,
            _ => throw new ArgumentOutOfRangeException(nameof(section)),
        };

    public static ImmutableArray<OptimizationOpportunity> Select(
        string section,
        IEnumerable<OptimizationOpportunity> opportunities) =>
        OptimizationOpportunityRowSpace.Select(
            QueryForSection(section),
            opportunities);

    public static bool Any(
        string section,
        IEnumerable<OptimizationOpportunity> opportunities) =>
        OptimizationOpportunityRowSpace.Any(
            QueryForSection(section),
            opportunities);

    private static string SectionForKind(
        OptimizationOpportunityKind kind) => kind switch
    {
        OptimizationOpportunityKind.Boxing =>
            SectionNames.PerformanceBoxing,
        OptimizationOpportunityKind.Arrays =>
            SectionNames.PerformanceArrays,
        OptimizationOpportunityKind.ClosuresAndDelegates =>
            SectionNames.PerformanceClosures,
        OptimizationOpportunityKind.Enumerators =>
            SectionNames.PerformanceEnumerators,
        OptimizationOpportunityKind.Strings =>
            SectionNames.PerformanceStrings,
        OptimizationOpportunityKind.LoopHotPaths =>
            SectionNames.PerformanceLoops,
        OptimizationOpportunityKind.AllocationHotspots =>
            SectionNames.PerformanceHotspots,
        OptimizationOpportunityKind.Async =>
            SectionNames.PerformanceAsync,
        OptimizationOpportunityKind.Other =>
            SectionNames.PerformanceOther,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>
    /// The short kind label for a performance section (the part after <c>"Performance: "</c>), used
    /// as the leading <c>Kind</c> column when the sections are flattened into one self-describing
    /// tabular table. Markdown keeps the full <c>## Performance: Kind</c> heading instead.
    /// </summary>
    public static string KindLabel(string section)
    {
        const string prefix = "Performance: ";
        return section.StartsWith(prefix, StringComparison.Ordinal)
            ? section[prefix.Length..]
            : section;
    }

    /// <summary>The snake_case JSON key for a performance section under the nested <c>performance</c> object.</summary>
    public static string StructuredKey(string section) => section switch
    {
        SectionNames.PerformanceBoxing => "boxing",
        SectionNames.PerformanceArrays => "arrays",
        SectionNames.PerformanceClosures => "closures_and_delegates",
        SectionNames.PerformanceEnumerators => "enumerators",
        SectionNames.PerformanceStrings => "strings",
        SectionNames.PerformanceLoops => "loop_hot_paths",
        SectionNames.PerformanceHotspots => "allocation_hotspots",
        SectionNames.PerformanceAsync => "async",
        _ => "other",
    };

    /// <summary>
    /// True when every section in <paramref name="sections"/> is a performance kind section. These
    /// sections share the single <c>PerformanceRow</c> view, so they can be rendered as one
    /// concatenated tabular table (<c>--table</c>/<c>--tsv</c>/<c>--jsonl</c>).
    /// </summary>
    public static bool AllShareCommonView(IReadOnlyCollection<string> sections)
    {
        if (sections.Count == 0)
            return false;
        foreach (var section in sections)
            if (Array.IndexOf(Sections, section) < 0)
                return false;
        return true;
    }
}
