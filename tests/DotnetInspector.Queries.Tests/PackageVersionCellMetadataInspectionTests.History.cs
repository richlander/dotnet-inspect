using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageVersionCellMetadataInspectionTests
{
    const string HistoryType =
        "DiffFixtureSample.MethodRemovalSample";

    [Fact]
    public async Task
        ExactApiMemberHistoryIgnoresUnrelatedMemberPopulationChanges()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()),
            (population[1], FixtureCatalog.MetadataApiCorrespondencePair
                .NewAssemblyPath()));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiAsync(
                new DiffHistoryApiOperationRequest(
                    ApiHistoryRequest(
                        DiffHistoryApiFindingKind.Members,
                        population,
                        OrdinalType,
                        member: MemberTargetSelector.Parse(OrdinalMember)),
                    new DiffHistoryCountRequest(
                        DiffHistoryCountCohort.ChangedVersions)),
                executor,
                TestContext.Current.CancellationToken);
        var available = Assert.IsType<DiffHistorySectionAvailable>(
            envelope.Content);
        DiffHistoryExactApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ExactApiMember>(
                available.Document).Content;

        Assert.Equal(
            DiffHistoryExactApiMemberSelectionState.Selected,
            document.Selection.State);
        Assert.Equal(2, document.History.Evaluations.Length);
        Assert.Equal(2, document.Identity.Timeline.Length);
        Assert.True(Assert.Single(document.History.Transitions)
            .Comparison.IsExact);
        Assert.Equal(
            DiffHistoryChangedVersionState.Unchanged,
            Assert.Single(document.History
                .ChangedVersionAssessments).State);
        var count = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    available.Count);
        Assert.Equal(0, Assert.Single(count.Counts).Value);
    }

    [Fact]
    public async Task ExactApiMemberHistoryStopsOnAmbiguousSource()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiAsync(
                ApiHistoryRequest(
                    DiffHistoryApiFindingKind.Members,
                    population,
                    OrdinalType,
                    member: MemberTargetSelector.Parse("Pick")),
                executor,
                TestContext.Current.CancellationToken);
        var unavailable =
            Assert.IsType<DiffHistoryOutcome.ExactApiMemberUnavailable>(
                envelope.Content);

        Assert.Equal(1, executor.Calls);
        Assert.Equal(
            DiffHistoryExactApiMemberSelectionState.MemberUnresolved,
            unavailable.Selection.State);
        Assert.Equal(
            MemberTargetDiagnosticKind.AmbiguousMember,
            unavailable.Selection.Diagnostic!.Kind);
        Assert.Contains(
            envelope.Diagnostics,
            static diagnostic =>
                diagnostic.Code
                    == "diff-history.exact-member-unavailable");
    }

    [Fact]
    public async Task
        ExactApiMemberActionRetainsResolvedIdentityInsteadOfOrdinal()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffPair.OldAssemblyPath()),
            (population[1], FixtureCatalog.DiffPair.NewAssemblyPath()));
        MemberTargetSelector selector =
            MemberTargetSelector.Parse("Removed:1");

        var available = Assert.IsType<DiffHistoryOutcome.Available>(
            await DiffHistoryInspector.InspectApiAsync(
                ApiHistoryRequest(
                    DiffHistoryApiFindingKind.Members,
                    population,
                    HistoryType,
                    new DiffHistoryEvaluationPlan.AdaptiveBisect(2),
                    maximumEvaluations: 2,
                    member: selector),
                executor,
                TestContext.Current.CancellationToken));
        DiffHistoryExactApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ExactApiMember>(
                available.Document).Content;
        var action = Assert.IsType<DiffHistoryNextAction.PairwiseDiff>(
            Assert.Single(document.History.NextActions));

        Assert.Equal(document.Selection.Member!.Anchor, action.Member);
        Assert.NotEqual(
            selector.NormalizedSelector,
            action.Member!.StableSelector);
        Assert.Null(action.SourceAsset);
    }

    [Fact]
    public async Task
        AnalysisHistoryBindsEveryCheckpointToOneSourceReceipt()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0",
                "3.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()),
            (population[1], FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                .AssemblyPath()),
            (population[2], FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectAnalysisAsync(
                new DiffHistoryAnalysisOperationRequest(
                    AnalysisHistoryRequest(
                        PackageVersionCellAnalysisProducerKind.Allocation,
                        population),
                    new DiffHistoryCountRequest(
                        DiffHistoryCountCohort.ChangedVersions)),
                executor,
                TestContext.Current.CancellationToken);
        var available = Assert.IsType<DiffHistorySectionAvailable>(
            envelope.Content);
        DiffHistoryAnalysisDocument<AllocationOccurrence> document =
            Assert.IsType<DiffHistoryDocument.Allocations>(
                available.Document).Content;

        Assert.Equal(5, executor.Calls);
        Assert.NotNull(document.SourceReceipt);
        Assert.All(
            document.Evaluations,
            evaluation => Assert.Same(
                document.SourceReceipt,
                evaluation.SourceReceipt));
        Assert.Equal(
            ApiCoordinateSourceSelectionStatus.Selected,
            document.Evaluations[0].SourceSelection!.Status);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            document.Evaluations[0].SourceValidation!.Status);
        Assert.Null(document.Evaluations[0].Relationship);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Absent,
            document.Evaluations[1].Relationship!.Status);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            document.Evaluations[2].Relationship!.Status);
        Assert.Equal(
            [
                DiffHistoryChangedVersionState.Changed,
                DiffHistoryChangedVersionState.Changed,
            ],
            document.ChangedVersionAssessments.Select(
                static assessment => assessment.State));
        var count = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    available.Count);
        Assert.Equal(2, Assert.Single(count.Counts).Value);
    }

    [Fact]
    public async Task
        AnalysisHistoryActionRetainsReceiptMemberAndSourceAsset()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()),
            (population[1], FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                .AssemblyPath()));

        var available = Assert.IsType<DiffHistoryOutcome.Available>(
            await DiffHistoryInspector.InspectAnalysisAsync(
                AnalysisHistoryRequest(
                    PackageVersionCellAnalysisProducerKind.Allocation,
                    population,
                    type: OrdinalType.ToLowerInvariant(),
                    evaluationPlan:
                        new DiffHistoryEvaluationPlan.AdaptiveBisect(2),
                    maximumEvaluations: 2),
                executor,
                TestContext.Current.CancellationToken));
        DiffHistoryAnalysisDocument<AllocationOccurrence> document =
            Assert.IsType<DiffHistoryDocument.Allocations>(
                available.Document).Content;
        var action = Assert.IsType<DiffHistoryNextAction.PairwiseDiff>(
            Assert.Single(document.NextActions));

        Assert.Equal(document.SourceReceipt!.Member, action.Member);
        Assert.Equal(
            document.SourceReceipt.Member.TypeFullName,
            action.TypeFullName);
        Assert.Equal(document.SourceReceipt.Asset, action.SourceAsset);
    }

    [Fact]
    public async Task AnalysisHistorySourceSelectionFailureStopsCheckpoints()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));
        DiffHistoryAnalysisInspectionRequest request =
            AnalysisHistoryRequest(
                PackageVersionCellAnalysisProducerKind.Unsafety,
                population,
                member: "Missing");

        var available = Assert.IsType<DiffHistoryOutcome.Available>(
            await DiffHistoryInspector.InspectAnalysisAsync(
                request,
                executor,
                TestContext.Current.CancellationToken));
        DiffHistoryAnalysisDocument<UnsafetyOccurrence> document =
            Assert.IsType<DiffHistoryDocument.Unsafety>(
                available.Document).Content;

        Assert.Equal(1, executor.Calls);
        Assert.Null(document.SourceReceipt);
        DiffHistoryAnalysisEvaluation<UnsafetyOccurrence> evaluation =
            Assert.Single(document.Evaluations);
        Assert.Equal(
            DiffHistoryAnalysisEvaluationState.SourceUnselected,
            evaluation.State);
        Assert.Equal(
            ApiCoordinateSourceSelectionStatus.Refused,
            evaluation.SourceSelection!.Status);
        Assert.IsType<FindingInspection<UnsafetyOccurrence>.Failed>(
            evaluation.Inspection.Value);
        Assert.IsType<DiffHistoryTerminalOutcome.BlockedByFailure>(
            document.TerminalOutcome);
        Assert.Same(population[1].Cell.Address,
            Assert.Single(document.UnevaluatedAddresses));
    }

    [Fact]
    public void AnalysisHistoryRequiresSelectedSourceVersion()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            AnalysisHistoryRequest(
                PackageVersionCellAnalysisProducerKind.CallSite,
                population,
                evaluationPlan: new DiffHistoryEvaluationPlan
                    .ExplicitCheckpoints(
                    [
                        population[1].Cell.Address,
                    ])));

        Assert.Equal("evaluationPlan", error.ParamName);
    }

    [Fact]
    public async Task ApiTypeHistoryRetainsNativeTypeEvidence()
    {
        const string type = "LibraryApiDiffFixture.RemovedType";
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.LibraryApiDiffV2.AssemblyPath()));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiAsync(
                new DiffHistoryApiOperationRequest(
                    ApiHistoryRequest(
                        DiffHistoryApiFindingKind.Type,
                        population,
                        type),
                    new DiffHistoryCountRequest(
                        DiffHistoryCountCohort.ChangedVersions)),
                executor,
                TestContext.Current.CancellationToken);
        var outcome = Assert.IsType<DiffHistorySectionAvailable>(
            envelope.Content);
        DiffHistoryApiFindingDocument<ApiTypeHandle> document =
            Assert.IsType<DiffHistoryDocument.ApiTypes>(
                outcome.Document).Content;

        Assert.Equal(2, executor.Calls);
        Assert.IsType<FindingInspection<ApiTypeHandle>.Complete>(
            document.Evaluations[0].Inspection.Value);
        Assert.Empty(
            Assert.IsType<FindingInspection<ApiTypeHandle>.Complete>(
                document.Evaluations[1].Inspection.Value).Findings);
        Assert.IsType<DiffHistoryApiMemberSubjectResolution.SubjectAbsent>(
            document.Evaluations[1].SubjectResolution);
        Assert.Equal(
            DiffHistoryChangedVersionState.Changed,
            Assert.Single(document.ChangedVersionAssessments).State);
        Assert.False(Assert.Single(document.Transitions).Comparison.IsExact);
        var count = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    outcome.Count);
        Assert.Equal(1, Assert.Single(count.Counts).Value);
        Assert.IsType<InspectionPortableProjection.NonProjectable>(envelope.PortableProjection);
    }

    [Fact]
    public async Task ApiAttributeHistoryRetainsTypeAbsenceTopology()
    {
        const string type = "LibraryApiDiffFixture.RemovedType";
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.LibraryApiDiffV2.AssemblyPath()));

        var outcome = Assert.IsType<DiffHistoryOutcome.Available>(
            await DiffHistoryInspector.InspectApiAsync(
                ApiHistoryRequest(
                    DiffHistoryApiFindingKind.Attributes,
                    population,
                    type),
                executor,
                TestContext.Current.CancellationToken));
        DiffHistoryApiFindingDocument<ApiAttributeHandle> document =
            Assert.IsType<DiffHistoryDocument.ApiAttributes>(
                outcome.Document).Content;

        Assert.Empty(
            Assert.IsType<FindingInspection<ApiAttributeHandle>.Complete>(
                document.Evaluations[0].Inspection.Value).Findings);
        Assert.Equal(
            FindingInspectionAbsenceKind.SubjectAbsent,
            Assert.IsType<FindingInspection<ApiAttributeHandle>.Absent>(
                document.Evaluations[1].Inspection.Value).Kind);
        var complete =
            Assert.IsType<FindingComparison<ApiAttributeHandle>.Complete>(
                Assert.Single(document.Transitions).Comparison.Value);
        Assert.Equal(
            FindingInspectionState.SubjectAbsent,
            complete.Transition.New);
    }

    [Fact]
    public async Task ApiTypeHistoryClassifiesSameIdentityFacetChange()
    {
        const string type =
            "LibraryApiDiffFixture.TypeDefinitionOnly";
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.LibraryApiDiffV2.AssemblyPath()));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiAsync(
                new DiffHistoryApiOperationRequest(
                    ApiHistoryRequest(
                        DiffHistoryApiFindingKind.Type,
                        population,
                        type),
                    new DiffHistoryCountRequest(
                        DiffHistoryCountCohort.ChangedVersions)),
                executor,
                TestContext.Current.CancellationToken);
        var available = Assert.IsType<DiffHistorySectionAvailable>(
            envelope.Content);
        DiffHistoryApiFindingDocument<ApiTypeHandle> document =
            Assert.IsType<DiffHistoryDocument.ApiTypes>(
                available.Document).Content;

        Assert.All(
            document.Evaluations,
            static evaluation => Assert.Single(
                Assert.IsType<FindingInspection<ApiTypeHandle>.Complete>(
                    evaluation.Inspection.Value).Findings));
        var complete = Assert.IsType<
            FindingComparison<ApiTypeHandle>.Complete>(
                Assert.Single(document.Transitions).Comparison.Value);
        var changed = Assert.IsType<
            PairFinding<ApiTypeHandle>.Changed>(
                Assert.Single(complete.Pairs).Value);
        Assert.Contains(
            "byref-like: false -> true",
            changed.Detail,
            StringComparison.Ordinal);
        Assert.Equal(
            DiffHistoryChangedVersionState.Changed,
            Assert.Single(document.ChangedVersionAssessments).State);
        var count = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    available.Count);
        Assert.Equal(1, Assert.Single(count.Counts).Value);
    }

    [Fact]
    public async Task
        ApiAttributeValueChangeDrivesAdaptiveBoundaryAndCount()
    {
        const string type =
            "LibraryApiDiffFixture.TypeDefinitionOnly";
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "1.1.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[2], FixtureCatalog.LibraryApiDiffV2.AssemblyPath()));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiAsync(
                new DiffHistoryApiOperationRequest(
                    ApiHistoryRequest(
                        DiffHistoryApiFindingKind.Attributes,
                        population,
                        type,
                        new DiffHistoryEvaluationPlan.AdaptiveBisect(3),
                        maximumEvaluations: 3),
                    new DiffHistoryCountRequest(
                        DiffHistoryCountCohort.ChangedVersions)),
                executor,
                TestContext.Current.CancellationToken);
        var available = Assert.IsType<DiffHistorySectionAvailable>(
            envelope.Content);
        DiffHistoryApiFindingDocument<ApiAttributeHandle> document =
            Assert.IsType<DiffHistoryDocument.ApiAttributes>(
                available.Document).Content;

        Assert.Equal([0, 2, 1], executor.Positions);
        var terminal =
            Assert.IsType<
                DiffHistoryTerminalOutcome.BoundariesResolved>(
                    document.TerminalOutcome);
        DiffHistoryInterval boundary =
            Assert.Single(terminal.Boundaries);
        Assert.Equal(1, boundary.Source.Position);
        Assert.Equal(2, boundary.Destination.Position);
        DiffHistoryTransition<ApiAttributeHandle> transition =
            Assert.Single(
                document.Transitions,
                static value => !value.Comparison.IsExact);
        var complete = Assert.IsType<
            FindingComparison<ApiAttributeHandle>.Complete>(
                transition.Comparison.Value);
        var changed = Assert.IsType<
            PairFinding<ApiAttributeHandle>.Changed>(
                Assert.Single(
                    complete.Pairs,
                    static pair => pair.Kind == PairKind.Changed).Value);
        Assert.Contains(
            "before",
            changed.Detail,
            StringComparison.Ordinal);
        Assert.Contains(
            "after",
            changed.Detail,
            StringComparison.Ordinal);
        Assert.Equal(
            [
                DiffHistoryChangedVersionState.Unchanged,
                DiffHistoryChangedVersionState.Changed,
            ],
            document.ChangedVersionAssessments.Select(
                static assessment => assessment.State));
        var count = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    available.Count);
        Assert.Equal(1, Assert.Single(count.Counts).Value);
    }

    [Fact]
    public async Task UnifiedApiMemberHistoryPreservesMemberDocument()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV2.AssemblyPath()));

        var outcome = Assert.IsType<DiffHistoryOutcome.Available>(
            await DiffHistoryInspector.InspectApiAsync(
                ApiHistoryRequest(
                    DiffHistoryApiFindingKind.Members,
                    population,
                    HistoryType),
                executor,
                TestContext.Current.CancellationToken));
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                outcome.Document).Content;

        Assert.False(Assert.Single(document.Transitions).Comparison.IsExact);
        Assert.Equal(
            DiffHistoryChangedVersionState.Changed,
            Assert.Single(document.ChangedVersionAssessments).State);
    }

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
                .Select(static assessment =>
                    assessment.Destination.Position));
        Assert.DoesNotContain(
            population[0].Cell.Address,
            document.ChangedVersionAddresses);
        Assert.Equal(ApiDiffOptions.Default, document.ComparisonOptions);
        Assert.Equal(100, document.MatchAcceptanceThreshold);
        Assert.IsType<
            DiffHistoryTerminalOutcome.FullPopulationCompleted>(
                document.TerminalOutcome);
        Assert.All(
            document.Probes,
            static probe => Assert.Equal(
                DiffHistoryProbePurpose.DenseCensus,
                probe.Purpose));
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
                    [last, first]),
                executor);

        Assert.Equal(2, executor.Calls);
        Assert.Equal([0, 2], executor.Positions);
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
        Assert.IsType<
            DiffHistoryTerminalOutcome.ExplicitCheckpointsCompleted>(
                document.TerminalOutcome);
        var action = Assert.IsType<DiffHistoryNextAction.Probe>(
            Assert.Single(document.NextActions));
        Assert.Same(population[1].Cell.Address, action.Address);
        Assert.Equal(3, action.SelectedAddresses.Length);
    }

    [Fact]
    public async Task
        AdaptiveHistoryLocatesBoundaryAndRetainsTypedDiffAction()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[2], FixtureCatalog.DiffV2.AssemblyPath()),
            (population[4], FixtureCatalog.DiffV2.AssemblyPath()));
        var replay = new DiffHistoryPackageReplayContext(
            sources: ["https://api.nuget.org/v3/index.json"],
            configFile: "/tmp/nuget.config");

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    HistoryType,
                    maximumEvaluations: 8,
                    evaluationPlan:
                        new DiffHistoryEvaluationPlan.AdaptiveBisect(8),
                    replayContext: replay),
                executor);

        Assert.Equal([0, 4, 2, 1], executor.Positions);
        Assert.Equal(8, document.AuthorizedProbeCount);
        Assert.Equal(4, document.UsedProbeCount);
        Assert.Equal(
            [
                DiffHistoryProbePurpose.PopulationStart,
                DiffHistoryProbePurpose.PopulationEnd,
                DiffHistoryProbePurpose.AdaptiveMidpoint,
                DiffHistoryProbePurpose.AdaptiveMidpoint,
            ],
            document.Probes.Select(static probe => probe.Purpose));
        var outcome = Assert.IsType<
            DiffHistoryTerminalOutcome.BoundariesResolved>(
                document.TerminalOutcome);
        DiffHistoryInterval boundary =
            Assert.Single(outcome.Boundaries);
        Assert.Same(population[1].Cell.Address, boundary.Source);
        Assert.Same(population[2].Cell.Address, boundary.Destination);
        var action = Assert.IsType<DiffHistoryNextAction.PairwiseDiff>(
            Assert.Single(document.NextActions));
        Assert.Equal(boundary, action.Boundary);
        Assert.Equal("Contoso.History", action.PackageId);
        Assert.Equal(HistoryType, action.TypeFullName);
        Assert.Null(action.Member);
        Assert.Null(action.SourceAsset);
        Assert.Equal(
            MetadataFindings.MemberDescriptor.Id,
            action.Finding);
        Assert.Equal(Framework, action.TargetContext.RequestedFramework);
        Assert.Same(replay, action.ReplayContext);
        Assert.Same(replay, document.ReplayContext);
    }

    [Fact]
    public async Task AdaptiveHistoryPreservesReversePopulationDirection()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "5.0.0",
                "4.0.0",
                "3.0.0",
                "2.0.0",
                "1.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[2], FixtureCatalog.DiffV2.AssemblyPath()),
            (population[4], FixtureCatalog.DiffV2.AssemblyPath()));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    HistoryType,
                    maximumEvaluations: 8,
                    evaluationPlan:
                        new DiffHistoryEvaluationPlan.AdaptiveBisect(8)),
                executor);

        Assert.Equal([0, 4, 2, 1], executor.Positions);
        DiffHistoryInterval boundary =
            Assert.Single(
                Assert.IsType<
                    DiffHistoryTerminalOutcome.BoundariesResolved>(
                        document.TerminalOutcome).Boundaries);
        Assert.Equal("4.0.0", boundary.Source.NormalizedVersion);
        Assert.Equal("3.0.0", boundary.Destination.NormalizedVersion);
    }

    [Fact]
    public async Task AdaptiveHistoryEvenPopulationUsesEarlierMiddle()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0",
                "6.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[2], FixtureCatalog.DiffV2.AssemblyPath()),
            (population[5], FixtureCatalog.DiffV2.AssemblyPath()));

        await InspectHistoryAsync(
            HistoryRequest(
                population,
                HistoryType,
                maximumEvaluations: 8,
                evaluationPlan:
                    new DiffHistoryEvaluationPlan.AdaptiveBisect(8)),
            executor);

        Assert.Equal([0, 5, 2, 1], executor.Positions);
    }

    [Fact]
    public async Task AdaptiveHistoryEqualEndpointsStopAfterTwoProbes()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[4], FixtureCatalog.DiffV1.AssemblyPath()));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    HistoryType,
                    maximumEvaluations: 8,
                    evaluationPlan:
                        new DiffHistoryEvaluationPlan.AdaptiveBisect(8)),
                executor);

        Assert.Equal([0, 4], executor.Positions);
        Assert.Equal(2, document.UsedProbeCount);
        Assert.IsType<DiffHistoryTerminalOutcome.EqualEndpoints>(
            document.TerminalOutcome);
        Assert.Equal(
            DiffHistoryProbeLearningKind.NoChangeObserved,
            document.Probes[1].Learning.Kind);
        Assert.Empty(document.NextActions);
    }

    [Fact]
    public async Task AdaptiveCountDoesNotBroadenEqualEndpointEvaluation()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[4], FixtureCatalog.DiffV1.AssemblyPath()));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(
                        population,
                        HistoryType,
                        maximumEvaluations: 8,
                        evaluationPlan:
                            new DiffHistoryEvaluationPlan
                                .AdaptiveBisect(8))),
                executor,
                TestContext.Current.CancellationToken);

        Assert.Equal([0, 4], executor.Positions);
        var available =
            Assert.IsType<DiffHistorySectionAvailable>(
                envelope.Content);
        Assert.IsType<
            DiffHistoryTerminalOutcome.EqualEndpoints>(
                Assert.IsType<DiffHistoryDocument.ApiMembers>(
                    available.Document).Content.TerminalOutcome);
        Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    available.Count);
    }

    [Fact]
    public async Task
        AdaptiveBudgetExhaustionRetainsEverySplitChangedInterval()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0",
                "6.0.0",
                "7.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[3],
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[6], FixtureCatalog.DiffV2.AssemblyPath()));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    HistoryType,
                    maximumEvaluations: 3,
                    evaluationPlan:
                        new DiffHistoryEvaluationPlan.AdaptiveBisect(3)),
                executor);

        Assert.Equal([0, 6, 3], executor.Positions);
        var outcome = Assert.IsType<
            DiffHistoryTerminalOutcome.BudgetExhausted>(
                document.TerminalOutcome);
        Assert.Equal(
            [(0, 3), (3, 6)],
            outcome.UnresolvedIntervals.Select(static interval =>
                (interval.Source.Position,
                    interval.Destination.Position)));
        DiffHistoryApiMemberProbe midpoint = document.Probes[2];
        Assert.Equal(
            DiffHistoryProbeLearningKind.ChangedIntervals,
            midpoint.Learning.Kind);
        Assert.Equal(
            [(0, 3), (3, 6)],
            midpoint.Learning.ChangedIntervals.Select(static interval =>
                (interval.Source.Position,
                    interval.Destination.Position)));
        Assert.Empty(document.NextActions);
    }

    [Fact]
    public async Task AdaptiveHistoryBreaksEqualIntervalTiesInPopulationOrder()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0",
                "6.0.0",
                "7.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[3],
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[6], FixtureCatalog.DiffV2.AssemblyPath()));

        await InspectHistoryAsync(
            HistoryRequest(
                population,
                HistoryType,
                maximumEvaluations: 4,
                evaluationPlan:
                    new DiffHistoryEvaluationPlan.AdaptiveBisect(4)),
            executor);

        Assert.Equal([0, 6, 3, 1], executor.Positions);
    }

    [Fact]
    public async Task AdaptiveProbeFailureStopsWithTypedBlockedIntervals()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0");
        byte[] firstImage =
            File.ReadAllBytes(FixtureCatalog.DiffV1.AssemblyPath());
        byte[] lastImage =
            File.ReadAllBytes(FixtureCatalog.DiffV2.AssemblyPath());
        var executor = new SettlementExecutor(execution =>
        {
            int position = execution.Cell.Address.Position;
            if (position == 2)
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
                HistoryRequest(
                    population,
                    HistoryType,
                    maximumEvaluations: 8,
                    evaluationPlan:
                        new DiffHistoryEvaluationPlan.AdaptiveBisect(8)),
                executor);

        Assert.Equal([0, 4, 2], executor.Positions);
        var outcome = Assert.IsType<
            DiffHistoryTerminalOutcome.BlockedByFailure>(
                document.TerminalOutcome);
        Assert.Empty(outcome.UnresolvedIntervals);
        Assert.Equal(2, outcome.BlockedIntervals.Length);
        Assert.Equal(
            DiffHistoryProbeLearningKind.BlockedByFailure,
            document.Probes[2].Learning.Kind);
        Assert.Empty(document.Probes[2].Learning.ChangedIntervals);
        Assert.Same(
            population[2].Cell.Address,
            Assert.Single(outcome.FailedAddresses));
    }

    [Fact]
    public async Task
        AdaptiveHistoryFailureRetainsUnaffectedChangedInterval()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0",
                "6.0.0",
                "7.0.0");
        byte[] firstImage =
            File.ReadAllBytes(FixtureCatalog.DiffV1.AssemblyPath());
        byte[] middleImage =
            File.ReadAllBytes(
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath());
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

            byte[] image = position switch
            {
                0 => firstImage,
                3 => middleImage,
                6 => lastImage,
                _ => throw new InvalidOperationException(
                    $"Unexpected probe at position {position}."),
            };
            CellFixture fixture = population[position];
            return fixture.Realize(
                execution,
                fixture.Content(
                    ($"lib/{Framework}/DiffFixtureSample.dll", image)));
        });

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    HistoryType,
                    maximumEvaluations: 8,
                    evaluationPlan:
                        new DiffHistoryEvaluationPlan.AdaptiveBisect(8)),
                executor);

        Assert.Equal([0, 6, 3, 1], executor.Positions);
        DiffHistoryProbeLearning learning =
            document.Probes[^1].Learning;
        Assert.Equal(
            DiffHistoryProbeLearningKind.BlockedByFailure,
            learning.Kind);
        DiffHistoryInterval learned =
            Assert.Single(learning.ChangedIntervals);
        Assert.Equal(
            (3, 6),
            (learned.Source.Position, learned.Destination.Position));

        var outcome = Assert.IsType<
            DiffHistoryTerminalOutcome.BlockedByFailure>(
                document.TerminalOutcome);
        Assert.Empty(outcome.ResolvedBoundaries);
        DiffHistoryInterval unresolved =
            Assert.Single(outcome.UnresolvedIntervals);
        Assert.Equal(
            (3, 6),
            (unresolved.Source.Position,
                unresolved.Destination.Position));
        Assert.Same(
            population[1].Cell.Address,
            Assert.Single(outcome.FailedAddresses));
        Assert.Equal(2, outcome.BlockedIntervals.Length);
    }

    [Fact]
    public async Task
        AdaptiveHistoryRetainsMultipleResolvedBoundariesAndActions()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0",
                "6.0.0",
                "7.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[2], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[3],
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[4],
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[5],
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[6], FixtureCatalog.DiffV2.AssemblyPath()));

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    HistoryType,
                    maximumEvaluations: 8,
                    evaluationPlan:
                        new DiffHistoryEvaluationPlan.AdaptiveBisect(8)),
                executor);

        Assert.Equal([0, 6, 3, 1, 4, 2, 5], executor.Positions);
        Assert.Equal(8, document.AuthorizedProbeCount);
        Assert.Equal(7, document.UsedProbeCount);
        var outcome = Assert.IsType<
            DiffHistoryTerminalOutcome.BoundariesResolved>(
                document.TerminalOutcome);
        Assert.Equal(
            [(2, 3), (5, 6)],
            outcome.Boundaries.Select(static interval =>
                (interval.Source.Position,
                    interval.Destination.Position)));
        Assert.Equal(
            [(2, 3), (5, 6)],
            document.NextActions
                .Cast<DiffHistoryNextAction.PairwiseDiff>()
                .Select(static action =>
                    (action.Boundary.Source.Position,
                        action.Boundary.Destination.Position)));
    }

    [Fact]
    public async Task
        AdaptiveCountUsesResolvedPrefixWithoutBroadeningUnresolvedRemainder()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0",
                "5.0.0",
                "6.0.0",
                "7.0.0");
        SettlementExecutor CreateExecutor() => Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[2], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[3],
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[4],
                FixtureCatalog.LibraryApiDiffV1.AssemblyPath()),
            (population[6], FixtureCatalog.DiffV2.AssemblyPath()));
        DiffHistoryApiMemberInspectionRequest request =
            HistoryRequest(
                population,
                HistoryType,
                maximumEvaluations: 6,
                evaluationPlan:
                    new DiffHistoryEvaluationPlan.AdaptiveBisect(6));
        RowSelectionIntent<string> headOne =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Head(1)]);
        SettlementExecutor prefixExecutor = CreateExecutor();

        InspectionEnvelope<DiffHistoryOutcome> prefixEnvelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(request, headOne),
                prefixExecutor,
                TestContext.Current.CancellationToken);

        Assert.Equal([0, 6, 3, 1, 4, 2], prefixExecutor.Positions);
        var prefixAvailable =
            Assert.IsType<DiffHistorySectionAvailable>(
                prefixEnvelope.Content);
        var terminal = Assert.IsType<
            DiffHistoryTerminalOutcome.BudgetExhausted>(
                Assert.IsType<DiffHistoryDocument.ApiMembers>(
                    prefixAvailable.Document).Content.TerminalOutcome);
        Assert.Equal(
            (2, 3),
            (
                Assert.Single(terminal.ResolvedBoundaries)
                    .Source.Position,
                terminal.ResolvedBoundaries[0]
                    .Destination.Position));
        Assert.Equal(
            (4, 6),
            (
                Assert.Single(terminal.UnresolvedIntervals)
                    .Source.Position,
                terminal.UnresolvedIntervals[0]
                    .Destination.Position));
        var completed = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    prefixAvailable.Count);
        Assert.Equal(1, Assert.Single(completed.Counts).Value);

        InspectionEnvelope<DiffHistoryOutcome> fullEnvelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(request),
                CreateExecutor(),
                TestContext.Current.CancellationToken);
        var source = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    Assert.IsType<DiffHistorySectionAvailable>(
                        fullEnvelope.Content).Count);
        DiffHistoryChangedVersionCountEvidence evidence =
            Assert.Single(source.Sources).Evidence;
        Assert.Equal(4, evidence.EstablishedAssessmentCount);
        Assert.Equal(
            DiffHistoryChangedVersionState.Unevaluated,
            evidence.FirstUnestablishedAssessment!.State);
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
        var outcome = Assert.IsType<
            DiffHistoryTerminalOutcome.BlockedByFailure>(
                document.TerminalOutcome);
        Assert.Same(
            population[1].Cell.Address,
            Assert.Single(outcome.FailedAddresses));
        Assert.Equal(2, outcome.BlockedIntervals.Length);
    }

    [Fact]
    public async Task
        HistorySingleExplicitFailureHasTypedTerminalOutcome()
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

        DiffHistoryApiMemberDocument document =
            await InspectHistoryAsync(
                HistoryRequest(
                    population,
                    HistoryType,
                    [population[0].Cell.Address]),
                executor);

        var outcome = Assert.IsType<
            DiffHistoryTerminalOutcome.BlockedByFailure>(
                document.TerminalOutcome);
        Assert.Same(
            population[0].Cell.Address,
            Assert.Single(outcome.FailedAddresses));
        Assert.Empty(outcome.BlockedIntervals);
    }

    [Fact]
    public async Task
        HistoryCountReducesChangedVersionRowsOncePerDestination()
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

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(population, HistoryType)),
                executor,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        var completed = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    available.Count);
        SectionCountEntry<DiffHistoryCountCohort> count =
            Assert.Single(completed.Counts);
        Assert.Equal(DiffHistoryCountCohort.ChangedVersions, count.Identity);
        Assert.Equal(2, count.Value);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;
        Assert.Equal(2, document.ChangedVersions.Length);
        Assert.All(
            document.ChangedVersions,
            static row =>
            {
                Assert.Equal(
                    row.Predecessor.Position + 1,
                    row.Destination.Position);
                Assert.NotNull(row.Comparison);
            });
        Assert.Empty(envelope.Diagnostics);
    }

    [Fact]
    public async Task HistoryCountCanEstablishZeroFromCompleteEvidence()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV1.AssemblyPath()));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(population, HistoryType)),
                executor,
                TestContext.Current.CancellationToken);

        var completed = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    Assert.IsType<DiffHistorySectionAvailable>(
                        envelope.Content).Count);
        Assert.Equal(0, Assert.Single(completed.Counts).Value);
    }

    [Fact]
    public async Task
        HistoryCountDegradedSignaturesRemainSourceInsufficient()
    {
        var signature = new BlobBuilder();
        SignatureTypeEncoder fieldType =
            new BlobEncoder(signature).FieldSignature();
        for (int depth = 0;
            depth <= SignatureBlobGuard.DefaultMaxDepth;
            depth++)
        {
            fieldType = fieldType.SZArray();
        }
        fieldType.Object();
        byte[] image =
            AssemblyContextApiComparisonQueryTests
                .BuildTypedApiSurfaceImage(
                    1,
                    "SignatureComparison",
                    signature.ToArray());
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.DegradedHistory",
                "1.0.0",
                "2.0.0");
        IPackageContent[] contents =
        [
            population[0].Content(
                ($"lib/{Framework}/SignatureComparison.dll", image)),
            population[1].Content(
                ($"lib/{Framework}/SignatureComparison.dll", image)),
        ];
        var executor = new SettlementExecutor(execution =>
        {
            int position = execution.Cell.Address.Position;
            return population[position].Realize(
                execution,
                contents[position]);
        });
        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(
                        population,
                        "ComparisonBudgetTypes.Type0")),
                executor,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;
        Assert.All(
            document.Evaluations,
            static evaluation =>
            {
                var complete = Assert.IsType<
                    FindingInspection<ApiMemberHandle>.Complete>(
                        evaluation.Inspection.Value);
                Assert.Equal(
                    SignatureDecodeStatus.Degraded,
                    Assert.Single(complete.Findings)
                        .Payload.Member.SignatureDecodeStatus);
            });
        DiffHistoryChangedVersionAssessment<ApiMemberHandle> assessment =
            Assert.Single(document.ChangedVersionAssessments);
        Assert.Equal(
            DiffHistoryChangedVersionState.Failed,
            assessment.State);
        Assert.IsType<FindingComparison<ApiMemberHandle>.Failed>(
            assessment.Comparison!.Value);
        var terminal = Assert.IsType<
            DiffHistoryTerminalOutcome.BlockedByFailure>(
                document.TerminalOutcome);
        Assert.Empty(terminal.FailedAddresses);
        Assert.Single(terminal.BlockedIntervals);
        var source = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    available.Count);
        Assert.Equal(
            DiffHistoryChangedVersionState.Failed,
            Assert.Single(source.Sources)
                .Evidence.FirstUnestablishedAssessment!.State);
        Assert.Contains(
            envelope.Diagnostics,
            static diagnostic =>
                diagnostic.Code
                    == "diff-history.count-source-insufficient");
    }

    [Fact]
    public async Task
        HistoryCountPotentiallyCoveringFailuresRemainSourceInsufficient()
    {
        byte[] image =
            AssemblyContextStructuralCloneRetrievalQueryTests
                .BuildMalformedTypeNameAssembly(
                    malformedTypes: 1);
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.IncompleteHistory",
                "1.0.0",
                "2.0.0");
        IPackageContent[] contents =
        [
            population[0].Content(
                ($"lib/{Framework}/IncompleteHistory.dll", image)),
            population[1].Content(
                ($"lib/{Framework}/IncompleteHistory.dll", image)),
        ];
        var executor = new SettlementExecutor(execution =>
        {
            int position = execution.Cell.Address.Position;
            return population[position].Realize(
                execution,
                contents[position]);
        });

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(
                        population,
                        "N.Fixture")),
                executor,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;
        Assert.All(
            document.Evaluations,
            static evaluation =>
            {
                Assert.IsType<
                    FindingInspection<ApiMemberHandle>.Complete>(
                        evaluation.Inspection.Value);
                Assert.Contains(
                    evaluation.Participants,
                    static participant =>
                        participant
                            is DiffHistoryApiParticipantEvidence
                                .Available
                            {
                                InspectionFailures.IsEmpty: false,
                            });
            });
        DiffHistoryChangedVersionAssessment<ApiMemberHandle> assessment =
            Assert.Single(document.ChangedVersionAssessments);
        Assert.Equal(
            DiffHistoryChangedVersionState.Failed,
            assessment.State);
        Assert.IsType<FindingComparison<ApiMemberHandle>.Failed>(
            assessment.Comparison!.Value);
        var source = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    available.Count);
        Assert.Equal(
            DiffHistoryChangedVersionState.Failed,
            Assert.Single(source.Sources)
                .Evidence.FirstUnestablishedAssessment!.State);
    }

    [Fact]
    public async Task
        HistoryCountIgnoresUnrelatedParticipantFailures()
    {
        byte[] image =
            AssemblyContextApiComparisonQueryTests
                .BuildTypedApiSurfaceImage(
                    typeCount: 1,
                    assemblyName: "UnrelatedFailure");
        byte[] dependency =
            AssemblyContextApiComparisonQueryTests
                .BuildMalformedConstraintDependency();
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.UnrelatedConstraint",
                "1.0.0",
                "2.0.0");
        IPackageContent[] contents =
        [
            population[0].Content(
                ($"lib/{Framework}/UnrelatedFailure.dll", image),
                ($"lib/{Framework}/Missing.dll", dependency)),
            population[1].Content(
                ($"lib/{Framework}/UnrelatedFailure.dll", image),
                ($"lib/{Framework}/Missing.dll", dependency)),
        ];
        var executor = new SettlementExecutor(execution =>
        {
            int position = execution.Cell.Address.Position;
            return population[position].Realize(
                execution,
                contents[position]);
        });

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(
                        population,
                        "ComparisonBudgetTypes.Type0")),
                executor,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;
        Assert.All(
            document.Evaluations,
            static evaluation =>
            {
                Assert.IsType<
                    FindingInspection<ApiMemberHandle>.Complete>(
                        evaluation.Inspection.Value);
                Assert.Contains(
                    evaluation.Participants,
                    static participant =>
                        participant
                            is DiffHistoryApiParticipantEvidence
                                .Available
                            {
                                InspectionFailures.IsEmpty: false,
                            });
            });
        Assert.Equal(
            DiffHistoryChangedVersionState.Unchanged,
            Assert.Single(document.ChangedVersionAssessments).State);
        var completed = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    available.Count);
        Assert.Equal(0, Assert.Single(completed.Counts).Value);
    }

    [Fact]
    public async Task
        HistoryCountSelectedConstraintFailureRemainsSourceInsufficient()
    {
        byte[] image =
            AssemblyContextApiComparisonQueryTests
                .BuildMissingConstraintConsumer();
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.SelectedConstraint",
                "1.0.0",
                "2.0.0");
        IPackageContent[] contents =
        [
            population[0].Content(
                ($"lib/{Framework}/MissingConstraintConsumer.dll",
                    image)),
            population[1].Content(
                ($"lib/{Framework}/MissingConstraintConsumer.dll",
                    image)),
        ];
        var executor = new SettlementExecutor(execution =>
        {
            int position = execution.Cell.Address.Position;
            return population[position].Realize(
                execution,
                contents[position]);
        });

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(
                        population,
                        "N.Consumer`1")),
                executor,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;
        Assert.All(
            document.Evaluations,
            static evaluation =>
            {
                Assert.IsType<
                    FindingInspection<ApiMemberHandle>.Complete>(
                        evaluation.Inspection.Value);
                var participant =
                    Assert.IsType<
                        DiffHistoryApiParticipantEvidence.Available>(
                            Assert.Single(evaluation.Participants));
                Assert.Contains(
                    participant.InspectionFailures,
                    static failure =>
                        failure.Operation
                            == ApiSurface.ConstraintResolutionOperation);
            });
        DiffHistoryChangedVersionAssessment<ApiMemberHandle> assessment =
            Assert.Single(document.ChangedVersionAssessments);
        Assert.Equal(
            DiffHistoryChangedVersionState.Failed,
            assessment.State);
        Assert.IsType<FindingComparison<ApiMemberHandle>.Failed>(
            assessment.Comparison!.Value);
        Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    available.Count);
    }

    [Fact]
    public async Task
        HistoryCountRetainsSparseFailureBesideUsableDocument()
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

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(
                        population,
                        HistoryType,
                        [
                            population[0].Cell.Address,
                            population[2].Cell.Address,
                        ])),
                executor,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;
        Assert.Single(document.Transitions);
        var failure = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    available.Count);
        DiffHistoryChangedVersionCountEvidence evidence =
            Assert.Single(failure.Sources).Evidence;
        Assert.Equal(0, evidence.EstablishedAssessmentCount);
        Assert.Equal(
            DiffHistoryChangedVersionState.Unevaluated,
            evidence.FirstUnestablishedAssessment!.State);
        Assert.Contains(
            envelope.Diagnostics,
            static diagnostic =>
                diagnostic.Code
                    == "diff-history.count-source-insufficient");
    }

    [Fact]
    public async Task
        HistoryTailCountRequiresCompleteSparseSuffixEvidence()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV2.AssemblyPath()));
        RowSelectionIntent<string> tailOne =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Tail(1)]);

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(
                        population,
                        HistoryType,
                        [
                            population[0].Cell.Address,
                            population[1].Cell.Address,
                        ]),
                    tailOne),
                executor,
                TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;
        Assert.Equal(
            [
                DiffHistoryChangedVersionState.Changed,
                DiffHistoryChangedVersionState.Unevaluated,
            ],
            document.ChangedVersionAssessments
                .Select(static assessment => assessment.State));
        var failure = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    available.Count);
        DiffHistoryChangedVersionCountEvidence evidence =
            Assert.Single(failure.Sources).Evidence;
        Assert.Equal(1, evidence.EstablishedAssessmentCount);
        Assert.Null(evidence.RequiredChangedVersionPrefix);
        Assert.Equal(
            DiffHistoryChangedVersionState.Unevaluated,
            evidence.FirstUnestablishedAssessment!.State);
    }

    [Fact]
    public async Task
        HistoryCountUsesProvenComposedPrefixesButNotEarlierUnknowns()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0",
                "3.0.0",
                "4.0.0");
        byte[] firstImage =
            File.ReadAllBytes(FixtureCatalog.DiffV1.AssemblyPath());
        byte[] secondImage =
            File.ReadAllBytes(FixtureCatalog.DiffV2.AssemblyPath());
        var lateFailure = new SettlementExecutor(execution =>
        {
            int position = execution.Cell.Address.Position;
            if (position == 3)
            {
                return new PackageHouseSettlement.ResourceFree(
                    new PackageHouseResult.Rejected(
                        new PackageHouseEvidence(execution.Request),
                        Reason("Late fixture rejection.")));
            }

            CellFixture fixture = population[position];
            return fixture.Realize(
                execution,
                fixture.Content(
                    ($"lib/{Framework}/DiffFixtureSample.dll",
                        position % 2 == 0
                            ? firstImage
                            : secondImage)));
        });
        RowSelectionIntent<string> headOne =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Head(1)]);
        RowSelectionIntent<string> windowOne =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Window(1, 1)]);
        RowSelectionIntent<string> identityThenHeadOne =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>
                        .Window(null, null),
                    RowSelectionIntentOperation<string>.Head(1),
                ]);
        RowSelectionIntent<string> headTwoThenHeadOne =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Head(2),
                    RowSelectionIntentOperation<string>.Head(1),
                ]);
        RowSelectionIntent<string> suffixThenHeadOne =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>
                        .Window(2, null),
                    RowSelectionIntentOperation<string>.Head(1),
                ]);
        RowSelectionIntent<string> tailOne =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Tail(1)]);
        RowSelectionIntent<string> headTwoThenTailOne =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Head(2),
                    RowSelectionIntentOperation<string>.Tail(1),
                ]);

        foreach (RowSelectionIntent<string> selection
            in new[]
            {
                headOne,
                windowOne,
                identityThenHeadOne,
                headTwoThenHeadOne,
                suffixThenHeadOne,
                headTwoThenTailOne,
            })
        {
            InspectionEnvelope<DiffHistoryOutcome> prefixEnvelope =
                await DiffHistoryInspection.InspectApiMembersAsync(
                    CountRequest(
                        HistoryRequest(population, HistoryType),
                        selection),
                    lateFailure,
                    TestContext.Current.CancellationToken);

            var prefixCompleted = Assert.IsType<
                SectionCountOutcome<
                    DiffHistoryCountCohort,
                    DiffHistoryChangedVersionCountEvidence>.Completed>(
                        Assert.IsType<DiffHistorySectionAvailable>(
                            prefixEnvelope.Content).Count);
            Assert.Equal(1, Assert.Single(prefixCompleted.Counts).Value);
        }

        RowSelectionIntent<string> boundedStrictFailure =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Head(1),
                    RowSelectionIntentOperation<string>.Window(1, 2),
                ]);
        InspectionEnvelope<DiffHistoryOutcome> semanticEnvelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(population, HistoryType),
                    boundedStrictFailure),
                lateFailure,
                TestContext.Current.CancellationToken);
        var semantic = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Semantic>(
                    Assert.IsType<DiffHistorySectionAvailable>(
                        semanticEnvelope.Content).Count);
        Assert.Equal(2, semantic.StageNumber);
        Assert.Equal(2, semantic.RequiredPosition);
        Assert.Equal(1, semantic.AvailableCount);

        RowSelectionIntent<string> unboundedSuffix =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Window(2, null)]);
        foreach (RowSelectionIntent<string> selection
            in new[] { unboundedSuffix, tailOne })
        {
            InspectionEnvelope<DiffHistoryOutcome> unboundedEnvelope =
                await DiffHistoryInspection.InspectApiMembersAsync(
                    CountRequest(
                        HistoryRequest(population, HistoryType),
                        selection),
                    lateFailure,
                    TestContext.Current.CancellationToken);
            var unbounded = Assert.IsType<
                SectionCountOutcome<
                    DiffHistoryCountCohort,
                    DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                        Assert.IsType<DiffHistorySectionAvailable>(
                            unboundedEnvelope.Content).Count);
            Assert.Null(
                Assert.Single(unbounded.Sources)
                    .Evidence.RequiredChangedVersionPrefix);
        }

        var earlyFailure = new SettlementExecutor(execution =>
        {
            int position = execution.Cell.Address.Position;
            if (position == 1)
            {
                return new PackageHouseSettlement.ResourceFree(
                    new PackageHouseResult.Rejected(
                        new PackageHouseEvidence(execution.Request),
                        Reason("Early fixture rejection.")));
            }

            CellFixture fixture = population[position];
            return fixture.Realize(
                execution,
                fixture.Content(
                    ($"lib/{Framework}/DiffFixtureSample.dll",
                        position == 0 ? firstImage : secondImage)));
        });

        InspectionEnvelope<DiffHistoryOutcome> blockedEnvelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(population, HistoryType),
                    headOne),
                earlyFailure,
                TestContext.Current.CancellationToken);

        var blocked = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    Assert.IsType<DiffHistorySectionAvailable>(
                        blockedEnvelope.Content).Count);
        Assert.Equal(
            1,
            Assert.Single(blocked.Sources)
                .Evidence.RequiredChangedVersionPrefix);
    }

    [Fact]
    public async Task
        HistoryCountPreservesStrictSelectionFailureWithoutScalar()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0",
                "2.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()),
            (population[1], FixtureCatalog.DiffV2.AssemblyPath()));
        RowSelectionIntent<string> window =
            RowSelectionIntent<string>.Create(
                [RowSelectionIntentOperation<string>.Window(2, 3)]);

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(population, HistoryType),
                    window),
                executor,
                TestContext.Current.CancellationToken);

        var semantic = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Semantic>(
                    Assert.IsType<DiffHistorySectionAvailable>(
                        envelope.Content).Count);
        Assert.Equal(1, semantic.StageNumber);
        Assert.Equal(3, semantic.RequiredPosition);
        Assert.Equal(1, semantic.AvailableCount);
        Assert.Contains(
            envelope.Diagnostics,
            static diagnostic =>
                diagnostic.Code
                    == "diff-history.count-selection-failure");
    }

    [Fact]
    public void HistoryCountRejectsRankingBeforeAcquisition()
    {
        RowSelectionIntent<string> ranked =
            RowSelectionIntent<string>.Create(
                [
                    RowSelectionIntentOperation<string>.Top(
                        1,
                        "change"),
                ]);

        Assert.Throws<ArgumentException>(
            () => new DiffHistoryCountRequest(
                DiffHistoryCountCohort.ChangedVersions,
                ranked));
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

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(population, HistoryType)),
                executor,
                TestContext.Current.CancellationToken);
        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;

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
        var countFailure = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    available.Count);
        Assert.Equal(
            DiffHistoryChangedVersionState.Inapplicable,
            Assert.Single(countFailure.Sources)
                .Evidence.FirstUnestablishedAssessment!.State);
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

        var share = Assert.IsType<InspectionPortableProjection.NonProjectable>(
            envelope.PortableProjection);
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
    [Trait("Speed", "Slow")]
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

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(
                        population,
                        "System.Text.Json.JsonSerializer",
                        framework: "net8.0")),
                executor,
                TestContext.Current.CancellationToken);
        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;

        Assert.Equal(3, executor.Calls);
        Assert.All(
            document.Evaluations,
            evaluation =>
                Assert.IsType<
                    FindingInspection<ApiMemberHandle>.Complete>(
                        evaluation.Inspection.Value));
        Assert.Equal(
            [
                DiffHistoryChangedVersionState.Changed,
                DiffHistoryChangedVersionState.Changed,
            ],
            document.ChangedVersionAssessments
                .Select(static assessment => assessment.State));
        var completed = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    available.Count);
        Assert.Equal(2, Assert.Single(completed.Counts).Value);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task HistoryCountRecordsPinnedUnchangedPackageWitness()
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

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(
                        population,
                        "System.Text.Json.JsonEncodedText",
                        framework: "net8.0")),
                executor,
                TestContext.Current.CancellationToken);
        var available =
            Assert.IsType<DiffHistorySectionAvailable>(envelope.Content);
        DiffHistoryApiMemberDocument document =
            Assert.IsType<DiffHistoryDocument.ApiMembers>(
                available.Document).Content;

        Assert.Equal(
            [
                DiffHistoryChangedVersionState.Unchanged,
                DiffHistoryChangedVersionState.Unchanged,
            ],
            document.ChangedVersionAssessments
                .Select(static assessment => assessment.State));
        var completed = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed>(
                    available.Count);
        Assert.Equal(0, Assert.Single(completed.Counts).Value);
    }

    [Fact]
    public async Task HistoryCountRequiresAtLeastOneTransition()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0");
        SettlementExecutor executor = Executor(
            (population[0], FixtureCatalog.DiffV1.AssemblyPath()));

        InspectionEnvelope<DiffHistoryOutcome> envelope =
            await DiffHistoryInspection.InspectApiMembersAsync(
                CountRequest(
                    HistoryRequest(population, HistoryType)),
                executor,
                TestContext.Current.CancellationToken);

        var failure = Assert.IsType<
            SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount>(
                    Assert.IsType<DiffHistorySectionAvailable>(
                        envelope.Content).Count);
        DiffHistoryChangedVersionCountEvidence evidence =
            Assert.Single(failure.Sources).Evidence;
        Assert.Equal(0, evidence.TotalAssessmentCount);
        Assert.Null(evidence.FirstUnestablishedAssessment);
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
                [
                    population[0].Cell.Address,
                    population[0].Cell.Address,
                ]));
        Assert.Throws<ArgumentException>(
            () => HistoryRequest(
                population,
                HistoryType,
                maximumEvaluations: 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DiffHistoryEvaluationPlan.AdaptiveBisect(1));
        ImmutableArray<CellFixture> single =
            CellFixture.CreatePopulation(
                "Contoso.History",
                "1.0.0");
        Assert.Throws<ArgumentException>(
            () => HistoryRequest(
                single,
                HistoryType,
                evaluationPlan:
                    new DiffHistoryEvaluationPlan.AdaptiveBisect(2)));
        Assert.Throws<ArgumentException>(
            () => new DiffHistoryPackageReplayContext(
                configFile: " "));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DiffHistoryApiMemberInspectionRequest(
                population[0].Cell.Population,
                new DiffHistoryEvaluationPlan.FullPopulation(),
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
        ApiSurfaceProjectionLimits? projectionLimits = null,
        DiffHistoryEvaluationPlan? evaluationPlan = null,
        DiffHistoryPackageReplayContext? replayContext = null) =>
        new(
            population[0].Cell.Population,
            evaluationPlan
                ?? (selection is null
                    ? new DiffHistoryEvaluationPlan.FullPopulation()
                    : new DiffHistoryEvaluationPlan
                        .ExplicitCheckpoints(selection)),
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
                    projectionLimits),
            replayContext: replayContext);

    static DiffHistoryApiInspectionRequest ApiHistoryRequest(
        DiffHistoryApiFindingKind finding,
        ImmutableArray<CellFixture> population,
        string typeFullName,
        DiffHistoryEvaluationPlan? evaluationPlan = null,
        int maximumEvaluations = 16,
        MemberTargetSelector? member = null) =>
        new(
            finding,
            population[0].Cell.Population,
            evaluationPlan
                ?? new DiffHistoryEvaluationPlan.FullPopulation(),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(Framework),
            new DiffHistoryEvaluationLimits(maximumEvaluations),
            new PackageVersionCellWorkspaceLimits(
                16,
                16_000_000,
                32_000_000),
            DateTimeOffset.UtcNow.AddMinutes(1),
            ApiRequest(typeFullName),
            member: member);

    static DiffHistoryAnalysisInspectionRequest AnalysisHistoryRequest(
        PackageVersionCellAnalysisProducerKind finding,
        ImmutableArray<CellFixture> population,
        string member = OrdinalMember,
        string type = OrdinalType,
        DiffHistoryEvaluationPlan? evaluationPlan = null,
        int maximumEvaluations = 16) =>
        new(
            finding,
            population[0].Cell.Population,
            evaluationPlan
                ?? new DiffHistoryEvaluationPlan.FullPopulation(),
            PackageHouseOperation.Create(
                PackageHouseOperationProfile.Realize),
            PackageHouseTargetContext.Exact(Framework),
            new DiffHistoryEvaluationLimits(maximumEvaluations),
            AnalysisLimits(),
            DateTimeOffset.UtcNow.AddMinutes(1),
            new PackageVersionCellMemberSelector(
                type,
                member),
            new FindingSubject(
                $"history:{type}:{member}",
                $"{type}.{member}"));

    static DiffHistoryApiMemberOperationRequest CountRequest(
        DiffHistoryApiMemberInspectionRequest inspection,
        RowSelectionIntent<string>? rowSelection = null) =>
        new(
            inspection,
            new DiffHistoryCountRequest(
                DiffHistoryCountCohort.ChangedVersions,
                rowSelection));

}
