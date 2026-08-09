using DotnetInspector.Packages;
using InertText;

namespace DotnetInspector.Services;

/// <summary>
/// All metadata parsed from a .nuspec file.
/// </summary>
public class NuspecData
{
    public string? PackageName { get; set; }
    public string? ManifestVersion { get; set; }
    public string? Version { get; set; }
    /// <summary>
    /// The package description, contained as prose at the nuspec boundary.
    /// </summary>
    /// <remarks>
    /// Descriptions are presentation-only. Carrying the treated value through the DTO keeps
    /// later services and models from accidentally recovering an untreated string.
    /// </remarks>
    public InertString? Description { get; set; }
    public string? Authors { get; set; }
    public string? Repository { get; set; }
    public string? RepositoryType { get; set; }
    public string? RepositoryCommit { get; set; }
    public string? License { get; set; }
    public string? LicenseUrl { get; set; }
    public List<string>? PackageTypes { get; set; }
    public bool IsToolPackage { get; set; }
    public string? ReadmeFile { get; set; }
    public List<DependencyGroup>? DependencyGroups { get; set; }
}
