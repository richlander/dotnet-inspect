namespace DotnetInspector;

public class DependencyGroup
{
    public string TargetFramework { get; set; } = "";
    public List<PackageDependency> Dependencies { get; set; } = [];
}
