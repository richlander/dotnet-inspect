using ILInspector.Analysis;
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
        Assert.True(sea.SeaLevel);
        Assert.False(sea.MountainPeak);

        LibraryStructuralTypeLeverageRow peak = Row(shard, 1);
        Assert.Equal(0, peak.SignatureIncomingDegree);
        Assert.Equal(4, peak.SignatureOutgoingDegree);
        Assert.Equal(
            LibraryStructuralTypeRole.Orchestrator,
            peak.Role);
        Assert.False(peak.SeaLevel);
        Assert.True(peak.MountainPeak);

        LibraryStructuralTypeLeverageRow excluded = Row(shard, 5);
        Assert.False(excluded.DesignationEligible);
        Assert.False(excluded.SeaLevel);
        Assert.False(excluded.MountainPeak);
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
        Assert.DoesNotContain(weak.Rows, static row => row.SeaLevel);
        Assert.DoesNotContain(
            weak.Rows,
            static row => row.MountainPeak);

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
        Assert.Equal(2, tied.Rows.Count(static row => row.SeaLevel));
        Assert.All(
            tied.Rows.Where(static row => row.SeaLevel),
            static row => Assert.Equal(
                LibraryStructuralSalience.MinimumDesignationDegree,
                row.SignatureIncomingDegree));
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
