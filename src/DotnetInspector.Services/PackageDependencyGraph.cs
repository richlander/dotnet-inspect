using NuGet.Versioning;

namespace DotnetInspector.Services;

/// <summary>An exact package coordinate used as dependency graph identity.</summary>
public sealed record PackageDependencyIdentity
{
    public PackageDependencyIdentity(
        string packageId,
        string version)
    {
        PackageId = packageId;
        Version = NuGetVersion.TryParse(
            version,
            out NuGetVersion? parsed)
            ? parsed.ToNormalizedString()
            : version;
    }

    public string PackageId { get; }

    public string Version { get; }
}

/// <summary>One package node retained independently of tree expansion.</summary>
public sealed record PackageDependencyGraphNode(
    PackageDependencyIdentity Identity,
    string? Author);

/// <summary>The acquisition state of a package dependency target.</summary>
public enum PackageDependencyResolutionState
{
    Declared,
    Resolved,
    Unavailable,
}

/// <summary>
/// One normalized package dependency relationship. The declaration constraint
/// remains evidence beside the selected target coordinate.
/// </summary>
public sealed record PackageDependencyRelationship(
    PackageDependencyIdentity Source,
    PackageDependencyIdentity Target,
    string VersionConstraint,
    PackageDependencyResolutionState Resolution,
    int Ordinal);

/// <summary>
/// Package dependency tree compatibility data plus its lossless graph
/// relationships.
/// </summary>
public sealed record PackageDependencyGraph(
    IReadOnlyList<PackageDependencyGraphNode> Nodes,
    IReadOnlyList<PackageDependencyRelationship> Relationships,
    List<DependencyNode> Tree);
