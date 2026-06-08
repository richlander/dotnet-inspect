using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Options for the source command.
/// </summary>
public record SourceOptions
{
    /// <summary>
    /// Type name to inspect (positional argument). Null for all-types listing.
    /// </summary>
    public string? TypeName { get; init; }

    /// <summary>
    /// Member name for method-level source resolution (positional argument).
    /// When set, resolves line numbers via PDB sequence points.
    /// </summary>
    public string? MemberName { get; init; }

    /// <summary>
    /// Overload index (1-based) for disambiguating overloaded methods (e.g., Parse:2).
    /// </summary>
    public int? OverloadIndex { get; init; }

    /// <summary>
    /// IL offset in "token+offset" format (e.g., "0x6000001+0x5").
    /// When set, resolves a method token and IL offset to a source location.
    /// </summary>
    public string? ILOffset { get; init; }

    // Source resolution
    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? PlatformAssembly { get; init; }
    public string? PlatformFramework { get; init; }
    public string? Tfm { get; init; }

    /// <summary>
    /// Type filter glob pattern (e.g., *Json*, Progress*).
    /// </summary>
    public string? TypeFilter { get; init; }

    /// <summary>
    /// Include hidden (EditorBrowsable.Never) and obsolete types.
    /// </summary>
    public bool IncludeAll { get; init; }

    /// <summary>
    /// Verify SourceLink URLs are accessible via HTTP HEAD.
    /// </summary>
    public bool Verify { get; init; }

    /// <summary>
    /// Use /blob/ URLs for browser viewing (default: /raw/ for LLM consumption).
    /// </summary>
    public bool BrowsableUrls { get; init; }

    /// <summary>
    /// Print source file contents to stdout.
    /// </summary>
    public bool Cat { get; init; }

    // Output
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool OneLine { get; init; }
    public bool Tsv { get; init; }
    public bool OneLineExplicitlySet { get; init; }
    public bool PlainText { get; init; }
    public bool NoHeader { get; init; }
    public int? Limit { get; init; }
    public int? Rows { get; init; }
    public bool Verbose { get; init; }

    // Projection & discovery
    public HashSet<string>? IncludeSections { get; init; }
    public string[]? Discover { get; init; }
    public bool Tree { get; init; }
    public string[]? Select { get; init; }
    public string[]? Columns { get; init; }
    public string[]? Fields { get; init; }

    // Tips
    public TipLevel TipLevel { get; init; } = TipLevel.Minimal;

    // NuGet
    public NuGetSourceOptions? NuGetOptions { get; init; }

    /// <summary>
    /// True when no explicit output format was selected (default invocation).
    /// </summary>
    public bool IsDefaultInvocation => OneLine && !JsonOutput && !OneLineExplicitlySet;

    /// <summary>
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public bool IsRawOutput => JsonOutput || OneLine || NoHeader || Cat;
}
