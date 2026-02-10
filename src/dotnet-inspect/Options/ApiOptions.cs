using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Options for the api command.
/// </summary>
public record ApiOptions
{
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
    public HashSet<string>? MemberFilter { get; init; }
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
    public bool SignaturesOnly { get; init; }
    public bool ShapeOutput { get; init; }
    public bool UnsafeOnly { get; init; }
    public bool CtorOnly { get; init; }
    public HashSet<string>? IncludeSections { get; init; }
    public HashSet<string>? ExcludeSections { get; init; }
    public NuGetSourceOptions? SourceOptions { get; init; }
    public TipLevel TipLevel { get; init; } = TipLevel.Minimal;

    /// <summary>
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public bool IsRawOutput => JsonOutput || SignaturesOnly || ShapeOutput;
}
