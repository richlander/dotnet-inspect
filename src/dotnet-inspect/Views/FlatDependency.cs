using Markout;

namespace DotnetInspector.Views;

public class FlatDependency
{
    [MarkoutPropertyName("Target Framework")]
    public string TargetFramework { get; set; } = "";

    [MarkoutPropertyName("Package")]
    public string Id { get; set; } = "";

    public string Version { get; set; } = "";
}
