using System.Collections.Immutable;

using ILInspector.Metadata;

namespace ILInspector.Research;

public enum LibraryArchitecturalFamilyCompositionRejection
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

public sealed record LibraryArchitecturalFamilyBinding(
    AssemblyArtifactIdentity Artifact,
    AssemblyReferenceIdentity Assembly,
    Guid ModuleVersionId);

public sealed record LibraryArchitecturalFamilyTypeRow(
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

public sealed record LibraryArchitecturalFamilyRow(
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

public static class LibraryArchitecturalFamilyOrder
{
    public static IComparer<LibraryArchitecturalFamilyRow> Prevalence { get; } =
        Comparer<LibraryArchitecturalFamilyRow>.Create(ComparePrevalence);

    private static int ComparePrevalence(
        LibraryArchitecturalFamilyRow? left,
        LibraryArchitecturalFamilyRow? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        int comparison = right.TypeCount.CompareTo(left.TypeCount);
        if (comparison != 0)
            return comparison;

        comparison = right.DistinctNamespaceCount.CompareTo(
            left.DistinctNamespaceCount);
        return comparison != 0
            ? comparison
            : LibraryNameFamilyOrder.CompareIdentity(
                left.Identity,
                right.Identity);
    }
}

public sealed record LibraryArchitecturalFamilyPopulation(
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
    ImmutableArray<LibraryArchitecturalFamilyRow> Families);

public sealed record LibraryArchitecturalFamilyStructuralShardReceipt(
    string Namespace,
    LibraryStructuralEvidenceDisposition RoleDisposition,
    LibraryStructuralSignatureUseQualification SignatureUse,
    LibraryStructuralTypeLeverageGraphWork GraphWork,
    LibraryStructuralEvidenceDisposition SeaLevelDisposition,
    LibraryStructuralEvidenceDisposition MountainPeakDisposition);

public sealed record LibraryArchitecturalFamilyStructuralReceipt(
    string MethodologyVersion,
    LibraryStructuralSalienceEvidenceMode EvidenceMode,
    LibraryStructuralEvidenceDisposition NamespaceDisposition,
    LibraryStructuralSignatureUseQualification SignatureUse,
    ImmutableArray<LibraryArchitecturalFamilyStructuralShardReceipt> Shards);

public sealed record LibraryArchitecturalFamilyPopulationReceipt(
    LibraryNameFamilyPopulationKind Kind,
    int TypeCount,
    int FoundationCount,
    int HubCount,
    int OrchestratorCount,
    int SeaLevelCount,
    int MountainPeakCount,
    int NoIssuedStructuralRoleCount);

public sealed record LibraryArchitecturalFamilyCompositionReceipt(
    int ExactTypeCount,
    int FoundationCount,
    int HubCount,
    int OrchestratorCount,
    int SeaLevelCount,
    int MountainPeakCount,
    int NoIssuedStructuralRoleCount,
    int OneWordFamilyRowCount,
    int TwoWordFamilyRowCount,
    ImmutableArray<LibraryArchitecturalFamilyPopulationReceipt> Populations,
    LibraryNameFamilyReceipt NameFamilies,
    LibraryArchitecturalFamilyStructuralReceipt StructuralSalience);

public sealed record LibraryArchitecturalFamilyCompositionDocument(
    LibraryArchitecturalFamilyBinding Binding,
    string MethodologyVersion,
    LibraryNameFamilyMethodology NameFamilyMethodology,
    LibraryNameFamilyProvenanceQualification Provenance,
    LibraryArchitecturalFamilyStructuralReceipt StructuralSalience,
    ImmutableArray<LibraryArchitecturalFamilyTypeRow> Types,
    ImmutableArray<LibraryArchitecturalFamilyPopulation> Populations,
    LibraryArchitecturalFamilyCompositionReceipt Receipt);

public abstract record LibraryArchitecturalFamilyCompositionOutcome
{
    private LibraryArchitecturalFamilyCompositionOutcome()
    {
    }

    public sealed record Available(LibraryArchitecturalFamilyCompositionDocument Document)
        : LibraryArchitecturalFamilyCompositionOutcome;

    public sealed record Rejected(
        LibraryArchitecturalFamilyCompositionRejection Reason,
        string Detail)
        : LibraryArchitecturalFamilyCompositionOutcome;
}

public static class LibraryArchitecturalFamilyComposition
{
    public const string MethodologyVersion =
        "architectural-families-composition.v1";

    public static LibraryArchitecturalFamilyCompositionOutcome Execute(
        LibraryArchitecturalFamilyBinding binding,
        LibraryNameFamilyDocument nameFamilies,
        LibraryStructuralSalienceDocument structuralSalience)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(nameFamilies);
        ArgumentNullException.ThrowIfNull(structuralSalience);

        if (!BindingMatches(binding, nameFamilies.Binding))
        {
            return Rejected(
                LibraryArchitecturalFamilyCompositionRejection
                    .NameFamilyBindingMismatch,
                "The name-family document does not share the exact "
                    + "composition artifact binding.");
        }

        if (!StructuralMethodologyMatches(structuralSalience))
        {
            return Rejected(
                LibraryArchitecturalFamilyCompositionRejection
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
                LibraryArchitecturalFamilyCompositionRejection
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
                    LibraryArchitecturalFamilyCompositionRejection
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
                    LibraryArchitecturalFamilyCompositionRejection.DuplicateNamespace,
                    $"Structural namespace '{row.Namespace}' is duplicated.");
            }
        }

        if (structuralSalience.TypeLeverageShards.Length
            != structuralSalience.NamespaceIndex.Rows.Length)
        {
            return Rejected(
                LibraryArchitecturalFamilyCompositionRejection
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
                LibraryArchitecturalFamilyCompositionRejection
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
                    LibraryArchitecturalFamilyCompositionRejection
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
                    LibraryArchitecturalFamilyCompositionRejection
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
                        LibraryArchitecturalFamilyCompositionRejection
                            .DuplicateStructuralType,
                        "Structural Type rows must contain unique exact "
                            + "addresses.");
                }
                if (!nameTypes.TryGetValue(row.Type, out var nameType))
                {
                    return Rejected(
                        LibraryArchitecturalFamilyCompositionRejection
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
                        LibraryArchitecturalFamilyCompositionRejection
                            .StructuralTypeNamespaceMismatch,
                        "A structural Type row does not belong to its exact "
                            + "namespace shard.");
                }
                if (!Equals(row.Name, nameType.Name))
                {
                    return Rejected(
                        LibraryArchitecturalFamilyCompositionRejection
                            .StructuredNameMismatch,
                        "Joined Type rows disagree on their structured "
                            + "metadata name.");
                }
            }

            LibraryArchitecturalFamilyCompositionOutcome.Rejected? orderFailure =
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
        LibraryArchitecturalFamilyTypeRow[] typeRows =
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
            ImmutableArray.CreateBuilder<LibraryArchitecturalFamilyPopulation>(
                nameFamilies.Populations.Length);
        foreach (LibraryNameFamilyPopulation population
            in nameFamilies.Populations.OrderBy(static population =>
                population.Kind))
        {
            if (!seenPopulations.Add(population.Kind))
            {
                return Rejected(
                    LibraryArchitecturalFamilyCompositionRejection
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
                    LibraryArchitecturalFamilyCompositionRejection
                        .PopulationTypeCountMismatch,
                    $"Population '{population.Kind}' does not close against "
                        + "its exact Type rows.");
            }

            var families =
                ImmutableArray.CreateBuilder<LibraryArchitecturalFamilyRow>(
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
                        LibraryArchitecturalFamilyCompositionRejection
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
                LibraryArchitecturalFamilyCompositionRejection
                    .PopulationTypeCountMismatch,
                "The name-family document does not contain its all-Types "
                    + "population.");
        }

        ImmutableArray<LibraryArchitecturalFamilyPopulation> populationRows =
            populations.MoveToImmutable();
        LibraryArchitecturalFamilyPopulation allTypes = populationRows.Single(
            static population =>
                population.Kind
                    == LibraryNameFamilyPopulationKind.AllTypes);
        LibraryArchitecturalFamilyStructuralReceipt structuralReceipt =
            CreateStructuralReceipt(structuralSalience);
        int oneWordFamilyCount = populationRows.Sum(static population =>
            population.Families.Count(static family =>
                family.Identity.Kind
                    == LibraryNameFamilyKind.OneWordSuffix));
        int twoWordFamilyCount = populationRows.Sum(static population =>
            population.Families.Count(static family =>
                family.Identity.Kind
                    == LibraryNameFamilyKind.TwoWordSuffix));
        var receipt = new LibraryArchitecturalFamilyCompositionReceipt(
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
                    new LibraryArchitecturalFamilyPopulationReceipt(
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

        return new LibraryArchitecturalFamilyCompositionOutcome.Available(
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
        LibraryArchitecturalFamilyBinding binding,
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
        LibraryArchitecturalFamilyBinding binding) =>
        receipt.ModuleVersionId == binding.ModuleVersionId
        && Equals(receipt.Assembly, binding.Assembly);

    private static LibraryArchitecturalFamilyCompositionOutcome.Rejected? ValidateOrder(
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
                    LibraryArchitecturalFamilyCompositionRejection
                        .StructuralOrderTypeNotFound,
                    $"Structural {pole} order references an unissued or "
                        + "duplicate Type row.");
            }
        }

        if (shard.Rows.Any(row =>
                row.Pole == pole && !ordered.Contains(row.Type)))
        {
            return Rejected(
                LibraryArchitecturalFamilyCompositionRejection
                    .StructuralOrderPoleMismatch,
                $"Structural {pole} order omits an owner-issued pole.");
        }

        return null;
    }

    private static LibraryArchitecturalFamilyTypeRow CreateTypeRow(
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
        IEnumerable<LibraryArchitecturalFamilyTypeRow> types)
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
        LibraryArchitecturalFamilyTypeRow type,
        LibraryNameFamilyIdentity identity) =>
        identity.Kind switch
        {
            LibraryNameFamilyKind.OneWordSuffix =>
                Equals(type.OneWordSuffix, identity),
            LibraryNameFamilyKind.TwoWordSuffix =>
                Equals(type.TwoWordSuffix, identity),
            _ => false,
        };

    private static LibraryArchitecturalFamilyRow CreateFamilyRow(
        LibraryNameFamilyIdentity identity,
        IEnumerable<MetadataTypeDefinitionAddress> types,
        IReadOnlyDictionary<
            MetadataTypeDefinitionAddress,
            LibraryArchitecturalFamilyTypeRow> rows)
    {
        LibraryArchitecturalFamilyTypeRow[] members =
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

    private static LibraryArchitecturalFamilyPopulation CreatePopulation(
        LibraryNameFamilyPopulationKind kind,
        IReadOnlyCollection<MetadataTypeDefinitionAddress> types,
        LibraryNameFamilyProvenanceQualification provenance,
        ImmutableArray<LibraryArchitecturalFamilyRow> families,
        IReadOnlyDictionary<
            MetadataTypeDefinitionAddress,
            LibraryArchitecturalFamilyTypeRow> rows,
        LibraryStructuralEvidenceDisposition emptyDisposition)
    {
        LibraryArchitecturalFamilyTypeRow[] members =
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
        IEnumerable<LibraryArchitecturalFamilyTypeRow> rows,
        LibraryStructuralTypeRole role) =>
        rows.Count(row => row.StructuralRole == role);

    private static int CountPole(
        IEnumerable<LibraryArchitecturalFamilyTypeRow> rows,
        LibraryStructuralTypePole pole) =>
        rows.Count(row => row.StructuralPole == pole);

    private static LibraryStructuralEvidenceDisposition StructuralDisposition(
        IReadOnlyCollection<LibraryArchitecturalFamilyTypeRow> rows,
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
        IEnumerable<LibraryArchitecturalFamilyTypeRow> rows) =>
    [
        .. rows.Select(static row => row.Type)
            .OrderBy(static type => type.Definition.Value),
    ];

    private static LibraryArchitecturalFamilyStructuralReceipt CreateStructuralReceipt(
        LibraryStructuralSalienceDocument document) =>
        new(
            document.MethodologyVersion,
            document.EvidenceMode,
            document.NamespaceIndex.Disposition,
            document.NamespaceIndex.SignatureUse,
            [
                .. document.TypeLeverageShards.Select(static shard =>
                    new LibraryArchitecturalFamilyStructuralShardReceipt(
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

    private static LibraryArchitecturalFamilyCompositionOutcome.Rejected Rejected(
        LibraryArchitecturalFamilyCompositionRejection reason,
        string detail) =>
        new(reason, detail);

    private sealed record StructuralTypeBinding(
        LibraryStructuralTypeLeverageRow Row,
        LibraryStructuralEvidenceDisposition Disposition);
}
