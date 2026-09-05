using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Options;

/// <summary>
/// The parsed request for <c>dependency-evidence</c>.
/// </summary>
public sealed record DependencyEvidenceOptions : IProjectionOptions
{
    /// <summary>Repeated package targets: <c>ID</c>, <c>ID@VERSION</c>, or a local <c>.nupkg</c>.</summary>
    public string[] Packages { get; init; } = [];

    /// <summary>Repeated direct nuspec paths.</summary>
    public string[] Nuspecs { get; init; } = [];

    /// <summary>Repeated project files, project directories, or direct assets paths.</summary>
    public string[] Projects { get; init; } = [];

    /// <summary>The single bounded NuGet Gallery manifest-profile root set.</summary>
    public string? PackagePrefix { get; init; }

    /// <summary>The requested target framework selection.</summary>
    public string? Tfm { get; init; }

    /// <summary>Whether latest remote package resolution may select a prerelease version.</summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>The package-prefix bound, or null for the default.</summary>
    public int? MaxPackages { get; init; }

    public Verbosity Verbosity { get; init; } = Verbosity.Minimal;

    public bool JsonOutput { get; init; }

    public bool CompactJson { get; init; }

    public bool Tabular { get; init; }

    public bool Tsv { get; init; }

    public bool Jsonl { get; init; }

    public bool NoHeader { get; init; }

    public string[]? Discover { get; init; }

    public bool Tree { get; init; }

    public bool Schema { get; init; }

    public string[]? Select { get; init; }

    public bool SelectDefault { get; init; }

    public string[]? Columns { get; init; }

    public string[]? Fields { get; init; }

    public bool Count { get; init; }

    public RowWindow? Rows { get; init; }

    public bool Verbose { get; init; }

    public NuGetSourceOptions? SourceOptions { get; init; }

    /// <summary>Whether any explicitly named root was supplied.</summary>
    public bool HasExplicitRoots =>
        Packages.Length > 0 || Nuspecs.Length > 0 || Projects.Length > 0;
}
