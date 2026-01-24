namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for package inspection.
/// </summary>
public record InspectionOptions
{
    /// <summary>
    /// Include SourceLink and determinism audit.
    /// </summary>
    public bool IncludeAudit { get; init; }

    /// <summary>
    /// Include public API surface extraction.
    /// </summary>
    public bool IncludeApi { get; init; }

    /// <summary>
    /// Include dependency analysis.
    /// </summary>
    public bool IncludeDeps { get; init; }

    /// <summary>
    /// Output as JSON instead of MDF.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Default options: metadata and tool info only.
    /// </summary>
    public static InspectionOptions Default => new();

    /// <summary>
    /// All inspection features enabled.
    /// </summary>
    public static InspectionOptions All => new()
    {
        IncludeAudit = true,
        IncludeApi = true,
        IncludeDeps = true
    };
}
