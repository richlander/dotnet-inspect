using System.Collections.Immutable;

namespace Inspector.Graph;

public enum GraphTraversalDirection
{
    Outgoing,
    Incoming,
    Both,
}

public enum GraphScopeMembership
{
    Unknown,
    Inside,
    Outside,
}

public enum GraphFocusReachability
{
    FromOrigins,
    EntireInsideScope,
}

public enum GraphStructuralCompletion
{
    Exhausted,
    DepthBounded,
}

public readonly record struct GraphTraversalEntry
{
    public GraphTraversalEntry(int edgeId, int nodeId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(edgeId);
        ArgumentOutOfRangeException.ThrowIfNegative(nodeId);
        EdgeId = edgeId;
        NodeId = nodeId;
    }

    public int EdgeId { get; }
    public int NodeId { get; }
}

public readonly record struct GraphNodeScope
{
    public GraphNodeScope(int nodeId, GraphScopeMembership membership)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeId);
        GraphCollections.RequireDefined(membership, nameof(membership));
        NodeId = nodeId;
        Membership = membership;
    }

    public int NodeId { get; }
    public GraphScopeMembership Membership { get; }
}

public sealed class GraphNeighborhoodPlan<TRelationship>
    where TRelationship : notnull
{
    public GraphNeighborhoodPlan(
        IEnumerable<TRelationship> relationships,
        GraphTraversalDirection direction,
        int maxDepth,
        IEnumerable<int> rootNodeIds,
        IEnumerable<GraphTraversalEntry> entries,
        IEnumerable<int>? anchorNodeIds = null)
    {
        GraphCollections.RequireDefined(direction, nameof(direction));
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        Relationships = GraphExecutionCollections.SnapshotValues(
            relationships,
            nameof(relationships));
        RootNodeIds = GraphExecutionCollections.SnapshotDistinctIds(
            rootNodeIds,
            nameof(rootNodeIds));
        Entries = GraphExecutionCollections.SnapshotDistinct(
            entries,
            EqualityComparer<GraphTraversalEntry>.Default,
            nameof(entries));
        AnchorNodeIds = GraphExecutionCollections.SnapshotDistinctIds(
            anchorNodeIds ?? [],
            nameof(anchorNodeIds));
        if (RootNodeIds.Intersect(AnchorNodeIds).Any())
        {
            throw new ArgumentException(
                "Root and anchor node ids must be disjoint.",
                nameof(anchorNodeIds));
        }
        Direction = direction;
        MaxDepth = maxDepth;
    }

    public ImmutableArray<TRelationship> Relationships { get; }
    public GraphTraversalDirection Direction { get; }
    public int MaxDepth { get; }
    public ImmutableArray<int> RootNodeIds { get; }
    public ImmutableArray<GraphTraversalEntry> Entries { get; }
    public ImmutableArray<int> AnchorNodeIds { get; }
}

public sealed class GraphFocusPlan<TRelationship>
    where TRelationship : notnull
{
    public GraphFocusPlan(
        IEnumerable<TRelationship> relationships,
        GraphTraversalDirection direction,
        IEnumerable<GraphNodeScope> nodeScopes,
        IEnumerable<int> originNodeIds,
        GraphFocusReachability reachability)
    {
        GraphCollections.RequireDefined(direction, nameof(direction));
        GraphCollections.RequireDefined(reachability, nameof(reachability));
        Relationships = GraphExecutionCollections.SnapshotValues(
            relationships,
            nameof(relationships));
        NodeScopes = GraphExecutionCollections.SnapshotDistinctNodeScopes(
            nodeScopes,
            nameof(nodeScopes));
        OriginNodeIds = GraphExecutionCollections.SnapshotDistinctIds(
            originNodeIds,
            nameof(originNodeIds));
        if (reachability == GraphFocusReachability.EntireInsideScope
            && !OriginNodeIds.IsEmpty)
        {
            throw new ArgumentException(
                "Entire-scope focus does not use origins.",
                nameof(originNodeIds));
        }
        Direction = direction;
        Reachability = reachability;
    }

    public ImmutableArray<TRelationship> Relationships { get; }
    public GraphTraversalDirection Direction { get; }
    public ImmutableArray<GraphNodeScope> NodeScopes { get; }
    public ImmutableArray<int> OriginNodeIds { get; }
    public GraphFocusReachability Reachability { get; }
}

public sealed record GraphExecutionWorkReceipt
{
    internal GraphExecutionWorkReceipt(
        GraphDocumentIdentity sourceDocument,
        int canonicalNodesExamined,
        int canonicalEdgesExamined,
        int selectedEdgesIndexed,
        int structuralViewsBuilt,
        int adjacencyEntriesExamined,
        int nodesAdmitted,
        bool terminalSettled)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        ArgumentOutOfRangeException.ThrowIfNegative(
            canonicalNodesExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(
            canonicalEdgesExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(
            selectedEdgesIndexed);
        ArgumentOutOfRangeException.ThrowIfNegative(
            structuralViewsBuilt);
        ArgumentOutOfRangeException.ThrowIfNegative(
            adjacencyEntriesExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(nodesAdmitted);
        SourceDocument = sourceDocument;
        CanonicalNodesExamined = canonicalNodesExamined;
        CanonicalEdgesExamined = canonicalEdgesExamined;
        SelectedEdgesIndexed = selectedEdgesIndexed;
        StructuralViewsBuilt = structuralViewsBuilt;
        AdjacencyEntriesExamined = adjacencyEntriesExamined;
        NodesAdmitted = nodesAdmitted;
        TerminalSettled = terminalSettled;
    }

    public GraphDocumentIdentity SourceDocument { get; }
    public int CanonicalNodesExamined { get; }
    public int CanonicalEdgesExamined { get; }
    public int SelectedEdgesIndexed { get; }
    public int StructuralViewsBuilt { get; }
    public int AdjacencyEntriesExamined { get; }
    public int NodesAdmitted { get; }
    public bool TerminalSettled { get; }
}

public sealed class GraphNeighborhoodResult
{
    internal GraphNeighborhoodResult(
        ImmutableArray<int> nodeIds,
        ImmutableArray<int> edgeIds,
        GraphStructuralCompletion completion,
        GraphExecutionWorkReceipt receipt)
    {
        NodeIds = nodeIds;
        EdgeIds = edgeIds;
        Completion = completion;
        Receipt = receipt;
    }

    public ImmutableArray<int> NodeIds { get; }
    public ImmutableArray<int> EdgeIds { get; }
    public GraphStructuralCompletion Completion { get; }
    public GraphExecutionWorkReceipt Receipt { get; }
}

public sealed class GraphFocusResult
{
    internal GraphFocusResult(
        ImmutableArray<int> exitEdgeIds,
        ImmutableArray<int> connectorEdgeIds,
        ImmutableArray<int> unclassifiedEdgeIds,
        ImmutableDictionary<int, ImmutableArray<int>> connectorPathsByNodeId,
        GraphExecutionWorkReceipt receipt)
    {
        ExitEdgeIds = exitEdgeIds;
        ConnectorEdgeIds = connectorEdgeIds;
        UnclassifiedEdgeIds = unclassifiedEdgeIds;
        ConnectorPathsByNodeId = connectorPathsByNodeId;
        Receipt = receipt;
    }

    public ImmutableArray<int> ExitEdgeIds { get; }
    public ImmutableArray<int> ConnectorEdgeIds { get; }
    public ImmutableArray<int> UnclassifiedEdgeIds { get; }
    public ImmutableDictionary<int, ImmutableArray<int>>
        ConnectorPathsByNodeId { get; }
    public GraphStructuralCompletion Completion =>
        GraphStructuralCompletion.Exhausted;
    public GraphExecutionWorkReceipt Receipt { get; }
}

public static class GraphDocumentExecution
{
    public static GraphNeighborhoodResult Neighborhood<
        TSubject,
        TRelationship,
        TOccurrenceEvidence,
        TCharacteristic,
        TLimit,
        TFailure>(
        GraphDocument<
            TSubject,
            TRelationship,
            TOccurrenceEvidence,
            TCharacteristic,
            TLimit,
            TFailure> document,
        GraphNeighborhoodPlan<TRelationship> plan)
        where TSubject : notnull
        where TRelationship : notnull
        where TOccurrenceEvidence : notnull
        where TCharacteristic : notnull
        where TLimit : notnull
        where TFailure : notnull
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);

        var work = new GraphExecutionWork(document.Identity);
        GraphEdgeIndex<TRelationship> index =
            GraphEdgeIndex<TRelationship>.Create(
                document.Edges,
                document.Nodes.Length,
                plan.Relationships,
                document.RelationshipComparer,
                work,
                includeOutgoing:
                    plan.Direction is GraphTraversalDirection.Outgoing
                        or GraphTraversalDirection.Both,
                includeIncoming:
                    plan.Direction is GraphTraversalDirection.Incoming
                        or GraphTraversalDirection.Both);
        ValidateNodeIds(
            plan.RootNodeIds,
            document.Nodes.Length,
            nameof(plan));
        ValidateNodeIds(
            plan.AnchorNodeIds,
            document.Nodes.Length,
            nameof(plan));

        var retainedNodes = new HashSet<int>();
        var retainedEdges = new HashSet<int>();
        var queue = new Queue<(int NodeId, int Depth)>();
        var nodeDepths = new Dictionary<int, int>();
        bool depthBounded =
            plan.MaxDepth == 0 && !plan.Entries.IsEmpty;
        foreach (int nodeId in plan.AnchorNodeIds)
        {
            retainedNodes.Add(nodeId);
            nodeDepths.Add(nodeId, 0);
        }
        foreach (int nodeId in plan.RootNodeIds)
        {
            if (retainedNodes.Add(nodeId))
                Enqueue(nodeId, 0, nodeDepths, queue);
        }
        foreach (GraphTraversalEntry entry in plan.Entries)
        {
            if ((uint)entry.EdgeId >= (uint)document.Edges.Length)
                throw new ArgumentException(
                    "A traversal entry edge is not present in the document.",
                    nameof(plan));
            if ((uint)entry.NodeId >= (uint)document.Nodes.Length)
                throw new ArgumentException(
                    "A traversal entry node is not present in the document.",
                    nameof(plan));
            GraphEdge<TRelationship> edge =
                document.Edges[entry.EdgeId];
            if (!index.Contains(entry.EdgeId))
            {
                throw new ArgumentException(
                    "A traversal entry edge does not have a selected relationship.",
                    nameof(plan));
            }
            if (entry.NodeId != edge.FromNodeId
                && entry.NodeId != edge.ToNodeId)
            {
                throw new ArgumentException(
                    "A traversal entry node must be an endpoint of its edge.",
                    nameof(plan));
            }
            if (plan.MaxDepth == 0)
                continue;

            retainedEdges.Add(entry.EdgeId);
            retainedNodes.Add(edge.FromNodeId);
            retainedNodes.Add(edge.ToNodeId);
            work.ExamineNode(edge.FromNodeId);
            work.ExamineNode(edge.ToNodeId);
            Enqueue(entry.NodeId, 1, nodeDepths, queue);
        }

        while (queue.TryDequeue(out var item))
        {
            work.ExamineNode(item.NodeId);
            if (item.Depth >= plan.MaxDepth)
            {
                depthBounded |= HasUnretainedAdjacentEdge(
                    index,
                    item.NodeId,
                    plan.Direction,
                    retainedEdges,
                    work);
                continue;
            }

            foreach (GraphTraversalStep step in Adjacent(
                         index,
                         item.NodeId,
                         plan.Direction,
                         work))
            {
                retainedEdges.Add(step.EdgeId);
                GraphEdge<TRelationship> edge =
                    document.Edges[step.EdgeId];
                retainedNodes.Add(edge.FromNodeId);
                retainedNodes.Add(edge.ToNodeId);
                work.ExamineNode(edge.FromNodeId);
                work.ExamineNode(edge.ToNodeId);
                Enqueue(
                    step.NodeId,
                    item.Depth + 1,
                    nodeDepths,
                    queue);
            }
        }

        work.NodesAdmitted = retainedNodes.Count;
        return new GraphNeighborhoodResult(
            [.. retainedNodes.Order()],
            [.. retainedEdges.Order()],
            depthBounded
                ? GraphStructuralCompletion.DepthBounded
                : GraphStructuralCompletion.Exhausted,
            work.CreateReceipt());
    }

    public static GraphFocusResult Focus<
        TSubject,
        TRelationship,
        TOccurrenceEvidence,
        TCharacteristic,
        TLimit,
        TFailure>(
        GraphDocument<
            TSubject,
            TRelationship,
            TOccurrenceEvidence,
            TCharacteristic,
            TLimit,
            TFailure> document,
        GraphFocusPlan<TRelationship> plan)
        where TSubject : notnull
        where TRelationship : notnull
        where TOccurrenceEvidence : notnull
        where TCharacteristic : notnull
        where TLimit : notnull
        where TFailure : notnull
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);

        GraphScopeMembership[] memberships =
            Enumerable.Repeat(
                    GraphScopeMembership.Unknown,
                    document.Nodes.Length)
                .ToArray();
        foreach (GraphNodeScope scope in plan.NodeScopes)
        {
            if ((uint)scope.NodeId >= (uint)memberships.Length)
            {
                throw new ArgumentException(
                    "A node scope is not present in the document.",
                    nameof(plan));
            }
            memberships[scope.NodeId] = scope.Membership;
        }
        ValidateNodeIds(
            plan.OriginNodeIds,
            document.Nodes.Length,
            nameof(plan));
        foreach (int origin in plan.OriginNodeIds)
        {
            if (memberships[origin] != GraphScopeMembership.Inside)
            {
                throw new ArgumentException(
                    "Every focus origin must be inside the supplied scope.",
                    nameof(plan));
            }
        }

        var work = new GraphExecutionWork(document.Identity);
        bool buildOutgoing =
            plan.Reachability == GraphFocusReachability.FromOrigins
            && plan.Direction is GraphTraversalDirection.Outgoing
                or GraphTraversalDirection.Both;
        bool buildIncoming =
            plan.Reachability == GraphFocusReachability.FromOrigins
            && plan.Direction is GraphTraversalDirection.Incoming
                or GraphTraversalDirection.Both;
        GraphEdgeIndex<TRelationship> index =
            GraphEdgeIndex<TRelationship>.Create(
                document.Edges,
                document.Nodes.Length,
                plan.Relationships,
                document.RelationshipComparer,
                work,
                buildOutgoing,
                buildIncoming,
                edge =>
                    memberships[edge.FromNodeId]
                        == GraphScopeMembership.Inside
                    && memberships[edge.ToNodeId]
                        == GraphScopeMembership.Inside);
        IReadOnlyDictionary<int, ImmutableArray<int>> outgoingPaths =
            plan.Reachability
                == GraphFocusReachability.EntireInsideScope
            || plan.Direction == GraphTraversalDirection.Incoming
                ? ImmutableDictionary<int, ImmutableArray<int>>.Empty
                : ShortestPaths(
                    index,
                    plan.OriginNodeIds,
                    GraphTraversalDirection.Outgoing,
                    work);
        IReadOnlyDictionary<int, ImmutableArray<int>> incomingPaths =
            plan.Reachability
                == GraphFocusReachability.EntireInsideScope
            || plan.Direction == GraphTraversalDirection.Outgoing
                ? ImmutableDictionary<int, ImmutableArray<int>>.Empty
                : ShortestPaths(
                    index,
                    plan.OriginNodeIds,
                    GraphTraversalDirection.Incoming,
                    work);

        var exits = new HashSet<int>();
        var connectors = new HashSet<int>();
        var unclassified = new HashSet<int>();
        foreach (GraphEdge<TRelationship> edge in document.Edges)
        {
            work.CanonicalEdgesExamined++;
            if (!index.RelationshipSelected(edge.Relationship))
                continue;

            GraphScopeMembership from = memberships[edge.FromNodeId];
            GraphScopeMembership to = memberships[edge.ToNodeId];
            work.ExamineNode(edge.FromNodeId);
            work.ExamineNode(edge.ToNodeId);
            if (plan.Direction is GraphTraversalDirection.Outgoing
                or GraphTraversalDirection.Both
                && from == GraphScopeMembership.Inside)
            {
                AddBoundary(
                    edge.Id,
                    to,
                    edge.FromNodeId,
                    outgoingPaths,
                    plan.Reachability
                        == GraphFocusReachability.EntireInsideScope,
                    exits,
                    connectors,
                    unclassified);
            }
            if (plan.Direction is GraphTraversalDirection.Incoming
                or GraphTraversalDirection.Both
                && to == GraphScopeMembership.Inside)
            {
                AddBoundary(
                    edge.Id,
                    from,
                    edge.ToNodeId,
                    incomingPaths,
                    plan.Reachability
                        == GraphFocusReachability.EntireInsideScope,
                    exits,
                    connectors,
                    unclassified);
            }
        }

        ImmutableDictionary<int, ImmutableArray<int>> bestPaths =
            BestPaths(outgoingPaths, incomingPaths);
        work.NodesAdmitted = bestPaths.Count;
        return new GraphFocusResult(
            [.. exits.Order()],
            [.. connectors.Order()],
            [.. unclassified.Order()],
            bestPaths,
            work.CreateReceipt());
    }

    static IEnumerable<GraphTraversalStep> Adjacent<TRelationship>(
        GraphEdgeIndex<TRelationship> index,
        int nodeId,
        GraphTraversalDirection direction,
        GraphExecutionWork work)
        where TRelationship : notnull
    {
        if (direction is GraphTraversalDirection.Outgoing
            or GraphTraversalDirection.Both)
        {
            foreach (GraphTraversalStep step in index.Outgoing(nodeId))
            {
                work.AdjacencyEntriesExamined++;
                yield return step;
            }
        }
        if (direction is GraphTraversalDirection.Incoming
            or GraphTraversalDirection.Both)
        {
            foreach (GraphTraversalStep step in index.Incoming(nodeId))
            {
                work.AdjacencyEntriesExamined++;
                yield return step;
            }
        }
    }

    static bool HasUnretainedAdjacentEdge<TRelationship>(
        GraphEdgeIndex<TRelationship> index,
        int nodeId,
        GraphTraversalDirection direction,
        IReadOnlySet<int> retainedEdges,
        GraphExecutionWork work)
        where TRelationship : notnull
    {
        foreach (GraphTraversalStep step in Adjacent(
                     index,
                     nodeId,
                     direction,
                     work))
        {
            if (!retainedEdges.Contains(step.EdgeId))
                return true;
        }
        return false;
    }

    static void Enqueue(
        int nodeId,
        int depth,
        Dictionary<int, int> nodeDepths,
        Queue<(int NodeId, int Depth)> queue)
    {
        if (nodeDepths.TryGetValue(nodeId, out int priorDepth)
            && priorDepth <= depth)
        {
            return;
        }
        nodeDepths[nodeId] = depth;
        queue.Enqueue((nodeId, depth));
    }

    static Dictionary<int, ImmutableArray<int>> ShortestPaths<
        TRelationship>(
        GraphEdgeIndex<TRelationship> index,
        IEnumerable<int> origins,
        GraphTraversalDirection direction,
        GraphExecutionWork work)
        where TRelationship : notnull
    {
        var distances = new Dictionary<int, int>();
        var paths = new Dictionary<int, ImmutableArray<int>>();
        var queue = new Queue<int>();
        foreach (int origin in origins.Order())
        {
            distances[origin] = 0;
            paths[origin] = [];
            queue.Enqueue(origin);
        }

        while (queue.TryDequeue(out int current))
        {
            work.ExamineNode(current);
            int distance = distances[current];
            ImmutableArray<int> prefix = paths[current];
            foreach (GraphTraversalStep step in Adjacent(
                         index,
                         current,
                         direction,
                         work))
            {
                int candidateDistance = distance + 1;
                ImmutableArray<int> candidate =
                    direction == GraphTraversalDirection.Incoming
                        ? [step.EdgeId, .. prefix]
                        : prefix.Add(step.EdgeId);
                if (distances.TryGetValue(
                        step.NodeId,
                        out int knownDistance)
                    && (knownDistance < candidateDistance
                        || (knownDistance == candidateDistance
                            && CompareSequences(
                                paths[step.NodeId],
                                candidate) <= 0)))
                {
                    continue;
                }

                distances[step.NodeId] = candidateDistance;
                paths[step.NodeId] = candidate;
                queue.Enqueue(step.NodeId);
            }
        }
        return paths;
    }

    static ImmutableDictionary<int, ImmutableArray<int>> BestPaths(
        IReadOnlyDictionary<int, ImmutableArray<int>> outgoing,
        IReadOnlyDictionary<int, ImmutableArray<int>> incoming)
    {
        var result = outgoing.ToDictionary();
        foreach ((int nodeId, ImmutableArray<int> candidate) in incoming)
        {
            if (!result.TryGetValue(nodeId, out ImmutableArray<int> current)
                || candidate.Length < current.Length
                || (candidate.Length == current.Length
                    && CompareSequences(candidate, current) < 0))
            {
                result[nodeId] = candidate;
            }
        }
        return result.ToImmutableDictionary();
    }

    static void AddBoundary(
        int edgeId,
        GraphScopeMembership opposite,
        int insideEndpoint,
        IReadOnlyDictionary<int, ImmutableArray<int>> paths,
        bool retainEntireInsideScope,
        HashSet<int> exitEdgeIds,
        HashSet<int> connectorEdgeIds,
        HashSet<int> unclassifiedEdgeIds)
    {
        if (opposite == GraphScopeMembership.Inside)
            return;

        ImmutableArray<int> connector = [];
        bool hasConnector =
            retainEntireInsideScope
            || paths.TryGetValue(insideEndpoint, out connector);
        if (opposite == GraphScopeMembership.Unknown)
        {
            unclassifiedEdgeIds.Add(edgeId);
            if (hasConnector)
                connectorEdgeIds.UnionWith(connector);
            return;
        }
        if (!hasConnector)
            return;

        exitEdgeIds.Add(edgeId);
        connectorEdgeIds.UnionWith(connector);
    }

    static int CompareSequences(
        ImmutableArray<int> left,
        ImmutableArray<int> right)
    {
        int length = Math.Min(left.Length, right.Length);
        for (int index = 0; index < length; index++)
        {
            int comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
                return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }

    static void ValidateNodeIds(
        IEnumerable<int> nodeIds,
        int nodeCount,
        string parameterName)
    {
        if (nodeIds.Any(nodeId => (uint)nodeId >= (uint)nodeCount))
        {
            throw new ArgumentException(
                "A node id is not present in the document.",
                parameterName);
        }
    }

    readonly record struct GraphTraversalStep(int EdgeId, int NodeId);

    sealed class GraphEdgeIndex<TRelationship>
        where TRelationship : notnull
    {
        readonly ImmutableArray<GraphTraversalStep>[] _outgoing;
        readonly ImmutableArray<GraphTraversalStep>[] _incoming;
        readonly bool[] _selectedEdges;
        readonly HashSet<TRelationship> _relationships;

        GraphEdgeIndex(
            ImmutableArray<GraphTraversalStep>[] outgoing,
            ImmutableArray<GraphTraversalStep>[] incoming,
            bool[] selectedEdges,
            HashSet<TRelationship> relationships)
        {
            _outgoing = outgoing;
            _incoming = incoming;
            _selectedEdges = selectedEdges;
            _relationships = relationships;
        }

        internal ImmutableArray<GraphTraversalStep> Outgoing(int nodeId) =>
            _outgoing[nodeId];

        internal ImmutableArray<GraphTraversalStep> Incoming(int nodeId) =>
            _incoming[nodeId];

        internal bool Contains(int edgeId) => _selectedEdges[edgeId];

        internal bool RelationshipSelected(TRelationship relationship) =>
            _relationships.Contains(relationship);

        internal static GraphEdgeIndex<TRelationship> Create(
            ImmutableArray<GraphEdge<TRelationship>> edges,
            int nodeCount,
            IEnumerable<TRelationship> relationships,
            IEqualityComparer<TRelationship> relationshipComparer,
            GraphExecutionWork work,
            bool includeOutgoing,
            bool includeIncoming,
            Predicate<GraphEdge<TRelationship>>? include = null)
        {
            var relationshipSet = new HashSet<TRelationship>(
                relationships,
                relationshipComparer);
            List<GraphTraversalStep>?[] outgoing =
                includeOutgoing
                    ? new List<GraphTraversalStep>?[nodeCount]
                    : [];
            List<GraphTraversalStep>?[] incoming =
                includeIncoming
                    ? new List<GraphTraversalStep>?[nodeCount]
                    : [];
            var selectedEdges = new bool[edges.Length];
            work.StructuralViewsBuilt +=
                (includeOutgoing ? 1 : 0)
                + (includeIncoming ? 1 : 0);
            foreach (GraphEdge<TRelationship> edge in edges)
            {
                work.CanonicalEdgesExamined++;
                if (!relationshipSet.Contains(edge.Relationship)
                    || include is not null
                    && !include(edge))
                {
                    continue;
                }

                selectedEdges[edge.Id] = true;
                if (includeOutgoing)
                {
                    Add(
                        outgoing,
                        edge.FromNodeId,
                        new(edge.Id, edge.ToNodeId));
                }
                if (includeIncoming)
                {
                    Add(
                        incoming,
                        edge.ToNodeId,
                        new(edge.Id, edge.FromNodeId));
                }
                if (includeOutgoing || includeIncoming)
                    work.SelectedEdgesIndexed++;
            }

            return new(
                includeOutgoing ? Snapshot(outgoing) : [],
                includeIncoming ? Snapshot(incoming) : [],
                selectedEdges,
                relationshipSet);
        }

        static void Add(
            List<GraphTraversalStep>?[] index,
            int nodeId,
            GraphTraversalStep step)
        {
            List<GraphTraversalStep> values =
                index[nodeId] ??= [];
            values.Add(step);
        }

        static ImmutableArray<GraphTraversalStep>[] Snapshot(
            List<GraphTraversalStep>?[] index)
        {
            var result =
                new ImmutableArray<GraphTraversalStep>[index.Length];
            for (int nodeId = 0; nodeId < index.Length; nodeId++)
            {
                result[nodeId] = index[nodeId] is { } values
                    ? [.. values]
                    : [];
            }
            return result;
        }
    }

    sealed class GraphExecutionWork
    {
        readonly HashSet<int> _canonicalNodesExamined = [];

        internal GraphExecutionWork(
            GraphDocumentIdentity sourceDocument)
        {
            SourceDocument = sourceDocument;
        }

        internal GraphDocumentIdentity SourceDocument { get; }
        internal int CanonicalEdgesExamined { get; set; }
        internal int SelectedEdgesIndexed { get; set; }
        internal int StructuralViewsBuilt { get; set; }
        internal int AdjacencyEntriesExamined { get; set; }
        internal int NodesAdmitted { get; set; }

        internal void ExamineNode(int nodeId) =>
            _canonicalNodesExamined.Add(nodeId);

        internal GraphExecutionWorkReceipt CreateReceipt() =>
            new(
                SourceDocument,
                _canonicalNodesExamined.Count,
                CanonicalEdgesExamined,
                SelectedEdgesIndexed,
                StructuralViewsBuilt,
                AdjacencyEntriesExamined,
                NodesAdmitted,
                terminalSettled: true);
    }
}

static class GraphExecutionCollections
{
    internal static ImmutableArray<T> SnapshotValues<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : notnull
    {
        ImmutableArray<T> snapshot =
            GraphCollections.Snapshot(values, parameterName);
        if (snapshot.Any(static value => value is null))
            throw new ArgumentNullException(parameterName);
        return snapshot;
    }

    internal static ImmutableArray<T> SnapshotDistinct<T>(
        IEnumerable<T> values,
        IEqualityComparer<T> comparer,
        string parameterName)
        where T : notnull
    {
        ImmutableArray<T> snapshot =
            GraphCollections.Snapshot(values, parameterName);
        if (snapshot.Any(static value => value is null))
            throw new ArgumentNullException(parameterName);
        if (snapshot.ToHashSet(comparer).Count != snapshot.Length)
        {
            throw new ArgumentException(
                "Values must be distinct.",
                parameterName);
        }
        return snapshot;
    }

    internal static ImmutableArray<int> SnapshotDistinctIds(
        IEnumerable<int> values,
        string parameterName)
    {
        ImmutableArray<int> snapshot =
            GraphCollections.Snapshot(values, parameterName);
        if (snapshot.Any(static value => value < 0))
            throw new ArgumentOutOfRangeException(parameterName);
        if (snapshot.Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Ids must be distinct.",
                parameterName);
        }
        return snapshot;
    }

    internal static ImmutableArray<GraphNodeScope>
        SnapshotDistinctNodeScopes(
        IEnumerable<GraphNodeScope> values,
        string parameterName)
    {
        ImmutableArray<GraphNodeScope> snapshot =
            GraphCollections.Snapshot(values, parameterName);
        if (snapshot.Select(static scope => scope.NodeId)
            .Distinct()
            .Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Node scopes must have distinct node ids.",
                parameterName);
        }
        return snapshot;
    }
}
