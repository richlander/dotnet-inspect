using Markout;

namespace DotnetInspector;

[MarkoutSerializable]
public class PackageDependency
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
}
