using Markout;

namespace DotnetInspector;

[MarkoutSerializable]
public class FlatDependency
{
    [MarkoutPropertyName("Target Framework")]
    public string TargetFramework { get; set; } = "";

    public string Id { get; set; } = "";

    public string Version { get; set; } = "";
}
