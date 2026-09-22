using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;

using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Options;

/// <summary>
/// Configuration options for package inspection.
/// </summary>
public record InspectionOptions : IProjectionOptions
{
    /// <summary>
    /// Package name/path arguments (positional). First element is package identifier.
    /// </summary>
    public string[] PackageArgs { get; init; } = [];

    /// <summary>
    /// Exact Package version selected by the explicit Package command.
    /// </summary>
    public string? ExplicitVersion { get; init; }

    internal PackageReferenceTarget? DeclaredPackageTarget { get; init; }

    /// <summary>
    /// Canonical Workspace packet supplying the selected Package context.
    /// </summary>
    public string? WorkspacePacket { get; init; }

    /// <summary>
    /// Optional derived Workspace Share output.
    /// </summary>
    public WorkspaceShareFormat? ShareFormat { get; init; }

#if DEBUG
    /// <summary>
    /// Debug-only destination for the complete enriched Package envelope.
    /// </summary>
    public string? EvidenceEnvelopePath { get; init; }
#endif

    /// <summary>
    /// Legacy dependency-tree input. The Package route rejects it with focused
    /// replacement guidance; Library routes retain their existing handling.
    /// </summary>
    public bool ShowDependencies { get; init; }

    public int? ReferenceHierarchyDepth { get; init; }

    /// <summary>
    /// Target framework to use for dependency resolution (defaults to highest).
    /// </summary>
    public string? Tfm { get; init; }

    public DependencyQueryPlan? DependencyQueryPlan { get; init; }

    internal int? DependencyHierarchyLegacyWindowStageIndex { get; init; }

    internal bool DependencyHierarchyRowsSelected { get; init; }

    /// <summary>
    /// Optional type glob/name filter for Source Files rows.
    /// </summary>
    public string? TypeFilter { get; init; }

    /// <summary>
    /// Prefer rendered browser-view URLs when supported; otherwise keep the original URL.
    /// </summary>
    public bool PreferRenderedUrls { get; init; }

    /// <summary>
    /// Library inside the package to inspect. Null means package inspection;
    /// an empty string means namesake narrowing; a non-empty value selects a
    /// specific DLL.
    /// </summary>
    public string? PackageLibrary { get; init; }

    /// <summary>
    /// Execute the selected Package compile-Library aggregate.
    /// </summary>
    public bool AllLibraries { get; init; }

    /// <summary>
    /// Narrow Package Library inspection by managed assembly identity.
    /// </summary>
    public bool NamesakeLibrary { get; init; }

    internal WorkspaceLibrarySelection? WorkspaceLibrarySelection { get; init; }

    internal string[]? WorkspaceLibraryAssetPaths { get; init; }

    public IntegrationQueryOptions IntegrationQuery { get; init; } =
        IntegrationQueryOptions.Default;

    public MetadataRootKind MetadataRoot { get; init; } = MetadataRootKind.Cli;

    public RowSelectionIntent<string>? ReferenceRowSelection { get; init; }

    public PerformanceTriageOptions PerformanceTriage { get; init; } =
        PerformanceTriageOptions.Default;

    public BodyKindQueryOptions BodyKindQuery { get; init; } =
        BodyKindQueryOptions.Default;

    public CloneCandidateQueryOptions CloneCandidateQuery { get; init; } =
        CloneCandidateQueryOptions.Default;

    public bool Trace { get; init; }

    public bool Effective { get; init; }

    public string? ExtractResources { get; init; }

    /// <summary>
    /// Show the package file tree (lib/tools structure).
    /// </summary>
    public bool ListLayout { get; init; }

    /// <summary>
    /// The caller explicitly supplied <c>--layout</c>, including in discovery
    /// mode where layout rendering itself is suppressed.
    /// </summary>
    public bool ListLayoutExplicitlySet { get; init; }

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
    /// Select one resolved version from an ordinary listing.
    /// </summary>
    public bool SingleVersionQuery { get; init; }

    /// <summary>
    /// Annotate each listed version with the feed it came from, one row per version and feed.
    /// </summary>
    public bool ListVersionsWithFeed { get; init; }

    /// <summary>
    /// Include prerelease/preview versions when listing versions or resolving an unversioned latest package.
    /// </summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>
    /// Include unlisted versions in <c>--versions</c> output, marked as unlisted, instead of hiding
    /// them. Discovery hides unlisted versions by default; this opt-in reveals them flagged.
    /// </summary>
    public bool IncludeUnlisted { get; init; }

    /// <summary>
    /// Show the README.md content from the package.
    /// </summary>

    /// <summary>
    /// Print the document behind the selected section's first row.
    /// </summary>
    public bool Print { get; init; }

    public RowSelector? PrintRow { get; init; }

    public bool Value { get; init; }

    public bool Urls { get; init; }

    public bool Paths { get; init; }

    /// <summary>
    /// Project top-level package roots represented by selected
    /// <c>Package files</c> rows.
    /// </summary>
    public bool Roots { get; init; }

    public bool JsonArray { get; init; }

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
    /// Semantic row selection for plural package-version listings.
    /// </summary>
    public RowSelectionIntent<string>? VersionRowSelection { get; init; }

    /// <summary>
    /// Semantic row selection for a single package's SourceLink file rows.
    /// </summary>
    public RowSelectionIntent<string>? SourceLinkFileRowSelection { get; init; }

    /// <summary>
    /// Semantic row selection for one single-package file-list section.
    /// </summary>
    public RowSelectionIntent<string>? PackageFileRowSelection { get; init; }

    /// <summary>
    /// Semantic row selection for one package layout's scoped file paths.
    /// </summary>
    public RowSelectionIntent<string>? PackageLayoutRowSelection { get; init; }

    /// <summary>
    /// Semantic row selection for one package's target-framework listing.
    /// </summary>
    public RowSelectionIntent<string>? PackageTfmRowSelection { get; init; }

    /// <summary>
    /// Semantic row selection for one Package's ecosystem-dependency pairs.
    /// </summary>
    public RowSelectionIntent<string>?
        EcosystemDependencyRowSelection { get; init; }

    /// <summary>
    /// Semantic row selection for Clone Candidates on a delegated Library route.
    /// </summary>
    public RowSelectionIntent<string>? CloneCandidateRowSelection { get; init; }

    /// <summary>
    /// Output as JSON instead of MDF.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Effective output format after CLI and environment precedence.
    /// </summary>
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;

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
    /// Bare <c>-S</c> mode: render the network-free <b>fixed</b> overview — only sections whose
    /// declared <see cref="DotnetInspector.Sections.SectionSizeClass.Fixed"/> growth class and
    /// <see cref="DotnetInspector.Sections.SectionCost.NetworkFree"/> cost make their membership
    /// package-independent. Set internally when a valueless <c>-S</c> is issued at the default
    /// verbosity; an explicit <c>-v:n</c>/<c>-v:d</c> stays on the normal curated ladder.
    /// </summary>
    public bool FixedOverview { get; init; }

    /// <summary>
    /// Sections to include by heading name. If null, all sections are included.
    /// </summary>
    public HashSet<string>? IncludeSections { get; init; }

    /// <summary>
    /// NuGet source configuration options.
    /// </summary>
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// When true, bypass source-scoped candidate metadata caches and always query the network.
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
    /// True when a tabular output flag was explicitly passed (not just the default format).
    /// Used to distinguish explicit tabular output from a tabular default.
    /// </summary>
    public bool TabularExplicitlySet { get; init; }

    /// <summary>
    /// Whether the caller actually passed <c>-S/--select</c>. <see cref="Select"/> alone cannot
    /// answer this: <c>--path</c> and <c>--type</c> are sugar that synthesize a selection, so a
    /// non-empty <see cref="Select"/> does not imply the caller asked for one.
    /// </summary>
    public bool SelectExplicitlySet { get; init; }

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

    /// <summary>
    /// Include additional structural discovery metadata.
    /// </summary>
    public bool DiscoverDetails { get; init; }

    public bool Tree { get; init; }

    /// <summary>
    /// Names to select (sections). Null means all.
    /// </summary>
    public string[]? Select { get; init; }

    /// <summary>
    /// Bare <c>-S</c>: a request for this command's default preset rather than for any named
    /// section or category. Tracked separately from <see cref="Select"/> so the marker is never
    /// spellable as a selector value. See #3547.
    /// </summary>
    public bool SelectDefault { get; init; }

    /// <summary>
    /// Column names to include. Null means all.
    /// </summary>
    public string[]? Columns { get; init; }

    /// <summary>
    /// Field names to include. Null means all.
    /// </summary>
    public string[]? Fields { get; init; }

    /// <summary>Whether the caller explicitly supplied <c>--fields</c>.</summary>
    public bool FieldsExplicitlySet { get; init; }

    /// <summary>
    /// Show static discovery schema instead of target-effective discovery.
    /// </summary>
    public bool Schema { get; init; }

    /// <summary>
    /// Output selected table-row cardinality as a scalar or per-section count rows.
    /// </summary>
    public bool Count { get; init; }

    /// <summary>
    /// Output the complete shared inspection envelope as JSON.
    /// </summary>
    public bool EnvelopeOutput { get; init; }

    /// <summary>
    /// Limit data rows per rendered table.
    /// </summary>
    public RowWindow? Rows { get; init; }

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public bool IsRawOutput => EnvelopeOutput || Bare || Format != OutputFormat.Markdown || JsonOutput || Tabular || Jsonl || JsonArray || NoHeader || ListLayout || ListTfms || ListVersions || Print || Value || Urls || Paths || Roots || ShowContent || ShowDependencies || Count || PackageLibrary != null || AllLibraries;

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

internal abstract record WorkspaceLibrarySelection
{
    private WorkspaceLibrarySelection()
    {
    }

    internal sealed record Aggregate : WorkspaceLibrarySelection;

    internal sealed record Namesake : WorkspaceLibrarySelection;

    internal sealed record Exact(string Library) : WorkspaceLibrarySelection;
}
