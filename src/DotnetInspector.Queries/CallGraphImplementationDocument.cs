using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace DotnetInspector.Queries;

/// <summary>The outcome of one document-local evidence join.</summary>
public enum CallGraphImplementationJoinMatch
{
    Found,
    NotProjected,
    Ambiguous,
}

/// <summary>
/// The outcome of joining one exact method identity to a graph node.
/// </summary>
public sealed record CallGraphMethodNodeJoin
{
    public CallGraphMethodNodeJoin(
        CallGraphImplementationJoinMatch match,
        int? nodeId)
    {
        if (!Enum.IsDefined(match))
            throw new ArgumentOutOfRangeException(nameof(match));
        if ((match == CallGraphImplementationJoinMatch.Found)
            != nodeId.HasValue)
        {
            throw new ArgumentException(
                "A found method join requires one node id; every other outcome requires none.",
                nameof(nodeId));
        }
        if (nodeId < 0)
            throw new ArgumentOutOfRangeException(nameof(nodeId));

        Match = match;
        NodeId = nodeId;
    }

    public CallGraphImplementationJoinMatch Match { get; }
    public int? NodeId { get; }
}

/// <summary>
/// Document-local graph joins for one retained implementation profile.
/// </summary>
public sealed record CallGraphImplementationProfileJoin(
    int ProfileIndex,
    CallGraphMethodNodeJoin LogicalOwner,
    CallGraphMethodNodeJoin EvidenceMethod);

/// <summary>
/// Document-local graph joins for one retained overload call.
/// </summary>
public sealed record CallGraphOverloadRelationshipJoin
{
    public CallGraphOverloadRelationshipJoin(
        int relationshipIndex,
        CallGraphMethodNodeJoin caller,
        CallGraphMethodNodeJoin callee,
        CallGraphImplementationJoinMatch edgeMatch,
        int? edgeId,
        CallGraphImplementationJoinMatch occurrenceMatch,
        int? occurrenceId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(relationshipIndex);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(callee);
        ValidateTarget(edgeMatch, edgeId, nameof(edgeId));
        ValidateTarget(
            occurrenceMatch,
            occurrenceId,
            nameof(occurrenceId));
        if (occurrenceMatch == CallGraphImplementationJoinMatch.Found
            && edgeMatch != CallGraphImplementationJoinMatch.Found)
        {
            throw new ArgumentException(
                "A physical occurrence cannot be joined without its logical edge.",
                nameof(occurrenceMatch));
        }

        RelationshipIndex = relationshipIndex;
        Caller = caller;
        Callee = callee;
        EdgeMatch = edgeMatch;
        EdgeId = edgeId;
        OccurrenceMatch = occurrenceMatch;
        OccurrenceId = occurrenceId;
    }

    public int RelationshipIndex { get; }
    public CallGraphMethodNodeJoin Caller { get; }
    public CallGraphMethodNodeJoin Callee { get; }
    public CallGraphImplementationJoinMatch EdgeMatch { get; }
    public int? EdgeId { get; }
    public CallGraphImplementationJoinMatch OccurrenceMatch { get; }
    public int? OccurrenceId { get; }

    static void ValidateTarget(
        CallGraphImplementationJoinMatch match,
        int? id,
        string parameterName)
    {
        if (!Enum.IsDefined(match))
            throw new ArgumentOutOfRangeException(nameof(match));
        if ((match == CallGraphImplementationJoinMatch.Found)
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
/// One typed call graph correlated with existing compiled implementation
/// evidence. The graph remains the authoritative topology.
/// </summary>
public sealed class CallGraphImplementationDocument
{
    internal CallGraphImplementationDocument(
        InspectionGraphDocument graph,
        LibraryBodyAnalysisReceipt receipt,
        ImplementationProfilePopulationCoverageReceipt coverage,
        IEnumerable<MethodImplementationProfile> profiles,
        IEnumerable<OverloadCallRelationship> overloadRelationships,
        IEnumerable<CallGraphImplementationProfileJoin> profileJoins,
        IEnumerable<CallGraphOverloadRelationshipJoin>
            overloadRelationshipJoins)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(coverage);
        if (!receipt.Features.HasFlag(
                LibraryBodyAnalysisFeatures.ImplementationProfiles)
            || !coverage.WasRequested)
        {
            throw new ArgumentException(
                "A call-graph implementation document requires requested implementation-profile evidence.",
                nameof(coverage));
        }

        Graph = graph;
        Receipt = receipt;
        Coverage = coverage;
        Profiles = InspectionGraphCollections.Snapshot(
            profiles,
            nameof(profiles));
        OverloadRelationships = InspectionGraphCollections.Snapshot(
            overloadRelationships,
            nameof(overloadRelationships));
        ProfileJoins = InspectionGraphCollections.Snapshot(
            profileJoins,
            nameof(profileJoins));
        OverloadRelationshipJoins = InspectionGraphCollections.Snapshot(
            overloadRelationshipJoins,
            nameof(overloadRelationshipJoins));

        ValidateProfileJoins();
        ValidateRelationshipJoins();
    }

    public InspectionGraphDocument Graph { get; }
    public LibraryBodyAnalysisReceipt Receipt { get; }
    public ImplementationProfilePopulationCoverageReceipt Coverage { get; }
    public ImmutableArray<MethodImplementationProfile> Profiles { get; }
    public ImmutableArray<OverloadCallRelationship>
        OverloadRelationships { get; }
    public ImmutableArray<CallGraphImplementationProfileJoin>
        ProfileJoins { get; }
    public ImmutableArray<CallGraphOverloadRelationshipJoin>
        OverloadRelationshipJoins { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics =>
        Receipt.Diagnostics;

    void ValidateProfileJoins()
    {
        if (ProfileJoins.Length != Profiles.Length)
        {
            throw new ArgumentException(
                "Every implementation profile requires exactly one graph join.",
                nameof(ProfileJoins));
        }

        for (var index = 0; index < ProfileJoins.Length; index++)
        {
            CallGraphImplementationProfileJoin join =
                ProfileJoins[index];
            if (join.ProfileIndex != index)
            {
                throw new ArgumentException(
                    "Implementation-profile joins must use dense profile order.",
                    nameof(ProfileJoins));
            }
            ValidateNodeJoin(join.LogicalOwner);
            ValidateNodeJoin(join.EvidenceMethod);
        }
    }

    void ValidateRelationshipJoins()
    {
        if (OverloadRelationshipJoins.Length
            != OverloadRelationships.Length)
        {
            throw new ArgumentException(
                "Every overload relationship requires exactly one graph join.",
                nameof(OverloadRelationshipJoins));
        }

        for (var index = 0;
            index < OverloadRelationshipJoins.Length;
            index++)
        {
            CallGraphOverloadRelationshipJoin join =
                OverloadRelationshipJoins[index];
            if (join.RelationshipIndex != index)
            {
                throw new ArgumentException(
                    "Overload-relationship joins must use dense relationship order.",
                    nameof(OverloadRelationshipJoins));
            }
            ValidateNodeJoin(join.Caller);
            ValidateNodeJoin(join.Callee);
            if (join.EdgeId is int edgeId)
                ValidateId(edgeId, Graph.Edges.Length, "Edge");
            if (join.OccurrenceId is int occurrenceId)
            {
                ValidateId(
                    occurrenceId,
                    Graph.Occurrences.Length,
                    "Occurrence");
                if (join.EdgeId is not int joinedEdgeId
                    || !Graph.Edges[joinedEdgeId].OccurrenceIds
                        .Contains(occurrenceId))
                {
                    throw new ArgumentException(
                        "A joined overload occurrence must support its joined edge.",
                        nameof(OverloadRelationshipJoins));
                }
            }
        }
    }

    void ValidateNodeJoin(CallGraphMethodNodeJoin join)
    {
        ArgumentNullException.ThrowIfNull(join);
        if (join.NodeId is int nodeId)
            ValidateId(nodeId, Graph.Nodes.Length, "Node");
    }

    static void ValidateId(
        int id,
        int count,
        string kind)
    {
        if ((uint)id >= (uint)count)
        {
            throw new ArgumentException(
                $"{kind} join id {id} is outside the document.");
        }
    }

}

/// <summary>
/// Correlates one call projection with already-produced implementation
/// evidence without changing either producer's semantics.
/// </summary>
public static class CallGraphImplementationDocumentAdapter
{
    public static CallGraphImplementationDocument Create(
        CallGraphProjection projection,
        LibraryImplementationProfileAnalysisResult implementationProfiles)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(implementationProfiles);
        if (!implementationProfiles.WasRequested)
        {
            throw new InvalidOperationException(
                "Implementation profiles were not requested for this Analysis execution.");
        }

        InspectionGraphDocument graph =
            CallGraphInspectionGraphAdapter.Create(projection);
        ImmutableArray<CallGraphImplementationProfileJoin> profileJoins =
        [
            .. implementationProfiles.Profiles.Select(
                (profile, index) =>
                    new CallGraphImplementationProfileJoin(
                        index,
                        FindNode(projection, profile.Method),
                        FindNode(
                            projection,
                            profile.EvidenceMethod))),
        ];
        ImmutableArray<CallGraphOverloadRelationshipJoin>
            relationshipJoins =
        [
            .. implementationProfiles.OverloadRelationships.Select(
                (relationship, index) =>
                    JoinRelationship(
                        projection,
                        graph,
                        relationship,
                        index)),
        ];

        return new CallGraphImplementationDocument(
            graph,
            implementationProfiles.Receipt,
            implementationProfiles.Coverage,
            implementationProfiles.Profiles,
            implementationProfiles.OverloadRelationships,
            profileJoins,
            relationshipJoins);
    }

    static CallGraphMethodNodeJoin FindNode(
        CallGraphProjection projection,
        MethodIdentity method)
    {
        CallGraphNodeMatch match =
            projection.FindNode(method, out CallGraphNode node);
        return new(
            JoinMatch(match),
            match == CallGraphNodeMatch.Found
                ? node.Id
                : null);
    }

    static CallGraphOverloadRelationshipJoin JoinRelationship(
        CallGraphProjection projection,
        InspectionGraphDocument graph,
        OverloadCallRelationship relationship,
        int relationshipIndex)
    {
        CallGraphMethodNodeJoin caller =
            FindNode(projection, relationship.Caller);
        CallGraphMethodNodeJoin callee =
            FindNode(projection, relationship.Callee);
        if (caller.NodeId is not int callerId
            || callee.NodeId is not int calleeId)
        {
            return new(
                relationshipIndex,
                caller,
                callee,
                CallGraphImplementationJoinMatch.NotProjected,
                edgeId: null,
                CallGraphImplementationJoinMatch.NotProjected,
                occurrenceId: null);
        }

        int[] edgeIds =
        [
            .. projection.Edges
                .Select((edge, index) => (Edge: edge, Index: index))
                .Where(item =>
                    item.Edge.From == callerId
                    && item.Edge.To == calleeId)
                .Select(static item => item.Index),
        ];
        if (edgeIds.Length != 1)
        {
            return new(
                relationshipIndex,
                caller,
                callee,
                edgeIds.Length == 0
                    ? CallGraphImplementationJoinMatch.NotProjected
                    : CallGraphImplementationJoinMatch.Ambiguous,
                edgeId: null,
                CallGraphImplementationJoinMatch.NotProjected,
                occurrenceId: null);
        }

        int edgeId = edgeIds[0];
        CallGraphEdge edge = projection.Edges[edgeId];
        int[] callSiteIds =
        [
            .. edge.CallSiteIds.Where(callSiteId =>
                Matches(
                    projection.CallSites[callSiteId].Call,
                    relationship)),
        ];
        if (callSiteIds.Length != 1)
        {
            return new(
                relationshipIndex,
                caller,
                callee,
                CallGraphImplementationJoinMatch.Found,
                edgeId,
                callSiteIds.Length == 0
                    ? CallGraphImplementationJoinMatch.NotProjected
                    : CallGraphImplementationJoinMatch.Ambiguous,
                occurrenceId: null);
        }

        CallGraphCallSite callSite =
            projection.CallSites[callSiteIds[0]];
        int[] occurrenceIds =
        [
            .. graph.Edges[edgeId].OccurrenceIds.Where(
                occurrenceId =>
                    graph.Occurrences[occurrenceId].Evidence
                        is CallGraphCallSiteEvidence evidence
                    && evidence.Identity.Equals(callSite.Identity)),
        ];
        if (occurrenceIds.Length != 1)
        {
            throw new InvalidOperationException(
                "The inspection graph did not retain exactly one occurrence for a projected physical call site.");
        }

        return new(
            relationshipIndex,
            caller,
            callee,
            CallGraphImplementationJoinMatch.Found,
            edgeId,
            CallGraphImplementationJoinMatch.Found,
            occurrenceIds[0]);
    }

    static CallGraphImplementationJoinMatch JoinMatch(
        CallGraphNodeMatch match) =>
        match switch
        {
            CallGraphNodeMatch.Found =>
                CallGraphImplementationJoinMatch.Found,
            CallGraphNodeMatch.NotProjected =>
                CallGraphImplementationJoinMatch.NotProjected,
            CallGraphNodeMatch.Ambiguous =>
                CallGraphImplementationJoinMatch.Ambiguous,
            _ => throw new ArgumentOutOfRangeException(nameof(match)),
        };

    static bool Matches(
        DirectCall call,
        OverloadCallRelationship relationship) =>
        call.Caller == relationship.Caller
        && call.EvidenceMethod == relationship.EvidenceMethod
        && call.ILOffset == relationship.ILOffset
        && call.Kind == relationship.Kind;
}
