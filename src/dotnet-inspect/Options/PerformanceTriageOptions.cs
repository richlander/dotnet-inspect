namespace DotnetInspector.Options;

/// <summary>
/// Row predicates for the Performance Triage section.
/// </summary>
public sealed record PerformanceTriageOptions
{
    public static PerformanceTriageOptions Default { get; } = new();
    public static readonly string[] KnownShapes =
    [
        "allocation-hotspot",
        "async-state-machine",
        "box-value-type",
        "capturing-delegate",
        "enumerator-allocation",
        "instance-method-group-delegate",
        "linq-scan-in-loop",
        "materialize-in-loop",
        "scan-method-in-loop-call",
        "small-array",
        "span-to-array-copy",
        "stackalloc-candidate",
        "string-build-in-loop",
        "temporary-byte-array-copy",
    ];

    public bool LoopOnly { get; init; }
    public string? MinConfidence { get; init; }
    public string[] Shapes { get; init; } = [];
    public int? Top { get; init; }

    public bool HasFilters =>
        LoopOnly
        || !string.IsNullOrWhiteSpace(MinConfidence)
        || Shapes.Length > 0
        || Top.HasValue;

    public static bool TryValidateShapes(PerformanceTriageOptions options, out string error)
    {
        var known = KnownShapes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalid = options.Shapes.Where(shape => !known.Contains(shape)).ToArray();
        if (invalid.Length == 0)
        {
            error = "";
            return true;
        }

        var quotedInvalid = string.Join(", ", invalid.Select(shape => $"'{shape}'"));
        error = $"Error: Unknown Performance Triage shape{(invalid.Length == 1 ? "" : "s")} {quotedInvalid}. Valid shapes: {string.Join(", ", KnownShapes)}.";
        return false;
    }
}
