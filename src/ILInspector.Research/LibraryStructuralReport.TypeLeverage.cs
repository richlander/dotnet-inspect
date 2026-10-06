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
        GraphDistinctNeighborDegreeResult Incoming,
        GraphDistinctNeighborDegreeResult Outgoing);

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

    public static LibraryStructuralBodyTypeLeverageShard
        CreateBodyTypeLeverageShard(
            MetadataLibrarySignatureUseResult typeInventory,
            AnalysisLibraryBodyUseResult bodyUse) =>
        CreateBodyTypeLeverageShards(
            [typeInventory],
            bodyUse)[0];

    public static ImmutableArray<LibraryStructuralBodyTypeLeverageShard>
        CreateBodyTypeLeverageShards(
            IEnumerable<MetadataLibrarySignatureUseResult> typeInventories,
            AnalysisLibraryBodyUseResult bodyUse)
    {
        ArgumentNullException.ThrowIfNull(typeInventories);
        ArgumentNullException.ThrowIfNull(bodyUse);
        MetadataLibrarySignatureUseResult[] inventories =
            [.. typeInventories];
        Dictionary<
            MetadataTypeDefinitionAddress,
            AnalysisLibraryBodyUseType> bodyTypes =
                bodyUse.Types.ToDictionary(
                    static type => type.Type);
        Dictionary<string, int> bodyTypeCounts =
            bodyUse.Types
                .GroupBy(
                    static type => type.Name.Namespace,
                    StringComparer.Ordinal)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Count(),
                    StringComparer.Ordinal);
        var edgesByNamespace =
            new Dictionary<
                string,
                List<(
                    MetadataTypeDefinitionAddress Source,
                    MetadataTypeDefinitionAddress Target)>>(
                StringComparer.Ordinal);
        foreach (AnalysisLibraryBodyUseOccurrence occurrence
            in bodyUse.Occurrences)
        {
            if (!bodyTypes.TryGetValue(
                    occurrence.Source,
                    out AnalysisLibraryBodyUseType? source)
                || !bodyTypes.TryGetValue(
                    occurrence.Target,
                    out AnalysisLibraryBodyUseType? target)
                || !StringComparer.Ordinal.Equals(
                    source.Name.Namespace,
                    target.Name.Namespace))
            {
                continue;
            }
            if (!edgesByNamespace.TryGetValue(
                    source.Name.Namespace,
                    out List<(
                        MetadataTypeDefinitionAddress Source,
                        MetadataTypeDefinitionAddress Target)>? edges))
            {
                edges = [];
                edgesByNamespace.Add(
                    source.Name.Namespace,
                    edges);
            }
            edges.Add((occurrence.Source, occurrence.Target));
        }

        var shards =
            ImmutableArray.CreateBuilder<
                LibraryStructuralBodyTypeLeverageShard>(
                inventories.Length);
        foreach (MetadataLibrarySignatureUseResult typeInventory
            in inventories)
        {
            string @namespace = ValidateBodyTypeInventory(
                typeInventory,
                bodyUse,
                bodyTypes,
                bodyTypeCounts);
            edgesByNamespace.TryGetValue(
                @namespace,
                out List<(
                    MetadataTypeDefinitionAddress Source,
                    MetadataTypeDefinitionAddress Target)>? edges);
            shards.Add(
                ProjectBodyTypeLeverageShard(
                    typeInventory,
                    bodyUse,
                    ExecuteBodyTypeLeverageGraph(
                        typeInventory,
                        edges ?? [])));
        }
        return shards.DrainToImmutable();
    }

    private static string ValidateBodyTypeInventory(
        MetadataLibrarySignatureUseResult typeInventory,
        AnalysisLibraryBodyUseResult bodyUse,
        IReadOnlyDictionary<
            MetadataTypeDefinitionAddress,
            AnalysisLibraryBodyUseType> bodyTypes,
        IReadOnlyDictionary<string, int> bodyTypeCounts)
    {
        ArgumentNullException.ThrowIfNull(typeInventory);
        string @namespace =
            typeInventory.Receipt.ExactNamespace
            ?? throw new ArgumentException(
                "A body Type-leverage shard requires an exact-namespace "
                    + "metadata Type inventory.",
                nameof(typeInventory));
        if (typeInventory.Types.Any(type =>
                !StringComparer.Ordinal.Equals(
                    type.Name.Namespace,
                    @namespace)))
        {
            throw new ArgumentException(
                "The metadata Type inventory contains Types outside "
                    + "its exact namespace.",
                nameof(typeInventory));
        }
        if (bodyUse.Receipt.ModuleVersionId
                != typeInventory.Receipt.ModuleVersionId
            || !Equals(
                bodyUse.Receipt.Assembly,
                typeInventory.Receipt.Assembly))
        {
            throw new ArgumentException(
                "Body Type leverage requires Metadata and Analysis "
                    + "evidence from the same exact Library generation.",
                nameof(bodyUse));
        }

        foreach (MetadataLibrarySignatureType type
            in typeInventory.Types)
        {
            if (!bodyTypes.TryGetValue(
                    type.Type,
                    out AnalysisLibraryBodyUseType? bodyType)
                || !Equals(bodyType.Name, type.Name)
                || bodyType.DefinitionKind != type.DefinitionKind)
            {
                throw new ArgumentException(
                    "The body-use Type inventory does not correspond to "
                        + "the metadata Type inventory.",
                    nameof(bodyUse));
            }
        }
        int bodyTypeCount = bodyTypeCounts.TryGetValue(
            @namespace,
            out int count)
                ? count
                : 0;
        if (bodyTypeCount != typeInventory.Types.Length)
        {
            throw new ArgumentException(
                "The body-use Type inventory has different exact-namespace "
                    + "coverage than the metadata Type inventory.",
                nameof(bodyUse));
        }

        return @namespace;
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
        return ExecuteTypeLeverageGraph(
            signatureUse.Types,
            signatureUse.Occurrences.Select(static occurrence =>
                (occurrence.Source, occurrence.Target)),
            TypeLeverageRelationship.SignatureUse);
    }

    internal static TypeLeverageGraphExecution
        ExecuteBodyTypeLeverageGraph(
            MetadataLibrarySignatureUseResult typeInventory,
            AnalysisLibraryBodyUseResult bodyUse)
    {
        ArgumentNullException.ThrowIfNull(typeInventory);
        ArgumentNullException.ThrowIfNull(bodyUse);
        return ExecuteTypeLeverageGraph(
            typeInventory.Types,
            bodyUse.Occurrences.Select(static occurrence =>
                (occurrence.Source, occurrence.Target)),
            TypeLeverageRelationship.BodyUse);
    }

    private static TypeLeverageGraphExecution
        ExecuteBodyTypeLeverageGraph(
            MetadataLibrarySignatureUseResult typeInventory,
            IEnumerable<(
                MetadataTypeDefinitionAddress Source,
                MetadataTypeDefinitionAddress Target)> sourceEdges)
    {
        ArgumentNullException.ThrowIfNull(typeInventory);
        ArgumentNullException.ThrowIfNull(sourceEdges);
        return ExecuteTypeLeverageGraph(
            typeInventory.Types,
            sourceEdges,
            TypeLeverageRelationship.BodyUse);
    }

    private static TypeLeverageGraphExecution ExecuteTypeLeverageGraph(
        IEnumerable<MetadataLibrarySignatureType> sourceTypes,
        IEnumerable<(
            MetadataTypeDefinitionAddress Source,
            MetadataTypeDefinitionAddress Target)> sourceEdges,
        TypeLeverageRelationship relationship)
    {
        MetadataLibrarySignatureType[] types =
        [
            .. sourceTypes.OrderBy(
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
        foreach ((MetadataTypeDefinitionAddress source,
            MetadataTypeDefinitionAddress target) in sourceEdges)
        {
            if (!nodeIds.TryGetValue(source, out int sourceNodeId)
                || !nodeIds.TryGetValue(target, out int targetNodeId))
            {
                continue;
            }
            logicalEdges.Add(
                new(
                    sourceNodeId,
                    targetNodeId,
                    relationship));
        }

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

        GraphDistinctNeighborDegreeResult incoming =
            Degree(
                graph,
                [relationship],
                GraphTraversalDirection.Incoming);
        GraphDistinctNeighborDegreeResult outgoing =
            Degree(
                graph,
                [relationship],
                GraphTraversalDirection.Outgoing);

        return new(
            [.. types],
            incoming,
            outgoing);
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
                        execution.Incoming.Rows[nodeId].Degree,
                    SignatureOutgoing =
                        execution.Outgoing.Rows[nodeId].Degree,
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
                        Pole: null)),
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
                Pole = Pole(
                    row,
                    seaLevelMaximum,
                    mountainPeakMaximum),
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
                execution.Incoming.Receipt,
                execution.Outgoing.Receipt));
    }

    internal static LibraryStructuralBodyTypeLeverageShard
        ProjectBodyTypeLeverageShard(
            MetadataLibrarySignatureUseResult typeInventory,
            AnalysisLibraryBodyUseResult bodyUse,
            TypeLeverageGraphExecution execution)
    {
        ArgumentNullException.ThrowIfNull(typeInventory);
        ArgumentNullException.ThrowIfNull(bodyUse);
        ArgumentNullException.ThrowIfNull(execution);
        string @namespace =
            typeInventory.Receipt.ExactNamespace
            ?? throw new ArgumentException(
                "A body Type-leverage shard requires an exact namespace.",
                nameof(typeInventory));

        LibraryStructuralBodyTypeLeverageRow[] rows =
        [
            .. execution.Types.Select((type, nodeId) => new
                {
                    Type = type,
                    Incoming = execution.Incoming.Rows[nodeId].Degree,
                    Outgoing = execution.Outgoing.Rows[nodeId].Degree,
                })
                .Where(static item =>
                    item.Incoming + item.Outgoing > 0)
                .Select(static item =>
                    new LibraryStructuralBodyTypeLeverageRow(
                        item.Type.Type,
                        item.Type.Name,
                        item.Type.Classification,
                        IsDesignationEligible(
                            item.Type.Classification),
                        item.Incoming,
                        item.Outgoing,
                        Role(
                            item.Incoming,
                            item.Outgoing),
                        Pole: null)),
        ];
        int seaLevelMaximum =
            EligibleMaximum(
                rows,
                static row => row.BodyIncomingDegree);
        int mountainPeakMaximum =
            EligibleMaximum(
                rows,
                static row => row.BodyOutgoingDegree);
        rows =
        [
            .. rows.Select(row => row with
            {
                Pole = Pole(
                    row.DesignationEligible,
                    row.BodyIncomingDegree,
                    row.BodyOutgoingDegree,
                    seaLevelMaximum,
                    mountainPeakMaximum),
            }),
        ];

        LibraryStructuralEvidenceDisposition disposition =
            Disposition(typeInventory, bodyUse);
        return new(
            LibraryStructuralSalience.CurrentMethodologyVersion,
            LibraryStructuralSalienceEvidenceMode.BodyUse,
            @namespace,
            [.. rows.OrderBy(static row => row.Type.Definition.Value)],
            new(
                disposition,
                Order(
                    rows,
                    static row => row.BodyIncomingDegree)),
            new(
                disposition,
                Order(
                    rows,
                    static row => row.BodyOutgoingDegree)),
            disposition,
            Qualification(typeInventory),
            Qualification(bodyUse),
            new(
                execution.Incoming.Receipt,
                execution.Outgoing.Receipt));
    }

    private static LibraryStructuralSignatureUseQualification Qualification(
        MetadataLibrarySignatureUseResult signatureUse) =>
        new(
            signatureUse.Receipt,
            signatureUse.Disposition,
            signatureUse.Coverage,
            signatureUse.Occurrences.Length,
            signatureUse.Diagnostics);

    private static LibraryStructuralBodyUseQualification Qualification(
        AnalysisLibraryBodyUseResult bodyUse) =>
        new(
            bodyUse.Receipt,
            bodyUse.Disposition,
            bodyUse.Coverage,
            bodyUse.Occurrences.Length,
            bodyUse.Diagnostics);

    private static LibraryStructuralEvidenceDisposition Disposition(
        MetadataLibrarySignatureUseResult signatureUse) =>
        signatureUse.Disposition
            == MetadataLibrarySignatureUseDisposition.Complete
                ? LibraryStructuralEvidenceDisposition.Complete
                : LibraryStructuralEvidenceDisposition.Qualified;

    private static LibraryStructuralEvidenceDisposition Disposition(
        MetadataLibrarySignatureUseResult typeInventory,
        AnalysisLibraryBodyUseResult bodyUse) =>
        typeInventory.Disposition
                == MetadataLibrarySignatureUseDisposition.Complete
            && bodyUse.Disposition
                == AnalysisLibraryBodyUseDisposition.Complete
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

    private static int EligibleMaximum(
        IEnumerable<LibraryStructuralBodyTypeLeverageRow> rows,
        Func<LibraryStructuralBodyTypeLeverageRow, int> degree) =>
        rows
            .Where(static row => row.DesignationEligible)
            .Select(degree)
            .DefaultIfEmpty()
            .Max();

    private static LibraryStructuralTypePole? Pole(
        LibraryStructuralTypeLeverageRow row,
        int seaLevelMaximum,
        int mountainPeakMaximum) =>
        Pole(
            row.DesignationEligible,
            row.SignatureIncomingDegree,
            row.SignatureOutgoingDegree,
            seaLevelMaximum,
            mountainPeakMaximum);

    private static LibraryStructuralTypePole? Pole(
        bool designationEligible,
        int incomingDegree,
        int outgoingDegree,
        int seaLevelMaximum,
        int mountainPeakMaximum)
    {
        bool seaLevel =
            designationEligible
            && IsDesignationDegree(
                incomingDegree,
                seaLevelMaximum);
        bool mountainPeak =
            designationEligible
            && IsDesignationDegree(
                outgoingDegree,
                mountainPeakMaximum);
        if (seaLevel && mountainPeak)
        {
            if (incomingDegree == outgoingDegree)
            {
                return null;
            }
            return incomingDegree > outgoingDegree
                ? LibraryStructuralTypePole.SeaLevel
                : LibraryStructuralTypePole.MountainPeak;
        }
        if (seaLevel)
            return LibraryStructuralTypePole.SeaLevel;
        if (mountainPeak)
            return LibraryStructuralTypePole.MountainPeak;
        return null;
    }

    private static bool IsDesignationDegree(
        int degree,
        int maximum) =>
        maximum >= LibraryStructuralSalience.MinimumDesignationDegree
        && (maximum
                < LibraryStructuralSalience.MinimumCohortMaximumDegree
            ? degree == maximum
            : degree * 100L
                >= maximum
                    * LibraryStructuralSalience
                        .CohortMinimumPercentage);

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

    private static ImmutableArray<MetadataTypeDefinitionAddress> Order(
        IEnumerable<LibraryStructuralBodyTypeLeverageRow> rows,
        Func<LibraryStructuralBodyTypeLeverageRow, int> degree) =>
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
