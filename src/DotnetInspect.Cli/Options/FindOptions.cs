using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;

namespace DotnetInspect.Cli.Options;

/// <summary>
/// Configuration options for the find command.
/// </summary>
public record FindOptions : IAssemblySourceOptions, IProjectionOptions
{
    internal SearchSourceSelection? SourceSelection { get; init; }

    internal bool PackagePrefixLimitReached { get; init; }

    /// <summary>
    /// Type name or glob pattern (positional argument). Comma-separated for multiple.
    /// </summary>
    public string Pattern { get; init; } = "";

    public string? Literal { get; init; }

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
    /// Search member names instead of type names (the <c>--members</c> lens). Auto-enabled when the
    /// positional pattern starts with <c>.</c> (e.g. <c>.Serialize</c>), an unambiguous sentinel since
    /// no valid type or namespace name starts with a dot.
    /// </summary>
    public bool Members { get; init; }

    /// <summary>
    /// Maximum package candidates or manifest enrichments authorized by
    /// <c>--take</c> for the active package-prefix mode.
    /// </summary>
    public int? Take { get; init; }

    /// <summary>
    /// Internal operational limit used by trusted lookup consumers. The
    /// <c>find</c> CLI does not lower semantic row selection into this value.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Raw API type-filter value supplied to <c>--type</c>.
    /// </summary>
    public string? TypeFilter { get; init; }

    /// <summary>
    /// Ordered semantic row-selection intent prepared by the shared CLI grammar.
    /// </summary>
    public RowSelectionIntent<string>? RowSelection { get; init; }

    /// <summary>
    /// Output the number of rendered result rows.
    /// </summary>
    public bool Count { get; init; }

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

    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;

    /// <summary>
    /// Tabular output (pretty table or TSV).
    /// </summary>
    public bool Tabular { get; init; }

    /// <summary>
    /// Emit tabular output as normalized TSV instead of a pretty table.
    /// </summary>
    public bool Tsv { get; init; }

    /// <summary>
    /// Emit tabular output as JSON Lines, one object per row.
    /// </summary>
    public bool Jsonl { get; init; }

    /// <summary>
    /// True when the user explicitly chose an output format via CLI flags.
    /// </summary>
    public bool FormatExplicitlySet { get; init; }

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

    public string[]? Select { get; init; }

    public PackageQueryOptions? PackageQuery { get; init; }

    /// <summary>
    /// Show discovery as a tree.
    /// </summary>
    public bool Tree { get; init; }

    /// <summary>
    /// NuGet source configuration options.
    /// </summary>
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// NuGet package ID prefix for prefix-based package discovery.
    /// </summary>
    public string? PackagePrefix { get; init; }

    /// <summary>
    /// Whether <c>--package-prefix</c> was explicitly supplied.
    /// </summary>
    public bool PackagePrefixSpecified { get; init; }

    internal bool HasPackageProfileGroupScope { get; init; }

    /// <summary>
    /// True when a patternless package-prefix search projects package manifests
    /// rather than acquiring package archives for API search.
    /// </summary>
    public bool IsPackageProfile =>
        Literal is null
        && Pattern.Length == 0
        && (PackagePrefixSpecified || PackagePrefix is not null);

    /// <summary>
    /// Returns true if a scope has been selected, including a normalized empty contribution.
    /// </summary>
    public bool HasAnyScope =>
        SourceSelection is not null ||
        Packages.Length > 0 ||
        Assemblies.Length > 0 ||
        PlatformAssemblies.Length > 0 ||
        PlatformFrameworks.Length > 0 ||
        Projects.Length > 0 ||
        BinPaths.Length > 0 ||
        PackagePrefixSpecified ||
        PackagePrefix is not null;

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public bool IsRawOutput => JsonOutput || Tabular || Jsonl || NoHeader;
}
