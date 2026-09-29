using System.Collections.Immutable;

using ILInspector.Metadata;
using Inspector.Graph;

namespace ILInspector.Research;

public static partial class LibraryStructuralReport
{
    private enum TypeLeverageRelationship
    {
        SignatureUse,
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
        GraphDistinctNeighborDegreeResult SignatureOutgoing);

    public static LibraryStructuralTypeLeverageDocument CreateTypeLeverage(
        MetadataLibrarySignatureUseResult signatureUse)
    {
        ArgumentNullException.ThrowIfNull(signatureUse);
        return ProjectTypeLeverage(
            signatureUse,
            ExecuteTypeLeverageGraph(signatureUse));
    }

    internal static TypeLeverageGraphExecution ExecuteTypeLeverageGraph(
        MetadataLibrarySignatureUseResult signatureUse)
    {
        ArgumentNullException.ThrowIfNull(signatureUse);

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
        GraphDistinctNeighborDegreeResult signatureOutgoing =
            Degree(
                graph,
                [TypeLeverageRelationship.SignatureUse],
                GraphTraversalDirection.Outgoing);

        return new(
            [.. types],
            signatureIncoming,
            signatureOutgoing);
    }

    internal static LibraryStructuralTypeLeverageDocument ProjectTypeLeverage(
        MetadataLibrarySignatureUseResult signatureUse,
        TypeLeverageGraphExecution execution)
    {
        ArgumentNullException.ThrowIfNull(signatureUse);
        ArgumentNullException.ThrowIfNull(execution);

        LibraryStructuralTypeLeverageRow[] rows =
        [
            .. execution.Types.Select((type, nodeId) => new
                {
                    Type = type,
                    SignatureIncoming =
                        execution.SignatureIncoming.Rows[nodeId].Degree,
                    SignatureOutgoing =
                        execution.SignatureOutgoing.Rows[nodeId].Degree,
                })
                .Where(static item =>
                    item.SignatureIncoming + item.SignatureOutgoing > 0)
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
                        item.SignatureOutgoing,
                        Role(
                            item.SignatureIncoming,
                            item.SignatureOutgoing));
                }),
        ];

        LibraryStructuralEvidenceDisposition disposition =
            signatureUse.Disposition
                == MetadataLibrarySignatureUseDisposition.Complete
                    ? LibraryStructuralEvidenceDisposition.Complete
                    : LibraryStructuralEvidenceDisposition.Qualified;

        return new(
            [.. rows.OrderBy(static row => row.Type.Definition.Value)],
            new(
                disposition,
                Order(
                    rows,
                    static row => row.SignatureIncomingDegree)),
            new(
                disposition,
                Order(
                    rows,
                    static row => row.SignatureOutgoingDegree)),
            disposition,
            new(
                signatureUse.Receipt,
                signatureUse.Disposition,
                signatureUse.Coverage,
                signatureUse.Occurrences.Length,
                signatureUse.Diagnostics),
            new(
                execution.SignatureIncoming.Receipt,
                execution.SignatureOutgoing.Receipt));
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
