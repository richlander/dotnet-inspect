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
    /// Optional type glob/name filter for Source Files rows.
    /// </summary>
    public string? TypeFilter { get; init; }

    /// <summary>
    /// Use GitHub /blob/ URLs for browser viewing instead of raw source URLs.
    /// </summary>
    public bool BrowsableUrls { get; init; }

    /// <summary>
    /// Library inside the package to inspect. Null means package inspection; empty string means select
    /// the primary library when unambiguous; a non-empty value selects a specific DLL.
    /// </summary>
    public string? PackageLibrary { get; init; }

    /// <summary>
    /// Inspect all compatible libraries in the package instead of selecting one.
    /// </summary>
    public bool AllLibraries { get; init; }

    /// <summary>
    /// Show the package file tree (lib/tools structure).
    /// </summary>
    public bool ListLayout { get; init; }

    /// <summary>
    /// Scope the file listing to one or more selectors: a file, a directory
    /// (trailing slash), the package root (<c>/</c>), a glob (<c>*.md</c>), or a
    /// token such as <c>@readme</c>/<c>@agents</c>. Selects the Files section.
    /// </summary>
    public string? PathFilter { get; init; }

    public string[]? PathFilters { get; init; }

    /// <summary>
    /// How repeated path selectors are resolved: <c>all</c> returns every match;
    /// <c>first</c> returns the first matching selector's first result per package.
    /// </summary>
    public string PathMatchMode { get; init; } = "all";

    /// <summary>
    /// In multi-package row output, omit packages whose selected Files section is empty.
    /// </summary>
    public bool SkipEmpty { get; init; }

    /// <summary>
    /// Scope to lib/ folder (use with --layout).
    /// </summary>
    public bool ScopeLib { get; init; }

    /// <summary>
    /// Scope to tools/ folder (use with --layout).
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
    /// Include prerelease/preview versions when listing versions or resolving an unversioned latest package.
    /// </summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>
    /// Show the README.md content from the package.
    /// </summary>
    public bool ShowReadme { get; init; }

    /// <summary>
    /// Print the document behind the selected section's first row.
    /// </summary>
    public bool Print { get; init; }

    public bool PrintAll { get; init; }

    public int? PrintRow { get; init; }

    /// <summary>
    /// Print the contents of files selected by <see cref="PathFilters"/>.
    /// </summary>
    public bool ShowContent { get; init; }

    /// <summary>
    /// Scope markdown content output to the full file, YAML frontmatter, or body.
    /// </summary>
    public PackageFileContentScope ContentScope { get; init; } = PackageFileContentScope.Full;

    public bool FrontmatterRequested { get; init; }

    public bool BodyRequested { get; init; }

    /// <summary>
    /// Path to write output to instead of stdout.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Limit the number of results (for --versions).
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Output as JSON instead of MDF.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Print a single selected payload without headings, fences, separators, or tips.
    /// </summary>
    public bool Bare { get; init; }

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
    /// True when a tabular output flag was explicitly passed (not just the default format).
    /// Used to distinguish explicit tabular output from a tabular default.
    /// </summary>
    public bool OneLineExplicitlySet { get; init; }

    /// <summary>
    /// True when the user explicitly chose an output format via CLI flags.
    /// </summary>
    public bool FormatExplicitlySet { get; init; }

    /// <summary>
    /// Suppress column headers (use with --table or --tsv).
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
    /// Limit data rows per rendered table.
    /// </summary>
    public int? Rows { get; init; }

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public bool IsRawOutput => Bare || JsonOutput || OneLine || Jsonl || NoHeader || ListLayout || ListTfms || ListVersions || ShowReadme || Print || PrintAll || ShowContent || ShowDependencies || Count || PackageLibrary != null || AllLibraries;

    /// <summary>
    /// All inspection features enabled.
    /// </summary>
    public static InspectionOptions All => new();
}

public enum PackageFileContentScope
{
    Full,
    Frontmatter,
    Body
}
