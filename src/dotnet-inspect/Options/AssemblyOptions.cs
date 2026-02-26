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
    /// Verify SourceLink URLs are fetchable and all source files are accessible.
    /// </summary>
    public bool IncludeSourcelinkAudit { get; init; }

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
    /// Platform assembly name (e.g., System.Text.Json).
    /// Resolves to installed .NET runtime assembly.
    /// </summary>
    public string? PlatformAssembly { get; init; }

    /// <summary>
    /// Platform framework (runtime, aspnetcore, netstandard).
    /// Use @version for specific version (e.g., runtime@9.0.12).
    /// </summary>
    public string? PlatformFramework { get; init; }

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
    /// One result per line, columnar output.
    /// </summary>
    public bool OneLine { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Output verbosity level.
    /// </summary>
    public Verbosity Verbosity { get; init; } = Verbosity.Normal;

    /// <summary>
    /// Sections to include by heading name. If null, all sections are included.
    /// </summary>
    public HashSet<string>? IncludeSections { get; init; }

    /// <summary>
    /// Sections to exclude by heading name.
    /// </summary>
    public HashSet<string>? ExcludeSections { get; init; }

    /// <summary>
    /// Names to select (sections). Null means all.
    /// </summary>
    public string[]? Select { get; init; }

    /// <summary>
    /// Column names to include. Null means all.
    /// </summary>
    public string[]? Columns { get; init; }

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
        IncludeSourcelinkAudit = true
    };

    /// <summary>
    /// True when no explicit output format was selected (default → oneline).
    /// </summary>
    public bool IsDefaultInvocation => OneLine && !JsonOutput;

    /// <summary>
    /// True when the user has opted into rich markdown output (via --markdown or -v:*).
    /// </summary>
    public bool VerbosityEnabled => !OneLine && !JsonOutput;

    /// <summary>
    /// True when output is raw text (not rendered markdown). Tips should be suppressed.
    /// </summary>
    public bool IsRawOutput => JsonOutput || OneLine || ExtractResources != null;
}
