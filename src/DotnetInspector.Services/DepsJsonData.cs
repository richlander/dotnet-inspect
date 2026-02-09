using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Data parsed from a .deps.json file.
/// </summary>
public class DepsJsonData
{
    public string? RuntimeTargetRid { get; set; }
    public List<PackageDependency>? RuntimeDependencies { get; set; }
}
