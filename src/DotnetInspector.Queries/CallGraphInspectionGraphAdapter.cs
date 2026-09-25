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

    public static InspectionGraphEvidenceDescriptor
        TraversalNodeBoundEvidence { get; } =
        new("call.traversal-node-bound", InspectionGraphOwner.CallGraph);

    public static InspectionGraphLimitDescriptor TraversalNodeBound { get; } =
        new(
            "call.traversal-node-bound",
            InspectionGraphOwner.CallGraph,
            [TraversalNodeBoundEvidence]);

    public static InspectionGraphEvidenceDescriptor
        CorrespondenceIncompleteEvidence { get; } =
        new(
            "call.correspondence-incomplete",
            InspectionGraphOwner.CallGraph);

    public static InspectionGraphLimitDescriptor
        CorrespondenceIncomplete { get; } =
        new(
            "call.correspondence-incomplete",
            InspectionGraphOwner.CallGraph,
            [CorrespondenceIncompleteEvidence]);

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

/// <summary>The maximum topology nodes admitted by one call traversal.</summary>
public sealed record CallGraphTraversalNodeBoundEvidence
    : IInspectionGraphDiagnosticEvidence
{
    public CallGraphTraversalNodeBoundEvidence(int maxNodes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);
        MaxNodes = maxNodes;
    }

    public int MaxNodes { get; }

    public InspectionGraphEvidenceDescriptor Descriptor =>
        CallGraphInspectionGraphCatalog.TraversalNodeBoundEvidence;
}

/// <summary>Counts of call correspondence that could not be completed.</summary>
public sealed record CallGraphCorrespondenceIncompleteEvidence
    : IInspectionGraphDiagnosticEvidence
{
    public CallGraphCorrespondenceIncompleteEvidence(
        int incompleteNodeCount,
        int incompleteEdgeCount,
        int bindingIdentityConflictCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(incompleteNodeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(incompleteEdgeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            bindingIdentityConflictCount);
        if (incompleteNodeCount == 0
            && incompleteEdgeCount == 0
            && bindingIdentityConflictCount == 0)
        {
            throw new ArgumentException(
                "Incomplete correspondence evidence requires a nonzero count.");
        }

        IncompleteNodeCount = incompleteNodeCount;
        IncompleteEdgeCount = incompleteEdgeCount;
        BindingIdentityConflictCount = bindingIdentityConflictCount;
    }

    public int IncompleteNodeCount { get; }
    public int IncompleteEdgeCount { get; }
    public int BindingIdentityConflictCount { get; }

    public InspectionGraphEvidenceDescriptor Descriptor =>
        CallGraphInspectionGraphCatalog.CorrespondenceIncompleteEvidence;
}

/// <summary>
/// Queries-owned composition contracts for an external-focused call graph.
/// </summary>
public static class ExternalFocusedCallGraphInspectionCatalog
{
    public static InspectionGraphCharacteristicDescriptor EdgeRole { get; } =
        new(
            "queries.call.external-focus-role",
            InspectionGraphOwner.Queries,
            InspectionGraphValueCatalog.Token,
            [InspectionGraphTargetKind.Edge],
            [],
            [InspectionGraphCharacteristicDerivationKind.Derived],
            InspectionGraphAggregationPolicy.None);

    public static InspectionGraphLimitDescriptor
        BoundaryClassificationIncomplete { get; } =
        new(
            "queries.call.external-boundary-classification-incomplete",
            InspectionGraphOwner.Queries);
}

/// <summary>
/// Adapts member call projections into the shared L1 graph document.
/// </summary>
public static class CallGraphInspectionGraphAdapter
{
    public static InspectionGraphDocument Create(
        CallGraphProjection projection)
        => Create(projection, CatalogCallGraphDiagnostics.Empty);

    public static InspectionGraphDocument Create(
        CallGraphProjection projection,
        CatalogCallGraphDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(diagnostics);
        InspectionGraphSubject[] roots = RootSubjects(projection);
        InspectionGraphModeRequest modeRequest =
            roots.Length == 1
                ? InspectionGraphModeRequest.SingleSeed(roots[0])
                : InspectionGraphModeRequest.PeerSeeds(roots);
        InspectionGraphLimit[] limits =
            diagnostics.IsIncomplete
                ?
                [
                    new(
                        CallGraphInspectionGraphCatalog
                            .CorrespondenceIncomplete,
                        InspectionGraphTarget.Node(projection.Focus.Id),
                        new CallGraphCorrespondenceIncompleteEvidence(
                            diagnostics.IncompleteNodeCount,
                            diagnostics.IncompleteEdgeCount,
                            diagnostics.BindingIdentityConflictCount)),
                ]
                : [];
        return Create(
            projection,
            modeRequest,
            limits);
    }

    internal static InspectionGraphDocument
        CreateEqualRootOutgoingNeighborhood(
        CallGraphProjection projection,
        int maxDepth,
        int maxNodes)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 2);
        InspectionGraphSubject[] roots = RootSubjects(projection);
        if (roots.Length < 2)
        {
            throw new ArgumentException(
                "An equal-root neighborhood requires at least two call-graph roots.",
                nameof(projection));
        }
        if (maxNodes < roots.Length
            || projection.Nodes.Length > maxNodes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxNodes),
                "The combined node bound must retain every equal root and the supplied projection.");
        }

        InspectionGraphNeighborhoodRequest request =
            InspectionGraphNeighborhoodRequest.PeerSeeds(
                roots,
                [CallGraphInspectionGraphCatalog.Call],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth);
        InspectionGraphDocument source = Create(
            projection,
            request.ModeRequest,
            [
                new InspectionGraphLimit(
                    CallGraphInspectionGraphCatalog.TraversalNodeBound,
                    Evidence:
                        new CallGraphTraversalNodeBoundEvidence(maxNodes)),
            ]);
        return InspectionGraphNeighborhoodProjection.Project(
            source,
            request);
    }

    internal static InspectionGraphDocument
        CreateExternalFocusedOutgoingNeighborhood(
        ExternalFocusedCallGraphProjection projection,
        int maxDepth,
        int maxNodes,
        CatalogCallGraphDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxNodes, 1);

        CallGraphProjection sourceProjection = projection.Source;
        if (projection.Request.Direction
                != ExternalFocusedCallGraphDirection.Outgoing
            || projection.Request.Mode
                != ExternalFocusedCallGraphMode.SeededConnectors
            || projection.Request.SeedNodeIds.Length != 1
            || projection.Request.SeedNodeIds[0]
                != sourceProjection.Focus.Id)
        {
            throw new ArgumentException(
                "The external-focused neighborhood must use the call-graph focus as its single outgoing seed.",
                nameof(projection));
        }

        InspectionGraphSubject seed = FocusSubject(sourceProjection);
        InspectionGraphNeighborhoodRequest request =
            InspectionGraphNeighborhoodRequest.SingleSeed(
                seed,
                [CallGraphInspectionGraphCatalog.Call],
                InspectionGraphTraversalDirection.Outgoing,
                maxDepth);
        var limits = new List<InspectionGraphLimit>
        {
            new(
                CallGraphInspectionGraphCatalog.TraversalNodeBound,
                InspectionGraphTarget.Node(sourceProjection.Focus.Id),
                new CallGraphTraversalNodeBoundEvidence(maxNodes)),
        };
        if (diagnostics.IsIncomplete)
        {
            limits.Add(
                new InspectionGraphLimit(
                    CallGraphInspectionGraphCatalog
                        .CorrespondenceIncomplete,
                    InspectionGraphTarget.Node(
                        sourceProjection.Focus.Id),
                    new CallGraphCorrespondenceIncompleteEvidence(
                        diagnostics.IncompleteNodeCount,
                        diagnostics.IncompleteEdgeCount,
                        diagnostics.BindingIdentityConflictCount)));
        }

        InspectionGraphDocument source = Create(
            sourceProjection,
            request.ModeRequest,
            limits);
        return ProjectExternalFocused(source, projection, request);
    }

    static InspectionGraphSubject FocusSubject(
        CallGraphProjection projection) =>
        InspectionGraphSubject.ForMember(
            projection.Focus.Identity,
            projection.Focus.Member);

    static InspectionGraphSubject[] RootSubjects(
        CallGraphProjection projection) =>
        [
            .. projection.RootNodeIds.Select(rootId =>
                InspectionGraphSubject.ForMember(
                    projection.Nodes[rootId].Identity,
                    projection.Nodes[rootId].Member)),
        ];

    static InspectionGraphDocument Create(
        CallGraphProjection projection,
        InspectionGraphModeRequest modeRequest,
        IEnumerable<InspectionGraphLimit> additionalLimits)
    {
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
                            call.EvidenceMethod.ModuleVersionId,
                            call.EvidenceMethod.MetadataToken,
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
        limits.AddRange(additionalLimits);

        return new InspectionGraphDocument(
            projection.Nodes.All(static node =>
                node.Identity.IsPortable)
            && projection.CallSites.All(static callSite =>
                callSite.Identity.IsPortable)
                ? InspectionGraphDocumentScope.Portable
                : InspectionGraphDocumentScope.SessionBound,
            modeRequest,
            nodes,
            [],
            edges,
            occurrences,
            characteristics,
            InspectionGraphSeedBinder.Bind(
                modeRequest,
                nodes,
                [],
                InspectionGraphSeedTargetPreference.Node),
            limits,
            failures);
    }

    static InspectionGraphDocument ProjectExternalFocused(
        InspectionGraphDocument source,
        ExternalFocusedCallGraphProjection projection,
        InspectionGraphNeighborhoodRequest request)
    {
        var retainedNodeIds = projection.Nodes
            .Select(static node => node.Id)
            .ToHashSet();
        Dictionary<int, int> sourceEdgeIdsByRowNumber =
            projection.Source.Rows
                .Select((row, edgeId) => (
                    RowNumber: row.Number,
                    EdgeId: edgeId))
                .ToDictionary(
                    static item => item.RowNumber,
                    static item => item.EdgeId);
        var retainedEdgeIds = projection.EvidenceRows
            .Select(row =>
                sourceEdgeIdsByRowNumber[row.Number])
            .ToHashSet();
        var retainedOccurrenceIds = retainedEdgeIds
            .SelectMany(id => source.Edges[id].OccurrenceIds)
            .ToHashSet();
        if (retainedOccurrenceIds.Any(id =>
            !source.Occurrences[id].DerivedFromOccurrenceIds.IsEmpty))
        {
            throw new InspectionQueryException(
                "External-focused projection does not yet support derived occurrence receipts.");
        }

        var retainedGroupIds = new HashSet<int>();
        foreach (int nodeId in retainedNodeIds)
        {
            retainedGroupIds.UnionWith(
                source.Nodes[nodeId].GroupIds);
        }
        InspectionGraphProjectionUtilities.RetainGroupParents(
            source,
            retainedGroupIds);

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
                    edge.OccurrenceIds.Select(
                        occurrenceId =>
                            occurrenceIds[occurrenceId]));
            }),
        ];
        var characteristics =
            new List<InspectionGraphCharacteristic>(
                source.Characteristics.Length
                + retainedEdgeIds.Count);
        characteristics.AddRange(
            source.Characteristics.Select(characteristic =>
                InspectionGraphProjectionUtilities.RemapCharacteristic(
                    characteristic,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds))
                .Where(static characteristic =>
                    characteristic is not null)
                .Select(static characteristic => characteristic!));
        AddExternalFocusRoles(
            characteristics,
            projection,
            sourceEdgeIdsByRowNumber,
            edgeIds);

        InspectionGraphSeed[] seeds =
        [
            .. source.Seeds.Select(sourceSeed =>
                new InspectionGraphSeed(
                    sourceSeed.Subject,
                    InspectionGraphProjectionUtilities.RemapTarget(
                        sourceSeed.Target,
                        nodeIds,
                        groupIds,
                        edgeIds,
                        occurrenceIds)
                        ?? throw new InspectionQueryException(
                            "The external-focused seed target was not retained."),
                    sourceSeed.Role)),
        ];
        var limits = new List<InspectionGraphLimit>(
            source.Limits.Length
            + projection.UnclassifiedBoundaryRows.Length
            + seeds.Length);
        limits.AddRange(
            source.Limits.Select(limit =>
                InspectionGraphProjectionUtilities.RemapLimit(
                    limit,
                    nodeIds,
                    groupIds,
                    edgeIds,
                    occurrenceIds))
                .Where(static limit => limit is not null)
                .Select(static limit => limit!));
        limits.AddRange(
            seeds.Select(seed =>
                new InspectionGraphLimit(
                    InspectionGraphNeighborhoodCatalog.DepthBound,
                    seed.Target,
                    new InspectionGraphNeighborhoodDepthBoundEvidence(
                        request.MaxDepth))));
        limits.AddRange(
            projection.UnclassifiedBoundaryRows.Select(row =>
                new InspectionGraphLimit(
                    ExternalFocusedCallGraphInspectionCatalog
                        .BoundaryClassificationIncomplete,
                    InspectionGraphTarget.Edge(
                        edgeIds[
                            sourceEdgeIdsByRowNumber[
                                row.Number]]))));

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
            seeds,
            limits,
            failures);
    }

    static void AddExternalFocusRoles(
        List<InspectionGraphCharacteristic> characteristics,
        ExternalFocusedCallGraphProjection projection,
        IReadOnlyDictionary<int, int> sourceEdgeIdsByRowNumber,
        IReadOnlyDictionary<int, int> edgeIds)
    {
        HashSet<int> boundaryRows = projection.BoundaryRows
            .Select(static row => row.Number)
            .ToHashSet();
        HashSet<int> unclassifiedRows =
            projection.UnclassifiedBoundaryRows
                .Select(static row => row.Number)
                .ToHashSet();
        foreach (CallGraphRow row in projection.EvidenceRows)
        {
            string role = boundaryRows.Contains(row.Number)
                ? "boundary"
                : unclassifiedRows.Contains(row.Number)
                    ? "unclassified-boundary"
                    : "connector";
            InspectionGraphTarget target =
                InspectionGraphTarget.Edge(
                    edgeIds[
                        sourceEdgeIdsByRowNumber[
                            row.Number]]);
            characteristics.Add(
                new InspectionGraphCharacteristic(
                    ExternalFocusedCallGraphInspectionCatalog.EdgeRole,
                    target,
                    new InspectionGraphValue.Token(role),
                    new InspectionGraphCharacteristicDerivation(
                        InspectionGraphCharacteristicDerivationKind
                            .Derived,
                        [target])));
        }
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
