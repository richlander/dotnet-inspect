using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Options;

/// <summary>The explicit root families admitted by asset-mode <c>depends</c>.</summary>
public enum DependsAssetRootKind
{
    Package,
    Nuspec,
    Library,
    Project,
}

/// <summary>One explicit asset root in command-line occurrence order.</summary>
public sealed record DependsAssetRoot(
    int OccurrenceIndex,
    DependsAssetRootKind Kind,
    string Value);

/// <summary>
/// Configuration options for the depends command.
/// </summary>
public record DependsOptions : IAssemblySourceOptions, IProjectionOptions
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

    /// <summary>Asset-mode roots in heterogeneous command-line occurrence order.</summary>
    public DependsAssetRoot[] AssetRoots { get; init; } = [];

    /// <summary>The exclusive bounded package-prefix root set.</summary>
    public string? PackagePrefix { get; init; }

    /// <summary>Whether latest remote package roots may select prerelease versions.</summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>The package-prefix root bound, or null for the default.</summary>
    public int? MaxPackages { get; init; }

    /// <summary>The positive dependency traversal depth, or null for complete traversal.</summary>
    public int? Depth { get; init; }

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

    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;

    /// <summary>The selected output format for the graph projection.</summary>
    public OutputFormat Format { get; init; } = OutputFormat.Markdown;

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

    /// <summary>Render the graph as a standalone plain-text tree.</summary>
    public bool Tree { get; init; }

    /// <summary>
    /// Limit data rows per rendered table.
    /// </summary>
    public RowWindow? Rows { get; init; }

    /// <summary>
    /// Host-neutral semantic selection over type-dependency relationship rows.
    /// </summary>
    public RowSelectionIntent<TypeDependencyRowOrder>? TypeDependencyRows
    {
        get;
        init;
    }

    /// <summary>
    /// Output the number of selected logical dependency edges.
    /// </summary>
    public bool Count { get; init; }

    public bool Tabular { get; init; }

    public bool Tsv { get; init; }

    public bool Jsonl { get; init; }

    /// <summary>Suppress table and TSV headers.</summary>
    public bool NoHeader { get; init; }

    public string[]? Discover { get; init; }

    public bool Effective { get; init; }

    public bool Schema { get; init; }

    public string[]? Select { get; init; }

    public bool SelectDefault { get; init; }

    public string[]? Columns { get; init; }

    public string[]? Fields { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// NuGet source configuration options.
    /// </summary>
    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>
    /// Portable Workspace share output for package dependency mode.
    /// </summary>
    public WorkspaceShareFormat? ShareFormat { get; init; }

    internal bool LineWindowExplicitlySet { get; init; }

    internal bool OutputFormatExplicitlySet { get; init; }

    internal SearchSourceSelection? SourceSelection { get; init; }

    /// <summary>
    /// Returns true if a scope has been selected, including a normalized empty contribution.
    /// </summary>
    public bool HasAnyScope =>
        SourceSelection is not null ||
        Packages.Length > 0 ||
        Assemblies.Length > 0 ||
        PlatformAssemblies.Length > 0 ||
        PlatformFrameworks.Length > 0 ||
        Projects.Length > 0;

    public bool HasAssetRoots =>
        AssetRoots.Length > 0 || PackagePrefix is not null;

    /// <summary>
    /// True when output is raw text (not rendered markdown).
    /// </summary>
    public bool IsRawOutput =>
        JsonOutput
        || ShareFormat is not null
        || Tree
        || Format is OutputFormat.PlainText
            or OutputFormat.Mermaid
            or OutputFormat.Table
            or OutputFormat.Tsv
            or OutputFormat.Jsonl;

    /// <summary>
    /// True when in type dependency mode (default when no --library/--package).
    /// </summary>
    public bool IsTypeMode =>
        !string.IsNullOrEmpty(TargetType)
        && LibraryName is null
        && PackageName is null;

    /// <summary>
    /// True when in library dependency mode.
    /// </summary>
    public bool IsLibraryMode => LibraryName != null;

    /// <summary>
    /// True when in package dependency mode.
    /// </summary>
    public bool IsPackageMode => PackageName != null;
}
