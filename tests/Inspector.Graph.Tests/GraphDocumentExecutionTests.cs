using Inspector.Graph;

using TestDocument = Inspector.Graph.GraphDocument<
    Inspector.Graph.Tests.Subject,
    Inspector.Graph.Tests.Relationship,
    Inspector.Graph.Tests.Receipt,
    Inspector.Graph.Tests.Characteristic,
    Inspector.Graph.Tests.Limit,
    Inspector.Graph.Tests.Failure>;

namespace Inspector.Graph.Tests;

public sealed class GraphDocumentExecutionTests
{
    static readonly Relationship Call = new("call");
    static readonly Relationship Reference = new("reference");

    [Fact]
    public void EmptyNeighborhoodIsStructurallyExhausted()
    {
        TestDocument document = Document([], []);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood([], roots: []));

        Assert.Empty(result.NodeIds);
        Assert.Empty(result.EdgeIds);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            result.Completion);
        Assert.Same(document.Identity, result.Receipt.SourceDocument);
        Assert.Equal(0, result.Receipt.CanonicalNodesExamined);
        Assert.Equal(0, result.Receipt.CanonicalEdgesExamined);
        Assert.Equal(1, result.Receipt.StructuralViewsBuilt);
        Assert.True(result.Receipt.TerminalSettled);
    }

    [Fact]
    public void IsolatedRootIsRetainedAndExhausted()
    {
        TestDocument document = Document(["root"], []);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood(
                    [Call],
                    GraphTraversalDirection.Both,
                    maxDepth: 0,
                    roots: [0]));

        Assert.Equal([0], result.NodeIds);
        Assert.Empty(result.EdgeIds);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            result.Completion);
    }

    [Theory]
    [InlineData(GraphTraversalDirection.Outgoing, new[] { 1 })]
    [InlineData(GraphTraversalDirection.Incoming, new[] { 0 })]
    [InlineData(GraphTraversalDirection.Both, new[] { 0, 1 })]
    public void NeighborhoodHonorsDirectionAndRelationshipSelection(
        GraphTraversalDirection direction,
        int[] expectedEdges)
    {
        TestDocument document = Document(
            ["left", "root", "right", "ignored"],
            [
                (0, 1, Call),
                (1, 2, Call),
                (1, 3, Reference),
            ]);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood(
                    [Call],
                    direction,
                    maxDepth: 1,
                    roots: [1]));

        Assert.Equal(expectedEdges, result.EdgeIds);
        Assert.DoesNotContain(2, result.EdgeIds);
        Assert.Equal(
            direction == GraphTraversalDirection.Both ? 2 : 1,
            result.Receipt.StructuralViewsBuilt);
    }

    [Fact]
    public void ExecutionUsesDocumentRelationshipEquality()
    {
        TestDocument document = Document(
            ["root", "next"],
            [(0, 1, new Relationship("CALL"))],
            RelationshipNameComparer.OrdinalIgnoreCase);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood(
                    [new Relationship("call")],
                    roots: [0]));

        Assert.Equal([0], result.EdgeIds);
    }

    [Fact]
    public void ParallelTypedRelationshipsAreSelectedIndependently()
    {
        TestDocument document = Document(
            ["root", "next"],
            [
                (0, 1, Call),
                (0, 1, Reference),
            ]);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood([Reference], roots: [0]));

        Assert.Equal([1], result.EdgeIds);
    }

    [Fact]
    public void BidirectionalSelfLoopIsVisitedOnce()
    {
        TestDocument document = Document(
            ["root"],
            [(0, 0, Call)]);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood(
                    [Call],
                    GraphTraversalDirection.Both,
                    roots: [0]));

        Assert.Equal([0], result.EdgeIds);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            result.Completion);
    }

    [Fact]
    public void NeighborhoodDistinguishesReachedBoundFromExhaustion()
    {
        TestDocument document = Document(
            ["zero", "one", "two"],
            [
                (0, 1, Call),
                (1, 2, Call),
            ]);

        GraphNeighborhoodResult bounded =
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood([Call], maxDepth: 1, roots: [0]));
        GraphNeighborhoodResult exhausted =
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood([Call], maxDepth: 2, roots: [0]));

        Assert.Equal([0], bounded.EdgeIds);
        Assert.Equal(
            GraphStructuralCompletion.DepthBounded,
            bounded.Completion);
        Assert.Equal([0, 1], exhausted.EdgeIds);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            exhausted.Completion);
    }

    [Fact]
    public void ZeroDepthEntryReportsBoundedWithoutRetainingItsEdge()
    {
        TestDocument document = Document(
            ["seed", "next"],
            [(0, 1, Call)]);
        var plan = new GraphNeighborhoodPlan<Relationship>(
            [Call],
            GraphTraversalDirection.Outgoing,
            maxDepth: 0,
            rootNodeIds: [],
            entries: [new GraphTraversalEntry(0, 1)]);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(document, plan);

        Assert.Empty(result.NodeIds);
        Assert.Empty(result.EdgeIds);
        Assert.Equal(
            GraphStructuralCompletion.DepthBounded,
            result.Completion);
    }

    [Fact]
    public void CallerLoweredEntryStartsAfterItsAdmittedEdge()
    {
        TestDocument document = Document(
            ["owned-seed", "first", "second"],
            [
                (0, 1, Call),
                (1, 2, Call),
            ]);
        var plan = new GraphNeighborhoodPlan<Relationship>(
            [Call],
            GraphTraversalDirection.Outgoing,
            maxDepth: 2,
            rootNodeIds: [],
            entries: [new GraphTraversalEntry(0, 1)]);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(document, plan);

        Assert.Equal([0, 1, 2], result.NodeIds);
        Assert.Equal([0, 1], result.EdgeIds);
    }

    [Fact]
    public void CallerLoweredAnchorDoesNotBecomeATraversalRoot()
    {
        TestDocument document = Document(
            ["seed", "next", "unrelated"],
            [
                (0, 1, Call),
                (1, 0, Call),
                (0, 2, Call),
            ]);
        var plan = new GraphNeighborhoodPlan<Relationship>(
            [Call],
            GraphTraversalDirection.Outgoing,
            maxDepth: 3,
            rootNodeIds: [],
            entries: [new GraphTraversalEntry(0, 1)],
            anchorNodeIds: [0]);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(document, plan);

        Assert.Equal([0, 1], result.EdgeIds);
        Assert.DoesNotContain(2, result.NodeIds);
    }

    [Fact]
    public void CyclicNeighborhoodUsesIterativeVisitedState()
    {
        const int count = 20_000;
        string[] nodes =
        [
            .. Enumerable.Range(0, count).Select(
                static id => id.ToString()),
        ];
        (int From, int To, Relationship Relationship)[] edges =
        [
            .. Enumerable.Range(0, count).Select(
                id => (id, (id + 1) % count, Call)),
        ];
        TestDocument document = Document(nodes, edges);

        GraphNeighborhoodResult result =
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood(
                    [Call],
                    maxDepth: count,
                    roots: [0]));

        Assert.Equal(count, result.NodeIds.Length);
        Assert.Equal(count, result.EdgeIds.Length);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            result.Completion);
    }

    [Fact]
    public void FocusChoosesLexicographicallyFirstShortestConnector()
    {
        TestDocument document = Document(
            ["origin", "left", "right", "inside", "outside"],
            [
                (0, 1, Call),
                (0, 2, Call),
                (1, 3, Call),
                (2, 3, Call),
                (3, 4, Call),
            ]);
        GraphFocusPlan<Relationship> plan = Focus(
            [Call],
            GraphTraversalDirection.Outgoing,
            [
                new(0, GraphScopeMembership.Inside),
                new(1, GraphScopeMembership.Inside),
                new(2, GraphScopeMembership.Inside),
                new(3, GraphScopeMembership.Inside),
                new(4, GraphScopeMembership.Outside),
            ],
            origins: [0]);

        GraphFocusResult result =
            GraphDocumentExecution.Focus(document, plan);

        Assert.Equal([4], result.ExitEdgeIds);
        Assert.Equal([0, 2], result.ConnectorEdgeIds);
        Assert.Equal(
            [0, 2],
            result.ConnectorPathsByNodeId[3]);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            result.Completion);
    }

    [Fact]
    public void FocusKeepsUnknownBoundaryWithoutInventingConnector()
    {
        TestDocument document = Document(
            ["origin", "disconnected", "unknown", "outside"],
            [
                (1, 2, Call),
                (1, 3, Call),
            ]);
        GraphNodeScope[] scopes =
        [
            new(0, GraphScopeMembership.Inside),
            new(1, GraphScopeMembership.Inside),
            new(2, GraphScopeMembership.Unknown),
            new(3, GraphScopeMembership.Outside),
        ];

        GraphFocusResult seeded =
            GraphDocumentExecution.Focus(
                document,
                Focus(
                    [Call],
                    GraphTraversalDirection.Outgoing,
                    scopes,
                    origins: [0]));
        GraphFocusResult induced =
            GraphDocumentExecution.Focus(
                document,
                Focus(
                    [Call],
                    GraphTraversalDirection.Outgoing,
                    scopes,
                    origins: [],
                    retainEntireInsideScope: true));

        Assert.Equal([0], seeded.UnclassifiedEdgeIds);
        Assert.Empty(seeded.ExitEdgeIds);
        Assert.Empty(seeded.ConnectorEdgeIds);
        Assert.Equal([0], induced.UnclassifiedEdgeIds);
        Assert.Equal([1], induced.ExitEdgeIds);
        Assert.Empty(induced.ConnectorEdgeIds);
        Assert.Equal(0, induced.Receipt.StructuralViewsBuilt);
    }

    [Fact]
    public void IncomingFocusOrdersConnectorsTowardTheOrigin()
    {
        TestDocument document = Document(
            ["inside", "left", "right", "origin", "outside"],
            [
                (0, 1, Call),
                (0, 2, Call),
                (1, 3, Call),
                (2, 3, Call),
                (4, 0, Call),
            ]);
        GraphNodeScope[] scopes =
        [
            new(0, GraphScopeMembership.Inside),
            new(1, GraphScopeMembership.Inside),
            new(2, GraphScopeMembership.Inside),
            new(3, GraphScopeMembership.Inside),
            new(4, GraphScopeMembership.Outside),
        ];

        GraphFocusResult result =
            GraphDocumentExecution.Focus(
                document,
                Focus(
                    [Call],
                    GraphTraversalDirection.Incoming,
                    scopes,
                    origins: [3]));

        Assert.Equal([4], result.ExitEdgeIds);
        Assert.Equal([0, 2], result.ConnectorEdgeIds);
        Assert.Equal([0, 2], result.ConnectorPathsByNodeId[0]);
    }

    [Fact]
    public void FocusReportsActualStructuralWork()
    {
        TestDocument document = Document(
            ["origin", "inside", "outside"],
            [
                (0, 1, Call),
                (1, 2, Call),
                (0, 2, Reference),
            ]);

        GraphFocusResult result =
            GraphDocumentExecution.Focus(
                document,
                Focus(
                    [Call],
                    GraphTraversalDirection.Outgoing,
                    [
                        new(0, GraphScopeMembership.Inside),
                        new(1, GraphScopeMembership.Inside),
                        new(2, GraphScopeMembership.Outside),
                    ],
                    origins: [0]));

        Assert.Equal(6, result.Receipt.CanonicalEdgesExamined);
        Assert.Equal(3, result.Receipt.CanonicalNodesExamined);
        Assert.Equal(1, result.Receipt.SelectedEdgesIndexed);
        Assert.Equal(1, result.Receipt.StructuralViewsBuilt);
        Assert.Equal(1, result.Receipt.AdjacencyEntriesExamined);
        Assert.Equal(2, result.Receipt.NodesAdmitted);
        Assert.Same(document.Identity, result.Receipt.SourceDocument);
        Assert.True(result.Receipt.TerminalSettled);
    }

    [Fact]
    public void InvalidPlanReferencesAreRejected()
    {
        TestDocument document = Document(["only"], []);

        Assert.Throws<ArgumentException>(() =>
            GraphDocumentExecution.Neighborhood(
                document,
                Neighborhood([Call], roots: [1])));
        Assert.Throws<ArgumentException>(() =>
            GraphDocumentExecution.Focus(
                document,
                Focus(
                    [Call],
                    GraphTraversalDirection.Outgoing,
                    [new(1, GraphScopeMembership.Inside)],
                    origins: [])));
        Assert.Throws<ArgumentException>(() =>
            new GraphNeighborhoodPlan<Relationship>(
                [Call],
                GraphTraversalDirection.Outgoing,
                maxDepth: 1,
                rootNodeIds: [0],
                entries: [],
                anchorNodeIds: [0]));
        Assert.Throws<ArgumentException>(() =>
            new GraphFocusPlan<Relationship>(
                [Call],
                GraphTraversalDirection.Outgoing,
                [new(0, GraphScopeMembership.Inside)],
                originNodeIds: [0],
                GraphFocusReachability.EntireInsideScope));
    }

    static GraphNeighborhoodPlan<Relationship> Neighborhood(
        IEnumerable<Relationship> relationships,
        GraphTraversalDirection direction =
            GraphTraversalDirection.Outgoing,
        int maxDepth = 1,
        IEnumerable<int>? roots = null) =>
        new(
            relationships,
            direction,
            maxDepth,
            roots ?? [0],
            entries: []);

    static GraphFocusPlan<Relationship> Focus(
        IEnumerable<Relationship> relationships,
        GraphTraversalDirection direction,
        IEnumerable<GraphNodeScope> scopes,
        IEnumerable<int> origins,
        bool retainEntireInsideScope = false) =>
        new(
            relationships,
            direction,
            scopes,
            origins,
            retainEntireInsideScope
                ? GraphFocusReachability.EntireInsideScope
                : GraphFocusReachability.FromOrigins);

    static TestDocument Document(
        IReadOnlyList<string> nodeNames,
        IReadOnlyList<(
            int From,
            int To,
            Relationship Relationship)> edges,
        IEqualityComparer<Relationship>? relationshipComparer = null)
    {
        GraphNode<Subject>[] nodes =
        [
            .. nodeNames.Select((name, id) =>
                new GraphNode<Subject>(
                    id,
                    new(name),
                    GraphNodeRole.Ordinary,
                    [])),
        ];
        GraphEdge<Relationship>[] graphEdges =
        [
            .. edges.Select((edge, id) =>
                new GraphEdge<Relationship>(
                    id,
                    edge.From,
                    edge.To,
                    edge.Relationship,
                    [])),
        ];
        return new(
            GraphDocumentScope.SessionBound,
            nodes,
            groups: [],
            graphEdges,
            occurrences: [],
            characteristics: [],
            seeds: [],
            limits: [],
            failures: [],
            relationshipComparer: relationshipComparer);
    }
}
