using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Graph;

namespace ILInspector.Research;

public static partial class LibraryStructuralReport
{
    private enum TypeLeverageRelationship
    {
        SignatureUse,
        BodyUse,
    }

    private enum TypeLeverageOccurrenceEvidence
    {
        None,
    }

    private enum TypeLeverageCharacteristic
    {
        None,
    }

    private enum TypeLeverageLimit
    {
        None,
    }

    private enum TypeLeverageFailure
    {
        None,
    }

    private readonly record struct TypeLeverageEdge(
        int SourceNodeId,
        int TargetNodeId,
        TypeLeverageRelationship Relationship);

    internal sealed record TypeLeverageGraphExecution(
        ImmutableArray<MetadataLibrarySignatureType> Types,
        GraphDistinctNeighborDegreeResult SignatureIncoming,
        GraphDistinctNeighborDegreeResult BodyOutgoing,
        GraphDistinctNeighborDegreeResult CombinedIncoming,
        GraphDistinctNeighborDegreeResult CombinedOutgoing);

    private static LibraryStructuralTypeLeverageDocument TypeLeverage(
        LibraryBodyAnalysisReceipt analysisReceipt,
        MetadataLibrarySignatureUseResult signatureUse,
        AnalysisLibraryBodyUseResult bodyUse) =>
        ProjectTypeLeverage(
            signatureUse,
            bodyUse,
            ExecuteTypeLeverageGraph(
                analysisReceipt,
                signatureUse,
                bodyUse));

    internal static TypeLeverageGraphExecution ExecuteTypeLeverageGraph(
        LibraryBodyAnalysisReceipt analysisReceipt,
        MetadataLibrarySignatureUseResult signatureUse,
        AnalysisLibraryBodyUseResult bodyUse)
    {
        ArgumentNullException.ThrowIfNull(analysisReceipt);
        ArgumentNullException.ThrowIfNull(signatureUse);
        ArgumentNullException.ThrowIfNull(bodyUse);
        ValidateTypeLeverageCorrespondence(
            analysisReceipt,
            signatureUse,
            bodyUse);

        MetadataLibrarySignatureType[] types =
        [
            .. signatureUse.Types.OrderBy(
                static type => type.Type.Definition.Value),
        ];
        var nodeIds = new Dictionary<MetadataTypeDefinitionAddress, int>(
            types.Length);
        var nodes =
            new GraphNode<MetadataTypeDefinitionAddress>[types.Length];
        for (var nodeId = 0; nodeId < types.Length; nodeId++)
        {
            MetadataLibrarySignatureType type = types[nodeId];
            nodeIds.Add(type.Type, nodeId);
            nodes[nodeId] = new(
                nodeId,
                type.Type,
                GraphNodeRole.Ordinary,
                []);
        }

        var logicalEdges = new HashSet<TypeLeverageEdge>();
        AddSignatureOccurrences(
            signatureUse.Occurrences,
            nodeIds,
            logicalEdges);
        AddBodyOccurrences(
            bodyUse.Occurrences,
            nodeIds,
            logicalEdges);

        GraphEdge<TypeLeverageRelationship>[] edges =
        [
            .. logicalEdges
                .OrderBy(static edge => edge.SourceNodeId)
                .ThenBy(static edge => edge.TargetNodeId)
                .ThenBy(static edge => edge.Relationship)
                .Select((edge, edgeId) =>
                    new GraphEdge<TypeLeverageRelationship>(
                        edgeId,
                        edge.SourceNodeId,
                        edge.TargetNodeId,
                        edge.Relationship,
                        [])),
        ];
        var graph =
            new GraphDocument<
                MetadataTypeDefinitionAddress,
                TypeLeverageRelationship,
                TypeLeverageOccurrenceEvidence,
                TypeLeverageCharacteristic,
                TypeLeverageLimit,
                TypeLeverageFailure>(
                GraphDocumentScope.Portable,
                nodes,
                [],
                edges,
                [],
                [],
                [],
                [],
                []);

        GraphDistinctNeighborDegreeResult signatureIncoming =
            Degree(
                graph,
                [TypeLeverageRelationship.SignatureUse],
                GraphTraversalDirection.Incoming);
        GraphDistinctNeighborDegreeResult bodyOutgoing =
            Degree(
                graph,
                [TypeLeverageRelationship.BodyUse],
                GraphTraversalDirection.Outgoing);
        GraphDistinctNeighborDegreeResult combinedIncoming =
            Degree(
                graph,
                [
                    TypeLeverageRelationship.SignatureUse,
                    TypeLeverageRelationship.BodyUse,
                ],
                GraphTraversalDirection.Incoming);
        GraphDistinctNeighborDegreeResult combinedOutgoing =
            Degree(
                graph,
                [
                    TypeLeverageRelationship.SignatureUse,
                    TypeLeverageRelationship.BodyUse,
                ],
                GraphTraversalDirection.Outgoing);

        return new(
            [.. types],
            signatureIncoming,
            bodyOutgoing,
            combinedIncoming,
            combinedOutgoing);
    }

    internal static LibraryStructuralTypeLeverageDocument ProjectTypeLeverage(
        MetadataLibrarySignatureUseResult signatureUse,
        AnalysisLibraryBodyUseResult bodyUse,
        TypeLeverageGraphExecution execution)
    {
        ArgumentNullException.ThrowIfNull(signatureUse);
        ArgumentNullException.ThrowIfNull(bodyUse);
        ArgumentNullException.ThrowIfNull(execution);

        LibraryStructuralTypeLeverageRow[] rows =
        [
            .. execution.Types.Select((type, nodeId) => new
                {
                    Type = type,
                    SignatureIncoming =
                        execution.SignatureIncoming.Rows[nodeId].Degree,
                    BodyOutgoing =
                        execution.BodyOutgoing.Rows[nodeId].Degree,
                    CombinedIncoming =
                        execution.CombinedIncoming.Rows[nodeId].Degree,
                    CombinedOutgoing =
                        execution.CombinedOutgoing.Rows[nodeId].Degree,
                })
                .Where(static item =>
                    item.CombinedIncoming + item.CombinedOutgoing > 0)
                .Select(static item =>
                {
                    bool eligible = IsRankingEligible(
                        item.Type.Classification);
                    return new LibraryStructuralTypeLeverageRow(
                        item.Type.Type,
                        item.Type.Name,
                        item.Type.Classification,
                        eligible,
                        item.SignatureIncoming,
                        item.BodyOutgoing,
                        item.CombinedIncoming,
                        item.CombinedOutgoing,
                        Role(
                            item.CombinedIncoming,
                            item.CombinedOutgoing));
                }),
        ];

        LibraryStructuralEvidenceDisposition signatureDisposition =
            signatureUse.Disposition
                == MetadataLibrarySignatureUseDisposition.Complete
                    ? LibraryStructuralEvidenceDisposition.Complete
                    : LibraryStructuralEvidenceDisposition.Qualified;
        LibraryStructuralEvidenceDisposition bodyDisposition =
            bodyUse.Disposition == AnalysisLibraryBodyUseDisposition.Complete
                ? LibraryStructuralEvidenceDisposition.Complete
                : LibraryStructuralEvidenceDisposition.Qualified;
        LibraryStructuralEvidenceDisposition roleDisposition =
            signatureDisposition
                    == LibraryStructuralEvidenceDisposition.Complete
                && bodyDisposition
                    == LibraryStructuralEvidenceDisposition.Complete
                    ? LibraryStructuralEvidenceDisposition.Complete
                    : LibraryStructuralEvidenceDisposition.Qualified;

        return new(
            [.. rows.OrderBy(static row => row.Type.Definition.Value)],
            new(
                signatureDisposition,
                Order(
                    rows,
                    static row => row.SignatureIncomingDegree)),
            new(
                bodyDisposition,
                Order(
                    rows,
                    static row => row.BodyOutgoingDegree)),
            roleDisposition,
            new(
                signatureUse.Receipt,
                signatureUse.Disposition,
                signatureUse.Coverage,
                signatureUse.Occurrences.Length,
                signatureUse.Diagnostics),
            new(
                bodyUse.Receipt,
                bodyUse.Disposition,
                bodyUse.Coverage,
                bodyUse.Occurrences.Length,
                bodyUse.Diagnostics),
            new(
                execution.SignatureIncoming.Receipt,
                execution.BodyOutgoing.Receipt,
                execution.CombinedIncoming.Receipt,
                execution.CombinedOutgoing.Receipt));
    }

    private static void ValidateTypeLeverageCorrespondence(
        LibraryBodyAnalysisReceipt analysisReceipt,
        MetadataLibrarySignatureUseResult signatureUse,
        AnalysisLibraryBodyUseResult bodyUse)
    {
        LibraryBodyModuleIdentity analysisIdentity =
            analysisReceipt.ModuleIdentity;
        if (analysisIdentity.AssemblyIdentity is null
            || analysisIdentity.ModuleVersionId
                != signatureUse.Receipt.ModuleVersionId
            || analysisIdentity.ModuleVersionId
                != bodyUse.Receipt.ModuleVersionId
            || analysisIdentity.AssemblyIdentity
                != signatureUse.Receipt.Assembly
            || analysisIdentity.AssemblyIdentity != bodyUse.Receipt.Assembly)
        {
            throw new ArgumentException(
                "Type structural leverage evidence must describe the exact "
                    + "Library generation in the Analysis report.");
        }

        Dictionary<MetadataTypeDefinitionAddress, AnalysisLibraryBodyUseType>
            bodyTypes = bodyUse.Types.ToDictionary(static type => type.Type);
        if (signatureUse.Types.Length != bodyTypes.Count
            || signatureUse.Types.Any(type =>
                !bodyTypes.TryGetValue(type.Type, out var bodyType)
                || type.Name != bodyType.Name
                || type.DefinitionKind != bodyType.DefinitionKind))
        {
            throw new ArgumentException(
                "Signature-use and body-use Type inventories must correspond "
                    + "exactly.");
        }
    }

    private static void AddSignatureOccurrences(
        ImmutableArray<MetadataLibrarySignatureUseOccurrence> source,
        IReadOnlyDictionary<MetadataTypeDefinitionAddress, int> nodeIds,
        HashSet<TypeLeverageEdge> logicalEdges)
    {
        foreach (MetadataLibrarySignatureUseOccurrence occurrence in source)
        {
            AddLogicalEdge(
                occurrence.Source,
                occurrence.Target,
                TypeLeverageRelationship.SignatureUse,
                nodeIds,
                logicalEdges);
        }
    }

    private static void AddBodyOccurrences(
        ImmutableArray<AnalysisLibraryBodyUseOccurrence> source,
        IReadOnlyDictionary<MetadataTypeDefinitionAddress, int> nodeIds,
        HashSet<TypeLeverageEdge> logicalEdges)
    {
        foreach (AnalysisLibraryBodyUseOccurrence occurrence in source)
        {
            AddLogicalEdge(
                occurrence.Source,
                occurrence.Target,
                TypeLeverageRelationship.BodyUse,
                nodeIds,
                logicalEdges);
        }
    }

    private static void AddLogicalEdge(
        MetadataTypeDefinitionAddress source,
        MetadataTypeDefinitionAddress target,
        TypeLeverageRelationship relationship,
        IReadOnlyDictionary<MetadataTypeDefinitionAddress, int> nodeIds,
        HashSet<TypeLeverageEdge> logicalEdges)
    {
        int sourceNodeId = nodeIds[source];
        int targetNodeId = nodeIds[target];
        logicalEdges.Add(
            new(
                sourceNodeId,
                targetNodeId,
                relationship));
    }

    private static GraphDistinctNeighborDegreeResult Degree(
        GraphDocument<
            MetadataTypeDefinitionAddress,
            TypeLeverageRelationship,
            TypeLeverageOccurrenceEvidence,
            TypeLeverageCharacteristic,
            TypeLeverageLimit,
            TypeLeverageFailure> graph,
        IReadOnlyList<TypeLeverageRelationship> relationships,
        GraphTraversalDirection direction) =>
        GraphDocumentExecution.DistinctNeighborDegree(
            graph,
            new GraphNeighborPlan<TypeLeverageRelationship>(
                relationships,
                direction,
                GraphSelfLoopPolicy.Exclude));

    private static ImmutableArray<MetadataTypeDefinitionAddress> Order(
        IEnumerable<LibraryStructuralTypeLeverageRow> rows,
        Func<LibraryStructuralTypeLeverageRow, int> degree) =>
    [
        .. rows
            .Where(static row => row.RankingEligible)
            .OrderByDescending(degree)
            .ThenBy(static row => row.Type.Definition.Value)
            .Select(static row => row.Type),
    ];

    private static bool IsRankingEligible(
        MetadataLibraryTypeClassification classification) =>
        (classification
            & (MetadataLibraryTypeClassification.UniversalBase
                | MetadataLibraryTypeClassification.Enum
                | MetadataLibraryTypeClassification.Attribute
                | MetadataLibraryTypeClassification.Exception
                | MetadataLibraryTypeClassification.Delegate))
            == MetadataLibraryTypeClassification.None;

    private static LibraryStructuralTypeRole Role(
        int incomingDegree,
        int outgoingDegree)
    {
        long denominator = (long)incomingDegree + outgoingDegree;
        if (incomingDegree * 10L >= denominator * 7L)
            return LibraryStructuralTypeRole.Foundation;
        if (incomingDegree * 10L <= denominator * 3L)
            return LibraryStructuralTypeRole.Orchestrator;
        return LibraryStructuralTypeRole.Hub;
    }
}
