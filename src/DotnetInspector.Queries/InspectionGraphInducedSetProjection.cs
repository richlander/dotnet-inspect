using System.Collections.Immutable;

namespace DotnetInspector.Queries;

internal static class InspectionGraphInducedSetProjection
{
    internal static InspectionGraphDocument Project(
        InspectionGraphDocument source,
        InspectionGraphInducedSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (source.ModeRequest.Mode != InspectionGraphMode.InducedSet)
        {
            throw new ArgumentException(
                "The source document must use induced-set mode.",
                nameof(source));
        }
        if (request.AdmissionRule
                != InspectionGraphInducedSetAdmissionRule
                    .BothEndpointsWithinSubjectClosure)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        if (!source.Seeds.IsEmpty)
        {
            throw new InspectionQueryException(
                "An induced-set source document cannot contain seeds.");
        }

        IReadOnlyDictionary<InspectionGraphSubject, InspectionGraphNode>
            nodesBySubject = source.Nodes.ToDictionary(
                static node => node.Subject);
        var retainedNodeIds = new HashSet<int>();
        var retainedGroupIds = new HashSet<int>();
        foreach (InspectionGraphSubject subject in request.Subjects)
        {
            bool retained = false;
            if (nodesBySubject.TryGetValue(
                    subject,
                    out InspectionGraphNode? node))
            {
                retainedNodeIds.Add(node.Id);
                retained = true;
            }
            foreach (InspectionGraphGroup group in source.Groups.Where(
                group => group.Subject == subject))
            {
                retainedGroupIds.Add(group.Id);
                retained = true;
            }
            if (!retained)
            {
                throw new InspectionQueryException(
                    $"The requested {subject.Kind.ToString().ToLowerInvariant()} "
                    + "subject is not present in this graph. Add the subject "
                    + "to workspace scope or select a relationship and lens "
                    + "that admit it.");
            }
        }

        var selectedRelationships =
            request.Relationships.ToHashSet();
        var retainedEdgeOccurrences =
            new Dictionary<int, ImmutableArray<int>>();
        var retainedOccurrenceIds = new HashSet<int>();
        foreach (InspectionGraphEdge edge in source.Edges)
        {
            if (!selectedRelationships.Contains(edge.Relationship))
                continue;

            ImmutableArray<int> admittedOccurrences =
            [
                .. edge.OccurrenceIds.Where(id =>
                    InspectionGraphProjectionUtilities.AdmitsEndpoint(
                        source,
                        nodesBySubject,
                        request.Subjects,
                        edge,
                        source.Occurrences[id],
                        InspectionGraphEndpointRole.Source)
                    && InspectionGraphProjectionUtilities.AdmitsEndpoint(
                        source,
                        nodesBySubject,
                        request.Subjects,
                        edge,
                        source.Occurrences[id],
                        InspectionGraphEndpointRole.Target)),
            ];
            if (admittedOccurrences.IsEmpty)
                continue;

            retainedEdgeOccurrences.Add(edge.Id, admittedOccurrences);
            retainedOccurrenceIds.UnionWith(admittedOccurrences);
            retainedNodeIds.Add(edge.FromNodeId);
            retainedNodeIds.Add(edge.ToNodeId);
        }

        foreach (InspectionGraphFailure failure in source.Failures)
        {
            if (failure.Target is
                {
                    Kind: InspectionGraphTargetKind.Node,
                } target
                && RelatedToSubjectClosure(
                    source,
                    nodesBySubject,
                    request.Subjects,
                    source.Nodes[target.Id].Subject))
            {
                retainedNodeIds.Add(target.Id);
            }
            else if (failure.Target is
                {
                    Kind: InspectionGraphTargetKind.Group,
                } groupTarget
                && request.Subjects.Contains(
                    source.Groups[groupTarget.Id].Subject))
            {
                retainedGroupIds.Add(groupTarget.Id);
            }
        }

        if (retainedOccurrenceIds.Any(id =>
            !source.Occurrences[id].DerivedFromOccurrenceIds.IsEmpty))
        {
            throw new InspectionQueryException(
                "Explicit induced-set projection does not yet support derived occurrence receipts.");
        }

        foreach (int nodeId in retainedNodeIds)
        {
            retainedGroupIds.UnionWith(
                source.Nodes[nodeId].GroupIds);
        }
        InspectionGraphProjectionUtilities.RetainGroupParents(
            source,
            retainedGroupIds);

        var retainedEdgeIds =
            retainedEdgeOccurrences.Keys.ToHashSet();
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
                    retainedEdgeOccurrences[id].Select(
                        occurrenceId =>
                            occurrenceIds[occurrenceId]));
            }),
        ];
        InspectionGraphCharacteristic[] characteristics =
        [
            .. source.Characteristics
                .Where(characteristic =>
                    !TargetsPartiallyRetainedEdge(
                        source,
                        characteristic,
                        retainedEdgeOccurrences))
                .Select(characteristic =>
                    InspectionGraphProjectionUtilities
                        .RemapCharacteristic(
                            characteristic,
                            nodeIds,
                            groupIds,
                            edgeIds,
                            occurrenceIds))
                .Where(static characteristic =>
                    characteristic is not null)
                .Select(static characteristic => characteristic!),
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
            new InspectionGraphLimit(
                InspectionGraphInducedSetCatalog.SubjectBound,
                Evidence:
                    new InspectionGraphInducedSubjectBoundEvidence(
                        request.Subjects.Length)),
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
            [],
            limits,
            failures);
    }

    static bool RelatedToSubjectClosure(
        InspectionGraphDocument source,
        IReadOnlyDictionary<
            InspectionGraphSubject,
            InspectionGraphNode> nodesBySubject,
        ImmutableArray<InspectionGraphSubject> inputSubjects,
        InspectionGraphSubject subject) =>
        inputSubjects.Any(input =>
            input == subject
            || InspectionGraphProjectionUtilities.StrictlyOwns(
                source,
                nodesBySubject,
                input,
                subject)
            || InspectionGraphProjectionUtilities.StrictlyOwns(
                source,
                nodesBySubject,
                subject,
                input));

    static bool TargetsPartiallyRetainedEdge(
        InspectionGraphDocument source,
        InspectionGraphCharacteristic characteristic,
        IReadOnlyDictionary<int, ImmutableArray<int>>
            retainedEdgeOccurrences)
    {
        if (characteristic.Target.Kind
                != InspectionGraphTargetKind.Edge
            || !retainedEdgeOccurrences.TryGetValue(
                characteristic.Target.Id,
                out ImmutableArray<int> retained))
        {
            return false;
        }

        return retained.Length
            != source.Edges[characteristic.Target.Id]
                .OccurrenceIds.Length;
    }
}
