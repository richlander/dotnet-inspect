using System.Collections.Immutable;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Graph;
using LibraryDependencyGraphDocument = Inspector.Graph.GraphDocument<
    ILInspector.Research.LibraryDependencyGraphSubject,
    ILInspector.Research.LibraryDependencyGraphRelationship,
    ILInspector.Research.LibraryDependencyGraphOccurrenceEvidence,
    ILInspector.Research.LibraryDependencyGraphCharacteristic,
    ILInspector.Research.LibraryDependencyGraphLimit,
    ILInspector.Research.LibraryDependencyGraphFailure>;

namespace ILInspector.Research;

public enum LibraryDependencyStructureUnavailableReason
{
    MethodEvidenceNotRequested,
    NonWholeLibraryScope,
}

public abstract record LibraryDependencyStructureResult
{
    private LibraryDependencyStructureResult()
    {
    }

    public sealed record Available(
        LibraryDependencyStructureDocument Document)
        : LibraryDependencyStructureResult;

    public sealed record Unavailable(
        LibraryDependencyStructureUnavailableReason Reason,
        string Message,
        LibraryBodyAnalysisReceipt Receipt)
        : LibraryDependencyStructureResult;
}

public enum LibraryDependencyCompleteness
{
    Complete,
    Qualified,
}

public enum LibraryDependencyUnresolvedReason
{
    Indirect,
    UnsupportedSignature,
    MalformedSignature,
    InvalidGenericDeclaration,
    Unmatched,
    Ambiguous,
    ModuleReference,
}

public sealed record LibraryDependencyCounts(
    int Invocations,
    int FunctionReferences)
{
    public int Total => Invocations + FunctionReferences;
}

public sealed record LibraryDependencyTypeNode(
    string TypeKey,
    TypeRef Type,
    string Namespace,
    int IntraTypeRelationshipCount);

public sealed record LibraryDependencyExternalNode(
    string Key,
    AssemblyReferenceIdentity? Assembly,
    bool IsIntrinsicCoreLibrary,
    string Namespace);

public sealed record LibraryDependencyTypeEdge(
    string SourceTypeKey,
    string TargetTypeKey,
    LibraryDependencyCounts Counts);

public sealed record LibraryDependencyExternalTypeEdge(
    string SourceTypeKey,
    string ExternalKey,
    LibraryDependencyCounts Counts);

public sealed record LibraryDependencyNamespaceNode(
    string Namespace,
    bool IsGlobalNamespace,
    int TypeCount,
    int IntraNamespaceRelationshipCount,
    int? CycleIndex,
    int Level);

public sealed record LibraryDependencyNamespaceEdge(
    string SourceNamespace,
    string TargetNamespace,
    LibraryDependencyCounts Counts,
    int ContributingTypeEdgeCount,
    ImmutableArray<LibraryDependencyTypeEdge> ExplainingTypeEdges,
    int RemainingContributorCount);

public sealed record LibraryDependencyExternalNamespaceEdge(
    string SourceNamespace,
    string ExternalKey,
    LibraryDependencyCounts Counts,
    int ContributingTypeEdgeCount,
    ImmutableArray<LibraryDependencyExternalTypeEdge> ExplainingTypeEdges,
    int RemainingContributorCount);

public sealed record LibraryDependencyNamespaceCycle(
    ImmutableArray<string> Namespaces);

public sealed record LibraryDependencyUnresolvedCount(
    LibraryDependencyUnresolvedReason Reason,
    int Count);

public sealed record LibraryDependencyPopulationReceipt(
    int ExaminedCallCount,
    int InternalCallCount,
    int SameTypeCallCount,
    int ExternalCallCount,
    int RuntimeProvidedCallCount,
    int UnresolvedCallCount,
    ImmutableArray<LibraryDependencyUnresolvedCount> UnresolvedReasons,
    int IncompleteBodyCount,
    int TypeCount,
    int NamespaceCount,
    int ExternalNodeCount);

public sealed class LibraryDependencyStructureDocument
{
    internal LibraryDependencyStructureDocument(
        LibraryBodyAnalysisReceipt analysisReceipt,
        string methodologyVersion,
        LibraryDependencyPopulationReceipt population,
        LibraryDependencyCompleteness completeness,
        ImmutableArray<LibraryDependencyTypeNode> types,
        ImmutableArray<LibraryDependencyExternalNode> externalNodes,
        ImmutableArray<LibraryDependencyTypeEdge> typeEdges,
        ImmutableArray<LibraryDependencyExternalTypeEdge> externalTypeEdges,
        ImmutableArray<LibraryDependencyNamespaceNode> namespaces,
        ImmutableArray<LibraryDependencyNamespaceEdge> namespaceEdges,
        ImmutableArray<LibraryDependencyExternalNamespaceEdge>
            externalNamespaceEdges,
        ImmutableArray<LibraryDependencyNamespaceCycle> cycles,
        ImmutableArray<AnalysisDiagnostic> diagnostics,
        LibraryDependencyGraphExecution graphExecution)
    {
        AnalysisReceipt = analysisReceipt;
        MethodologyVersion = methodologyVersion;
        Population = population;
        Completeness = completeness;
        Types = types;
        ExternalNodes = externalNodes;
        TypeEdges = typeEdges;
        ExternalTypeEdges = externalTypeEdges;
        Namespaces = namespaces;
        NamespaceEdges = namespaceEdges;
        ExternalNamespaceEdges = externalNamespaceEdges;
        Cycles = cycles;
        Diagnostics = diagnostics;
        GraphExecution = graphExecution;
    }

    public LibraryBodyAnalysisReceipt AnalysisReceipt { get; }
    public string MethodologyVersion { get; }
    public LibraryDependencyPopulationReceipt Population { get; }
    public LibraryDependencyCompleteness Completeness { get; }
    public ImmutableArray<LibraryDependencyTypeNode> Types { get; }
    public ImmutableArray<LibraryDependencyExternalNode> ExternalNodes
    { get; }
    public ImmutableArray<LibraryDependencyTypeEdge> TypeEdges { get; }
    public ImmutableArray<LibraryDependencyExternalTypeEdge> ExternalTypeEdges
    { get; }
    public ImmutableArray<LibraryDependencyNamespaceNode> Namespaces { get; }
    public ImmutableArray<LibraryDependencyNamespaceEdge> NamespaceEdges
    { get; }
    public ImmutableArray<LibraryDependencyExternalNamespaceEdge>
        ExternalNamespaceEdges { get; }
    public ImmutableArray<LibraryDependencyNamespaceCycle> Cycles { get; }
    public ImmutableArray<AnalysisDiagnostic> Diagnostics { get; }

    internal LibraryDependencyGraphExecution GraphExecution { get; }
}

public static class LibraryDependencyStructure
{
    public const string CurrentMethodologyVersion =
        "library-dependency-structure.v1";

    public const int MaximumExplainingTypeEdges = 5;

    const string IntrinsicCoreLibraryKey = "<intrinsic-core-library>";

    public static LibraryDependencyStructureResult Execute(
        LibraryBodyAnalysisExecution analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return Execute(analysis.CallGraph);
    }

    public static LibraryDependencyStructureResult Execute(
        LibraryCallGraphAnalysisResult callGraph)
    {
        ArgumentNullException.ThrowIfNull(callGraph);
        if (!callGraph.WasRequested)
        {
            return new LibraryDependencyStructureResult.Unavailable(
                LibraryDependencyStructureUnavailableReason
                    .MethodEvidenceNotRequested,
                "Library Dependency Structure requires method call evidence.",
                callGraph.Receipt);
        }
        if (!callGraph.HasFullMethodEvidenceScope)
        {
            return new LibraryDependencyStructureResult.Unavailable(
                LibraryDependencyStructureUnavailableReason
                    .NonWholeLibraryScope,
                "Library Dependency Structure requires unscoped whole-library "
                    + "method evidence.",
                callGraph.Receipt);
        }

        return new LibraryDependencyStructureResult.Available(
            Build(callGraph));
    }

    static LibraryDependencyStructureDocument Build(
        LibraryCallGraphAnalysisResult callGraph)
    {
        var types = new Dictionary<string, TypeRef>(StringComparer.Ordinal);
        foreach (MethodIdentity method in callGraph.DeclaredMethods)
            AddType(types, method.DeclaringType);

        var externalNodes =
            new Dictionary<
                string,
                LibraryDependencyExternalNode>(StringComparer.Ordinal);
        var admittedOccurrences = new List<AdmittedOccurrence>();
        var unresolved =
            new Dictionary<LibraryDependencyUnresolvedReason, int>();
        var physicalCallSites = new HashSet<PhysicalCallSite>();
        var examined = 0;
        var internalCalls = 0;
        var sameTypeCalls = 0;
        var externalCalls = 0;
        var runtimeProvidedCalls = 0;

        foreach (DirectCall call in callGraph.DirectCalls)
        {
            var callSite = new PhysicalCallSite(
                call.EvidenceMethod.MetadataToken,
                call.ILOffset,
                call.OperandToken);
            if (!physicalCallSites.Add(callSite))
            {
                throw new InvalidOperationException(
                    "Library Dependency Structure requires one direct call "
                    + "per physical call site; duplicate call site at body "
                    + $"0x{callSite.EvidenceBodyToken:X8}, IL offset "
                    + $"{callSite.ILOffset}, operand "
                    + $"0x{callSite.OperandToken:X8}.");
            }

            examined++;
            LibraryDependencyGraphRelationship relationship =
                call.Kind is CallKind.LoadFunction
                    or CallKind.LoadVirtualFunction
                    ? LibraryDependencyGraphRelationship.FunctionReference
                    : LibraryDependencyGraphRelationship.Invocation;
            string sourceKey = AddType(types, call.Caller.DeclaringType);

            switch (callGraph.ResolveTarget(call))
            {
                case DirectCallTarget.CurrentModule current:
                    MethodIdentity target =
                        callGraph.ResolveDeclaredMethod(current.Method)
                        ?? current.Method;
                    string targetKey = AddType(
                        types,
                        target.DeclaringType);
                    internalCalls++;
                    if (targetKey == sourceKey)
                        sameTypeCalls++;
                    admittedOccurrences.Add(
                        AdmittedOccurrence.Internal(
                            sourceKey,
                            targetKey,
                            relationship,
                            callSite));
                    break;

                case DirectCallTarget.External
                    {
                        Origin: TypeReferenceOrigin.ModuleReference,
                    }:
                    Count(
                        unresolved,
                        LibraryDependencyUnresolvedReason.ModuleReference);
                    break;

                case DirectCallTarget.External externalTarget
                    when ExternalNode(
                        externalTarget.Origin,
                        call.Callee.DeclaringType) is { } externalNode:
                    externalNodes.TryAdd(
                        externalNode.Key,
                        externalNode);
                    externalCalls++;
                    admittedOccurrences.Add(
                        AdmittedOccurrence.External(
                            sourceKey,
                            externalNode.Key,
                            relationship,
                            callSite));
                    break;

                case DirectCallTarget.External:
                    Count(
                        unresolved,
                        LibraryDependencyUnresolvedReason.Unmatched);
                    break;

                case DirectCallTarget.RuntimeProvided:
                    runtimeProvidedCalls++;
                    break;

                case DirectCallTarget.Unresolved failure:
                    Count(unresolved, Map(failure.Reason));
                    break;
            }
        }

        LibraryDependencyGraphExecution graphExecution = BuildGraph(
            types,
            externalNodes,
            admittedOccurrences);
        LibraryDependencyRows rows = ProjectRows(graphExecution, types);
        int incompleteBodies = callGraph.Diagnostics
            .Select(static diagnostic => diagnostic.MethodToken)
            .Distinct()
            .Count();
        int unresolvedTotal = unresolved.Values.Sum();
        var population = new LibraryDependencyPopulationReceipt(
            examined,
            internalCalls,
            sameTypeCalls,
            externalCalls,
            runtimeProvidedCalls,
            unresolvedTotal,
            [
                .. unresolved
                    .OrderBy(static pair => pair.Key)
                    .Select(static pair =>
                        new LibraryDependencyUnresolvedCount(
                            pair.Key,
                            pair.Value)),
            ],
            incompleteBodies,
            rows.Types.Length,
            rows.Namespaces.Length,
            externalNodes.Count);

        return new(
            callGraph.Receipt,
            CurrentMethodologyVersion,
            population,
            unresolvedTotal == 0 && incompleteBodies == 0
                ? LibraryDependencyCompleteness.Complete
                : LibraryDependencyCompleteness.Qualified,
            rows.Types,
            [
                .. externalNodes.Values.OrderBy(
                    static node => node.Key,
                    StringComparer.Ordinal),
            ],
            rows.TypeEdges,
            rows.ExternalTypeEdges,
            rows.Namespaces,
            rows.NamespaceEdges,
            rows.ExternalNamespaceEdges,
            rows.Cycles,
            callGraph.Diagnostics,
            graphExecution);
    }

    static LibraryDependencyGraphExecution BuildGraph(
        IReadOnlyDictionary<string, TypeRef> types,
        IReadOnlyDictionary<string, LibraryDependencyExternalNode>
            externalNodes,
        IReadOnlyList<AdmittedOccurrence> admittedOccurrences)
    {
        string[] typeKeys =
        [
            .. types.Keys.Order(StringComparer.Ordinal),
        ];
        string[] externalKeys =
        [
            .. externalNodes.Keys.Order(StringComparer.Ordinal),
        ];
        string[] namespaces =
        [
            .. typeKeys
                .Select(key => NamespaceOf(types[key]))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        var nodeIdByType =
            new Dictionary<string, int>(typeKeys.Length, StringComparer.Ordinal);
        var nodeIdByExternal =
            new Dictionary<
                string,
                int>(externalKeys.Length, StringComparer.Ordinal);
        var groupIdByNamespace =
            new Dictionary<
                string,
                int>(namespaces.Length, StringComparer.Ordinal);
        var groupIdByExternal =
            new Dictionary<
                string,
                int>(externalKeys.Length, StringComparer.Ordinal);
        var groups =
            new List<GraphGroup<LibraryDependencyGraphSubject>>(
                namespaces.Length + externalKeys.Length);

        foreach (string @namespace in namespaces)
        {
            int groupId = groups.Count;
            groupIdByNamespace.Add(@namespace, groupId);
            groups.Add(
                new(
                    groupId,
                    LibraryDependencyGraphSubject.InternalNamespace(
                        @namespace),
                    parentId: null));
        }
        foreach (string externalKey in externalKeys)
        {
            int groupId = groups.Count;
            groupIdByExternal.Add(externalKey, groupId);
            groups.Add(
                new(
                    groupId,
                    LibraryDependencyGraphSubject.External(externalKey),
                    parentId: null));
        }

        var nodes =
            new List<GraphNode<LibraryDependencyGraphSubject>>(
                typeKeys.Length + externalKeys.Length);
        foreach (string typeKey in typeKeys)
        {
            int nodeId = nodes.Count;
            nodeIdByType.Add(typeKey, nodeId);
            string @namespace = NamespaceOf(types[typeKey]);
            nodes.Add(
                new(
                    nodeId,
                    LibraryDependencyGraphSubject.Type(typeKey),
                    GraphNodeRole.Ordinary,
                    [groupIdByNamespace[@namespace]]));
        }
        foreach (string externalKey in externalKeys)
        {
            int nodeId = nodes.Count;
            nodeIdByExternal.Add(externalKey, nodeId);
            nodes.Add(
                new(
                    nodeId,
                    LibraryDependencyGraphSubject.External(externalKey),
                    GraphNodeRole.External,
                    [groupIdByExternal[externalKey]]));
        }

        var occurrences =
            new List<
                GraphOccurrence<
                    LibraryDependencyGraphSubject,
                    LibraryDependencyGraphRelationship,
                    LibraryDependencyGraphOccurrenceEvidence>>(
                        admittedOccurrences.Count);
        var occurrenceIdsByEdge =
            new Dictionary<LibraryDependencyGraphEdge, List<int>>();
        foreach (AdmittedOccurrence admitted in admittedOccurrences)
        {
            int sourceNodeId = nodeIdByType[admitted.SourceTypeKey];
            int targetNodeId = admitted.ExternalTarget
                ? nodeIdByExternal[admitted.TargetKey]
                : nodeIdByType[admitted.TargetKey];
            int occurrenceId = occurrences.Count;
            occurrences.Add(
                new(
                    occurrenceId,
                    admitted.Relationship,
                    nodes[sourceNodeId].Subject,
                    nodes[targetNodeId].Subject,
                    new(
                        admitted.CallSite.EvidenceBodyToken,
                        admitted.CallSite.ILOffset,
                        admitted.CallSite.OperandToken),
                    []));
            var edge = new LibraryDependencyGraphEdge(
                sourceNodeId,
                targetNodeId,
                admitted.Relationship);
            if (!occurrenceIdsByEdge.TryGetValue(
                edge,
                out List<int>? occurrenceIds))
            {
                occurrenceIds = [];
                occurrenceIdsByEdge.Add(edge, occurrenceIds);
            }
            occurrenceIds.Add(occurrenceId);
        }

        GraphEdge<LibraryDependencyGraphRelationship>[] edges =
        [
            .. occurrenceIdsByEdge
                .OrderBy(static pair => pair.Key.SourceNodeId)
                .ThenBy(static pair => pair.Key.TargetNodeId)
                .ThenBy(static pair => pair.Key.Relationship)
                .Select((pair, edgeId) =>
                    new GraphEdge<LibraryDependencyGraphRelationship>(
                        edgeId,
                        pair.Key.SourceNodeId,
                        pair.Key.TargetNodeId,
                        pair.Key.Relationship,
                        pair.Value)),
        ];
        var graph =
            new LibraryDependencyGraphDocument(
                GraphDocumentScope.Portable,
                nodes,
                groups,
                edges,
                occurrences,
                [],
                [],
                [],
                []);
        GraphGroupProjectionResult<LibraryDependencyGraphRelationship>
            projection = GraphDocumentExecution.GroupProjection(
                graph,
                new(
                    graph.Identity,
                    [
                        .. graph.Nodes.Select(node =>
                            new GraphGroupAssignment(
                                node.Id,
                                node.GroupIds[0])),
                    ],
                    [
                        LibraryDependencyGraphRelationship.Invocation,
                        LibraryDependencyGraphRelationship.FunctionReference,
                    ]));
        GraphComponentAnalysisResult components =
            GraphDocumentExecution.ComponentAnalysis(
                projection,
                new(
                    graph.Identity,
                    [
                        .. groups
                            .Where(static group =>
                                group.Subject.Kind
                                    == LibraryDependencyGraphSubjectKind
                                        .InternalNamespace)
                            .Select(static group => group.Id),
                    ]));
        return new(graph, projection, components);
    }

    static LibraryDependencyRows ProjectRows(
        LibraryDependencyGraphExecution execution,
        IReadOnlyDictionary<string, TypeRef> types)
    {
        LibraryDependencyGraphDocument graph = execution.Graph;
        ImmutableArray<LibraryDependencyTypeEdge> typeEdges =
            TypeEdges(graph);
        ImmutableArray<LibraryDependencyExternalTypeEdge> externalTypeEdges =
            ExternalTypeEdges(graph);
        var typeNodeByKey = graph.Nodes
            .Where(static node =>
                node.Subject.Kind
                    == LibraryDependencyGraphSubjectKind.Type)
            .ToDictionary(
                static node => node.Subject.Key,
                static node => node,
                StringComparer.Ordinal);
        var externalNodeIdByKey = graph.Nodes
            .Where(static node =>
                node.Subject.Kind
                    == LibraryDependencyGraphSubjectKind.External)
            .ToDictionary(
                static node => node.Subject.Key,
                static node => node.Id,
                StringComparer.Ordinal);
        var intraTypeCountByNodeId = new Dictionary<int, int>();
        foreach (GraphEdge<LibraryDependencyGraphRelationship> edge
            in graph.Edges)
        {
            if (edge.FromNodeId != edge.ToNodeId)
                continue;
            intraTypeCountByNodeId[edge.FromNodeId] =
                intraTypeCountByNodeId.GetValueOrDefault(edge.FromNodeId)
                + edge.OccurrenceIds.Length;
        }
        ImmutableArray<LibraryDependencyTypeNode> typeNodes =
        [
            .. typeNodeByKey
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new LibraryDependencyTypeNode(
                    pair.Key,
                    types[pair.Key],
                    NamespaceOf(types[pair.Key]),
                    intraTypeCountByNodeId.GetValueOrDefault(
                        pair.Value.Id))),
        ];
        var typeEdgesByNodePair = typeEdges.ToDictionary(
            edge => (
                Source: typeNodeByKey[edge.SourceTypeKey].Id,
                Target: typeNodeByKey[edge.TargetTypeKey].Id));
        var externalTypeEdgesByNodePair = externalTypeEdges.ToDictionary(
            edge => (
                Source: typeNodeByKey[edge.SourceTypeKey].Id,
                Target: externalNodeIdByKey[edge.ExternalKey]));

        GraphGroupProjectionResult<LibraryDependencyGraphRelationship>
            projection = execution.GroupProjection;
        Dictionary<
            (int SourceGroupId, int TargetGroupId),
            ProjectedRelationshipSet> projectedRelationships =
                ProjectedRelationships(projection);
        var namespaceEdges = new List<LibraryDependencyNamespaceEdge>();
        var externalNamespaceEdges =
            new List<LibraryDependencyExternalNamespaceEdge>();
        foreach ((var groupPair, ProjectedRelationshipSet relationships)
            in projectedRelationships)
        {
            LibraryDependencyGraphSubject source =
                graph.Groups[groupPair.SourceGroupId].Subject;
            LibraryDependencyGraphSubject target =
                graph.Groups[groupPair.TargetGroupId].Subject;
            if (source.Kind
                    != LibraryDependencyGraphSubjectKind.InternalNamespace)
            {
                continue;
            }

            int[] sourceEdgeIds = relationships.SourceEdgeIds();
            LibraryDependencyCounts counts = relationships.Counts();
            if (target.Kind
                == LibraryDependencyGraphSubjectKind.InternalNamespace)
            {
                LibraryDependencyTypeEdge[] contributors =
                [
                    .. sourceEdgeIds
                        .Select(edgeId => graph.Edges[edgeId])
                        .Select(edge =>
                            typeEdgesByNodePair[
                                (edge.FromNodeId, edge.ToNodeId)])
                        .Distinct(),
                ];
                ImmutableArray<LibraryDependencyTypeEdge> explanation =
                    Explain(
                        contributors,
                        static edge => edge.Counts,
                        static edge => edge.SourceTypeKey,
                        static edge => edge.TargetTypeKey);
                namespaceEdges.Add(
                    new(
                        source.Key,
                        target.Key,
                        counts,
                        contributors.Length,
                        explanation,
                        contributors.Length - explanation.Length));
            }
            else if (target.Kind
                == LibraryDependencyGraphSubjectKind.External)
            {
                LibraryDependencyExternalTypeEdge[] contributors =
                [
                    .. sourceEdgeIds
                        .Select(edgeId => graph.Edges[edgeId])
                        .Select(edge =>
                            externalTypeEdgesByNodePair[
                                (edge.FromNodeId, edge.ToNodeId)])
                        .Distinct(),
                ];
                ImmutableArray<LibraryDependencyExternalTypeEdge>
                    explanation = Explain(
                        contributors,
                        static edge => edge.Counts,
                        static edge => edge.SourceTypeKey,
                        static edge => edge.ExternalKey);
                externalNamespaceEdges.Add(
                    new(
                        source.Key,
                        target.Key,
                        counts,
                        contributors.Length,
                        explanation,
                        contributors.Length - explanation.Length));
            }
        }

        GraphComponentAnalysisResult components =
            execution.Components;
        var componentByGroupId = components.Memberships.ToDictionary(
            static membership => membership.SourceGroupId,
            static membership => membership.ComponentId);
        var cycles =
            components.Components
                .Where(static component =>
                    component.SourceGroupIds.Length >= 2)
                .Select(component =>
                    new LibraryDependencyNamespaceCycle(
                        [
                            .. component.SourceGroupIds
                                .Select(groupId =>
                                    graph.Groups[groupId].Subject.Key)
                                .Order(StringComparer.Ordinal),
                        ]))
                .OrderBy(static cycle => cycle.Namespaces[0],
                    StringComparer.Ordinal)
                .ToImmutableArray();
        var cycleIndexByNamespace =
            new Dictionary<string, int>(StringComparer.Ordinal);
        for (var cycleIndex = 0;
            cycleIndex < cycles.Length;
            cycleIndex++)
        {
            foreach (string @namespace in cycles[cycleIndex].Namespaces)
                cycleIndexByNamespace.Add(@namespace, cycleIndex);
        }

        var withinCountByGroupId = new Dictionary<int, int>();
        foreach (GraphWithinGroupContribution<
            LibraryDependencyGraphRelationship> contribution
            in projection.WithinGroupContributions)
        {
            int groupId =
                projection.Nodes[contribution.ProjectedNodeId].SourceGroupId;
            withinCountByGroupId[groupId] =
                withinCountByGroupId.GetValueOrDefault(groupId)
                + contribution.SourceOccurrenceIds.Length;
        }
        ImmutableArray<LibraryDependencyNamespaceNode> namespaceNodes =
        [
            .. projection.Nodes
                .Select(projected => (
                    Projected: projected,
                    Group: graph.Groups[projected.SourceGroupId]))
                .Where(static item =>
                    item.Group.Subject.Kind
                        == LibraryDependencyGraphSubjectKind.InternalNamespace)
                .Select(item =>
                {
                    int componentId =
                        componentByGroupId[item.Group.Id];
                    string @namespace = item.Group.Subject.Key;
                    return new LibraryDependencyNamespaceNode(
                        @namespace,
                        @namespace.Length == 0,
                        item.Projected.SourceNodeIds.Length,
                        withinCountByGroupId.GetValueOrDefault(
                            item.Group.Id),
                        cycleIndexByNamespace.TryGetValue(
                            @namespace,
                            out int cycleIndex)
                                ? cycleIndex
                                : null,
                        components.Components[componentId].Level);
                })
                .OrderBy(static node => node.Namespace,
                    StringComparer.Ordinal),
        ];
        return new(
            typeNodes,
            typeEdges,
            externalTypeEdges,
            namespaceNodes,
            [
                .. namespaceEdges
                    .OrderBy(static edge => edge.SourceNamespace,
                        StringComparer.Ordinal)
                    .ThenBy(static edge => edge.TargetNamespace,
                        StringComparer.Ordinal),
            ],
            [
                .. externalNamespaceEdges
                    .OrderBy(static edge => edge.SourceNamespace,
                        StringComparer.Ordinal)
                    .ThenBy(static edge => edge.ExternalKey,
                        StringComparer.Ordinal),
            ],
            cycles);
    }

    static ImmutableArray<LibraryDependencyTypeEdge> TypeEdges(
        LibraryDependencyGraphDocument graph) =>
    [
        .. graph.Edges
            .Where(edge =>
                edge.FromNodeId != edge.ToNodeId
                && graph.Nodes[edge.FromNodeId].Subject.Kind
                    == LibraryDependencyGraphSubjectKind.Type
                && graph.Nodes[edge.ToNodeId].Subject.Kind
                    == LibraryDependencyGraphSubjectKind.Type)
            .GroupBy(edge => (
                Source: graph.Nodes[edge.FromNodeId].Subject.Key,
                Target: graph.Nodes[edge.ToNodeId].Subject.Key))
            .Select(group => new LibraryDependencyTypeEdge(
                group.Key.Source,
                group.Key.Target,
                Counts(group)))
            .OrderBy(static edge => edge.SourceTypeKey,
                StringComparer.Ordinal)
            .ThenBy(static edge => edge.TargetTypeKey,
                StringComparer.Ordinal),
    ];

    static ImmutableArray<LibraryDependencyExternalTypeEdge>
        ExternalTypeEdges(
            LibraryDependencyGraphDocument graph) =>
    [
        .. graph.Edges
            .Where(edge =>
                graph.Nodes[edge.FromNodeId].Subject.Kind
                    == LibraryDependencyGraphSubjectKind.Type
                && graph.Nodes[edge.ToNodeId].Subject.Kind
                    == LibraryDependencyGraphSubjectKind.External)
            .GroupBy(edge => (
                Source: graph.Nodes[edge.FromNodeId].Subject.Key,
                External: graph.Nodes[edge.ToNodeId].Subject.Key))
            .Select(group => new LibraryDependencyExternalTypeEdge(
                group.Key.Source,
                group.Key.External,
                Counts(group)))
            .OrderBy(static edge => edge.SourceTypeKey,
                StringComparer.Ordinal)
            .ThenBy(static edge => edge.ExternalKey,
                StringComparer.Ordinal),
    ];

    static Dictionary<
        (int SourceGroupId, int TargetGroupId),
        ProjectedRelationshipSet> ProjectedRelationships(
            GraphGroupProjectionResult<
                LibraryDependencyGraphRelationship> projection)
    {
        var relationships =
            new Dictionary<
                (int SourceGroupId, int TargetGroupId),
                ProjectedRelationshipSet>();
        foreach (GraphProjectedEdge<LibraryDependencyGraphRelationship> edge
            in projection.Edges)
        {
            int sourceGroupId =
                projection.Nodes[edge.FromProjectedNodeId].SourceGroupId;
            int targetGroupId =
                projection.Nodes[edge.ToProjectedNodeId].SourceGroupId;
            var key = (sourceGroupId, targetGroupId);
            if (!relationships.TryGetValue(
                key,
                out ProjectedRelationshipSet? set))
            {
                set = new();
                relationships.Add(key, set);
            }
            set.Add(edge);
        }
        return relationships;
    }

    static LibraryDependencyCounts Counts(
        IEnumerable<GraphEdge<LibraryDependencyGraphRelationship>> edges)
    {
        var invocations = 0;
        var functionReferences = 0;
        foreach (GraphEdge<LibraryDependencyGraphRelationship> edge in edges)
        {
            if (edge.Relationship
                == LibraryDependencyGraphRelationship.Invocation)
            {
                invocations += edge.OccurrenceIds.Length;
            }
            else
            {
                functionReferences += edge.OccurrenceIds.Length;
            }
        }
        return new(invocations, functionReferences);
    }

    static string AddType(
        Dictionary<string, TypeRef> types,
        TypeRef type)
    {
        string key = LibraryStructuralReport.TypeKey(type);
        types.TryAdd(key, type);
        return key;
    }

    static string NamespaceOf(TypeRef type) =>
        type.Resolution?.Type.Namespace ?? type.Namespace;

    static LibraryDependencyExternalNode? ExternalNode(
        TypeReferenceOrigin origin,
        TypeRef declaringType)
    {
        TypeRef definition =
            declaringType.Kind == TypeRefKind.GenericInstance
                ? declaringType.ElementType ?? declaringType
                : declaringType;
        string @namespace = NamespaceOf(definition);
        return origin switch
        {
            TypeReferenceOrigin.AssemblyReference reference => new(
                ExternalKey(reference.Assembly, @namespace),
                reference.Assembly,
                IsIntrinsicCoreLibrary: false,
                @namespace),
            TypeReferenceOrigin.IntrinsicCoreLibrary => new(
                ExternalKey(assembly: null, @namespace),
                Assembly: null,
                IsIntrinsicCoreLibrary: true,
                @namespace),
            _ => null,
        };
    }

    internal static string ExternalKey(
        AssemblyReferenceIdentity? assembly,
        string @namespace) =>
        assembly is null
            ? string.Concat(
                Component("intrinsic", IntrinsicCoreLibraryKey),
                Component("namespace", @namespace))
            : string.Concat(
                Component("assembly", assembly.Name),
                Component("version", assembly.Version?.ToString()),
                Component("culture", assembly.Culture),
                Component("publicKeyToken", assembly.PublicKeyToken),
                Component("namespace", @namespace));

    static string Component(string name, string? value) =>
        value is null
            ? $"{name}=null;"
            : $"{name}={value.Length}:{value};";

    static LibraryDependencyUnresolvedReason Map(
        DirectCallTargetUnresolvedReason reason) =>
        reason switch
        {
            DirectCallTargetUnresolvedReason.Indirect =>
                LibraryDependencyUnresolvedReason.Indirect,
            DirectCallTargetUnresolvedReason.UnsupportedSignature =>
                LibraryDependencyUnresolvedReason.UnsupportedSignature,
            DirectCallTargetUnresolvedReason.MalformedSignature =>
                LibraryDependencyUnresolvedReason.MalformedSignature,
            DirectCallTargetUnresolvedReason.InvalidGenericDeclaration =>
                LibraryDependencyUnresolvedReason.InvalidGenericDeclaration,
            DirectCallTargetUnresolvedReason.Ambiguous =>
                LibraryDependencyUnresolvedReason.Ambiguous,
            _ => LibraryDependencyUnresolvedReason.Unmatched,
        };

    static ImmutableArray<TEdge> Explain<TEdge>(
        IEnumerable<TEdge> contributors,
        Func<TEdge, LibraryDependencyCounts> counts,
        Func<TEdge, string> source,
        Func<TEdge, string> target) =>
    [
        .. contributors
            .OrderByDescending(edge => counts(edge).Total)
            .ThenBy(source, StringComparer.Ordinal)
            .ThenBy(target, StringComparer.Ordinal)
            .Take(MaximumExplainingTypeEdges),
    ];

    static void Count<TKey>(
        Dictionary<TKey, int> counts,
        TKey key)
        where TKey : notnull =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    readonly record struct PhysicalCallSite(
        int EvidenceBodyToken,
        int ILOffset,
        int OperandToken);

    readonly record struct AdmittedOccurrence(
        string SourceTypeKey,
        string TargetKey,
        bool ExternalTarget,
        LibraryDependencyGraphRelationship Relationship,
        PhysicalCallSite CallSite)
    {
        internal static AdmittedOccurrence Internal(
            string sourceTypeKey,
            string targetTypeKey,
            LibraryDependencyGraphRelationship relationship,
            PhysicalCallSite callSite) =>
            new(
                sourceTypeKey,
                targetTypeKey,
                ExternalTarget: false,
                relationship,
                callSite);

        internal static AdmittedOccurrence External(
            string sourceTypeKey,
            string externalKey,
            LibraryDependencyGraphRelationship relationship,
            PhysicalCallSite callSite) =>
            new(
                sourceTypeKey,
                externalKey,
                ExternalTarget: true,
                relationship,
                callSite);
    }

    sealed class ProjectedRelationshipSet
    {
        readonly List<
            GraphProjectedEdge<LibraryDependencyGraphRelationship>> _edges =
                [];

        internal void Add(
            GraphProjectedEdge<LibraryDependencyGraphRelationship> edge) =>
            _edges.Add(edge);

        internal LibraryDependencyCounts Counts()
        {
            var invocations = 0;
            var functionReferences = 0;
            foreach (GraphProjectedEdge<
                LibraryDependencyGraphRelationship> edge in _edges)
            {
                if (edge.Relationship
                    == LibraryDependencyGraphRelationship.Invocation)
                {
                    invocations += edge.SourceOccurrenceIds.Length;
                }
                else
                {
                    functionReferences += edge.SourceOccurrenceIds.Length;
                }
            }
            return new(invocations, functionReferences);
        }

        internal int[] SourceEdgeIds() =>
        [
            .. _edges
                .SelectMany(static edge => edge.SourceEdgeIds)
                .Distinct()
                .Order(),
        ];
    }

    readonly record struct LibraryDependencyRows(
        ImmutableArray<LibraryDependencyTypeNode> Types,
        ImmutableArray<LibraryDependencyTypeEdge> TypeEdges,
        ImmutableArray<LibraryDependencyExternalTypeEdge> ExternalTypeEdges,
        ImmutableArray<LibraryDependencyNamespaceNode> Namespaces,
        ImmutableArray<LibraryDependencyNamespaceEdge> NamespaceEdges,
        ImmutableArray<LibraryDependencyExternalNamespaceEdge>
            ExternalNamespaceEdges,
        ImmutableArray<LibraryDependencyNamespaceCycle> Cycles);
}

internal enum LibraryDependencyGraphSubjectKind
{
    Type,
    InternalNamespace,
    External,
}

internal readonly record struct LibraryDependencyGraphSubject(
    LibraryDependencyGraphSubjectKind Kind,
    string Key)
{
    internal static LibraryDependencyGraphSubject Type(string key) =>
        new(LibraryDependencyGraphSubjectKind.Type, key);

    internal static LibraryDependencyGraphSubject InternalNamespace(
        string @namespace) =>
        new(LibraryDependencyGraphSubjectKind.InternalNamespace, @namespace);

    internal static LibraryDependencyGraphSubject External(string key) =>
        new(LibraryDependencyGraphSubjectKind.External, key);
}

internal enum LibraryDependencyGraphRelationship
{
    Invocation,
    FunctionReference,
}

internal readonly record struct LibraryDependencyGraphOccurrenceEvidence(
    int EvidenceBodyToken,
    int ILOffset,
    int OperandToken);

internal enum LibraryDependencyGraphCharacteristic
{
    None,
}

internal enum LibraryDependencyGraphLimit
{
    None,
}

internal enum LibraryDependencyGraphFailure
{
    None,
}

internal readonly record struct LibraryDependencyGraphEdge(
    int SourceNodeId,
    int TargetNodeId,
    LibraryDependencyGraphRelationship Relationship);

internal sealed record LibraryDependencyGraphExecution(
    LibraryDependencyGraphDocument Graph,
    GraphGroupProjectionResult<LibraryDependencyGraphRelationship>
        GroupProjection,
    GraphComponentAnalysisResult Components);
