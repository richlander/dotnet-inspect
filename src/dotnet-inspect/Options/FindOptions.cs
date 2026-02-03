namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for the find command.
/// </summary>
public record FindOptions
{
    /// <summary>
    /// Package to search (name or name@version).
    /// </summary>
    public string? PackagePath { get; init; }

    /// <summary>
    /// Assembly path to search.
    /// </summary>
    public string? AssemblyPath { get; init; }

    /// <summary>
    /// Platform assembly name to search (e.g., System.Text.Json).
    /// </summary>
    public string? PlatformAssembly { get; init; }

    /// <summary>
    /// Platform framework (runtime, aspnetcore, netstandard). Use @version for specific version.
    /// </summary>
    public string? PlatformFramework { get; init; }

    /// <summary>
    /// Target framework moniker (e.g., net8.0).
    /// </summary>
    public string? Tfm { get; init; }

    /// <summary>
    /// Include hidden (EditorBrowsable.Never) and obsolete types.
    /// </summary>
    public bool IncludeAll { get; init; }

    /// <summary>
    /// Limit number of results.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Output as JSON.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Minified JSON output.
    /// </summary>
    public bool CompactJson { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }
}
