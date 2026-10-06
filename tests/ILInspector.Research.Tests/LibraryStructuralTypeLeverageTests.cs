using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.Research;

namespace ILInspector.Research.Tests;

public sealed class LibraryStructuralTypeLeverageTests
{
    private static readonly Guid s_moduleVersionId =
        new("50f29137-4d23-4d47-a1e0-e34a358ce471");
    private static readonly AssemblyReferenceIdentity s_assembly =
        new(
            "Fake",
            new Version(1, 0, 0, 0),
            null,
            null);
    private static readonly LibraryImplementationProfileAnalysisResult
        s_analysis = CreateAnalysis();

    [Fact]
    public void NamespaceIndexCountsDistinctExternalSourceTypes()
    {
        MetadataLibrarySignatureUseResult whole = Evidence(
            [
                ("A", "Source1", MetadataLibraryTypeClassification.None),
                ("A", "Source2", MetadataLibraryTypeClassification.None),
                ("B", "Target1", MetadataLibraryTypeClassification.None),
                ("B", "Target2", MetadataLibraryTypeClassification.None),
                ("C", "Target3", MetadataLibraryTypeClassification.None),
            ],
            [
                (0, 2),
                (0, 2),
                (0, 3),
                (1, 2),
                (2, 4),
                (3, 2),
            ],
            exactNamespace: null);

        LibraryStructuralNamespaceLeverageIndex index =
            LibraryStructuralReport.CreateNamespaceLeverageIndex(whole);

        Assert.Equal(
            ["B", "C", "A"],
            index.Rows.Select(static row => row.Namespace));
        Assert.Equal(
            [2, 1, 0],
            index.Rows.Select(
                static row =>
                    row.ExternalIncomingSourceTypeCount));
        Assert.True(index.Rows[0].TopLeverage);
        Assert.False(index.Rows[1].TopLeverage);
        Assert.False(index.Rows[2].TopLeverage);
        Assert.Equal(2, index.Rows[0].TypeCount);
        Assert.Equal(
            LibraryStructuralEvidenceDisposition.Complete,
            index.Disposition);
        Assert.Equal(
            LibraryStructuralSalience.CurrentMethodologyVersion,
            index.MethodologyVersion);
    }

    [Fact]
    public void TypeShardUsesInducedDirectedDistinctPeersAndExactMaxima()
    {
        LibraryStructuralTypeLeverageShard shard = Shard(
            "B",
            [
                ("Sea", MetadataLibraryTypeClassification.None),
                ("Peak", MetadataLibraryTypeClassification.None),
                ("Peer1", MetadataLibraryTypeClassification.None),
                ("Peer2", MetadataLibraryTypeClassification.None),
                ("Peer3", MetadataLibraryTypeClassification.None),
                ("Excluded", MetadataLibraryTypeClassification.Enum),
            ],
            [
                (1, 0),
                (2, 0),
                (3, 0),
                (1, 0),
                (1, 2),
                (1, 3),
                (1, 4),
                (5, 0),
                (5, 2),
                (5, 3),
                (5, 4),
                (5, 5),
            ]);

        LibraryStructuralTypeLeverageRow sea = Row(shard, 0);
        Assert.Equal(4, sea.SignatureIncomingDegree);
        Assert.Equal(0, sea.SignatureOutgoingDegree);
        Assert.Equal(LibraryStructuralTypeRole.Foundation, sea.Role);
        Assert.Equal(LibraryStructuralTypePole.SeaLevel, sea.Pole);

        LibraryStructuralTypeLeverageRow peak = Row(shard, 1);
        Assert.Equal(0, peak.SignatureIncomingDegree);
        Assert.Equal(4, peak.SignatureOutgoingDegree);
        Assert.Equal(
            LibraryStructuralTypeRole.Orchestrator,
            peak.Role);
        Assert.Equal(
            LibraryStructuralTypePole.MountainPeak,
            peak.Pole);

        LibraryStructuralTypeLeverageRow excluded = Row(shard, 5);
        Assert.False(excluded.DesignationEligible);
        Assert.Null(excluded.Pole);
        Assert.DoesNotContain(excluded.Type, shard.SeaLevel.Types);
        Assert.DoesNotContain(
            excluded.Type,
            shard.MountainPeak.Types);
        Assert.Same(
            shard.GraphWork.SignatureIncomingDegree.SourceDocument,
            shard.GraphWork.SignatureOutgoingDegree.SourceDocument);
        Assert.Equal(
            11,
            shard.GraphWork.SignatureIncomingDegree
                .CanonicalEdgesExamined);
        Assert.Equal(
            10,
            shard.GraphWork.SignatureIncomingDegree
                .SelectedEdgesIndexed);
    }

    [Fact]
    public void TypeShardRequiresThreePeersAndPreservesMaximumTies()
    {
        LibraryStructuralTypeLeverageShard weak = Shard(
            "Weak",
            [
                ("A", MetadataLibraryTypeClassification.None),
                ("B", MetadataLibraryTypeClassification.None),
                ("C", MetadataLibraryTypeClassification.None),
            ],
            [(0, 1), (0, 2)]);
        Assert.All(weak.Rows, static row => Assert.Null(row.Pole));

        LibraryStructuralTypeLeverageShard tied = Shard(
            "Tied",
            [
                ("Sea1", MetadataLibraryTypeClassification.None),
                ("Sea2", MetadataLibraryTypeClassification.None),
                ("Peer1", MetadataLibraryTypeClassification.None),
                ("Peer2", MetadataLibraryTypeClassification.None),
                ("Peer3", MetadataLibraryTypeClassification.None),
            ],
            [
                (2, 0),
                (3, 0),
                (4, 0),
                (2, 1),
                (3, 1),
                (4, 1),
            ]);
        Assert.Equal(
            2,
            tied.Rows.Count(static row =>
                row.Pole == LibraryStructuralTypePole.SeaLevel));
        Assert.All(
            tied.Rows.Where(static row =>
                row.Pole == LibraryStructuralTypePole.SeaLevel),
            static row => Assert.Equal(
                LibraryStructuralSalience.MinimumDesignationDegree,
                row.SignatureIncomingDegree));
    }

    [Fact]
    public void TypeShardUsesTenPercentCohortOnlyAtDoubleDigitMaximum()
    {
        (string Name, MetadataLibraryTypeClassification Classification)[]
            cohortTypes =
            [
                ("Maximum", MetadataLibraryTypeClassification.None),
                ("NinetyPercent", MetadataLibraryTypeClassification.None),
                ("BelowNinetyPercent", MetadataLibraryTypeClassification.None),
                .. Enumerable.Range(1, 10).Select(index =>
                    ($"Peer{index}",
                        MetadataLibraryTypeClassification.None)),
            ];
        (int Source, int Target)[] cohortRelationships =
        [
            .. Enumerable.Range(3, 10).Select(source => (source, 0)),
            .. Enumerable.Range(3, 9).Select(source => (source, 1)),
            .. Enumerable.Range(3, 8).Select(source => (source, 2)),
        ];
        LibraryStructuralTypeLeverageShard cohort = Shard(
            "Cohort",
            cohortTypes,
            cohortRelationships);

        Assert.Equal(
            LibraryStructuralTypePole.SeaLevel,
            Row(cohort, 0).Pole);
        Assert.Equal(
            LibraryStructuralTypePole.SeaLevel,
            Row(cohort, 1).Pole);
        Assert.Null(Row(cohort, 2).Pole);

        (string Name, MetadataLibraryTypeClassification Classification)[]
            exactTypes =
            [
                ("Maximum", MetadataLibraryTypeClassification.None),
                ("BelowMaximum", MetadataLibraryTypeClassification.None),
                .. Enumerable.Range(1, 9).Select(index =>
                    ($"Peer{index}",
                        MetadataLibraryTypeClassification.None)),
            ];
        (int Source, int Target)[] exactRelationships =
        [
            .. Enumerable.Range(2, 9).Select(source => (source, 0)),
            .. Enumerable.Range(2, 8).Select(source => (source, 1)),
        ];
        LibraryStructuralTypeLeverageShard exact = Shard(
            "Exact",
            exactTypes,
            exactRelationships);

        Assert.Equal(
            LibraryStructuralTypePole.SeaLevel,
            Row(exact, 0).Pole);
        Assert.Null(Row(exact, 1).Pole);
    }

    [Fact]
    public void TypeShardIssuesOneDominantPoleOrNoneForExactTie()
    {
        LibraryStructuralTypeLeverageShard seaDominant = Shard(
            "SeaDominant",
            [
                ("Target", MetadataLibraryTypeClassification.None),
                ("Peer1", MetadataLibraryTypeClassification.None),
                ("Peer2", MetadataLibraryTypeClassification.None),
                ("Peer3", MetadataLibraryTypeClassification.None),
                ("Peer4", MetadataLibraryTypeClassification.None),
            ],
            [
                (1, 0),
                (2, 0),
                (3, 0),
                (4, 0),
                (0, 1),
                (0, 2),
                (0, 3),
            ]);
        Assert.Equal(
            LibraryStructuralTypePole.SeaLevel,
            Row(seaDominant, 0).Pole);

        LibraryStructuralTypeLeverageShard peakDominant = Shard(
            "PeakDominant",
            [
                ("Target", MetadataLibraryTypeClassification.None),
                ("Peer1", MetadataLibraryTypeClassification.None),
                ("Peer2", MetadataLibraryTypeClassification.None),
                ("Peer3", MetadataLibraryTypeClassification.None),
                ("Peer4", MetadataLibraryTypeClassification.None),
            ],
            [
                (1, 0),
                (2, 0),
                (3, 0),
                (0, 1),
                (0, 2),
                (0, 3),
                (0, 4),
            ]);
        Assert.Equal(
            LibraryStructuralTypePole.MountainPeak,
            Row(peakDominant, 0).Pole);

        LibraryStructuralTypeLeverageShard exactTie = Shard(
            "ExactTie",
            [
                ("Target", MetadataLibraryTypeClassification.None),
                ("Peer1", MetadataLibraryTypeClassification.None),
                ("Peer2", MetadataLibraryTypeClassification.None),
                ("Peer3", MetadataLibraryTypeClassification.None),
            ],
            [
                (1, 0),
                (2, 0),
                (3, 0),
                (0, 1),
                (0, 2),
                (0, 3),
            ]);
        LibraryStructuralTypeLeverageRow tied = Row(exactTie, 0);
        Assert.Equal(3, tied.SignatureIncomingDegree);
        Assert.Equal(3, tied.SignatureOutgoingDegree);
        Assert.Null(tied.Pole);
        Assert.Contains(tied.Type, exactTie.SeaLevel.Types);
        Assert.Contains(tied.Type, exactTie.MountainPeak.Types);
    }

    [Theory]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Complete,
        LibraryStructuralEvidenceDisposition.Complete)]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Partial,
        LibraryStructuralEvidenceDisposition.Qualified)]
    public void TypeShardQualifiesEveryViewFromNamespaceEvidence(
        MetadataLibrarySignatureUseDisposition signatureDisposition,
        LibraryStructuralEvidenceDisposition expectedDisposition)
    {
        LibraryStructuralTypeLeverageShard shard = Shard(
            "N",
            [
                ("A", MetadataLibraryTypeClassification.None),
                ("B", MetadataLibraryTypeClassification.None),
            ],
            [(0, 1)],
            signatureDisposition);

        Assert.Equal(expectedDisposition, shard.SeaLevel.Disposition);
        Assert.Equal(
            expectedDisposition,
            shard.MountainPeak.Disposition);
        Assert.Equal(expectedDisposition, shard.RoleDisposition);
        Assert.Equal(
            signatureDisposition,
            shard.SignatureUse.Disposition);
    }

    [Fact]
    public void BodyTypeShardUsesTypedOperandsDistinctPeersAndMetadataEligibility()
    {
        LibraryStructuralBodyTypeLeverageShard shard = BodyShard(
            "B",
            [
                ("Sea", MetadataLibraryTypeClassification.None),
                ("Peak", MetadataLibraryTypeClassification.None),
                ("Peer1", MetadataLibraryTypeClassification.None),
                ("Peer2", MetadataLibraryTypeClassification.None),
                ("Peer3", MetadataLibraryTypeClassification.None),
                ("Excluded", MetadataLibraryTypeClassification.Enum),
            ],
            [
                (1, 0, AnalysisLibraryBodyUseOperandKind.Call),
                (2, 0, AnalysisLibraryBodyUseOperandKind.Field),
                (3, 0, AnalysisLibraryBodyUseOperandKind.Type),
                (4, 0, AnalysisLibraryBodyUseOperandKind.MethodToken),
                (2, 0, AnalysisLibraryBodyUseOperandKind.Constructor),
                (0, 0, AnalysisLibraryBodyUseOperandKind.TypeToken),
                (1, 2, AnalysisLibraryBodyUseOperandKind.Array),
                (1, 3, AnalysisLibraryBodyUseOperandKind.Box),
                (1, 4, AnalysisLibraryBodyUseOperandKind.Constrained),
                (5, 0, AnalysisLibraryBodyUseOperandKind.FieldToken),
                (5, 2, AnalysisLibraryBodyUseOperandKind.Cast),
                (5, 3, AnalysisLibraryBodyUseOperandKind.TypeTest),
                (5, 4, AnalysisLibraryBodyUseOperandKind.Unbox),
                (5, 5, AnalysisLibraryBodyUseOperandKind.MethodReference),
            ]);

        LibraryStructuralBodyTypeLeverageRow sea = BodyRow(shard, 0);
        Assert.Equal(5, sea.BodyIncomingDegree);
        Assert.Equal(0, sea.BodyOutgoingDegree);
        Assert.Equal(LibraryStructuralTypeRole.Foundation, sea.Role);
        Assert.Equal(LibraryStructuralTypePole.SeaLevel, sea.Pole);

        LibraryStructuralBodyTypeLeverageRow peak = BodyRow(shard, 1);
        Assert.Equal(0, peak.BodyIncomingDegree);
        Assert.Equal(4, peak.BodyOutgoingDegree);
        Assert.Equal(
            LibraryStructuralTypeRole.Orchestrator,
            peak.Role);
        Assert.Equal(
            LibraryStructuralTypePole.MountainPeak,
            peak.Pole);

        LibraryStructuralBodyTypeLeverageRow excluded =
            BodyRow(shard, 5);
        Assert.False(excluded.DesignationEligible);
        Assert.Null(excluded.Pole);
        Assert.DoesNotContain(excluded.Type, shard.SeaLevel.Types);
        Assert.DoesNotContain(
            excluded.Type,
            shard.MountainPeak.Types);
        Assert.Same(
            shard.GraphWork.BodyIncomingDegree.SourceDocument,
            shard.GraphWork.BodyOutgoingDegree.SourceDocument);
        Assert.Equal(
            LibraryStructuralSalienceEvidenceMode.BodyUse,
            shard.EvidenceMode);
    }

    [Fact]
    public void BodyTypeShardUsesExactNamespaceInducedRelationships()
    {
        MetadataLibrarySignatureUseResult inventory = Evidence(
            [
                ("N", "A", MetadataLibraryTypeClassification.None),
                ("N", "B", MetadataLibraryTypeClassification.None),
            ],
            [],
            exactNamespace: "N");
        AnalysisLibraryBodyUseType[] bodyTypes =
        [
            .. inventory.Types.Select(static type =>
                new AnalysisLibraryBodyUseType(
                    type.Type,
                    type.Name,
                    type.DefinitionKind)),
            new(
                Address(2),
                Name("Other", "External"),
                AssemblyTypeDefinitionKind.Class),
        ];
        AnalysisLibraryBodyUseResult bodyUse = BodyEvidence(
            bodyTypes,
            [
                (0, 1, AnalysisLibraryBodyUseOperandKind.Call),
                (0, 2, AnalysisLibraryBodyUseOperandKind.Field),
                (2, 0, AnalysisLibraryBodyUseOperandKind.Type),
            ]);

        LibraryStructuralBodyTypeLeverageShard shard =
            LibraryStructuralReport.CreateBodyTypeLeverageShard(
                inventory,
                bodyUse);

        LibraryStructuralBodyTypeLeverageRow a = BodyRow(shard, 0);
        LibraryStructuralBodyTypeLeverageRow b = BodyRow(shard, 1);
        Assert.Equal(0, a.BodyIncomingDegree);
        Assert.Equal(1, a.BodyOutgoingDegree);
        Assert.Equal(1, b.BodyIncomingDegree);
        Assert.Equal(0, b.BodyOutgoingDegree);
    }

    [Fact]
    public void BodyTypeShardsPartitionWholeLibraryRelationshipsOnce()
    {
        MetadataLibrarySignatureUseResult whole = Evidence(
            [
                ("A", "Source", MetadataLibraryTypeClassification.None),
                ("A", "Target", MetadataLibraryTypeClassification.None),
                ("B", "Source", MetadataLibraryTypeClassification.None),
                ("B", "Target", MetadataLibraryTypeClassification.None),
            ],
            [],
            exactNamespace: null);
        MetadataLibrarySignatureUseResult a = whole with
        {
            Receipt = whole.Receipt with { ExactNamespace = "A" },
            Types =
            [
                .. whole.Types.Where(
                    static type => type.Name.Namespace == "A"),
            ],
        };
        MetadataLibrarySignatureUseResult b = whole with
        {
            Receipt = whole.Receipt with { ExactNamespace = "B" },
            Types =
            [
                .. whole.Types.Where(
                    static type => type.Name.Namespace == "B"),
            ],
        };
        AnalysisLibraryBodyUseResult bodyUse = BodyEvidence(
            whole.Types.Select(static type =>
                new AnalysisLibraryBodyUseType(
                    type.Type,
                    type.Name,
                    type.DefinitionKind)),
            [
                (0, 1, AnalysisLibraryBodyUseOperandKind.Call),
                (2, 3, AnalysisLibraryBodyUseOperandKind.Field),
                (0, 2, AnalysisLibraryBodyUseOperandKind.Type),
                (3, 1, AnalysisLibraryBodyUseOperandKind.Type),
            ]);

        ImmutableArray<LibraryStructuralBodyTypeLeverageShard> shards =
            LibraryStructuralReport.CreateBodyTypeLeverageShards(
                [b, a],
                bodyUse);

        Assert.Equal(
            ["B", "A"],
            shards.Select(static shard => shard.Namespace));
        Assert.All(
            shards,
            shard =>
            {
                Assert.Equal(2, shard.Rows.Length);
                Assert.Equal(
                    [0, 1],
                    shard.Rows.Select(
                        static row => row.BodyIncomingDegree));
                Assert.Equal(
                    [1, 0],
                    shard.Rows.Select(
                        static row => row.BodyOutgoingDegree));
            });
    }

    [Theory]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Complete,
        AnalysisLibraryBodyUseDisposition.Complete,
        LibraryStructuralEvidenceDisposition.Complete)]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Partial,
        AnalysisLibraryBodyUseDisposition.Complete,
        LibraryStructuralEvidenceDisposition.Qualified)]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Complete,
        AnalysisLibraryBodyUseDisposition.Qualified,
        LibraryStructuralEvidenceDisposition.Qualified)]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Complete,
        AnalysisLibraryBodyUseDisposition.Partial,
        LibraryStructuralEvidenceDisposition.Qualified)]
    public void BodyTypeShardQualifiesEveryViewFromBothEvidenceSources(
        MetadataLibrarySignatureUseDisposition inventoryDisposition,
        AnalysisLibraryBodyUseDisposition bodyDisposition,
        LibraryStructuralEvidenceDisposition expectedDisposition)
    {
        LibraryStructuralBodyTypeLeverageShard shard = BodyShard(
            "N",
            [
                ("A", MetadataLibraryTypeClassification.None),
                ("B", MetadataLibraryTypeClassification.None),
            ],
            [(0, 1, AnalysisLibraryBodyUseOperandKind.Call)],
            inventoryDisposition,
            bodyDisposition);

        Assert.Equal(expectedDisposition, shard.SeaLevel.Disposition);
        Assert.Equal(
            expectedDisposition,
            shard.MountainPeak.Disposition);
        Assert.Equal(expectedDisposition, shard.RoleDisposition);
        Assert.Equal(
            inventoryDisposition,
            shard.TypeInventory.Disposition);
        Assert.Equal(bodyDisposition, shard.BodyUse.Disposition);
    }

    [Fact]
    public void BodyTypeShardRejectsMismatchedLibraryGeneration()
    {
        MetadataLibrarySignatureUseResult inventory = Evidence(
            [("N", "A", MetadataLibraryTypeClassification.None)],
            [],
            exactNamespace: "N");
        AnalysisLibraryBodyUseResult bodyUse = BodyEvidence(
            inventory.Types.Select(static type =>
                new AnalysisLibraryBodyUseType(
                    type.Type,
                    type.Name,
                    type.DefinitionKind)),
            []) with
        {
            Receipt = new(
                Guid.NewGuid(),
                s_assembly,
                new WorkReceipt(0, [])),
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => LibraryStructuralReport.CreateBodyTypeLeverageShard(
                inventory,
                bodyUse));
        Assert.Contains(
            "same exact Library generation",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExhaustiveCompositionRequiresEveryOrderedNamespaceShard()
    {
        MetadataLibrarySignatureUseResult whole = Evidence(
            [
                ("A", "Source", MetadataLibraryTypeClassification.None),
                ("B", "Target", MetadataLibraryTypeClassification.None),
            ],
            [(0, 1)],
            exactNamespace: null);
        LibraryStructuralNamespaceLeverageIndex index =
            LibraryStructuralReport.CreateNamespaceLeverageIndex(whole);
        LibraryStructuralTypeLeverageShard[] shards =
        [
            Shard("B", [("Target", MetadataLibraryTypeClassification.None)], []),
            Shard("A", [("Source", MetadataLibraryTypeClassification.None)], []),
        ];

        LibraryStructuralSalienceDocument document =
            LibraryStructuralReport.CreateStructuralSalience(
                index,
                shards);

        Assert.Equal(
            ["B", "A"],
            document.TypeLeverageShards.Select(
                static shard => shard.Namespace));
        var available =
            Assert.IsType<LibraryStructuralReportResult.Available>(
                LibraryStructuralReport.Execute(
                    s_analysis,
                    document));
        Assert.Same(
            document,
            available.Document.StructuralSalience);

        Assert.Throws<ArgumentException>(
            () => LibraryStructuralReport.CreateStructuralSalience(
                index,
                shards.Reverse()));
        Assert.Throws<ArgumentException>(
            () => LibraryStructuralReport.CreateStructuralSalience(
                index,
                shards.Take(1)));
    }

    [Fact]
    public void StructuralSalienceRejectsMismatchedAnalysisGeneration()
    {
        MetadataLibrarySignatureUseResult whole = Evidence(
            [("A", "Source", MetadataLibraryTypeClassification.None)],
            [],
            exactNamespace: null);
        LibraryStructuralNamespaceLeverageIndex index =
            LibraryStructuralReport.CreateNamespaceLeverageIndex(whole);
        LibraryStructuralSalienceDocument document =
            LibraryStructuralReport.CreateStructuralSalience(
                index,
                [
                    Shard(
                        "A",
                        [("Source", MetadataLibraryTypeClassification.None)],
                        []),
                ]);
        document = document with
        {
            NamespaceIndex = document.NamespaceIndex with
            {
                SignatureUse =
                    document.NamespaceIndex.SignatureUse with
                    {
                        Receipt =
                            document.NamespaceIndex.SignatureUse.Receipt
                            with
                            {
                                ModuleVersionId = Guid.NewGuid(),
                            },
                    },
            },
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => LibraryStructuralReport.Execute(
                s_analysis,
                document));
        Assert.Contains(
            "exact Library generation",
            error.Message,
            StringComparison.Ordinal);
    }

    private static LibraryStructuralTypeLeverageShard Shard(
        string @namespace,
        IReadOnlyList<(
            string Name,
            MetadataLibraryTypeClassification Classification)> types,
        IReadOnlyList<(int Source, int Target)> signatureRelationships,
        MetadataLibrarySignatureUseDisposition signatureDisposition =
            MetadataLibrarySignatureUseDisposition.Complete) =>
        LibraryStructuralReport.CreateTypeLeverageShard(
            Evidence(
                types.Select(type =>
                    (@namespace, type.Name, type.Classification))
                    .ToArray(),
                signatureRelationships,
                @namespace,
                signatureDisposition));

    private static LibraryStructuralBodyTypeLeverageShard BodyShard(
        string @namespace,
        IReadOnlyList<(
            string Name,
            MetadataLibraryTypeClassification Classification)> types,
        IReadOnlyList<(
            int Source,
            int Target,
            AnalysisLibraryBodyUseOperandKind Kind)> bodyRelationships,
        MetadataLibrarySignatureUseDisposition inventoryDisposition =
            MetadataLibrarySignatureUseDisposition.Complete,
        AnalysisLibraryBodyUseDisposition bodyDisposition =
            AnalysisLibraryBodyUseDisposition.Complete)
    {
        MetadataLibrarySignatureUseResult inventory = Evidence(
            types.Select(type =>
                (@namespace, type.Name, type.Classification))
                .ToArray(),
            [],
            @namespace,
            inventoryDisposition);
        return LibraryStructuralReport.CreateBodyTypeLeverageShard(
            inventory,
            BodyEvidence(
                inventory.Types.Select(static type =>
                    new AnalysisLibraryBodyUseType(
                        type.Type,
                        type.Name,
                        type.DefinitionKind)),
                bodyRelationships,
                bodyDisposition));
    }

    private static AnalysisLibraryBodyUseResult BodyEvidence(
        IEnumerable<AnalysisLibraryBodyUseType> types,
        IReadOnlyList<(
            int Source,
            int Target,
            AnalysisLibraryBodyUseOperandKind Kind)> bodyRelationships,
        AnalysisLibraryBodyUseDisposition disposition =
            AnalysisLibraryBodyUseDisposition.Complete)
    {
        AnalysisLibraryBodyUseType[] bodyTypes = [.. types];
        AnalysisLibraryBodyUseOccurrence[] occurrences =
        [
            .. bodyRelationships.Select((relationship, ordinal) =>
                new AnalysisLibraryBodyUseOccurrence(
                    bodyTypes[relationship.Source].Type,
                    bodyTypes[relationship.Source].Name,
                    bodyTypes[relationship.Target].Type,
                    bodyTypes[relationship.Target].Name,
                    0x06000001 + ordinal,
                    relationship.Kind,
                    0x0A000001 + ordinal,
                    ordinal,
                    ordinal)),
        ];
        int examinedBodies = occurrences.Length == 0 ? 0 : 1;
        return new(
            new(
                s_moduleVersionId,
                s_assembly,
                new WorkReceipt(0, [])),
            disposition,
            [.. bodyTypes],
            [.. occurrences],
            [],
            new(
                bodiesConsidered: examinedBodies,
                bodiesExamined: examinedBodies,
                bodiesPhysicalOnly: 0,
                bodiesUnavailable: 0,
                bodiesLimited: 0,
                operandsConsidered: occurrences.Length,
                operandsExamined: occurrences.Length,
                operandsUnavailable: 0,
                operandsLimited: 0),
            []);
    }

    private static MetadataLibrarySignatureUseResult Evidence(
        IReadOnlyList<(
            string Namespace,
            string Name,
            MetadataLibraryTypeClassification Classification)> types,
        IReadOnlyList<(int Source, int Target)> signatureRelationships,
        string? exactNamespace,
        MetadataLibrarySignatureUseDisposition signatureDisposition =
            MetadataLibrarySignatureUseDisposition.Complete)
    {
        MetadataLibrarySignatureType[] signatureTypes =
        [
            .. types.Select((type, index) =>
                new MetadataLibrarySignatureType(
                    Address(index),
                    Name(type.Namespace, type.Name),
                    AssemblyTypeDefinitionKind.Class,
                    type.Classification)),
        ];
        MetadataLibrarySignatureUseOccurrence[] signatureOccurrences =
        [
            .. signatureRelationships.Select((relationship, ordinal) =>
                new MetadataLibrarySignatureUseOccurrence(
                    signatureTypes[relationship.Source].Type,
                    signatureTypes[relationship.Source].Name,
                    signatureTypes[relationship.Target].Type,
                    signatureTypes[relationship.Target].Name,
                    MetadataLibrarySignatureUseSiteKind.FieldType,
                    0x04000001 + ordinal,
                    ordinal)),
        ];

        return new(
            new(
                s_moduleVersionId,
                s_assembly,
                exactNamespace,
                new MetadataOperationCounters(0)),
            signatureDisposition,
            [.. signatureTypes],
            [.. signatureOccurrences],
            new(
                signatureOccurrences.Length,
                signatureOccurrences.Length,
                unavailable: 0,
                limited: 0),
            []);
    }

    private static LibraryStructuralTypeLeverageRow Row(
        LibraryStructuralTypeLeverageShard shard,
        int typeIndex) =>
        Assert.Single(
            shard.Rows,
            row => row.Type == Address(typeIndex));

    private static LibraryStructuralBodyTypeLeverageRow BodyRow(
        LibraryStructuralBodyTypeLeverageShard shard,
        int typeIndex) =>
        Assert.Single(
            shard.Rows,
            row => row.Type == Address(typeIndex));

    private static MetadataTypeDefinitionAddress Address(int index) =>
        MetadataTypeDefinitionAddress.FromToken(
            s_moduleVersionId,
            0x02000001 + index);

    private static MetadataTypeDefinitionName Name(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;

    private static LibraryImplementationProfileAnalysisResult
        CreateAnalysis()
    {
        var receipt = new LibraryBodyAnalysisReceipt(
            "fake.dll",
            new LibraryBodyModuleIdentity(s_assembly, s_moduleVersionId),
            LibraryBodyAnalysisFeatures.MethodEvidence
                | LibraryBodyAnalysisFeatures.ImplementationProfiles,
            HasFullMethodEvidenceScope: true,
            []);
        var coverage =
            new ImplementationProfilePopulationCoverageReceipt(
                WasRequested: true,
                HasFullMethodEvidenceScope: true,
                DeclaredMethods: [],
                ManagedMethodBodies: [],
                ProfiledEvidenceBodies: [],
                UnavailableBodies: [],
                Diagnostics: []);
        return new(receipt, coverage, [], [], []);
    }
}
