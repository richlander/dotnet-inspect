using ILInspector.CallGraph;

namespace DotnetInspector.Queries;

/// <summary>CallGraph-owned contracts used by the generic L1 envelope.</summary>
public static class CallGraphInspectionGraphCatalog
{
    private static InspectionGraphOccurrenceIdentityProjection
        LogicalEdgeIdentity { get; } =
        new LogicalEdgeIdentityProjection();

    public static InspectionGraphEvidenceDescriptor LogicalEdgeEvidence { get; } =
        new("call.logical-edge", InspectionGraphOwner.CallGraph);

    public static InspectionGraphRelationshipDescriptor Call { get; } =
        new(
            "call",
            InspectionGraphOwner.CallGraph,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Member],
            InspectionGraphEndpointProjection.Exact,
            LogicalEdgeIdentity,
            [LogicalEdgeEvidence]);

    public static InspectionGraphLimitDescriptor TraversalIncomplete { get; } =
        new("call.traversal-incomplete", InspectionGraphOwner.CallGraph);

    public static InspectionGraphLimitDescriptor
        PhysicalOccurrencesUnavailable { get; } =
        new(
            "call.physical-occurrences-unavailable",
            InspectionGraphOwner.CallGraph);

    public static InspectionGraphFailureDescriptor AnalysisIncomplete { get; } =
        new("call.analysis-incomplete", InspectionGraphOwner.CallGraph);

    private sealed class LogicalEdgeIdentityProjection
        : InspectionGraphOccurrenceIdentityProjection
    {
        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            (occurrence.SourceSubject, occurrence.TargetSubject);
    }
}

/// <summary>
/// Typed receipt for one logical row emitted by the current call projection.
/// Physical call-site receipts replace this transitional evidence in the next
/// producer-owned delivery slice.
/// </summary>
public sealed record CallGraphLogicalEdgeEvidence(int RowNumber)
    : IInspectionGraphOccurrenceEvidence
{
    public InspectionGraphEvidenceDescriptor Descriptor =>
        CallGraphInspectionGraphCatalog.LogicalEdgeEvidence;
}

/// <summary>
/// Adapts the current member call projection without changing its acquisition
/// or presentation behavior.
/// </summary>
public static class CallGraphInspectionGraphAdapter
{
    public static InspectionGraphDocument Create(
        CallGraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        InspectionGraphNode[] nodes =
        [
            .. projection.Nodes.Select(node =>
                new InspectionGraphNode(
                    node.Id,
                    InspectionGraphSubject.ForMember(
                        node.Identity,
                        node.Member),
                    NodeRole(node.Kind),
                    [])),
        ];

        var occurrences =
            new InspectionGraphOccurrence[projection.Rows.Length];
        var edges = new InspectionGraphEdge[projection.Rows.Length];
        for (var index = 0; index < projection.Rows.Length; index++)
        {
            CallGraphRow row = projection.Rows[index];
            InspectionGraphSubject source =
                nodes[row.Edge.From].Subject;
            InspectionGraphSubject target =
                nodes[row.Edge.To].Subject;
            occurrences[index] = new InspectionGraphOccurrence(
                index,
                CallGraphInspectionGraphCatalog.Call,
                source,
                target,
                new CallGraphLogicalEdgeEvidence(row.Number),
                []);
            edges[index] = new InspectionGraphEdge(
                index,
                row.Edge.From,
                row.Edge.To,
                CallGraphInspectionGraphCatalog.Call,
                [index]);
        }

        var limits = new List<InspectionGraphLimit>();
        if (projection.HasUnexploredTraversalBoundary)
        {
            limits.Add(
                new InspectionGraphLimit(
                    CallGraphInspectionGraphCatalog
                        .TraversalIncomplete));
        }
        foreach (InspectionGraphEdge edge in edges)
        {
            limits.Add(
                new InspectionGraphLimit(
                    CallGraphInspectionGraphCatalog
                        .PhysicalOccurrencesUnavailable,
                    InspectionGraphTarget.Edge(edge.Id)));
        }

        InspectionGraphFailure[] failures =
            projection.HasAnalysisFailureBoundary
                ?
                [
                    new InspectionGraphFailure(
                        CallGraphInspectionGraphCatalog
                            .AnalysisIncomplete),
                ]
                : [];

        return new InspectionGraphDocument(
            projection.Nodes.All(static node =>
                node.Identity.IsPortable)
                ? InspectionGraphDocumentScope.Portable
                : InspectionGraphDocumentScope.SessionBound,
            nodes,
            [],
            edges,
            occurrences,
            [],
            [
                new InspectionGraphSeed(
                    nodes[projection.Focus.Id].Subject,
                    InspectionGraphTarget.Node(projection.Focus.Id),
                    InspectionGraphSeedRole.Primary),
            ],
            limits,
            failures);
    }

    private static InspectionGraphNodeRole NodeRole(
        CallGraphNodeKind kind) =>
        kind switch
        {
            CallGraphNodeKind.Focus =>
                InspectionGraphNodeRole.Unclassified,
            CallGraphNodeKind.Normal => InspectionGraphNodeRole.Ordinary,
            CallGraphNodeKind.External => InspectionGraphNodeRole.External,
            CallGraphNodeKind.Truncated => InspectionGraphNodeRole.Truncated,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
