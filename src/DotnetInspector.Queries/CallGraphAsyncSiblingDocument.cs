using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace DotnetInspector.Queries;

/// <summary>Analysis-owned async-sibling graph contracts.</summary>
public static class CallGraphAsyncSiblingCatalog
{
    private static InspectionGraphOccurrenceIdentityProjection
        OccurrenceIdentity { get; } =
        new AsyncSiblingOccurrenceIdentityProjection();

    public static InspectionGraphEvidenceDescriptor OpportunityEvidence
        { get; } =
        new(
            "analysis.async-sibling-opportunity.evidence",
            InspectionGraphOwner.Analysis);

    public static InspectionGraphRelationshipDescriptor Opportunity
        { get; } =
        new(
            "analysis.async-sibling-opportunity",
            InspectionGraphOwner.Analysis,
            InspectionGraphRelationshipSemantics.Derived,
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [
                new(
                    InspectionGraphSubjectKind.Member,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Member,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            OccurrenceIdentity,
            [OpportunityEvidence]);

    private sealed class AsyncSiblingOccurrenceIdentityProjection
        : InspectionGraphOccurrenceIdentityProjection
    {
        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            occurrence.Evidence switch
            {
                CallGraphAsyncSiblingOpportunityEvidence evidence =>
                    new AsyncSiblingOccurrenceIdentity(
                        evidence.Opportunity.Method,
                        evidence.Opportunity.AsyncSibling!
                            .SynchronousCall,
                        evidence.Opportunity.AsyncSibling
                            .AsyncCandidate),
                _ => throw new ArgumentException(
                    "Unsupported async-sibling occurrence evidence.",
                    nameof(occurrence)),
            };
    }

    private sealed record AsyncSiblingOccurrenceIdentity(
        MethodIdentity Source,
        DirectCall SynchronousCall,
        MemberRef AsyncCandidate);
}

/// <summary>
/// One raw Analysis opportunity retained as graph relationship evidence.
/// </summary>
public sealed record CallGraphAsyncSiblingOpportunityEvidence
    : IInspectionGraphOccurrenceEvidence
{
    public CallGraphAsyncSiblingOpportunityEvidence(
        OptimizationOpportunity opportunity)
    {
        ArgumentNullException.ThrowIfNull(opportunity);
        if (opportunity.AsyncSibling is null
            || opportunity.Shape != "sync-call-in-async")
        {
            throw new ArgumentException(
                "Async-sibling graph evidence requires one typed sync-call-in-async opportunity.",
                nameof(opportunity));
        }
        Opportunity = opportunity;
    }

    public OptimizationOpportunity Opportunity { get; }

    public InspectionGraphEvidenceDescriptor Descriptor =>
        CallGraphAsyncSiblingCatalog.OpportunityEvidence;
}

/// <summary>
/// Document-local joins for one retained async-sibling opportunity.
/// </summary>
public sealed record CallGraphAsyncSiblingRelationshipJoin
{
    public CallGraphAsyncSiblingRelationshipJoin(
        int opportunityIndex,
        InspectionGraphNodeJoin source,
        InspectionGraphNodeJoin candidate,
        InspectionGraphJoinMatch observedCallEdgeMatch,
        int? observedCallEdgeId,
        InspectionGraphJoinMatch observedCallOccurrenceMatch,
        int? observedCallOccurrenceId,
        InspectionGraphJoinMatch relationshipEdgeMatch,
        int? relationshipEdgeId,
        InspectionGraphJoinMatch relationshipOccurrenceMatch,
        int? relationshipOccurrenceId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(opportunityIndex);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidate);
        ValidateTarget(
            observedCallEdgeMatch,
            observedCallEdgeId,
            nameof(observedCallEdgeId));
        ValidateTarget(
            observedCallOccurrenceMatch,
            observedCallOccurrenceId,
            nameof(observedCallOccurrenceId));
        ValidateTarget(
            relationshipEdgeMatch,
            relationshipEdgeId,
            nameof(relationshipEdgeId));
        ValidateTarget(
            relationshipOccurrenceMatch,
            relationshipOccurrenceId,
            nameof(relationshipOccurrenceId));
        if (observedCallOccurrenceMatch
                == InspectionGraphJoinMatch.Found
            && observedCallEdgeMatch
                != InspectionGraphJoinMatch.Found)
        {
            throw new ArgumentException(
                "A physical call occurrence cannot join without its logical call edge.",
                nameof(observedCallOccurrenceMatch));
        }
        if ((relationshipEdgeMatch
                    == InspectionGraphJoinMatch.Found)
            != (relationshipOccurrenceMatch
                    == InspectionGraphJoinMatch.Found))
        {
            throw new ArgumentException(
                "An async-sibling relationship edge and occurrence must join together.");
        }
        if (relationshipOccurrenceMatch
                == InspectionGraphJoinMatch.Found
            && (source.Match != InspectionGraphJoinMatch.Found
                || candidate.Match
                    != InspectionGraphJoinMatch.Found
                || observedCallOccurrenceMatch
                    != InspectionGraphJoinMatch.Found))
        {
            throw new ArgumentException(
                "A projected async-sibling relationship requires both endpoints and its observed call occurrence.");
        }

        OpportunityIndex = opportunityIndex;
        Source = source;
        Candidate = candidate;
        ObservedCallEdgeMatch = observedCallEdgeMatch;
        ObservedCallEdgeId = observedCallEdgeId;
        ObservedCallOccurrenceMatch =
            observedCallOccurrenceMatch;
        ObservedCallOccurrenceId =
            observedCallOccurrenceId;
        RelationshipEdgeMatch = relationshipEdgeMatch;
        RelationshipEdgeId = relationshipEdgeId;
        RelationshipOccurrenceMatch =
            relationshipOccurrenceMatch;
        RelationshipOccurrenceId =
            relationshipOccurrenceId;
    }

    public int OpportunityIndex { get; }
    public InspectionGraphNodeJoin Source { get; }
    public InspectionGraphNodeJoin Candidate { get; }
    public InspectionGraphJoinMatch ObservedCallEdgeMatch { get; }
    public int? ObservedCallEdgeId { get; }
    public InspectionGraphJoinMatch ObservedCallOccurrenceMatch
        { get; }
    public int? ObservedCallOccurrenceId { get; }
    public InspectionGraphJoinMatch RelationshipEdgeMatch { get; }
    public int? RelationshipEdgeId { get; }
    public InspectionGraphJoinMatch RelationshipOccurrenceMatch
        { get; }
    public int? RelationshipOccurrenceId { get; }

    static void ValidateTarget(
        InspectionGraphJoinMatch match,
        int? id,
        string parameterName)
    {
        if (!Enum.IsDefined(match))
            throw new ArgumentOutOfRangeException(nameof(match));
        if ((match == InspectionGraphJoinMatch.Found)
            != id.HasValue)
        {
            throw new ArgumentException(
                "A found graph join requires one target id; every other outcome requires none.",
                parameterName);
        }
        if (id < 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }
}

/// <summary>
/// One call graph composed with raw Analysis async-sibling evidence.
/// </summary>
public sealed class CallGraphAsyncSiblingDocument
{
    internal CallGraphAsyncSiblingDocument(
        InspectionGraphDocument graph,
        LibraryBodyAnalysisReceipt receipt,
        IEnumerable<OptimizationOpportunity> opportunities,
        IEnumerable<CallGraphAsyncSiblingRelationshipJoin>
            relationshipJoins)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures
                    .AsyncSiblingOpportunities))
        {
            throw new ArgumentException(
                "An async-sibling graph document requires requested async-sibling evidence.",
                nameof(receipt));
        }

        Graph = graph;
        Receipt = receipt;
        Opportunities = InspectionGraphCollections.Snapshot(
            opportunities,
            nameof(opportunities));
        RelationshipJoins =
            InspectionGraphCollections.Snapshot(
                relationshipJoins,
                nameof(relationshipJoins));
        ValidateJoins();
    }

    public InspectionGraphDocument Graph { get; }
    public LibraryBodyAnalysisReceipt Receipt { get; }
    public bool HasFullMethodEvidenceScope =>
        Receipt.HasFullMethodEvidenceScope;
    public ImmutableArray<OptimizationOpportunity> Opportunities
        { get; }
    public ImmutableArray<CallGraphAsyncSiblingRelationshipJoin>
        RelationshipJoins { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics =>
        Receipt.Diagnostics;

    void ValidateJoins()
    {
        int[] expected =
        [
            .. Opportunities
                .Select((opportunity, index) => (
                    Opportunity: opportunity,
                    Index: index))
                .Where(static item =>
                    item.Opportunity.AsyncSibling is not null)
                .Select(static item => item.Index),
        ];
        if (!RelationshipJoins
            .Select(static join => join.OpportunityIndex)
            .SequenceEqual(expected))
        {
            throw new ArgumentException(
                "Every typed async-sibling opportunity requires one ordered graph join.",
                nameof(RelationshipJoins));
        }

        foreach (CallGraphAsyncSiblingRelationshipJoin join
            in RelationshipJoins)
        {
            ValidateNodeJoin(join.Source);
            ValidateNodeJoin(join.Candidate);
            ValidateTarget(
                join.ObservedCallEdgeId,
                Graph.Edges.Length,
                "Observed call edge");
            ValidateTarget(
                join.ObservedCallOccurrenceId,
                Graph.Occurrences.Length,
                "Observed call occurrence");
            ValidateTarget(
                join.RelationshipEdgeId,
                Graph.Edges.Length,
                "Async-sibling relationship edge");
            ValidateTarget(
                join.RelationshipOccurrenceId,
                Graph.Occurrences.Length,
                "Async-sibling relationship occurrence");
            if (join.ObservedCallEdgeId is int callEdgeId
                && !ReferenceEquals(
                    Graph.Edges[callEdgeId].Relationship,
                    CallGraphInspectionGraphCatalog.Call))
            {
                throw new ArgumentException(
                    "An observed call join must target an ordinary call edge.",
                    nameof(RelationshipJoins));
            }
            if (join.ObservedCallOccurrenceId
                    is int callOccurrenceId
                && (join.ObservedCallEdgeId is not int edgeId
                    || !Graph.Edges[edgeId].OccurrenceIds.Contains(
                        callOccurrenceId)))
            {
                throw new ArgumentException(
                    "An observed call occurrence must support its joined call edge.",
                    nameof(RelationshipJoins));
            }
            if (join.RelationshipOccurrenceId
                    is int relationshipOccurrenceId)
            {
                int relationshipEdgeId =
                    join.RelationshipEdgeId!.Value;
                InspectionGraphOccurrence occurrence =
                    Graph.Occurrences[relationshipOccurrenceId];
                if (!ReferenceEquals(
                        Graph.Edges[relationshipEdgeId]
                            .Relationship,
                        CallGraphAsyncSiblingCatalog.Opportunity)
                    || !Graph.Edges[relationshipEdgeId]
                        .OccurrenceIds.Contains(
                            relationshipOccurrenceId)
                    || !occurrence.DerivedFromOccurrenceIds
                        .SequenceEqual(
                        [join.ObservedCallOccurrenceId!.Value]))
                {
                    throw new ArgumentException(
                        "An async-sibling occurrence must derive from its joined observed call occurrence.",
                        nameof(RelationshipJoins));
                }
            }
        }
    }

    void ValidateNodeJoin(InspectionGraphNodeJoin join)
    {
        ArgumentNullException.ThrowIfNull(join);
        ValidateTarget(join.NodeId, Graph.Nodes.Length, "Node");
    }

    static void ValidateTarget(
        int? id,
        int count,
        string kind)
    {
        if (id is int value && (uint)value >= (uint)count)
        {
            throw new ArgumentException(
                $"{kind} join id {value} is outside the document.");
        }
    }
}

/// <summary>
/// Composes already-produced async-sibling evidence with one call projection.
/// </summary>
public static class CallGraphAsyncSiblingDocumentAdapter
{
    public static CallGraphAsyncSiblingDocument Create(
        CallGraphProjection projection,
        LibraryOptimizationAnalysisResult optimization)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(optimization);
        if (!optimization.Receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures
                    .AsyncSiblingOpportunities))
        {
            throw new InvalidOperationException(
                "Async-sibling opportunities were not requested for this Analysis execution.");
        }

        InspectionGraphDocument baseline =
            CallGraphInspectionGraphAdapter.Create(projection);
        ImmutableArray<OptimizationOpportunity> opportunities =
            optimization.Opportunities;
        var pending =
            new List<MutableRelationshipJoin>();
        var contributions =
            new List<InspectionGraphRelationshipContribution>();

        for (var index = 0;
            index < opportunities.Length;
            index++)
        {
            OptimizationOpportunity opportunity =
                opportunities[index];
            if (opportunity.AsyncSibling is not
                    AsyncSiblingOpportunityEvidence evidence)
            {
                continue;
            }

            InspectionGraphNodeJoin source =
                FindSourceNode(
                    projection,
                    opportunity.Method);
            InspectionGraphNodeJoin candidate =
                FindCandidateNode(
                    projection,
                    evidence.AsyncCandidate);
            CallOccurrenceJoin call =
                FindObservedCall(
                    projection,
                    baseline,
                    source,
                    evidence.SynchronousCall);
            var join = new MutableRelationshipJoin(
                index,
                source,
                candidate,
                call);
            pending.Add(join);

            if (source.NodeId is not int sourceNodeId
                || candidate.NodeId is not int candidateNodeId
                || call.OccurrenceId is not
                    int callOccurrenceId)
            {
                continue;
            }

            join.ContributionIndex = contributions.Count;
            contributions.Add(
                new InspectionGraphRelationshipContribution(
                    sourceNodeId,
                    candidateNodeId,
                    CallGraphAsyncSiblingCatalog.Opportunity,
                    baseline.Nodes[sourceNodeId].Subject,
                    baseline.Nodes[candidateNodeId].Subject,
                    new CallGraphAsyncSiblingOpportunityEvidence(
                        opportunity),
                    [callOccurrenceId]));
        }

        InspectionGraphRelationshipComposition composition =
            InspectionGraphRelationshipComposer.Compose(
                baseline,
                contributions);
        foreach (MutableRelationshipJoin join in pending)
        {
            if (join.ContributionIndex is not int contributionIndex)
                continue;
            InspectionGraphRelationshipContributionJoin composed =
                composition.Joins[contributionIndex];
            join.RelationshipEdgeId = composed.EdgeId;
            join.RelationshipOccurrenceId =
                composed.OccurrenceId;
        }

        return new CallGraphAsyncSiblingDocument(
            composition.Document,
            optimization.Receipt,
            opportunities,
            pending.Select(static join => join.Freeze()));
    }

    static InspectionGraphNodeJoin FindSourceNode(
        CallGraphProjection projection,
        MethodIdentity source)
    {
        CallGraphNodeMatch match =
            projection.FindNode(
                source,
                out CallGraphNode node);
        return new(
            JoinMatch(match),
            match == CallGraphNodeMatch.Found
                ? node.Id
                : null);
    }

    static InspectionGraphNodeJoin FindCandidateNode(
        CallGraphProjection projection,
        MemberRef candidate)
    {
        CallGraphNode[] exact =
        [
            .. projection.Nodes.Where(node =>
                node.Member == candidate),
        ];
        if (exact.Length == 1)
        {
            return new(
                InspectionGraphJoinMatch.Found,
                exact[0].Id);
        }
        if (exact.Length > 1)
        {
            return new(
                InspectionGraphJoinMatch.Ambiguous,
                nodeId: null);
        }

        GraphNodeIdentity identity =
            GraphNodeIdentity.FromMember(candidate);
        CallGraphNode[] structural =
        [
            .. projection.Nodes.Where(node =>
                GraphNodeIdentity.FromMember(node.Member)
                    == identity),
        ];
        return structural.Length switch
        {
            1 => new(
                InspectionGraphJoinMatch.Found,
                structural[0].Id),
            > 1 => new(
                InspectionGraphJoinMatch.Ambiguous,
                nodeId: null),
            _ => new(
                InspectionGraphJoinMatch.NotProjected,
                nodeId: null),
        };
    }

    static CallOccurrenceJoin FindObservedCall(
        CallGraphProjection projection,
        InspectionGraphDocument graph,
        InspectionGraphNodeJoin source,
        DirectCall call)
    {
        if (source.NodeId is not int sourceNodeId)
        {
            return new(
                InspectionGraphJoinMatch.NotProjected,
                EdgeId: null,
                InspectionGraphJoinMatch.NotProjected,
                OccurrenceId: null);
        }

        CallGraphRowMatch rowMatch =
            projection.FindCalleeRow(
                sourceNodeId,
                call,
                out CallGraphRow row);
        if (rowMatch != CallGraphRowMatch.Found)
        {
            return new(
                JoinMatch(rowMatch),
                EdgeId: null,
                InspectionGraphJoinMatch.NotProjected,
                OccurrenceId: null);
        }

        int edgeId = projection.Rows.IndexOf(row);
        if (edgeId < 0)
        {
            throw new InvalidOperationException(
                "A found call row is not retained by its projection.");
        }
        int[] callSiteIds =
        [
            .. row.Edge.CallSiteIds.Where(callSiteId =>
                SamePhysicalCall(
                    projection.CallSites[callSiteId].Call,
                    call)),
        ];
        if (callSiteIds.Length != 1)
        {
            return new(
                InspectionGraphJoinMatch.Found,
                edgeId,
                callSiteIds.Length == 0
                    ? InspectionGraphJoinMatch.NotProjected
                    : InspectionGraphJoinMatch.Ambiguous,
                OccurrenceId: null);
        }

        CallGraphCallSite callSite =
            projection.CallSites[callSiteIds[0]];
        int[] occurrenceIds =
        [
            .. graph.Edges[edgeId].OccurrenceIds.Where(
                occurrenceId =>
                    graph.Occurrences[occurrenceId].Evidence
                        is CallGraphCallSiteEvidence evidence
                    && evidence.Identity.Equals(
                        callSite.Identity)),
        ];
        if (occurrenceIds.Length != 1)
        {
            throw new InvalidOperationException(
                "The inspection graph did not retain exactly one occurrence for a projected physical call site.");
        }

        return new(
            InspectionGraphJoinMatch.Found,
            edgeId,
            InspectionGraphJoinMatch.Found,
            occurrenceIds[0]);
    }

    static InspectionGraphJoinMatch JoinMatch(
        CallGraphNodeMatch match) =>
        match switch
        {
            CallGraphNodeMatch.Found =>
                InspectionGraphJoinMatch.Found,
            CallGraphNodeMatch.NotProjected =>
                InspectionGraphJoinMatch.NotProjected,
            CallGraphNodeMatch.Ambiguous =>
                InspectionGraphJoinMatch.Ambiguous,
            _ => throw new ArgumentOutOfRangeException(
                nameof(match)),
        };

    static InspectionGraphJoinMatch JoinMatch(
        CallGraphRowMatch match) =>
        match switch
        {
            CallGraphRowMatch.Found =>
                InspectionGraphJoinMatch.Found,
            CallGraphRowMatch.NotProjected =>
                InspectionGraphJoinMatch.NotProjected,
            CallGraphRowMatch.Ambiguous =>
                InspectionGraphJoinMatch.Ambiguous,
            _ => throw new ArgumentOutOfRangeException(
                nameof(match)),
        };

    static bool SamePhysicalCall(
        DirectCall first,
        DirectCall second) =>
        first.EvidenceMethod.AssemblyName
            == second.EvidenceMethod.AssemblyName
        && first.EvidenceMethod.ModuleVersionId
            == second.EvidenceMethod.ModuleVersionId
        && first.EvidenceMethod.MetadataToken
            == second.EvidenceMethod.MetadataToken
        && first.ILOffset == second.ILOffset
        && first.OperandToken == second.OperandToken;

    sealed record CallOccurrenceJoin(
        InspectionGraphJoinMatch EdgeMatch,
        int? EdgeId,
        InspectionGraphJoinMatch OccurrenceMatch,
        int? OccurrenceId);

    sealed class MutableRelationshipJoin(
        int opportunityIndex,
        InspectionGraphNodeJoin source,
        InspectionGraphNodeJoin candidate,
        CallOccurrenceJoin observedCall)
    {
        internal int? ContributionIndex { get; set; }
        internal int? RelationshipEdgeId { get; set; }
        internal int? RelationshipOccurrenceId { get; set; }

        internal CallGraphAsyncSiblingRelationshipJoin Freeze() =>
            new(
                opportunityIndex,
                source,
                candidate,
                observedCall.EdgeMatch,
                observedCall.EdgeId,
                observedCall.OccurrenceMatch,
                observedCall.OccurrenceId,
                RelationshipEdgeId.HasValue
                    ? InspectionGraphJoinMatch.Found
                    : InspectionGraphJoinMatch.NotProjected,
                RelationshipEdgeId,
                RelationshipOccurrenceId.HasValue
                    ? InspectionGraphJoinMatch.Found
                    : InspectionGraphJoinMatch.NotProjected,
                RelationshipOccurrenceId);
    }
}
