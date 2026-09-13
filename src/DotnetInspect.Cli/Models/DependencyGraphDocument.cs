using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Models;

internal enum DependencyGraphNodeKind
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

internal enum DependencyGraphResolutionState
{
    Declared,
    Resolved,
    Unavailable,
    Rejected,
}

internal abstract record DependencyGraphNodeIdentity
{
    private DependencyGraphNodeIdentity()
    {
    }

    internal abstract DependencyGraphNodeKind Kind { get; }

    internal sealed record Type(string Name) : DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.Type;
    }

    internal sealed record Library(ManagedMetadataIdentity Identity) :
        DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.Library;
    }

    internal sealed record Package(string Id, string Version) :
        DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.Package;
    }

    internal sealed record RestoredRoot(RestoredProjectRootIdentity Identity) :
        DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.RestoredRoot;
    }

    internal sealed record RestoredProject(
        RestoredProjectProjectNodeIdentity Identity) :
        DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.RestoredProject;
    }

    internal sealed record RestoredPackage(
        RestoredProjectPackageNodeIdentity Identity) :
        DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.RestoredPackage;
    }

    internal sealed record PackageBoundary(
        int SourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
        string PackageId,
        string VersionConstraint) : DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.PackageBoundary;
    }

    internal sealed record PackageFailure(
        int SourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
        string PackageId,
        string VersionConstraint) : DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.PackageFailure;
    }

    internal sealed record PackageBudget(
        int SourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity,
        string PackageId,
        string VersionConstraint) : DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            DependencyGraphNodeKind.PackageBudget;
    }
}

internal sealed record DependencyGraphRootOccurrence(
    int OccurrenceIndex,
    int NodeId);

internal sealed record DependencyGraphNode(
    int Id,
    DependencyGraphNodeIdentity Identity,
    InertString Label);

internal enum DependencyGraphDepthBoundaryProducerKind
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
internal sealed record DependencyGraphDepthBoundary(
    int NodeId,
    int? PackageProjectionId,
    int MaximumDepth,
    ImmutableArray<int> RootOccurrences,
    DependencyGraphDepthBoundaryProducerKind Producer);

/// <summary>
/// One source-relative package-manifest projection retained separately from
/// the semantic package node it describes.
/// </summary>
internal sealed record DependencyGraphPackageProjection(
    int Id,
    int NodeId,
    PackageDependencyTraversalProjectionKind Kind,
    PackageDependencyTraversalProjectionExpansion Expansion,
    PackageDependencyEvidenceRoot? Evidence,
    PackageAcquisitionCandidate? Candidate,
    int? RootOccurrence,
    ImmutableArray<PackageAuthorityFailure> Diagnostics);

internal abstract record DependencyGraphEvidenceIdentity
{
    private DependencyGraphEvidenceIdentity()
    {
    }

    internal sealed record AssemblyReference(
        AssemblyReferenceIdentity Identity) :
        DependencyGraphEvidenceIdentity;

    internal sealed record PackageVersionConstraint(
        InertString Value) :
        DependencyGraphEvidenceIdentity;

    internal sealed record PackageDeclaration(
        int SourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity Identity,
        InertString VersionConstraint) :
        DependencyGraphEvidenceIdentity;

    internal sealed record RestoredProjectRelationship(
        RestoredProjectProjectRelationshipIdentity Identity) :
        DependencyGraphEvidenceIdentity;

    internal sealed record RestoredPackageRelationship(
        RestoredProjectEdgeIdentity Identity) :
        DependencyGraphEvidenceIdentity;
}

internal sealed record DependencyGraphEdge(
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
    ImmutableArray<PackageAuthorityFailure> PackageDiagnostics = default);

internal sealed record DependencyGraphDocument(
    ImmutableArray<DependencyGraphRootOccurrence> Roots,
    ImmutableArray<DependencyGraphNode> Nodes,
    ImmutableArray<DependencyGraphEdge> Edges,
    ImmutableArray<DependencyGraphPackageProjection> PackageProjections,
    ImmutableArray<DependencyGraphDepthBoundary> DepthBoundaries);
