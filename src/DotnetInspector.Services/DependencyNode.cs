namespace DotnetInspector.Services;

public enum DependencyNodeResolutionState
{
    Resolved,
    Unresolved,
}

/// <summary>
/// Represents a dependency in a transitive dependency tree.
/// </summary>
public record DependencyNode(
    string PackageId,
    string Version,
    string? Author,
    List<DependencyNode> Children)
{
    /// <summary>
    /// The concrete version selected from <see cref="Version"/>, when one was
    /// available.
    /// </summary>
    public string? ResolvedVersion { get; init; }

    /// <summary>
    /// Whether the package manifest needed to expand this node was available.
    /// </summary>
    public DependencyNodeResolutionState Resolution { get; init; } =
        DependencyNodeResolutionState.Resolved;
}
