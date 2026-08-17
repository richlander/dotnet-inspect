using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace DotnetInspector.Queries;

/// <summary>CallGraph-owned contracts used by the generic L1 envelope.</summary>
public static class CallGraphInspectionGraphCatalog
{
    private static InspectionGraphOccurrenceIdentityProjection
        CallOccurrenceIdentity { get; } =
        new CallOccurrenceIdentityProjection();

    public static InspectionGraphEvidenceDescriptor LogicalEdgeEvidence { get; } =
        new("call.logical-edge", InspectionGraphOwner.CallGraph);

    public static InspectionGraphEvidenceDescriptor CallSiteEvidence { get; } =
        new("call.site", InspectionGraphOwner.Analysis);

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
            CallOccurrenceIdentity,
            [CallSiteEvidence, LogicalEdgeEvidence]);

    public static InspectionGraphCharacteristicDescriptor
        OccurrenceCallKind { get; } =
        new(
            "call.occurrence.kind",
            InspectionGraphOwner.Analysis,
            InspectionGraphValueCatalog.Token,
            [InspectionGraphTargetKind.Occurrence],
            [],
            [InspectionGraphCharacteristicDerivationKind.Direct],
            InspectionGraphAggregationPolicy.None);

    public static InspectionGraphCharacteristicDescriptor
        OccurrenceILOffset { get; } =
        new(
            "call.occurrence.il-offset",
            InspectionGraphOwner.Analysis,
            InspectionGraphValueCatalog.Integer,
            [InspectionGraphTargetKind.Occurrence],
            [],
            [InspectionGraphCharacteristicDerivationKind.Direct],
            InspectionGraphAggregationPolicy.None);

    public static InspectionGraphCharacteristicDescriptor
        OccurrenceOperandToken { get; } =
        new(
            "call.occurrence.operand-token",
            InspectionGraphOwner.Analysis,
            InspectionGraphValueCatalog.Token,
            [InspectionGraphTargetKind.Occurrence],
            [],
            [InspectionGraphCharacteristicDerivationKind.Direct],
            InspectionGraphAggregationPolicy.None);

    public static InspectionGraphCharacteristicDescriptor
        OccurrenceInLoop { get; } =
        new(
            "call.occurrence.in-loop",
            InspectionGraphOwner.Analysis,
            InspectionGraphValueCatalog.Boolean,
            [InspectionGraphTargetKind.Occurrence],
            [],
            [InspectionGraphCharacteristicDerivationKind.Direct],
            InspectionGraphAggregationPolicy.None);

    public static InspectionGraphCharacteristicDescriptor
        OccurrenceDispatchKind { get; } =
        new(
            "call.occurrence.dispatch-kind",
            InspectionGraphOwner.CallGraph,
            InspectionGraphValueCatalog.Token,
            [InspectionGraphTargetKind.Occurrence],
            [],
            [InspectionGraphCharacteristicDerivationKind.Derived],
            InspectionGraphAggregationPolicy.None);

    public static InspectionGraphCharacteristicDescriptor
        EdgeCallSiteMultiplicity { get; } =
        new(
            "call.edge.call-site-count",
            InspectionGraphOwner.CallGraph,
            InspectionGraphValueCatalog.Integer,
            [InspectionGraphTargetKind.Edge],
            [],
            [InspectionGraphCharacteristicDerivationKind.Aggregated],
            InspectionGraphAggregationPolicy.DistinctOccurrenceCount);

    public static InspectionGraphCharacteristicDescriptor
        EdgeAnyInLoop { get; } =
        new(
            "call.edge.any-in-loop",
            InspectionGraphOwner.CallGraph,
            InspectionGraphValueCatalog.Boolean,
            [InspectionGraphTargetKind.Edge],
            [],
            [InspectionGraphCharacteristicDerivationKind.Aggregated],
            InspectionGraphAggregationPolicy.Any);

    public static InspectionGraphCharacteristicDescriptor
        EdgeCallKinds { get; } =
        new(
            "call.edge.call-kinds",
            InspectionGraphOwner.CallGraph,
            InspectionGraphValueCatalog.TokenSet,
            [InspectionGraphTargetKind.Edge],
            [],
            [InspectionGraphCharacteristicDerivationKind.Aggregated],
            InspectionGraphAggregationPolicy.OrderedDistinctSet);

    public static InspectionGraphCharacteristicDescriptor
        EdgeDispatchKinds { get; } =
        new(
            "call.edge.dispatch-kinds",
            InspectionGraphOwner.CallGraph,
            InspectionGraphValueCatalog.TokenSet,
            [InspectionGraphTargetKind.Edge],
            [],
            [InspectionGraphCharacteristicDerivationKind.Aggregated],
            InspectionGraphAggregationPolicy.OrderedDistinctSet);

    public static InspectionGraphLimitDescriptor TraversalIncomplete { get; } =
        new("call.traversal-incomplete", InspectionGraphOwner.CallGraph);

    public static InspectionGraphLimitDescriptor
        PhysicalOccurrencesUnavailable { get; } =
        new(
            "call.physical-occurrences-unavailable",
            InspectionGraphOwner.CallGraph);

    public static InspectionGraphFailureDescriptor AnalysisIncomplete { get; } =
        new("call.analysis-incomplete", InspectionGraphOwner.CallGraph);

    private sealed class CallOccurrenceIdentityProjection
        : InspectionGraphOccurrenceIdentityProjection
    {
        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            occurrence.Evidence switch
            {
                CallGraphCallSiteEvidence callSite =>
                    callSite.Identity,
                CallGraphLogicalEdgeEvidence =>
                    (
                        occurrence.SourceSubject,
                        occurrence.TargetSubject),
                _ => throw new ArgumentException(
                    "Unsupported call occurrence evidence.",
                    nameof(occurrence)),
            };
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

/// <summary>Typed evidence for one physical IL call site.</summary>
public sealed record CallGraphCallSiteEvidence(
    CallGraphCallSiteIdentity Identity,
    Guid CallerModuleVersionId,
    int CallerMethodToken,
    int ILOffset,
    int OperandToken,
    CallKind CallKind,
    CallGraphDispatchKind DispatchKind,
    bool InLoop)
    : IInspectionGraphOccurrenceEvidence
{
    public InspectionGraphEvidenceDescriptor Descriptor =>
        CallGraphInspectionGraphCatalog.CallSiteEvidence;
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

        var occurrences = new List<InspectionGraphOccurrence>(
            projection.CallSites.Length + projection.Rows.Length);
        var edges = new InspectionGraphEdge[projection.Rows.Length];
        var characteristics =
            new List<InspectionGraphCharacteristic>();
        var limits = new List<InspectionGraphLimit>();
        if (projection.HasUnexploredTraversalBoundary)
        {
            limits.Add(
                new InspectionGraphLimit(
                    CallGraphInspectionGraphCatalog
                        .TraversalIncomplete));
        }

        for (var index = 0; index < projection.Rows.Length; index++)
        {
            CallGraphRow row = projection.Rows[index];
            InspectionGraphSubject source =
                nodes[row.Edge.From].Subject;
            InspectionGraphSubject target =
                nodes[row.Edge.To].Subject;
            var occurrenceIds = new List<int>(
                row.Edge.CallSiteIds.Length);
            foreach (int callSiteId in row.Edge.CallSiteIds)
            {
                CallGraphCallSite callSite =
                    projection.CallSites[callSiteId];
                if (callSite.EdgeId != index)
                {
                    throw new InvalidOperationException(
                        "A projected call site does not belong to its edge.");
                }

                DirectCall call = callSite.Call;
                int occurrenceId = occurrences.Count;
                occurrenceIds.Add(occurrenceId);
                occurrences.Add(
                    new InspectionGraphOccurrence(
                        occurrenceId,
                        CallGraphInspectionGraphCatalog.Call,
                        source,
                        target,
                        new CallGraphCallSiteEvidence(
                            callSite.Identity,
                            call.Caller.ModuleVersionId,
                            call.Caller.MetadataToken,
                            call.ILOffset,
                            call.OperandToken,
                            call.Kind,
                            callSite.DispatchKind,
                            call.InLoop),
                        []));
                AddOccurrenceCharacteristics(
                    characteristics,
                    occurrenceId,
                    call,
                    callSite.DispatchKind);
            }

            bool hasCompletePhysicalOccurrences =
                HasCompletePhysicalOccurrences(row.Edge);
            if (occurrenceIds.Count == 0)
            {
                int occurrenceId = occurrences.Count;
                occurrenceIds.Add(occurrenceId);
                occurrences.Add(
                    new InspectionGraphOccurrence(
                        occurrenceId,
                        CallGraphInspectionGraphCatalog.Call,
                        source,
                        target,
                        new CallGraphLogicalEdgeEvidence(row.Number),
                        []));
            }
            else if (hasCompletePhysicalOccurrences)
            {
                AddEdgeCharacteristics(
                    characteristics,
                    index,
                    occurrenceIds,
                    row.Edge.CallSiteIds
                        .Select(id => projection.CallSites[id]));
            }
            if (!hasCompletePhysicalOccurrences)
            {
                limits.Add(
                    new InspectionGraphLimit(
                        CallGraphInspectionGraphCatalog
                            .PhysicalOccurrencesUnavailable,
                        InspectionGraphTarget.Edge(index)));
            }

            edges[index] = new InspectionGraphEdge(
                index,
                row.Edge.From,
                row.Edge.To,
                CallGraphInspectionGraphCatalog.Call,
                occurrenceIds);
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
            && projection.CallSites.All(static callSite =>
                callSite.Identity.IsPortable)
                ? InspectionGraphDocumentScope.Portable
                : InspectionGraphDocumentScope.SessionBound,
            InspectionGraphModeRequest.SingleSeed(
                nodes[projection.Focus.Id].Subject),
            nodes,
            [],
            edges,
            occurrences,
            characteristics,
            [
                new InspectionGraphSeed(
                    nodes[projection.Focus.Id].Subject,
                    InspectionGraphTarget.Node(projection.Focus.Id),
                    InspectionGraphSeedRole.Primary),
            ],
            limits,
            failures);
    }

    internal static bool HasCompletePhysicalOccurrences(
        CallGraphEdge edge) =>
        edge.CallSiteIds.Length > 0
        && !edge.HasUnavailablePhysicalOccurrences;

    private static void AddOccurrenceCharacteristics(
        List<InspectionGraphCharacteristic> characteristics,
        int occurrenceId,
        DirectCall call,
        CallGraphDispatchKind dispatchKind)
    {
        InspectionGraphTarget target =
            InspectionGraphTarget.Occurrence(occurrenceId);
        var direct = new InspectionGraphCharacteristicDerivation(
            InspectionGraphCharacteristicDerivationKind.Direct,
            []);
        characteristics.Add(
            new InspectionGraphCharacteristic(
                CallGraphInspectionGraphCatalog.OccurrenceCallKind,
                target,
                new InspectionGraphValue.Token(CallKindToken(call.Kind)),
                direct));
        characteristics.Add(
            new InspectionGraphCharacteristic(
                CallGraphInspectionGraphCatalog.OccurrenceILOffset,
                target,
                new InspectionGraphValue.Integer(call.ILOffset),
                direct));
        characteristics.Add(
            new InspectionGraphCharacteristic(
                CallGraphInspectionGraphCatalog.OccurrenceOperandToken,
                target,
                new InspectionGraphValue.Token(
                    $"0x{call.OperandToken:X8}"),
                direct));
        characteristics.Add(
            new InspectionGraphCharacteristic(
                CallGraphInspectionGraphCatalog.OccurrenceInLoop,
                target,
                new InspectionGraphValue.Boolean(call.InLoop),
                direct));
        characteristics.Add(
            new InspectionGraphCharacteristic(
                CallGraphInspectionGraphCatalog
                    .OccurrenceDispatchKind,
                target,
                new InspectionGraphValue.Token(
                    DispatchKindToken(dispatchKind)),
                new InspectionGraphCharacteristicDerivation(
                    InspectionGraphCharacteristicDerivationKind.Derived,
                    [target])));
    }

    private static void AddEdgeCharacteristics(
        List<InspectionGraphCharacteristic> characteristics,
        int edgeId,
        IReadOnlyList<int> occurrenceIds,
        IEnumerable<CallGraphCallSite> callSites)
    {
        CallGraphCallSite[] sites = callSites.ToArray();
        InspectionGraphTarget target =
            InspectionGraphTarget.Edge(edgeId);
        InspectionGraphTarget[] sources =
        [
            .. occurrenceIds.Select(
                InspectionGraphTarget.Occurrence),
        ];
        var aggregated =
            new InspectionGraphCharacteristicDerivation(
                InspectionGraphCharacteristicDerivationKind.Aggregated,
                sources);
        characteristics.Add(
            new InspectionGraphCharacteristic(
                CallGraphInspectionGraphCatalog
                    .EdgeCallSiteMultiplicity,
                target,
                new InspectionGraphValue.Integer(sites.Length),
                aggregated));
        characteristics.Add(
            new InspectionGraphCharacteristic(
                CallGraphInspectionGraphCatalog.EdgeAnyInLoop,
                target,
                new InspectionGraphValue.Boolean(
                    sites.Any(site => site.Call.InLoop)),
                aggregated));
        characteristics.Add(
            new InspectionGraphCharacteristic(
                CallGraphInspectionGraphCatalog.EdgeCallKinds,
                target,
                new InspectionGraphValue.TokenSet(
                    sites.Select(site =>
                            CallKindToken(site.Call.Kind))
                        .Distinct(StringComparer.Ordinal)),
                aggregated));
        characteristics.Add(
            new InspectionGraphCharacteristic(
                CallGraphInspectionGraphCatalog.EdgeDispatchKinds,
                target,
                new InspectionGraphValue.TokenSet(
                    sites.Select(site =>
                            DispatchKindToken(
                                site.DispatchKind))
                        .Distinct(StringComparer.Ordinal)),
                aggregated));
    }

    private static string CallKindToken(CallKind kind) =>
        kind switch
        {
            CallKind.Call => "call",
            CallKind.CallVirtual => "callvirt",
            CallKind.NewObject => "newobj",
            CallKind.LoadFunction => "ldftn",
            CallKind.LoadVirtualFunction => "ldvirtftn",
            CallKind.CallIndirect => "calli",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static string DispatchKindToken(
        CallGraphDispatchKind kind) =>
        kind switch
        {
            CallGraphDispatchKind.Direct => "direct",
            CallGraphDispatchKind.Virtual => "virtual",
            CallGraphDispatchKind.FunctionPointer =>
                "function-pointer",
            CallGraphDispatchKind.VirtualFunctionPointer =>
                "virtual-function-pointer",
            CallGraphDispatchKind.Indirect => "indirect",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

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
