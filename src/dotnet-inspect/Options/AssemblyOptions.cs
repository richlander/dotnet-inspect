using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for assembly inspection.
/// </summary>
public record AssemblyOptions
{
    /// <summary>
    /// Include SourceLink and determinism audit.
    /// </summary>
    public bool IncludeAudit { get; init; }

    /// <summary>
    /// Include assembly references in output.
    /// </summary>
    public bool IncludeReferences { get; init; }

    /// <summary>
    /// Enable strict audit mode: verify SourceLink URLs are fetchable
    /// and all source files are accessible or embedded.
    /// </summary>
    public bool StrictAudit { get; init; }

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
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Output verbosity level.
    /// </summary>
    public Verbosity Verbosity { get; init; } = Verbosity.Normal;

    /// <summary>
    /// Sections to include (1-indexed). If empty, all sections are included.
    /// </summary>
    public HashSet<int>? IncludeSections { get; init; }

    /// <summary>
    /// Sections to exclude (1-indexed).
    /// </summary>
    public HashSet<int>? ExcludeSections { get; init; }

    /// <summary>
    /// NuGet source configuration options.
    /// </summary>
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// Default options: basic assembly info only.
    /// </summary>
    public static AssemblyOptions Default => new();

    /// <summary>
    /// All inspection features enabled.
    /// </summary>
    public static AssemblyOptions All => new()
    {
        IncludeAudit = true
    };
}
