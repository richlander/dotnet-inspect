using System.Collections.Immutable;

using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageVersionCellMetadataInspectionTests
{
    const string HistoryType =
        "DiffFixtureSample.MethodRemovalSample";

    [Fact]
    public async Task
        HistoryDenseChangeAndReversionRetainBothChangedDestinations()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV2.AssemblyPath()),
            (population[2], FixtureCatalog.DiffV1.AssemblyPath()));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(population, HistoryType),
                executor);

        Assert.Equal(3, executor.Calls);
        Assert.Equal(3, document.Evaluations.Length);
        Assert.Equal(2, document.Transitions.Length);
        Assert.All(
            document.Transitions,
            transition =>
            {
                Assert.True(transition.IsPopulationAdjacent);
                Assert.False(transition.Comparison.IsExact);
            });
        var removal = Assert.IsType<
            FindingComparison<ApiMemberHandle>.Complete>(
                document.Transitions[0].Comparison.Value);
        Assert.Contains(
            removal.Pairs,
            static pair => pair.Kind == PairKind.Removed);
        var restoration = Assert.IsType<
            FindingComparison<ApiMemberHandle>.Complete>(
                document.Transitions[1].Comparison.Value);
        Assert.Contains(
            restoration.Pairs,
            static pair => pair.Kind == PairKind.Added);
        Assert.Equal(
            [
                DiffHistoryChangedVersionState.Changed,
                DiffHistoryChangedVersionState.Changed,
            ],
            document.ChangedVersionAssessments
                .Select(static assessment => assessment.State));
        Assert.Equal(
            [1, 2],
            document.ChangedVersions
                .Select(static address => address.Position));
        Assert.DoesNotContain(
            population[0].Cell.Address,
            document.ChangedVersions);
        Assert.True(
            document.Correlation.Compare("#1", "#3").IsExact);
    }

    [Fact]
    public async Task
        HistorySparseTransitionPreservesGapWithoutClaimingOnset()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[2], FixtureCatalog.DiffV2.AssemblyPath()));
        PackageVersionAddress first =
            population[0].Cell.Address;
        PackageVersionAddress last =
            population[2].Cell.Address;

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    HistoryType,
                    [first, last]),
                executor);

        Assert.Equal(2, executor.Calls);
        DiffHistoryTransition<ApiMemberHandle> transition =
            Assert.Single(document.Transitions);
        Assert.False(transition.IsPopulationAdjacent);
        Assert.Same(
            population[1].Cell.Address,
            Assert.Single(transition.UnevaluatedBetween));
        Assert.False(transition.Comparison.IsExact);
        Assert.Equal(
            [
                DiffHistoryChangedVersionState.Unevaluated,
                DiffHistoryChangedVersionState.Unevaluated,
            ],
            document.ChangedVersionAssessments
                .Select(static assessment => assessment.State));
        Assert.Empty(document.ChangedVersions);
        Assert.Same(
            population[1].Cell.Address,
            Assert.Single(document.UnevaluatedAddresses));
    }

    [Fact]
    public async Task HistoryRetainsProducerClassifiedFacetChange()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV2.AssemblyPath()));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    "DiffFixtureSample.BodyStateSample"),
                executor);

        var comparison = Assert.IsType<
            FindingComparison<ApiMemberHandle>.Complete>(
                Assert.Single(document.Transitions)
                    .Comparison.Value);
        Assert.Contains(
            comparison.Pairs,
            static pair => pair.Kind == PairKind.Changed);
        Assert.Equal(
            DiffHistoryChangedVersionState.Changed,
            Assert.Single(
                document.ChangedVersionAssessments).State);
    }

    [Fact]
    public async Task HistoryFailureDoesNotAbortLaterEvaluation()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0");
        byte[] firstImage =
            File.ReadAllBytes(FixtureCatalog.DiffV1.AssemblyPath());
        byte[] lastImage =
            File.ReadAllBytes(FixtureCatalog.DiffV2.AssemblyPath());
        var executor = new SettlementExecutor(execution =>
        {
            int position = execution.Cell.Address.Position;
            if (position == 1)
            {
                return new PackageHouseSettlement.ResourceFree(
                    new PackageHouseResult.Rejected(
                        new PackageHouseEvidence(execution.Request),
                        Reason("Fixture rejection.")));
            }

            CellFixture fixture = population[position];
            byte[] image = position == 0 ? firstImage : lastImage;
            return fixture.Realize(
                execution,
                fixture.Content(
                    ($"lib/{Framework}/DiffFixtureSample.dll", image)));
        });

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(population, HistoryType),
                executor);

        Assert.Equal(3, executor.Calls);
        Assert.IsType<FindingInspection<ApiMemberHandle>.Complete>(
            document.Evaluations[0].Inspection.Value);
        Assert.IsType<FindingInspection<ApiMemberHandle>.Failed>(
            document.Evaluations[1].Inspection.Value);
        Assert.IsType<FindingInspection<ApiMemberHandle>.Complete>(
            document.Evaluations[2].Inspection.Value);
        Assert.All(
            document.Transitions,
            transition =>
                Assert.IsType<FindingComparison<ApiMemberHandle>.Failed>(
                    transition.Comparison.Value));
        Assert.All(
            document.ChangedVersionAssessments,
            assessment => Assert.Equal(
                DiffHistoryChangedVersionState.Failed,
                assessment.State));
        Assert.Empty(document.ChangedVersions);
    }

    [Fact]
    public async Task HistorySubjectAbsenceIsNotFailure()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV2.AssemblyPath()));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    "DiffFixtureSample.DoesNotExist"),
                executor);

        Assert.All(
            document.Evaluations,
            evaluation =>
            {
                var absent = Assert.IsType<
                    FindingInspection<ApiMemberHandle>.Absent>(
                        evaluation.Inspection.Value);
                Assert.Equal(
                    FindingInspectionAbsenceKind.SubjectAbsent,
                    absent.Kind);
            });
        Assert.True(Assert.Single(document.Transitions).Comparison.IsExact);
        Assert.Equal(
            DiffHistoryChangedVersionState.Unchanged,
            Assert.Single(
                document.ChangedVersionAssessments).State);
    }

    [Fact]
    public async Task
        HistorySubjectAbsentAcrossParticipantsDoesNotBecomeAmbiguous()
    {
        CellFixture fixture =
            CellFixture.Create("Contoso.History", "1.0.0");
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/DiffFixtureSample.dll",
                File.ReadAllBytes(
                    FixtureCatalog.DiffV1.AssemblyPath())),
            ($"lib/{Framework}/ILInspector.Analysis.Fixtures.dll",
                File.ReadAllBytes(
                    FixtureCatalog.AnalysisStringLiterals.AssemblyPath())));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    [fixture],
                    "Contoso.DoesNotExist"),
                new SettlementExecutor(
                    execution => fixture.Realize(
                        execution,
                        content)));

        var absent = Assert.IsType<
            FindingInspection<ApiMemberHandle>.Absent>(
                Assert.Single(document.Evaluations)
                    .Inspection.Value);
        Assert.Equal(
            FindingInspectionAbsenceKind.SubjectAbsent,
            absent.Kind);
        DiffHistoryApiMemberEvaluation evaluation =
            Assert.Single(document.Evaluations);
        Assert.IsType<
            DiffHistoryApiMemberSubjectResolution.SubjectAbsent>(
                evaluation.SubjectResolution);
        Assert.Equal(2, evaluation.Participants.Length);
    }

    [Fact]
    public async Task HistoryMultipleMatchingParticipantsAreAmbiguous()
    {
        CellFixture fixture =
            CellFixture.Create("Contoso.History", "1.0.0");
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/DiffAsmLibA.dll",
                File.ReadAllBytes(
                    FixtureCatalog.DiffAsmLibA.AssemblyPath())),
            ($"lib/{Framework}/DiffAsmLibB.dll",
                File.ReadAllBytes(
                    FixtureCatalog.DiffAsmLibB.AssemblyPath())));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest([fixture], "Shared.Token"),
                new SettlementExecutor(
                    execution => fixture.Realize(
                        execution,
                        content)));

        DiffHistoryApiMemberEvaluation evaluation =
            Assert.Single(document.Evaluations);
        var ambiguous = Assert.IsType<
            DiffHistoryApiMemberSubjectResolution.Ambiguous>(
                evaluation.SubjectResolution);
        Assert.Equal(2, ambiguous.Assemblies.Length);
        Assert.Equal(2, evaluation.Participants.Length);
        Assert.IsType<FindingInspection<ApiMemberHandle>.Failed>(
            evaluation.Inspection.Value);
    }

    [Fact]
    public async Task HistoryEmptyCompileGroupIsNoApplicableInput()
    {
        CellFixture fixture =
            CellFixture.Create("Contoso.History", "1.0.0");
        IPackageContent content = fixture.Content(
            ($"ref/{Framework}/_._", []));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest([fixture], HistoryType),
                new SettlementExecutor(
                    execution => fixture.Realize(
                        execution,
                        content)));

        DiffHistoryApiMemberEvaluation evaluation =
            Assert.Single(document.Evaluations);
        var absent = Assert.IsType<
            FindingInspection<ApiMemberHandle>.Absent>(
                evaluation.Inspection.Value);
        Assert.Equal(
            FindingInspectionAbsenceKind.NoApplicableInput,
            absent.Kind);
        Assert.IsType<
            DiffHistoryApiMemberSubjectResolution.NoApplicableInput>(
                evaluation.SubjectResolution);
        Assert.Empty(evaluation.Participants);
    }

    [Fact]
    public async Task
        HistoryInapplicableAdjacencyDoesNotBecomeChangedOrUnchanged()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0");
        IPackageContent[] contents =
        [
            population[0].Content(($"ref/{Framework}/_._", [])),
            population[1].Content(($"ref/{Framework}/_._", [])),
            population[2].Content(
                ($"lib/{Framework}/DiffFixtureSample.dll",
                    File.ReadAllBytes(
                        FixtureCatalog.DiffV1.AssemblyPath()))),
        ];
        var executor = new SettlementExecutor(execution =>
        {
            int position = execution.Cell.Address.Position;
            return population[position].Realize(
                execution,
                contents[position]);
        });

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(population, HistoryType),
                executor);

        Assert.Equal(
            [
                DiffHistoryChangedVersionState.Inapplicable,
                DiffHistoryChangedVersionState.Inapplicable,
            ],
            document.ChangedVersionAssessments
                .Select(static assessment => assessment.State));
        Assert.True(document.Transitions[0].Comparison.IsExact);
        Assert.False(document.Transitions[1].Comparison.IsExact);
        Assert.Empty(document.ChangedVersions);
    }

    [Fact]
    public async Task HistoryProjectionTruncationRemainsFailureEvidence()
    {
        CellFixture fixture =
            CellFixture.Create("Contoso.History", "1.0.0");
        IPackageContent content = fixture.Content(
            ($"lib/{Framework}/DiffFixtureSample.dll",
                File.ReadAllBytes(
                    FixtureCatalog.DiffV1.AssemblyPath())));
        var limits = new ApiSurfaceProjectionLimits(
            16,
            maxTypes: 1,
            10_000,
            100,
            100,
            100_000,
            2_000_000);

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    [fixture],
                    HistoryType,
                    projectionLimits: limits),
                new SettlementExecutor(
                    execution => fixture.Realize(
                        execution,
                        content)));

        DiffHistoryApiMemberEvaluation evaluation =
            Assert.Single(document.Evaluations);
        ApiSurfaceProjectionTruncation truncation =
            Assert.IsType<ApiSurfaceProjectionTruncation>(
                evaluation.ProjectionTruncation);
        Assert.Equal(ApiSurfaceProjectionLimit.Types, truncation.Limit);
        Assert.Equal(1, truncation.OmittedParticipants);
        Assert.Empty(evaluation.Participants);
        Assert.IsType<DiffHistoryApiMemberSubjectResolution.Failed>(
            evaluation.SubjectResolution);
        Assert.IsType<FindingInspection<ApiMemberHandle>.Failed>(
            evaluation.Inspection.Value);
    }

    [Fact]
    public async Task
        HistoryEnvelopeRetainsNonProjectableShareAndFailureDiagnostic()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0");
        var executor = new SettlementExecutor(execution =>
            new PackageHouseSettlement.ResourceFree(
                new PackageHouseResult.Rejected(
                    new PackageHouseEvidence(execution.Request),
                    Reason("Fixture rejection."))));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                HistoryRequest(population, HistoryType),
                executor,
                TestContext.Current.CancellationToken);

        var share = Assert.IsType<InspectionShare.NonProjectable>(
            envelope.Share);
        Assert.Equal("diff-history/share", share.Path);
        InspectionDiagnostic diagnostic =
            Assert.Single(envelope.Diagnostics);
        Assert.Equal(
            "diff-history.evaluation-failure",
            diagnostic.Code);
        Assert.Equal(
            InspectionDiagnosticSeverity.Error,
            diagnostic.Severity);
        Assert.Equal("1.0.0", diagnostic.Correspondence!.ToString());
    }

    [Fact]
    public async Task HistoryExecutesPinnedSystemTextJsonPopulation()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "System.Text.Json",
                "8.0.6",
                "9.0.0",
                "10.0.0");
        SettlementExecutor executor = PackageExecutor(
            (population[0], "system.text.json.8.0.6.nupkg"),
            (population[1], "system.text.json.9.0.0.nupkg"),
            (population[2], "system.text.json.10.0.0.nupkg"));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    "System.Text.Json.JsonSerializer",
                    framework: "net8.0"),
                executor);

        Assert.Equal(3, executor.Calls);
        Assert.All(
            document.Evaluations,
            evaluation =>
                Assert.IsType<
                    FindingInspection<ApiMemberHandle>.Complete>(
                        evaluation.Inspection.Value));
        Assert.NotEmpty(document.ChangedVersions);
    }

    [Fact]
    public async Task HistoryCancellationTerminatesSerialEvaluation()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        byte[] image =
            File.ReadAllBytes(FixtureCatalog.DiffV1.AssemblyPath());
        using var cancellation = new CancellationTokenSource();
        var executor = new SettlementExecutor(execution =>
        {
            CellFixture fixture =
                population[execution.Cell.Address.Position];
            PackageHouseSettlement settlement = fixture.Realize(
                execution,
                fixture.Content(
                    ($"lib/{Framework}/DiffFixtureSample.dll", image)));
            cancellation.Cancel();
            return settlement;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => DiffHistoryInspector.InspectApiMembersAsync(
                HistoryRequest(population, HistoryType),
                executor,
                cancellation.Token));

        Assert.Equal(1, executor.Calls);
    }

    [Fact]
    public void HistoryRequestRejectsForeignOrOverLimitSelection()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        PackageVersionAddress foreign =
            CellFixture.Create("Contoso.History", "3.0.0")
                .Cell.Address;

        Assert.Throws<ArgumentException>(
            () => HistoryRequest(
                population,
                HistoryType,
                [population[0].Cell.Address, foreign]));
        Assert.Throws<ArgumentException>(
            () => HistoryRequest(
                population,
                HistoryType,
                maximumEvaluations: 1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DiffHistoryApiMemberInspectionRequest(
                population[0].Cell.Population,
                population.Select(static item => item.Cell.Address),
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Realize),
                PackageHouseTargetContext.Exact(Framework),
                new DiffHistoryEvaluationLimits(2),
                new PackageVersionCellWorkspaceLimits(
                    16,
                    16_000_000,
                    32_000_000),
                DateTimeOffset.UtcNow.AddMinutes(1),
                ApiRequest(HistoryType),
                matchAcceptanceThreshold: -1));
    }

    static async Task<DiffHistoryApiMemberDocument> InspectHistoryAsync(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor)
    {
        var outcome = Assert.IsType<DiffHistoryOutcome.Available>(
            await DiffHistoryInspector.InspectApiMembersAsync(
                request,
                executor,
                TestContext.Current.CancellationToken));
        return Assert.IsType<DiffHistoryDocument.ApiMembers>(
            outcome.Document).Content;
    }

    static DiffHistoryApiMemberInspectionRequest HistoryRequest(
        ImmutableArray<CellFixture> population,
        string typeFullName,
        IEnumerable<PackageVersionAddress>? selection = null,
        string framework = Framework,
        int maximumEvaluations = 16,
        ApiSurfaceProjectionLimits? projectionLimits = null) =>
        new(
            population[0].Cell.Population,
            selection
                ?? population.Select(static item => item.Cell.Address),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(framework),
            new DiffHistoryEvaluationLimits(maximumEvaluations),
            new PackageVersionCellWorkspaceLimits(
                16,
                16_000_000,
                32_000_000),
            DateTimeOffset.UtcNow.AddMinutes(1),
            projectionLimits is null
                ? ApiRequest(typeFullName)
                : new PackageVersionCellApiInspectionRequest(
                    typeFullName,
                    projectionLimits));
}
