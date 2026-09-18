using System.Collections.Immutable;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspector.Sections.Tests;

public sealed class DependencyHierarchyDocumentTests
{
    [Fact]
    public void EmptyGraphProducesEmptyHierarchy()
    {
        DependencyHierarchyDocument hierarchy =
            DependencyHierarchyDocument.Create(
                new DependencyGraphDocument([], [], [], [], []));

        Assert.Empty(hierarchy.Roots);
        Assert.Empty(hierarchy.Occurrences);
        Assert.Empty(hierarchy.BackingGraph.Nodes);
        Assert.Empty(hierarchy.BackingGraph.Edges);
    }

    [Fact]
    public void SharedTargetUnderTwoParentsRetainsRevisitOccurrence()
    {
        DependencyHierarchyDocument hierarchy =
            DependencyHierarchyDocument.Create(
                Graph(
                    [new DependencyGraphRootOccurrence(1, 0)],
                    ["Root", "A", "B", "Shared", "Leaf"],
                    [
                        Edge(0, 0, 1, [1]),
                        Edge(1, 0, 2, [1]),
                        Edge(2, 1, 3, [1]),
                        Edge(3, 2, 3, [1]),
                        Edge(4, 3, 4, [1]),
                    ]));

        Assert.Equal(
            [0, 2, 4, 1, 3],
            hierarchy.Occurrences.Select(
                static occurrence => occurrence.IncomingEdgeId));
        Assert.Equal(
            [
                DependencyHierarchyOccurrenceDisposition.Expanded,
                DependencyHierarchyOccurrenceDisposition.Expanded,
                DependencyHierarchyOccurrenceDisposition.Expanded,
                DependencyHierarchyOccurrenceDisposition.Expanded,
                DependencyHierarchyOccurrenceDisposition.Revisit,
            ],
            hierarchy.Occurrences.Select(
                static occurrence => occurrence.Disposition));

        DependencyHierarchyOccurrence revisit = hierarchy.Occurrences[4];
        Assert.Equal(3, revisit.TargetNodeId);
        Assert.Equal(
            new DependencyHierarchyOccurrenceIdentity(
                new DependencyRootOccurrenceIdentity(1),
                4),
            revisit.ParentIdentity);
        Assert.Equal(2, revisit.Depth);
    }

    [Fact]
    public void EachRootExpandsSharedCanonicalNodeIndependently()
    {
        DependencyHierarchyDocument hierarchy =
            DependencyHierarchyDocument.Create(
                Graph(
                    [
                        new DependencyGraphRootOccurrence(7, 0),
                        new DependencyGraphRootOccurrence(3, 1),
                    ],
                    ["First", "Second", "Shared", "Leaf"],
                    [
                        Edge(0, 0, 2, [7]),
                        Edge(1, 1, 2, [3]),
                        Edge(2, 2, 3, [7, 3]),
                    ]));

        Assert.Equal(
            [7, 3],
            hierarchy.Roots.Select(
                static root => root.RootOccurrence.Value));
        Assert.Equal(
            [0, 1],
            hierarchy.Roots.Select(static root => root.RootPosition));
        Assert.Equal(
            [0, 2, 1, 2],
            hierarchy.Occurrences.Select(
                static occurrence => occurrence.IncomingEdgeId));
        Assert.Equal(
            [7, 7, 3, 3],
            hierarchy.Occurrences.Select(
                static occurrence => occurrence.RootOccurrence.Value));
        Assert.Equal(
            [1, 2, 1, 2],
            hierarchy.Occurrences.Select(
                static occurrence => occurrence.Identity.Value));
        Assert.All(
            hierarchy.Occurrences,
            static occurrence => Assert.Equal(
                DependencyHierarchyOccurrenceDisposition.Expanded,
                occurrence.Disposition));
    }

    [Fact]
    public void AncestorTargetIsRetainedAsCycleBoundary()
    {
        DependencyHierarchyDocument hierarchy =
            DependencyHierarchyDocument.Create(
                Graph(
                    [new DependencyGraphRootOccurrence(1, 0)],
                    ["Root", "A", "B"],
                    [
                        Edge(0, 0, 1, [1]),
                        Edge(1, 1, 2, [1]),
                        Edge(2, 2, 1, [1]),
                    ]));

        DependencyHierarchyOccurrence cycle =
            Assert.Single(
                hierarchy.Occurrences,
                static occurrence =>
                    occurrence.Disposition
                    == DependencyHierarchyOccurrenceDisposition.Cycle);
        Assert.Equal(2, cycle.IncomingEdgeId);
        Assert.Equal(1, cycle.TargetNodeId);
        Assert.Equal(
            new DependencyHierarchyOccurrenceIdentity(
                new DependencyRootOccurrenceIdentity(1),
                2),
            cycle.ParentIdentity);
        Assert.Equal(3, cycle.Depth);
    }

    [Fact]
    public void RootWithoutEdgesRemainsExplicit()
    {
        DependencyHierarchyDocument hierarchy =
            DependencyHierarchyDocument.Create(
                Graph(
                    [new DependencyGraphRootOccurrence(12, 0)],
                    ["Root"],
                    []));

        DependencyHierarchyRootOccurrence root =
            Assert.Single(hierarchy.Roots);
        Assert.Equal(
            new DependencyHierarchyOccurrenceIdentity(
                new DependencyRootOccurrenceIdentity(12),
                0),
            root.Identity);
        Assert.Equal(0, root.RootPosition);
        Assert.Equal(0, root.NodeId);
        Assert.Equal(0, root.Depth);
        Assert.Empty(hierarchy.Occurrences);
    }

    [Fact]
    public void ProjectionOrderUsesRootAndRelationshipArrayOrder()
    {
        DependencyGraphDocument graph = Graph(
            [new DependencyGraphRootOccurrence(9, 0)],
            ["Root", "A", "B", "A Leaf", "B Leaf"],
            [
                Edge(0, 0, 2, [9]),
                Edge(1, 1, 3, [9]),
                Edge(2, 0, 1, [9]),
                Edge(3, 2, 4, [9]),
            ]);

        DependencyHierarchyDocument first =
            DependencyHierarchyDocument.Create(graph);
        DependencyHierarchyDocument second =
            DependencyHierarchyDocument.Create(graph);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(
            [0, 3, 2, 1],
            first.Occurrences.Select(
                static occurrence => occurrence.IncomingEdgeId));
    }

    [Fact]
    public void RootAdmittedUnreachableEdgeFailsExplicitly()
    {
        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(
                () => DependencyHierarchyDocument.Create(
                    Graph(
                        [new DependencyGraphRootOccurrence(1, 0)],
                        ["Root", "Detached", "Target"],
                        [Edge(0, 1, 2, [1])])));

        Assert.Contains(
            "admits unreachable edges: 0",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static DependencyGraphDocument Graph(
        ImmutableArray<DependencyGraphRootOccurrence> roots,
        ImmutableArray<string> labels,
        ImmutableArray<DependencyGraphEdge> edges) =>
        new(
            roots,
            [
                .. labels.Select((label, index) =>
                    new DependencyGraphNode(
                        index,
                        new DependencyGraphNodeIdentity.Package(
                            label,
                            "1.0.0"),
                        new InertString(TextPolicy.Field, label))),
            ],
            edges,
            [],
            []);

    private static DependencyGraphEdge Edge(
        int id,
        int source,
        int target,
        ImmutableArray<int> rootOccurrences) =>
        new(
            id,
            source,
            target,
            "package-dependency",
            rootOccurrences,
            MinimumDepth: 1,
            DependencyGraphResolutionState.Resolved,
            EvidenceIdentity: null);
}
