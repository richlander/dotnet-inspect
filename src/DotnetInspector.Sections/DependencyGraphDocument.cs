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

    public sealed record Library(ManagedMetadataIdentity Identity) :
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
    DependencyGraphDepthBoundaryProducerKind Producer);

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
    PackageAcquisitionCandidate? Candidate,
    int? RootOccurrence,
    ImmutableArray<PackageAuthorityFailure> Diagnostics);

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
    ImmutableArray<PackageAuthorityFailure> PackageDiagnostics = default)
{
    public ImmutableArray<PackageAuthorityFailure> PackageDiagnostics
    { get; init; } = PackageDiagnostics.IsDefault ? [] : PackageDiagnostics;
}

public sealed record DependencyGraphDocument(
    ImmutableArray<DependencyGraphRootOccurrence> Roots,
    ImmutableArray<DependencyGraphNode> Nodes,
    ImmutableArray<DependencyGraphEdge> Edges,
    ImmutableArray<DependencyGraphPackageProjection> PackageProjections,
    ImmutableArray<DependencyGraphDepthBoundary> DepthBoundaries);
