using MarkOut;

namespace DotnetInspector;

[MarkOutSerializable]
public class PackageDependency
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
}
