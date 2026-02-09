namespace DotnetInspector.Services;

/// <summary>
/// Represents a resolved dependency in a transitive dependency tree.
/// </summary>
public record DependencyNode(
    string PackageId,
    string Version,
    string? Author,
    List<DependencyNode> Children);
