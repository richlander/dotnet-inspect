using System.Collections.Immutable;

using ILInspector.Analysis;

namespace DotnetInspector.Queries;

/// <summary>
/// Whether the retained graph supports complete structural conclusions or only
/// graph-relative candidates.
/// </summary>
public enum OverloadFamilyStructuralFactStatus
{
    Complete,
    Candidate,
}

/// <summary>The structural convergence kind of one retained graph node.</summary>
public enum OverloadFamilyConvergenceKind
{
    None,
    Family,
    Implementation,
}

/// <summary>One strongly connected component of overload-family members.</summary>
public sealed class OverloadFamilyComponent
{
    internal OverloadFamilyComponent(
        int id,
        IEnumerable<int> memberNodeIds,
        bool isEntry,
        IEnumerable<int> originEntryComponentIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        Id = id;
        MemberNodeIds = SnapshotDistinct(
            memberNodeIds,
            nameof(memberNodeIds));
        if (MemberNodeIds.IsEmpty)
        {
            throw new ArgumentException(
                "An overload-family component requires at least one member.",
                nameof(memberNodeIds));
        }

        IsEntry = isEntry;
        OriginEntryComponentIds = SnapshotDistinct(
            originEntryComponentIds,
            nameof(originEntryComponentIds));
    }

    public int Id { get; }
    public ImmutableArray<int> MemberNodeIds { get; }
    public bool IsEntry { get; }
    public ImmutableArray<int> OriginEntryComponentIds { get; }

    static ImmutableArray<int> SnapshotDistinct(
        IEnumerable<int> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        ImmutableArray<int> snapshot = [.. values];
        if (snapshot.Any(static value => value < 0))
            throw new ArgumentOutOfRangeException(parameterName);
        if (snapshot.Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Document-local ids must be distinct.",
                parameterName);
        }
        return snapshot;
    }
}

/// <summary>Entry-origin and convergence facts for one retained graph node.</summary>
public sealed class OverloadFamilyNodeStructuralFact
{
    internal OverloadFamilyNodeStructuralFact(
        int nodeId,
        int? familyComponentId,
        IEnumerable<int> originEntryComponentIds,
        OverloadFamilyConvergenceKind convergence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeId);
        if (familyComponentId is int componentId)
            ArgumentOutOfRangeException.ThrowIfNegative(componentId);
        if (!Enum.IsDefined(convergence))
            throw new ArgumentOutOfRangeException(nameof(convergence));

        NodeId = nodeId;
        FamilyComponentId = familyComponentId;
        OriginEntryComponentIds = [
            .. originEntryComponentIds
                ?? throw new ArgumentNullException(
                    nameof(originEntryComponentIds)),
        ];
        if (OriginEntryComponentIds.Any(static id => id < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(originEntryComponentIds));
        }
        if (OriginEntryComponentIds.Distinct().Count()
            != OriginEntryComponentIds.Length)
        {
            throw new ArgumentException(
                "Entry-origin ids must be distinct.",
                nameof(originEntryComponentIds));
        }
        if ((convergence == OverloadFamilyConvergenceKind.Family)
            != (familyComponentId.HasValue
                && OriginEntryComponentIds.Length > 1)
            || (convergence
                    == OverloadFamilyConvergenceKind.Implementation)
                != (!familyComponentId.HasValue
                    && OriginEntryComponentIds.Length > 1))
        {
            throw new ArgumentException(
                "Convergence kind must match family membership and entry-origin multiplicity.",
                nameof(convergence));
        }

        Convergence = convergence;
    }

    public int NodeId { get; }
    public int? FamilyComponentId { get; }
    public ImmutableArray<int> OriginEntryComponentIds { get; }
    public OverloadFamilyConvergenceKind Convergence { get; }
}

/// <summary>
/// One overload-family graph plus deterministic, coverage-qualified structural
/// facts. The retained graph remains the authoritative topology.
/// </summary>
public sealed class OverloadFamilyCallGraphStructuralDocument
{
    internal OverloadFamilyCallGraphStructuralDocument(
        InspectionGraphDocument graph,
        OverloadFamilyStructuralFactStatus status,
        IEnumerable<OverloadFamilyComponent> familyComponents,
        IEnumerable<OverloadFamilyNodeStructuralFact> nodeFacts)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        Graph = graph;
        Status = status;
        FamilyComponents = [
            .. familyComponents
                ?? throw new ArgumentNullException(
                    nameof(familyComponents)),
        ];
        NodeFacts = [
            .. nodeFacts
                ?? throw new ArgumentNullException(nameof(nodeFacts)),
        ];
        if (!FamilyComponents.Select(static component => component.Id)
            .SequenceEqual(Enumerable.Range(0, FamilyComponents.Length)))
        {
            throw new ArgumentException(
                "Family components must use dense document-local ids.",
                nameof(familyComponents));
        }
        if (!NodeFacts.Select(static fact => fact.NodeId)
            .SequenceEqual(Enumerable.Range(0, graph.Nodes.Length)))
        {
            throw new ArgumentException(
                "Every graph node requires one dense structural fact.",
                nameof(nodeFacts));
        }
    }

    public InspectionGraphDocument Graph { get; }
    public OverloadFamilyStructuralFactStatus Status { get; }
    public ImmutableArray<OverloadFamilyComponent> FamilyComponents
        { get; }
    public ImmutableArray<OverloadFamilyNodeStructuralFact> NodeFacts
        { get; }
    public IEnumerable<OverloadFamilyComponent> EntryComponents =>
        FamilyComponents.Where(static component => component.IsEntry);
    public IEnumerable<OverloadFamilyNodeStructuralFact>
        FamilyConvergencePoints =>
        NodeFacts.Where(static fact =>
            fact.Convergence
                == OverloadFamilyConvergenceKind.Family);
    public IEnumerable<OverloadFamilyNodeStructuralFact>
        ImplementationConvergencePoints =>
        NodeFacts.Where(static fact =>
            fact.Convergence
                == OverloadFamilyConvergenceKind.Implementation);
}

/// <summary>
/// Derives structural entry origins and convergence from an already-produced
/// overload-family graph.
/// </summary>
public static class OverloadFamilyCallGraphStructuralQuery
{
    public static InspectionQuery<
        OverloadFamilyCallGraphStructuralDocument> Definition { get; } =
        new(
            "Overload-family call graph structure",
            InspectionCost.Unbounded);

    public static OverloadFamilyCallGraphStructuralDocument Execute(
        LibraryCallGraphAnalysisResult callGraph,
        OverloadFamilyCallGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(callGraph);
        ArgumentNullException.ThrowIfNull(request);
        return OverloadFamilyCallGraphStructuralAdapter.Create(
            OverloadFamilyCallGraphQuery.Execute(callGraph, request));
    }
}

internal static class OverloadFamilyCallGraphStructuralAdapter
{
    internal static OverloadFamilyCallGraphStructuralDocument Create(
        InspectionGraphDocument graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ValidateGraph(graph);

        int[] familyNodeIds = [
            .. graph.Seeds.Select(static seed => seed.Target.Id),
        ];
        var familyNodeSet = familyNodeIds.ToHashSet();
        bool isConstructorFamily = FamilyMember(
            graph.Nodes[familyNodeIds[0]]).Name == ".ctor";
        InspectionGraphEdge[] structuralEdges = [
            .. graph.Edges.Where(edge =>
                IsStructuralEdge(
                    graph,
                    edge,
                    familyNodeSet,
                    isConstructorFamily)),
        ];
        IReadOnlyList<int>[] familyOutgoing =
            BuildFamilyOutgoing(
                familyNodeIds,
                familyNodeSet,
                structuralEdges);
        List<int[]> componentMembers =
            FindFamilyComponents(
                familyNodeIds,
                familyOutgoing);
        int[] componentIdsByNode = Enumerable.Repeat(
                -1,
                graph.Nodes.Length)
            .ToArray();
        for (var componentId = 0;
            componentId < componentMembers.Count;
            componentId++)
        {
            foreach (int nodeId in componentMembers[componentId])
                componentIdsByNode[nodeId] = componentId;
        }

        bool[] isEntry = Enumerable.Repeat(
                true,
                componentMembers.Count)
            .ToArray();
        foreach (InspectionGraphEdge edge in structuralEdges)
        {
            if (!familyNodeSet.Contains(edge.FromNodeId)
                || !familyNodeSet.Contains(edge.ToNodeId))
            {
                continue;
            }

            int sourceComponent =
                componentIdsByNode[edge.FromNodeId];
            int targetComponent =
                componentIdsByNode[edge.ToNodeId];
            if (sourceComponent != targetComponent)
                isEntry[targetComponent] = false;
        }

        int[][] structuralOutgoing =
            BuildStructuralOutgoing(
                graph.Nodes.Length,
                structuralEdges);
        var originsByNode = Enumerable.Range(
                0,
                graph.Nodes.Length)
            .Select(static _ => new List<int>())
            .ToArray();
        for (var componentId = 0;
            componentId < componentMembers.Count;
            componentId++)
        {
            if (!isEntry[componentId])
                continue;

            AddOriginReachability(
                componentId,
                componentMembers[componentId],
                structuralOutgoing,
                originsByNode);
        }

        OverloadFamilyComponent[] components = [
            .. componentMembers.Select((members, componentId) =>
                new OverloadFamilyComponent(
                    componentId,
                    members,
                    isEntry[componentId],
                    members.SelectMany(nodeId =>
                            originsByNode[nodeId])
                        .Distinct()
                        .Order())),
        ];
        OverloadFamilyNodeStructuralFact[] nodeFacts = [
            .. Enumerable.Range(0, graph.Nodes.Length)
                .Select(nodeId =>
                {
                    int? componentId =
                        componentIdsByNode[nodeId] >= 0
                            ? componentIdsByNode[nodeId]
                            : null;
                    OverloadFamilyConvergenceKind convergence =
                        originsByNode[nodeId].Count > 1
                            ? componentId.HasValue
                                ? OverloadFamilyConvergenceKind.Family
                                : OverloadFamilyConvergenceKind
                                    .Implementation
                            : OverloadFamilyConvergenceKind.None;
                    return new OverloadFamilyNodeStructuralFact(
                        nodeId,
                        componentId,
                        originsByNode[nodeId],
                        convergence);
                }),
        ];

        return new OverloadFamilyCallGraphStructuralDocument(
            graph,
            HasCompleteStructuralCoverage(graph)
                ? OverloadFamilyStructuralFactStatus.Complete
                : OverloadFamilyStructuralFactStatus.Candidate,
            components,
            nodeFacts);
    }

    static void ValidateGraph(InspectionGraphDocument graph)
    {
        if (graph.ModeRequest.Mode != InspectionGraphMode.PeerSeeds
            || graph.Seeds.Length < 2
            || graph.NeighborhoodRequest is not
            {
                Direction:
                    InspectionGraphTraversalDirection.Outgoing,
            } request
            || request.Relationships.Length != 1
            || !ReferenceEquals(
                request.Relationships[0],
                CallGraphInspectionGraphCatalog.Call))
        {
            throw new ArgumentException(
                "Structural derivation requires one outgoing call neighborhood over at least two peer seeds.",
                nameof(graph));
        }
        if (graph.Seeds.Any(seed =>
            seed.Role != InspectionGraphSeedRole.Peer
            || seed.Target.Kind != InspectionGraphTargetKind.Node))
        {
            throw new ArgumentException(
                "Every overload-family seed must bind one peer node.",
                nameof(graph));
        }
        if (graph.Edges.Any(edge => !ReferenceEquals(
            edge.Relationship,
            CallGraphInspectionGraphCatalog.Call)))
        {
            throw new ArgumentException(
                "An overload-family structural graph may contain only call relationships.",
                nameof(graph));
        }

        MemberRef first = FamilyMember(
            graph.Nodes[graph.Seeds[0].Target.Id]);
        foreach (InspectionGraphSeed seed in graph.Seeds)
        {
            MemberRef member =
                FamilyMember(graph.Nodes[seed.Target.Id]);
            if (!member.DeclaringType.Equals(first.DeclaringType)
                || !StringComparer.Ordinal.Equals(
                    member.Name,
                    first.Name))
            {
                throw new ArgumentException(
                    "Every peer seed must belong to one exact declaring-type and metadata-name family.",
                    nameof(graph));
            }
        }
    }

    static MemberRef FamilyMember(InspectionGraphNode node) =>
        node.Subject is InspectionGraphSubject.MemberSubject
        {
            Identity:
                InspectionGraphMemberIdentity.CallGraph identity,
        }
            ? identity.Member
            : throw new ArgumentException(
                "An overload-family seed requires Call Graph member identity.");

    static bool IsStructuralEdge(
        InspectionGraphDocument graph,
        InspectionGraphEdge edge,
        IReadOnlySet<int> familyNodeIds,
        bool isConstructorFamily)
    {
        if (!isConstructorFamily
            || !familyNodeIds.Contains(edge.FromNodeId)
            || !familyNodeIds.Contains(edge.ToNodeId))
        {
            return true;
        }

        return edge.OccurrenceIds.Any(occurrenceId =>
            graph.Occurrences[occurrenceId].Evidence
                is CallGraphCallSiteEvidence
                {
                    CallKind: CallKind.Call,
                });
    }

    static IReadOnlyList<int>[] BuildFamilyOutgoing(
        IReadOnlyList<int> familyNodeIds,
        IReadOnlySet<int> familyNodeSet,
        IEnumerable<InspectionGraphEdge> edges)
    {
        var outgoing = familyNodeIds.ToDictionary(
            static nodeId => nodeId,
            static _ => new List<int>());
        foreach (InspectionGraphEdge edge in edges)
        {
            if (familyNodeSet.Contains(edge.FromNodeId)
                && familyNodeSet.Contains(edge.ToNodeId))
            {
                outgoing[edge.FromNodeId].Add(edge.ToNodeId);
            }
        }
        return [
            .. familyNodeIds.Select(nodeId =>
                (IReadOnlyList<int>)outgoing[nodeId]),
        ];
    }

    static List<int[]> FindFamilyComponents(
        IReadOnlyList<int> familyNodeIds,
        IReadOnlyList<int>[] outgoing)
    {
        var familyPosition = familyNodeIds
            .Select((nodeId, index) => (nodeId, index))
            .ToDictionary(
                static item => item.nodeId,
                static item => item.index);
        var indexes = new int[familyNodeIds.Count];
        Array.Fill(indexes, -1);
        var lowLinks = new int[familyNodeIds.Count];
        var onStack = new bool[familyNodeIds.Count];
        var stack = new Stack<int>();
        var components = new List<int[]>();
        int nextIndex = 0;

        void Visit(int position)
        {
            indexes[position] = nextIndex;
            lowLinks[position] = nextIndex;
            nextIndex++;
            stack.Push(position);
            onStack[position] = true;

            foreach (int targetNodeId in outgoing[position])
            {
                int target = familyPosition[targetNodeId];
                if (indexes[target] < 0)
                {
                    Visit(target);
                    lowLinks[position] = Math.Min(
                        lowLinks[position],
                        lowLinks[target]);
                }
                else if (onStack[target])
                {
                    lowLinks[position] = Math.Min(
                        lowLinks[position],
                        indexes[target]);
                }
            }

            if (lowLinks[position] != indexes[position])
                return;

            var members = new List<int>();
            int member;
            do
            {
                member = stack.Pop();
                onStack[member] = false;
                members.Add(familyNodeIds[member]);
            }
            while (member != position);
            members.Sort((left, right) =>
                familyPosition[left].CompareTo(
                    familyPosition[right]));
            components.Add([.. members]);
        }

        for (var position = 0;
            position < familyNodeIds.Count;
            position++)
        {
            if (indexes[position] < 0)
                Visit(position);
        }

        components.Sort((left, right) =>
            familyPosition[left[0]].CompareTo(
                familyPosition[right[0]]));
        return components;
    }

    static int[][] BuildStructuralOutgoing(
        int nodeCount,
        IEnumerable<InspectionGraphEdge> edges)
    {
        var outgoing = Enumerable.Range(0, nodeCount)
            .Select(static _ => new List<int>())
            .ToArray();
        foreach (InspectionGraphEdge edge in edges)
            outgoing[edge.FromNodeId].Add(edge.ToNodeId);
        return [
            .. outgoing.Select(static targets =>
                targets.Distinct().ToArray()),
        ];
    }

    static void AddOriginReachability(
        int originComponentId,
        IEnumerable<int> startNodeIds,
        IReadOnlyList<int>[] outgoing,
        IReadOnlyList<List<int>> originsByNode)
    {
        var seen = new bool[outgoing.Length];
        var queue = new Queue<int>();
        foreach (int nodeId in startNodeIds)
        {
            if (!seen[nodeId])
            {
                seen[nodeId] = true;
                queue.Enqueue(nodeId);
            }
        }

        while (queue.TryDequeue(out int nodeId))
        {
            originsByNode[nodeId].Add(originComponentId);
            foreach (int target in outgoing[nodeId])
            {
                if (!seen[target])
                {
                    seen[target] = true;
                    queue.Enqueue(target);
                }
            }
        }
    }

    static bool HasCompleteStructuralCoverage(
        InspectionGraphDocument graph) =>
        !graph.Limits.Any(limit =>
            ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog.TraversalIncomplete)
            || ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .CorrespondenceIncomplete)
            || ReferenceEquals(
                limit.Descriptor,
                CallGraphInspectionGraphCatalog
                    .PhysicalOccurrencesUnavailable))
        && !graph.Failures.Any(failure =>
            ReferenceEquals(
                failure.Descriptor,
                CallGraphInspectionGraphCatalog.AnalysisIncomplete));
}
