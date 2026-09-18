using System.Collections.Immutable;

namespace DotnetInspector.Sections;

public readonly record struct DependencyHierarchyOccurrenceIdentity(
    DependencyRootOccurrenceIdentity RootOccurrence,
    int Value);

public enum DependencyHierarchyOccurrenceDisposition
{
    Expanded,
    Revisit,
    Cycle,
}

public sealed record DependencyHierarchyRootOccurrence(
    DependencyHierarchyOccurrenceIdentity Identity,
    int RootPosition,
    int NodeId,
    int Depth)
{
    public DependencyRootOccurrenceIdentity RootOccurrence =>
        Identity.RootOccurrence;
}

public sealed record DependencyHierarchyOccurrence(
    DependencyHierarchyOccurrenceIdentity Identity,
    DependencyHierarchyOccurrenceIdentity ParentIdentity,
    int TargetNodeId,
    int IncomingEdgeId,
    int Depth,
    DependencyHierarchyOccurrenceDisposition Disposition)
{
    public DependencyRootOccurrenceIdentity RootOccurrence =>
        Identity.RootOccurrence;
}

public sealed record DependencyHierarchyDocument(
    DependencyGraphDocument BackingGraph,
    ImmutableArray<DependencyHierarchyRootOccurrence> Roots,
    ImmutableArray<DependencyHierarchyOccurrence> Occurrences)
{
    public static DependencyHierarchyDocument Empty { get; } =
        new(
            new DependencyGraphDocument([], [], [], [], []),
            [],
            []);

    public static DependencyHierarchyDocument Create(
        DependencyGraphDocument graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ValidateGraph(graph);

        var outgoingBySource =
            new Dictionary<int, List<DependencyGraphEdge>>();
        var admittedEdgeIdsByRoot = graph.Roots.ToDictionary(
            static root => root.OccurrenceIndex,
            static _ => new HashSet<int>());
        foreach (DependencyGraphEdge edge in graph.Edges)
        {
            if (!outgoingBySource.TryGetValue(
                    edge.SourceNodeId,
                    out List<DependencyGraphEdge>? outgoing))
            {
                outgoing = [];
                outgoingBySource.Add(edge.SourceNodeId, outgoing);
            }
            outgoing.Add(edge);
            foreach (int rootOccurrence in edge.RootOccurrences)
                admittedEdgeIdsByRoot[rootOccurrence].Add(edge.Id);
        }

        var roots =
            ImmutableArray.CreateBuilder<DependencyHierarchyRootOccurrence>(
                graph.Roots.Length);
        var occurrences =
            ImmutableArray.CreateBuilder<DependencyHierarchyOccurrence>();

        for (int rootPosition = 0;
             rootPosition < graph.Roots.Length;
             rootPosition++)
        {
            DependencyGraphRootOccurrence root = graph.Roots[rootPosition];
            var rootOccurrence = new DependencyRootOccurrenceIdentity(
                root.OccurrenceIndex);
            var rootIdentity = new DependencyHierarchyOccurrenceIdentity(
                rootOccurrence,
                Value: 0);
            roots.Add(
                new DependencyHierarchyRootOccurrence(
                    rootIdentity,
                    rootPosition,
                    root.NodeId,
                    Depth: 0));

            var expandedNodes = new HashSet<int> { root.NodeId };
            var ancestorNodes = new HashSet<int> { root.NodeId };
            var emittedEdgeIds = new HashSet<int>();
            HashSet<int> admittedEdgeIds =
                admittedEdgeIdsByRoot[root.OccurrenceIndex];
            var stack = new Stack<ExpansionFrame>();
            stack.Push(
                new ExpansionFrame(
                    root.NodeId,
                    rootIdentity,
                    Depth: 0,
                    Outgoing(root.NodeId),
                    NextEdgeIndex: 0));
            int nextOccurrence = 1;

            while (stack.TryPop(out ExpansionFrame frame))
            {
                if (frame.NextEdgeIndex >= frame.Outgoing.Count)
                {
                    ancestorNodes.Remove(frame.NodeId);
                    continue;
                }

                DependencyGraphEdge edge =
                    frame.Outgoing[frame.NextEdgeIndex];
                stack.Push(
                    frame with
                    {
                        NextEdgeIndex = frame.NextEdgeIndex + 1,
                    });
                if (!admittedEdgeIds.Contains(edge.Id))
                    continue;

                emittedEdgeIds.Add(edge.Id);
                var identity = new DependencyHierarchyOccurrenceIdentity(
                    rootOccurrence,
                    nextOccurrence++);
                DependencyHierarchyOccurrenceDisposition disposition;
                if (ancestorNodes.Contains(edge.TargetNodeId))
                {
                    disposition =
                        DependencyHierarchyOccurrenceDisposition.Cycle;
                }
                else if (!expandedNodes.Add(edge.TargetNodeId))
                {
                    disposition =
                        DependencyHierarchyOccurrenceDisposition.Revisit;
                }
                else
                {
                    disposition =
                        DependencyHierarchyOccurrenceDisposition.Expanded;
                }

                int depth = frame.Depth + 1;
                occurrences.Add(
                    new DependencyHierarchyOccurrence(
                        identity,
                        frame.OccurrenceIdentity,
                        edge.TargetNodeId,
                        edge.Id,
                        depth,
                        disposition));

                if (disposition
                    != DependencyHierarchyOccurrenceDisposition.Expanded)
                {
                    continue;
                }

                ancestorNodes.Add(edge.TargetNodeId);
                stack.Push(
                    new ExpansionFrame(
                        edge.TargetNodeId,
                        identity,
                        depth,
                        Outgoing(edge.TargetNodeId),
                        NextEdgeIndex: 0));
            }

            int[] unreachableEdgeIds =
            [
                .. graph.Edges
                    .Where(edge =>
                        admittedEdgeIds.Contains(edge.Id)
                        && !emittedEdgeIds.Contains(edge.Id))
                    .Select(static edge => edge.Id),
            ];
            if (unreachableEdgeIds.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Dependency graph root occurrence {root.OccurrenceIndex} admits unreachable edges: {string.Join(", ", unreachableEdgeIds)}.");
            }
        }

        return new DependencyHierarchyDocument(
            graph,
            roots.ToImmutable(),
            occurrences.ToImmutable());

        IReadOnlyList<DependencyGraphEdge> Outgoing(int nodeId) =>
            outgoingBySource.TryGetValue(
                nodeId,
                out List<DependencyGraphEdge>? outgoing)
                ? outgoing
                : [];
    }

    public bool Equals(DependencyHierarchyDocument? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && BackingGraph == other.BackingGraph
        && DependencyValueEquality.SequenceEqual(Roots, other.Roots)
        && DependencyValueEquality.SequenceEqual(
            Occurrences,
            other.Occurrences);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BackingGraph);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Roots);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Occurrences);
        return hash.ToHashCode();
    }

    private static void ValidateGraph(DependencyGraphDocument graph)
    {
        if (graph.Roots.IsDefault
            || graph.Nodes.IsDefault
            || graph.Edges.IsDefault
            || graph.PackageProjections.IsDefault
            || graph.DepthBoundaries.IsDefault)
        {
            throw new InvalidOperationException(
                "Dependency graph arrays must be initialized.");
        }

        for (int index = 0; index < graph.Nodes.Length; index++)
        {
            if (graph.Nodes[index].Id != index)
            {
                throw new InvalidOperationException(
                    $"Dependency graph node {graph.Nodes[index].Id} does not match its document position {index}.");
            }
        }

        var rootOccurrences = new HashSet<int>();
        foreach (DependencyGraphRootOccurrence root in graph.Roots)
        {
            ValidateNodeId(graph, root.NodeId, "root");
            if (!rootOccurrences.Add(root.OccurrenceIndex))
            {
                throw new InvalidOperationException(
                    $"Dependency graph root occurrence {root.OccurrenceIndex} is duplicated.");
            }
        }

        var edgeIds = new HashSet<int>();
        foreach (DependencyGraphEdge edge in graph.Edges)
        {
            if (!edgeIds.Add(edge.Id))
            {
                throw new InvalidOperationException(
                    $"Dependency graph edge {edge.Id} is duplicated.");
            }
            ValidateNodeId(graph, edge.SourceNodeId, "edge source");
            ValidateNodeId(graph, edge.TargetNodeId, "edge target");
            if (edge.RootOccurrences.IsDefault)
            {
                throw new InvalidOperationException(
                    $"Dependency graph edge {edge.Id} has an uninitialized root-occurrence set.");
            }
            if (edge.RootOccurrences.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"Dependency graph edge {edge.Id} is not admitted by any root occurrence.");
            }

            var edgeRootOccurrences = new HashSet<int>();
            foreach (int rootOccurrence in edge.RootOccurrences)
            {
                if (!edgeRootOccurrences.Add(rootOccurrence))
                {
                    throw new InvalidOperationException(
                        $"Dependency graph edge {edge.Id} duplicates root occurrence {rootOccurrence}.");
                }
                if (!rootOccurrences.Contains(rootOccurrence))
                {
                    throw new InvalidOperationException(
                        $"Dependency graph edge {edge.Id} names unknown root occurrence {rootOccurrence}.");
                }
            }
        }
    }

    private static void ValidateNodeId(
        DependencyGraphDocument graph,
        int nodeId,
        string role)
    {
        if ((uint)nodeId >= (uint)graph.Nodes.Length)
        {
            throw new InvalidOperationException(
                $"Dependency graph {role} node {nodeId} is outside the node table.");
        }
    }

    private readonly record struct ExpansionFrame(
        int NodeId,
        DependencyHierarchyOccurrenceIdentity OccurrenceIdentity,
        int Depth,
        IReadOnlyList<DependencyGraphEdge> Outgoing,
        int NextEdgeIndex);
}
