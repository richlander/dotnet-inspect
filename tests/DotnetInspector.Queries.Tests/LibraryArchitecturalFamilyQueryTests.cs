using System.Collections.Immutable;
using System.Text.Json;

using CSharpText;

using DotnetInspector.Fixtures;
using DotnetInspector.PerformanceOracles;
using DotnetInspector.ResearchSections;

using ILInspector.Metadata;
using ILInspector.Research;

using Inspector.Artifacts;

using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Rows;

namespace DotnetInspector.Queries.Tests;

public sealed class LibraryArchitecturalFamilyQueryTests
{
    [Fact]
    public void QuerySpace_DeclaresBothOwnerRowVocabularies()
    {
        QuerySpaceBinding querySpace = LibraryArchitecturalFamilyQuery.QuerySpace;

        Assert.Equal(
            "architectural-families",
            LibraryArchitecturalFamilyQuery.OperationIdentity);
        Assert.Equal(
            "families",
            LibraryArchitecturalFamilyQuery.FamilyRowsRowSet);
        Assert.Equal(
            "types",
            LibraryArchitecturalFamilyQuery.TypeRowsRowSet);
        Assert.Equal(
            [
                LibraryArchitecturalFamilyQuery.FamilyRowsRowSet,
                LibraryArchitecturalFamilyQuery.TypeRowsRowSet,
            ],
            querySpace.Descriptor.Operation.RowSets);
        Assert.Equal(
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            querySpace.Descriptor.Terminals);
        Assert.Equal(2, querySpace.RowScopes.Count);
        Assert.Contains(
            LibraryArchitecturalFamilyQuery.FamilyRowsScope.Descriptor.Facets,
            facet =>
                facet.Key
                    == LibraryArchitecturalFamilyQuery
                        .FamilyOrchestratorCountKey);
        Assert.Contains(
            LibraryArchitecturalFamilyQuery.TypeRowsScope.Descriptor.Facets,
            facet =>
                facet.Key
                    == LibraryArchitecturalFamilyQuery.TypeStructuralRoleKey);
    }

    [Theory]
    [InlineData("all", LibraryNameFamilyPopulationKind.AllTypes)]
    [InlineData(
        "ordinary",
        LibraryNameFamilyPopulationKind.OrdinaryEvidenceOnly)]
    [InlineData(
        "generated",
        LibraryNameFamilyPopulationKind.GeneratedEvidenceOnly)]
    [InlineData("mixed", LibraryNameFamilyPopulationKind.MixedEvidence)]
    [InlineData("unknown", LibraryNameFamilyPopulationKind.Unknown)]
    public void ResolveIntent_BindsExactPopulation(
        string token,
        LibraryNameFamilyPopulationKind expected)
    {
        var accepted =
            Assert.IsType<LibraryArchitecturalFamilyQueryPlanResult.Accepted>(
                LibraryArchitecturalFamilyQuery.ResolveIntent(
                    PortableQueryIntent.Create(
                        [
                            new(
                                LibraryArchitecturalFamilyQuery.PopulationTermKey,
                                PortableQueryOperator.Equal,
                                token),
                        ],
                        [],
                        [],
                        []),
                    TestContext.Current.CancellationToken));

        Assert.Equal(expected, accepted.Plan.Population);
    }

    [Fact]
    public void Execute_SelectsRowsAfterOneCompleteComposition()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryArchitecturalFamilyQueryPlan operation =
            LibraryArchitecturalFamilyQuery.CreatePlan(
                LibraryNameFamilyPopulationKind.AllTypes);
        QuerySpaceRequest request =
            LibraryArchitecturalFamilyQuery.CreateFamilyRequest(
                operation,
                RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(5)]),
                QuerySpaceTerminalRequirement.Rows);

        var available =
            Assert.IsType<LibraryArchitecturalFamilyQueryResult.Available>(
                LibraryArchitecturalFamilyInspection.Execute(
                    fixture.Assembly,
                    fixture.Session,
                    fixture.Provenance,
                    operation,
                    request,
                    TestContext.Current.CancellationToken));

        Assert.Equal(5, available.SelectedRowCount);
        Assert.Equal(5, available.FamilyRows.Length);
        Assert.True(available.TotalRowCount > available.SelectedRowCount);
        Assert.Empty(available.TypeRows);
        Assert.Equal(
            available.Document.Receipt.ExactTypeCount,
            available.Document.Types.Length);
        Assert.Equal(
            available.Population.TypeCount,
            available.Document.Types.Length);
    }

    [Fact]
    public void Execute_CountAndTypeRowsRetainExactPopulationTotals()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryArchitecturalFamilyQueryPlan operation =
            LibraryArchitecturalFamilyQuery.CreatePlan(
                LibraryNameFamilyPopulationKind.AllTypes);
        QuerySpaceRequest countRequest =
            LibraryArchitecturalFamilyQuery.CreateFamilyRequest(
                operation,
                RowSelectionIntent<string>.Create([]),
                QuerySpaceTerminalRequirement.Count);
        QuerySpaceRequest typeRequest =
            LibraryArchitecturalFamilyQuery.CreateTypeRequest(
                operation,
                RowSelectionIntent<string>.Create([]),
                QuerySpaceTerminalRequirement.Rows);

        var count =
            Assert.IsType<LibraryArchitecturalFamilyQueryResult.Available>(
                LibraryArchitecturalFamilyInspection.Execute(
                    fixture.Assembly,
                    fixture.Session,
                    fixture.Provenance,
                    operation,
                    countRequest,
                    TestContext.Current.CancellationToken));
        var types =
            Assert.IsType<LibraryArchitecturalFamilyQueryResult.Available>(
                LibraryArchitecturalFamilyInspection.Execute(
                    fixture.Assembly,
                    fixture.Session,
                    fixture.Provenance,
                    operation,
                    typeRequest,
                    TestContext.Current.CancellationToken));

        Assert.Equal(count.TotalRowCount, count.Count);
        Assert.Equal(count.TotalRowCount, count.SelectedRowCount);
        Assert.Empty(count.FamilyRows);
        Assert.Equal(
            types.Document.Receipt.ExactTypeCount,
            types.TotalRowCount);
        Assert.Equal(types.TotalRowCount, types.SelectedRowCount);
        Assert.Equal(types.TotalRowCount, types.TypeRows.Length);
        Assert.All(
            types.TypeRows,
            row => Assert.Equal(
                fixture.Assembly.Registration.ModuleVersionId,
                row.Type.ModuleVersionId));
    }

    [Fact]
    public void CompleteJson_RetainsTypesPopulationsAndSupportAddresses()
    {
        using FixtureExecution fixture = OpenFixture();
        LibraryArchitecturalFamilyQueryPlan operation =
            LibraryArchitecturalFamilyQuery.CreatePlan(
                LibraryNameFamilyPopulationKind.AllTypes);
        QuerySpaceRequest request =
            LibraryArchitecturalFamilyQuery.CreateFamilyRequest(
                operation,
                RowSelectionIntent<string>.Create(
                    [RowSelectionIntentOperation<string>.Head(1)]),
                QuerySpaceTerminalRequirement.Rows);
        var available =
            Assert.IsType<LibraryArchitecturalFamilyQueryResult.Available>(
                LibraryArchitecturalFamilyInspection.Execute(
                    fixture.Assembly,
                    fixture.Session,
                    fixture.Provenance,
                    operation,
                    request,
                    TestContext.Current.CancellationToken));

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            LibraryArchitecturalFamilyInspectionJson.Write(
                writer,
                available.Document);
        using JsonDocument json = JsonDocument.Parse(stream.ToArray());
        JsonElement root = json.RootElement;

        Assert.Equal(
            available.Document.Types.Length,
            root.GetProperty("types").GetArrayLength());
        Assert.Equal(
            available.Document.Populations.Length,
            root.GetProperty("populations").GetArrayLength());
        Assert.Equal(
            available.Document.Receipt.ExactTypeCount,
            root.GetProperty("receipt")
                .GetProperty("typeCount")
                .GetInt32());
        LibraryArchitecturalFamilyPopulation population =
            available.Document.Populations[0];
        LibraryArchitecturalFamilyRow family = population.Families[0];
        JsonElement jsonFamily = root.GetProperty("populations")[0]
            .GetProperty("families")[0];
        Assert.Equal(
            family.Types.Length,
            jsonFamily.GetProperty("types").GetArrayLength());
        Assert.Equal(
            family.NoIssuedStructuralRoles.Length,
            jsonFamily.GetProperty("noIssuedStructuralRoles")
                .GetArrayLength());
        Assert.Same(
            available.Document,
            LibraryArchitecturalFamilyInspection.Envelope(available).Content);
    }

    [Fact]
    public void TypeRoleAndPolePredicates_SelectExactOwnerValues()
    {
        LibraryArchitecturalFamilyTypeRow foundation = TypeRow(
            row: 1,
            LibraryStructuralTypeRole.Foundation,
            LibraryStructuralTypePole.SeaLevel);
        LibraryArchitecturalFamilyTypeRow orchestrator = TypeRow(
            row: 2,
            LibraryStructuralTypeRole.Orchestrator,
            LibraryStructuralTypePole.MountainPeak);
        RowQueryResolutionResult<LibraryArchitecturalFamilyTypeRow>
            roleResolution =
                LibraryArchitecturalFamilyQuery.TypeRowsScope.Resolve(
                    PortableQueryIntent.Create(
                        [
                            new(
                                LibraryArchitecturalFamilyQuery
                                    .TypeStructuralRoleKey,
                                PortableQueryOperator.Equal,
                                "Foundation"),
                        ],
                        [],
                        [],
                        []));
        RowQueryResolutionResult<LibraryArchitecturalFamilyTypeRow>
            poleResolution =
                LibraryArchitecturalFamilyQuery.TypeRowsScope.Resolve(
                    PortableQueryIntent.Create(
                        [
                            new(
                                LibraryArchitecturalFamilyQuery
                                    .TypeStructuralPoleKey,
                                PortableQueryOperator.Equal,
                                "MountainPeak"),
                        ],
                        [],
                        [],
                        []));

        Assert.True(roleResolution.IsSuccess);
        Assert.True(poleResolution.IsSuccess);
        Assert.Equal(
            [foundation],
            RowQueryExecutor.Apply(
                [foundation, orchestrator],
                roleResolution.Plan!).Values);
        Assert.Equal(
            [orchestrator],
            RowQueryExecutor.Apply(
                [foundation, orchestrator],
                poleResolution.Plan!).Values);
    }

    [Fact]
    public void FamilyCountPredicate_PreservesPrevalenceOrder()
    {
        LibraryArchitecturalFamilyRow smaller =
            FamilyRow("Small", typeCount: 2, namespaces: 2);
        LibraryArchitecturalFamilyRow narrower =
            FamilyRow("Narrow", typeCount: 5, namespaces: 1);
        LibraryArchitecturalFamilyRow wider =
            FamilyRow(
                "Wide",
                typeCount: 5,
                namespaces: 2,
                foundationCount: 2);
        RowQueryResolutionResult<LibraryArchitecturalFamilyRow> resolution =
            LibraryArchitecturalFamilyQuery.FamilyRowsScope.Resolve(
                PortableQueryIntent.Create(
                    [
                        new(
                            LibraryArchitecturalFamilyQuery
                                .FamilyFoundationCountKey,
                            PortableQueryOperator.AtLeast,
                            "1"),
                    ],
                    [],
                    [],
                    []));

        Assert.True(resolution.IsSuccess);
        Assert.Equal(
            [wider],
            RowQueryExecutor.Apply(
                [smaller, narrower, wider],
                resolution.Plan!).Values);

        RowQueryResolutionResult<LibraryArchitecturalFamilyRow>
            prevalenceResolution =
                LibraryArchitecturalFamilyQuery.FamilyRowsScope.Resolve(
                    PortableQueryIntent.Create([], [], [], []));
        Assert.Equal(
            [wider, narrower, smaller],
            RowQueryExecutor.Apply(
                [smaller, narrower, wider],
                prevalenceResolution.Plan!).Values);
    }

    [Theory]
    [InlineData(
        "one-word",
        LibraryNameFamilyKind.OneWordSuffix)]
    [InlineData(
        "two-word",
        LibraryNameFamilyKind.TwoWordSuffix)]
    public void FamilyKindPredicate_BindsDeclaredTokens(
        string token,
        LibraryNameFamilyKind expected)
    {
        LibraryArchitecturalFamilyRow oneWord =
            FamilyRow("One", typeCount: 2, namespaces: 1);
        LibraryArchitecturalFamilyRow twoWord =
            FamilyRow(
                "Two",
                typeCount: 2,
                namespaces: 1,
                kind: LibraryNameFamilyKind.TwoWordSuffix);
        RowQueryResolutionResult<LibraryArchitecturalFamilyRow> resolution =
            LibraryArchitecturalFamilyQuery.FamilyRowsScope.Resolve(
                PortableQueryIntent.Create(
                    [
                        new(
                            LibraryArchitecturalFamilyQuery.FamilyKindKey,
                            PortableQueryOperator.Equal,
                            token),
                    ],
                    [],
                    [],
                    []));

        Assert.True(resolution.IsSuccess);
        Assert.Equal(
            [expected == LibraryNameFamilyKind.OneWordSuffix
                ? oneWord
                : twoWord],
            RowQueryExecutor.Apply(
                [oneWord, twoWord],
                resolution.Plan!).Values);
    }

    [Fact]
    public void PerformanceScorecard_AgreesAcrossAllSupportedClosings()
    {
        var shape = new ScorecardShape(
                N: 2,
                WindowFirst: 1,
                WindowLast: 2);
        IReadOnlyList<
                ScorecardAsset<LibraryArchitecturalFamilyScorecardAsset>> assets =
                    LibraryArchitecturalFamilyPopulationScorecard.LoadAssets(
                        [
                            FixtureCatalog.ResearchNameFamilies
                                .AssemblyPath(),
                        ]);
        ScorecardColumn<
                LibraryArchitecturalFamilyScorecardAsset,
                LibraryArchitecturalFamilyRow> oracle =
                    LibraryArchitecturalFamilyPopulationScorecard
                        .NLinqColumn(shape);
        ScorecardCheck check = Scorecard.Check(
                assets,
                oracle,
                [
                    LibraryArchitecturalFamilyPopulationScorecard
                        .LinqColumn(shape),
                    oracle,
                    LibraryArchitecturalFamilyPopulationScorecard
                        .QuerySpaceColumn(shape),
                ],
                LibraryArchitecturalFamilyPopulationScorecard.RowText,
                closings:
                [
                    ScorecardClosing.Count,
                    ScorecardClosing.Head,
                    ScorecardClosing.Tail,
                    ScorecardClosing.Rows,
                    ScorecardClosing.Window,
                ]);

        Assert.True(
                check.Agrees,
                string.Join(Environment.NewLine, check.Mismatches));
        Assert.Empty(check.WindowFailures);
    }

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
                    "architectural-family-query-fixture"))
            ?? throw new InvalidOperationException(
                "The fixture must contain managed metadata.");
        PdbContext context = PdbContext.OpenEmbeddedPdbOnly(assembly);
        PdbSourceProvenanceOutcome provenance =
            context.InspectSourceProvenance();
        AssemblyInspectionSession session =
            AssemblyInspectionSession.Borrow(context);
        return new(authority, assembly, context, session, provenance);
    }

    private static LibraryArchitecturalFamilyTypeRow TypeRow(
        int row,
        LibraryStructuralTypeRole role,
        LibraryStructuralTypePole pole)
    {
        string typeName = $"Type{row}";
        var created =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Example",
                    ImmutableArray.Create(typeName)));
        return new(
            MetadataTypeDefinitionAddress.FromToken(
                Guid.Parse(
                    "00112233-4455-6677-8899-aabbccddeeff"),
                0x02000000 + row),
            created.Name,
            AssemblyTypeDefinitionKind.Class,
            "Example",
            OneWordSuffix: null,
            TwoWordSuffix: null,
            OneWordResidual: null,
            TwoWordResidual: null,
            SourceDisposition: null,
            SignatureIncomingDegree: row,
            SignatureOutgoingDegree: row,
            StructuralClassification: null,
            role,
            pole,
            LibraryStructuralEvidenceDisposition.Complete);
    }

    private static LibraryArchitecturalFamilyRow FamilyRow(
        string word,
        int typeCount,
        int namespaces,
        int foundationCount = 0,
        LibraryNameFamilyKind kind =
            LibraryNameFamilyKind.OneWordSuffix)
    {
        var methodology = new LibraryNameFamilyMethodology(
            "test",
            new IdentifierWordOracleReceipt(
                "grammar",
                "vocabulary",
                "digest",
                "source",
                "review",
                EntryCount: 1));
        var identity = new LibraryNameFamilyIdentity(
            methodology,
            kind,
            kind == LibraryNameFamilyKind.OneWordSuffix
                ? [word]
                : [word, "Family"],
            separator:
                kind == LibraryNameFamilyKind.OneWordSuffix
                    ? null
                    : "");
        return new(
            identity,
            typeCount,
            foundationCount,
            HubCount: 0,
            OrchestratorCount: 0,
            SeaLevelCount: 0,
            MountainPeakCount: 0,
            NoIssuedStructuralRoleCount:
                typeCount - foundationCount,
            DistinctNamespaceCount: namespaces,
            LibraryStructuralEvidenceDisposition.Complete,
            Types: [],
            Foundations: [],
            Hubs: [],
            Orchestrators: [],
            SeaLevels: [],
            MountainPeaks: [],
            NoIssuedStructuralRoles: []);
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
