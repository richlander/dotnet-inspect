using System.Collections.Immutable;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspector.Sections.Tests;

public sealed class DependencyHierarchyProjectionTests
{
    [Fact]
    public void Write_StreamsOccurrenceTopologyInOwnerOrder()
    {
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(SharedDag());
        var sink = new RecordingSink();

        DependencyHierarchyProjection.Write(
            document,
            [
                .. document.Occurrences.Select(
                    static occurrence => occurrence.Identity),
            ],
            sink);

        Assert.Equal(
            [
                (0, "Root", true, null, false),
                (1, "A", false,
                    DependencyHierarchyOccurrenceDisposition.Expanded,
                    false),
                (2, "Shared", true,
                    DependencyHierarchyOccurrenceDisposition.Expanded,
                    false),
                (3, "Leaf", true,
                    DependencyHierarchyOccurrenceDisposition.Expanded,
                    false),
                (1, "B", true,
                    DependencyHierarchyOccurrenceDisposition.Expanded,
                    false),
                (2, "Shared", true,
                    DependencyHierarchyOccurrenceDisposition.Revisit,
                    false),
            ],
            sink.Nodes.Select(static item => (
                item.Depth,
                item.Node.Label.ToString(),
                item.Last,
                item.Node.Disposition,
                item.Node.IsFragment)));
    }

    [Fact]
    public void Write_MatchesBoundariesByPackageProjection()
    {
        DependencyGraphDocument graph = Graph(
            [
                new DependencyGraphRootOccurrence(1, 0),
                new DependencyGraphRootOccurrence(2, 0),
            ],
            ["Root", "Child"],
            [
                Edge(
                    0,
                    0,
                    1,
                    [1],
                    sourcePackageProjectionId: 0,
                    targetPackageProjectionId: 1),
                Edge(
                    1,
                    0,
                    1,
                    [2],
                    sourcePackageProjectionId: 2,
                    targetPackageProjectionId: 3),
            ],
            [
                Projection(0, 0, rootOccurrence: 1),
                Projection(1, 1),
                Projection(2, 0, rootOccurrence: 2),
                Projection(3, 1),
            ]) with
        {
            DepthBoundaries =
            [
                new DependencyGraphDepthBoundary(
                    NodeId: 1,
                    PackageProjectionId: 1,
                    MaximumDepth: 1,
                    [1],
                    DependencyGraphDepthBoundaryProducerKind.Restored),
                new DependencyGraphDepthBoundary(
                    NodeId: 1,
                    PackageProjectionId: 3,
                    MaximumDepth: 2,
                    [2],
                    DependencyGraphDepthBoundaryProducerKind.Restored),
            ],
        };
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(graph);
        var sink = new RecordingSink();

        DependencyHierarchyProjection.Write(
            document,
            [
                .. document.Occurrences.Select(
                    static occurrence => occurrence.Identity),
            ],
            sink);

        Assert.Equal(
            [[1], [2]],
            sink.Nodes
                .Where(static item => item.Depth == 1)
                .Select(static item => item.Node.BoundaryDepths));
        Assert.Equal(
            [false, true, true, true],
            sink.Nodes.Select(static item => item.Last));
    }

    [Fact]
    public void Write_SelectedDeepRowRetainsFragmentParentAndBoundary()
    {
        DependencyGraphDocument graph = SharedDag() with
        {
            DepthBoundaries =
            [
                new DependencyGraphDepthBoundary(
                    NodeId: 3,
                    PackageProjectionId: null,
                    MaximumDepth: 2,
                    [1],
                    DependencyGraphDepthBoundaryProducerKind.Restored),
            ],
        };
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(graph);
        DependencyHierarchyOccurrence leaf =
            document.Occurrences.Single(
                static occurrence => occurrence.IncomingEdgeId == 4);
        var sink = new RecordingSink();

        DependencyHierarchyProjection.Write(
            document,
            [leaf.Identity],
            sink);

        Assert.Equal(3, sink.Nodes.Count);
        Assert.Equal("Root", sink.Nodes[0].Node.Label.ToString());
        Assert.False(sink.Nodes[0].Last);
        Assert.Equal("Shared", sink.Nodes[1].Node.Label.ToString());
        Assert.True(sink.Nodes[1].Node.IsFragment);
        Assert.Equal([2], sink.Nodes[1].Node.BoundaryDepths);
        Assert.True(sink.Nodes[1].Last);
        Assert.Equal("Leaf", sink.Nodes[2].Node.Label.ToString());
        Assert.Equal(1, sink.Nodes[2].Depth);
        Assert.True(sink.Nodes[2].Last);
    }

    private static DependencyGraphDocument SharedDag() =>
        Graph(
            [new DependencyGraphRootOccurrence(1, 0)],
            ["Root", "A", "B", "Shared", "Leaf"],
            [
                Edge(0, 0, 1),
                Edge(1, 0, 2),
                Edge(2, 1, 3),
                Edge(3, 2, 3),
                Edge(4, 3, 4),
            ]);

    private static DependencyGraphDocument Graph(
        ImmutableArray<DependencyGraphRootOccurrence> roots,
        ImmutableArray<string> labels,
        ImmutableArray<DependencyGraphEdge> edges,
        ImmutableArray<DependencyGraphPackageProjection>
            packageProjections = default) =>
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
            packageProjections.IsDefault ? [] : packageProjections,
            []);

    private static DependencyGraphEdge Edge(
        int id,
        int source,
        int target,
        ImmutableArray<int>? rootOccurrences = null,
        int? sourcePackageProjectionId = null,
        int? targetPackageProjectionId = null) =>
        new(
            id,
            source,
            target,
            "package-dependency",
            rootOccurrences ?? [1],
            MinimumDepth: 1,
            DependencyGraphResolutionState.Resolved,
            EvidenceIdentity: null,
            SourcePackageProjectionId: sourcePackageProjectionId,
            TargetPackageProjectionId: targetPackageProjectionId);

    private static DependencyGraphPackageProjection Projection(
        int id,
        int nodeId,
        int? rootOccurrence = null) =>
        new(
            id,
            nodeId,
            PackageDependencyTraversalProjectionKind.CandidateAcquired,
            PackageDependencyTraversalProjectionExpansion.Expanded,
            Evidence: null,
            Candidate: null,
            RootOccurrence: rootOccurrence,
            Diagnostics: []);

    private sealed class RecordingSink(
        int depth = 0,
        List<(int Depth, DependencyHierarchyNode Node, bool Last)>? nodes =
            null)
        : IInspectionHierarchySink<DependencyHierarchyNode>
    {
        public List<(int Depth, DependencyHierarchyNode Node, bool Last)>
            Nodes { get; } = nodes ?? [];

        public void WriteNode(
            DependencyHierarchyNode node,
            bool isLastSibling,
            Action<IInspectionHierarchySink<DependencyHierarchyNode>>?
                writeChildren = null)
        {
            Nodes.Add((depth, node, isLastSibling));
            writeChildren?.Invoke(
                new RecordingSink(
                    depth + 1,
                    Nodes));
        }
    }
}
