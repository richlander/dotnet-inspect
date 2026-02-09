using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for package inspection.
/// </summary>
public record InspectionOptions
{
    /// <summary>
    /// Include dependency analysis.
    /// </summary>
    public bool IncludeDeps { get; init; }

    /// <summary>
    /// Show package dependencies as a tree view with transitive resolution.
    /// </summary>
    public bool ShowDependencies { get; init; }

    /// <summary>
    /// Target framework to use for dependency resolution (defaults to highest).
    /// </summary>
    public string? Tfm { get; init; }

    /// <summary>
    /// List files in the package instead of showing metadata.
    /// </summary>
    public bool ListFiles { get; init; }

    /// <summary>
    /// List target frameworks in the package, one per line.
    /// </summary>
    public bool ListTfms { get; init; }

    /// <summary>
    /// When listing files, include all files (not just .dll files).
    /// </summary>
    public bool ListAllFiles { get; init; }

    /// <summary>
    /// List available versions of the package from nuget.org.
    /// </summary>
    public bool ListVersions { get; init; }

    /// <summary>
    /// Include prerelease/preview versions when listing versions.
    /// </summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>
    /// Show the README.md content from the package.
    /// </summary>
    public bool ShowReadme { get; init; }

    /// <summary>
    /// Path to write output to instead of stdout.
    /// </summary>
    public string? OutputPath { get; init; }

    /// <summary>
    /// Limit the number of results (for --versions, --files).
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Output as JSON instead of MDF.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Discover available sections and exit.
    /// </summary>
    public bool Discover { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Output verbosity level.
    /// </summary>
    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;

    /// <summary>
    /// Sections to include by heading name. If null, all sections are included.
    /// </summary>
    public HashSet<string>? IncludeSections { get; init; }

    /// <summary>
    /// Sections to exclude by heading name.
    /// </summary>
    public HashSet<string>? ExcludeSections { get; init; }

    /// <summary>
    /// NuGet source configuration options.
    /// </summary>
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// Default options: metadata only.
    /// </summary>
    public static InspectionOptions Default => new();

    /// <summary>
    /// All inspection features enabled.
    /// </summary>
    public static InspectionOptions All => new()
    {
        IncludeDeps = true
    };
}
