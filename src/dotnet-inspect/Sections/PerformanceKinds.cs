namespace DotnetInspector.Sections;

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
        SectionNames.PerformanceLoops,
        SectionNames.PerformanceHotspots,
        SectionNames.PerformanceAsync,
        SectionNames.PerformanceOther,
    ];

    /// <summary>
    /// Resolves the section that renders a given opportunity shape. Unmapped shapes route to
    /// <see cref="SectionNames.PerformanceOther"/> so the scan is never silently lossy.
    /// </summary>
    public static string SectionForShape(string? shape) => NormalizeShape(shape) switch
    {
        "box-value-type" => SectionNames.PerformanceBoxing,

        "small-array"
        or "temporary-byte-array-copy"
        or "span-to-array-copy"
        or "stackalloc-candidate" => SectionNames.PerformanceArrays,

        "capturing-delegate"
        or "instance-method-group-delegate" => SectionNames.PerformanceClosures,

        "enumerator-allocation" => SectionNames.PerformanceEnumerators,

        "linq-scan-in-loop"
        or "materialize-in-loop"
        or "scan-method-in-loop-call"
        or "string-build-in-loop" => SectionNames.PerformanceLoops,

        "allocation-hotspot"
        or "allocation-fanout" => SectionNames.PerformanceHotspots,

        "async-state-machine" => SectionNames.PerformanceAsync,

        _ => SectionNames.PerformanceOther,
    };

    // Shape validation and row filtering are case-insensitive, so a differently-cased shape (e.g.
    // "BOX-VALUE-TYPE") must resolve to the same kind section its findings bucket into; otherwise
    // the accepted shape would silently route to Performance: Other and hide the matching rows.
    private static string? NormalizeShape(string? shape) => shape?.ToLowerInvariant();

    /// <summary>The snake_case JSON key for a performance section under the nested <c>performance</c> object.</summary>
    public static string StructuredKey(string section) => section switch
    {
        SectionNames.PerformanceBoxing => "boxing",
        SectionNames.PerformanceArrays => "arrays",
        SectionNames.PerformanceClosures => "closures_and_delegates",
        SectionNames.PerformanceEnumerators => "enumerators",
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
