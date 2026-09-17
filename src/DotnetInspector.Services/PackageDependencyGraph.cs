using System.Collections.Immutable;
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

/// <summary>The expected reason a dependency could not be resolved further.</summary>
public enum DependencyResolutionDiagnosticKind
{
    InvalidVersionRange,
    ManifestUnavailable,
}

/// <summary>One package-attributed partial-resolution diagnostic.</summary>
public sealed record DependencyResolutionDiagnostic(
    PackageDependencyIdentity Target,
    PackageDependencyResolutionState Resolution,
    DependencyResolutionDiagnosticKind Kind);

/// <summary>A partial or complete dependency result and all expected diagnostics.</summary>
public sealed record DependencyResolutionResult<T>(
    T Value,
    ImmutableArray<DependencyResolutionDiagnostic> Diagnostics);

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
