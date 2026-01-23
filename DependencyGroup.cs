using MarkdownData;

namespace DotnetInspector;

[MdfSerializable]
public class DependencyGroup
{
    [MdfPropertyName("Target Framework")]
    public string TargetFramework { get; set; } = "";

    [MdfIgnore]
    public List<PackageDependency> Dependencies { get; set; } = [];
}
