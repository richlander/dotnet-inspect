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
    public void TypeLeverage_UsesSymmetricDirectedDistinctSignaturePeers()
    {
        LibraryStructuralTypeLeverageDocument leverage = Execute(
            [
                ("A", MetadataLibraryTypeClassification.None),
                ("B", MetadataLibraryTypeClassification.UniversalBase),
                ("C", MetadataLibraryTypeClassification.None),
                ("D", MetadataLibraryTypeClassification.None),
            ],
            [
                (0, 1),
                (0, 1),
                (2, 1),
                (0, 2),
                (1, 1),
                (3, 0),
            ]);

        LibraryStructuralTypeLeverageRow a = Row(leverage, 0);
        Assert.Equal(1, a.SignatureIncomingDegree);
        Assert.Equal(2, a.SignatureOutgoingDegree);
        Assert.Equal(LibraryStructuralTypeRole.Hub, a.Role);

        LibraryStructuralTypeLeverageRow b = Row(leverage, 1);
        Assert.Equal(2, b.SignatureIncomingDegree);
        Assert.Equal(0, b.SignatureOutgoingDegree);
        Assert.False(b.RankingEligible);
        Assert.DoesNotContain(b.Type, leverage.SeaLevel.Types);
        Assert.DoesNotContain(b.Type, leverage.MountainPeak.Types);

        Assert.Equal(
            [Address(0), Address(2), Address(3)],
            leverage.SeaLevel.Types);
        Assert.Equal(
            [Address(0), Address(2), Address(3)],
            leverage.MountainPeak.Types);
        Assert.Same(
            leverage.GraphWork.SignatureIncomingDegree.SourceDocument,
            leverage.GraphWork.SignatureOutgoingDegree.SourceDocument);
        Assert.Equal(6, leverage.SignatureUse.OccurrenceCount);
        Assert.Equal(
            5,
            leverage.GraphWork.SignatureIncomingDegree
                .CanonicalEdgesExamined);
        Assert.Equal(
            4,
            leverage.GraphWork.SignatureIncomingDegree
                .SelectedEdgesIndexed);
        Assert.Equal(
            4,
            leverage.GraphWork.SignatureOutgoingDegree
                .SelectedEdgesIndexed);
    }

    [Fact]
    public void TypeLeverage_ClassifiesExactRoleThresholds()
    {
        (string, MetadataLibraryTypeClassification)[] types =
        [
            ("Foundation", MetadataLibraryTypeClassification.None),
            ("Orchestrator", MetadataLibraryTypeClassification.None),
            .. Enumerable.Range(0, 20).Select(index =>
                ($"Peer{index}", MetadataLibraryTypeClassification.None)),
        ];
        var relationships = new List<(int Source, int Target)>();
        relationships.AddRange(
            Enumerable.Range(2, 7).Select(peer => (peer, 0)));
        relationships.AddRange(
            Enumerable.Range(9, 3).Select(peer => (0, peer)));
        relationships.AddRange(
            Enumerable.Range(12, 3).Select(peer => (peer, 1)));
        relationships.AddRange(
            Enumerable.Range(15, 7).Select(peer => (1, peer)));

        LibraryStructuralTypeLeverageDocument leverage = Execute(
            types,
            relationships);

        LibraryStructuralTypeLeverageRow foundation = Row(leverage, 0);
        Assert.Equal(7, foundation.SignatureIncomingDegree);
        Assert.Equal(3, foundation.SignatureOutgoingDegree);
        Assert.Equal(
            LibraryStructuralTypeRole.Foundation,
            foundation.Role);

        LibraryStructuralTypeLeverageRow orchestrator = Row(leverage, 1);
        Assert.Equal(3, orchestrator.SignatureIncomingDegree);
        Assert.Equal(7, orchestrator.SignatureOutgoingDegree);
        Assert.Equal(
            LibraryStructuralTypeRole.Orchestrator,
            orchestrator.Role);
    }

    [Fact]
    public void TypeLeverage_PreservesExactIdentityAndOmitsIsolatedRows()
    {
        LibraryStructuralTypeLeverageDocument leverage = Execute(
            [
                ("Box`1", MetadataLibraryTypeClassification.None),
                ("Box`2", MetadataLibraryTypeClassification.None),
                ("Sink", MetadataLibraryTypeClassification.None),
                ("Isolated", MetadataLibraryTypeClassification.None),
            ],
            [
                (0, 2),
                (1, 2),
            ]);

        Assert.Equal(3, leverage.Rows.Length);
        Assert.Contains(leverage.Rows, row => row.Type == Address(0));
        Assert.Contains(leverage.Rows, row => row.Type == Address(1));
        Assert.DoesNotContain(leverage.Rows, row => row.Type == Address(3));
        Assert.Equal(
            [Address(2), Address(0), Address(1)],
            leverage.SeaLevel.Types);
        Assert.Equal(
            [Address(0), Address(1), Address(2)],
            leverage.MountainPeak.Types);
    }

    [Theory]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Complete,
        LibraryStructuralEvidenceDisposition.Complete)]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Partial,
        LibraryStructuralEvidenceDisposition.Qualified)]
    public void TypeLeverage_QualifiesAllSurfaceViewsFromSignatureEvidence(
        MetadataLibrarySignatureUseDisposition signatureDisposition,
        LibraryStructuralEvidenceDisposition expectedDisposition)
    {
        LibraryStructuralTypeLeverageDocument leverage = Execute(
            [
                ("A", MetadataLibraryTypeClassification.None),
                ("B", MetadataLibraryTypeClassification.None),
            ],
            [(0, 1)],
            signatureDisposition);

        Assert.Equal(expectedDisposition, leverage.SeaLevel.Disposition);
        Assert.Equal(
            expectedDisposition,
            leverage.MountainPeak.Disposition);
        Assert.Equal(expectedDisposition, leverage.RoleDisposition);
        Assert.Equal(
            signatureDisposition,
            leverage.SignatureUse.Disposition);
    }

    [Fact]
    public void TypeLeverage_RejectsMismatchedProducerEvidence()
    {
        MetadataLibrarySignatureUseResult signature = Evidence(
            [
                ("A", MetadataLibraryTypeClassification.None),
                ("B", MetadataLibraryTypeClassification.None),
            ],
            [(0, 1)]);
        MetadataLibrarySignatureUseResult mismatched = signature with
        {
            Receipt = signature.Receipt with
            {
                ModuleVersionId = Guid.NewGuid(),
            },
        };

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => LibraryStructuralReport.Execute(
                s_analysis,
                mismatched));
        Assert.Contains(
            "exact Library generation",
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MetadataLibraryTypeClassification.UniversalBase)]
    [InlineData(MetadataLibraryTypeClassification.Enum)]
    [InlineData(MetadataLibraryTypeClassification.Attribute)]
    [InlineData(MetadataLibraryTypeClassification.Exception)]
    [InlineData(MetadataLibraryTypeClassification.Delegate)]
    public void TypeLeverage_ExcludesOnlyOwnerClassifiedRowsFromRankings(
        MetadataLibraryTypeClassification classification)
    {
        LibraryStructuralTypeLeverageDocument leverage = Execute(
            [
                ("Classified", classification),
                ("Peer", MetadataLibraryTypeClassification.None),
            ],
            [(0, 1)]);

        LibraryStructuralTypeLeverageRow classified = Row(leverage, 0);
        Assert.False(classified.RankingEligible);
        Assert.DoesNotContain(classified.Type, leverage.SeaLevel.Types);
        Assert.DoesNotContain(
            classified.Type,
            leverage.MountainPeak.Types);
        Assert.Contains(leverage.Rows, row => row.Type == classified.Type);
    }

    private static LibraryStructuralTypeLeverageDocument Execute(
        IReadOnlyList<(string Name, MetadataLibraryTypeClassification
            Classification)> types,
        IReadOnlyList<(int Source, int Target)> signatureRelationships,
        MetadataLibrarySignatureUseDisposition signatureDisposition =
            MetadataLibrarySignatureUseDisposition.Complete)
    {
        MetadataLibrarySignatureUseResult signature = Evidence(
            types,
            signatureRelationships,
            signatureDisposition);
        var available = Assert.IsType<LibraryStructuralReportResult.Available>(
            LibraryStructuralReport.Execute(
                s_analysis,
                signature));
        return Assert.IsType<LibraryStructuralTypeLeverageDocument>(
            available.Document.TypeLeverage);
    }

    private static MetadataLibrarySignatureUseResult Evidence(
        IReadOnlyList<(string Name, MetadataLibraryTypeClassification
            Classification)> types,
        IReadOnlyList<(int Source, int Target)> signatureRelationships,
        MetadataLibrarySignatureUseDisposition signatureDisposition =
            MetadataLibrarySignatureUseDisposition.Complete)
    {
        LibraryBodyModuleIdentity identity = s_analysis.Receipt.ModuleIdentity;
        MetadataLibrarySignatureType[] signatureTypes =
        [
            .. types.Select((type, index) =>
                new MetadataLibrarySignatureType(
                    Address(index),
                    Name(type.Name),
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
                identity.ModuleVersionId,
                s_assembly,
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
        LibraryStructuralTypeLeverageDocument leverage,
        int typeIndex) =>
        Assert.Single(
            leverage.Rows,
            row => row.Type == Address(typeIndex));

    private static MetadataTypeDefinitionAddress Address(int index) =>
        MetadataTypeDefinitionAddress.FromToken(
            s_moduleVersionId,
            0x02000001 + index);

    private static MetadataTypeDefinitionName Name(string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("", [name])).Name;

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
