using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>The outcome of one document-local graph join.</summary>
public enum InspectionGraphJoinMatch
{
    Found,
    NotProjected,
    Ambiguous,
}

/// <summary>The outcome of joining one semantic subject to a graph node.</summary>
public sealed record InspectionGraphNodeJoin
{
    public InspectionGraphNodeJoin(
        InspectionGraphJoinMatch match,
        int? nodeId)
    {
        if (!Enum.IsDefined(match))
            throw new ArgumentOutOfRangeException(nameof(match));
        if ((match == InspectionGraphJoinMatch.Found)
            != nodeId.HasValue)
        {
            throw new ArgumentException(
                "A found node join requires one node id; every other outcome requires none.",
                nameof(nodeId));
        }
        if (nodeId < 0)
            throw new ArgumentOutOfRangeException(nameof(nodeId));

        Match = match;
        NodeId = nodeId;
    }

    public InspectionGraphJoinMatch Match { get; }
    public int? NodeId { get; }
}

internal sealed record InspectionGraphRelationshipContribution(
    int FromNodeId,
    int ToNodeId,
    InspectionGraphRelationshipDescriptor Relationship,
    InspectionGraphSubject SourceSubject,
    InspectionGraphSubject TargetSubject,
    IInspectionGraphOccurrenceEvidence Evidence,
    ImmutableArray<int> DerivedFromOccurrenceIds);

internal sealed record InspectionGraphRelationshipContributionJoin(
    int ContributionIndex,
    int EdgeId,
    int OccurrenceId);

internal sealed record InspectionGraphRelationshipComposition(
    InspectionGraphDocument Document,
    ImmutableArray<InspectionGraphRelationshipContributionJoin> Joins);

/// <summary>
/// Appends producer-issued relationships without changing existing graph ids.
/// </summary>
internal static class InspectionGraphRelationshipComposer
{
    internal static InspectionGraphRelationshipComposition Compose(
        InspectionGraphDocument source,
        IEnumerable<InspectionGraphRelationshipContribution>
            contributions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ImmutableArray<InspectionGraphRelationshipContribution> pending =
            InspectionGraphCollections.Snapshot(
                contributions,
                nameof(contributions));
        if (pending.IsEmpty)
            return new(source, []);

        if (source.NeighborhoodRequest is { } neighborhood
            && pending.Any(contribution =>
                !neighborhood.Relationships.Contains(
                    contribution.Relationship)))
        {
            throw new ArgumentException(
                "A neighborhood can compose only relationships selected by its request.",
                nameof(contributions));
        }

        var occurrences =
            source.Occurrences.ToList();
        var edges =
            source.Edges
                .Select(edge => new MutableEdge(
                    edge.Id,
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.Relationship,
                    [.. edge.OccurrenceIds]))
                .ToList();
        var edgesByIdentity =
            edges.ToDictionary(
                static edge => (
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.Relationship));
        var occurrencesByIdentity =
            new Dictionary<
                InspectionGraphRelationshipDescriptor,
                Dictionary<object, int>>();
        foreach (InspectionGraphOccurrence occurrence
            in source.Occurrences)
        {
            Dictionary<object, int> identities =
                GetOccurrenceIdentities(
                    occurrencesByIdentity,
                    occurrence.Relationship);
            identities.Add(
                occurrence.Relationship.OccurrenceIdentity
                    .Project(occurrence),
                occurrence.Id);
        }

        var joins =
            ImmutableArray.CreateBuilder<
                InspectionGraphRelationshipContributionJoin>(
                    pending.Length);
        for (var index = 0; index < pending.Length; index++)
        {
            InspectionGraphRelationshipContribution contribution =
                pending[index];
            ValidateContribution(source, contribution);
            var projected = new InspectionGraphOccurrence(
                id: 0,
                contribution.Relationship,
                contribution.SourceSubject,
                contribution.TargetSubject,
                contribution.Evidence,
                contribution.DerivedFromOccurrenceIds);
            object identity =
                contribution.Relationship.OccurrenceIdentity
                    .Project(projected)
                ?? throw new ArgumentException(
                    "An occurrence identity cannot be null.",
                    nameof(contributions));
            Dictionary<object, int> identities =
                GetOccurrenceIdentities(
                    occurrencesByIdentity,
                    contribution.Relationship);
            if (!identities.TryGetValue(
                    identity,
                    out int occurrenceId))
            {
                occurrenceId = occurrences.Count;
                occurrences.Add(
                    new InspectionGraphOccurrence(
                        occurrenceId,
                        contribution.Relationship,
                        contribution.SourceSubject,
                        contribution.TargetSubject,
                        contribution.Evidence,
                        contribution.DerivedFromOccurrenceIds));
                identities.Add(identity, occurrenceId);
            }
            else if (!Matches(
                occurrences[occurrenceId],
                contribution))
            {
                throw new InvalidOperationException(
                    "One relationship occurrence identity cannot carry contradictory evidence.");
            }

            var edgeIdentity = (
                contribution.FromNodeId,
                contribution.ToNodeId,
                contribution.Relationship);
            if (!edgesByIdentity.TryGetValue(
                    edgeIdentity,
                    out MutableEdge? edge))
            {
                edge = new MutableEdge(
                    edges.Count,
                    contribution.FromNodeId,
                    contribution.ToNodeId,
                    contribution.Relationship,
                    []);
                edges.Add(edge);
                edgesByIdentity.Add(edgeIdentity, edge);
            }
            if (!edge.OccurrenceIds.Contains(occurrenceId))
                edge.OccurrenceIds.Add(occurrenceId);
            joins.Add(new(index, edge.Id, occurrenceId));
        }

        InspectionGraphEdge[] composedEdges =
        [
            .. edges.Select(edge =>
                new InspectionGraphEdge(
                    edge.Id,
                    edge.FromNodeId,
                    edge.ToNodeId,
                    edge.Relationship,
                    edge.OccurrenceIds)),
        ];
        InspectionGraphDocument document = Rebuild(
            source,
            composedEdges,
            occurrences);
        return new(document, joins.MoveToImmutable());
    }

    static Dictionary<object, int> GetOccurrenceIdentities(
        Dictionary<
            InspectionGraphRelationshipDescriptor,
            Dictionary<object, int>> identities,
        InspectionGraphRelationshipDescriptor relationship)
    {
        if (!identities.TryGetValue(
                relationship,
                out Dictionary<object, int>? values))
        {
            values = [];
            identities.Add(relationship, values);
        }
        return values;
    }

    static void ValidateContribution(
        InspectionGraphDocument source,
        InspectionGraphRelationshipContribution contribution)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            contribution.FromNodeId);
        ArgumentOutOfRangeException.ThrowIfNegative(
            contribution.ToNodeId);
        if ((uint)contribution.FromNodeId
                >= (uint)source.Nodes.Length
            || (uint)contribution.ToNodeId
                >= (uint)source.Nodes.Length)
        {
            throw new ArgumentException(
                "A relationship contribution endpoint is outside the source document.",
                nameof(contribution));
        }
        ArgumentNullException.ThrowIfNull(
            contribution.Relationship);
        ArgumentNullException.ThrowIfNull(
            contribution.SourceSubject);
        ArgumentNullException.ThrowIfNull(
            contribution.TargetSubject);
        ArgumentNullException.ThrowIfNull(
            contribution.Evidence);
        if (contribution.DerivedFromOccurrenceIds.IsDefault)
        {
            throw new ArgumentException(
                "Derived occurrence ids must be initialized.",
                nameof(contribution));
        }
    }

    static bool Matches(
        InspectionGraphOccurrence occurrence,
        InspectionGraphRelationshipContribution contribution) =>
        ReferenceEquals(
            occurrence.Relationship,
            contribution.Relationship)
        && occurrence.SourceSubject
            == contribution.SourceSubject
        && occurrence.TargetSubject
            == contribution.TargetSubject
        && Equals(
            occurrence.Evidence,
            contribution.Evidence)
        && occurrence.DerivedFromOccurrenceIds.SequenceEqual(
            contribution.DerivedFromOccurrenceIds);

    static InspectionGraphDocument Rebuild(
        InspectionGraphDocument source,
        IEnumerable<InspectionGraphEdge> edges,
        IEnumerable<InspectionGraphOccurrence> occurrences)
    {
        if (source.NeighborhoodRequest is { } neighborhood)
        {
            return new(
                source.Scope,
                neighborhood,
                source.Nodes,
                source.Groups,
                edges,
                occurrences,
                source.Characteristics,
                source.Seeds,
                source.Limits,
                source.Failures);
        }
        if (source.InducedSetRequest is { } inducedSet)
        {
            return new(
                source.Scope,
                inducedSet,
                source.Nodes,
                source.Groups,
                edges,
                occurrences,
                source.Characteristics,
                source.Seeds,
                source.Limits,
                source.Failures);
        }
        return new(
            source.Scope,
            source.ModeRequest,
            source.Nodes,
            source.Groups,
            edges,
            occurrences,
            source.Characteristics,
            source.Seeds,
            source.Limits,
            source.Failures);
    }

    sealed record MutableEdge(
        int Id,
        int FromNodeId,
        int ToNodeId,
        InspectionGraphRelationshipDescriptor Relationship,
        List<int> OccurrenceIds);
}
