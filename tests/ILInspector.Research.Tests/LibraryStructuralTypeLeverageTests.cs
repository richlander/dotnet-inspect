using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

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
    public void TypeLeverage_UsesDirectedDistinctPeersAndUnionSelection()
    {
        LibraryStructuralTypeLeverageDocument leverage = Execute(
            [
                ("A", MetadataLibraryTypeClassification.None),
                ("B", MetadataLibraryTypeClassification.UniversalBase),
                ("C", MetadataLibraryTypeClassification.None),
                ("D", MetadataLibraryTypeClassification.None),
            ],
            signatureRelationships:
            [
                (0, 1),
                (0, 1),
                (2, 1),
                (0, 2),
                (1, 1),
            ],
            bodyRelationships:
            [
                (0, 1),
                (0, 2),
                (1, 0),
                (3, 0),
            ]);

        LibraryStructuralTypeLeverageRow a = Row(leverage, 0);
        Assert.Equal(0, a.SignatureIncomingDegree);
        Assert.Equal(2, a.BodyOutgoingDegree);
        Assert.Equal(2, a.CombinedIncomingDegree);
        Assert.Equal(2, a.CombinedOutgoingDegree);
        Assert.Equal(LibraryStructuralTypeRole.Hub, a.Role);

        LibraryStructuralTypeLeverageRow b = Row(leverage, 1);
        Assert.Equal(2, b.SignatureIncomingDegree);
        Assert.Equal(1, b.BodyOutgoingDegree);
        Assert.Equal(2, b.CombinedIncomingDegree);
        Assert.Equal(1, b.CombinedOutgoingDegree);
        Assert.False(b.RankingEligible);
        Assert.DoesNotContain(b.Type, leverage.SeaLevel.Types);
        Assert.DoesNotContain(b.Type, leverage.MountainPeak.Types);

        Assert.Equal(
            [Address(2), Address(0), Address(3)],
            leverage.SeaLevel.Types);
        Assert.Equal(
            [Address(0), Address(3), Address(2)],
            leverage.MountainPeak.Types);
        Assert.Same(
            leverage.GraphWork.SignatureIncomingDegree.SourceDocument,
            leverage.GraphWork.BodyOutgoingDegree.SourceDocument);
        Assert.Same(
            leverage.GraphWork.SignatureIncomingDegree.SourceDocument,
            leverage.GraphWork.CombinedIncomingDegree.SourceDocument);
        Assert.Same(
            leverage.GraphWork.SignatureIncomingDegree.SourceDocument,
            leverage.GraphWork.CombinedOutgoingDegree.SourceDocument);
        Assert.Equal(5, leverage.SignatureUse.OccurrenceCount);
        Assert.Equal(4, leverage.BodyUse.OccurrenceCount);
        Assert.Equal(
            8,
            leverage.GraphWork.SignatureIncomingDegree
                .CanonicalEdgesExamined);
        Assert.Equal(
            3,
            leverage.GraphWork.SignatureIncomingDegree
                .SelectedEdgesIndexed);
        Assert.Equal(
            4,
            leverage.GraphWork.BodyOutgoingDegree.SelectedEdgesIndexed);
        Assert.Equal(
            7,
            leverage.GraphWork.CombinedIncomingDegree
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
            relationships,
            []);

        LibraryStructuralTypeLeverageRow foundation = Row(leverage, 0);
        Assert.Equal(7, foundation.CombinedIncomingDegree);
        Assert.Equal(3, foundation.CombinedOutgoingDegree);
        Assert.Equal(
            LibraryStructuralTypeRole.Foundation,
            foundation.Role);

        LibraryStructuralTypeLeverageRow orchestrator = Row(leverage, 1);
        Assert.Equal(3, orchestrator.CombinedIncomingDegree);
        Assert.Equal(7, orchestrator.CombinedOutgoingDegree);
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
            signatureRelationships:
            [
                (0, 2),
                (1, 2),
            ],
            bodyRelationships: []);

        Assert.Equal(3, leverage.Rows.Length);
        Assert.Contains(leverage.Rows, row => row.Type == Address(0));
        Assert.Contains(leverage.Rows, row => row.Type == Address(1));
        Assert.DoesNotContain(leverage.Rows, row => row.Type == Address(3));
        Assert.Equal(
            [Address(2), Address(0), Address(1)],
            leverage.SeaLevel.Types);
    }

    [Theory]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Partial,
        AnalysisLibraryBodyUseDisposition.Complete,
        LibraryStructuralEvidenceDisposition.Qualified,
        LibraryStructuralEvidenceDisposition.Complete)]
    [InlineData(
        MetadataLibrarySignatureUseDisposition.Complete,
        AnalysisLibraryBodyUseDisposition.Qualified,
        LibraryStructuralEvidenceDisposition.Complete,
        LibraryStructuralEvidenceDisposition.Qualified)]
    public void TypeLeverage_QualifiesProducerViewsIndependently(
        MetadataLibrarySignatureUseDisposition signatureDisposition,
        AnalysisLibraryBodyUseDisposition bodyDisposition,
        LibraryStructuralEvidenceDisposition expectedSeaLevel,
        LibraryStructuralEvidenceDisposition expectedMountainPeak)
    {
        LibraryStructuralTypeLeverageDocument leverage = Execute(
            [
                ("A", MetadataLibraryTypeClassification.None),
                ("B", MetadataLibraryTypeClassification.None),
            ],
            [(0, 1)],
            [(1, 0)],
            signatureDisposition,
            bodyDisposition);

        Assert.Equal(expectedSeaLevel, leverage.SeaLevel.Disposition);
        Assert.Equal(
            expectedMountainPeak,
            leverage.MountainPeak.Disposition);
        Assert.Equal(
            LibraryStructuralEvidenceDisposition.Qualified,
            leverage.RoleDisposition);
        Assert.Equal(
            signatureDisposition,
            leverage.SignatureUse.Disposition);
        Assert.Equal(bodyDisposition, leverage.BodyUse.Disposition);
    }

    [Fact]
    public void TypeLeverage_RejectsMismatchedProducerEvidence()
    {
        (MetadataLibrarySignatureUseResult signature,
            AnalysisLibraryBodyUseResult body) = Evidence(
                [
                    ("A", MetadataLibraryTypeClassification.None),
                    ("B", MetadataLibraryTypeClassification.None),
                ],
                [(0, 1)],
                []);
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
                mismatched,
                body));
        Assert.Contains(
            "exact Library generation",
            error.Message,
            StringComparison.Ordinal);

        AnalysisLibraryBodyUseResult mismatchedInventory = body with
        {
            Types = [.. body.Types.Skip(1)],
        };
        error = Assert.Throws<ArgumentException>(
            () => LibraryStructuralReport.Execute(
                s_analysis,
                signature,
                mismatchedInventory));
        Assert.Contains(
            "inventories",
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
            [(0, 1)],
            [(1, 0)]);

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
        IReadOnlyList<(int Source, int Target)> bodyRelationships,
        MetadataLibrarySignatureUseDisposition signatureDisposition =
            MetadataLibrarySignatureUseDisposition.Complete,
        AnalysisLibraryBodyUseDisposition bodyDisposition =
            AnalysisLibraryBodyUseDisposition.Complete)
    {
        (MetadataLibrarySignatureUseResult signature,
            AnalysisLibraryBodyUseResult body) = Evidence(
                types,
                signatureRelationships,
                bodyRelationships,
                signatureDisposition,
                bodyDisposition);
        var available = Assert.IsType<LibraryStructuralReportResult.Available>(
            LibraryStructuralReport.Execute(
                s_analysis,
                signature,
                body));
        return Assert.IsType<LibraryStructuralTypeLeverageDocument>(
            available.Document.TypeLeverage);
    }

    private static (
        MetadataLibrarySignatureUseResult Signature,
        AnalysisLibraryBodyUseResult Body) Evidence(
        IReadOnlyList<(string Name, MetadataLibraryTypeClassification
            Classification)> types,
        IReadOnlyList<(int Source, int Target)> signatureRelationships,
        IReadOnlyList<(int Source, int Target)> bodyRelationships,
        MetadataLibrarySignatureUseDisposition signatureDisposition =
            MetadataLibrarySignatureUseDisposition.Complete,
        AnalysisLibraryBodyUseDisposition bodyDisposition =
            AnalysisLibraryBodyUseDisposition.Complete)
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
        AnalysisLibraryBodyUseType[] bodyTypes =
        [
            .. signatureTypes.Select(type =>
                new AnalysisLibraryBodyUseType(
                    type.Type,
                    type.Name,
                    type.DefinitionKind)),
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
        AnalysisLibraryBodyUseOccurrence[] bodyOccurrences =
        [
            .. bodyRelationships.Select((relationship, ordinal) =>
                new AnalysisLibraryBodyUseOccurrence(
                    bodyTypes[relationship.Source].Type,
                    bodyTypes[relationship.Source].Name,
                    bodyTypes[relationship.Target].Type,
                    bodyTypes[relationship.Target].Name,
                    0x06000001,
                    AnalysisLibraryBodyUseOperandKind.Type,
                    0x01000001 + ordinal,
                    ordinal,
                    ordinal)),
        ];

        return (
            new(
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
                []),
            new(
                new(
                    identity.ModuleVersionId,
                    s_assembly,
                    new WorkReceipt(0, [])),
                bodyDisposition,
                [.. bodyTypes],
                [.. bodyOccurrences],
                [],
                new(
                    bodiesConsidered: 0,
                    bodiesExamined: 0,
                    bodiesPhysicalOnly: 0,
                    bodiesUnavailable: 0,
                    bodiesLimited: 0,
                    operandsConsidered: bodyOccurrences.Length,
                    operandsExamined: bodyOccurrences.Length,
                    operandsUnavailable: 0,
                    operandsLimited: 0),
                []));
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
