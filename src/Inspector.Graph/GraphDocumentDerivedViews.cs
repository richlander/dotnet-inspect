using System.Collections.Immutable;

namespace Inspector.Graph;

public enum GraphSelfLoopPolicy
{
    Include,
    Exclude,
}

public sealed class GraphNeighborPlan<TRelationship>
    where TRelationship : notnull
{
    public GraphNeighborPlan(
        IReadOnlyList<TRelationship> relationships,
        GraphTraversalDirection direction,
        GraphSelfLoopPolicy selfLoopPolicy)
    {
        GraphCollections.RequireDefined(direction, nameof(direction));
        GraphCollections.RequireDefined(
            selfLoopPolicy,
            nameof(selfLoopPolicy));
        Relationships = GraphExecutionCollections.SnapshotValues(
            relationships,
            nameof(relationships));
        Direction = direction;
        SelfLoopPolicy = selfLoopPolicy;
    }

    public ImmutableArray<TRelationship> Relationships { get; }
    public GraphTraversalDirection Direction { get; }
    public GraphSelfLoopPolicy SelfLoopPolicy { get; }
}

public sealed class GraphAdjacencyRow
{
    internal GraphAdjacencyRow(
        int nodeId,
        ImmutableArray<int> edgeIds,
        ImmutableArray<int> neighborNodeIds)
    {
        NodeId = nodeId;
        EdgeIds = edgeIds;
        NeighborNodeIds = neighborNodeIds;
    }

    public int NodeId { get; }
    public ImmutableArray<int> EdgeIds { get; }
    public ImmutableArray<int> NeighborNodeIds { get; }
}

public readonly record struct GraphNodeDegree
{
    public GraphNodeDegree(int nodeId, int degree)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeId);
        ArgumentOutOfRangeException.ThrowIfNegative(degree);
        NodeId = nodeId;
        Degree = degree;
    }

    public int NodeId { get; }
    public int Degree { get; }
}

public sealed class GraphAdjacencyResult
{
    internal GraphAdjacencyResult(
        ImmutableArray<GraphAdjacencyRow> rows,
        GraphExecutionWorkReceipt receipt)
    {
        Rows = rows;
        Receipt = receipt;
    }

    public ImmutableArray<GraphAdjacencyRow> Rows { get; }
    public GraphStructuralCompletion Completion =>
        GraphStructuralCompletion.Exhausted;
    public GraphExecutionWorkReceipt Receipt { get; }
}

public sealed class GraphDistinctNeighborDegreeResult
{
    internal GraphDistinctNeighborDegreeResult(
        ImmutableArray<GraphNodeDegree> rows,
        GraphExecutionWorkReceipt receipt)
    {
        Rows = rows;
        Receipt = receipt;
    }

    public ImmutableArray<GraphNodeDegree> Rows { get; }
    public GraphStructuralCompletion Completion =>
        GraphStructuralCompletion.Exhausted;
    public GraphExecutionWorkReceipt Receipt { get; }
}

public static partial class GraphDocumentExecution
{
    public static GraphAdjacencyResult Adjacency<
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
        GraphNeighborPlan<TRelationship> plan)
        where TSubject : notnull
        where TRelationship : notnull
        where TOccurrenceEvidence : notnull
        where TCharacteristic : notnull
        where TLimit : notnull
        where TFailure : notnull
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);

        var work = new GraphExecutionWork(document.Identity)
        {
            StructuralViewsBuilt = 1,
            NodesAdmitted = document.Nodes.Length,
        };
        work.ExamineAllNodes(document.Nodes.Length);
        var edgeIds = new List<int>?[document.Nodes.Length];
        var neighborNodeIds = new List<int>?[document.Nodes.Length];
        HashSet<TRelationship> relationships = SelectedRelationships(
            plan.Relationships,
            document.RelationshipComparer);
        if (relationships.Count > 0)
        {
            foreach (GraphEdge<TRelationship> edge in document.Edges)
            {
                work.CanonicalEdgesExamined++;
                if (!relationships.Contains(edge.Relationship)
                    || plan.SelfLoopPolicy == GraphSelfLoopPolicy.Exclude
                    && edge.FromNodeId == edge.ToNodeId)
                {
                    continue;
                }

                AddSelectedAdjacency(
                    edge,
                    plan.Direction,
                    edgeIds,
                    neighborNodeIds,
                    work);
                work.SelectedEdgesIndexed++;
            }
        }

        var rows = ImmutableArray.CreateBuilder<GraphAdjacencyRow>(
            document.Nodes.Length);
        for (var nodeId = 0; nodeId < document.Nodes.Length; nodeId++)
        {
            rows.Add(
                new(
                    nodeId,
                    edgeIds[nodeId] is { } selectedEdges
                        ? [.. selectedEdges]
                        : [],
                    SnapshotDistinctSorted(neighborNodeIds[nodeId])));
        }
        return new(rows.MoveToImmutable(), work.CreateReceipt());
    }

    public static GraphDistinctNeighborDegreeResult DistinctNeighborDegree<
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
        GraphNeighborPlan<TRelationship> plan)
        where TSubject : notnull
        where TRelationship : notnull
        where TOccurrenceEvidence : notnull
        where TCharacteristic : notnull
        where TLimit : notnull
        where TFailure : notnull
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);

        var work = new GraphExecutionWork(document.Identity)
        {
            StructuralViewsBuilt = 1,
            NodesAdmitted = document.Nodes.Length,
        };
        work.ExamineAllNodes(document.Nodes.Length);
        var degrees = new int[document.Nodes.Length];
        HashSet<TRelationship> relationships = SelectedRelationships(
            plan.Relationships,
            document.RelationshipComparer);
        // One directed relationship inherits the document's logical-edge
        // uniqueness. Both directions can encounter reciprocal edges, while
        // multiple relationships can share the same endpoints.
        HashSet<GraphNeighborIdentity>? selectedNeighbors =
            relationships.Count > 0
            && (relationships.Count > 1
                || plan.Direction == GraphTraversalDirection.Both)
                ? []
                : null;
        if (relationships.Count > 0)
        {
            foreach (GraphEdge<TRelationship> edge in document.Edges)
            {
                work.CanonicalEdgesExamined++;
                if (!relationships.Contains(edge.Relationship)
                    || plan.SelfLoopPolicy == GraphSelfLoopPolicy.Exclude
                    && edge.FromNodeId == edge.ToNodeId)
                {
                    continue;
                }

                AddSelectedDegrees(
                    edge,
                    plan.Direction,
                    selectedNeighbors,
                    degrees,
                    work);
                work.SelectedEdgesIndexed++;
            }
        }

        var rows = ImmutableArray.CreateBuilder<GraphNodeDegree>(
            document.Nodes.Length);
        for (var nodeId = 0; nodeId < document.Nodes.Length; nodeId++)
            rows.Add(new(nodeId, degrees[nodeId]));
        return new(rows.MoveToImmutable(), work.CreateReceipt());
    }

    static HashSet<TRelationship> SelectedRelationships<TRelationship>(
        IEnumerable<TRelationship> relationships,
        IEqualityComparer<TRelationship> comparer)
        where TRelationship : notnull =>
        new(relationships, comparer);

    static void AddSelectedAdjacency<TRelationship>(
        GraphEdge<TRelationship> edge,
        GraphTraversalDirection direction,
        List<int>?[] edgeIds,
        List<int>?[] neighborNodeIds,
        GraphExecutionWork work)
        where TRelationship : notnull
    {
        if (direction is GraphTraversalDirection.Outgoing
            or GraphTraversalDirection.Both)
        {
            AddAdjacency(
                edge.FromNodeId,
                edge.ToNodeId,
                edge.Id,
                edgeIds,
                neighborNodeIds,
                work);
        }
        if (direction == GraphTraversalDirection.Incoming
            || direction == GraphTraversalDirection.Both
            && edge.FromNodeId != edge.ToNodeId)
        {
            AddAdjacency(
                edge.ToNodeId,
                edge.FromNodeId,
                edge.Id,
                edgeIds,
                neighborNodeIds,
                work);
        }
    }

    static void AddAdjacency(
        int nodeId,
        int neighborNodeId,
        int edgeId,
        List<int>?[] edgeIds,
        List<int>?[] neighborNodeIds,
        GraphExecutionWork work)
    {
        (edgeIds[nodeId] ??= []).Add(edgeId);
        (neighborNodeIds[nodeId] ??= []).Add(neighborNodeId);
        work.AdjacencyEntriesExamined++;
    }

    static void AddSelectedDegrees<TRelationship>(
        GraphEdge<TRelationship> edge,
        GraphTraversalDirection direction,
        HashSet<GraphNeighborIdentity>? selectedNeighbors,
        int[] degrees,
        GraphExecutionWork work)
        where TRelationship : notnull
    {
        if (direction is GraphTraversalDirection.Outgoing
            or GraphTraversalDirection.Both)
        {
            AddNeighbor(
                edge.FromNodeId,
                edge.ToNodeId,
                selectedNeighbors,
                degrees,
                work);
        }
        if (direction == GraphTraversalDirection.Incoming
            || direction == GraphTraversalDirection.Both
            && edge.FromNodeId != edge.ToNodeId)
        {
            AddNeighbor(
                edge.ToNodeId,
                edge.FromNodeId,
                selectedNeighbors,
                degrees,
                work);
        }
    }

    static void AddNeighbor(
        int nodeId,
        int neighborNodeId,
        HashSet<GraphNeighborIdentity>? selectedNeighbors,
        int[] degrees,
        GraphExecutionWork work)
    {
        if (selectedNeighbors is null
            || selectedNeighbors.Add(new(nodeId, neighborNodeId)))
        {
            degrees[nodeId]++;
        }
        work.AdjacencyEntriesExamined++;
    }

    static ImmutableArray<int> SnapshotDistinctSorted(
        List<int>? values)
    {
        if (values is null)
            return [];

        values.Sort();
        var result = ImmutableArray.CreateBuilder<int>(values.Count);
        var hasPrior = false;
        var prior = 0;
        foreach (int value in values)
        {
            if (hasPrior && value == prior)
                continue;
            result.Add(value);
            prior = value;
            hasPrior = true;
        }
        return result.DrainToImmutable();
    }

    readonly record struct GraphNeighborIdentity(
        int NodeId,
        int NeighborNodeId);
}
