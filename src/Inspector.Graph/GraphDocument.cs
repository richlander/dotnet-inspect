using System.Collections.Immutable;

namespace Inspector.Graph;

public enum GraphNodeRole
{
    Unclassified,
    Ordinary,
    External,
    Truncated,
}

public enum GraphTargetKind
{
    Node,
    Group,
    Edge,
    Occurrence,
}

public enum GraphSeedRole
{
    Primary,
    Peer,
}

public enum GraphCharacteristicDerivationKind
{
    Direct,
    Aggregated,
    RolledUp,
    Derived,
}

public enum GraphDocumentScope
{
    SessionBound,
    Portable,
}

public readonly record struct GraphTarget
{
    private readonly bool _initialized;

    private GraphTarget(GraphTargetKind kind, int id)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        Kind = kind;
        Id = id;
        _initialized = true;
    }

    public GraphTargetKind Kind { get; }
    public int Id { get; }

    internal bool IsInitialized => _initialized;

    public static GraphTarget Node(int id) =>
        new(GraphTargetKind.Node, id);

    public static GraphTarget Group(int id) =>
        new(GraphTargetKind.Group, id);

    public static GraphTarget Edge(int id) =>
        new(GraphTargetKind.Edge, id);

    public static GraphTarget Occurrence(int id) =>
        new(GraphTargetKind.Occurrence, id);
}

public sealed class GraphNode<TSubject>
    where TSubject : notnull
{
    public GraphNode(
        int id,
        TSubject subject,
        GraphNodeRole role,
        IEnumerable<int> groupIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        GraphCollections.RequireValue(subject, nameof(subject));
        GraphCollections.RequireDefined(role, nameof(role));
        ImmutableArray<int> groups = GraphCollections.Snapshot(
            groupIds,
            nameof(groupIds));
        if (groups.Any(static groupId => groupId < 0))
            throw new ArgumentOutOfRangeException(nameof(groupIds));
        if (groups.Distinct().Count() != groups.Length)
        {
            throw new ArgumentException(
                "Group ids must be distinct.",
                nameof(groupIds));
        }

        Id = id;
        Subject = subject;
        Role = role;
        GroupIds = groups;
    }

    public int Id { get; }
    public TSubject Subject { get; }
    public GraphNodeRole Role { get; }
    public ImmutableArray<int> GroupIds { get; }
}

public sealed class GraphGroup<TSubject>
    where TSubject : notnull
{
    public GraphGroup(
        int id,
        TSubject subject,
        int? parentId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        if (parentId is not null)
            ArgumentOutOfRangeException.ThrowIfNegative(parentId.Value);
        GraphCollections.RequireValue(subject, nameof(subject));

        Id = id;
        Subject = subject;
        ParentId = parentId;
    }

    public int Id { get; }
    public TSubject Subject { get; }
    public int? ParentId { get; }
}

public sealed class GraphOccurrence<
    TSubject,
    TRelationship,
    TOccurrenceEvidence>
    where TSubject : notnull
    where TRelationship : notnull
    where TOccurrenceEvidence : notnull
{
    public GraphOccurrence(
        int id,
        TRelationship relationship,
        TSubject sourceSubject,
        TSubject targetSubject,
        TOccurrenceEvidence evidence,
        IEnumerable<int> derivedFromOccurrenceIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        GraphCollections.RequireValue(relationship, nameof(relationship));
        GraphCollections.RequireValue(sourceSubject, nameof(sourceSubject));
        GraphCollections.RequireValue(targetSubject, nameof(targetSubject));
        GraphCollections.RequireValue(evidence, nameof(evidence));
        ImmutableArray<int> sourceIds = GraphCollections.Snapshot(
            derivedFromOccurrenceIds,
            nameof(derivedFromOccurrenceIds));
        if (sourceIds.Any(static sourceId => sourceId < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(derivedFromOccurrenceIds));
        }
        if (sourceIds.Distinct().Count() != sourceIds.Length)
        {
            throw new ArgumentException(
                "Derived occurrence ids must be distinct.",
                nameof(derivedFromOccurrenceIds));
        }

        Id = id;
        Relationship = relationship;
        SourceSubject = sourceSubject;
        TargetSubject = targetSubject;
        Evidence = evidence;
        DerivedFromOccurrenceIds = sourceIds;
    }

    public int Id { get; }
    public TRelationship Relationship { get; }
    public TSubject SourceSubject { get; }
    public TSubject TargetSubject { get; }
    public TOccurrenceEvidence Evidence { get; }
    public ImmutableArray<int> DerivedFromOccurrenceIds { get; }
}

public sealed class GraphEdge<TRelationship>
    where TRelationship : notnull
{
    public GraphEdge(
        int id,
        int fromNodeId,
        int toNodeId,
        TRelationship relationship,
        IEnumerable<int> occurrenceIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(id);
        ArgumentOutOfRangeException.ThrowIfNegative(fromNodeId);
        ArgumentOutOfRangeException.ThrowIfNegative(toNodeId);
        GraphCollections.RequireValue(relationship, nameof(relationship));
        ImmutableArray<int> occurrences = GraphCollections.Snapshot(
            occurrenceIds,
            nameof(occurrenceIds));
        if (occurrences.Any(static occurrenceId => occurrenceId < 0))
            throw new ArgumentOutOfRangeException(nameof(occurrenceIds));
        if (occurrences.Distinct().Count() != occurrences.Length)
        {
            throw new ArgumentException(
                "Occurrence ids must be distinct.",
                nameof(occurrenceIds));
        }

        Id = id;
        FromNodeId = fromNodeId;
        ToNodeId = toNodeId;
        Relationship = relationship;
        OccurrenceIds = occurrences;
    }

    public int Id { get; }
    public int FromNodeId { get; }
    public int ToNodeId { get; }
    public TRelationship Relationship { get; }
    public ImmutableArray<int> OccurrenceIds { get; }
}

public sealed class GraphCharacteristicDerivation
{
    public GraphCharacteristicDerivation(
        GraphCharacteristicDerivationKind kind,
        IEnumerable<GraphTarget> sources)
    {
        GraphCollections.RequireDefined(kind, nameof(kind));
        ImmutableArray<GraphTarget> sourceTargets = GraphCollections.Snapshot(
            sources,
            nameof(sources));
        if (sourceTargets.Distinct().Count() != sourceTargets.Length)
        {
            throw new ArgumentException(
                "Derivation sources must be distinct.",
                nameof(sources));
        }
        if (kind == GraphCharacteristicDerivationKind.Direct
            && !sourceTargets.IsEmpty)
        {
            throw new ArgumentException(
                "A direct characteristic cannot cite derivation sources.",
                nameof(sources));
        }
        if (kind != GraphCharacteristicDerivationKind.Direct
            && sourceTargets.IsEmpty)
        {
            throw new ArgumentException(
                "A non-direct characteristic must cite derivation sources.",
                nameof(sources));
        }
        if (kind == GraphCharacteristicDerivationKind.Aggregated
            && sourceTargets.Any(
                static source =>
                    source.Kind != GraphTargetKind.Occurrence))
        {
            throw new ArgumentException(
                "An aggregated characteristic must cite occurrences.",
                nameof(sources));
        }
        if (kind == GraphCharacteristicDerivationKind.RolledUp
            && sourceTargets.Any(
                static source =>
                    source.Kind is not (
                        GraphTargetKind.Node
                        or GraphTargetKind.Group)))
        {
            throw new ArgumentException(
                "A rolled-up characteristic must cite nodes or groups.",
                nameof(sources));
        }

        Kind = kind;
        Sources = sourceTargets;
    }

    public GraphCharacteristicDerivationKind Kind { get; }
    public ImmutableArray<GraphTarget> Sources { get; }
}

public sealed class GraphCharacteristic<TCharacteristic>
    where TCharacteristic : notnull
{
    public GraphCharacteristic(
        GraphTarget target,
        TCharacteristic payload,
        GraphCharacteristicDerivation derivation)
    {
        GraphCollections.RequireValue(payload, nameof(payload));
        ArgumentNullException.ThrowIfNull(derivation);

        Target = target;
        Payload = payload;
        Derivation = derivation;
    }

    public GraphTarget Target { get; }
    public TCharacteristic Payload { get; }
    public GraphCharacteristicDerivation Derivation { get; }
}

public sealed class GraphSeed<TSubject>
    where TSubject : notnull
{
    public GraphSeed(
        TSubject subject,
        GraphTarget target,
        GraphSeedRole role)
    {
        GraphCollections.RequireValue(subject, nameof(subject));
        GraphCollections.RequireDefined(role, nameof(role));

        Subject = subject;
        Target = target;
        Role = role;
    }

    public TSubject Subject { get; }
    public GraphTarget Target { get; }
    public GraphSeedRole Role { get; }
}

public sealed class GraphLimit<TLimit>
    where TLimit : notnull
{
    public GraphLimit(
        TLimit payload,
        GraphTarget? target = null)
    {
        GraphCollections.RequireValue(payload, nameof(payload));
        Payload = payload;
        Target = target;
    }

    public TLimit Payload { get; }
    public GraphTarget? Target { get; }
}

public sealed class GraphFailure<TFailure>
    where TFailure : notnull
{
    public GraphFailure(
        TFailure payload,
        GraphTarget? target = null)
    {
        GraphCollections.RequireValue(payload, nameof(payload));
        Payload = payload;
        Target = target;
    }

    public TFailure Payload { get; }
    public GraphTarget? Target { get; }
}

public sealed class GraphDocument<
    TSubject,
    TRelationship,
    TOccurrenceEvidence,
    TCharacteristic,
    TLimit,
    TFailure>
    where TSubject : notnull
    where TRelationship : notnull
    where TOccurrenceEvidence : notnull
    where TCharacteristic : notnull
    where TLimit : notnull
    where TFailure : notnull
{
    public GraphDocument(
        GraphDocumentScope scope,
        IEnumerable<GraphNode<TSubject>> nodes,
        IEnumerable<GraphGroup<TSubject>> groups,
        IEnumerable<GraphEdge<TRelationship>> edges,
        IEnumerable<
            GraphOccurrence<
                TSubject,
                TRelationship,
                TOccurrenceEvidence>> occurrences,
        IEnumerable<GraphCharacteristic<TCharacteristic>> characteristics,
        IEnumerable<GraphSeed<TSubject>> seeds,
        IEnumerable<GraphLimit<TLimit>> limits,
        IEnumerable<GraphFailure<TFailure>> failures,
        IEqualityComparer<TSubject>? subjectComparer = null,
        IEqualityComparer<TRelationship>? relationshipComparer = null)
    {
        GraphCollections.RequireDefined(scope, nameof(scope));
        Scope = scope;
        Nodes = GraphCollections.Snapshot(nodes, nameof(nodes));
        Groups = GraphCollections.Snapshot(groups, nameof(groups));
        Edges = GraphCollections.Snapshot(edges, nameof(edges));
        Occurrences = GraphCollections.Snapshot(
            occurrences,
            nameof(occurrences));
        Characteristics = GraphCollections.Snapshot(
            characteristics,
            nameof(characteristics));
        Seeds = GraphCollections.Snapshot(seeds, nameof(seeds));
        Limits = GraphCollections.Snapshot(limits, nameof(limits));
        Failures = GraphCollections.Snapshot(failures, nameof(failures));

        IEqualityComparer<TSubject> admittedSubjectComparer =
            subjectComparer ?? EqualityComparer<TSubject>.Default;
        IEqualityComparer<TRelationship> admittedRelationshipComparer =
            relationshipComparer ?? EqualityComparer<TRelationship>.Default;

        ValidateDenseIds(Nodes, static node => node.Id, nameof(nodes));
        ValidateDenseIds(Groups, static group => group.Id, nameof(groups));
        ValidateDenseIds(Edges, static edge => edge.Id, nameof(edges));
        ValidateDenseIds(
            Occurrences,
            static occurrence => occurrence.Id,
            nameof(occurrences));
        ValidateNodeSubjects(admittedSubjectComparer);
        ValidateGroups();
        ValidateEdgesAndOccurrences(admittedRelationshipComparer);
        ValidateCharacteristics();
        ValidateSeeds(admittedSubjectComparer);
        ValidateDiagnostics();
    }

    public GraphDocumentScope Scope { get; }
    public ImmutableArray<GraphNode<TSubject>> Nodes { get; }
    public ImmutableArray<GraphGroup<TSubject>> Groups { get; }
    public ImmutableArray<GraphEdge<TRelationship>> Edges { get; }
    public ImmutableArray<
        GraphOccurrence<
            TSubject,
            TRelationship,
            TOccurrenceEvidence>> Occurrences { get; }
    public ImmutableArray<GraphCharacteristic<TCharacteristic>>
        Characteristics { get; }
    public ImmutableArray<GraphSeed<TSubject>> Seeds { get; }
    public ImmutableArray<GraphLimit<TLimit>> Limits { get; }
    public ImmutableArray<GraphFailure<TFailure>> Failures { get; }

    private void ValidateNodeSubjects(
        IEqualityComparer<TSubject> subjectComparer)
    {
        var subjects = new HashSet<TSubject>(subjectComparer);
        if (Nodes.Any(node => !subjects.Add(node.Subject)))
        {
            throw new ArgumentException(
                "A subject can appear as at most one node.",
                nameof(Nodes));
        }
    }

    private void ValidateGroups()
    {
        foreach (GraphGroup<TSubject> group in Groups)
        {
            if (group.ParentId is not int parentId)
                continue;

            ValidateId(parentId, Groups.Length, "Group parent");
            if (parentId == group.Id)
            {
                throw new ArgumentException(
                    "A group cannot be its own parent.",
                    nameof(Groups));
            }
        }

        ValidateGroupParentCycles();

        foreach (GraphNode<TSubject> node in Nodes)
        {
            foreach (int groupId in node.GroupIds)
                ValidateId(groupId, Groups.Length, "Node group");
        }
    }

    private void ValidateGroupParentCycles()
    {
        var states = new byte[Groups.Length];
        for (var start = 0; start < Groups.Length; start++)
        {
            if (states[start] != 0)
                continue;

            int current = start;
            while (states[current] == 0)
            {
                states[current] = 1;
                if (Groups[current].ParentId is not int parentId)
                {
                    current = -1;
                    break;
                }
                current = parentId;
            }

            if (current >= 0 && states[current] == 1)
            {
                throw new ArgumentException(
                    "Group parents must not form a cycle.",
                    nameof(Groups));
            }

            current = start;
            while (current >= 0 && states[current] == 1)
            {
                states[current] = 2;
                current = Groups[current].ParentId ?? -1;
            }
        }
    }

    private void ValidateEdgesAndOccurrences(
        IEqualityComparer<TRelationship> relationshipComparer)
    {
        var boundOccurrences = new int[Occurrences.Length];
        var logicalEdges = new HashSet<EdgeIdentity>(
            new EdgeIdentityComparer(relationshipComparer));

        foreach (GraphEdge<TRelationship> edge in Edges)
        {
            ValidateId(edge.FromNodeId, Nodes.Length, "Edge source node");
            ValidateId(edge.ToNodeId, Nodes.Length, "Edge target node");
            if (!logicalEdges.Add(
                new(
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.Relationship)))
            {
                throw new ArgumentException(
                    "Logical edges must be unique by source, target, and relationship.",
                    nameof(Edges));
            }

            foreach (int occurrenceId in edge.OccurrenceIds)
            {
                ValidateId(
                    occurrenceId,
                    Occurrences.Length,
                    "Edge occurrence");
                GraphOccurrence<
                    TSubject,
                    TRelationship,
                    TOccurrenceEvidence> occurrence =
                        Occurrences[occurrenceId];
                if (!relationshipComparer.Equals(
                    occurrence.Relationship,
                    edge.Relationship))
                {
                    throw new ArgumentException(
                        "An occurrence relationship must equal its edge relationship.",
                        nameof(Edges));
                }
                boundOccurrences[occurrenceId]++;
            }
        }

        if (boundOccurrences.Any(static count => count == 0))
        {
            throw new ArgumentException(
                "Every occurrence must support at least one edge.",
                nameof(Occurrences));
        }

        foreach (GraphOccurrence<
            TSubject,
            TRelationship,
            TOccurrenceEvidence> occurrence in Occurrences)
        {
            foreach (int sourceId in occurrence.DerivedFromOccurrenceIds)
            {
                ValidateId(
                    sourceId,
                    Occurrences.Length,
                    "Derived source occurrence");
                if (sourceId == occurrence.Id)
                {
                    throw new ArgumentException(
                        "An occurrence cannot derive from itself.",
                        nameof(Occurrences));
                }
            }
        }

        ValidateOccurrenceDerivationCycles();
    }

    private void ValidateOccurrenceDerivationCycles()
    {
        var states = new byte[Occurrences.Length];
        for (var root = 0; root < Occurrences.Length; root++)
        {
            if (states[root] != 0)
                continue;

            states[root] = 1;
            var stack = new Stack<(int Id, int NextSource)>();
            stack.Push((root, 0));
            while (stack.TryPop(out (int Id, int NextSource) frame))
            {
                ImmutableArray<int> sources =
                    Occurrences[frame.Id].DerivedFromOccurrenceIds;
                if (frame.NextSource >= sources.Length)
                {
                    states[frame.Id] = 2;
                    continue;
                }

                stack.Push((frame.Id, frame.NextSource + 1));
                int sourceId = sources[frame.NextSource];
                if (states[sourceId] == 1)
                {
                    throw new ArgumentException(
                        "Occurrence derivations must not form a cycle.",
                        nameof(Occurrences));
                }
                if (states[sourceId] == 0)
                {
                    states[sourceId] = 1;
                    stack.Push((sourceId, 0));
                }
            }
        }
    }

    private void ValidateCharacteristics()
    {
        foreach (GraphCharacteristic<TCharacteristic> characteristic
            in Characteristics)
        {
            ValidateTarget(characteristic.Target);
            foreach (GraphTarget source
                in characteristic.Derivation.Sources)
            {
                ValidateTarget(source);
            }
        }
    }

    private void ValidateSeeds(
        IEqualityComparer<TSubject> subjectComparer)
    {
        var targets = new HashSet<GraphTarget>();
        foreach (GraphSeed<TSubject> seed in Seeds)
        {
            if (seed.Target.Kind is not (
                GraphTargetKind.Node
                or GraphTargetKind.Group))
            {
                throw new ArgumentException(
                    "A seed must target a node or group.",
                    nameof(Seeds));
            }
            ValidateTarget(seed.Target);
            TSubject targetSubject =
                seed.Target.Kind == GraphTargetKind.Node
                    ? Nodes[seed.Target.Id].Subject
                    : Groups[seed.Target.Id].Subject;
            if (!subjectComparer.Equals(seed.Subject, targetSubject))
            {
                throw new ArgumentException(
                    "A seed subject must equal its target subject.",
                    nameof(Seeds));
            }
            if (!targets.Add(seed.Target))
            {
                throw new ArgumentException(
                    "A target can have only one seed role.",
                    nameof(Seeds));
            }
        }
    }

    private void ValidateDiagnostics()
    {
        foreach (GraphLimit<TLimit> limit in Limits)
        {
            if (limit.Target is GraphTarget target)
                ValidateTarget(target);
        }
        foreach (GraphFailure<TFailure> failure in Failures)
        {
            if (failure.Target is GraphTarget target)
                ValidateTarget(target);
        }
    }

    private void ValidateTarget(GraphTarget target)
    {
        if (!target.IsInitialized)
        {
            throw new ArgumentException(
                "A graph target must be initialized.",
                nameof(target));
        }

        int count = target.Kind switch
        {
            GraphTargetKind.Node => Nodes.Length,
            GraphTargetKind.Group => Groups.Length,
            GraphTargetKind.Edge => Edges.Length,
            GraphTargetKind.Occurrence => Occurrences.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        ValidateId(target.Id, count, "Graph target");
    }

    private static void ValidateDenseIds<T>(
        ImmutableArray<T> values,
        Func<T, int> getId,
        string parameterName)
    {
        for (var index = 0; index < values.Length; index++)
        {
            if (getId(values[index]) != index)
            {
                throw new ArgumentException(
                    "Document-local ids must be dense, zero-based, and ordered.",
                    parameterName);
            }
        }
    }

    private static void ValidateId(int id, int count, string name)
    {
        if ((uint)id >= (uint)count)
        {
            throw new ArgumentException(
                $"{name} id {id} is outside the document.");
        }
    }

    private readonly record struct EdgeIdentity(
        int FromNodeId,
        int ToNodeId,
        TRelationship Relationship);

    private sealed class EdgeIdentityComparer(
        IEqualityComparer<TRelationship> relationshipComparer)
        : IEqualityComparer<EdgeIdentity>
    {
        public bool Equals(EdgeIdentity x, EdgeIdentity y) =>
            x.FromNodeId == y.FromNodeId
            && x.ToNodeId == y.ToNodeId
            && relationshipComparer.Equals(
                x.Relationship,
                y.Relationship);

        public int GetHashCode(EdgeIdentity value) =>
            HashCode.Combine(
                value.FromNodeId,
                value.ToNodeId,
                relationshipComparer.GetHashCode(value.Relationship));
    }
}

internal static class GraphCollections
{
    internal static void RequireDefined<T>(
        T value,
        string parameterName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(parameterName);
    }

    internal static void RequireValue<T>(
        T value,
        string parameterName)
        where T : notnull
    {
        if (value is null)
            throw new ArgumentNullException(parameterName);
    }

    internal static ImmutableArray<T> Snapshot<T>(
        IEnumerable<T> values,
        string parameterName)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values is ImmutableArray<T> immutable && immutable.IsDefault)
        {
            throw new ArgumentException(
                "The immutable array must be initialized.",
                parameterName);
        }

        ImmutableArray<T> snapshot = values.ToImmutableArray();
        if (snapshot.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Collection elements cannot be null.",
                parameterName);
        }
        return snapshot;
    }
}
