using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for package inspection.
/// </summary>
public record InspectionOptions
{
    /// <summary>
    /// Package name/path arguments (positional). First element is package identifier.
    /// </summary>
    public string[] PackageArgs { get; init; } = [];

    /// <summary>
    /// Explicit version override (from --version option).
    /// </summary>
    public string? ExplicitVersion { get; init; }

    /// <summary>
    /// Show package dependencies as a tree view with transitive resolution.
    /// </summary>
    public bool ShowDependencies { get; init; }

    /// <summary>
    /// Target framework to use for dependency resolution (defaults to highest).
    /// </summary>
    public string? Tfm { get; init; }

    /// <summary>
    /// Show the package file tree (lib/tools structure).
    /// </summary>
    public bool ListLayout { get; init; }

    /// <summary>
    /// List files in the package (flat list, filterable with --tfm).
    /// </summary>
    public bool ListFiles { get; init; }

    /// <summary>
    /// Scope to lib/ folder (use with --files or --layout).
    /// </summary>
    public bool ScopeLib { get; init; }

    /// <summary>
    /// Scope to tools/ folder (use with --files or --layout).
    /// </summary>
    public bool ScopeTools { get; init; }

    /// <summary>
    /// List target frameworks in the package, one per line.
    /// </summary>
    public bool ListTfms { get; init; }

    /// <summary>
    /// List available versions of the package from nuget.org.
    /// </summary>
    public bool ListVersions { get; init; }

    /// <summary>
    /// Include prerelease/preview versions when listing versions.
    /// </summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>
    /// Show the README.md content from the package.
    /// </summary>
    public bool ShowReadme { get; init; }

    /// <summary>
    /// Path to write output to instead of stdout.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Limit the number of results (for --versions, --files).
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Output as JSON instead of MDF.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Output verbosity level.
    /// </summary>
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;

    /// <summary>
    /// Tip verbosity level.
    /// </summary>
    public TipLevel TipLevel { get; init; } = TipLevel.Minimal;

    /// <summary>
    /// Sections to include by heading name. If null, all sections are included.
    /// </summary>
    public HashSet<string>? IncludeSections { get; init; }

    /// <summary>
    /// NuGet source configuration options.
    /// </summary>
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// When true, bypass cache-first version resolution and always query the network.
    /// Used when the user specifies @latest.
    /// </summary>
    public bool ForceLatest { get; init; }

    /// <summary>
    /// Default options: metadata only.
    /// </summary>
    public static InspectionOptions Default => new();

    /// <summary>
    /// One result per line, columnar output.
    /// </summary>
    public bool OneLine { get; init; }

    /// <summary>
    /// True when --oneline was explicitly passed (not just the default format).
    /// Used to distinguish "user wants oneline" from "oneline is the default".
    /// </summary>
    public bool OneLineExplicitlySet { get; init; }

    /// <summary>
    /// True when the user explicitly chose an output format via CLI flags.
    /// </summary>
    public bool FormatExplicitlySet { get; init; }

    /// <summary>
    /// Suppress column headers (use with --oneline).
    /// </summary>
    public bool NoHeader { get; init; }

    /// <summary>
    /// Discovery flag values. Null means not specified, empty array means bare -D, populated means section name.
    /// </summary>
    public string[]? Discover { get; init; }

    public bool Tree { get; init; }

    /// <summary>
    /// Names to select (sections). Null means all.
    /// </summary>
    public string[]? Select { get; init; }

    /// <summary>
    /// Column names to include. Null means all.
    /// </summary>
    public string[]? Columns { get; init; }

    /// <summary>
    /// Field names to include. Null means all.
    /// </summary>
    public string[]? Fields { get; init; }

    /// <summary>
    /// Show static discovery schema instead of target-effective discovery.
    /// </summary>
    public bool Schema { get; init; }

    /// <summary>
    /// Output the number of rendered table rows for a single selected section.
    /// </summary>
    public bool Count { get; init; }

    /// <summary>
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public bool IsRawOutput => JsonOutput || OneLine || NoHeader || ListLayout || ListFiles || ListTfms || ListVersions || ShowReadme || ShowDependencies || Count;

    /// <summary>
    /// All inspection features enabled.
    /// </summary>
    public static InspectionOptions All => new();
}
