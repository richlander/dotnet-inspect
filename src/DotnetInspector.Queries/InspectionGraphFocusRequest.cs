using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>How one graph subject relates to the selected scope.</summary>
public enum InspectionGraphScopeMembership
{
    Unknown,
    Inside,
    Outside,
}

/// <summary>One owner-issued scope decision over a semantic subject.</summary>
public sealed record InspectionGraphScopeDecision
{
    public InspectionGraphScopeDecision(
        InspectionGraphSubject subject,
        InspectionGraphScopeMembership membership)
    {
        ArgumentNullException.ThrowIfNull(subject);
        InspectionGraphCollections.RequireDefined(
            membership,
            nameof(membership));
        Subject = subject;
        Membership = membership;
    }

    public InspectionGraphSubject Subject { get; }
    public InspectionGraphScopeMembership Membership { get; }
}

/// <summary>The topology extent retained by focus projection.</summary>
public enum InspectionGraphFocusExtent
{
    ExitFrontier,
}

/// <summary>
/// Focus choices applied to one finite, already-produced graph document.
/// </summary>
public sealed class InspectionGraphFocusRequest
{
    InspectionGraphFocusRequest(
        InspectionGraphModeRequest modeRequest,
        InspectionGraphFocusExtent extent,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        InspectionGraphTraversalDirection direction,
        IEnumerable<InspectionGraphScopeDecision> scopeDecisions)
    {
        ArgumentNullException.ThrowIfNull(modeRequest);
        InspectionGraphCollections.RequireDefined(extent, nameof(extent));
        InspectionGraphCollections.RequireDefined(direction, nameof(direction));
        ModeRequest = modeRequest;
        Extent = extent;
        Direction = direction;
        Relationships = InspectionGraphCollections.Snapshot(
            relationships,
            nameof(relationships));
        ScopeDecisions = InspectionGraphCollections.Snapshot(
            scopeDecisions,
            nameof(scopeDecisions));

        if (Relationships.IsEmpty)
        {
            throw new ArgumentException(
                "Focus projection requires at least one relationship.",
                nameof(relationships));
        }
        if (Relationships.Distinct().Count() != Relationships.Length
            || Relationships.Select(static relationship => relationship.Id)
                .Distinct(StringComparer.Ordinal).Count()
                != Relationships.Length)
        {
            throw new ArgumentException(
                "Selected relationships must have distinct identities and ids.",
                nameof(relationships));
        }
        if (ScopeDecisions.Select(static decision => decision.Subject)
                .Distinct().Count()
            != ScopeDecisions.Length)
        {
            throw new ArgumentException(
                "Each scope subject must have at most one decision.",
                nameof(scopeDecisions));
        }
        if (!ScopeDecisions.Any(static decision =>
                decision.Membership
                    == InspectionGraphScopeMembership.Inside))
        {
            throw new ArgumentException(
                "Focus projection requires at least one subject inside scope.",
                nameof(scopeDecisions));
        }

        IReadOnlyDictionary<
            InspectionGraphSubject,
            InspectionGraphScopeMembership> membership =
            ScopeDecisions.ToDictionary(
                static decision => decision.Subject,
                static decision => decision.Membership);
        foreach (InspectionGraphSubject seed in modeRequest.Seeds)
        {
            if (!membership.TryGetValue(
                    seed,
                    out InspectionGraphScopeMembership value)
                || value != InspectionGraphScopeMembership.Inside)
            {
                throw new InspectionQueryException(
                    "Every focus origin must have an explicit inside-scope decision.");
            }
            if (!Relationships
                .SelectMany(relationship =>
                    relationship.GetSeedAdmissions(seed.Kind))
                .Any(admission => Includes(admission.Role)))
            {
                string ids = string.Join(
                    ", ",
                    Relationships.Select(static relationship =>
                        relationship.Id));
                throw new InspectionQueryException(
                    $"No selected relationship admits the "
                    + $"{seed.Kind.ToString().ToLowerInvariant()} focus "
                    + $"origin in the "
                    + $"{direction.ToString().ToLowerInvariant()} direction. "
                    + $"Selected relationships: {ids}.");
            }
        }
    }

    public InspectionGraphModeRequest ModeRequest { get; }
    public InspectionGraphFocusExtent Extent { get; }
    public ImmutableArray<InspectionGraphRelationshipDescriptor>
        Relationships { get; }
    public InspectionGraphTraversalDirection Direction { get; }
    public ImmutableArray<InspectionGraphScopeDecision> ScopeDecisions { get; }

    public static InspectionGraphFocusRequest ExitFrontier(
        InspectionGraphModeRequest modeRequest,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        InspectionGraphTraversalDirection direction,
        IEnumerable<InspectionGraphScopeDecision> scopeDecisions) =>
        new(
            modeRequest,
            InspectionGraphFocusExtent.ExitFrontier,
            relationships,
            direction,
            scopeDecisions);

    internal bool Includes(InspectionGraphEndpointRole role) =>
        Direction switch
        {
            InspectionGraphTraversalDirection.Outgoing =>
                role == InspectionGraphEndpointRole.Source,
            InspectionGraphTraversalDirection.Incoming =>
                role == InspectionGraphEndpointRole.Target,
            InspectionGraphTraversalDirection.Both => true,
            _ => throw new ArgumentOutOfRangeException(nameof(Direction)),
        };
}

/// <summary>Focus-projection-owned graph contracts.</summary>
public static class InspectionGraphFocusCatalog
{
    public const string FocusRole = "focus";
    public const string ConnectorRole = "connector";
    public const string ExitRole = "exit";
    public const string UnclassifiedBoundaryRole =
        "unclassified-boundary";

    public static InspectionGraphCharacteristicDescriptor Role { get; } =
        new(
            "queries.focus-role",
            InspectionGraphOwner.Queries,
            InspectionGraphValueCatalog.TokenSet,
            [
                InspectionGraphTargetKind.Node,
                InspectionGraphTargetKind.Group,
                InspectionGraphTargetKind.Edge,
                InspectionGraphTargetKind.Occurrence,
            ],
            [],
            [InspectionGraphCharacteristicDerivationKind.Derived],
            InspectionGraphAggregationPolicy.OrderedDistinctSet);

    public static InspectionGraphLimitDescriptor
        ScopeClassificationIncomplete { get; } =
        new(
            "queries.focus-scope-classification-incomplete",
            InspectionGraphOwner.Queries);
}

/// <summary>
/// Projects one settled Inspection Graph into an evidence-preserving focus
/// view.
/// </summary>
public static class InspectionGraphFocusProjection
{
    public static InspectionGraphDocument Project(
        InspectionGraphDocument source,
        InspectionGraphFocusRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(source.ModeRequest, request.ModeRequest))
        {
            throw new ArgumentException(
                "The source document must use the focus request's mode request.",
                nameof(source));
        }
        if (request.Extent != InspectionGraphFocusExtent.ExitFrontier)
            throw new ArgumentOutOfRangeException(nameof(request));

        var sourceSubjects = source.Nodes
            .Select(static node => node.Subject)
            .ToHashSet();
        InspectionGraphScopeDecision? foreign =
            request.ScopeDecisions.FirstOrDefault(decision =>
                !sourceSubjects.Contains(decision.Subject));
        if (foreign is not null)
        {
            throw new ArgumentException(
                "Every scope decision must identify a node subject in the source document.",
                nameof(request));
        }

        IReadOnlyDictionary<
            InspectionGraphSubject,
            InspectionGraphScopeMembership> membership =
            request.ScopeDecisions.ToDictionary(
                static decision => decision.Subject,
                static decision => decision.Membership);
        var selectedRelationships =
            request.Relationships.ToHashSet();
        InspectionGraphEdge[] selectedEdges =
        [
            .. source.Edges.Where(edge =>
                selectedRelationships.Contains(edge.Relationship)),
        ];
        LocalEdgeIndex local = LocalEdgeIndex.Create(
            source,
            selectedEdges,
            membership);
        ImmutableArray<int> origins = OriginNodeIds(source);
        Dictionary<int, ImmutableArray<int>> outgoingPaths =
            source.ModeRequest.Mode != InspectionGraphMode.InducedSet
            && request.Includes(InspectionGraphEndpointRole.Source)
                ? ShortestPathsFromOrigins(local, origins)
                : [];
        Dictionary<int, ImmutableArray<int>> incomingPaths =
            source.ModeRequest.Mode != InspectionGraphMode.InducedSet
            && request.Includes(InspectionGraphEndpointRole.Target)
                ? ShortestPathsToOrigins(local, origins)
                : [];

        var exitEdgeIds = new HashSet<int>();
        var connectorEdgeIds = new HashSet<int>();
        var unclassifiedEdgeIds = new HashSet<int>();
        foreach (InspectionGraphEdge edge in selectedEdges)
        {
            InspectionGraphScopeMembership from = Membership(
                source.Nodes[edge.FromNodeId].Subject,
                membership);
            InspectionGraphScopeMembership to = Membership(
                source.Nodes[edge.ToNodeId].Subject,
                membership);
            if (from == InspectionGraphScopeMembership.Inside
                && request.Includes(
                    InspectionGraphEndpointRole.Source))
            {
                AddBoundary(
                    edge,
                    to,
                    edge.FromNodeId,
                    outgoingPaths,
                    source.ModeRequest.Mode,
                    exitEdgeIds,
                    connectorEdgeIds,
                    unclassifiedEdgeIds);
            }
            if (to == InspectionGraphScopeMembership.Inside
                && request.Includes(
                    InspectionGraphEndpointRole.Target))
            {
                AddBoundary(
                    edge,
                    from,
                    edge.ToNodeId,
                    incomingPaths,
                    source.ModeRequest.Mode,
                    exitEdgeIds,
                    connectorEdgeIds,
                    unclassifiedEdgeIds);
            }
        }

        HashSet<int> diagnosticNodeIds =
            RetainReachableNodeDiagnostics(
                source,
                membership,
                outgoingPaths,
                incomingPaths,
                connectorEdgeIds);
        HashSet<int> retainedEdgeIds =
        [
            .. exitEdgeIds,
            .. connectorEdgeIds,
            .. unclassifiedEdgeIds,
        ];
        HashSet<int> retainedNodeIds =
        [
            .. retainedEdgeIds.SelectMany(id =>
                new[]
                {
                    source.Edges[id].FromNodeId,
                    source.Edges[id].ToNodeId,
                }),
            .. origins,
            .. diagnosticNodeIds,
        ];
        HashSet<int> retainedOccurrenceIds =
        [
            .. retainedEdgeIds.SelectMany(id =>
                source.Edges[id].OccurrenceIds),
        ];
        if (retainedOccurrenceIds.Any(id =>
            !source.Occurrences[id].DerivedFromOccurrenceIds.IsEmpty))
        {
            throw new InspectionQueryException(
                "Exit-frontier projection does not yet support derived occurrence receipts.");
        }

        var retainedGroupIds = new HashSet<int>();
        RetainExplicitInputSubjects(
            source,
            retainedNodeIds,
            retainedGroupIds);
        foreach (int nodeId in retainedNodeIds)
        {
            retainedGroupIds.UnionWith(
                source.Nodes[nodeId].GroupIds);
        }
        InspectionGraphProjectionUtilities.RetainGroupParents(
            source,
            retainedGroupIds);

        Dictionary<int, int> groupIds =
            InspectionGraphProjectionUtilities.DenseMap(
                retainedGroupIds);
        Dictionary<int, int> nodeIds =
            InspectionGraphProjectionUtilities.DenseMap(
                retainedNodeIds);
        Dictionary<int, int> occurrenceIds =
            InspectionGraphProjectionUtilities.DenseMap(
                retainedOccurrenceIds);
        Dictionary<int, int> edgeIds =
            InspectionGraphProjectionUtilities.DenseMap(
                retainedEdgeIds);

        InspectionGraphGroup[] groups =
        [
            .. retainedGroupIds.Order().Select(id =>
                new InspectionGraphGroup(
                    groupIds[id],
                    source.Groups[id].Subject,
                    source.Groups[id].ParentId is int parentId
                        ? groupIds[parentId]
                        : null)),
        ];
        InspectionGraphNode[] nodes =
        [
            .. retainedNodeIds.Order().Select(id =>
                new InspectionGraphNode(
                    nodeIds[id],
                    source.Nodes[id].Subject,
                    source.Nodes[id].Role,
                    source.Nodes[id].GroupIds
                        .Where(groupIds.ContainsKey)
                        .Select(groupId => groupIds[groupId]))),
        ];
        InspectionGraphOccurrence[] occurrences =
        [
            .. retainedOccurrenceIds.Order().Select(id =>
            {
                InspectionGraphOccurrence occurrence =
                    source.Occurrences[id];
                return new InspectionGraphOccurrence(
                    occurrenceIds[id],
                    occurrence.Relationship,
                    occurrence.SourceSubject,
                    occurrence.TargetSubject,
                    occurrence.Evidence,
                    []);
            }),
        ];
        InspectionGraphEdge[] edges =
        [
            .. retainedEdgeIds.Order().Select(id =>
            {
                InspectionGraphEdge edge = source.Edges[id];
                return new InspectionGraphEdge(
                    edgeIds[id],
                    nodeIds[edge.FromNodeId],
                    nodeIds[edge.ToNodeId],
                    edge.Relationship,
                    edge.OccurrenceIds.Select(
                        occurrenceId =>
                            occurrenceIds[occurrenceId]));
            }),
        ];
        var characteristics =
            new List<InspectionGraphCharacteristic>(
                source.Characteristics.Length
                + retainedEdgeIds.Count
                + origins.Length);
        characteristics.AddRange(
            source.Characteristics.Select(characteristic =>
                InspectionGraphProjectionUtilities.RemapCharacteristic(
                    characteristic,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds))
                .Where(static characteristic =>
                    characteristic is not null)
                .Select(static characteristic => characteristic!));
        AddRoles(
            characteristics,
            origins,
            exitEdgeIds,
            connectorEdgeIds,
            unclassifiedEdgeIds,
            nodeIds,
            edgeIds);

        InspectionGraphSeed[] seeds =
        [
            .. source.Seeds.Select(seed =>
                new InspectionGraphSeed(
                    seed.Subject,
                    InspectionGraphProjectionUtilities.RemapTarget(
                        seed.Target,
                        nodeIds,
                        groupIds,
                        edgeIds,
                        occurrenceIds)
                        ?? throw new InspectionQueryException(
                            "A focus origin target was not retained."),
                    seed.Role)),
        ];
        var limits = new List<InspectionGraphLimit>(
            source.Limits.Length
            + unclassifiedEdgeIds.Count);
        limits.AddRange(
            source.Limits.Select(limit =>
                InspectionGraphProjectionUtilities.RemapLimit(
                    limit,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds))
                .Where(static limit => limit is not null)
                .Select(static limit => limit!));
        limits.AddRange(
            unclassifiedEdgeIds.Order().Select(id =>
                new InspectionGraphLimit(
                    InspectionGraphFocusCatalog
                        .ScopeClassificationIncomplete,
                    InspectionGraphTarget.Edge(edgeIds[id]))));

        InspectionGraphFailure[] failures =
        [
            .. source.Failures.Select(failure =>
                InspectionGraphProjectionUtilities.RemapFailure(
                    failure,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds))
                .Where(static failure => failure is not null)
                .Select(static failure => failure!),
        ];

        return new InspectionGraphDocument(
            source,
            request,
            nodes,
            groups,
            edges,
            occurrences,
            characteristics,
            seeds,
            limits,
            failures);
    }

    static HashSet<int> RetainReachableNodeDiagnostics(
        InspectionGraphDocument source,
        IReadOnlyDictionary<
            InspectionGraphSubject,
            InspectionGraphScopeMembership> membership,
        IReadOnlyDictionary<int, ImmutableArray<int>> outgoingPaths,
        IReadOnlyDictionary<int, ImmutableArray<int>> incomingPaths,
        HashSet<int> connectorEdgeIds)
    {
        var retainedNodeIds = new HashSet<int>();
        IEnumerable<InspectionGraphTarget?> targets =
            source.Limits.Select(static limit => limit.Target)
                .Concat(source.Failures.Select(
                    static failure => failure.Target));
        foreach (InspectionGraphTarget? target in targets)
        {
            if (target is not
                {
                    Kind: InspectionGraphTargetKind.Node,
                } nodeTarget
                || Membership(
                    source.Nodes[nodeTarget.Id].Subject,
                    membership)
                    != InspectionGraphScopeMembership.Inside)
            {
                continue;
            }

            if (source.ModeRequest.Mode
                == InspectionGraphMode.InducedSet)
            {
                retainedNodeIds.Add(nodeTarget.Id);
                continue;
            }

            ImmutableArray<int>? connector = null;
            if (outgoingPaths.TryGetValue(
                    nodeTarget.Id,
                    out ImmutableArray<int> outgoing))
            {
                connector = outgoing;
            }
            if (incomingPaths.TryGetValue(
                    nodeTarget.Id,
                    out ImmutableArray<int> incoming)
                && (connector is null
                    || incoming.Length < connector.Value.Length
                    || (incoming.Length == connector.Value.Length
                        && CompareSequences(
                            incoming,
                            connector.Value) < 0)))
            {
                connector = incoming;
            }
            if (connector is null)
                continue;

            connectorEdgeIds.UnionWith(connector.Value);
            retainedNodeIds.Add(nodeTarget.Id);
        }
        return retainedNodeIds;
    }

    static void RetainExplicitInputSubjects(
        InspectionGraphDocument source,
        HashSet<int> retainedNodeIds,
        HashSet<int> retainedGroupIds)
    {
        if (source.InducedSetRequest is not { } request)
            return;

        HashSet<InspectionGraphSubject> subjects =
            request.Subjects.ToHashSet();
        retainedNodeIds.UnionWith(
            source.Nodes
                .Where(node => subjects.Contains(node.Subject))
                .Select(static node => node.Id));
        retainedGroupIds.UnionWith(
            source.Groups
                .Where(group => subjects.Contains(group.Subject))
                .Select(static group => group.Id));
    }

    static ImmutableArray<int> OriginNodeIds(
        InspectionGraphDocument source)
    {
        if (source.ModeRequest.Mode == InspectionGraphMode.InducedSet)
        {
            if (!source.Seeds.IsEmpty)
            {
                throw new InspectionQueryException(
                    "An induced focus source cannot contain seeds.");
            }
            return [];
        }

        if (source.Seeds.Length != source.ModeRequest.Seeds.Length
            || !source.Seeds.Select(static seed => seed.Subject)
                .SequenceEqual(source.ModeRequest.Seeds))
        {
            throw new InspectionQueryException(
                "The source document seeds do not match its focus origins.");
        }

        var origins = ImmutableArray.CreateBuilder<int>(
            source.Seeds.Length);
        foreach (InspectionGraphSeed seed in source.Seeds)
        {
            if (seed.Target.Kind != InspectionGraphTargetKind.Node)
            {
                throw new InspectionQueryException(
                    "This exit-frontier adoption requires node-bound focus origins.");
            }
            origins.Add(seed.Target.Id);
        }
        return origins.ToImmutable();
    }

    static void AddBoundary(
        InspectionGraphEdge edge,
        InspectionGraphScopeMembership opposite,
        int insideEndpoint,
        IReadOnlyDictionary<int, ImmutableArray<int>> paths,
        InspectionGraphMode mode,
        HashSet<int> exitEdgeIds,
        HashSet<int> connectorEdgeIds,
        HashSet<int> unclassifiedEdgeIds)
    {
        if (opposite == InspectionGraphScopeMembership.Inside)
            return;

        ImmutableArray<int> connector = [];
        bool hasConnector =
            mode == InspectionGraphMode.InducedSet
            || paths.TryGetValue(insideEndpoint, out connector);
        if (opposite == InspectionGraphScopeMembership.Unknown)
        {
            unclassifiedEdgeIds.Add(edge.Id);
            if (hasConnector)
                connectorEdgeIds.UnionWith(connector);
            return;
        }
        if (!hasConnector)
            return;

        exitEdgeIds.Add(edge.Id);
        connectorEdgeIds.UnionWith(connector);
    }

    static Dictionary<int, ImmutableArray<int>>
        ShortestPathsFromOrigins(
        LocalEdgeIndex index,
        ImmutableArray<int> origins)
    {
        Dictionary<int, int> distances = Distances(
            index,
            origins,
            reverse: false);
        var paths = origins.ToDictionary(
            static origin => origin,
            static _ => ImmutableArray<int>.Empty);
        foreach ((int node, int distance) in distances
            .Where(static item => item.Value > 0)
            .OrderBy(static item => item.Value)
            .ThenBy(static item => item.Key))
        {
            ImmutableArray<int>? best = null;
            foreach (InspectionGraphEdge edge in index.Incoming(node))
            {
                if (!distances.TryGetValue(
                        edge.FromNodeId,
                        out int predecessorDistance)
                    || predecessorDistance != distance - 1)
                {
                    continue;
                }

                ImmutableArray<int> candidate =
                    paths[edge.FromNodeId].Add(edge.Id);
                if (best is null
                    || CompareSequences(candidate, best.Value) < 0)
                {
                    best = candidate;
                }
            }
            if (best is not null)
                paths.Add(node, best.Value);
        }
        return paths;
    }

    static Dictionary<int, ImmutableArray<int>>
        ShortestPathsToOrigins(
        LocalEdgeIndex index,
        ImmutableArray<int> origins)
    {
        Dictionary<int, int> distances = Distances(
            index,
            origins,
            reverse: true);
        var paths = origins.ToDictionary(
            static origin => origin,
            static _ => ImmutableArray<int>.Empty);
        foreach ((int node, int distance) in distances
            .Where(static item => item.Value > 0)
            .OrderBy(static item => item.Value)
            .ThenBy(static item => item.Key))
        {
            ImmutableArray<int>? best = null;
            foreach (InspectionGraphEdge edge in index.Outgoing(node))
            {
                if (!distances.TryGetValue(
                        edge.ToNodeId,
                        out int successorDistance)
                    || successorDistance != distance - 1)
                {
                    continue;
                }

                ImmutableArray<int> candidate =
                    [edge.Id, .. paths[edge.ToNodeId]];
                if (best is null
                    || CompareSequences(candidate, best.Value) < 0)
                {
                    best = candidate;
                }
            }
            if (best is not null)
                paths.Add(node, best.Value);
        }
        return paths;
    }

    static Dictionary<int, int> Distances(
        LocalEdgeIndex index,
        ImmutableArray<int> origins,
        bool reverse)
    {
        var distances = origins.ToDictionary(
            static origin => origin,
            static _ => 0);
        var queue = new Queue<int>(origins);
        while (queue.TryDequeue(out int node))
        {
            int nextDistance = distances[node] + 1;
            IEnumerable<InspectionGraphEdge> edges = reverse
                ? index.Incoming(node)
                : index.Outgoing(node);
            foreach (InspectionGraphEdge edge in edges)
            {
                int adjacent = reverse
                    ? edge.FromNodeId
                    : edge.ToNodeId;
                if (distances.ContainsKey(adjacent))
                    continue;

                distances.Add(adjacent, nextDistance);
                queue.Enqueue(adjacent);
            }
        }
        return distances;
    }

    static int CompareSequences(
        ImmutableArray<int> first,
        ImmutableArray<int> second)
    {
        int length = Math.Min(first.Length, second.Length);
        for (var index = 0; index < length; index++)
        {
            int comparison = first[index].CompareTo(second[index]);
            if (comparison != 0)
                return comparison;
        }
        return first.Length.CompareTo(second.Length);
    }

    static void AddRoles(
        List<InspectionGraphCharacteristic> characteristics,
        ImmutableArray<int> origins,
        HashSet<int> exitEdgeIds,
        HashSet<int> connectorEdgeIds,
        HashSet<int> unclassifiedEdgeIds,
        IReadOnlyDictionary<int, int> nodeIds,
        IReadOnlyDictionary<int, int> edgeIds)
    {
        foreach (int origin in origins)
        {
            InspectionGraphTarget target =
                InspectionGraphTarget.Node(nodeIds[origin]);
            characteristics.Add(
                Role(
                    target,
                    InspectionGraphFocusCatalog.FocusRole));
        }
        foreach (int id in connectorEdgeIds.Order())
        {
            InspectionGraphTarget target =
                InspectionGraphTarget.Edge(edgeIds[id]);
            characteristics.Add(Role(target, InspectionGraphFocusCatalog.ConnectorRole));
        }
        foreach (int id in exitEdgeIds.Order())
        {
            InspectionGraphTarget target =
                InspectionGraphTarget.Edge(edgeIds[id]);
            characteristics.Add(Role(target, InspectionGraphFocusCatalog.ExitRole));
        }
        foreach (int id in unclassifiedEdgeIds.Order())
        {
            InspectionGraphTarget target =
                InspectionGraphTarget.Edge(edgeIds[id]);
            characteristics.Add(
                Role(
                    target,
                    InspectionGraphFocusCatalog
                        .UnclassifiedBoundaryRole));
        }
    }

    static InspectionGraphCharacteristic Role(
        InspectionGraphTarget target,
        string role) =>
        new(
            InspectionGraphFocusCatalog.Role,
            target,
            new InspectionGraphValue.TokenSet([role]),
            new InspectionGraphCharacteristicDerivation(
                InspectionGraphCharacteristicDerivationKind.Derived,
                [target]));

    static InspectionGraphScopeMembership Membership(
        InspectionGraphSubject subject,
        IReadOnlyDictionary<
            InspectionGraphSubject,
            InspectionGraphScopeMembership> membership) =>
        membership.TryGetValue(subject, out var value)
            ? value
            : InspectionGraphScopeMembership.Unknown;

    sealed class LocalEdgeIndex
    {
        readonly IReadOnlyDictionary<
            int,
            ImmutableArray<InspectionGraphEdge>> _outgoing;
        readonly IReadOnlyDictionary<
            int,
            ImmutableArray<InspectionGraphEdge>> _incoming;

        LocalEdgeIndex(
            IReadOnlyDictionary<
                int,
                ImmutableArray<InspectionGraphEdge>> outgoing,
            IReadOnlyDictionary<
                int,
                ImmutableArray<InspectionGraphEdge>> incoming)
        {
            _outgoing = outgoing;
            _incoming = incoming;
        }

        internal ImmutableArray<InspectionGraphEdge> Outgoing(int node) =>
            _outgoing.TryGetValue(node, out var edges)
                ? edges
                : [];

        internal ImmutableArray<InspectionGraphEdge> Incoming(int node) =>
            _incoming.TryGetValue(node, out var edges)
                ? edges
                : [];

        internal static LocalEdgeIndex Create(
            InspectionGraphDocument source,
            IEnumerable<InspectionGraphEdge> edges,
            IReadOnlyDictionary<
                InspectionGraphSubject,
                InspectionGraphScopeMembership> membership)
        {
            var outgoing =
                new Dictionary<int, List<InspectionGraphEdge>>();
            var incoming =
                new Dictionary<int, List<InspectionGraphEdge>>();
            foreach (InspectionGraphEdge edge in edges)
            {
                if (Membership(
                        source.Nodes[edge.FromNodeId].Subject,
                        membership)
                        != InspectionGraphScopeMembership.Inside
                    || Membership(
                        source.Nodes[edge.ToNodeId].Subject,
                        membership)
                        != InspectionGraphScopeMembership.Inside)
                {
                    continue;
                }
                Add(outgoing, edge.FromNodeId, edge);
                Add(incoming, edge.ToNodeId, edge);
            }

            return new LocalEdgeIndex(
                outgoing.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToImmutableArray()),
                incoming.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToImmutableArray()));
        }

        static void Add(
            Dictionary<int, List<InspectionGraphEdge>> index,
            int node,
            InspectionGraphEdge edge)
        {
            if (!index.TryGetValue(node, out var edges))
            {
                edges = [];
                index.Add(node, edges);
            }
            edges.Add(edge);
        }
    }
}
