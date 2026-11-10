using System.Collections.Immutable;
using DotnetInspector.Queries;
using InertText;

namespace DotnetInspector.Sections;

public static class RestoredTraversalGraphProjection
{
    public static DependencyGraphDocument Project(
        RestoredProjectDependencyTraversal traversal,
        int rootOccurrenceIndex,
        InertString rootLabel)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        var nodes = new List<DependencyGraphNode>();
        var nodeIds =
            new Dictionary<RestoredProjectGraphParentIdentity, int>();
        foreach (RestoredProjectTraversalNode node in traversal.Nodes)
        {
            int id = nodes.Count;
            nodeIds.Add(node.Identity, id);
            nodes.Add(
                new DependencyGraphNode(
                    id,
                    Identity(node.Identity),
                    node.Identity is RestoredProjectGraphParentIdentity.Root
                        ? rootLabel
                        : node.Identity
                            is RestoredProjectGraphParentIdentity.Project
                                ? node.SourceProjectSpelling!.Value
                                : PackageLabel(node.Identity)));
        }

        var edges = ImmutableArray.CreateBuilder<DependencyGraphEdge>();
        foreach (RestoredProjectTraversalProjectRelationship relationship in
                 traversal.ProjectRelationships)
        {
            edges.Add(
                new DependencyGraphEdge(
                    edges.Count,
                    nodeIds[relationship.Parent],
                    nodeIds[
                        new RestoredProjectGraphParentIdentity.Project(
                            relationship.Dependency)],
                    "project-reference",
                    [rootOccurrenceIndex],
                    relationship.Distance,
                    DependencyGraphResolutionState.Resolved,
                    new DependencyGraphEvidenceIdentity
                        .RestoredProjectRelationship(
                            relationship.Identity)));
        }
        foreach (RestoredProjectTraversalPackageRelationship relationship in
                 traversal.PackageRelationships)
        {
            edges.Add(
                new DependencyGraphEdge(
                    edges.Count,
                    nodeIds[relationship.Edge.Parent],
                    nodeIds[
                        new RestoredProjectGraphParentIdentity.Package(
                            relationship.Edge.Dependency)],
                    "package-dependency",
                    [rootOccurrenceIndex],
                    relationship.Distance,
                    DependencyGraphResolutionState.Resolved,
                    new DependencyGraphEvidenceIdentity
                        .RestoredPackageRelationship(
                            relationship.Edge.Identity)));
        }

        int rootNodeId = nodeIds[
            new RestoredProjectGraphParentIdentity.Root(traversal.Root)];
        ImmutableArray<DependencyGraphDepthBoundary> depthBoundaries =
        [
            .. traversal.DepthBoundaries.Select(boundary =>
                new DependencyGraphDepthBoundary(
                    nodeIds[boundary.Node],
                    PackageProjectionId: null,
                    boundary.MaximumDepth,
                    [rootOccurrenceIndex],
                    DependencyGraphDepthBoundaryProducerKind.Restored)),
        ];
        return new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(
                rootOccurrenceIndex,
                rootNodeId)],
            [.. nodes],
            edges.ToImmutable(),
            [],
            depthBoundaries);
    }

    private static DependencyGraphNodeIdentity Identity(
        RestoredProjectGraphParentIdentity identity) =>
        identity switch
        {
            RestoredProjectGraphParentIdentity.Root root =>
                new DependencyGraphNodeIdentity.RestoredRoot(root.Identity),
            RestoredProjectGraphParentIdentity.Project project =>
                new DependencyGraphNodeIdentity.RestoredProject(
                    project.Identity),
            RestoredProjectGraphParentIdentity.Package package =>
                new DependencyGraphNodeIdentity.RestoredPackage(
                    package.Identity),
            _ => throw new InvalidOperationException(
                "Unknown restored-project node identity."),
        };

    private static InertString PackageLabel(
        RestoredProjectGraphParentIdentity identity)
    {
        RestoredProjectPackageNodeIdentity package =
            ((RestoredProjectGraphParentIdentity.Package)identity).Identity;
        return InertString.Format(
            TextPolicy.Field,
            $"{Field(package.Coordinate.PackageId)} {Field(package.Coordinate.Version)}");
    }

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
