using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Options for the api command.
/// </summary>
public record ApiOptions
{
    /// <summary>
    /// Type name to inspect (positional argument). Null for full API listing.
    /// </summary>
    public string? TypeName { get; init; }

    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? PlatformAssembly { get; init; }
    public string? PlatformFramework { get; init; }
    public string? Tfm { get; init; }
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Verbose { get; init; }
    public int? Limit { get; init; }
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;
    public HashSet<string> MemberFilter { get; init; } = [];
    public bool ShowDocs { get; init; }

    /// <summary>
    /// Whether the user explicitly set --docs (true or false).
    /// When false, the command decides the default based on context.
    /// </summary>
    public bool DocsExplicitlySet { get; init; }
    public bool UseLocalDocs { get; init; }
    public bool ShowSamples { get; init; }
    public bool SourceLinkOnly { get; init; }
    public bool BrowsableUrls { get; init; }
    public bool IncludeAll { get; init; }
    public string? TypeFilter { get; init; }
    public bool OneLine { get; init; }
    public bool Markdown { get; init; }
    public bool NoHeader { get; init; }
    public bool ShapeOutput { get; init; }
    public bool UnsafeOnly { get; init; }
    public bool CtorOnly { get; init; }
    public int? OverloadIndex { get; init; }
    public string[]? ParamTypes { get; init; }
    public string? FirstParamType { get; init; }
    public bool ShowSelect { get; init; }
    public string? DllPath { get; init; }
    public MethodSourceContext? MethodSource { get; init; }
    public HashSet<string>? IncludeSections { get; init; }
    public HashSet<string>? ExcludeSections { get; init; }
    public string[]? Select { get; init; }
    public bool PreferFields { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
    public TipLevel TipLevel { get; init; } = TipLevel.Minimal;

    /// <summary>
    /// True when invoked via the member command. Member always shows tables (quiet gets summary, minimal+ gets full).
    /// </summary>
    public bool IsMemberCommand { get; init; }

    /// <summary>
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public bool IsRawOutput => JsonOutput || !UseMarkdown || ShapeOutput;

    /// <summary>
    /// Resolved output format: true when full markdown rendering should be used.
    /// </summary>
    public bool UseMarkdown => Markdown || Verbosity >= Verbosity.Normal;

    /// <summary>
    /// Resolved shape output: true when --shape or single type without explicit --oneline/--markdown.
    /// </summary>
    public bool UseShape => ShapeOutput || (TypeName != null && !IsMemberCommand && !OneLine && !UseMarkdown);
}

/// <summary>
/// Resolved source context for a single method, ready for rendering.
/// </summary>
public record MethodSourceContext(string SourceCode, string? SourceUrl);
