using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

public enum DependencyGraphNodeKind
{
    Type,
    Library,
    Package,
    RestoredRoot,
    RestoredProject,
    RestoredPackage,
    PackageBoundary,
    PackageFailure,
    PackageBudget,
}

public enum DependencyGraphResolutionState
{
    Declared,
    Resolved,
    Unavailable,
    Rejected,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(DependencyGraphNodeIdentity.Type), "type")]
[JsonDerivedType(typeof(DependencyGraphNodeIdentity.Library), "library")]
[JsonDerivedType(typeof(DependencyGraphNodeIdentity.Package), "package")]
[JsonDerivedType(typeof(DependencyGraphNodeIdentity.RestoredRoot), "restored-root")]
[JsonDerivedType(
    typeof(DependencyGraphNodeIdentity.RestoredProject),
    "restored-project")]
[JsonDerivedType(
    typeof(DependencyGraphNodeIdentity.RestoredPackage),
    "restored-package")]
[JsonDerivedType(
    typeof(DependencyGraphNodeIdentity.PackageBoundary),
    "package-boundary")]
[JsonDerivedType(
    typeof(DependencyGraphNodeIdentity.PackageFailure),
    "package-failure")]
[JsonDerivedType(
    typeof(DependencyGraphNodeIdentity.PackageBudget),
    "package-budget")]
public abstract record DependencyGraphNodeIdentity
{
    private DependencyGraphNodeIdentity()
    {
    }

    [JsonIgnore]
    public abstract DependencyGraphNodeKind Kind { get; }

    public sealed record Type(string Name) : DependencyGraphNodeIdentity
    {
        [JsonIgnore]
        public override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.Type;
    }

    public sealed record Library(
        [property: JsonConverter(
            typeof(DependencyGraphManagedMetadataIdentityJsonConverter))]
        ManagedMetadataIdentity Identity) :
        DependencyGraphNodeIdentity
    {
        [JsonIgnore]
        public override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.Library;
    }

    public sealed record Package(string Id, string Version) :
        DependencyGraphNodeIdentity
    {
        [JsonIgnore]
        public override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.Package;
    }

    public sealed record RestoredRoot(RestoredProjectRootIdentity Identity) :
        DependencyGraphNodeIdentity
    {
        [JsonIgnore]
        public override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.RestoredRoot;
    }

    public sealed record RestoredProject(
        RestoredProjectProjectNodeIdentity Identity) :
        DependencyGraphNodeIdentity
    {
        [JsonIgnore]
        public override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.RestoredProject;
    }

    public sealed record RestoredPackage(
        RestoredProjectPackageNodeIdentity Identity) :
        DependencyGraphNodeIdentity
    {
        [JsonIgnore]
        public override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.RestoredPackage;
    }

    public sealed record PackageBoundary(
        int SourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
        string PackageId,
        string VersionConstraint) : DependencyGraphNodeIdentity
    {
        [JsonIgnore]
        public override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.PackageBoundary;
    }

    public sealed record PackageFailure(
        int SourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
        string PackageId,
        string VersionConstraint) : DependencyGraphNodeIdentity
    {
        [JsonIgnore]
        public override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.PackageFailure;
    }

    public sealed record PackageBudget(
        int SourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
        string PackageId,
        string VersionConstraint) : DependencyGraphNodeIdentity
    {
        [JsonIgnore]
        public override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.PackageBudget;
    }
}

public sealed record DependencyGraphRootOccurrence(
    int OccurrenceIndex,
    int NodeId);

public sealed record DependencyGraphNode(
    int Id,
    DependencyGraphNodeIdentity Identity,
    InertString Label);

public enum DependencyGraphDepthBoundaryProducerKind
{
    Type,
    Library,
    Package,
    Restored,
}

/// <summary>
/// Producer-issued context that identifies a graph node or package projection
/// whose outgoing relationships were intentionally excluded by a depth bound.
/// </summary>
public sealed record DependencyGraphDepthBoundary(
    int NodeId,
    int? PackageProjectionId,
    int MaximumDepth,
    ImmutableArray<int> RootOccurrences,
    DependencyGraphDepthBoundaryProducerKind Producer)
{
    public ImmutableArray<int> RootOccurrences { get; init; } =
        RootOccurrences.IsDefault ? [] : RootOccurrences;
}

/// <summary>
/// One source-relative package-manifest projection retained separately from
/// the semantic package node it describes.
/// </summary>
public sealed record DependencyGraphPackageProjection(
    int Id,
    int NodeId,
    PackageDependencyTraversalProjectionKind Kind,
    PackageDependencyTraversalProjectionExpansion Expansion,
    PackageDependencyEvidenceRoot? Evidence,
    DependencyInspectionPackageCandidate? Candidate,
    int? RootOccurrence,
    ImmutableArray<DependencyInspectionPackageAuthorityFailure> Diagnostics)
{
    public ImmutableArray<DependencyInspectionPackageAuthorityFailure>
        Diagnostics
    { get; init; } = Diagnostics.IsDefault ? [] : Diagnostics;

    [JsonIgnore]
    public PackageAcquisitionCandidate? RuntimeCandidate { get; init; }

    [JsonIgnore]
    public ImmutableArray<PackageAuthorityFailure> RuntimeDiagnostics
    {
        get;
        init;
    } = [];
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(DependencyGraphEvidenceIdentity.AssemblyReference),
    "assembly-reference")]
[JsonDerivedType(
    typeof(DependencyGraphEvidenceIdentity.PackageVersionConstraint),
    "package-version-constraint")]
[JsonDerivedType(
    typeof(DependencyGraphEvidenceIdentity.PackageDeclaration),
    "package-declaration")]
[JsonDerivedType(
    typeof(DependencyGraphEvidenceIdentity.RestoredProjectRelationship),
    "restored-project-relationship")]
[JsonDerivedType(
    typeof(DependencyGraphEvidenceIdentity.RestoredPackageRelationship),
    "restored-package-relationship")]
public abstract record DependencyGraphEvidenceIdentity
{
    private DependencyGraphEvidenceIdentity()
    {
    }

    public sealed record AssemblyReference(
        AssemblyReferenceIdentity Identity) :
        DependencyGraphEvidenceIdentity;

    public sealed record PackageVersionConstraint(
        InertString Value) :
        DependencyGraphEvidenceIdentity;

    public sealed record PackageDeclaration(
        int SourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity Identity,
        InertString VersionConstraint) :
        DependencyGraphEvidenceIdentity;

    public sealed record RestoredProjectRelationship(
        RestoredProjectProjectRelationshipIdentity Identity) :
        DependencyGraphEvidenceIdentity;

    public sealed record RestoredPackageRelationship(
        RestoredProjectEdgeIdentity Identity) :
        DependencyGraphEvidenceIdentity;
}

public sealed record DependencyGraphEdge(
    int Id,
    int SourceNodeId,
    int TargetNodeId,
    string Relationship,
    ImmutableArray<int> RootOccurrences,
    int MinimumDepth,
    DependencyGraphResolutionState Resolution,
    DependencyGraphEvidenceIdentity? EvidenceIdentity,
    int? SourcePackageProjectionId = null,
    int? TargetPackageProjectionId = null,
    PackageDependencyTraversalEdgeEmissionAuthority? PackageEmissionAuthority =
        null,
    ImmutableArray<DependencyInspectionPackageAuthorityFailure>
        PackageDiagnostics = default)
{
    public ImmutableArray<int> RootOccurrences { get; init; } =
        RootOccurrences.IsDefault ? [] : RootOccurrences;

    public ImmutableArray<DependencyInspectionPackageAuthorityFailure>
        PackageDiagnostics
    { get; init; } = PackageDiagnostics.IsDefault ? [] : PackageDiagnostics;

    [JsonIgnore]
    public ImmutableArray<PackageAuthorityFailure> RuntimePackageDiagnostics
    {
        get;
        init;
    } = [];
}

public sealed record DependencyGraphDocument(
    ImmutableArray<DependencyGraphRootOccurrence> Roots,
    ImmutableArray<DependencyGraphNode> Nodes,
    ImmutableArray<DependencyGraphEdge> Edges,
    ImmutableArray<DependencyGraphPackageProjection> PackageProjections,
    ImmutableArray<DependencyGraphDepthBoundary> DepthBoundaries)
{
    public ImmutableArray<DependencyGraphRootOccurrence> Roots { get; init; } =
        Roots.IsDefault ? [] : Roots;

    public ImmutableArray<DependencyGraphNode> Nodes { get; init; } =
        Nodes.IsDefault ? [] : Nodes;

    public ImmutableArray<DependencyGraphEdge> Edges { get; init; } =
        Edges.IsDefault ? [] : Edges;

    public ImmutableArray<DependencyGraphPackageProjection> PackageProjections
    { get; init; } = PackageProjections.IsDefault ? [] : PackageProjections;

    public ImmutableArray<DependencyGraphDepthBoundary> DepthBoundaries
    { get; init; } = DepthBoundaries.IsDefault ? [] : DepthBoundaries;
}
