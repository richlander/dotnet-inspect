using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Research;

public enum LibraryFamilyRoleCompositionRejection
{
    NameFamilyBindingMismatch,
    StructuralMethodologyMismatch,
    StructuralReceiptMismatch,
    DuplicateNameFamilyType,
    DuplicateStructuralType,
    DuplicateNamespace,
    NamespaceShardCountMismatch,
    NamespaceShardOrderMismatch,
    NamespaceTypeCountMismatch,
    StructuralTypeNotFound,
    StructuralTypeNamespaceMismatch,
    StructuredNameMismatch,
    StructuralOrderTypeNotFound,
    StructuralOrderPoleMismatch,
    DuplicatePopulation,
    PopulationTypeCountMismatch,
    PopulationFamilyMismatch,
}

public sealed record LibraryFamilyRoleBinding(
    AssemblyArtifactIdentity Artifact,
    AssemblyReferenceIdentity Assembly,
    Guid ModuleVersionId);

public sealed record LibraryFamilyRoleTypeRow(
    MetadataTypeDefinitionAddress Type,
    MetadataTypeDefinitionName Name,
    AssemblyTypeDefinitionKind DefinitionKind,
    string Namespace,
    LibraryNameFamilyIdentity? OneWordSuffix,
    LibraryNameFamilyIdentity? TwoWordSuffix,
    LibraryNameFamilyOneWordResidualReason? OneWordResidual,
    LibraryNameFamilyTwoWordResidualReason? TwoWordResidual,
    PdbTypeSourceDisposition? SourceDisposition,
    int? SignatureIncomingDegree,
    int? SignatureOutgoingDegree,
    MetadataLibraryTypeClassification? StructuralClassification,
    LibraryStructuralTypeRole? StructuralRole,
    LibraryStructuralTypePole? StructuralPole,
    LibraryStructuralEvidenceDisposition StructuralDisposition);

public sealed record LibraryFamilyRoleRow(
    LibraryNameFamilyIdentity Identity,
    int TypeCount,
    int FoundationCount,
    int HubCount,
    int OrchestratorCount,
    int SeaLevelCount,
    int MountainPeakCount,
    int NoIssuedStructuralRoleCount,
    int DistinctNamespaceCount,
    LibraryStructuralEvidenceDisposition StructuralDisposition,
    ImmutableArray<MetadataTypeDefinitionAddress> Types,
    ImmutableArray<MetadataTypeDefinitionAddress> Foundations,
    ImmutableArray<MetadataTypeDefinitionAddress> Hubs,
    ImmutableArray<MetadataTypeDefinitionAddress> Orchestrators,
    ImmutableArray<MetadataTypeDefinitionAddress> SeaLevels,
    ImmutableArray<MetadataTypeDefinitionAddress> MountainPeaks,
    ImmutableArray<MetadataTypeDefinitionAddress> NoIssuedStructuralRoles);

public sealed record LibraryFamilyRolePopulation(
    LibraryNameFamilyPopulationKind Kind,
    int TypeCount,
    int FoundationCount,
    int HubCount,
    int OrchestratorCount,
    int SeaLevelCount,
    int MountainPeakCount,
    int NoIssuedStructuralRoleCount,
    LibraryNameFamilyProvenanceQualification Provenance,
    LibraryStructuralEvidenceDisposition StructuralDisposition,
    ImmutableArray<LibraryFamilyRoleRow> Families);

public sealed record LibraryFamilyRoleStructuralShardReceipt(
    string Namespace,
    LibraryStructuralEvidenceDisposition RoleDisposition,
    LibraryStructuralSignatureUseQualification SignatureUse,
    LibraryStructuralTypeLeverageGraphWork GraphWork,
    LibraryStructuralEvidenceDisposition SeaLevelDisposition,
    LibraryStructuralEvidenceDisposition MountainPeakDisposition);

public sealed record LibraryFamilyRoleStructuralReceipt(
    string MethodologyVersion,
    LibraryStructuralSalienceEvidenceMode EvidenceMode,
    LibraryStructuralEvidenceDisposition NamespaceDisposition,
    LibraryStructuralSignatureUseQualification SignatureUse,
    ImmutableArray<LibraryFamilyRoleStructuralShardReceipt> Shards);

public sealed record LibraryFamilyRolePopulationReceipt(
    LibraryNameFamilyPopulationKind Kind,
    int TypeCount,
    int FoundationCount,
    int HubCount,
    int OrchestratorCount,
    int SeaLevelCount,
    int MountainPeakCount,
    int NoIssuedStructuralRoleCount);

public sealed record LibraryFamilyRoleCompositionReceipt(
    int ExactTypeCount,
    int FoundationCount,
    int HubCount,
    int OrchestratorCount,
    int SeaLevelCount,
    int MountainPeakCount,
    int NoIssuedStructuralRoleCount,
    int OneWordFamilyRowCount,
    int TwoWordFamilyRowCount,
    ImmutableArray<LibraryFamilyRolePopulationReceipt> Populations,
    LibraryNameFamilyReceipt NameFamilies,
    LibraryFamilyRoleStructuralReceipt StructuralSalience);

public sealed record LibraryFamilyRoleCompositionDocument(
    LibraryFamilyRoleBinding Binding,
    string MethodologyVersion,
    LibraryNameFamilyMethodology NameFamilyMethodology,
    LibraryNameFamilyProvenanceQualification Provenance,
    LibraryFamilyRoleStructuralReceipt StructuralSalience,
    ImmutableArray<LibraryFamilyRoleTypeRow> Types,
    ImmutableArray<LibraryFamilyRolePopulation> Populations,
    LibraryFamilyRoleCompositionReceipt Receipt);

public abstract record LibraryFamilyRoleCompositionOutcome
{
    private LibraryFamilyRoleCompositionOutcome()
    {
    }

    public sealed record Available(LibraryFamilyRoleCompositionDocument Document)
        : LibraryFamilyRoleCompositionOutcome;

    public sealed record Rejected(
        LibraryFamilyRoleCompositionRejection Reason,
        string Detail)
        : LibraryFamilyRoleCompositionOutcome;
}

public static class LibraryFamilyRoleComposition
{
    public const string MethodologyVersion =
        "library-family-role-composition.v1";

    public static LibraryFamilyRoleCompositionOutcome Execute(
        LibraryFamilyRoleBinding binding,
        LibraryNameFamilyDocument nameFamilies,
        LibraryStructuralSalienceDocument structuralSalience)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(nameFamilies);
        ArgumentNullException.ThrowIfNull(structuralSalience);

        if (!BindingMatches(binding, nameFamilies.Binding))
        {
            return Rejected(
                LibraryFamilyRoleCompositionRejection
                    .NameFamilyBindingMismatch,
                "The name-family document does not share the exact "
                    + "composition artifact binding.");
        }

        if (!StructuralMethodologyMatches(structuralSalience))
        {
            return Rejected(
                LibraryFamilyRoleCompositionRejection
                    .StructuralMethodologyMismatch,
                "Structural salience must use one consistent current "
                    + "signature-use methodology.");
        }

        if (!ReceiptMatches(
                structuralSalience.NamespaceIndex.SignatureUse.Receipt,
                binding)
            || structuralSalience.NamespaceIndex.SignatureUse.Receipt
                .ExactNamespace is not null)
        {
            return Rejected(
                LibraryFamilyRoleCompositionRejection
                    .StructuralReceiptMismatch,
                "The structural namespace index does not share the exact "
                    + "assembly and module binding.");
        }

        var nameTypes =
            new Dictionary<
                MetadataTypeDefinitionAddress,
                LibraryNameFamilyTypeRow>(nameFamilies.Types.Length);
        foreach (LibraryNameFamilyTypeRow type in nameFamilies.Types)
        {
            if (type.Type.ModuleVersionId != binding.ModuleVersionId
                || !nameTypes.TryAdd(type.Type, type))
            {
                return Rejected(
                    LibraryFamilyRoleCompositionRejection
                        .DuplicateNameFamilyType,
                    "Name-family Type rows must contain unique addresses "
                        + "from the bound module.");
            }
        }

        var namespaceRows = new Dictionary<
            string,
            LibraryStructuralNamespaceLeverageRow>(
                StringComparer.Ordinal);
        foreach (LibraryStructuralNamespaceLeverageRow row
            in structuralSalience.NamespaceIndex.Rows)
        {
            if (!namespaceRows.TryAdd(row.Namespace, row))
            {
                return Rejected(
                    LibraryFamilyRoleCompositionRejection.DuplicateNamespace,
                    $"Structural namespace '{row.Namespace}' is duplicated.");
            }
        }

        if (structuralSalience.TypeLeverageShards.Length
            != structuralSalience.NamespaceIndex.Rows.Length)
        {
            return Rejected(
                LibraryFamilyRoleCompositionRejection
                    .NamespaceShardCountMismatch,
                "Structural salience requires one Type shard for every "
                    + "namespace-index row.");
        }

        var nameNamespaceCounts = nameFamilies.Types
            .GroupBy(static type => type.Name.Namespace, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Count(),
                StringComparer.Ordinal);
        if (nameNamespaceCounts.Count != namespaceRows.Count
            || nameNamespaceCounts.Any(pair =>
                !namespaceRows.TryGetValue(pair.Key, out var row)
                || row.TypeCount != pair.Value))
        {
            return Rejected(
                LibraryFamilyRoleCompositionRejection
                    .NamespaceTypeCountMismatch,
                "Structural namespace Type counts do not close against the "
                    + "name-family population.");
        }

        var structuralTypes =
            new Dictionary<
                MetadataTypeDefinitionAddress,
                StructuralTypeBinding>();
        for (var index = 0;
             index < structuralSalience.TypeLeverageShards.Length;
             index++)
        {
            LibraryStructuralTypeLeverageShard shard =
                structuralSalience.TypeLeverageShards[index];
            string expectedNamespace =
                structuralSalience.NamespaceIndex.Rows[index].Namespace;
            if (!StringComparer.Ordinal.Equals(
                    shard.Namespace,
                    expectedNamespace))
            {
                return Rejected(
                    LibraryFamilyRoleCompositionRejection
                        .NamespaceShardOrderMismatch,
                    "Structural Type shards do not follow namespace-index "
                        + "order.");
            }
            if (!ReceiptMatches(shard.SignatureUse.Receipt, binding)
                || !StringComparer.Ordinal.Equals(
                    shard.SignatureUse.Receipt.ExactNamespace,
                    shard.Namespace))
            {
                return Rejected(
                    LibraryFamilyRoleCompositionRejection
                        .StructuralReceiptMismatch,
                    $"Structural namespace '{shard.Namespace}' does not "
                        + "share the exact assembly and module binding.");
            }

            foreach (LibraryStructuralTypeLeverageRow row in shard.Rows)
            {
                if (!structuralTypes.TryAdd(
                        row.Type,
                        new(row, shard.RoleDisposition)))
                {
                    return Rejected(
                        LibraryFamilyRoleCompositionRejection
                            .DuplicateStructuralType,
                        "Structural Type rows must contain unique exact "
                            + "addresses.");
                }
                if (!nameTypes.TryGetValue(row.Type, out var nameType))
                {
                    return Rejected(
                        LibraryFamilyRoleCompositionRejection
                            .StructuralTypeNotFound,
                        "A structural Type row has no exact name-family Type.");
                }
                if (!StringComparer.Ordinal.Equals(
                        nameType.Name.Namespace,
                        shard.Namespace)
                    || !StringComparer.Ordinal.Equals(
                        row.Name.Namespace,
                        shard.Namespace))
                {
                    return Rejected(
                        LibraryFamilyRoleCompositionRejection
                            .StructuralTypeNamespaceMismatch,
                        "A structural Type row does not belong to its exact "
                            + "namespace shard.");
                }
                if (!Equals(row.Name, nameType.Name))
                {
                    return Rejected(
                        LibraryFamilyRoleCompositionRejection
                            .StructuredNameMismatch,
                        "Joined Type rows disagree on their structured "
                            + "metadata name.");
                }
            }

            LibraryFamilyRoleCompositionOutcome.Rejected? orderFailure =
                ValidateOrder(
                    shard,
                    shard.SeaLevel,
                    LibraryStructuralTypePole.SeaLevel);
            orderFailure ??= ValidateOrder(
                shard,
                shard.MountainPeak,
                LibraryStructuralTypePole.MountainPeak);
            if (orderFailure is not null)
                return orderFailure;
        }

        var structuralDispositionsByNamespace =
            structuralSalience.TypeLeverageShards.ToDictionary(
                static shard => shard.Namespace,
                static shard => shard.RoleDisposition,
                StringComparer.Ordinal);
        LibraryFamilyRoleTypeRow[] typeRows =
        [
            .. nameFamilies.Types
                .OrderBy(static type => type.Type.Definition.Value)
                .Select(type => CreateTypeRow(
                    type,
                    structuralTypes.GetValueOrDefault(type.Type),
                    structuralDispositionsByNamespace[
                        type.Name.Namespace])),
        ];
        var composedTypes = typeRows.ToDictionary(
            static type => type.Type);

        var seenPopulations = new HashSet<LibraryNameFamilyPopulationKind>();
        var populations =
            ImmutableArray.CreateBuilder<LibraryFamilyRolePopulation>(
                nameFamilies.Populations.Length);
        foreach (LibraryNameFamilyPopulation population
            in nameFamilies.Populations.OrderBy(static population =>
                population.Kind))
        {
            if (!seenPopulations.Add(population.Kind))
            {
                return Rejected(
                    LibraryFamilyRoleCompositionRejection
                        .DuplicatePopulation,
                    $"Name-family population '{population.Kind}' is duplicated.");
            }

            MetadataTypeDefinitionAddress[] populationTypes =
                PopulationTypes(population.Kind, typeRows);
            var populationTypeSet =
                new HashSet<MetadataTypeDefinitionAddress>(populationTypes);
            if (populationTypes.Length != population.TypeCount)
            {
                return Rejected(
                    LibraryFamilyRoleCompositionRejection
                        .PopulationTypeCountMismatch,
                    $"Population '{population.Kind}' does not close against "
                        + "its exact Type rows.");
            }

            var families =
                ImmutableArray.CreateBuilder<LibraryFamilyRoleRow>(
                    population.Families.Length);
            foreach (LibraryNameFamilyRow family
                in population.Families.Order(
                    LibraryNameFamilyOrder.Prevalence))
            {
                if (family.TypeCount != family.Types.Length
                    || family.Types.Distinct().Count()
                        != family.Types.Length
                    || family.Types.Any(type =>
                        !composedTypes.TryGetValue(type, out var composed)
                        || !populationTypeSet.Contains(type)
                        || !HasFamily(composed, family.Identity)))
                {
                    return Rejected(
                        LibraryFamilyRoleCompositionRejection
                            .PopulationFamilyMismatch,
                        $"Family '{FamilyDisplay(family.Identity)}' does not "
                            + "match its exact source population.");
                }

                families.Add(
                    CreateFamilyRow(
                        family.Identity,
                        family.Types,
                        composedTypes));
            }

            populations.Add(
                CreatePopulation(
                    population.Kind,
                    populationTypes,
                    nameFamilies.Provenance,
                    families.MoveToImmutable(),
                    composedTypes,
                    structuralSalience.NamespaceIndex.Disposition));
        }

        if (!seenPopulations.Contains(
                LibraryNameFamilyPopulationKind.AllTypes))
        {
            return Rejected(
                LibraryFamilyRoleCompositionRejection
                    .PopulationTypeCountMismatch,
                "The name-family document does not contain its all-Types "
                    + "population.");
        }

        ImmutableArray<LibraryFamilyRolePopulation> populationRows =
            populations.MoveToImmutable();
        LibraryFamilyRolePopulation allTypes = populationRows.Single(
            static population =>
                population.Kind
                    == LibraryNameFamilyPopulationKind.AllTypes);
        LibraryFamilyRoleStructuralReceipt structuralReceipt =
            CreateStructuralReceipt(structuralSalience);
        int oneWordFamilyCount = populationRows.Sum(static population =>
            population.Families.Count(static family =>
                family.Identity.Kind
                    == LibraryNameFamilyKind.OneWordSuffix));
        int twoWordFamilyCount = populationRows.Sum(static population =>
            population.Families.Count(static family =>
                family.Identity.Kind
                    == LibraryNameFamilyKind.TwoWordSuffix));
        var receipt = new LibraryFamilyRoleCompositionReceipt(
            typeRows.Length,
            allTypes.FoundationCount,
            allTypes.HubCount,
            allTypes.OrchestratorCount,
            allTypes.SeaLevelCount,
            allTypes.MountainPeakCount,
            allTypes.NoIssuedStructuralRoleCount,
            oneWordFamilyCount,
            twoWordFamilyCount,
            [
                .. populationRows.Select(static population =>
                    new LibraryFamilyRolePopulationReceipt(
                        population.Kind,
                        population.TypeCount,
                        population.FoundationCount,
                        population.HubCount,
                        population.OrchestratorCount,
                        population.SeaLevelCount,
                        population.MountainPeakCount,
                        population.NoIssuedStructuralRoleCount)),
            ],
            nameFamilies.Receipt,
            structuralReceipt);

        return new LibraryFamilyRoleCompositionOutcome.Available(
            new(
                binding,
                MethodologyVersion,
                nameFamilies.Methodology,
                nameFamilies.Provenance,
                structuralReceipt,
                [.. typeRows],
                populationRows,
                receipt));
    }

    private static bool BindingMatches(
        LibraryFamilyRoleBinding binding,
        LibraryNameFamilyBinding nameFamilies) =>
        Equals(binding.Artifact, nameFamilies.Artifact)
        && Equals(binding.Assembly, nameFamilies.Assembly)
        && binding.ModuleVersionId == nameFamilies.ModuleVersionId;

    private static bool StructuralMethodologyMatches(
        LibraryStructuralSalienceDocument document) =>
        document.MethodologyVersion
            == LibraryStructuralSalience.CurrentMethodologyVersion
        && document.EvidenceMode
            == LibraryStructuralSalienceEvidenceMode.Signature
        && document.NamespaceIndex.MethodologyVersion
            == document.MethodologyVersion
        && document.NamespaceIndex.EvidenceMode == document.EvidenceMode
        && document.TypeLeverageShards.All(shard =>
            shard.MethodologyVersion == document.MethodologyVersion
            && shard.EvidenceMode == document.EvidenceMode);

    private static bool ReceiptMatches(
        MetadataLibrarySignatureUseReceipt receipt,
        LibraryFamilyRoleBinding binding) =>
        receipt.ModuleVersionId == binding.ModuleVersionId
        && Equals(receipt.Assembly, binding.Assembly);

    private static LibraryFamilyRoleCompositionOutcome.Rejected? ValidateOrder(
        LibraryStructuralTypeLeverageShard shard,
        LibraryStructuralTypeLeverageOrder order,
        LibraryStructuralTypePole pole)
    {
        var rows = shard.Rows.ToDictionary(static row => row.Type);
        var ordered = new HashSet<MetadataTypeDefinitionAddress>();
        foreach (MetadataTypeDefinitionAddress type in order.Types)
        {
            if (!ordered.Add(type) || !rows.TryGetValue(type, out var row))
            {
                return Rejected(
                    LibraryFamilyRoleCompositionRejection
                        .StructuralOrderTypeNotFound,
                    $"Structural {pole} order references an unissued or "
                        + "duplicate Type row.");
            }
        }

        if (shard.Rows.Any(row =>
                row.Pole == pole && !ordered.Contains(row.Type)))
        {
            return Rejected(
                LibraryFamilyRoleCompositionRejection
                    .StructuralOrderPoleMismatch,
                $"Structural {pole} order omits an owner-issued pole.");
        }

        return null;
    }

    private static LibraryFamilyRoleTypeRow CreateTypeRow(
        LibraryNameFamilyTypeRow name,
        StructuralTypeBinding? structural,
        LibraryStructuralEvidenceDisposition disposition) =>
        new(
            name.Type,
            name.Name,
            name.DefinitionKind,
            name.Name.Namespace,
            name.OneWordSuffix,
            name.TwoWordSuffix,
            name.OneWordResidual,
            name.TwoWordResidual,
            name.SourceEvidence?.Disposition,
            structural?.Row.SignatureIncomingDegree,
            structural?.Row.SignatureOutgoingDegree,
            structural?.Row.Classification,
            structural?.Row.Role,
            structural?.Row.Pole,
            structural?.Disposition ?? disposition);

    private static MetadataTypeDefinitionAddress[] PopulationTypes(
        LibraryNameFamilyPopulationKind kind,
        IEnumerable<LibraryFamilyRoleTypeRow> types)
    {
        PdbTypeSourceDisposition? disposition = kind switch
        {
            LibraryNameFamilyPopulationKind.AllTypes => null,
            LibraryNameFamilyPopulationKind.OrdinaryEvidenceOnly =>
                PdbTypeSourceDisposition.OrdinaryEvidenceOnly,
            LibraryNameFamilyPopulationKind.GeneratedEvidenceOnly =>
                PdbTypeSourceDisposition.GeneratedEvidenceOnly,
            LibraryNameFamilyPopulationKind.MixedEvidence =>
                PdbTypeSourceDisposition.MixedEvidence,
            LibraryNameFamilyPopulationKind.Unknown =>
                PdbTypeSourceDisposition.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        return
        [
            .. types
                .Where(type =>
                    kind == LibraryNameFamilyPopulationKind.AllTypes
                    || type.SourceDisposition == disposition)
                .Select(static type => type.Type),
        ];
    }

    private static bool HasFamily(
        LibraryFamilyRoleTypeRow type,
        LibraryNameFamilyIdentity identity) =>
        identity.Kind switch
        {
            LibraryNameFamilyKind.OneWordSuffix =>
                Equals(type.OneWordSuffix, identity),
            LibraryNameFamilyKind.TwoWordSuffix =>
                Equals(type.TwoWordSuffix, identity),
            _ => false,
        };

    private static LibraryFamilyRoleRow CreateFamilyRow(
        LibraryNameFamilyIdentity identity,
        IEnumerable<MetadataTypeDefinitionAddress> types,
        IReadOnlyDictionary<
            MetadataTypeDefinitionAddress,
            LibraryFamilyRoleTypeRow> rows)
    {
        LibraryFamilyRoleTypeRow[] members =
        [
            .. types.Select(type => rows[type])
                .OrderBy(static row => row.Type.Definition.Value),
        ];
        return new(
            identity,
            members.Length,
            CountRole(members, LibraryStructuralTypeRole.Foundation),
            CountRole(members, LibraryStructuralTypeRole.Hub),
            CountRole(members, LibraryStructuralTypeRole.Orchestrator),
            CountPole(members, LibraryStructuralTypePole.SeaLevel),
            CountPole(members, LibraryStructuralTypePole.MountainPeak),
            members.Count(static row => row.StructuralRole is null),
            members.Select(static row => row.Namespace)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            StructuralDisposition(members),
            Addresses(members),
            Addresses(members.Where(static row =>
                row.StructuralRole
                    == LibraryStructuralTypeRole.Foundation)),
            Addresses(members.Where(static row =>
                row.StructuralRole
                    == LibraryStructuralTypeRole.Hub)),
            Addresses(members.Where(static row =>
                row.StructuralRole
                    == LibraryStructuralTypeRole.Orchestrator)),
            Addresses(members.Where(static row =>
                row.StructuralPole
                    == LibraryStructuralTypePole.SeaLevel)),
            Addresses(members.Where(static row =>
                row.StructuralPole
                    == LibraryStructuralTypePole.MountainPeak)),
            Addresses(members.Where(static row =>
                row.StructuralRole is null)));
    }

    private static LibraryFamilyRolePopulation CreatePopulation(
        LibraryNameFamilyPopulationKind kind,
        IReadOnlyCollection<MetadataTypeDefinitionAddress> types,
        LibraryNameFamilyProvenanceQualification provenance,
        ImmutableArray<LibraryFamilyRoleRow> families,
        IReadOnlyDictionary<
            MetadataTypeDefinitionAddress,
            LibraryFamilyRoleTypeRow> rows,
        LibraryStructuralEvidenceDisposition emptyDisposition)
    {
        LibraryFamilyRoleTypeRow[] members =
        [
            .. types.Select(type => rows[type]),
        ];
        return new(
            kind,
            members.Length,
            CountRole(members, LibraryStructuralTypeRole.Foundation),
            CountRole(members, LibraryStructuralTypeRole.Hub),
            CountRole(members, LibraryStructuralTypeRole.Orchestrator),
            CountPole(members, LibraryStructuralTypePole.SeaLevel),
            CountPole(members, LibraryStructuralTypePole.MountainPeak),
            members.Count(static row => row.StructuralRole is null),
            provenance,
            StructuralDisposition(members, emptyDisposition),
            families);
    }

    private static int CountRole(
        IEnumerable<LibraryFamilyRoleTypeRow> rows,
        LibraryStructuralTypeRole role) =>
        rows.Count(row => row.StructuralRole == role);

    private static int CountPole(
        IEnumerable<LibraryFamilyRoleTypeRow> rows,
        LibraryStructuralTypePole pole) =>
        rows.Count(row => row.StructuralPole == pole);

    private static LibraryStructuralEvidenceDisposition StructuralDisposition(
        IReadOnlyCollection<LibraryFamilyRoleTypeRow> rows,
        LibraryStructuralEvidenceDisposition emptyDisposition =
            LibraryStructuralEvidenceDisposition.Complete)
    {
        if (rows.Count == 0)
            return emptyDisposition;
        return rows.Any(static row =>
                row.StructuralDisposition
                    == LibraryStructuralEvidenceDisposition.Qualified)
                ? LibraryStructuralEvidenceDisposition.Qualified
                : LibraryStructuralEvidenceDisposition.Complete;
    }

    private static ImmutableArray<MetadataTypeDefinitionAddress> Addresses(
        IEnumerable<LibraryFamilyRoleTypeRow> rows) =>
    [
        .. rows.Select(static row => row.Type)
            .OrderBy(static type => type.Definition.Value),
    ];

    private static LibraryFamilyRoleStructuralReceipt CreateStructuralReceipt(
        LibraryStructuralSalienceDocument document) =>
        new(
            document.MethodologyVersion,
            document.EvidenceMode,
            document.NamespaceIndex.Disposition,
            document.NamespaceIndex.SignatureUse,
            [
                .. document.TypeLeverageShards.Select(static shard =>
                    new LibraryFamilyRoleStructuralShardReceipt(
                        shard.Namespace,
                        shard.RoleDisposition,
                        shard.SignatureUse,
                        shard.GraphWork,
                        shard.SeaLevel.Disposition,
                        shard.MountainPeak.Disposition)),
            ]);

    private static string FamilyDisplay(
        LibraryNameFamilyIdentity identity) =>
        string.Join(identity.Separator ?? string.Empty, identity.Words);

    private static LibraryFamilyRoleCompositionOutcome.Rejected Rejected(
        LibraryFamilyRoleCompositionRejection reason,
        string detail) =>
        new(reason, detail);

    private sealed record StructuralTypeBinding(
        LibraryStructuralTypeLeverageRow Row,
        LibraryStructuralEvidenceDisposition Disposition);
}
