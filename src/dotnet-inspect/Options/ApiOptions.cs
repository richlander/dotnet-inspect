using DotnetInspector.Packages;

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
    public TipLevel TipLevel { get; init; } = TipLevel.Minimal;

    /// <summary>
    /// True when the user has opted into rich markdown output (via --markdown or -v:*).
    /// </summary>
    public bool VerbosityEnabled => !OneLine && !JsonOutput;

    /// <summary>
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public virtual bool IsRawOutput => JsonOutput || OneLine;
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
    public bool IsDefaultInvocation => OneLine && !JsonOutput && !OneLineExplicitlySet;

    /// <summary>
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public override bool IsRawOutput => JsonOutput || OneLine || ShapeOutput;
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
}

/// <summary>
/// Resolved source context for a single method, ready for rendering.
/// </summary>
public record MethodSourceContext(string SourceCode, string? SourceUrl);
