using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// Configuration options for the extensions command.
/// </summary>
public record ExtensionsOptions : IAssemblySourceOptions
{
    /// <summary>
    /// Target type to find extensions for (e.g., "HttpClient", "IEnumerable&lt;T&gt;").
    /// </summary>
    public string TargetType { get; init; } = "";

    /// <summary>
    /// Packages to search (name or name@version). Can specify multiple.
    /// </summary>
    public string[] Packages { get; init; } = [];

    /// <summary>
    /// Assembly paths to search. Can specify multiple.
    /// </summary>
    public string[] Assemblies { get; init; } = [];

    /// <summary>
    /// Platform assembly names to search (e.g., System.Text.Json). Can specify multiple.
    /// </summary>
    public string[] PlatformAssemblies { get; init; } = [];

    /// <summary>
    /// Platform frameworks to search (runtime, aspnetcore, netstandard). Can specify multiple.
    /// </summary>
    public string[] PlatformFrameworks { get; init; } = [];

    /// <summary>
    /// Include extensions on types reachable via properties/methods.
    /// </summary>
    public bool Reachable { get; init; }

    /// <summary>
    /// Maximum depth for reachable type traversal (default: 2).
    /// </summary>
    public int Depth { get; init; } = 2;

    /// <summary>
    /// Target framework moniker (e.g., net8.0).
    /// </summary>
    public string? Tfm { get; init; }

    /// <summary>
    /// Include hidden (EditorBrowsable.Never) and obsolete members.
    /// </summary>
    public bool IncludeAll { get; init; }

    /// <summary>
    /// Limit number of results per source.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Output as JSON.
    /// </summary>
    public bool JsonOutput { get; init; }

    /// <summary>
    /// Minified JSON output.
    /// </summary>
    public bool CompactJson { get; init; }

    /// <summary>
    /// Show progress messages on stderr.
    /// </summary>
    public bool Verbose { get; init; }

    /// <summary>
    /// Output verbosity level.
    /// </summary>
    public Verbosity Verbosity { get; init; } = Verbosity.Normal;

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
        PlatformFrameworks.Length > 0;
}
