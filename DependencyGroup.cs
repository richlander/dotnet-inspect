using MarkOut;

namespace DotnetInspector;

[MarkOutSerializable]
public class DependencyGroup
{
    [MarkOutPropertyName("Target Framework")]
    public string TargetFramework { get; set; } = "";

    [MarkOutIgnore]
    public List<PackageDependency> Dependencies { get; set; } = [];
}
