using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// All metadata parsed from a .nuspec file.
/// </summary>
public class NuspecData
{
    public string? PackageName { get; set; }
    public string? ManifestVersion { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
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
