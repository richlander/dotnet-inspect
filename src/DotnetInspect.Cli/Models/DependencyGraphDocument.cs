using System.Collections.Immutable;
using ILInspector.Metadata;
using InertText;
using DotnetInspector.Queries;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspect.Cli.Models;

internal enum DependencyGraphNodeKind
{
    Type,
    Library,
    Package,
    Project,
    Declaration,
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

    internal sealed record Coordinate(PackageSourceCoordinate Value) : DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind => DependencyGraphNodeKind.Package;
    }

    internal sealed record Restored(RestoredProjectGraphParentIdentity Value) : DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind =>
            Value is RestoredProjectGraphParentIdentity.Package
                ? DependencyGraphNodeKind.Package : DependencyGraphNodeKind.Project;
    }

    internal sealed record Declaration(
        int ProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity Value,
        PackageDependencyTraversalEdgeEmissionAuthority Authority) : DependencyGraphNodeIdentity
    {
        internal override DependencyGraphNodeKind Kind => DependencyGraphNodeKind.Declaration;
    }
}

internal sealed record DependencyGraphRootOccurrence(
    int OccurrenceIndex,
    int NodeId);

internal sealed record DependencyGraphNode(
    int Id,
    DependencyGraphNodeIdentity Identity,
    InertString Label);

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

    internal sealed record Declaration(
        int SourceProjectionIndex,
        PackageDependencyEvidenceDeclaration Value,
        PackageDependencyTraversalEdgeEmissionAuthority Authority,
        int? TargetProjectionIndex,
        PackageAcquisitionCandidate? Candidate,
        int TraversalEdgeIndex) : DependencyGraphEvidenceIdentity;

    internal sealed record RestoredPackage(RestoredProjectGraphEdge Value) : DependencyGraphEvidenceIdentity;

    internal sealed record RestoredProject(RestoredProjectTraversalProjectRelationship Value) : DependencyGraphEvidenceIdentity;
}

internal sealed record DependencyGraphEdge(
    int Id,
    int SourceNodeId,
    int TargetNodeId,
    string Relationship,
    ImmutableArray<int> RootOccurrences,
    int MinimumDepth,
    DependencyGraphResolutionState Resolution,
    DependencyGraphEvidenceIdentity? EvidenceIdentity)
{
    internal ImmutableDictionary<int, int> RootDistances { get; init; } =
        ImmutableDictionary<int, int>.Empty;
}

internal sealed record DependencyGraphDocument(
    ImmutableArray<DependencyGraphRootOccurrence> Roots,
    ImmutableArray<DependencyGraphNode> Nodes,
    ImmutableArray<DependencyGraphEdge> Edges)
{
    internal static DependencyGraphDocument Empty { get; } = new([], [], []);
    internal ImmutableArray<DependencyGraphBoundary> Boundaries { get; init; } = [];
}

internal sealed record DependencyGraphBoundary(
    int RootOccurrence, int NodeId, string Kind, int? MaximumDepth);
