using System.Collections.Immutable;

using ProjectionDocument = Inspector.Graph.GraphDocument<
    Inspector.Graph.Tests.Subject,
    Inspector.Graph.Tests.Relationship,
    Inspector.Graph.Tests.Receipt,
    Inspector.Graph.Tests.Characteristic,
    Inspector.Graph.Tests.Limit,
    Inspector.Graph.Tests.Failure>;

namespace Inspector.Graph.Tests;

public sealed class GraphGroupProjectionTests
{
    static readonly Relationship Call = new("call");
    static readonly Relationship Reference = new("reference");

    [Fact]
    public void
        GroupProjection_RequiresOneAdmittedAssignmentPerCanonicalNode()
    {
        ProjectionDocument document = Document(
            nodeGroups: [[1], [2]],
            groupParents: [null, 0, 0],
            edges: [],
            occurrences: []);
        ProjectionDocument otherDocument = Document(
            nodeGroups: [[0], [0]],
            groupParents: [null],
            edges: [],
            occurrences: []);

        GraphGroupProjectionResult<Relationship> result =
            Project(
                document,
                [new(1, 0), new(0, 1)],
                []);

        Assert.Equal([0, 1], result.Nodes.Select(node => node.SourceGroupId));
        Assert.Equal([1], result.Nodes[0].SourceNodeIds);
        Assert.Equal([0], result.Nodes[1].SourceNodeIds);
        Assert.Same(document.Identity, result.Receipt.SourceDocument);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            result.Completion);

        Assert.Throws<ArgumentException>(() =>
            Project(document, [new(0, 1)], []));
        Assert.Throws<ArgumentException>(() =>
            new GraphGroupProjectionPlan<Relationship>(
                document.Identity,
                [new(0, 1), new(0, 0)],
                []));
        Assert.Throws<ArgumentException>(() =>
            Project(document, [new(0, 2), new(1, 2)], []));
        Assert.Throws<ArgumentException>(() =>
            Project(document, [new(0, 1), new(42, 0)], []));
        Assert.Throws<ArgumentException>(() =>
            Project(document, [new(0, 1), new(1, 42)], []));
        Assert.Throws<ArgumentException>(() =>
            GraphDocumentExecution.GroupProjection(
                document,
                new GraphGroupProjectionPlan<Relationship>(
                    otherDocument.Identity,
                    [new(0, 0), new(1, 0)],
                    [])));
        Assert.Throws<ArgumentException>(() =>
            new GraphGroupProjectionPlan<Relationship>(
                document.Identity,
                default(ImmutableArray<GraphGroupAssignment>),
                []));
        Assert.Throws<ArgumentException>(() =>
            new GraphGroupProjectionPlan<Relationship>(
                document.Identity,
                [default],
                []));

        ProjectionDocument empty = Document([], [], [], []);
        GraphGroupProjectionResult<Relationship> emptyResult =
            Project(empty, [], []);
        Assert.Empty(emptyResult.Nodes);
        Assert.Empty(emptyResult.Edges);
        Assert.Empty(emptyResult.WithinGroupContributions);
    }

    [Fact]
    public void
        GroupProjection_PartitionsEverySelectedSourceEdgeExactlyOnce()
    {
        ProjectionDocument document = Document(
            nodeGroups: [[0], [0], [1]],
            groupParents: [null, null],
            edges:
            [
                (0, 1, Call, new[] { 0 }),
                (0, 2, Call, new[] { 1 }),
                (1, 2, Call, new[] { 2 }),
                (2, 2, Call, new[] { 3 }),
            ],
            occurrences:
            [
                (Call, 0, 1),
                (Call, 0, 2),
                (Call, 1, 2),
                (Call, 2, 2),
            ]);

        GraphGroupProjectionResult<Relationship> result =
            Project(
                document,
                [new(0, 0), new(1, 0), new(2, 1)],
                [Call]);

        GraphProjectedEdge<Relationship> projected =
            Assert.Single(result.Edges);
        Assert.Equal(0, projected.Id);
        Assert.Equal(0, projected.FromProjectedNodeId);
        Assert.Equal(1, projected.ToProjectedNodeId);
        Assert.Equal([1, 2], projected.SourceEdgeIds);
        Assert.Equal([1, 2], projected.SourceOccurrenceIds);
        Assert.Null(projected.Explanation);

        Assert.Equal(2, result.WithinGroupContributions.Length);
        Assert.Equal(
            [0],
            result.WithinGroupContributions[0].SourceEdgeIds);
        Assert.Equal(
            [3],
            result.WithinGroupContributions[1].SourceEdgeIds);
        int[] partition =
        [
            .. result.Edges.SelectMany(edge => edge.SourceEdgeIds),
            .. result.WithinGroupContributions.SelectMany(
                row => row.SourceEdgeIds),
        ];
        Assert.Equal([0, 1, 2, 3], partition.Order());
        Assert.Equal(4, partition.Distinct().Count());

        Assert.Equal(3, result.Receipt.CanonicalNodesExamined);
        Assert.Equal(3, result.Receipt.NodeAssignmentsExamined);
        Assert.Equal(2, result.Receipt.SourceGroupsAdmitted);
        Assert.Equal(4, result.Receipt.CanonicalEdgesExamined);
        Assert.Equal(4, result.Receipt.SelectedSourceEdgesAdmitted);
        Assert.Equal(2, result.Receipt.CrossGroupSourceEdgesAdmitted);
        Assert.Equal(2, result.Receipt.WithinGroupSourceEdgesAdmitted);
        Assert.Equal(4, result.Receipt.DistinctSelectedOccurrences);
        Assert.Equal(1, result.Receipt.ProjectedEdgesIssued);
        Assert.Equal(2, result.Receipt.WithinGroupRowsIssued);
        Assert.Equal(0, result.Receipt.ContributorRankingsRequested);
        Assert.Equal(0, result.Receipt.ContributorRankingsIssued);
        Assert.True(result.Receipt.TerminalSettled);
    }

    [Fact]
    public void GroupProjection_UsesDocumentRelationshipEquality()
    {
        var comparer = new RelationshipHashComparer(
            StringComparer.OrdinalIgnoreCase,
            hashCode: 17);
        ProjectionDocument document = Document(
            nodeGroups: [[0], [0], [1]],
            groupParents: [null, null],
            edges:
            [
                (0, 2, new("CALL"), Array.Empty<int>()),
                (1, 2, new("call"), Array.Empty<int>()),
                (0, 2, Reference, Array.Empty<int>()),
            ],
            occurrences: [],
            relationshipComparer: comparer);

        GraphGroupProjectionResult<Relationship> result =
            Project(
                document,
                [new(0, 0), new(1, 0), new(2, 1)],
                [new("Call"), Reference]);

        Assert.Equal(2, result.Edges.Length);
        Assert.Equal([0, 1], result.Edges[0].SourceEdgeIds);
        Assert.Equal("CALL", result.Edges[0].Relationship.Name);
        Assert.Equal([2], result.Edges[1].SourceEdgeIds);
        Assert.Equal("reference", result.Edges[1].Relationship.Name);
    }

    [Fact]
    public void
        GroupProjection_RetainsDistinctCanonicalOccurrences()
    {
        ProjectionDocument document = Document(
            nodeGroups: [[0], [0], [1]],
            groupParents: [null, null],
            edges:
            [
                (0, 2, Call, new[] { 0 }),
                (1, 2, Call, new[] { 0, 1 }),
            ],
            occurrences:
            [
                (Call, 0, 2),
                (Call, 1, 2),
            ]);

        GraphGroupProjectionResult<Relationship> result =
            Project(
                document,
                [new(0, 0), new(1, 0), new(2, 1)],
                [Call]);

        GraphProjectedEdge<Relationship> edge =
            Assert.Single(result.Edges);
        Assert.Equal([0, 1], edge.SourceOccurrenceIds);
        Assert.Equal(2, result.Receipt.DistinctSelectedOccurrences);
    }

    [Fact]
    public void GroupProjection_RetainsIsolatedAssignedGroups()
    {
        ProjectionDocument document = Document(
            nodeGroups: [[2], [0], [1]],
            groupParents: [null, null, null],
            edges:
            [
                (0, 1, Call, Array.Empty<int>()),
            ],
            occurrences: []);

        GraphGroupProjectionResult<Relationship> result =
            Project(
                document,
                [new(2, 1), new(0, 2), new(1, 0)],
                []);

        Assert.Equal([0, 1, 2], result.Nodes.Select(
            node => node.SourceGroupId));
        Assert.Equal([1], result.Nodes[0].SourceNodeIds);
        Assert.Equal([2], result.Nodes[1].SourceNodeIds);
        Assert.Equal([0], result.Nodes[2].SourceNodeIds);
        Assert.Empty(result.Edges);
        Assert.Empty(result.WithinGroupContributions);
        Assert.Equal(0, result.Receipt.CanonicalEdgesExamined);
        Assert.Equal(3, result.Receipt.SourceGroupsAdmitted);
    }

    [Fact]
    public void
        GroupProjection_OrdersOnlyByCanonicalStructuralIdentity()
    {
        GraphGroupProjectionResult<Relationship> first =
            OrderedProjection(hashCode: 0, reverseInputs: false);
        GraphGroupProjectionResult<Relationship> second =
            OrderedProjection(hashCode: 73, reverseInputs: true);

        Assert.Equal(StructuralRows(first), StructuralRows(second));
        Assert.Equal(
            [
                "N0:G0:1",
                "N1:G1:0,2",
                "N2:G2:3",
                "E0:0>1:1,3",
                "E1:1>2:0",
                "E2:1>2:2",
            ],
            StructuralRows(first));
    }

    [Theory]
    [InlineData(0, 0, 3, 0, 5)]
    [InlineData(1, 1, 2, 3, 2)]
    [InlineData(3, 3, 0, 5, 0)]
    [InlineData(8, 3, 0, 5, 0)]
    public void
        GroupProjection_ExplainsBoundedContributorsWithExactRemainders(
        int maximum,
        int expectedRetained,
        int expectedOmitted,
        int expectedCoveredOccurrences,
        int expectedOmittedOnlyOccurrences)
    {
        ProjectionDocument document = Document(
            nodeGroups: [[0], [0], [0], [1], [1], [1]],
            groupParents: [null, null],
            edges:
            [
                (0, 3, Call, new[] { 0, 1, 2 }),
                (1, 4, Call, new[] { 2, 3 }),
                (2, 5, Call, new[] { 4 }),
            ],
            occurrences:
            [
                (Call, 0, 3),
                (Call, 0, 3),
                (Call, 0, 3),
                (Call, 1, 4),
                (Call, 2, 5),
            ]);

        GraphGroupProjectionResult<Relationship> result =
            Project(
                document,
                [
                    new(0, 0),
                    new(1, 0),
                    new(2, 0),
                    new(3, 1),
                    new(4, 1),
                    new(5, 1),
                ],
                [Call],
                maximum);

        GraphProjectedEdgeExplanation explanation =
            Assert.IsType<GraphProjectedEdgeExplanation>(
                Assert.Single(result.Edges).Explanation);
        Assert.Equal(3, explanation.TotalSourceEdgeContributorCount);
        Assert.Equal(
            expectedRetained,
            explanation.RetainedSourceEdgeContributorCount);
        Assert.Equal(
            expectedOmitted,
            explanation.OmittedSourceEdgeContributorCount);
        Assert.Equal(5, explanation.TotalDistinctOccurrenceCount);
        Assert.Equal(
            expectedCoveredOccurrences,
            explanation.RetainedCoveredDistinctOccurrenceCount);
        Assert.Equal(
            expectedOmittedOnlyOccurrences,
            explanation.OmittedOnlyDistinctOccurrenceCount);
        Assert.Equal(
            1,
            result.Receipt.ContributorRankingsRequested);
        Assert.Equal(
            1,
            result.Receipt.ContributorRankingsIssued);
        if (maximum > 0)
        {
            Assert.Equal(
                new[]
                {
                    new GraphProjectedEdgeContributor(0, 3),
                    new GraphProjectedEdgeContributor(1, 2),
                    new GraphProjectedEdgeContributor(2, 1),
                }.Take(expectedRetained),
                explanation.RetainedContributors);
        }
    }

    [Fact]
    public void GroupProjection_HighFanInRemainsIterativeAndBounded()
    {
        const int contributorCount = 4096;
        GraphNode<Subject>[] nodes =
        [
            .. Enumerable.Range(0, contributorCount + 1)
                .Select(id =>
                    new GraphNode<Subject>(
                        id,
                        new($"node-{id}"),
                        GraphNodeRole.Ordinary,
                        [id == contributorCount ? 1 : 0])),
        ];
        GraphOccurrence<Subject, Relationship, Receipt>[] occurrences =
        [
            .. Enumerable.Range(0, contributorCount)
                .Select(id =>
                    new GraphOccurrence<Subject, Relationship, Receipt>(
                        id,
                        Call,
                        nodes[id].Subject,
                        nodes[contributorCount].Subject,
                        new($"occurrence-{id}"),
                        [])),
        ];
        GraphEdge<Relationship>[] edges =
        [
            .. Enumerable.Range(0, contributorCount)
                .Select(id =>
                    new GraphEdge<Relationship>(
                        id,
                        id,
                        contributorCount,
                        Call,
                        [id])),
        ];
        var document = new ProjectionDocument(
            GraphDocumentScope.SessionBound,
            nodes,
            [
                new(0, new("group-0"), parentId: null),
                new(1, new("group-1"), parentId: null),
            ],
            edges,
            occurrences,
            characteristics: [],
            seeds: [],
            limits: [],
            failures: []);
        GraphGroupAssignment[] assignments =
        [
            .. Enumerable.Range(0, contributorCount)
                .Select(id => new GraphGroupAssignment(id, 0)),
            new(contributorCount, 1),
        ];

        GraphGroupProjectionResult<Relationship> result =
            Project(document, assignments, [Call], maximum: 5);

        GraphProjectedEdge<Relationship> edge =
            Assert.Single(result.Edges);
        GraphProjectedEdgeExplanation explanation =
            Assert.IsType<GraphProjectedEdgeExplanation>(edge.Explanation);
        Assert.Equal(contributorCount, edge.SourceEdgeIds.Length);
        Assert.Equal(contributorCount, edge.SourceOccurrenceIds.Length);
        Assert.Equal(5, explanation.RetainedContributors.Length);
        Assert.Equal(
            contributorCount - 5,
            explanation.OmittedSourceEdgeContributorCount);
        Assert.Equal(
            contributorCount - 5,
            explanation.OmittedOnlyDistinctOccurrenceCount);
    }

    static GraphGroupProjectionResult<Relationship> OrderedProjection(
        int hashCode,
        bool reverseInputs)
    {
        var comparer = new RelationshipHashComparer(
            StringComparer.Ordinal,
            hashCode);
        ProjectionDocument document = Document(
            nodeGroups: [[1], [0], [1], [2]],
            groupParents: [null, null, null],
            edges:
            [
                (0, 3, Call, Array.Empty<int>()),
                (1, 0, Reference, Array.Empty<int>()),
                (2, 3, Reference, Array.Empty<int>()),
                (1, 2, Reference, Array.Empty<int>()),
            ],
            occurrences: [],
            relationshipComparer: comparer);
        GraphGroupAssignment[] assignments =
        [
            new(0, 1),
            new(1, 0),
            new(2, 1),
            new(3, 2),
        ];
        Relationship[] relationships = [Call, Reference];
        if (reverseInputs)
        {
            Array.Reverse(assignments);
            Array.Reverse(relationships);
        }
        return Project(document, assignments, relationships);
    }

    static string[] StructuralRows(
        GraphGroupProjectionResult<Relationship> result) =>
    [
        .. result.Nodes.Select(node =>
            $"N{node.Id}:G{node.SourceGroupId}:"
            + string.Join(',', node.SourceNodeIds)),
        .. result.Edges.Select(edge =>
            $"E{edge.Id}:{edge.FromProjectedNodeId}>"
            + $"{edge.ToProjectedNodeId}:"
            + string.Join(',', edge.SourceEdgeIds)),
    ];

    static GraphGroupProjectionResult<Relationship> Project(
        ProjectionDocument document,
        IReadOnlyList<GraphGroupAssignment> assignments,
        IReadOnlyList<Relationship> relationships,
        int? maximum = null) =>
        GraphDocumentExecution.GroupProjection(
            document,
            new GraphGroupProjectionPlan<Relationship>(
                document.Identity,
                assignments,
                relationships,
                maximum));

    static ProjectionDocument Document(
        IReadOnlyList<int[]> nodeGroups,
        IReadOnlyList<int?> groupParents,
        IReadOnlyList<(
            int From,
            int To,
            Relationship Relationship,
            int[] OccurrenceIds)> edges,
        IReadOnlyList<(
            Relationship Relationship,
            int SourceNode,
            int TargetNode)> occurrences,
        IEqualityComparer<Relationship>? relationshipComparer = null)
    {
        GraphNode<Subject>[] nodes =
        [
            .. nodeGroups.Select((groups, id) =>
                new GraphNode<Subject>(
                    id,
                    new($"node-{id}"),
                    GraphNodeRole.Ordinary,
                    groups)),
        ];
        return new(
            GraphDocumentScope.SessionBound,
            nodes,
            [
                .. groupParents.Select((parentId, id) =>
                    new GraphGroup<Subject>(
                        id,
                        new($"group-{id}"),
                        parentId)),
            ],
            [
                .. edges.Select((edge, id) =>
                    new GraphEdge<Relationship>(
                        id,
                        edge.From,
                        edge.To,
                        edge.Relationship,
                        edge.OccurrenceIds)),
            ],
            [
                .. occurrences.Select((occurrence, id) =>
                    new GraphOccurrence<
                        Subject,
                        Relationship,
                        Receipt>(
                        id,
                        occurrence.Relationship,
                        nodes[occurrence.SourceNode].Subject,
                        nodes[occurrence.TargetNode].Subject,
                        new($"occurrence-{id}"),
                        [])),
            ],
            characteristics: [],
            seeds: [],
            limits: [],
            failures: [],
            relationshipComparer: relationshipComparer);
    }

    sealed class RelationshipHashComparer
        : IEqualityComparer<Relationship>
    {
        readonly StringComparer _nameComparer;
        readonly int _hashCode;

        internal RelationshipHashComparer(
            StringComparer nameComparer,
            int hashCode)
        {
            _nameComparer = nameComparer;
            _hashCode = hashCode;
        }

        public bool Equals(Relationship? left, Relationship? right) =>
            left is not null
            && right is not null
            && _nameComparer.Equals(left.Name, right.Name);

        public int GetHashCode(Relationship value) => _hashCode;
    }
}
