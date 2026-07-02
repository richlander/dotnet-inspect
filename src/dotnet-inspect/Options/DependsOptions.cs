using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for the depends command.
/// </summary>
public record DependsOptions : IAssemblySourceOptions
{
    /// <summary>
    /// Target type name to walk dependencies for (type mode).
    /// </summary>
    public string TargetType { get; init; } = "";

    /// <summary>
    /// Library mode: show assembly reference dependencies.
    /// </summary>
    public string? LibraryName { get; init; }

    /// <summary>
    /// Package mode: show NuGet package dependencies.
    /// </summary>
    public string? PackageName { get; init; }

    /// <summary>
    /// Packages to search for type resolution. Can specify multiple.
    /// </summary>
    public string[] Packages { get; init; } = [];

    /// <summary>
    /// Assembly paths to search. Can specify multiple.
    /// </summary>
    public string[] Assemblies { get; init; } = [];

    /// <summary>
    /// Platform assembly names to search. Can specify multiple.
    /// </summary>
    public string[] PlatformAssemblies { get; init; } = [];

    /// <summary>
    /// Platform frameworks to search (runtime, aspnetcore, netstandard). Can specify multiple.
    /// </summary>
    public string[] PlatformFrameworks { get; init; } = [];

    /// <summary>
    /// Project files, directories, or project.assets.json paths to search via restored assets.
    /// </summary>
    public string[] Projects { get; init; } = [];

    /// <summary>
    /// Target framework moniker (e.g., net8.0).
    /// </summary>
    public string? Tfm { get; init; }

    /// <summary>
    /// Output as JSON.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Minified JSON output.
    /// </summary>
    public bool CompactJson { get; init; }

    /// <summary>
    /// Output as standalone Mermaid diagram.
    /// </summary>
    public bool MermaidOutput { get; init; }

    /// <summary>
    /// Embed mermaid diagrams in markdown output (--markdown --mermaid).
    /// </summary>
    public bool EmbeddedMermaid { get; init; }

    /// <summary>
    /// Limit data rows per rendered table.
    /// </summary>
    public int? Rows { get; init; }

    /// <summary>
    /// Output the number of dependency nodes in the rendered tree.
    /// </summary>
    public bool Count { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// NuGet source configuration options.
    /// </summary>
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// Returns true if any search scope is specified.
    /// </summary>
    public bool HasAnyScope =>
        Packages.Length > 0 ||
        Assemblies.Length > 0 ||
        PlatformAssemblies.Length > 0 ||
        PlatformFrameworks.Length > 0 ||
        Projects.Length > 0;

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public bool IsRawOutput => JsonOutput;

    /// <summary>
    /// True when in type dependency mode (default when no --library/--package).
    /// </summary>
    public bool IsTypeMode => LibraryName == null && PackageName == null;

    /// <summary>
    /// True when in library dependency mode.
    /// </summary>
    public bool IsLibraryMode => LibraryName != null;

    /// <summary>
    /// True when in package dependency mode.
    /// </summary>
    public bool IsPackageMode => PackageName != null;
}
