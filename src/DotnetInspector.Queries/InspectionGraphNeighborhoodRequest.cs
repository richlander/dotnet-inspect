using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Which semantic endpoint roles a neighborhood may traverse.</summary>
public enum InspectionGraphTraversalDirection
{
    Outgoing,
    Incoming,
    Both,
}

/// <summary>
/// A finite relationship neighborhood around one typed seed.
/// </summary>
public sealed class InspectionGraphNeighborhoodRequest
{
    InspectionGraphNeighborhoodRequest(
        InspectionGraphSubject seed,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        InspectionGraphTraversalDirection direction,
        int maxDepth)
    {
        ArgumentNullException.ThrowIfNull(seed);
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

        ModeRequest = InspectionGraphModeRequest.SingleSeed(seed);
        Direction = direction;
        MaxDepth = maxDepth;
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
                + $"Selected relationships: {ids}.");
        }
    }

    public InspectionGraphModeRequest ModeRequest { get; }
    public InspectionGraphSubject Seed => ModeRequest.Seeds[0];
    public ImmutableArray<InspectionGraphRelationshipDescriptor>
        Relationships { get; }
    public InspectionGraphTraversalDirection Direction { get; }
    public int MaxDepth { get; }

    public static InspectionGraphNeighborhoodRequest SingleSeed(
        InspectionGraphSubject seed,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships,
        InspectionGraphTraversalDirection direction,
        int maxDepth) =>
        new(seed, relationships, direction, maxDepth);

    internal bool Includes(InspectionGraphEndpointRole role) =>
        Direction switch
        {
            InspectionGraphTraversalDirection.Outgoing =>
                role == InspectionGraphEndpointRole.Source,
            InspectionGraphTraversalDirection.Incoming =>
                role == InspectionGraphEndpointRole.Target,
            InspectionGraphTraversalDirection.Both =>
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
        IReadOnlyDictionary<int, ImmutableArray<InspectionGraphEdge>>
            outgoingEdges = IndexEdges(
                selectedEdges,
                static edge => edge.FromNodeId);
        IReadOnlyDictionary<int, ImmutableArray<InspectionGraphEdge>>
            incomingEdges = IndexEdges(
                selectedEdges,
                static edge => edge.ToNodeId);
        IReadOnlyDictionary<InspectionGraphSubject, InspectionGraphNode>
            nodesBySubject = source.Nodes.ToDictionary(
                static node => node.Subject);
        var retainedEdgeIds = new HashSet<int>();
        var retainedNodeIds = new HashSet<int>();
        var queue = new Queue<(int NodeId, int Depth)>();
        var nodeDepths = new Dictionary<int, int>();
        InspectionGraphSeed sourceSeed = AssertSingleSeed(source, request);
        RetainTarget(
            sourceSeed.Target,
            retainedNodeIds,
            retainedGroupIds: null);
        if (sourceSeed.Target.Kind == InspectionGraphTargetKind.Node)
            nodeDepths[sourceSeed.Target.Id] = 0;

        if (request.MaxDepth > 0)
        {
            foreach (InspectionGraphEdge edge in selectedEdges)
            {
                foreach (InspectionGraphSeedAdmission admission
                    in edge.Relationship.GetSeedAdmissions(
                        request.Seed.Kind))
                {
                    if (!request.Includes(admission.Role)
                        || !AdmissionMatches(
                            source,
                            nodesBySubject,
                            edge,
                            request.Seed,
                            admission))
                    {
                        continue;
                    }

                    RetainEdge(edge, retainedEdgeIds, retainedNodeIds);
                    int nextNodeId =
                        admission.Role == InspectionGraphEndpointRole.Source
                            ? edge.ToNodeId
                            : edge.FromNodeId;
                    Enqueue(nextNodeId, 1, nodeDepths, queue);
                }
            }
        }

        while (queue.TryDequeue(out (int NodeId, int Depth) item))
        {
            if (item.Depth >= request.MaxDepth)
                continue;

            if (request.Includes(InspectionGraphEndpointRole.Source)
                && outgoingEdges.TryGetValue(
                    item.NodeId,
                    out ImmutableArray<InspectionGraphEdge> outgoing))
            {
                foreach (InspectionGraphEdge edge in outgoing)
                {
                    RetainEdge(edge, retainedEdgeIds, retainedNodeIds);
                    Enqueue(
                        edge.ToNodeId,
                        item.Depth + 1,
                        nodeDepths,
                        queue);
                }
            }
            if (request.Includes(InspectionGraphEndpointRole.Target)
                && incomingEdges.TryGetValue(
                    item.NodeId,
                    out ImmutableArray<InspectionGraphEdge> incoming))
            {
                foreach (InspectionGraphEdge edge in incoming)
                {
                    RetainEdge(edge, retainedEdgeIds, retainedNodeIds);
                    Enqueue(
                        edge.FromNodeId,
                        item.Depth + 1,
                        nodeDepths,
                        queue);
                }
            }
        }

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
        RetainTarget(
            sourceSeed.Target,
            retainedNodeIds: null,
            retainedGroupIds: retainedGroupIds);
        foreach (int nodeId in retainedNodeIds)
        {
            retainedGroupIds.UnionWith(
                source.Nodes[nodeId].GroupIds);
        }
        RetainGroupParents(source, retainedGroupIds);

        Dictionary<int, int> groupIds = DenseMap(retainedGroupIds);
        Dictionary<int, int> nodeIds = DenseMap(retainedNodeIds);
        Dictionary<int, int> occurrenceIds =
            DenseMap(retainedOccurrenceIds);
        Dictionary<int, int> edgeIds = DenseMap(retainedEdgeIds);

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
                RemapCharacteristic(
                    characteristic,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds))
                .Where(static characteristic =>
                    characteristic is not null)
                .Select(static characteristic => characteristic!),
        ];
        InspectionGraphSeed seed = new(
            sourceSeed.Subject,
            RemapTarget(
                sourceSeed.Target,
                nodeIds,
                groupIds,
                edgeIds,
                occurrenceIds)
                ?? throw new InspectionQueryException(
                    "The neighborhood seed target was not retained."),
            sourceSeed.Role);
        InspectionGraphLimit[] limits =
        [
            .. source.Limits.Select(limit =>
                RemapLimit(
                    limit,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds))
                .Where(static limit => limit is not null)
                .Select(static limit => limit!),
            new(
                InspectionGraphNeighborhoodCatalog.DepthBound,
                seed.Target,
                new InspectionGraphNeighborhoodDepthBoundEvidence(
                    request.MaxDepth)),
        ];
        InspectionGraphFailure[] failures =
        [
            .. source.Failures.Select(failure =>
                RemapFailure(
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
            [seed],
            limits,
            failures);
    }

    static IReadOnlyDictionary<int, ImmutableArray<InspectionGraphEdge>>
        IndexEdges(
            IEnumerable<InspectionGraphEdge> edges,
            Func<InspectionGraphEdge, int> getNodeId) =>
        edges.GroupBy(getNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray());

    static InspectionGraphSeed AssertSingleSeed(
        InspectionGraphDocument source,
        InspectionGraphNeighborhoodRequest request)
    {
        InspectionGraphSeed seed = source.Seeds.SingleOrDefault()
            ?? throw new InspectionQueryException(
                "A single-seed neighborhood requires one bound seed.");
        if (seed.Subject != request.Seed
            || seed.Role != InspectionGraphSeedRole.Primary)
        {
            throw new InspectionQueryException(
                "The source document seed does not match the neighborhood request.");
        }
        return seed;
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
                    OccurrenceEndpoint(
                        source.Occurrences[id],
                        admission.Role)
                    == seed),
            InspectionGraphSeedAdmissionKind.OwnedSubjects =>
                StrictlyOwns(
                    source,
                    nodesBySubject,
                    seed,
                    edgeEndpoint)
                || edge.OccurrenceIds.Any(id =>
                    StrictlyOwns(
                        source,
                        nodesBySubject,
                        seed,
                        OccurrenceEndpoint(
                            source.Occurrences[id],
                            admission.Role))),
            _ => throw new ArgumentOutOfRangeException(nameof(admission)),
        };
    }

    static InspectionGraphSubject OccurrenceEndpoint(
        InspectionGraphOccurrence occurrence,
        InspectionGraphEndpointRole role) =>
        role == InspectionGraphEndpointRole.Source
            ? occurrence.SourceSubject
            : occurrence.TargetSubject;

    static bool StrictlyOwns(
        InspectionGraphDocument source,
        IReadOnlyDictionary<
            InspectionGraphSubject,
            InspectionGraphNode> nodesBySubject,
        InspectionGraphSubject owner,
        InspectionGraphSubject subject)
    {
        if (owner.Kind == subject.Kind)
            return false;
        if (owner is InspectionGraphSubject.PackageSubject package)
        {
            return nodesBySubject.TryGetValue(
                    subject,
                    out InspectionGraphNode? node)
                && node.GroupIds.Any(groupId =>
                    source.Groups[groupId].Subject == package);
        }
        if (!TryGetRegistration(owner, out var ownerRegistration)
            || !TryGetRegistration(subject, out var subjectRegistration)
            || !ReferenceEquals(
                ownerRegistration,
                subjectRegistration))
        {
            return false;
        }

        return owner switch
        {
            InspectionGraphSubject.AssemblySubject =>
                subject.Kind is InspectionGraphSubjectKind.Type
                    or InspectionGraphSubjectKind.Member,
            InspectionGraphSubject.TypeSubject
                {
                    Identity:
                        InspectionGraphTypeIdentity.AcquiredDefinition
                        ownerType,
                } when subject is InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.AcquiredApi member,
                } =>
                string.Equals(
                    ownerType.Type.ToMetadataFullName(),
                    member.Member.TypeFullName,
                    StringComparison.Ordinal),
            _ => false,
        };
    }

    static bool TryGetRegistration(
        InspectionGraphSubject subject,
        out AssemblyAcquisitionRegistration? registration)
    {
        registration = subject switch
        {
            InspectionGraphSubject.MemberSubject
            {
                Identity:
                    InspectionGraphMemberIdentity.AcquiredApi acquired,
            } => acquired.Registration,
            InspectionGraphSubject.TypeSubject
            {
                Identity:
                    InspectionGraphTypeIdentity.AcquiredDefinition acquired,
            } => acquired.Registration,
            InspectionGraphSubject.AssemblySubject
            {
                Identity:
                    InspectionGraphAssemblyIdentity.Acquired acquired,
            } => acquired.Registration,
            _ => null,
        };
        return registration is not null;
    }

    static void RetainEdge(
        InspectionGraphEdge edge,
        HashSet<int> retainedEdgeIds,
        HashSet<int> retainedNodeIds)
    {
        retainedEdgeIds.Add(edge.Id);
        retainedNodeIds.Add(edge.FromNodeId);
        retainedNodeIds.Add(edge.ToNodeId);
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

    static void RetainGroupParents(
        InspectionGraphDocument source,
        HashSet<int> retainedGroupIds)
    {
        int[] initial = [.. retainedGroupIds];
        foreach (int id in initial)
        {
            int? parentId = source.Groups[id].ParentId;
            while (parentId is int parent)
            {
                retainedGroupIds.Add(parent);
                parentId = source.Groups[parent].ParentId;
            }
        }
    }

    static Dictionary<int, int> DenseMap(
        HashSet<int> retainedIds) =>
        retainedIds.Order().Select((id, index) => (id, index))
            .ToDictionary(static item => item.id, static item => item.index);

    static InspectionGraphCharacteristic? RemapCharacteristic(
        InspectionGraphCharacteristic characteristic,
        IReadOnlyDictionary<int, int> nodeIds,
        IReadOnlyDictionary<int, int> groupIds,
        IReadOnlyDictionary<int, int> edgeIds,
        IReadOnlyDictionary<int, int> occurrenceIds)
    {
        InspectionGraphTarget? target = RemapTarget(
            characteristic.Target,
            nodeIds,
            groupIds,
            edgeIds,
            occurrenceIds);
        if (target is null)
            return null;

        InspectionGraphTarget?[] sources =
        [
            .. characteristic.Derivation.Sources.Select(source =>
                RemapTarget(
                    source,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds)),
        ];
        if (sources.Any(static source => source is null))
            return null;

        return new InspectionGraphCharacteristic(
            characteristic.Descriptor,
            target.Value,
            characteristic.Value,
            new InspectionGraphCharacteristicDerivation(
                characteristic.Derivation.Kind,
                sources.Select(static source => source!.Value)));
    }

    static InspectionGraphLimit? RemapLimit(
        InspectionGraphLimit limit,
        IReadOnlyDictionary<int, int> nodeIds,
        IReadOnlyDictionary<int, int> groupIds,
        IReadOnlyDictionary<int, int> edgeIds,
        IReadOnlyDictionary<int, int> occurrenceIds)
    {
        if (limit.Target is not { } sourceTarget)
            return limit;
        InspectionGraphTarget? target = RemapTarget(
            sourceTarget,
            nodeIds,
            groupIds,
            edgeIds,
            occurrenceIds);
        return target is null
            ? null
            : new InspectionGraphLimit(
                limit.Descriptor,
                target,
                limit.Evidence);
    }

    static InspectionGraphFailure? RemapFailure(
        InspectionGraphFailure failure,
        IReadOnlyDictionary<int, int> nodeIds,
        IReadOnlyDictionary<int, int> groupIds,
        IReadOnlyDictionary<int, int> edgeIds,
        IReadOnlyDictionary<int, int> occurrenceIds)
    {
        if (failure.Target is not { } sourceTarget)
            return failure;
        InspectionGraphTarget? target = RemapTarget(
            sourceTarget,
            nodeIds,
            groupIds,
            edgeIds,
            occurrenceIds);
        return target is null
            ? null
            : new InspectionGraphFailure(
                failure.Descriptor,
                target,
                failure.Evidence);
    }

    static InspectionGraphTarget? RemapTarget(
        InspectionGraphTarget target,
        IReadOnlyDictionary<int, int> nodeIds,
        IReadOnlyDictionary<int, int> groupIds,
        IReadOnlyDictionary<int, int> edgeIds,
        IReadOnlyDictionary<int, int> occurrenceIds)
    {
        IReadOnlyDictionary<int, int> ids = target.Kind switch
        {
            InspectionGraphTargetKind.Node => nodeIds,
            InspectionGraphTargetKind.Group => groupIds,
            InspectionGraphTargetKind.Edge => edgeIds,
            InspectionGraphTargetKind.Occurrence => occurrenceIds,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        if (!ids.TryGetValue(target.Id, out int id))
            return null;
        return target.Kind switch
        {
            InspectionGraphTargetKind.Node =>
                InspectionGraphTarget.Node(id),
            InspectionGraphTargetKind.Group =>
                InspectionGraphTarget.Group(id),
            InspectionGraphTargetKind.Edge =>
                InspectionGraphTarget.Edge(id),
            InspectionGraphTargetKind.Occurrence =>
                InspectionGraphTarget.Occurrence(id),
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
    }
}
