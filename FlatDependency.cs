using MarkOut;

namespace DotnetInspector;

[MarkOutSerializable]
public class FlatDependency
{
    [MarkOutPropertyName("Target Framework")]
    public string TargetFramework { get; set; } = "";

    public string Id { get; set; } = "";

    public string Version { get; set; } = "";
}
