using MarkdownData;

namespace DotnetInspector;

[MdfSerializable]
public class FlatDependency
{
    [MdfPropertyName("Target Framework")]
    public string TargetFramework { get; set; } = "";

    public string Id { get; set; } = "";

    public string Version { get; set; } = "";
}
