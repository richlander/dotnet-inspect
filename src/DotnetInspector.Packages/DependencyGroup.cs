namespace DotnetInspector.Packages;

public class DependencyGroup
{
    public string TargetFramework { get; set; } = "";
    public List<PackageDependency> Dependencies { get; set; } = [];
    public bool IsImplicitManifestGroup { get; set; }
}
