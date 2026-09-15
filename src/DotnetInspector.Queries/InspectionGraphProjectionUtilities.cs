using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal static class InspectionGraphProjectionUtilities
{
    internal static InspectionGraphSubject OccurrenceEndpoint(
        InspectionGraphOccurrence occurrence,
        InspectionGraphEndpointRole role) =>
        role == InspectionGraphEndpointRole.Source
            ? occurrence.SourceSubject
            : occurrence.TargetSubject;

    internal static bool AdmitsEndpoint(
        InspectionGraphDocument source,
        IReadOnlyDictionary<
            InspectionGraphSubject,
            InspectionGraphNode> nodesBySubject,
        IReadOnlyList<InspectionGraphSubject> inputSubjects,
        InspectionGraphEdge edge,
        InspectionGraphOccurrence occurrence,
        InspectionGraphEndpointRole role)
    {
        InspectionGraphSubject edgeEndpoint =
            role == InspectionGraphEndpointRole.Source
                ? source.Nodes[edge.FromNodeId].Subject
                : source.Nodes[edge.ToNodeId].Subject;
        InspectionGraphSubject occurrenceEndpoint =
            OccurrenceEndpoint(occurrence, role);
        return inputSubjects.Any(subject =>
            subject == edgeEndpoint
            || subject == occurrenceEndpoint
            || StrictlyOwns(
                source,
                nodesBySubject,
                subject,
                edgeEndpoint)
            || StrictlyOwns(
                source,
                nodesBySubject,
                subject,
                occurrenceEndpoint));
    }

    internal static bool StrictlyOwns(
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
        if (owner is InspectionGraphSubject.AssemblySubject
            {
                Identity:
                    InspectionGraphAssemblyIdentity.CensusParticipant
                    assembly,
            })
        {
            return subject switch
            {
                InspectionGraphSubject.TypeSubject
                {
                    Identity:
                        InspectionGraphTypeIdentity.CensusType type,
                } =>
                    assembly.Participant.Equals(
                        type.Identity.Participant),
                InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.CensusMember
                        assemblyMember,
                } =>
                    assembly.Participant.Equals(
                        assemblyMember.Source.Participant),
                _ => false,
            };
        }
        if (owner is InspectionGraphSubject.TypeSubject
            {
                Identity:
                    InspectionGraphTypeIdentity.CensusType
                    integrationOwnerType,
            }
            && subject is InspectionGraphSubject.MemberSubject
            {
                Identity:
                    InspectionGraphMemberIdentity.CensusMember
                    integrationMember,
            })
        {
            return integrationOwnerType.Identity.Participant.Equals(
                    integrationMember.Source.Participant)
                && integrationOwnerType.Identity.Type.Equals(
                    integrationMember.Source.SourceType);
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
                ownerType.Type.Equals(member.DeclaringType),
            _ => false,
        };
    }

    internal static void RetainGroupParents(
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

    internal static Dictionary<int, int> DenseMap(
        HashSet<int> retainedIds) =>
        retainedIds.Order().Select((id, index) => (id, index))
            .ToDictionary(static item => item.id, static item => item.index);

    internal static InspectionGraphCharacteristic? RemapCharacteristic(
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

    internal static InspectionGraphLimit? RemapLimit(
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

    internal static InspectionGraphFailure? RemapFailure(
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

    internal static InspectionGraphTarget? RemapTarget(
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
}
