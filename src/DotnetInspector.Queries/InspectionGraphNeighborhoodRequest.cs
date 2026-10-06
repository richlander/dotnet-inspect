using System.Collections.Immutable;
using Inspector.Graph;

namespace DotnetInspector.Queries;

/// <summary>
/// A finite relationship neighborhood around one or more typed seeds.
/// </summary>
public sealed class InspectionGraphNeighborhoodRequest
{
    InspectionGraphNeighborhoodRequest(
        InspectionGraphModeRequest modeRequest,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        GraphTraversalDirection direction,
        int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(modeRequest);
        if (modeRequest.Mode is not InspectionGraphMode.SingleSeed
            and not InspectionGraphMode.PeerSeeds)
        {
            throw new ArgumentException(
                "A neighborhood requires single-seed or peer-seed mode.",
                nameof(modeRequest));
        }
        InspectionGraphCollections.RequireDefined(direction, nameof(direction));
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        Relationships = InspectionGraphCollections.Snapshot(
            relationships,
            nameof(relationships));
        if (Relationships.IsEmpty)
        {
            throw new ArgumentException(
                "A neighborhood requires at least one relationship.",
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

        ModeRequest = modeRequest;
        Direction = direction;
        MaxDepth = maxDepth;
        string modeName = modeRequest.Mode switch
        {
            InspectionGraphMode.SingleSeed => "single seed",
            InspectionGraphMode.PeerSeeds => "peer seeds",
            _ => throw new ArgumentOutOfRangeException(nameof(modeRequest)),
        };
        foreach (InspectionGraphSubject seed in modeRequest.Seeds)
        {
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
                    + $"{seed.Kind.ToString().ToLowerInvariant()} seed in the "
                    + $"{direction.ToString().ToLowerInvariant()} direction. "
                    + $"Seed mode: {modeName}; "
                    + $"selected relationships: {ids}.");
            }
        }
    }

    public InspectionGraphModeRequest ModeRequest { get; }
    public ImmutableArray<InspectionGraphSubject> Seeds =>
        ModeRequest.Seeds;
    public ImmutableArray<InspectionGraphRelationshipDescriptor>
        Relationships
    { get; }
    public GraphTraversalDirection Direction { get; }
    public int MaxDepth { get; }

    public static InspectionGraphNeighborhoodRequest SingleSeed(
        InspectionGraphSubject seed,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        GraphTraversalDirection direction,
        int maxDepth) =>
        new(
            InspectionGraphModeRequest.SingleSeed(seed),
            relationships,
            direction,
            maxDepth);

    public static InspectionGraphNeighborhoodRequest PeerSeeds(
        IEnumerable<InspectionGraphSubject> seeds,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        GraphTraversalDirection direction,
        int maxDepth) =>
        new(
            InspectionGraphModeRequest.PeerSeeds(seeds),
            relationships,
            direction,
            maxDepth);

    internal bool Includes(InspectionGraphEndpointRole role) =>
        Direction switch
        {
            GraphTraversalDirection.Outgoing =>
                role == InspectionGraphEndpointRole.Source,
            GraphTraversalDirection.Incoming =>
                role == InspectionGraphEndpointRole.Target,
            GraphTraversalDirection.Both =>
                true,
            _ => throw new ArgumentOutOfRangeException(nameof(Direction)),
        };
}

/// <summary>Neighborhood-owned graph contracts.</summary>
public static class InspectionGraphNeighborhoodCatalog
{
    public static InspectionGraphEvidenceDescriptor DepthBoundEvidence
    { get; } =
        new("queries.neighborhood-depth-bound", InspectionGraphOwner.Queries);

    public static InspectionGraphLimitDescriptor DepthBound { get; } =
        new(
            "queries.neighborhood-depth-bound",
            InspectionGraphOwner.Queries,
            [DepthBoundEvidence]);
}

/// <summary>The requested maximum number of traversed relationship edges.</summary>
public sealed record InspectionGraphNeighborhoodDepthBoundEvidence
    : IInspectionGraphDiagnosticEvidence
{
    public InspectionGraphNeighborhoodDepthBoundEvidence(int maxDepth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        MaxDepth = maxDepth;
    }

    public int MaxDepth { get; }

    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphNeighborhoodCatalog.DepthBoundEvidence;
}

internal static class InspectionGraphNeighborhoodProjection
{
    internal static InspectionGraphDocument Project(
        InspectionGraphDocument source,
        InspectionGraphNeighborhoodRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(source.ModeRequest, request.ModeRequest))
        {
            throw new ArgumentException(
                "The source document must use the neighborhood's mode request.",
                nameof(source));
        }

        var selectedRelationships =
            request.Relationships.ToHashSet();
        InspectionGraphEdge[] selectedEdges =
        [
            .. source.Edges.Where(edge =>
                selectedRelationships.Contains(edge.Relationship)),
        ];
        IReadOnlyDictionary<InspectionGraphSubject, InspectionGraphNode>
            nodesBySubject = source.Nodes.ToDictionary(
                static node => node.Subject);
        var retainedNodeIds = new HashSet<int>();
        var entries = new HashSet<GraphTraversalEntry>();
        ImmutableArray<InspectionGraphSeed> sourceSeeds =
            AssertSeeds(source, request);
        foreach (InspectionGraphSeed sourceSeed in sourceSeeds)
        {
            RetainTarget(
                sourceSeed.Target,
                retainedNodeIds,
                retainedGroupIds: null);
        }

        if (request.MaxDepth > 0)
        {
            foreach (InspectionGraphSeed sourceSeed in sourceSeeds)
            {
                foreach (InspectionGraphEdge edge in selectedEdges)
                {
                    foreach (InspectionGraphSeedAdmission admission
                        in edge.Relationship.GetSeedAdmissions(
                            sourceSeed.Subject.Kind))
                    {
                        if (!request.Includes(admission.Role)
                            || !AdmissionMatches(
                                source,
                                nodesBySubject,
                                edge,
                                sourceSeed.Subject,
                                admission))
                        {
                            continue;
                        }

                        int nextNodeId =
                            admission.Role
                                == InspectionGraphEndpointRole.Source
                                    ? edge.ToNodeId
                                    : edge.FromNodeId;
                        entries.Add(
                            new GraphTraversalEntry(
                                edge.Id,
                                nextNodeId));
                    }
                }
            }
        }

        GraphNeighborhoodResult execution =
            GraphDocumentExecution.Neighborhood(
                source.Structure,
                new GraphNeighborhoodPlan<
                    InspectionGraphRelationshipDescriptor>(
                    request.Relationships,
                    request.Direction,
                    request.MaxDepth,
                    rootNodeIds: [],
                    [.. entries],
                    anchorNodeIds:
                    [
                        .. sourceSeeds
                        .Where(seed =>
                            seed.Target.Kind
                                == InspectionGraphTargetKind.Node)
                        .Select(static seed => seed.Target.Id),
                    ]));
        retainedNodeIds.UnionWith(execution.NodeIds);
        var retainedEdgeIds = execution.EdgeIds.ToHashSet();

        foreach (InspectionGraphFailure failure in source.Failures)
        {
            if (failure.Target is
                {
                    Kind: InspectionGraphTargetKind.Node,
                } target)
            {
                retainedNodeIds.Add(target.Id);
            }
        }

        var retainedOccurrenceIds = retainedEdgeIds
            .SelectMany(id => source.Edges[id].OccurrenceIds)
            .ToHashSet();
        if (retainedOccurrenceIds.Any(id =>
            !source.Occurrences[id].DerivedFromOccurrenceIds.IsEmpty))
        {
            throw new InspectionQueryException(
                "Neighborhood projection does not yet support derived occurrence receipts.");
        }

        var retainedGroupIds = new HashSet<int>();
        foreach (InspectionGraphSeed sourceSeed in sourceSeeds)
        {
            RetainTarget(
                sourceSeed.Target,
                retainedNodeIds: null,
                retainedGroupIds: retainedGroupIds);
        }
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
        InspectionGraphCharacteristic[] characteristics =
        [
            .. source.Characteristics.Select(characteristic =>
                InspectionGraphProjectionUtilities.RemapCharacteristic(
                    characteristic,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds))
                .Where(static characteristic =>
                    characteristic is not null)
                .Select(static characteristic => characteristic!),
        ];
        InspectionGraphSeed[] seeds =
        [
            .. sourceSeeds.Select(sourceSeed =>
                new InspectionGraphSeed(
                    sourceSeed.Subject,
                    InspectionGraphProjectionUtilities.RemapTarget(
                        sourceSeed.Target,
                        nodeIds,
                        groupIds,
                        edgeIds,
                        occurrenceIds)
                        ?? throw new InspectionQueryException(
                            "A neighborhood seed target was not retained."),
                    sourceSeed.Role)),
        ];
        InspectionGraphLimit[] limits =
        [
            .. source.Limits.Select(limit =>
                InspectionGraphProjectionUtilities.RemapLimit(
                    limit,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds))
                .Where(static limit => limit is not null)
                .Select(static limit => limit!),
            .. seeds.Select(seed =>
                new InspectionGraphLimit(
                    new InspectionGraphLimitPayload(
                        InspectionGraphNeighborhoodCatalog.DepthBound,
                        new InspectionGraphNeighborhoodDepthBoundEvidence(
                            request.MaxDepth)),
                    seed.Target)),
        ];
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
            source.Scope,
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

    static ImmutableArray<InspectionGraphSeed> AssertSeeds(
        InspectionGraphDocument source,
        InspectionGraphNeighborhoodRequest request)
    {
        InspectionGraphSeedRole expectedRole =
            request.ModeRequest.Mode == InspectionGraphMode.SingleSeed
                ? InspectionGraphSeedRole.Primary
                : InspectionGraphSeedRole.Peer;
        if (source.Seeds.Length != request.Seeds.Length
            || !source.Seeds.Select(static seed => seed.Subject)
                .SequenceEqual(request.Seeds)
            || source.Seeds.Any(seed => seed.Role != expectedRole))
        {
            throw new InspectionQueryException(
                "The source document seeds do not match the neighborhood request.");
        }
        return source.Seeds;
    }

    static bool AdmissionMatches(
        InspectionGraphDocument source,
        IReadOnlyDictionary<
            InspectionGraphSubject,
            InspectionGraphNode> nodesBySubject,
        InspectionGraphEdge edge,
        InspectionGraphSubject seed,
        InspectionGraphSeedAdmission admission)
    {
        InspectionGraphSubject edgeEndpoint =
            admission.Role == InspectionGraphEndpointRole.Source
                ? source.Nodes[edge.FromNodeId].Subject
                : source.Nodes[edge.ToNodeId].Subject;
        return admission.Kind switch
        {
            InspectionGraphSeedAdmissionKind.EdgeEndpoint =>
                edgeEndpoint == seed,
            InspectionGraphSeedAdmissionKind.OccurrenceEndpoint =>
                edge.OccurrenceIds.Any(id =>
                    InspectionGraphProjectionUtilities.OccurrenceEndpoint(
                        source.Occurrences[id],
                        admission.Role)
                    == seed),
            InspectionGraphSeedAdmissionKind.OwnedSubjects =>
                InspectionGraphProjectionUtilities.StrictlyOwns(
                    source,
                    nodesBySubject,
                    seed,
                    edgeEndpoint)
                || edge.OccurrenceIds.Any(id =>
                    InspectionGraphProjectionUtilities.StrictlyOwns(
                        source,
                        nodesBySubject,
                        seed,
                        InspectionGraphProjectionUtilities.OccurrenceEndpoint(
                            source.Occurrences[id],
                            admission.Role))),
            _ => throw new ArgumentOutOfRangeException(nameof(admission)),
        };
    }

    static void RetainTarget(
        InspectionGraphTarget target,
        HashSet<int>? retainedNodeIds,
        HashSet<int>? retainedGroupIds)
    {
        if (target.Kind == InspectionGraphTargetKind.Node)
            retainedNodeIds?.Add(target.Id);
        else if (target.Kind == InspectionGraphTargetKind.Group)
            retainedGroupIds?.Add(target.Id);
    }
}
