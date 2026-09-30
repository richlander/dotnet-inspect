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

    public static LibraryStructuralNamespaceLeverageIndex
        CreateNamespaceLeverageIndex(
            MetadataLibrarySignatureUseResult signatureUse)
    {
        ArgumentNullException.ThrowIfNull(signatureUse);
        if (signatureUse.Receipt.ExactNamespace is not null)
        {
            throw new ArgumentException(
                "Namespace leverage requires a whole-Library "
                    + "signature-use population.",
                nameof(signatureUse));
        }

        var externalSources =
            new Dictionary<
                string,
                HashSet<MetadataTypeDefinitionAddress>>(
                    StringComparer.Ordinal);
        foreach (MetadataLibrarySignatureUseOccurrence occurrence
            in signatureUse.Occurrences)
        {
            if (StringComparer.Ordinal.Equals(
                    occurrence.SourceType.Namespace,
                    occurrence.TargetType.Namespace))
            {
                continue;
            }

            if (!externalSources.TryGetValue(
                    occurrence.TargetType.Namespace,
                    out HashSet<
                        MetadataTypeDefinitionAddress>? sources))
            {
                sources = [];
                externalSources.Add(
                    occurrence.TargetType.Namespace,
                    sources);
            }
            sources.Add(occurrence.Source);
        }

        var populations =
            signatureUse.Types
                .GroupBy(
                    static type => type.Name.Namespace,
                    StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Count(),
                    StringComparer.Ordinal);
        int maximum =
            populations.Keys
                .Select(@namespace =>
                    externalSources.TryGetValue(
                        @namespace,
                        out HashSet<
                            MetadataTypeDefinitionAddress>? sources)
                        ? sources.Count
                        : 0)
                .DefaultIfEmpty()
                .Max();
        LibraryStructuralNamespaceLeverageRow[] rows =
        [
            .. populations
                .Select(pair =>
                {
                    int score =
                        externalSources.TryGetValue(
                            pair.Key,
                            out HashSet<
                                MetadataTypeDefinitionAddress>? sources)
                            ? sources.Count
                            : 0;
                    return new LibraryStructuralNamespaceLeverageRow(
                        pair.Key,
                        pair.Value,
                        score,
                        maximum > 0 && score == maximum);
                })
                .OrderByDescending(
                    static row =>
                        row.ExternalIncomingSourceTypeCount)
                .ThenBy(
                    static row => row.Namespace,
                    StringComparer.Ordinal),
        ];
        LibraryStructuralEvidenceDisposition disposition =
            Disposition(signatureUse);
        return new(
            LibraryStructuralSalience.CurrentMethodologyVersion,
            LibraryStructuralSalienceEvidenceMode.Signature,
            disposition,
            [.. rows],
            Qualification(signatureUse));
    }

    public static LibraryStructuralTypeLeverageShard
        CreateTypeLeverageShard(
            MetadataLibrarySignatureUseResult signatureUse)
    {
        ArgumentNullException.ThrowIfNull(signatureUse);
        string @namespace =
            signatureUse.Receipt.ExactNamespace
            ?? throw new ArgumentException(
                "A Type-leverage shard requires an exact-namespace "
                    + "signature-use population.",
                nameof(signatureUse));
        if (signatureUse.Types.Any(type =>
                !StringComparer.Ordinal.Equals(
                    type.Name.Namespace,
                    @namespace))
            || signatureUse.Occurrences.Any(occurrence =>
                !StringComparer.Ordinal.Equals(
                    occurrence.SourceType.Namespace,
                    @namespace)
                || !StringComparer.Ordinal.Equals(
                    occurrence.TargetType.Namespace,
                    @namespace)))
        {
            throw new ArgumentException(
                "The signature-use population contains evidence outside "
                    + "its exact namespace.",
                nameof(signatureUse));
        }

        return ProjectTypeLeverageShard(
            signatureUse,
            ExecuteTypeLeverageGraph(signatureUse));
    }

    public static LibraryStructuralSalienceDocument
        CreateStructuralSalience(
            LibraryStructuralNamespaceLeverageIndex namespaceIndex,
            IEnumerable<LibraryStructuralTypeLeverageShard> shards)
    {
        ArgumentNullException.ThrowIfNull(namespaceIndex);
        ArgumentNullException.ThrowIfNull(shards);
        LibraryStructuralTypeLeverageShard[] materialized = [.. shards];
        if (namespaceIndex.MethodologyVersion
                != LibraryStructuralSalience.CurrentMethodologyVersion
            || namespaceIndex.EvidenceMode
                != LibraryStructuralSalienceEvidenceMode.Signature)
        {
            throw new ArgumentException(
                "The namespace index does not use the current structural "
                    + "salience methodology.",
                nameof(namespaceIndex));
        }
        if (materialized.Length != namespaceIndex.Rows.Length)
        {
            throw new ArgumentException(
                "Exhaustive structural salience requires exactly one "
                    + "Type-leverage shard per namespace.",
                nameof(shards));
        }

        for (var index = 0; index < materialized.Length; index++)
        {
            LibraryStructuralTypeLeverageShard shard =
                materialized[index];
            if (shard.MethodologyVersion
                    != namespaceIndex.MethodologyVersion
                || shard.EvidenceMode != namespaceIndex.EvidenceMode
                || !StringComparer.Ordinal.Equals(
                    shard.Namespace,
                    namespaceIndex.Rows[index].Namespace)
                || shard.SignatureUse.Receipt.ModuleVersionId
                    != namespaceIndex.SignatureUse.Receipt.ModuleVersionId
                || !Equals(
                    shard.SignatureUse.Receipt.Assembly,
                    namespaceIndex.SignatureUse.Receipt.Assembly))
            {
                throw new ArgumentException(
                    "A Type-leverage shard does not correspond to the "
                        + "namespace index.",
                    nameof(shards));
            }
        }

        return new(
            LibraryStructuralSalience.CurrentMethodologyVersion,
            LibraryStructuralSalienceEvidenceMode.Signature,
            namespaceIndex,
            [.. materialized]);
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

    internal static LibraryStructuralTypeLeverageShard
        ProjectTypeLeverageShard(
            MetadataLibrarySignatureUseResult signatureUse,
            TypeLeverageGraphExecution execution)
    {
        ArgumentNullException.ThrowIfNull(signatureUse);
        ArgumentNullException.ThrowIfNull(execution);
        string @namespace =
            signatureUse.Receipt.ExactNamespace
            ?? throw new ArgumentException(
                "A Type-leverage shard requires an exact namespace.",
                nameof(signatureUse));

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
                    new LibraryStructuralTypeLeverageRow(
                        item.Type.Type,
                        item.Type.Name,
                        item.Type.Classification,
                        IsDesignationEligible(
                            item.Type.Classification),
                        item.SignatureIncoming,
                        item.SignatureOutgoing,
                        Role(
                            item.SignatureIncoming,
                            item.SignatureOutgoing),
                        SeaLevel: false,
                        MountainPeak: false)),
        ];
        int seaLevelMaximum =
            EligibleMaximum(
                rows,
                static row => row.SignatureIncomingDegree);
        int mountainPeakMaximum =
            EligibleMaximum(
                rows,
                static row => row.SignatureOutgoingDegree);
        rows =
        [
            .. rows.Select(row => row with
            {
                SeaLevel =
                    seaLevelMaximum
                        >= LibraryStructuralSalience
                            .MinimumDesignationDegree
                    && row.DesignationEligible
                    && row.SignatureIncomingDegree == seaLevelMaximum,
                MountainPeak =
                    mountainPeakMaximum
                        >= LibraryStructuralSalience
                            .MinimumDesignationDegree
                    && row.DesignationEligible
                    && row.SignatureOutgoingDegree
                        == mountainPeakMaximum,
            }),
        ];

        LibraryStructuralEvidenceDisposition disposition =
            Disposition(signatureUse);
        return new(
            LibraryStructuralSalience.CurrentMethodologyVersion,
            LibraryStructuralSalienceEvidenceMode.Signature,
            @namespace,
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
            Qualification(signatureUse),
            new(
                execution.SignatureIncoming.Receipt,
                execution.SignatureOutgoing.Receipt));
    }

    private static LibraryStructuralSignatureUseQualification Qualification(
        MetadataLibrarySignatureUseResult signatureUse) =>
        new(
            signatureUse.Receipt,
            signatureUse.Disposition,
            signatureUse.Coverage,
            signatureUse.Occurrences.Length,
            signatureUse.Diagnostics);

    private static LibraryStructuralEvidenceDisposition Disposition(
        MetadataLibrarySignatureUseResult signatureUse) =>
        signatureUse.Disposition
            == MetadataLibrarySignatureUseDisposition.Complete
                ? LibraryStructuralEvidenceDisposition.Complete
                : LibraryStructuralEvidenceDisposition.Qualified;

    private static int EligibleMaximum(
        IEnumerable<LibraryStructuralTypeLeverageRow> rows,
        Func<LibraryStructuralTypeLeverageRow, int> degree) =>
        rows
            .Where(static row => row.DesignationEligible)
            .Select(degree)
            .DefaultIfEmpty()
            .Max();

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
            .Where(static row => row.DesignationEligible)
            .OrderByDescending(degree)
            .ThenBy(static row => row.Type.Definition.Value)
            .Select(static row => row.Type),
    ];

    private static bool IsDesignationEligible(
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
