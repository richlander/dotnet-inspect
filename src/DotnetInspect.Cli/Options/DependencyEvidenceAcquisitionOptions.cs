using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Options;

/// <summary>
/// Internal inputs for normalized dependency-evidence acquisition.
/// </summary>
internal sealed record DependencyEvidenceAcquisitionOptions
{
    /// <summary>The requested target framework selection.</summary>
    public string? Tfm { get; init; }

    /// <summary>Whether latest remote package resolution may select a prerelease version.</summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>The package-prefix bound, or null for the default.</summary>
    public int? MaxPackages { get; init; }

    public NuGetSourceOptions? SourceOptions { get; init; }
}
