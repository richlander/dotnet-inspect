using DotnetInspector.Packages;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Options;

/// <summary>
/// Base options shared by type and member commands.
/// </summary>
public record ApiOptions
{
    /// <summary>
    /// Type name to inspect (positional argument). Null for full API listing.
    /// </summary>
    public string? TypeName { get; init; }

    // Source resolution
    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? PlatformAssembly { get; init; }
    public string? PlatformFramework { get; init; }
    public string? Tfm { get; init; }
    public bool IncludeAll { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
    public bool Verbose { get; init; }

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
    public bool OneLine { get; init; }
    public bool OneLineExplicitlySet { get; init; }
    public bool PlainText { get; init; }

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
    public bool Effective { get; init; }
    public bool Schema { get; init; }
    public bool Count { get; init; }
    public TipLevel TipLevel { get; init; } = TipLevel.Minimal;

    /// <summary>
    /// True when discovery (-D) should resolve and load the source to report only the
    /// sections/columns that actually have data (effective discovery). For type/member
    /// queries this is the default; <c>--schema</c> opts out to the cheap, offline static
    /// schema listing. The legacy <c>--effective</c> flag is now redundant but still honored.
    /// </summary>
    public bool EffectiveDiscovery => Discover != null && !Schema;

    /// <summary>
    /// True when the user has opted into rich markdown output (via --markdown or -v:*).
    /// </summary>
    public bool VerbosityEnabled => !OneLine && !JsonOutput;

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
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public virtual bool IsRawOutput => JsonOutput || OneLine || NoHeader || Count;
}

/// <summary>
/// Options specific to the type command.
/// </summary>
public record TypeOptions : ApiOptions
{
    public string? TypeFilter { get; init; }
    public bool ShapeOutput { get; init; }

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
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public override bool IsRawOutput => JsonOutput || OneLine || NoHeader || ShapeOutput || Count;
}

/// <summary>
/// Options specific to the member command.
/// </summary>
public record MemberOptions : ApiOptions
{
    public bool CtorOnly { get; init; }
    public int? OverloadIndex { get; init; }
    public string[]? ParamTypes { get; init; }
    public string? FirstParamType { get; init; }
    public bool ShowSelect { get; init; }
    public string? DllPath { get; init; }
    public MethodSourceContext? MethodSource { get; init; }

    /// <summary>On-disk path to an acquired portable PDB, used by the decompiler (--index code
    /// sections) to resolve real local-variable names instead of synthesized <c>V_n</c> slots.</summary>
    public string? PdbPath { get; init; }
}

/// <summary>
/// Resolved source context for a single method, ready for rendering.
/// </summary>
public record MethodSourceContext(string SourceCode, string? SourceUrl);
