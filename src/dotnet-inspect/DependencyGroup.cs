using Markout;

namespace DotnetInspector;

[MarkoutSerializable]
public class DependencyGroup
{
    [MarkoutPropertyName("Target Framework")]
    public string TargetFramework { get; set; } = "";

    [MarkoutIgnore]
    public List<PackageDependency> Dependencies { get; set; } = [];
}
