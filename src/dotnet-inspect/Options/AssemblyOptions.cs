using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for assembly inspection.
/// </summary>
public record AssemblyOptions
{
    /// <summary>
    /// Assembly name within a package (positional argument).
    /// Null when inspecting via --package, --platform, or direct file path.
    /// </summary>
    public string? AssemblyName { get; init; }

    /// <summary>
    /// Show PE metadata (Assembly Info section: name, version, TFM, arch, signed, etc.).
    /// </summary>
    public bool IncludeMetadata { get; init; }

    /// <summary>
    /// Include assembly references in output.
    /// </summary>
    public bool IncludeReferences { get; init; }

    /// <summary>
    /// Show assembly dependencies as a clean deduplicated tree.
    /// </summary>
    public bool IncludeDependencies { get; init; }

    /// <summary>
    /// Path to a NuGet package to extract the assembly from.
    /// If null, the assembly is loaded from the local filesystem.
    /// </summary>
    public string? PackagePath { get; init; }

    /// <summary>
    /// Include prerelease/preview versions when resolving an unversioned package.
    /// </summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>
    /// Platform assembly name (e.g., System.Text.Json).
    /// Resolves to installed .NET runtime assembly.
    /// </summary>
    public string? PlatformAssembly { get; init; }

    /// <summary>
    /// Optional platform framework family (runtime, aspnetcore, netstandard).
    /// </summary>
    public string? PlatformFramework { get; init; }

    /// <summary>
    /// Platform shared runtime version. Searches runtime frameworks in priority order when framework is not specified.
    /// </summary>
    public string? PlatformVersion { get; init; }

    /// <summary>
    /// Target framework moniker to select assembly from package.
    /// Alternative to specifying full assembly path.
    /// </summary>
    public string? Tfm { get; init; }

    /// <summary>
    /// Output as JSON instead of MDF.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Explicit markdown output requested.
    /// </summary>
    public bool Markdown { get; init; }

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
    /// Suppress column headers (use with --table or --tsv).
    /// </summary>
    public bool NoHeader { get; init; }

    /// <summary>
    /// True when a tabular output flag was explicitly passed (not just the default format).
    /// </summary>
    public bool OneLineExplicitlySet { get; init; }

    /// <summary>
    /// True when the user explicitly chose an output format via CLI flags.
    /// </summary>
    public bool FormatExplicitlySet { get; init; }

    /// <summary>
    /// Resolved output format.
    /// </summary>
    public OutputFormat Format { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Output verbosity level.
    /// </summary>
    public Verbosity Verbosity { get; init; } = Verbosity.Normal;

    /// <summary>
    /// The user's originally requested verbosity, before any internal force-bumping for
    /// <c>-S</c>/<c>-D</c>. Capability authorization (network work) keys off this so
    /// a forced Detailed bump never silently authorizes downloads. Defaults to <see cref="Verbosity"/>.
    /// </summary>
    public Verbosity? UserVerbosityOverride { get; init; }

    /// <summary>Effective user verbosity (falls back to <see cref="Verbosity"/> when not set).</summary>
    public Verbosity UserVerbosity => UserVerbosityOverride ?? Verbosity;

    /// <summary>
    /// Sections to include by heading name. If null, all sections are included.
    /// </summary>
    public HashSet<string>? IncludeSections { get; init; }

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
    /// NuGet source configuration options.
    /// </summary>
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// Extract embedded resources to a directory.
    /// </summary>
    public string? ExtractResources { get; init; }

    /// <summary>
    /// Default options: basic assembly info only.
    /// </summary>
    public static AssemblyOptions Default => new();

    /// <summary>
    /// All inspection features enabled.
    /// </summary>
    public static AssemblyOptions All => new()
    {
    };

    /// <summary>
    /// True when no explicit output format was selected (default → table).
    /// </summary>
    public bool IsDefaultInvocation => OneLine && !JsonOutput;

    /// <summary>
    /// True when the user has opted into rich markdown output (via --markdown or -v:*).
    /// </summary>
    public bool VerbosityEnabled => !OneLine && !JsonOutput;

    /// <summary>
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public bool IsRawOutput => JsonOutput || OneLine || Jsonl || NoHeader || ExtractResources != null || Count;
}
