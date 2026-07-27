using DotnetInspector.Output;
using DotnetInspector.Packages;
using ILInspector.Decompiler.Pipeline;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Options;

/// <summary>
/// Base options shared by type and member commands.
/// </summary>
public partial record ApiOptions
{
    /// <summary>
    /// Type name to inspect (positional argument). Null for full API listing.
    /// </summary>
    public string? TypeName { get; init; }

    // Source resolution
    public string? PackagePath { get; init; }
    /// <summary>Explicit address within a package version range: version, #N, first, or last.</summary>
    public string? PackageRangeAddress { get; init; }
    public string? AssemblyPath { get; init; }
    public string? PlatformAssembly { get; init; }
    public string? ProjectPath { get; init; }
    public string? ProjectAssetsPath { get; init; }

    /// <summary>
    /// Local git clone(s) to read authored source from (keyed on the SourceLink commit and
    /// authenticated by the portable-PDB checksum) before falling back to the remote SourceLink
    /// URL. Empty = network only. Set via <c>--repo</c>; can repeat.
    /// </summary>
    public string[] SourceRepositories { get; init; } = [];
    public string? PlatformFramework { get; init; }
    public string? Tfm { get; init; }
    public bool IncludeAll { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
    public bool Verbose { get; init; }

    /// <summary>
    /// Decompiler spelling options resolved from the tool-owned
    /// <c>.dotnet-inspectconfig</c> at the CLI edge (see
    /// <see cref="DotnetInspector.Services.RenderStyleConfig"/>). Null means the
    /// shipped defaults; the render path treats null and
    /// <see cref="PrinterOptions.Default"/> identically, keeping output
    /// byte-for-byte unchanged when no config is present.
    /// </summary>
    public PrinterOptions? RenderOptions { get; init; }

    /// <summary>
    /// The <c>--taste</c> gesture: request the whole oracle-endorsed style set for
    /// this run, equivalent to <c>dotnet_inspect_style_full_taste = true</c> in
    /// <c>.dotnet-inspectconfig</c> but scoped to one invocation. Applied after the
    /// config resolves and wins for the knobs the aggregate covers, so an explicit
    /// gesture is not silently narrowed by a checked-in config; knobs outside the
    /// oracle-endorsed set keep whatever the file selected. Includes byte-divergent
    /// lenses, so the Annotated view drops its interleaved IL for any member a lens
    /// actually rewrites.
    /// </summary>
    public bool RequestAllTaste { get; init; }

    /// <summary>
    /// Pending <c>.dotnet-inspectconfig</c> parse/read warnings, emitted to stderr
    /// exactly once at the point a decompiled-source render consumes
    /// <see cref="RenderOptions"/> (see
    /// <see cref="DotnetInspector.Services.RenderConfigWarningSink"/>). A
    /// reference-typed latch so the single emission survives the record <c>with</c>
    /// copies that flow the options. Null when the resolved config raised no
    /// warnings.
    /// </summary>
    internal DotnetInspector.Services.RenderConfigWarningSink? RenderConfigWarnings { get; init; }

    // Enrichment
    public bool ShowDocs { get; init; }

    /// <summary>
    /// Whether the user explicitly set --docs (true or false).
    /// When false, the command decides the default based on context.
    /// </summary>
    public bool DocsExplicitlySet { get; init; }
    public bool UseLocalDocs { get; init; }
    public bool ShowSamples { get; init; }
    public bool BrowsableUrls { get; init; }

    // Shared output
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Tabular { get; init; }
    public bool Tsv { get; init; }
    public bool Jsonl { get; init; }
    public bool TabularExplicitlySet { get; init; }
    public bool PlainText { get; init; }

    /// <summary>Print only the selected payload with no heading,
    /// fence, separator, or tips.</summary>
    public bool Bare { get; init; }

    public bool Print { get; init; }

    public bool PrintAll { get; init; }

    public RowSelector? PrintRow { get; init; }

    public bool Value { get; init; }

    public bool Urls { get; init; }

    public bool Paths { get; init; }

    public bool JsonArray { get; init; }

    /// <summary>
    /// True when the user explicitly chose an output format via CLI flags.
    /// When false, commands are free to apply their own default format (e.g., shape/tree).
    /// </summary>
    public bool FormatExplicitlySet { get; init; }

    public bool NoHeader { get; init; }
    public int? Limit { get; init; }
    public HashSet<string> MemberFilter { get; init; } = [];
    public HashSet<string> KindFilter { get; init; } = [];
    public bool UnsafeOnly { get; init; }
    public HashSet<string>? IncludeSections { get; init; }
    public string[]? Discover { get; init; }
    public bool Tree { get; init; }
    public string[]? Select { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }
    public bool Schema { get; init; }
    public bool Count { get; init; }
    public RowWindow? Rows { get; init; }
    public PerformanceTriageOptions PerformanceTriage { get; init; } = PerformanceTriageOptions.Default;
    public TipLevel TipLevel { get; init; } = TipLevel.Minimal;

    /// <summary>
    /// True when discovery (-D) should resolve and load the source to report only the
    /// sections/columns that actually have data (effective discovery). For type/member
    /// queries this is the default; <c>--schema</c> opts out to the cheap, offline static
    /// schema listing.
    /// </summary>
    public bool EffectiveDiscovery => Discover != null && !Schema;

    /// <summary>
    /// True when the user has opted into rich markdown output (via --markdown or -v:*).
    /// </summary>
    public bool VerbosityEnabled => !Tabular && !JsonOutput;

    /// <summary>
    /// True when the user is performing a section/projection query (-S/--columns/--fields).
    /// Such queries produce a focused section view, not the default tree shape.
    /// </summary>
    public bool HasSectionQuery =>
        Select is { Length: > 0 } || Columns is { Length: > 0 } || Fields is { Length: > 0 };

    /// <summary>
    /// Returns the appropriate Markout formatter for the current output format.
    /// </summary>
    public IMarkoutFormatter CreateFormatter() =>
        PlainText ? new PlainTextFormatter() : new MarkdownFormatter();

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public virtual bool IsRawOutput => Bare || Print || PrintAll || Value || Urls || Paths || JsonOutput || Tabular || Jsonl || NoHeader || Count;
}

/// <summary>
/// Options specific to the type command.
/// </summary>
public partial record ApiOptions
{
    /// <summary>On-disk path of the resolved assembly, when a command has one
    /// in hand — enables decompiler-backed sections (member code sections,
    /// whole-type Decompiled Source).</summary>
    public string? DllPath { get; init; }

    /// <summary>On-disk path to an acquired portable PDB, used by the decompiler
    /// to resolve real local-variable names instead of synthesized <c>V_n</c> slots.</summary>
    public string? PdbPath { get; init; }
}

public record TypeOptions : ApiOptions
{
    public string? TypeFilter { get; init; }
    public string? OriginalTypeQuery { get; init; }
    public string? PlatformPrefixQuery { get; init; }
    public bool AllowPlatformPrefixFallback { get; init; }
    public bool ShapeOutput { get; init; }
    public bool MarkdownExplicitlySet { get; init; }

    /// <summary>
    /// Whether the user explicitly set --shape.
    /// When false and resolving a single type, shape is the default view.
    /// </summary>
    public bool ShapeExplicitlySet { get; init; }

    /// <summary>
    /// True when no explicit output format was selected (default invocation).
    /// </summary>
    public bool IsDefaultInvocation => !FormatExplicitlySet && !ShapeExplicitlySet;

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public override bool IsRawOutput => Bare || JsonOutput || Tabular || Jsonl || NoHeader || ShapeOutput || Count;
}

/// <summary>
/// Options specific to the member command.
/// </summary>
public record MemberOptions : ApiOptions
{
    public bool CtorOnly { get; init; }
    public int? OverloadIndex { get; init; }
    public string? MemberDigest { get; init; }
    public int? MemberGenericArity { get; init; }
    public MethodSourceContext? MethodSource { get; init; }

    /// <summary>
    /// Output directories (<c>--bin</c>/<c>--directory</c>) to scan for inbound callers of the
    /// selected member, in addition to the member's own assembly. Empty = own assembly only.
    /// </summary>
    public string[] CallerScopeDirectories { get; init; } = [];

    /// <summary>
    /// Projects (<c>--project</c>) whose restored dependency assemblies are scanned for inbound
    /// callers of the selected member, resolved via <c>project.assets.json</c>.
    /// </summary>
    public string[] CallerScopeProjects { get; init; } = [];

    /// <summary>
    /// Packages (<c>--caller-package</c>) to download and scan for inbound callers.
    /// </summary>
    public string[] CallerScopePackages { get; init; } = [];

    /// <summary>
    /// Resolved on-disk assembly paths that make up the caller scope (from
    /// <see cref="CallerScopeDirectories"/>, <see cref="CallerScopeProjects"/>, and
    /// <see cref="CallerScopePackages"/>), deduped and excluding the member's own assembly.
    /// Populated by the command layer before rendering.
    /// </summary>
    public IReadOnlyList<string> CallerScopeAssemblies { get; init; } = [];

    /// <summary>True when the user supplied any caller-scope flag.</summary>
    public bool HasCallerScope => CallerScopeDirectories.Length > 0 
        || CallerScopeProjects.Length > 0 
        || CallerScopePackages.Length > 0;
}

/// <summary>
/// Resolved source context for a single method, ready for rendering.
/// </summary>
public record MethodSourceContext(string SourceCode, string? SourceUrl);
