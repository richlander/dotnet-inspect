namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for the find command.
/// </summary>
public record FindOptions
{
    /// <summary>
    /// Packages to search (name or name@version). Can specify multiple.
    /// </summary>
    public string[] Packages { get; init; } = [];

    /// <summary>
    /// Assembly paths to search. Can specify multiple.
    /// </summary>
    public string[] Assemblies { get; init; } = [];

    /// <summary>
    /// Platform assembly names to search (e.g., System.Text.Json). Can specify multiple.
    /// </summary>
    public string[] PlatformAssemblies { get; init; } = [];

    /// <summary>
    /// Platform frameworks to search (runtime, aspnetcore, netstandard). Can specify multiple.
    /// </summary>
    public string[] PlatformFrameworks { get; init; } = [];

    /// <summary>
    /// Project file paths (.csproj). Searches transitive dependencies. Can specify multiple.
    /// </summary>
    public string[] Projects { get; init; } = [];

    /// <summary>
    /// Binary output directory paths. Searches all DLLs. Can specify multiple.
    /// </summary>
    public string[] BinPaths { get; init; } = [];

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

    /// <summary>
    /// One-line output: space-separated type names (flat by default, grouped with --grouped).
    /// </summary>
    public bool OneLine { get; init; }

    /// <summary>
    /// Group results by pattern (use with --oneline).
    /// </summary>
    public bool Grouped { get; init; }

    /// <summary>
    /// Show only type names, one per line.
    /// </summary>
    public bool NameOnly { get; init; }

    /// <summary>
    /// Returns true if any search scope is specified.
    /// </summary>
    public bool HasAnyScope =>
        Packages.Length > 0 ||
        Assemblies.Length > 0 ||
        PlatformAssemblies.Length > 0 ||
        PlatformFrameworks.Length > 0 ||
        Projects.Length > 0 ||
        BinPaths.Length > 0;
}
