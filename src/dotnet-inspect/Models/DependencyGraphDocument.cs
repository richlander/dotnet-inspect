using System.Collections.Immutable;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Models;

internal enum DependencyGraphNodeKind
{
    Type,
    Library,
    Package,
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
}

internal sealed record DependencyGraphEdge(
    int Id,
    int SourceNodeId,
    int TargetNodeId,
    string Relationship,
    ImmutableArray<int> RootOccurrences,
    int MinimumDepth,
    DependencyGraphResolutionState Resolution,
    DependencyGraphEvidenceIdentity? EvidenceIdentity);

internal sealed record DependencyGraphDocument(
    ImmutableArray<DependencyGraphRootOccurrence> Roots,
    ImmutableArray<DependencyGraphNode> Nodes,
    ImmutableArray<DependencyGraphEdge> Edges);
