using System.Collections.Immutable;

namespace Inspector.Graph;

public readonly record struct GraphGroupAssignment
{
    readonly bool _initialized;

    public GraphGroupAssignment(int nodeId, int groupId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodeId);
        ArgumentOutOfRangeException.ThrowIfNegative(groupId);
        NodeId = nodeId;
        GroupId = groupId;
        _initialized = true;
    }

    public int NodeId { get; }
    public int GroupId { get; }

    internal bool IsInitialized => _initialized;
}

public sealed class GraphGroupProjectionPlan<TRelationship>
    where TRelationship : notnull
{
    public GraphGroupProjectionPlan(
        GraphDocumentIdentity sourceDocument,
        IReadOnlyList<GraphGroupAssignment> assignments,
        IReadOnlyList<TRelationship> relationships,
        int? maxContributorsPerProjectedEdge = null)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        ArgumentNullException.ThrowIfNull(assignments);
        if (assignments is ImmutableArray<GraphGroupAssignment> immutable
            && immutable.IsDefault)
        {
            throw new ArgumentException(
                "The immutable array must be initialized.",
                nameof(assignments));
        }
        if (maxContributorsPerProjectedEdge is int maximum)
            ArgumentOutOfRangeException.ThrowIfNegative(maximum);

        ImmutableArray<GraphGroupAssignment> assignmentSnapshot =
            [.. assignments];
        if (assignmentSnapshot.Any(
            static assignment => !assignment.IsInitialized))
        {
            throw new ArgumentException(
                "Assignments must be initialized.",
                nameof(assignments));
        }
        if (assignmentSnapshot.Select(static assignment => assignment.NodeId)
            .Distinct()
            .Count() != assignmentSnapshot.Length)
        {
            throw new ArgumentException(
                "Assignments must have distinct node ids.",
                nameof(assignments));
        }

        SourceDocument = sourceDocument;
        Assignments = assignmentSnapshot;
        Relationships = GraphExecutionCollections.SnapshotValues(
            relationships,
            nameof(relationships));
        MaxContributorsPerProjectedEdge =
            maxContributorsPerProjectedEdge;
    }

    public GraphDocumentIdentity SourceDocument { get; }
    public ImmutableArray<GraphGroupAssignment> Assignments { get; }
    public ImmutableArray<TRelationship> Relationships { get; }
    public int? MaxContributorsPerProjectedEdge { get; }
}

public sealed class GraphProjectedGroupNode
{
    internal GraphProjectedGroupNode(
        int id,
        int sourceGroupId,
        ImmutableArray<int> sourceNodeIds)
    {
        Id = id;
        SourceGroupId = sourceGroupId;
        SourceNodeIds = sourceNodeIds;
    }

    public int Id { get; }
    public int SourceGroupId { get; }
    public ImmutableArray<int> SourceNodeIds { get; }
}

public readonly record struct GraphProjectedEdgeContributor
{
    public GraphProjectedEdgeContributor(
        int sourceEdgeId,
        int occurrenceCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceEdgeId);
        ArgumentOutOfRangeException.ThrowIfNegative(occurrenceCount);
        SourceEdgeId = sourceEdgeId;
        OccurrenceCount = occurrenceCount;
    }

    public int SourceEdgeId { get; }
    public int OccurrenceCount { get; }
}

public sealed class GraphProjectedEdgeExplanation
{
    internal GraphProjectedEdgeExplanation(
        ImmutableArray<GraphProjectedEdgeContributor> retainedContributors,
        int totalSourceEdgeContributorCount,
        int totalDistinctOccurrenceCount,
        int retainedCoveredDistinctOccurrenceCount)
    {
        RetainedContributors = retainedContributors;
        TotalSourceEdgeContributorCount =
            totalSourceEdgeContributorCount;
        TotalDistinctOccurrenceCount = totalDistinctOccurrenceCount;
        RetainedCoveredDistinctOccurrenceCount =
            retainedCoveredDistinctOccurrenceCount;
    }

    public ImmutableArray<GraphProjectedEdgeContributor>
        RetainedContributors { get; }
    public int TotalSourceEdgeContributorCount { get; }
    public int RetainedSourceEdgeContributorCount =>
        RetainedContributors.Length;
    public int OmittedSourceEdgeContributorCount =>
        TotalSourceEdgeContributorCount
        - RetainedSourceEdgeContributorCount;
    public int TotalDistinctOccurrenceCount { get; }
    public int RetainedCoveredDistinctOccurrenceCount { get; }
    public int OmittedOnlyDistinctOccurrenceCount =>
        TotalDistinctOccurrenceCount
        - RetainedCoveredDistinctOccurrenceCount;
}

public sealed class GraphProjectedEdge<TRelationship>
    where TRelationship : notnull
{
    internal GraphProjectedEdge(
        int id,
        int fromProjectedNodeId,
        int toProjectedNodeId,
        TRelationship relationship,
        ImmutableArray<int> sourceEdgeIds,
        ImmutableArray<int> sourceOccurrenceIds,
        GraphProjectedEdgeExplanation? explanation)
    {
        Id = id;
        FromProjectedNodeId = fromProjectedNodeId;
        ToProjectedNodeId = toProjectedNodeId;
        Relationship = relationship;
        SourceEdgeIds = sourceEdgeIds;
        SourceOccurrenceIds = sourceOccurrenceIds;
        Explanation = explanation;
    }

    public int Id { get; }
    public int FromProjectedNodeId { get; }
    public int ToProjectedNodeId { get; }
    public TRelationship Relationship { get; }
    public ImmutableArray<int> SourceEdgeIds { get; }
    public ImmutableArray<int> SourceOccurrenceIds { get; }
    public GraphProjectedEdgeExplanation? Explanation { get; }
}

public sealed class GraphWithinGroupContribution<TRelationship>
    where TRelationship : notnull
{
    internal GraphWithinGroupContribution(
        int projectedNodeId,
        TRelationship relationship,
        ImmutableArray<int> sourceEdgeIds,
        ImmutableArray<int> sourceOccurrenceIds)
    {
        ProjectedNodeId = projectedNodeId;
        Relationship = relationship;
        SourceEdgeIds = sourceEdgeIds;
        SourceOccurrenceIds = sourceOccurrenceIds;
    }

    public int ProjectedNodeId { get; }
    public TRelationship Relationship { get; }
    public ImmutableArray<int> SourceEdgeIds { get; }
    public ImmutableArray<int> SourceOccurrenceIds { get; }
}

public sealed record GraphGroupProjectionWorkReceipt
{
    internal GraphGroupProjectionWorkReceipt(
        GraphDocumentIdentity sourceDocument,
        int canonicalNodesExamined,
        int nodeAssignmentsExamined,
        int sourceGroupsAdmitted,
        int canonicalEdgesExamined,
        int selectedSourceEdgesAdmitted,
        int crossGroupSourceEdgesAdmitted,
        int withinGroupSourceEdgesAdmitted,
        int distinctSelectedOccurrences,
        int projectedEdgesIssued,
        int withinGroupRowsIssued,
        int contributorRankingsRequested,
        int contributorRankingsIssued,
        bool terminalSettled)
    {
        ArgumentNullException.ThrowIfNull(sourceDocument);
        ArgumentOutOfRangeException.ThrowIfNegative(
            canonicalNodesExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(
            nodeAssignmentsExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(
            sourceGroupsAdmitted);
        ArgumentOutOfRangeException.ThrowIfNegative(
            canonicalEdgesExamined);
        ArgumentOutOfRangeException.ThrowIfNegative(
            selectedSourceEdgesAdmitted);
        ArgumentOutOfRangeException.ThrowIfNegative(
            crossGroupSourceEdgesAdmitted);
        ArgumentOutOfRangeException.ThrowIfNegative(
            withinGroupSourceEdgesAdmitted);
        ArgumentOutOfRangeException.ThrowIfNegative(
            distinctSelectedOccurrences);
        ArgumentOutOfRangeException.ThrowIfNegative(projectedEdgesIssued);
        ArgumentOutOfRangeException.ThrowIfNegative(withinGroupRowsIssued);
        ArgumentOutOfRangeException.ThrowIfNegative(
            contributorRankingsRequested);
        ArgumentOutOfRangeException.ThrowIfNegative(
            contributorRankingsIssued);

        SourceDocument = sourceDocument;
        CanonicalNodesExamined = canonicalNodesExamined;
        NodeAssignmentsExamined = nodeAssignmentsExamined;
        SourceGroupsAdmitted = sourceGroupsAdmitted;
        CanonicalEdgesExamined = canonicalEdgesExamined;
        SelectedSourceEdgesAdmitted = selectedSourceEdgesAdmitted;
        CrossGroupSourceEdgesAdmitted =
            crossGroupSourceEdgesAdmitted;
        WithinGroupSourceEdgesAdmitted =
            withinGroupSourceEdgesAdmitted;
        DistinctSelectedOccurrences = distinctSelectedOccurrences;
        ProjectedEdgesIssued = projectedEdgesIssued;
        WithinGroupRowsIssued = withinGroupRowsIssued;
        ContributorRankingsRequested = contributorRankingsRequested;
        ContributorRankingsIssued = contributorRankingsIssued;
        TerminalSettled = terminalSettled;
    }

    public GraphDocumentIdentity SourceDocument { get; }
    public int CanonicalNodesExamined { get; }
    public int NodeAssignmentsExamined { get; }
    public int SourceGroupsAdmitted { get; }
    public int CanonicalEdgesExamined { get; }
    public int SelectedSourceEdgesAdmitted { get; }
    public int CrossGroupSourceEdgesAdmitted { get; }
    public int WithinGroupSourceEdgesAdmitted { get; }
    public int DistinctSelectedOccurrences { get; }
    public int ProjectedEdgesIssued { get; }
    public int WithinGroupRowsIssued { get; }
    public int ContributorRankingsRequested { get; }
    public int ContributorRankingsIssued { get; }
    public bool TerminalSettled { get; }
}

public sealed class GraphGroupProjectionResult<TRelationship>
    where TRelationship : notnull
{
    internal GraphGroupProjectionResult(
        ImmutableArray<GraphProjectedGroupNode> nodes,
        ImmutableArray<GraphProjectedEdge<TRelationship>> edges,
        ImmutableArray<GraphWithinGroupContribution<TRelationship>>
            withinGroupContributions,
        GraphGroupProjectionWorkReceipt receipt)
    {
        Nodes = nodes;
        Edges = edges;
        WithinGroupContributions = withinGroupContributions;
        Receipt = receipt;
    }

    public ImmutableArray<GraphProjectedGroupNode> Nodes { get; }
    public ImmutableArray<GraphProjectedEdge<TRelationship>> Edges
    { get; }
    public ImmutableArray<GraphWithinGroupContribution<TRelationship>>
        WithinGroupContributions { get; }
    public GraphStructuralCompletion Completion =>
        GraphStructuralCompletion.Exhausted;
    public GraphGroupProjectionWorkReceipt Receipt { get; }
}

public static partial class GraphDocumentExecution
{
    public static GraphGroupProjectionResult<TRelationship>
        GroupProjection<
        TSubject,
        TRelationship,
        TOccurrenceEvidence,
        TCharacteristic,
        TLimit,
        TFailure>(
        GraphDocument<
            TSubject,
            TRelationship,
            TOccurrenceEvidence,
            TCharacteristic,
            TLimit,
            TFailure> document,
        GraphGroupProjectionPlan<TRelationship> plan)
        where TSubject : notnull
        where TRelationship : notnull
        where TOccurrenceEvidence : notnull
        where TCharacteristic : notnull
        where TLimit : notnull
        where TFailure : notnull
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);
        if (!ReferenceEquals(document.Identity, plan.SourceDocument))
        {
            throw new ArgumentException(
                "The projection plan belongs to another graph document.",
                nameof(plan));
        }

        int[] assignedGroupIds =
            ValidateGroupAssignments(document, plan.Assignments);
        (
            ImmutableArray<GraphProjectedGroupNode> projectedNodes,
            int[] projectedNodeIds) =
                CreateProjectedNodes(document, assignedGroupIds);

        HashSet<TRelationship> selectedRelationships =
            SelectedRelationships(
                plan.Relationships,
                document.RelationshipComparer);
        var projectedLookup =
            new Dictionary<
                GroupProjectedEdgeKey<TRelationship>,
                GroupContribution<TRelationship>>(
                new GroupProjectedEdgeKeyComparer<TRelationship>(
                    document.RelationshipComparer));
        var withinGroupLookup =
            new Dictionary<
                GroupWithinEdgeKey<TRelationship>,
                GroupContribution<TRelationship>>(
                new GroupWithinEdgeKeyComparer<TRelationship>(
                    document.RelationshipComparer));
        var selectedOccurrences = new HashSet<int>();
        var canonicalEdgesExamined = 0;
        var selectedSourceEdges = 0;
        var crossGroupSourceEdges = 0;
        var withinGroupSourceEdges = 0;

        if (selectedRelationships.Count > 0)
        {
            foreach (GraphEdge<TRelationship> edge in document.Edges)
            {
                canonicalEdgesExamined++;
                if (!selectedRelationships.Contains(edge.Relationship))
                    continue;

                selectedSourceEdges++;
                foreach (int occurrenceId in edge.OccurrenceIds)
                    selectedOccurrences.Add(occurrenceId);

                int fromProjectedNodeId =
                    projectedNodeIds[edge.FromNodeId];
                int toProjectedNodeId =
                    projectedNodeIds[edge.ToNodeId];
                if (fromProjectedNodeId == toProjectedNodeId)
                {
                    withinGroupSourceEdges++;
                    AddWithinGroupContribution(
                        withinGroupLookup,
                        fromProjectedNodeId,
                        edge);
                }
                else
                {
                    crossGroupSourceEdges++;
                    AddProjectedEdgeContribution(
                        projectedLookup,
                        fromProjectedNodeId,
                        toProjectedNodeId,
                        edge);
                }
            }
        }

        List<GroupContribution<TRelationship>> projectedContributions =
            [.. projectedLookup.Values];
        projectedContributions.Sort(
            static (left, right) =>
            {
                int comparison = left.FromProjectedNodeId.CompareTo(
                    right.FromProjectedNodeId);
                if (comparison != 0)
                    return comparison;
                comparison = left.ToProjectedNodeId.CompareTo(
                    right.ToProjectedNodeId);
                return comparison != 0
                    ? comparison
                    : left.FirstSourceEdgeId.CompareTo(
                        right.FirstSourceEdgeId);
            });

        var projectedEdges =
            ImmutableArray.CreateBuilder<GraphProjectedEdge<TRelationship>>(
                projectedContributions.Count);
        for (var id = 0; id < projectedContributions.Count; id++)
        {
            GroupContribution<TRelationship> contribution =
                projectedContributions[id];
            projectedEdges.Add(
                new(
                    id,
                    contribution.FromProjectedNodeId,
                    contribution.ToProjectedNodeId,
                    contribution.Relationship,
                    [.. contribution.SourceEdgeIds],
                    SnapshotSorted(contribution.SourceOccurrenceIds),
                    plan.MaxContributorsPerProjectedEdge is int maximum
                        ? Explain(document, contribution, maximum)
                        : null));
        }

        List<GroupContribution<TRelationship>> withinContributions =
            [.. withinGroupLookup.Values];
        withinContributions.Sort(
            static (left, right) =>
            {
                int comparison = left.FromProjectedNodeId.CompareTo(
                    right.FromProjectedNodeId);
                return comparison != 0
                    ? comparison
                    : left.FirstSourceEdgeId.CompareTo(
                        right.FirstSourceEdgeId);
            });
        var withinGroupRows =
            ImmutableArray.CreateBuilder<
                GraphWithinGroupContribution<TRelationship>>(
                withinContributions.Count);
        foreach (GroupContribution<TRelationship> contribution
            in withinContributions)
        {
            withinGroupRows.Add(
                new(
                    contribution.FromProjectedNodeId,
                    contribution.Relationship,
                    [.. contribution.SourceEdgeIds],
                    SnapshotSorted(contribution.SourceOccurrenceIds)));
        }

        int contributorRankings =
            plan.MaxContributorsPerProjectedEdge.HasValue
                ? projectedEdges.Count
                : 0;
        var receipt = new GraphGroupProjectionWorkReceipt(
            document.Identity,
            document.Nodes.Length,
            plan.Assignments.Length,
            projectedNodes.Length,
            canonicalEdgesExamined,
            selectedSourceEdges,
            crossGroupSourceEdges,
            withinGroupSourceEdges,
            selectedOccurrences.Count,
            projectedEdges.Count,
            withinGroupRows.Count,
            contributorRankings,
            contributorRankings,
            terminalSettled: true);
        return new(
            projectedNodes,
            projectedEdges.MoveToImmutable(),
            withinGroupRows.MoveToImmutable(),
            receipt);
    }

    static int[] ValidateGroupAssignments<
        TSubject,
        TRelationship,
        TOccurrenceEvidence,
        TCharacteristic,
        TLimit,
        TFailure>(
        GraphDocument<
            TSubject,
            TRelationship,
            TOccurrenceEvidence,
            TCharacteristic,
            TLimit,
            TFailure> document,
        ImmutableArray<GraphGroupAssignment> assignments)
        where TSubject : notnull
        where TRelationship : notnull
        where TOccurrenceEvidence : notnull
        where TCharacteristic : notnull
        where TLimit : notnull
        where TFailure : notnull
    {
        if (assignments.Length != document.Nodes.Length)
        {
            throw new ArgumentException(
                "Every canonical node must have one assignment.",
                nameof(assignments));
        }

        var assignedGroupIds = new int[document.Nodes.Length];
        Array.Fill(assignedGroupIds, -1);
        foreach (GraphGroupAssignment assignment in assignments)
        {
            if ((uint)assignment.NodeId >= (uint)document.Nodes.Length)
            {
                throw new ArgumentException(
                    "An assignment node id is outside the source document.",
                    nameof(assignments));
            }
            if ((uint)assignment.GroupId >= (uint)document.Groups.Length)
            {
                throw new ArgumentException(
                    "An assignment group id is outside the source document.",
                    nameof(assignments));
            }
            if (!IsAdmittedGroup(
                document,
                document.Nodes[assignment.NodeId],
                assignment.GroupId))
            {
                throw new ArgumentException(
                    "An assigned group must be a direct group or its ancestor.",
                    nameof(assignments));
            }
            assignedGroupIds[assignment.NodeId] = assignment.GroupId;
        }
        if (assignedGroupIds.Any(static groupId => groupId < 0))
        {
            throw new ArgumentException(
                "Every canonical node must have one assignment.",
                nameof(assignments));
        }
        return assignedGroupIds;
    }

    static bool IsAdmittedGroup<
        TSubject,
        TRelationship,
        TOccurrenceEvidence,
        TCharacteristic,
        TLimit,
        TFailure>(
        GraphDocument<
            TSubject,
            TRelationship,
            TOccurrenceEvidence,
            TCharacteristic,
            TLimit,
            TFailure> document,
        GraphNode<TSubject> node,
        int assignedGroupId)
        where TSubject : notnull
        where TRelationship : notnull
        where TOccurrenceEvidence : notnull
        where TCharacteristic : notnull
        where TLimit : notnull
        where TFailure : notnull
    {
        foreach (int directGroupId in node.GroupIds)
        {
            int currentGroupId = directGroupId;
            while (true)
            {
                if (currentGroupId == assignedGroupId)
                    return true;
                if (document.Groups[currentGroupId].ParentId
                    is not int parentId)
                {
                    break;
                }
                currentGroupId = parentId;
            }
        }
        return false;
    }

    static (
        ImmutableArray<GraphProjectedGroupNode> Nodes,
        int[] ProjectedNodeIds)
        CreateProjectedNodes<
        TSubject,
        TRelationship,
        TOccurrenceEvidence,
        TCharacteristic,
        TLimit,
        TFailure>(
        GraphDocument<
            TSubject,
            TRelationship,
            TOccurrenceEvidence,
            TCharacteristic,
            TLimit,
            TFailure> document,
        int[] assignedGroupIds)
        where TSubject : notnull
        where TRelationship : notnull
        where TOccurrenceEvidence : notnull
        where TCharacteristic : notnull
        where TLimit : notnull
        where TFailure : notnull
    {
        var sourceNodeIdsByGroup =
            new List<int>?[document.Groups.Length];
        for (var nodeId = 0; nodeId < assignedGroupIds.Length; nodeId++)
        {
            int groupId = assignedGroupIds[nodeId];
            (sourceNodeIdsByGroup[groupId] ??= []).Add(nodeId);
        }

        var projectedNodeIds = new int[document.Nodes.Length];
        var nodes =
            ImmutableArray.CreateBuilder<GraphProjectedGroupNode>();
        for (var groupId = 0;
            groupId < sourceNodeIdsByGroup.Length;
            groupId++)
        {
            if (sourceNodeIdsByGroup[groupId] is not { } sourceNodeIds)
                continue;

            int projectedNodeId = nodes.Count;
            foreach (int sourceNodeId in sourceNodeIds)
                projectedNodeIds[sourceNodeId] = projectedNodeId;
            nodes.Add(new(projectedNodeId, groupId, [.. sourceNodeIds]));
        }
        return (nodes.ToImmutable(), projectedNodeIds);
    }

    static void AddProjectedEdgeContribution<TRelationship>(
        Dictionary<
            GroupProjectedEdgeKey<TRelationship>,
            GroupContribution<TRelationship>> contributions,
        int fromProjectedNodeId,
        int toProjectedNodeId,
        GraphEdge<TRelationship> sourceEdge)
        where TRelationship : notnull
    {
        var key = new GroupProjectedEdgeKey<TRelationship>(
            fromProjectedNodeId,
            toProjectedNodeId,
            sourceEdge.Relationship);
        if (!contributions.TryGetValue(key, out var contribution))
        {
            contribution = new(
                fromProjectedNodeId,
                toProjectedNodeId,
                sourceEdge.Relationship);
            contributions.Add(key, contribution);
        }
        contribution.Add(sourceEdge);
    }

    static void AddWithinGroupContribution<TRelationship>(
        Dictionary<
            GroupWithinEdgeKey<TRelationship>,
            GroupContribution<TRelationship>> contributions,
        int projectedNodeId,
        GraphEdge<TRelationship> sourceEdge)
        where TRelationship : notnull
    {
        var key = new GroupWithinEdgeKey<TRelationship>(
            projectedNodeId,
            sourceEdge.Relationship);
        if (!contributions.TryGetValue(key, out var contribution))
        {
            contribution = new(
                projectedNodeId,
                projectedNodeId,
                sourceEdge.Relationship);
            contributions.Add(key, contribution);
        }
        contribution.Add(sourceEdge);
    }

    static GraphProjectedEdgeExplanation Explain<
        TSubject,
        TRelationship,
        TOccurrenceEvidence,
        TCharacteristic,
        TLimit,
        TFailure>(
        GraphDocument<
            TSubject,
            TRelationship,
            TOccurrenceEvidence,
            TCharacteristic,
            TLimit,
            TFailure> document,
        GroupContribution<TRelationship> contribution,
        int maximum)
        where TSubject : notnull
        where TRelationship : notnull
        where TOccurrenceEvidence : notnull
        where TCharacteristic : notnull
        where TLimit : notnull
        where TFailure : notnull
    {
        var contributors =
            new List<GraphProjectedEdgeContributor>(
                contribution.SourceEdgeIds.Count);
        foreach (int sourceEdgeId in contribution.SourceEdgeIds)
        {
            contributors.Add(
                new(
                    sourceEdgeId,
                    document.Edges[sourceEdgeId].OccurrenceIds.Length));
        }
        contributors.Sort(
            static (left, right) =>
            {
                int comparison = right.OccurrenceCount.CompareTo(
                    left.OccurrenceCount);
                return comparison != 0
                    ? comparison
                    : left.SourceEdgeId.CompareTo(right.SourceEdgeId);
            });
        int retainedCount = Math.Min(maximum, contributors.Count);
        var retained = ImmutableArray.CreateBuilder<
            GraphProjectedEdgeContributor>(retainedCount);
        var retainedOccurrences = new HashSet<int>();
        for (var index = 0; index < retainedCount; index++)
        {
            GraphProjectedEdgeContributor contributor =
                contributors[index];
            retained.Add(contributor);
            foreach (int occurrenceId
                in document.Edges[contributor.SourceEdgeId].OccurrenceIds)
            {
                retainedOccurrences.Add(occurrenceId);
            }
        }
        return new(
            retained.MoveToImmutable(),
            contributors.Count,
            contribution.SourceOccurrenceIds.Count,
            retainedOccurrences.Count);
    }

    static ImmutableArray<int> SnapshotSorted(HashSet<int> values)
    {
        int[] snapshot = [.. values];
        Array.Sort(snapshot);
        return [.. snapshot];
    }

    readonly record struct GroupProjectedEdgeKey<TRelationship>(
        int FromProjectedNodeId,
        int ToProjectedNodeId,
        TRelationship Relationship)
        where TRelationship : notnull;

    sealed class GroupProjectedEdgeKeyComparer<TRelationship>
        : IEqualityComparer<GroupProjectedEdgeKey<TRelationship>>
        where TRelationship : notnull
    {
        readonly IEqualityComparer<TRelationship> _relationshipComparer;

        internal GroupProjectedEdgeKeyComparer(
            IEqualityComparer<TRelationship> relationshipComparer)
        {
            _relationshipComparer = relationshipComparer;
        }

        public bool Equals(
            GroupProjectedEdgeKey<TRelationship> left,
            GroupProjectedEdgeKey<TRelationship> right) =>
            left.FromProjectedNodeId == right.FromProjectedNodeId
            && left.ToProjectedNodeId == right.ToProjectedNodeId
            && _relationshipComparer.Equals(
                left.Relationship,
                right.Relationship);

        public int GetHashCode(
            GroupProjectedEdgeKey<TRelationship> value) =>
            HashCode.Combine(
                value.FromProjectedNodeId,
                value.ToProjectedNodeId,
                _relationshipComparer.GetHashCode(value.Relationship));
    }

    readonly record struct GroupWithinEdgeKey<TRelationship>(
        int ProjectedNodeId,
        TRelationship Relationship)
        where TRelationship : notnull;

    sealed class GroupWithinEdgeKeyComparer<TRelationship>
        : IEqualityComparer<GroupWithinEdgeKey<TRelationship>>
        where TRelationship : notnull
    {
        readonly IEqualityComparer<TRelationship> _relationshipComparer;

        internal GroupWithinEdgeKeyComparer(
            IEqualityComparer<TRelationship> relationshipComparer)
        {
            _relationshipComparer = relationshipComparer;
        }

        public bool Equals(
            GroupWithinEdgeKey<TRelationship> left,
            GroupWithinEdgeKey<TRelationship> right) =>
            left.ProjectedNodeId == right.ProjectedNodeId
            && _relationshipComparer.Equals(
                left.Relationship,
                right.Relationship);

        public int GetHashCode(
            GroupWithinEdgeKey<TRelationship> value) =>
            HashCode.Combine(
                value.ProjectedNodeId,
                _relationshipComparer.GetHashCode(value.Relationship));
    }

    sealed class GroupContribution<TRelationship>
        where TRelationship : notnull
    {
        internal GroupContribution(
            int fromProjectedNodeId,
            int toProjectedNodeId,
            TRelationship relationship)
        {
            FromProjectedNodeId = fromProjectedNodeId;
            ToProjectedNodeId = toProjectedNodeId;
            Relationship = relationship;
        }

        internal int FromProjectedNodeId { get; }
        internal int ToProjectedNodeId { get; }
        internal TRelationship Relationship { get; }
        internal List<int> SourceEdgeIds { get; } = [];
        internal HashSet<int> SourceOccurrenceIds { get; } = [];
        internal int FirstSourceEdgeId => SourceEdgeIds[0];

        internal void Add(GraphEdge<TRelationship> sourceEdge)
        {
            SourceEdgeIds.Add(sourceEdge.Id);
            foreach (int occurrenceId in sourceEdge.OccurrenceIds)
                SourceOccurrenceIds.Add(occurrenceId);
        }
    }
}
