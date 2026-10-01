using System.Collections.Immutable;

namespace Inspector.Graph;

public sealed class GraphComponentAnalysisPlan
{
    public GraphComponentAnalysisPlan(
        GraphDocumentIdentity sourceDocument,
        IReadOnlyList<int> sourceGroupIds)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        SourceDocument = sourceDocument;
        SourceGroupIds = GraphExecutionCollections.SnapshotDistinctIds(
            sourceGroupIds,
            nameof(sourceGroupIds));
    }

    public GraphDocumentIdentity SourceDocument { get; }
    public ImmutableArray<int> SourceGroupIds { get; }
}

public sealed class GraphStrongComponent
{
    internal GraphStrongComponent(
        int id,
        ImmutableArray<int> sourceGroupIds,
        int level)
    {
        Id = id;
        SourceGroupIds = sourceGroupIds;
        Level = level;
    }

    public int Id { get; }
    public ImmutableArray<int> SourceGroupIds { get; }
    public int Level { get; }
}

public readonly record struct GraphComponentMembership
{
    internal GraphComponentMembership(int sourceGroupId, int componentId)
    {
        SourceGroupId = sourceGroupId;
        ComponentId = componentId;
    }

    public int SourceGroupId { get; }
    public int ComponentId { get; }
}

public sealed class GraphCondensationEdge
{
    internal GraphCondensationEdge(
        int id,
        int fromComponentId,
        int toComponentId,
        ImmutableArray<int> sourceEdgeIds)
    {
        Id = id;
        FromComponentId = fromComponentId;
        ToComponentId = toComponentId;
        SourceEdgeIds = sourceEdgeIds;
    }

    public int Id { get; }
    public int FromComponentId { get; }
    public int ToComponentId { get; }
    public ImmutableArray<int> SourceEdgeIds { get; }
}

public sealed record GraphComponentAnalysisWorkReceipt
{
    internal GraphComponentAnalysisWorkReceipt(
        GraphDocumentIdentity sourceDocument,
        int projectedNodesExamined,
        int selectedSourceGroupsAdmitted,
        int projectedEdgesExamined,
        int inducedProjectedEdgesAdmitted,
        int distinctStructuralArcsIndexed,
        int stronglyConnectedComponentsIssued,
        int membershipRowsIssued,
        int intraComponentProjectedEdgesAdmitted,
        int crossComponentProjectedEdgesAdmitted,
        int condensationEdgesIssued,
        int distinctCanonicalSourceEdgeContributorsRetained,
        int componentsWithLevelsSettled,
        bool terminalSettled)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        ArgumentOutOfRangeException.ThrowIfNegative(
            projectedNodesExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(
            selectedSourceGroupsAdmitted);
        ArgumentOutOfRangeException.ThrowIfNegative(
            projectedEdgesExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(
            inducedProjectedEdgesAdmitted);
        ArgumentOutOfRangeException.ThrowIfNegative(
            distinctStructuralArcsIndexed);
        ArgumentOutOfRangeException.ThrowIfNegative(
            stronglyConnectedComponentsIssued);
        ArgumentOutOfRangeException.ThrowIfNegative(
            membershipRowsIssued);
        ArgumentOutOfRangeException.ThrowIfNegative(
            intraComponentProjectedEdgesAdmitted);
        ArgumentOutOfRangeException.ThrowIfNegative(
            crossComponentProjectedEdgesAdmitted);
        ArgumentOutOfRangeException.ThrowIfNegative(
            condensationEdgesIssued);
        ArgumentOutOfRangeException.ThrowIfNegative(
            distinctCanonicalSourceEdgeContributorsRetained);
        ArgumentOutOfRangeException.ThrowIfNegative(
            componentsWithLevelsSettled);

        SourceDocument = sourceDocument;
        ProjectedNodesExamined = projectedNodesExamined;
        SelectedSourceGroupsAdmitted = selectedSourceGroupsAdmitted;
        ProjectedEdgesExamined = projectedEdgesExamined;
        InducedProjectedEdgesAdmitted = inducedProjectedEdgesAdmitted;
        DistinctStructuralArcsIndexed = distinctStructuralArcsIndexed;
        StronglyConnectedComponentsIssued =
            stronglyConnectedComponentsIssued;
        MembershipRowsIssued = membershipRowsIssued;
        IntraComponentProjectedEdgesAdmitted =
            intraComponentProjectedEdgesAdmitted;
        CrossComponentProjectedEdgesAdmitted =
            crossComponentProjectedEdgesAdmitted;
        CondensationEdgesIssued = condensationEdgesIssued;
        DistinctCanonicalSourceEdgeContributorsRetained =
            distinctCanonicalSourceEdgeContributorsRetained;
        ComponentsWithLevelsSettled = componentsWithLevelsSettled;
        TerminalSettled = terminalSettled;
    }

    public GraphDocumentIdentity SourceDocument { get; }
    public int ProjectedNodesExamined { get; }
    public int SelectedSourceGroupsAdmitted { get; }
    public int ProjectedEdgesExamined { get; }
    public int InducedProjectedEdgesAdmitted { get; }
    public int DistinctStructuralArcsIndexed { get; }
    public int StronglyConnectedComponentsIssued { get; }
    public int MembershipRowsIssued { get; }
    public int IntraComponentProjectedEdgesAdmitted { get; }
    public int CrossComponentProjectedEdgesAdmitted { get; }
    public int CondensationEdgesIssued { get; }
    public int DistinctCanonicalSourceEdgeContributorsRetained { get; }
    public int ComponentsWithLevelsSettled { get; }
    public bool TerminalSettled { get; }
}

public sealed class GraphComponentAnalysisResult
{
    internal GraphComponentAnalysisResult(
        ImmutableArray<GraphStrongComponent> components,
        ImmutableArray<GraphComponentMembership> memberships,
        ImmutableArray<GraphCondensationEdge> condensationEdges,
        GraphComponentAnalysisWorkReceipt receipt)
    {
        Components = components;
        Memberships = memberships;
        CondensationEdges = condensationEdges;
        Receipt = receipt;
    }

    public ImmutableArray<GraphStrongComponent> Components { get; }
    public ImmutableArray<GraphComponentMembership> Memberships { get; }
    public ImmutableArray<GraphCondensationEdge> CondensationEdges { get; }
    public GraphStructuralCompletion Completion =>
        GraphStructuralCompletion.Exhausted;
    public GraphComponentAnalysisWorkReceipt Receipt { get; }
}

public static partial class GraphDocumentExecution
{
    public static GraphComponentAnalysisResult ComponentAnalysis<
        TRelationship>(
        GraphGroupProjectionResult<TRelationship> projection,
        GraphComponentAnalysisPlan plan)
        where TRelationship : notnull
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(
            projection.Receipt.SourceDocument,
            plan.SourceDocument))
        {
            throw new ArgumentException(
                "The component plan belongs to another graph document.",
                nameof(plan));
        }

        if (plan.SourceGroupIds.IsEmpty)
            return EmptyComponentAnalysis(plan.SourceDocument);

        int[] sourceGroupIds = [.. plan.SourceGroupIds];
        Array.Sort(sourceGroupIds);
        var vertexBySourceGroupId = new Dictionary<int, int>(
            sourceGroupIds.Length);
        for (var vertex = 0; vertex < sourceGroupIds.Length; vertex++)
            vertexBySourceGroupId.Add(sourceGroupIds[vertex], vertex);

        var vertexByProjectedNodeId = new Dictionary<int, int>(
            sourceGroupIds.Length);
        var admittedSourceGroups = 0;
        foreach (GraphProjectedGroupNode node in projection.Nodes)
        {
            if (!vertexBySourceGroupId.TryGetValue(
                node.SourceGroupId,
                out int vertex))
            {
                continue;
            }

            vertexByProjectedNodeId.Add(node.Id, vertex);
            admittedSourceGroups++;
        }
        if (admittedSourceGroups != sourceGroupIds.Length)
        {
            throw new ArgumentException(
                "Every selected source group must occur in the projection.",
                nameof(plan));
        }

        List<int>[] forward = CreateAdjacency(sourceGroupIds.Length);
        List<int>[] reverse = CreateAdjacency(sourceGroupIds.Length);
        var structuralArcs = new HashSet<long>();
        var inducedEdges =
            new List<GraphProjectedEdge<TRelationship>>();
        foreach (GraphProjectedEdge<TRelationship> edge in projection.Edges)
        {
            if (!vertexByProjectedNodeId.TryGetValue(
                edge.FromProjectedNodeId,
                out int fromVertex)
                || !vertexByProjectedNodeId.TryGetValue(
                    edge.ToProjectedNodeId,
                    out int toVertex))
            {
                continue;
            }

            inducedEdges.Add(edge);
            long arc = Pair(fromVertex, toVertex);
            if (!structuralArcs.Add(arc))
                continue;

            forward[fromVertex].Add(toVertex);
            reverse[toVertex].Add(fromVertex);
        }

        int[] discoveredComponentByVertex;
        List<int[]> discoveredComponents;
        (discoveredComponentByVertex, discoveredComponents) =
            DiscoverStrongComponents(forward, reverse);
        int[] componentByVertex = OrderComponents(
            discoveredComponentByVertex,
            discoveredComponents);

        var contributorsByComponentPair =
            new Dictionary<long, HashSet<int>>();
        var retainedSourceEdgeIds = new HashSet<int>();
        var intraComponentProjectedEdges = 0;
        var crossComponentProjectedEdges = 0;
        foreach (GraphProjectedEdge<TRelationship> edge in inducedEdges)
        {
            int fromVertex =
                vertexByProjectedNodeId[edge.FromProjectedNodeId];
            int toVertex =
                vertexByProjectedNodeId[edge.ToProjectedNodeId];
            int fromComponentId = componentByVertex[fromVertex];
            int toComponentId = componentByVertex[toVertex];
            if (fromComponentId == toComponentId)
            {
                intraComponentProjectedEdges++;
                continue;
            }

            crossComponentProjectedEdges++;
            long pair = Pair(fromComponentId, toComponentId);
            if (!contributorsByComponentPair.TryGetValue(
                pair,
                out HashSet<int>? sourceEdgeIds))
            {
                sourceEdgeIds = [];
                contributorsByComponentPair.Add(pair, sourceEdgeIds);
            }
            foreach (int sourceEdgeId in edge.SourceEdgeIds)
            {
                sourceEdgeIds.Add(sourceEdgeId);
                retainedSourceEdgeIds.Add(sourceEdgeId);
            }
        }

        List<long> orderedComponentPairs =
            [.. contributorsByComponentPair.Keys];
        orderedComponentPairs.Sort();
        var condensationEdges =
            ImmutableArray.CreateBuilder<GraphCondensationEdge>(
                orderedComponentPairs.Count);
        foreach (long pair in orderedComponentPairs)
        {
            int[] sourceEdgeIds =
                [.. contributorsByComponentPair[pair]];
            Array.Sort(sourceEdgeIds);
            condensationEdges.Add(
                new(
                    condensationEdges.Count,
                    PairFirst(pair),
                    PairSecond(pair),
                    [.. sourceEdgeIds]));
        }

        int[] levels = SettleLevels(
            discoveredComponents.Count,
            orderedComponentPairs);
        ImmutableArray<GraphStrongComponent> components =
            CreateComponents(
                sourceGroupIds,
                componentByVertex,
                levels);
        ImmutableArray<GraphComponentMembership> memberships =
            CreateMemberships(sourceGroupIds, componentByVertex);
        var receipt = new GraphComponentAnalysisWorkReceipt(
            plan.SourceDocument,
            projection.Nodes.Length,
            sourceGroupIds.Length,
            projection.Edges.Length,
            inducedEdges.Count,
            structuralArcs.Count,
            components.Length,
            memberships.Length,
            intraComponentProjectedEdges,
            crossComponentProjectedEdges,
            condensationEdges.Count,
            retainedSourceEdgeIds.Count,
            levels.Length,
            terminalSettled: true);
        return new(
            components,
            memberships,
            condensationEdges.MoveToImmutable(),
            receipt);
    }

    static GraphComponentAnalysisResult EmptyComponentAnalysis(
        GraphDocumentIdentity sourceDocument) =>
        new(
            [],
            [],
            [],
            new(
                sourceDocument,
                projectedNodesExamined: 0,
                selectedSourceGroupsAdmitted: 0,
                projectedEdgesExamined: 0,
                inducedProjectedEdgesAdmitted: 0,
                distinctStructuralArcsIndexed: 0,
                stronglyConnectedComponentsIssued: 0,
                membershipRowsIssued: 0,
                intraComponentProjectedEdgesAdmitted: 0,
                crossComponentProjectedEdgesAdmitted: 0,
                condensationEdgesIssued: 0,
                distinctCanonicalSourceEdgeContributorsRetained: 0,
                componentsWithLevelsSettled: 0,
                terminalSettled: true));

    static List<int>[] CreateAdjacency(int count)
    {
        var adjacency = new List<int>[count];
        for (var index = 0; index < count; index++)
            adjacency[index] = [];
        return adjacency;
    }

    static (int[] ComponentByVertex, List<int[]> Components)
        DiscoverStrongComponents(
            List<int>[] forward,
            List<int>[] reverse)
    {
        List<int> finishingOrder = FinishingOrder(forward);
        var componentByVertex = new int[forward.Length];
        Array.Fill(componentByVertex, -1);
        var components = new List<int[]>();
        var stack = new Stack<int>();
        for (var index = finishingOrder.Count - 1; index >= 0; index--)
        {
            int root = finishingOrder[index];
            if (componentByVertex[root] >= 0)
                continue;

            int componentId = components.Count;
            var members = new List<int>();
            componentByVertex[root] = componentId;
            stack.Push(root);
            while (stack.Count > 0)
            {
                int vertex = stack.Pop();
                members.Add(vertex);
                foreach (int neighbor in reverse[vertex])
                {
                    if (componentByVertex[neighbor] >= 0)
                        continue;
                    componentByVertex[neighbor] = componentId;
                    stack.Push(neighbor);
                }
            }
            int[] memberArray = [.. members];
            Array.Sort(memberArray);
            components.Add(memberArray);
        }
        return (componentByVertex, components);
    }

    static List<int> FinishingOrder(List<int>[] adjacency)
    {
        var visited = new bool[adjacency.Length];
        var finishingOrder = new List<int>(adjacency.Length);
        var stack = new Stack<DepthFirstFrame>();
        for (var root = 0; root < adjacency.Length; root++)
        {
            if (visited[root])
                continue;

            visited[root] = true;
            stack.Push(new(root, 0));
            while (stack.Count > 0)
            {
                DepthFirstFrame frame = stack.Pop();
                if (frame.NextNeighborIndex
                    == adjacency[frame.Vertex].Count)
                {
                    finishingOrder.Add(frame.Vertex);
                    continue;
                }

                int neighbor =
                    adjacency[frame.Vertex][frame.NextNeighborIndex];
                stack.Push(
                    new(
                        frame.Vertex,
                        frame.NextNeighborIndex + 1));
                if (visited[neighbor])
                    continue;
                visited[neighbor] = true;
                stack.Push(new(neighbor, 0));
            }
        }
        return finishingOrder;
    }

    static int[] OrderComponents(
        int[] discoveredComponentByVertex,
        List<int[]> discoveredComponents)
    {
        int[] orderedDiscoveredComponentIds =
            [.. Enumerable.Range(0, discoveredComponents.Count)];
        Array.Sort(
            orderedDiscoveredComponentIds,
            (left, right) =>
                discoveredComponents[left][0].CompareTo(
                    discoveredComponents[right][0]));

        var orderedComponentByDiscoveredComponent =
            new int[discoveredComponents.Count];
        for (var componentId = 0;
            componentId < orderedDiscoveredComponentIds.Length;
            componentId++)
        {
            orderedComponentByDiscoveredComponent[
                orderedDiscoveredComponentIds[componentId]] = componentId;
        }

        var componentByVertex =
            new int[discoveredComponentByVertex.Length];
        for (var vertex = 0;
            vertex < discoveredComponentByVertex.Length;
            vertex++)
        {
            componentByVertex[vertex] =
                orderedComponentByDiscoveredComponent[
                    discoveredComponentByVertex[vertex]];
        }
        return componentByVertex;
    }

    static int[] SettleLevels(
        int componentCount,
        List<long> orderedComponentPairs)
    {
        List<int>[] predecessors = CreateAdjacency(componentCount);
        var remainingOutgoingEdges = new int[componentCount];
        foreach (long pair in orderedComponentPairs)
        {
            int fromComponentId = PairFirst(pair);
            int toComponentId = PairSecond(pair);
            remainingOutgoingEdges[fromComponentId]++;
            predecessors[toComponentId].Add(fromComponentId);
        }

        var levels = new int[componentCount];
        var ready = new Queue<int>();
        for (var componentId = 0;
            componentId < remainingOutgoingEdges.Length;
            componentId++)
        {
            if (remainingOutgoingEdges[componentId] == 0)
                ready.Enqueue(componentId);
        }

        var settled = 0;
        while (ready.Count > 0)
        {
            int targetComponentId = ready.Dequeue();
            settled++;
            foreach (int predecessorId
                in predecessors[targetComponentId])
            {
                levels[predecessorId] = Math.Max(
                    levels[predecessorId],
                    levels[targetComponentId] + 1);
                remainingOutgoingEdges[predecessorId]--;
                if (remainingOutgoingEdges[predecessorId] == 0)
                    ready.Enqueue(predecessorId);
            }
        }
        if (settled != componentCount)
        {
            throw new InvalidOperationException(
                "Strong-component condensation must be acyclic.");
        }
        return levels;
    }

    static ImmutableArray<GraphStrongComponent> CreateComponents(
        int[] sourceGroupIds,
        int[] componentByVertex,
        int[] levels)
    {
        var memberIds = new List<int>[levels.Length];
        for (var componentId = 0;
            componentId < memberIds.Length;
            componentId++)
        {
            memberIds[componentId] = [];
        }
        for (var vertex = 0; vertex < sourceGroupIds.Length; vertex++)
        {
            memberIds[componentByVertex[vertex]].Add(
                sourceGroupIds[vertex]);
        }

        var components =
            ImmutableArray.CreateBuilder<GraphStrongComponent>(
                levels.Length);
        for (var componentId = 0;
            componentId < memberIds.Length;
            componentId++)
        {
            components.Add(
                new(
                    componentId,
                    [.. memberIds[componentId]],
                    levels[componentId]));
        }
        return components.MoveToImmutable();
    }

    static ImmutableArray<GraphComponentMembership> CreateMemberships(
        int[] sourceGroupIds,
        int[] componentByVertex)
    {
        var memberships =
            ImmutableArray.CreateBuilder<GraphComponentMembership>(
                sourceGroupIds.Length);
        for (var vertex = 0; vertex < sourceGroupIds.Length; vertex++)
        {
            memberships.Add(
                new(
                    sourceGroupIds[vertex],
                    componentByVertex[vertex]));
        }
        return memberships.MoveToImmutable();
    }

    static long Pair(int first, int second) =>
        ((long)first << 32) | (uint)second;

    static int PairFirst(long pair) => (int)(pair >> 32);

    static int PairSecond(long pair) => (int)pair;

    readonly record struct DepthFirstFrame(
        int Vertex,
        int NextNeighborIndex);
}
