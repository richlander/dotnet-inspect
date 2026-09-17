using System.Collections.Immutable;

namespace ILInspector.CallGraph;

/// <summary>The call directions admitted by an external-focused projection.</summary>
[Flags]
public enum ExternalFocusedCallGraphDirection
{
    Incoming = 1,
    Outgoing = 2,
    Both = Incoming | Outgoing,
}

/// <summary>The topology retained around an explicitly classified hub.</summary>
public enum ExternalFocusedCallGraphMode
{
    BoundaryOnly,
    SeededConnectors,
}

/// <summary>
/// Explicit node membership and topology requested from one source call graph.
/// </summary>
public sealed class ExternalFocusedCallGraphRequest
{
    public ExternalFocusedCallGraphRequest(
        IEnumerable<int> hubNodeIds,
        IEnumerable<int> externalNodeIds,
        ExternalFocusedCallGraphDirection direction =
            ExternalFocusedCallGraphDirection.Both,
        ExternalFocusedCallGraphMode mode =
            ExternalFocusedCallGraphMode.BoundaryOnly,
        IEnumerable<int>? seedNodeIds = null)
    {
        ArgumentNullException.ThrowIfNull(hubNodeIds);
        ArgumentNullException.ThrowIfNull(externalNodeIds);
        if (direction is < ExternalFocusedCallGraphDirection.Incoming
            or > ExternalFocusedCallGraphDirection.Both)
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode));

        HubNodeIds = Normalize(hubNodeIds, nameof(hubNodeIds));
        ExternalNodeIds = Normalize(
            externalNodeIds,
            nameof(externalNodeIds));
        SeedNodeIds = Normalize(
            seedNodeIds ?? [],
            nameof(seedNodeIds));
        Direction = direction;
        Mode = mode;

        if (HubNodeIds.IsEmpty)
        {
            throw new ArgumentException(
                "External focus requires at least one hub node.",
                nameof(hubNodeIds));
        }
        if (HubNodeIds.Intersect(ExternalNodeIds).Any())
        {
            throw new ArgumentException(
                "Hub and external node membership must be disjoint.",
                nameof(externalNodeIds));
        }
        if (mode == ExternalFocusedCallGraphMode.SeededConnectors
            && SeedNodeIds.IsEmpty)
        {
            throw new ArgumentException(
                "Seeded connector projection requires at least one seed node.",
                nameof(seedNodeIds));
        }
        if (mode == ExternalFocusedCallGraphMode.BoundaryOnly
            && !SeedNodeIds.IsEmpty)
        {
            throw new ArgumentException(
                "Boundary-only projection does not accept seed nodes.",
                nameof(seedNodeIds));
        }
        if (SeedNodeIds.Except(HubNodeIds).Any())
        {
            throw new ArgumentException(
                "Every external-focus seed must be a hub node.",
                nameof(seedNodeIds));
        }
    }

    public ImmutableArray<int> HubNodeIds { get; }
    public ImmutableArray<int> ExternalNodeIds { get; }
    public ImmutableArray<int> SeedNodeIds { get; }
    public ExternalFocusedCallGraphDirection Direction { get; }
    public ExternalFocusedCallGraphMode Mode { get; }

    static ImmutableArray<int> Normalize(
        IEnumerable<int> ids,
        string parameterName)
    {
        int[] normalized = [.. ids.Distinct().Order()];
        if (normalized.Any(static id => id < 0))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Call graph node ids cannot be negative.");
        }

        return [.. normalized];
    }
}

/// <summary>
/// A source-relative view retaining calls that cross one explicit hub
/// boundary, with optional shortest hub-local connectors.
/// </summary>
public sealed class ExternalFocusedCallGraphProjection
{
    ExternalFocusedCallGraphProjection(
        CallGraphProjection source,
        ExternalFocusedCallGraphRequest request,
        ImmutableArray<CallGraphRow> boundaryRows,
        ImmutableArray<CallGraphRow> connectorRows,
        ImmutableArray<CallGraphRow> unclassifiedBoundaryRows,
        ImmutableArray<CallGraphRow> unclassifiedConnectorRows)
    {
        Source = source;
        Request = request;
        BoundaryRows = boundaryRows;
        ConnectorRows = connectorRows;
        UnclassifiedBoundaryRows = unclassifiedBoundaryRows;
        UnclassifiedConnectorRows = unclassifiedConnectorRows;

        HashSet<int> retainedRowNumbers =
        [
            .. boundaryRows.Select(static row => row.Number),
            .. connectorRows.Select(static row => row.Number),
        ];
        Rows =
        [
            .. source.Rows.Where(row =>
                retainedRowNumbers.Contains(row.Number)),
        ];

        HashSet<int> evidenceRowNumbers =
        [
            .. retainedRowNumbers,
            .. unclassifiedBoundaryRows.Select(static row => row.Number),
            .. unclassifiedConnectorRows.Select(static row => row.Number),
        ];
        EvidenceRows =
        [
            .. source.Rows.Where(row =>
                evidenceRowNumbers.Contains(row.Number)),
        ];

        HashSet<int> nodeIds =
        [
            .. EvidenceRows.SelectMany(static row =>
                new[] { row.Edge.From, row.Edge.To }),
            .. (request.Mode
                == ExternalFocusedCallGraphMode.SeededConnectors
                    ? request.SeedNodeIds
                    : []),
        ];
        Nodes =
        [
            .. source.Nodes.Where(node => nodeIds.Contains(node.Id)),
        ];

        HashSet<int> callSiteIds =
        [
            .. EvidenceRows.SelectMany(
                static row => row.Edge.CallSiteIds),
        ];
        CallSites =
        [
            .. source.CallSites.Where(callSite =>
                callSiteIds.Contains(callSite.Id)),
        ];
    }

    public CallGraphProjection Source { get; }
    public ExternalFocusedCallGraphRequest Request { get; }
    public ImmutableArray<CallGraphNode> Nodes { get; }
    public ImmutableArray<CallGraphRow> Rows { get; }
    public ImmutableArray<CallGraphRow> EvidenceRows { get; }
    public ImmutableArray<CallGraphRow> BoundaryRows { get; }
    public ImmutableArray<CallGraphRow> ConnectorRows { get; }
    public ImmutableArray<CallGraphCallSite> CallSites { get; }

    /// <summary>
    /// Directionally relevant hub edges whose opposite endpoint was not
    /// classified.
    /// </summary>
    public ImmutableArray<CallGraphRow> UnclassifiedBoundaryRows { get; }

    /// <summary>
    /// Hub-local connectors to retained unclassified boundary candidates.
    /// </summary>
    public ImmutableArray<CallGraphRow> UnclassifiedConnectorRows { get; }

    /// <summary>
    /// Whether every directionally relevant edge incident on the hub has a
    /// classified opposite endpoint.
    /// </summary>
    public bool HasCompleteBoundaryClassification =>
        UnclassifiedBoundaryRows.IsEmpty;

    public static ExternalFocusedCallGraphProjection Create(
        CallGraphProjection source,
        ExternalFocusedCallGraphRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        ValidateNodeIds(source, request);

        var hub = request.HubNodeIds.ToHashSet();
        var external = request.ExternalNodeIds.ToHashSet();
        HubLocalIndex hubIndex = HubLocalIndex.Create(source, hub);
        Dictionary<int, ImmutableArray<int>> outgoingPaths =
            request.Mode == ExternalFocusedCallGraphMode.SeededConnectors
            && request.Direction.HasFlag(
                ExternalFocusedCallGraphDirection.Outgoing)
                ? ShortestPathsFromSeeds(
                    hubIndex,
                    request.SeedNodeIds)
                : [];
        Dictionary<int, ImmutableArray<int>> incomingPaths =
            request.Mode == ExternalFocusedCallGraphMode.SeededConnectors
            && request.Direction.HasFlag(
                ExternalFocusedCallGraphDirection.Incoming)
                ? ShortestPathsToSeeds(
                    hubIndex,
                    request.SeedNodeIds)
                : [];

        var boundaryRowNumbers = new HashSet<int>();
        var connectorRowNumbers = new HashSet<int>();
        var unclassifiedRowNumbers = new HashSet<int>();
        var unclassifiedConnectorRowNumbers = new HashSet<int>();
        foreach (CallGraphRow row in source.Rows)
        {
            NodeMembership from = Membership(
                row.Edge.From,
                hub,
                external);
            NodeMembership to = Membership(
                row.Edge.To,
                hub,
                external);

            if (from == NodeMembership.Hub
                && request.Direction.HasFlag(
                    ExternalFocusedCallGraphDirection.Outgoing))
            {
                AddBoundary(
                    row,
                    to,
                    row.Edge.From,
                    outgoingPaths,
                    request.Mode,
                    boundaryRowNumbers,
                    connectorRowNumbers,
                    unclassifiedRowNumbers,
                    unclassifiedConnectorRowNumbers);
            }
            if (to == NodeMembership.Hub
                && request.Direction.HasFlag(
                    ExternalFocusedCallGraphDirection.Incoming))
            {
                AddBoundary(
                    row,
                    from,
                    row.Edge.To,
                    incomingPaths,
                    request.Mode,
                    boundaryRowNumbers,
                    connectorRowNumbers,
                    unclassifiedRowNumbers,
                    unclassifiedConnectorRowNumbers);
            }
        }

        return new ExternalFocusedCallGraphProjection(
            source,
            request,
            SelectRows(source, boundaryRowNumbers),
            SelectRows(source, connectorRowNumbers),
            SelectRows(source, unclassifiedRowNumbers),
            SelectRows(source, unclassifiedConnectorRowNumbers));
    }

    static void AddBoundary(
        CallGraphRow row,
        NodeMembership opposite,
        int hubEndpoint,
        IReadOnlyDictionary<int, ImmutableArray<int>> paths,
        ExternalFocusedCallGraphMode mode,
        HashSet<int> boundaryRowNumbers,
        HashSet<int> connectorRowNumbers,
        HashSet<int> unclassifiedRowNumbers,
        HashSet<int> unclassifiedConnectorRowNumbers)
    {
        if (opposite == NodeMembership.Hub)
            return;

        ImmutableArray<int> connector = [];
        if (mode == ExternalFocusedCallGraphMode.SeededConnectors
            && !paths.TryGetValue(hubEndpoint, out connector))
        {
            return;
        }

        if (opposite == NodeMembership.External)
        {
            boundaryRowNumbers.Add(row.Number);
            connectorRowNumbers.UnionWith(connector);
        }
        else
        {
            unclassifiedRowNumbers.Add(row.Number);
            unclassifiedConnectorRowNumbers.UnionWith(connector);
        }
    }

    static Dictionary<int, ImmutableArray<int>> ShortestPathsFromSeeds(
        HubLocalIndex index,
        ImmutableArray<int> seeds)
    {
        Dictionary<int, int> distances = Distances(
            index,
            seeds,
            reverse: false);
        var paths = seeds.ToDictionary(
            static seed => seed,
            static _ => ImmutableArray<int>.Empty);
        foreach ((int node, int distance) in distances
            .Where(static item => item.Value > 0)
            .OrderBy(static item => item.Value)
            .ThenBy(static item => item.Key))
        {
            ImmutableArray<int>? best = null;
            foreach (CallGraphRow row in index.Incoming(node))
            {
                if (!distances.TryGetValue(
                        row.Edge.From,
                        out int predecessorDistance)
                    || predecessorDistance != distance - 1)
                {
                    continue;
                }

                ImmutableArray<int> candidate =
                    paths[row.Edge.From].Add(row.Number);
                if (best is null
                    || CompareRowSequences(candidate, best.Value) < 0)
                {
                    best = candidate;
                }
            }

            if (best is not null)
                paths.Add(node, best.Value);
        }

        return paths;
    }

    static Dictionary<int, ImmutableArray<int>> ShortestPathsToSeeds(
        HubLocalIndex index,
        ImmutableArray<int> seeds)
    {
        Dictionary<int, int> distances = Distances(
            index,
            seeds,
            reverse: true);
        var paths = seeds.ToDictionary(
            static seed => seed,
            static _ => ImmutableArray<int>.Empty);
        foreach ((int node, int distance) in distances
            .Where(static item => item.Value > 0)
            .OrderBy(static item => item.Value)
            .ThenBy(static item => item.Key))
        {
            ImmutableArray<int>? best = null;
            foreach (CallGraphRow row in index.Outgoing(node))
            {
                if (!distances.TryGetValue(
                        row.Edge.To,
                        out int successorDistance)
                    || successorDistance != distance - 1)
                {
                    continue;
                }

                ImmutableArray<int> candidate =
                    [row.Number, .. paths[row.Edge.To]];
                if (best is null
                    || CompareRowSequences(candidate, best.Value) < 0)
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
        HubLocalIndex index,
        ImmutableArray<int> seeds,
        bool reverse)
    {
        var distances = seeds.ToDictionary(
            static seed => seed,
            static _ => 0);
        var queue = new Queue<int>(seeds);
        while (queue.TryDequeue(out int node))
        {
            int nextDistance = distances[node] + 1;
            IEnumerable<CallGraphRow> rows = reverse
                ? index.Incoming(node)
                : index.Outgoing(node);
            foreach (CallGraphRow row in rows)
            {
                int adjacent = reverse
                    ? row.Edge.From
                    : row.Edge.To;
                if (distances.ContainsKey(adjacent))
                    continue;

                distances.Add(adjacent, nextDistance);
                queue.Enqueue(adjacent);
            }
        }

        return distances;
    }

    static int CompareRowSequences(
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

    static ImmutableArray<CallGraphRow> SelectRows(
        CallGraphProjection source,
        HashSet<int> rowNumbers) =>
        [
            .. source.Rows.Where(row =>
                rowNumbers.Contains(row.Number)),
        ];

    static NodeMembership Membership(
        int nodeId,
        HashSet<int> hub,
        HashSet<int> external) =>
        hub.Contains(nodeId)
            ? NodeMembership.Hub
            : external.Contains(nodeId)
                ? NodeMembership.External
                : NodeMembership.Unknown;

    static void ValidateNodeIds(
        CallGraphProjection source,
        ExternalFocusedCallGraphRequest request)
    {
        foreach (int nodeId in request.HubNodeIds
            .Concat(request.ExternalNodeIds)
            .Concat(request.SeedNodeIds))
        {
            if (nodeId >= source.Nodes.Length)
            {
                throw new ArgumentException(
                    $"Node id {nodeId} does not belong to the source call graph.",
                    nameof(request));
            }
        }
    }

    enum NodeMembership
    {
        Unknown,
        Hub,
        External,
    }

    sealed class HubLocalIndex
    {
        readonly IReadOnlyDictionary<
            int,
            ImmutableArray<CallGraphRow>> _outgoing;
        readonly IReadOnlyDictionary<
            int,
            ImmutableArray<CallGraphRow>> _incoming;

        HubLocalIndex(
            IReadOnlyDictionary<
                int,
                ImmutableArray<CallGraphRow>> outgoing,
            IReadOnlyDictionary<
                int,
                ImmutableArray<CallGraphRow>> incoming)
        {
            _outgoing = outgoing;
            _incoming = incoming;
        }

        internal ImmutableArray<CallGraphRow> Outgoing(int node) =>
            _outgoing.TryGetValue(node, out var rows)
                ? rows
                : [];

        internal ImmutableArray<CallGraphRow> Incoming(int node) =>
            _incoming.TryGetValue(node, out var rows)
                ? rows
                : [];

        internal static HubLocalIndex Create(
            CallGraphProjection source,
            HashSet<int> hub)
        {
            var outgoing =
                new Dictionary<int, List<CallGraphRow>>();
            var incoming =
                new Dictionary<int, List<CallGraphRow>>();
            foreach (CallGraphRow row in source.Rows)
            {
                if (!hub.Contains(row.Edge.From)
                    || !hub.Contains(row.Edge.To))
                {
                    continue;
                }

                Add(outgoing, row.Edge.From, row);
                Add(incoming, row.Edge.To, row);
            }

            return new(
                Freeze(outgoing),
                Freeze(incoming));
        }

        static void Add(
            Dictionary<int, List<CallGraphRow>> index,
            int node,
            CallGraphRow row)
        {
            if (!index.TryGetValue(node, out List<CallGraphRow>? rows))
            {
                rows = [];
                index.Add(node, rows);
            }
            rows.Add(row);
        }

        static Dictionary<int, ImmutableArray<CallGraphRow>> Freeze(
            Dictionary<int, List<CallGraphRow>> source) =>
            source.ToDictionary(
                static item => item.Key,
                static item => item.Value.ToImmutableArray());
    }
}
