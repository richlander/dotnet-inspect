using System.Collections.Immutable;

using ComponentDocument = Inspector.Graph.GraphDocument<
    Inspector.Graph.Tests.Subject,
    Inspector.Graph.Tests.Relationship,
    Inspector.Graph.Tests.Receipt,
    Inspector.Graph.Tests.Characteristic,
    Inspector.Graph.Tests.Limit,
    Inspector.Graph.Tests.Failure>;

namespace Inspector.Graph.Tests;

public sealed class GraphComponentAnalysisTests
{
    static readonly Relationship Call = new("call");
    static readonly Relationship Reference = new("reference");

    [Fact]
    public void ComponentAnalysis_RequiresSourceBoundAdmittedGroups()
    {
        GraphGroupProjectionResult<Relationship> projection =
            Projection(
                groupCount: 3,
                [new(0, 1, Call), new(1, 2, Call)]);
        GraphGroupProjectionResult<Relationship> otherProjection =
            Projection(groupCount: 1, []);

        Assert.Throws<ArgumentException>(() =>
            Analyze(
                projection,
                new(
                    otherProjection.Receipt.SourceDocument,
                    [0])));
        Assert.Throws<ArgumentException>(() =>
            Analyze(
                projection,
                new(
                    projection.Receipt.SourceDocument,
                    [0, 42])));
        Assert.Throws<ArgumentException>(() =>
            new GraphComponentAnalysisPlan(
                projection.Receipt.SourceDocument,
                [0, 0]));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GraphComponentAnalysisPlan(
                projection.Receipt.SourceDocument,
                [-1]));
        Assert.Throws<ArgumentException>(() =>
            new GraphComponentAnalysisPlan(
                projection.Receipt.SourceDocument,
                default(ImmutableArray<int>)));

        GraphComponentAnalysisResult empty = Analyze(
            projection,
            new(
                projection.Receipt.SourceDocument,
                []));

        Assert.Empty(empty.Components);
        Assert.Empty(empty.Memberships);
        Assert.Empty(empty.CondensationEdges);
        Assert.Equal(0, empty.Receipt.ProjectedNodesExamined);
        Assert.Equal(0, empty.Receipt.ProjectedEdgesExamined);
        Assert.True(empty.Receipt.TerminalSettled);
        Assert.Equal(
            GraphStructuralCompletion.Exhausted,
            empty.Completion);
    }

    [Fact]
    public void
        ComponentAnalysis_SparseSelectionAllocatesBySelectedTopology()
    {
        GraphGroupProjectionResult<Relationship> small =
            Projection(groupCount: 1, []);
        GraphGroupProjectionResult<Relationship> large =
            Projection(groupCount: 16384, []);
        var smallPlan = new GraphComponentAnalysisPlan(
            small.Receipt.SourceDocument,
            [0]);
        var largePlan = new GraphComponentAnalysisPlan(
            large.Receipt.SourceDocument,
            [0]);

        _ = Analyze(small, smallPlan);
        _ = Analyze(large, largePlan);
        long smallAllocation = MeasureAllocation(small, smallPlan);
        long largeAllocation = MeasureAllocation(large, largePlan);

        Assert.InRange(
            largeAllocation,
            0,
            smallAllocation + 4096);
    }

    [Fact]
    public void
        ComponentAnalysis_PartitionsEverySelectedGroupExactlyOnce()
    {
        GraphGroupProjectionResult<Relationship> projection =
            Projection(
                groupCount: 5,
                [
                    new(0, 1, Call),
                    new(1, 0, Call),
                    new(2, 3, Call),
                ]);

        GraphComponentAnalysisResult result = Analyze(
            projection,
            new(
                projection.Receipt.SourceDocument,
                [4, 3, 2, 1, 0]));

        Assert.Equal(
            ["C0:L0:0,1", "C1:L1:2", "C2:L0:3", "C3:L0:4"],
            result.Components.Select(component =>
                $"C{component.Id}:L{component.Level}:"
                + string.Join(',', component.SourceGroupIds)));
        Assert.Equal(
            ["G0:C0", "G1:C0", "G2:C1", "G3:C2", "G4:C3"],
            result.Memberships.Select(membership =>
                $"G{membership.SourceGroupId}:"
                + $"C{membership.ComponentId}"));
        Assert.Equal(
            [0, 1, 2, 3, 4],
            result.Components
                .SelectMany(component => component.SourceGroupIds)
                .Order());
        Assert.Equal(
            [0, 1, 2, 3, 4],
            result.Memberships.Select(
                membership => membership.SourceGroupId));
        Assert.Equal(5, result.Receipt.ProjectedNodesExamined);
        Assert.Equal(5, result.Receipt.SelectedSourceGroupsAdmitted);
        Assert.Equal(4, result.Receipt.StronglyConnectedComponentsIssued);
        Assert.Equal(5, result.Receipt.MembershipRowsIssued);
        Assert.Equal(4, result.Receipt.ComponentsWithLevelsSettled);
    }

    [Fact]
    public void ComponentAnalysis_DerivesMaximalStrongComponents()
    {
        ComponentEdge[] edges =
        [
            new(0, 1, Call),
            new(1, 0, Call),
            new(1, 2, Call),
            new(2, 3, Call),
            new(3, 4, Call),
            new(4, 2, Call),
        ];
        GraphGroupProjectionResult<Relationship> projection =
            Projection(groupCount: 6, edges);

        GraphComponentAnalysisResult result = Analyze(
            projection,
            new(
                projection.Receipt.SourceDocument,
                [0, 1, 2, 3, 4, 5]));

        string[] expected = MutualReachabilityComponents(6, edges);
        string[] actual =
        [
            .. result.Components.Select(component =>
                string.Join(',', component.SourceGroupIds)),
        ];

        Assert.Equal(["0,1", "2,3,4", "5"], expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void
        ComponentAnalysis_CondensesToAcyclicContributorCompleteGraph()
    {
        GraphGroupProjectionResult<Relationship> projection =
            Projection(
                groupCount: 4,
                [
                    new(0, 2, Call),
                    new(2, 0, Call),
                    new(2, 1, Call),
                    new(0, 1, Reference),
                    new(0, 1, Call),
                    new(1, 3, Call),
                ]);

        GraphComponentAnalysisResult result = Analyze(
            projection,
            new(
                projection.Receipt.SourceDocument,
                [0, 1, 2, 3]));

        Assert.Equal(
            ["C0:0,2", "C1:1", "C2:3"],
            result.Components.Select(component =>
                $"C{component.Id}:"
                + string.Join(',', component.SourceGroupIds)));
        Assert.Equal(
            ["E0:0>1:2,3,4", "E1:1>2:5"],
            result.CondensationEdges.Select(edge =>
                $"E{edge.Id}:{edge.FromComponentId}>"
                + $"{edge.ToComponentId}:"
                + string.Join(',', edge.SourceEdgeIds)));
        Assert.DoesNotContain(
            result.CondensationEdges,
            edge => edge.FromComponentId == edge.ToComponentId);
        Assert.Equal(
            result.CondensationEdges.Length,
            result.CondensationEdges
                .Select(edge =>
                    (edge.FromComponentId, edge.ToComponentId))
                .Distinct()
                .Count());
        AssertAcyclic(
            result.Components.Length,
            result.CondensationEdges);
        Assert.Equal(6, result.Receipt.InducedProjectedEdgesAdmitted);
        Assert.Equal(5, result.Receipt.DistinctStructuralArcsIndexed);
        Assert.Equal(2, result.Receipt.IntraComponentProjectedEdgesAdmitted);
        Assert.Equal(4, result.Receipt.CrossComponentProjectedEdgesAdmitted);
        Assert.Equal(4, result.Receipt
            .DistinctCanonicalSourceEdgeContributorsRetained);
    }

    [Fact]
    public void ComponentAnalysis_AssignsSinkBasedMaxDependencyLevels()
    {
        GraphGroupProjectionResult<Relationship> projection =
            Projection(
                groupCount: 7,
                [
                    new(0, 1, Call),
                    new(0, 2, Call),
                    new(1, 3, Call),
                    new(2, 4, Call),
                    new(4, 5, Call),
                ]);

        GraphComponentAnalysisResult result = Analyze(
            projection,
            new(
                projection.Receipt.SourceDocument,
                [0, 1, 2, 3, 4, 5, 6]));
        int[] levels =
        [
            .. result.Components.Select(component => component.Level),
        ];

        Assert.Equal([3, 1, 2, 0, 1, 0, 0], levels);
        foreach (GraphStrongComponent component in result.Components)
        {
            int[] targetLevels =
            [
                .. result.CondensationEdges
                    .Where(edge =>
                        edge.FromComponentId == component.Id)
                    .Select(edge =>
                        result.Components[edge.ToComponentId].Level),
            ];
            Assert.Equal(
                targetLevels.Length == 0
                    ? 0
                    : targetLevels.Max() + 1,
                component.Level);
        }
    }

    [Fact]
    public void
        ComponentAnalysis_ExcludesCrossBoundaryAndWithinGroupEvidence()
    {
        ComponentDocument document = Document(
            groupCount: 4,
            [
                new(0, 1, Call),
                new(1, 0, Call),
                new(2, 0, Call),
                new(1, 2, Call),
                new(0, 0, Call),
            ]);
        GraphGroupProjectionResult<Relationship> projection =
            Project(document, [Call]);

        GraphComponentAnalysisResult result = Analyze(
            projection,
            new(
                projection.Receipt.SourceDocument,
                [3, 1, 0]));

        Assert.Single(projection.WithinGroupContributions);
        Assert.Equal(
            ["C0:L0:0,1", "C1:L0:3"],
            result.Components.Select(component =>
                $"C{component.Id}:L{component.Level}:"
                + string.Join(',', component.SourceGroupIds)));
        Assert.Empty(result.CondensationEdges);
        Assert.Equal(4, result.Receipt.ProjectedEdgesExamined);
        Assert.Equal(2, result.Receipt.InducedProjectedEdgesAdmitted);
        Assert.Equal(2, result.Receipt.IntraComponentProjectedEdgesAdmitted);
        Assert.Equal(0, result.Receipt.CrossComponentProjectedEdgesAdmitted);
    }

    [Fact]
    public void
        ComponentAnalysis_OrdersOnlyByCanonicalStructuralIdentity()
    {
        GraphComponentAnalysisResult first =
            OrderedAnalysis(hashCode: 0, reversePlan: false);
        GraphComponentAnalysisResult second =
            OrderedAnalysis(hashCode: 73, reversePlan: true);

        Assert.Equal(StructuralRows(first), StructuralRows(second));
        Assert.Equal(
            [
                "C0:L1:0,2",
                "C1:L2:1",
                "C2:L1:3",
                "C3:L0:4",
                "M0:0",
                "M1:1",
                "M2:0",
                "M3:2",
                "M4:3",
                "E0:0>3:5",
                "E1:1>2:2,3",
                "E2:2>3:4",
            ],
            StructuralRows(first));
    }

    [Fact]
    public void
        ComponentAnalysis_DeepChainAndGiantCycleRemainIterative()
    {
        const int depth = 32768;
        const int cycleStart = depth;
        const int groupCount = depth * 2;
        ComponentEdge[] edges = new ComponentEdge[groupCount - 1];
        for (var groupId = 0; groupId < depth - 1; groupId++)
            edges[groupId] = new(groupId, groupId + 1, Call);
        for (var offset = 0; offset < depth - 1; offset++)
        {
            int groupId = cycleStart + offset;
            edges[depth - 1 + offset] =
                new(groupId, groupId + 1, Call);
        }
        edges[^1] = new(groupCount - 1, cycleStart, Call);
        GraphGroupProjectionResult<Relationship> projection =
            Projection(groupCount, edges);

        GraphComponentAnalysisResult result = Analyze(
            projection,
            new(
                projection.Receipt.SourceDocument,
                [.. Enumerable.Range(0, groupCount)]));

        Assert.Equal(depth + 1, result.Components.Length);
        Assert.Equal(depth - 1, result.Components[0].Level);
        Assert.Equal(0, result.Components[depth - 1].Level);
        Assert.Equal(
            depth,
            result.Components[^1].SourceGroupIds.Length);
        Assert.Equal(0, result.Components[^1].Level);
        Assert.Equal(groupCount, result.Memberships.Length);
        Assert.Equal(depth - 1, result.CondensationEdges.Length);
        Assert.Equal(
            depth + 1,
            result.Receipt.ComponentsWithLevelsSettled);
    }

    static GraphComponentAnalysisResult OrderedAnalysis(
        int hashCode,
        bool reversePlan)
    {
        ComponentDocument document = Document(
            groupCount: 5,
            [
                new(0, 2, Call),
                new(2, 0, Reference),
                new(1, 3, Call),
                new(1, 3, Reference),
                new(3, 4, Call),
                new(2, 4, Call),
            ],
            new RelationshipHashComparer(hashCode));
        Relationship[] relationships = [Call, Reference];
        int[] selectedGroups = [0, 1, 2, 3, 4];
        if (reversePlan)
        {
            Array.Reverse(relationships);
            Array.Reverse(selectedGroups);
        }
        GraphGroupProjectionResult<Relationship> projection =
            Project(document, relationships);
        return Analyze(
            projection,
            new(
                projection.Receipt.SourceDocument,
                selectedGroups));
    }

    static string[] StructuralRows(
        GraphComponentAnalysisResult result) =>
    [
        .. result.Components.Select(component =>
            $"C{component.Id}:L{component.Level}:"
            + string.Join(',', component.SourceGroupIds)),
        .. result.Memberships.Select(membership =>
            $"M{membership.SourceGroupId}:"
            + membership.ComponentId),
        .. result.CondensationEdges.Select(edge =>
            $"E{edge.Id}:{edge.FromComponentId}>"
            + $"{edge.ToComponentId}:"
            + string.Join(',', edge.SourceEdgeIds)),
    ];

    static GraphComponentAnalysisResult Analyze(
        GraphGroupProjectionResult<Relationship> projection,
        GraphComponentAnalysisPlan plan) =>
        GraphDocumentExecution.ComponentAnalysis(projection, plan);

    static long MeasureAllocation(
        GraphGroupProjectionResult<Relationship> projection,
        GraphComponentAnalysisPlan plan)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        GraphComponentAnalysisResult result = Analyze(projection, plan);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(result);
        return allocated;
    }

    static GraphGroupProjectionResult<Relationship> Projection(
        int groupCount,
        IReadOnlyList<ComponentEdge> edges) =>
        Project(Document(groupCount, edges), [Call, Reference]);

    static GraphGroupProjectionResult<Relationship> Project(
        ComponentDocument document,
        IReadOnlyList<Relationship> relationships) =>
        GraphDocumentExecution.GroupProjection(
            document,
            new GraphGroupProjectionPlan<Relationship>(
                document.Identity,
                [
                    .. Enumerable.Range(0, document.Nodes.Length)
                        .Select(nodeId =>
                            new GraphGroupAssignment(nodeId, nodeId)),
                ],
                relationships));

    static ComponentDocument Document(
        int groupCount,
        IReadOnlyList<ComponentEdge> edges,
        IEqualityComparer<Relationship>? relationshipComparer = null)
    {
        GraphNode<Subject>[] nodes =
        [
            .. Enumerable.Range(0, groupCount)
                .Select(id =>
                    new GraphNode<Subject>(
                        id,
                        new($"node-{id}"),
                        GraphNodeRole.Ordinary,
                        [id])),
        ];
        return new(
            GraphDocumentScope.SessionBound,
            nodes,
            [
                .. Enumerable.Range(0, groupCount)
                    .Select(id =>
                        new GraphGroup<Subject>(
                            id,
                            new($"group-{id}"),
                            parentId: null)),
            ],
            [
                .. edges.Select((edge, id) =>
                    new GraphEdge<Relationship>(
                        id,
                        edge.From,
                        edge.To,
                        edge.Relationship,
                        [])),
            ],
            occurrences: [],
            characteristics: [],
            seeds: [],
            limits: [],
            failures: [],
            relationshipComparer: relationshipComparer);
    }

    static string[] MutualReachabilityComponents(
        int groupCount,
        IReadOnlyList<ComponentEdge> edges)
    {
        var reachable = new bool[groupCount, groupCount];
        for (var source = 0; source < groupCount; source++)
        {
            var pending = new Queue<int>();
            reachable[source, source] = true;
            pending.Enqueue(source);
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                foreach (ComponentEdge edge in edges)
                {
                    if (edge.From != current
                        || reachable[source, edge.To])
                    {
                        continue;
                    }
                    reachable[source, edge.To] = true;
                    pending.Enqueue(edge.To);
                }
            }
        }

        var assigned = new bool[groupCount];
        var components = new List<string>();
        for (var source = 0; source < groupCount; source++)
        {
            if (assigned[source])
                continue;
            int[] members =
            [
                .. Enumerable.Range(0, groupCount).Where(candidate =>
                    reachable[source, candidate]
                    && reachable[candidate, source]),
            ];
            foreach (int member in members)
                assigned[member] = true;
            components.Add(string.Join(',', members));
        }
        return [.. components];
    }

    static void AssertAcyclic(
        int componentCount,
        ImmutableArray<GraphCondensationEdge> edges)
    {
        var indegree = new int[componentCount];
        List<int>[] outgoing =
        [
            .. Enumerable.Range(0, componentCount)
                .Select(_ => new List<int>()),
        ];
        foreach (GraphCondensationEdge edge in edges)
        {
            outgoing[edge.FromComponentId].Add(edge.ToComponentId);
            indegree[edge.ToComponentId]++;
        }
        var ready = new Queue<int>(
            Enumerable.Range(0, componentCount).Where(
                componentId => indegree[componentId] == 0));
        var visited = 0;
        while (ready.Count > 0)
        {
            int componentId = ready.Dequeue();
            visited++;
            foreach (int targetId in outgoing[componentId])
            {
                indegree[targetId]--;
                if (indegree[targetId] == 0)
                    ready.Enqueue(targetId);
            }
        }
        Assert.Equal(componentCount, visited);
    }

    readonly record struct ComponentEdge(
        int From,
        int To,
        Relationship Relationship);

    sealed class RelationshipHashComparer
        : IEqualityComparer<Relationship>
    {
        readonly int _hashCode;

        internal RelationshipHashComparer(int hashCode)
        {
            _hashCode = hashCode;
        }

        public bool Equals(Relationship? left, Relationship? right) =>
            left is not null
            && right is not null
            && StringComparer.Ordinal.Equals(left.Name, right.Name);

        public int GetHashCode(Relationship value) => _hashCode;
    }
}
