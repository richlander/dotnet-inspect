using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using Inspector.Artifacts;

namespace ILInspector.Research.Tests;

public sealed class LibraryFamilyRoleCompositionTests
{
    private static readonly (
        string Source,
        string Target)[] s_relationships =
    [
        ("GenericValidator`1", "CustomerValidator"),
        ("ValidatorOptions", "CustomerValidator"),
        ("ValidationContext", "CustomerValidator"),
        ("GenericValidator`1", "OrderValidator"),
        ("OrderValidator", "ValidatorOptions"),
        ("GenericValidator`1", "ValidationContext"),
    ];

    [Fact]
    public void CompositionJoinsExactTypesAndClosesFamilyPopulations()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryNameFamilyDocument names = NameFamilies(fixture);
        LibraryStructuralSalienceDocument structural =
            Structural(names, s_relationships);

        LibraryFamilyRoleCompositionDocument document =
            Available(Compose(names, structural));

        Assert.Equal(names.Types.Length, document.Types.Length);
        Assert.Equal(
            names.Types.Select(static type => type.Type),
            document.Types.Select(static type => type.Type));
        Assert.Equal(5, document.Populations.Length);
        Assert.All(
            document.Populations,
            static population => Assert.Equal(
                population.TypeCount,
                population.FoundationCount
                    + population.HubCount
                    + population.OrchestratorCount
                    + population.NoIssuedStructuralRoleCount));

        LibraryFamilyRoleTypeRow foundation =
            Type(document, "CustomerValidator");
        Assert.Equal(
            LibraryStructuralTypeRole.Foundation,
            foundation.StructuralRole);
        Assert.Equal(
            LibraryStructuralTypePole.SeaLevel,
            foundation.StructuralPole);

        LibraryFamilyRoleTypeRow orchestrator =
            Type(document, "GenericValidator`1");
        Assert.Equal(
            LibraryStructuralTypeRole.Orchestrator,
            orchestrator.StructuralRole);
        Assert.Equal(
            LibraryStructuralTypePole.MountainPeak,
            orchestrator.StructuralPole);

        Assert.Equal(
            LibraryStructuralTypeRole.Hub,
            Type(document, "OrderValidator").StructuralRole);
        LibraryFamilyRoleTypeRow unassigned =
            Type(document, "InventoryValidator");
        Assert.Null(unassigned.StructuralRole);
        Assert.Null(unassigned.SignatureIncomingDegree);
        Assert.Equal(
            LibraryStructuralEvidenceDisposition.Complete,
            unassigned.StructuralDisposition);

        LibraryFamilyRolePopulation all = Population(
            document,
            LibraryNameFamilyPopulationKind.AllTypes);
        LibraryFamilyRoleRow validators = Family(
            all,
            LibraryNameFamilyKind.OneWordSuffix,
            ["Validator"]);
        Assert.True(validators.FoundationCount >= 1);
        Assert.True(validators.HubCount >= 1);
        Assert.True(validators.OrchestratorCount >= 1);
        Assert.True(validators.NoIssuedStructuralRoleCount >= 1);
        Assert.Equal(
            validators.TypeCount,
            validators.FoundationCount
                + validators.HubCount
                + validators.OrchestratorCount
                + validators.NoIssuedStructuralRoleCount);
        Assert.Equal(
            validators.FoundationCount,
            validators.Foundations.Length);
        Assert.Equal(
            validators.HubCount,
            validators.Hubs.Length);
        Assert.Equal(
            validators.OrchestratorCount,
            validators.Orchestrators.Length);
        Assert.Equal(
            validators.SeaLevelCount,
            validators.SeaLevels.Length);
        Assert.Equal(
            validators.MountainPeakCount,
            validators.MountainPeaks.Length);
        Assert.Equal(
            validators.NoIssuedStructuralRoleCount,
            validators.NoIssuedStructuralRoles.Length);

        LibraryFamilyRoleTypeRow topLevel = Assert.Single(
            document.Types,
            static type =>
                type.Name.Segments.SequenceEqual(["Validator"]));
        LibraryFamilyRoleTypeRow nested = Assert.Single(
            document.Types,
            static type =>
                type.Name.Segments.SequenceEqual(["Outer", "Validator"]));
        Assert.NotEqual(topLevel.Type, nested.Type);
        Assert.Equal(topLevel.OneWordSuffix, nested.OneWordSuffix);

        Assert.Contains(
            all.Families,
            static family =>
                family.Identity.Kind
                    == LibraryNameFamilyKind.OneWordSuffix);
        Assert.Contains(
            all.Families,
            static family =>
                family.Identity.Kind
                    == LibraryNameFamilyKind.TwoWordSuffix);
        Assert.Equal(names.Receipt, document.Receipt.NameFamilies);
        Assert.Same(
            structural.TypeLeverageShards[0].GraphWork,
            document.StructuralSalience.Shards[0].GraphWork);
    }

    [Fact]
    public void CompositionPreservesQualifiedStructuralEvidence()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryNameFamilyDocument names = NameFamilies(fixture);
        LibraryStructuralSalienceDocument structural = Structural(
            names,
            s_relationships,
            MetadataLibrarySignatureUseDisposition.Partial);

        LibraryFamilyRoleCompositionDocument document =
            Available(Compose(names, structural));

        Assert.Equal(
            LibraryStructuralEvidenceDisposition.Qualified,
            document.StructuralSalience.NamespaceDisposition);
        Assert.All(
            document.StructuralSalience.Shards,
            static shard => Assert.Equal(
                LibraryStructuralEvidenceDisposition.Qualified,
                shard.RoleDisposition));
        Assert.All(
            document.Types,
            static type => Assert.Equal(
                LibraryStructuralEvidenceDisposition.Qualified,
                type.StructuralDisposition));
        Assert.All(
            document.Populations,
            static population => Assert.Equal(
                LibraryStructuralEvidenceDisposition.Qualified,
                population.StructuralDisposition));
        Assert.All(
            document.Populations.SelectMany(static population =>
                population.Families),
            static family => Assert.Equal(
                LibraryStructuralEvidenceDisposition.Qualified,
                family.StructuralDisposition));
        Assert.Equal(
            LibraryNameFamilyProvenanceState.Available,
            document.Provenance.State);
    }

    [Fact]
    public void CompositionRejectsBrokenExactCorrespondence()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryNameFamilyDocument names = NameFamilies(fixture);
        LibraryStructuralSalienceDocument structural =
            Structural(names, s_relationships);

        AssertRejected(
            names with
            {
                Types = [.. names.Types, names.Types[0]],
            },
            structural,
            LibraryFamilyRoleCompositionRejection.DuplicateNameFamilyType);

        LibraryStructuralNamespaceLeverageRow firstNamespace =
            structural.NamespaceIndex.Rows[0];
        AssertRejected(
            names,
            structural with
            {
                NamespaceIndex = structural.NamespaceIndex with
                {
                    Rows =
                    [
                        firstNamespace with
                        {
                            TypeCount = firstNamespace.TypeCount + 1,
                        },
                        .. structural.NamespaceIndex.Rows[1..],
                    ],
                },
            },
            LibraryFamilyRoleCompositionRejection
                .NamespaceTypeCountMismatch);

        AssertRejected(
            names,
            structural with
            {
                TypeLeverageShards =
                [
                    .. structural.TypeLeverageShards.Reverse(),
                ],
            },
            LibraryFamilyRoleCompositionRejection
                .NamespaceShardOrderMismatch);

        LibraryStructuralTypeLeverageShard populated =
            structural.TypeLeverageShards.First(static shard =>
                shard.Rows.Length >= 2);
        LibraryStructuralTypeLeverageRow first = populated.Rows[0];
        LibraryStructuralTypeLeverageRow second = populated.Rows[1];
        AssertRejected(
            names,
            ReplaceShard(
                structural,
                populated,
                populated with
                {
                    Rows =
                    [
                        first with { Name = second.Name },
                        .. populated.Rows[1..],
                    ],
                }),
            LibraryFamilyRoleCompositionRejection.StructuredNameMismatch);

        AssertRejected(
            names,
            ReplaceShard(
                structural,
                populated,
                populated with
                {
                    Rows = [.. populated.Rows, first],
                }),
            LibraryFamilyRoleCompositionRejection
                .DuplicateStructuralType);

        MetadataTypeDefinitionAddress unknown =
            MetadataTypeDefinitionAddress.FromToken(
                names.Binding.ModuleVersionId,
                0x02FFFFFE);
        AssertRejected(
            names,
            ReplaceShard(
                structural,
                populated,
                populated with
                {
                    Rows =
                    [
                        .. populated.Rows,
                        first with { Type = unknown },
                    ],
                }),
            LibraryFamilyRoleCompositionRejection.StructuralTypeNotFound);

        LibraryStructuralTypeLeverageRow seaLevel =
            populated.Rows.First(static row =>
                row.Pole == LibraryStructuralTypePole.SeaLevel);
        AssertRejected(
            names,
            ReplaceShard(
                structural,
                populated,
                populated with
                {
                    SeaLevel = populated.SeaLevel with
                    {
                        Types =
                        [
                            .. populated.SeaLevel.Types.Where(type =>
                                type != seaLevel.Type),
                        ],
                    },
                }),
            LibraryFamilyRoleCompositionRejection
                .StructuralOrderPoleMismatch);
    }

    [Fact]
    public void CompositionOutputIsIndependentOfUnorderedInputEnumeration()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryNameFamilyDocument names = NameFamilies(fixture);
        LibraryStructuralSalienceDocument structural =
            Structural(names, s_relationships);
        LibraryFamilyRoleCompositionDocument expected =
            Available(Compose(names, structural));

        LibraryNameFamilyDocument reversedNames = names with
        {
            Types = [.. names.Types.Reverse()],
            Populations =
            [
                .. names.Populations.Reverse().Select(population =>
                    population with
                    {
                        Families = [.. population.Families.Reverse()],
                    }),
            ],
        };
        LibraryStructuralSalienceDocument reversedStructural =
            structural with
            {
                TypeLeverageShards =
                [
                    .. structural.TypeLeverageShards.Select(shard =>
                        shard with { Rows = [.. shard.Rows.Reverse()] }),
                ],
            };

        LibraryFamilyRoleCompositionDocument actual =
            Available(Compose(reversedNames, reversedStructural));

        Assert.Equal(Projection(expected), Projection(actual));
    }

    private static LibraryFamilyRoleCompositionOutcome Compose(
        LibraryNameFamilyDocument names,
        LibraryStructuralSalienceDocument structural) =>
        LibraryFamilyRoleComposition.Execute(
            new(
                names.Binding.Artifact,
                names.Binding.Assembly,
                names.Binding.ModuleVersionId),
            names,
            structural);

    private static void AssertRejected(
        LibraryNameFamilyDocument names,
        LibraryStructuralSalienceDocument structural,
        LibraryFamilyRoleCompositionRejection reason)
    {
        var rejected =
            Assert.IsType<LibraryFamilyRoleCompositionOutcome.Rejected>(
                Compose(names, structural));
        Assert.Equal(reason, rejected.Reason);
        Assert.False(string.IsNullOrWhiteSpace(rejected.Detail));
    }

    private static LibraryFamilyRoleCompositionDocument Available(
        LibraryFamilyRoleCompositionOutcome outcome)
    {
        if (outcome is LibraryFamilyRoleCompositionOutcome.Rejected rejected)
        {
            Assert.Fail($"{rejected.Reason}: {rejected.Detail}");
        }
        return Assert.IsType<
            LibraryFamilyRoleCompositionOutcome.Available>(outcome).Document;
    }

    private static LibraryStructuralSalienceDocument ReplaceShard(
        LibraryStructuralSalienceDocument document,
        LibraryStructuralTypeLeverageShard current,
        LibraryStructuralTypeLeverageShard replacement) =>
        document with
        {
            TypeLeverageShards =
            [
                .. document.TypeLeverageShards.Select(shard =>
                    ReferenceEquals(shard, current)
                        ? replacement
                        : shard),
            ],
        };

    private static LibraryStructuralSalienceDocument Structural(
        LibraryNameFamilyDocument names,
        IReadOnlyList<(string Source, string Target)> relationships,
        MetadataLibrarySignatureUseDisposition disposition =
            MetadataLibrarySignatureUseDisposition.Complete)
    {
        var typesBySimpleName = names.Types
            .GroupBy(static type => type.MetadataSimpleName)
            .Where(static group => group.Count() == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.Single(),
                StringComparer.Ordinal);
        (MetadataTypeDefinitionAddress Source,
            MetadataTypeDefinitionAddress Target)[] edges =
        [
            .. relationships.Select(relationship => (
                typesBySimpleName[relationship.Source].Type,
                typesBySimpleName[relationship.Target].Type)),
        ];

        MetadataLibrarySignatureUseResult whole = Evidence(
            names,
            names.Types,
            edges,
            exactNamespace: null,
            disposition);
        LibraryStructuralNamespaceLeverageIndex index =
            LibraryStructuralReport.CreateNamespaceLeverageIndex(whole);
        LibraryStructuralTypeLeverageShard[] shards =
        [
            .. index.Rows.Select(row =>
            {
                LibraryNameFamilyTypeRow[] namespaceTypes =
                [
                    .. names.Types.Where(type =>
                        StringComparer.Ordinal.Equals(
                            type.Name.Namespace,
                            row.Namespace)),
                ];
                var addresses = namespaceTypes
                    .Select(static type => type.Type)
                    .ToHashSet();
                return LibraryStructuralReport.CreateTypeLeverageShard(
                    Evidence(
                        names,
                        namespaceTypes,
                        edges.Where(edge =>
                            addresses.Contains(edge.Source)
                            && addresses.Contains(edge.Target)),
                        row.Namespace,
                        disposition));
            }),
        ];
        return LibraryStructuralReport.CreateStructuralSalience(index, shards);
    }

    private static MetadataLibrarySignatureUseResult Evidence(
        LibraryNameFamilyDocument names,
        IEnumerable<LibraryNameFamilyTypeRow> sourceTypes,
        IEnumerable<(
            MetadataTypeDefinitionAddress Source,
            MetadataTypeDefinitionAddress Target)> sourceEdges,
        string? exactNamespace,
        MetadataLibrarySignatureUseDisposition disposition)
    {
        MetadataLibrarySignatureType[] types =
        [
            .. sourceTypes.Select(static type =>
                new MetadataLibrarySignatureType(
                    type.Type,
                    type.Name,
                    type.DefinitionKind,
                    MetadataLibraryTypeClassification.None)),
        ];
        var typesByAddress = types.ToDictionary(static type => type.Type);
        MetadataLibrarySignatureUseOccurrence[] occurrences =
        [
            .. sourceEdges.Select((edge, ordinal) =>
                new MetadataLibrarySignatureUseOccurrence(
                    edge.Source,
                    typesByAddress[edge.Source].Name,
                    edge.Target,
                    typesByAddress[edge.Target].Name,
                    MetadataLibrarySignatureUseSiteKind.FieldType,
                    0x04000001 + ordinal,
                    ordinal)),
        ];
        return new(
            new(
                names.Binding.ModuleVersionId,
                names.Binding.Assembly,
                exactNamespace,
                new MetadataOperationCounters(0)),
            disposition,
            [.. types],
            [.. occurrences],
            new(
                occurrences.Length,
                occurrences.Length,
                unavailable: 0,
                limited: 0),
            []);
    }

    private static LibraryNameFamilyDocument NameFamilies(
        FixtureExecution fixture) =>
        Assert.IsType<LibraryNameFamilySummaryOutcome.Available>(
            LibraryNameFamilySummary.Execute(
                fixture.Assembly,
                fixture.Session,
                provenance: fixture.Provenance)).Document;

    private static LibraryFamilyRoleTypeRow Type(
        LibraryFamilyRoleCompositionDocument document,
        string metadataSimpleName) =>
        Assert.Single(
            document.Types,
            type =>
                type.Name.Segments[^1] == metadataSimpleName);

    private static LibraryFamilyRolePopulation Population(
        LibraryFamilyRoleCompositionDocument document,
        LibraryNameFamilyPopulationKind kind) =>
        Assert.Single(
            document.Populations,
            population => population.Kind == kind);

    private static LibraryFamilyRoleRow Family(
        LibraryFamilyRolePopulation population,
        LibraryNameFamilyKind kind,
        string[] words) =>
        Assert.Single(
            population.Families,
            family =>
                family.Identity.Kind == kind
                && family.Identity.Words.SequenceEqual(words));

    private static string Projection(
        LibraryFamilyRoleCompositionDocument document) =>
        string.Join(
            "\n",
            document.Populations.SelectMany(population =>
                population.Families.Select(family =>
                    $"{population.Kind}:{family.Identity.Kind}:"
                    + $"{string.Join("|", family.Identity.Words)}:"
                    + $"{family.Identity.Separator}:{family.TypeCount}:"
                    + $"{family.FoundationCount}:{family.HubCount}:"
                    + $"{family.OrchestratorCount}:"
                    + $"{family.NoIssuedStructuralRoleCount}:"
                    + string.Join(
                        ",",
                        family.Types.Select(static type =>
                            type.Definition.Value)))));

    private static FixtureExecution OpenFixture()
    {
        byte[] image = File.ReadAllBytes(
            FixtureCatalog.ResearchNameFamilies.AssemblyPath());
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
               authority.BeginContribution(admission))
        {
            contribution = scope.Register(
                TestArtifactProvenance.Instance,
                _ => new MemoryStream(image, writable: false));
        }
        authority.CreateRetainedContent(
            contribution.Registration,
            _ => new MemoryStream(image, writable: false));
        authority.CompleteAdmission(admission);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                contribution.Registration,
                () => new MemoryStream(image, writable: false),
                AssemblyResolutionProvenance.Local(
                    "family-role-composition-fixture"))
            ?? throw new InvalidOperationException(
                "The fixture must contain managed metadata.");
        PdbContext context = PdbContext.OpenEmbeddedPdbOnly(assembly);
        PdbSourceProvenanceOutcome provenance =
            context.InspectSourceProvenance();
        AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(context);
        return new(authority, assembly, context, session, provenance);
    }

    private sealed record FixtureExecution(
        ArtifactGenerationAuthority Authority,
        ResolvedAssemblyReference Assembly,
        PdbContext Context,
        AssemblyInspectionSession Session,
        PdbSourceProvenanceOutcome Provenance) : IDisposable
    {
        public void Dispose()
        {
            Session.Dispose();
            Context.Dispose();
        }
    }

    private sealed class TestArtifactProvenance : IArtifactProvenance
    {
        public static TestArtifactProvenance Instance { get; } = new();
    }
}
