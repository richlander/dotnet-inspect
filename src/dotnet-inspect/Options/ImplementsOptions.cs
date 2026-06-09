using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for the implements command.
/// </summary>
public record ImplementsOptions : IAssemblySourceOptions
{
    /// <summary>
    /// Target interface or base type to find implementers for.
    /// </summary>
    public string TargetType { get; init; } = "";

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
    /// Limit data rows per rendered table.
    /// </summary>
    public int? Rows { get; init; }

    /// <summary>
    /// Output as JSON.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Minified JSON output.
    /// </summary>
    public bool CompactJson { get; init; }

    /// <summary>
    /// Tabular output (pretty table or TSV).
    /// </summary>
    public bool OneLine { get; init; }

    /// <summary>
    /// Emit tabular output as normalized TSV instead of a pretty table.
    /// </summary>
    public bool Tsv { get; init; }

    /// <summary>
    /// Emit tabular output as JSON Lines, one object per row.
    /// </summary>
    public bool Jsonl { get; init; }

    /// <summary>
    /// Suppress column headers in tabular output.
    /// </summary>
    public bool NoHeader { get; init; }

    /// <summary>
    /// Column projection (include only named columns).
    /// </summary>
    public string[]? Columns { get; init; }

    /// <summary>
    /// Field projection (include only named fields).
    /// </summary>
    public string[]? Fields { get; init; }

    /// <summary>
    /// Discovery mode (-D/--discover). Null = not requested, empty = bare, populated = section name.
    /// </summary>
    public string[]? Discover { get; init; }

    /// <summary>
    /// Show discovery as a tree.
    /// </summary>
    public bool Tree { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// NuGet source configuration options.
    /// </summary>
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// NuGet package ID prefix for prefix-based package discovery.
    /// </summary>
    public string? PackagePrefix { get; init; }

    /// <summary>
    /// Returns true if any search scope is specified.
    /// </summary>
    public bool HasAnyScope =>
        Packages.Length > 0 ||
        Assemblies.Length > 0 ||
        PlatformAssemblies.Length > 0 ||
        PlatformFrameworks.Length > 0 ||
        PackagePrefix != null;

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public bool IsRawOutput => JsonOutput || OneLine || Jsonl || NoHeader;
}
