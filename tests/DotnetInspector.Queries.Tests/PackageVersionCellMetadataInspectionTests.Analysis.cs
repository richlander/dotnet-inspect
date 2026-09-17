using System.Collections;
using System.Collections.Immutable;
using System.Reflection;

using DotnetInspector.Fixtures;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed partial class PackageVersionCellMetadataInspectionTests
{
    const string OrdinalType =
        "MetadataCorrespondenceFixture.OrdinalSelection";
    const string OrdinalMember = "Pick:1";
    const string CorrespondenceAssembly =
        "ILInspector.Metadata.ApiDeclarationCorrespondence.dll";

    [Fact]
    public async Task BaselineIssuesReceiptAndAnalyzesExactMember()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        CellFixture source = At(population, "1.0.0");
        SettlementExecutor executor = Executor(
            (source, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));
        var findingSubject = new FindingSubject(
            "history:ordinal-pick-int",
            "OrdinalSelection.Pick(int)");

        var outcome = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>>
            .Available>(
                await PackageVersionCellAnalysisInspector
                    .InspectBaselineAllocationsAsync(
                        BaselineRequest(source, findingSubject),
                        executor,
                        TestContext.Current.CancellationToken));
        var result = Assert.IsType<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>
                .Evaluated>(outcome.Result);

        Assert.Equal(1, executor.Calls);
        Assert.Single(outcome.Executions);
        Assert.Equal(
            ApiCoordinateSourceSelectionStatus.Selected,
            result.Selection.Status);
        Assert.Same(findingSubject, result.Receipt.FindingSubject);
        Assert.Equal("coordinate.sample", result.Receipt.Package.PackageId);
        Assert.Equal("1.0.0", result.Receipt.Package.Version);
        Assert.Equal(
            $"lib/{Framework}/{CorrespondenceAssembly}",
            result.Receipt.Asset.Path);
        Assert.Equal(
            CorrespondenceAssembly[..^4],
            result.Receipt.Assembly.Name);
        Assert.Equal(
            OrdinalType,
            result.Receipt.DeclaringType.ToEscapedFullName());
        Assert.Contains(
            "int",
            result.Receipt.Member.CanonicalSignature,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ApiDeclarationKind.Method,
            result.Receipt.DeclarationKind);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            result.Validation.Status);
        Assert.NotNull(result.Finding.Resolution);
        Assert.IsType<FindingInspection<AllocationOccurrence>.Complete>(
            result.Finding.Inspection.Value);
        Assert.Null(
            typeof(PackageVersionCellBaselineAnalysisResult<
                    AllocationOccurrence>.Evaluated)
                .GetProperty("Relationship"));
    }

    [Fact]
    public async Task BaselineProducerEntrypointsPreserveFindingTypes()
    {
        CellFixture source = CellFixture.Create(
            "coordinate.sample",
            "1.0.0");
        SettlementExecutor executor = Executor(
            (source, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));
        PackageVersionCellBaselineAnalysisRequest request =
            BaselineRequest(
                source,
                new(
                    "history:ordinal-pick-int",
                    "OrdinalSelection.Pick(int)"));

        var calls = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellBaselineAnalysisResult<DirectCall>>.Available>(
                await PackageVersionCellAnalysisInspector
                    .InspectBaselineCallSitesAsync(
                        request,
                        executor,
                        TestContext.Current.CancellationToken));
        var unsafety = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellBaselineAnalysisResult<UnsafetyOccurrence>>
            .Available>(
                await PackageVersionCellAnalysisInspector
                    .InspectBaselineUnsafetyAsync(
                        request,
                        executor,
                        TestContext.Current.CancellationToken));

        Assert.Equal(2, executor.Calls);
        Assert.Equal(
            PackageVersionCellAnalysisProducerKind.CallSite,
            Assert.IsType<PackageVersionCellBaselineAnalysisResult<
                DirectCall>.Evaluated>(calls.Result).Producer);
        Assert.Equal(
            PackageVersionCellAnalysisProducerKind.Unsafety,
            Assert.IsType<PackageVersionCellBaselineAnalysisResult<
                UnsafetyOccurrence>.Evaluated>(unsafety.Result).Producer);
    }

    [Fact]
    public async Task
        CheckpointBindsReceiptWithoutReplayingDestinationOrdinal()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        CellFixture source = At(population, "1.0.0");
        CellFixture destination = At(population, "2.0.0");
        SettlementExecutor executor = Executor(
            (source, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()),
            (destination, FixtureCatalog.MetadataApiCorrespondencePair
                .NewAssemblyPath()));
        var findingSubject = new FindingSubject(
            "history:ordinal-pick-int",
            "OrdinalSelection.Pick(int)");
        var baseline = Assert.IsType<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>
                .Evaluated>(
                Assert.IsType<PackageVersionCellAnalysisOutcome<
                    PackageVersionCellBaselineAnalysisResult<
                        AllocationOccurrence>>.Available>(
                            await PackageVersionCellAnalysisInspector
                                .InspectBaselineAllocationsAsync(
                                    BaselineRequest(
                                        source,
                                        findingSubject),
                                    executor,
                                    TestContext.Current.CancellationToken))
                    .Result);

        PackageVersionCellCheckpointAnalysisRequest request =
            CheckpointRequest(
                source,
                destination,
                baseline.Receipt);
        var first = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellCheckpointAnalysisResult<AllocationOccurrence>>
            .Available>(
                await PackageVersionCellAnalysisInspector
                    .InspectCheckpointAllocationsAsync(
                        request,
                        executor,
                        TestContext.Current.CancellationToken));
        var second = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellCheckpointAnalysisResult<AllocationOccurrence>>
            .Available>(
                await PackageVersionCellAnalysisInspector
                    .InspectCheckpointAllocationsAsync(
                        request,
                        executor,
                        TestContext.Current.CancellationToken));

        Assert.Equal(5, executor.Calls);
        foreach (PackageVersionCellCheckpointAnalysisResult<
            AllocationOccurrence> result in new[]
            {
                first.Result,
                second.Result,
            })
        {
            Assert.Equal(
                PackageVersionCellSourceBindingStatus.Exact,
                result.SourceBinding.Status);
            Assert.Equal(
                ApiCoordinateCorrespondenceStatus.Exact,
                result.Relationship!.Status);
            Assert.Contains(
                "int",
                result.Relationship.Destination!.Member!
                    .CanonicalSignature,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "bool",
                result.Relationship.Destination.Member
                    .CanonicalSignature,
                StringComparison.OrdinalIgnoreCase);
            Assert.Same(baseline.Receipt, result.Receipt);
            Assert.Same(
                findingSubject,
                result.Receipt.FindingSubject);
            Assert.NotNull(result.Finding.Resolution);
        }
    }

    [Fact]
    public async Task CheckpointAbsenceDoesNotPreventLaterExactEvaluation()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0",
                "3.0.0");
        CellFixture source = At(population, "1.0.0");
        CellFixture absent = At(population, "2.0.0");
        CellFixture exact = At(population, "3.0.0");
        SettlementExecutor executor = Executor(
            (source, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()),
            (absent, FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                .AssemblyPath()),
            (exact, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));
        var baseline = Assert.IsType<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>
                .Evaluated>(
                Assert.IsType<PackageVersionCellAnalysisOutcome<
                    PackageVersionCellBaselineAnalysisResult<
                        AllocationOccurrence>>.Available>(
                            await PackageVersionCellAnalysisInspector
                                .InspectBaselineAllocationsAsync(
                                    BaselineRequest(
                                        source,
                                        new(
                                            "history:ordinal-pick-int",
                                            "OrdinalSelection.Pick(int)")),
                                    executor,
                                    TestContext.Current.CancellationToken))
                    .Result);

        PackageVersionCellCheckpointAnalysisResult<AllocationOccurrence>
            absentResult =
            Assert.IsType<PackageVersionCellAnalysisOutcome<
                PackageVersionCellCheckpointAnalysisResult<
                    AllocationOccurrence>>.Available>(
                    await PackageVersionCellAnalysisInspector
                        .InspectCheckpointAllocationsAsync(
                            CheckpointRequest(
                                source,
                                absent,
                                baseline.Receipt),
                            executor,
                            TestContext.Current.CancellationToken))
                .Result;
        PackageVersionCellCheckpointAnalysisResult<AllocationOccurrence>
            exactResult =
            Assert.IsType<PackageVersionCellAnalysisOutcome<
                PackageVersionCellCheckpointAnalysisResult<
                    AllocationOccurrence>>.Available>(
                    await PackageVersionCellAnalysisInspector
                        .InspectCheckpointAllocationsAsync(
                            CheckpointRequest(
                                source,
                                exact,
                                baseline.Receipt),
                            executor,
                            TestContext.Current.CancellationToken))
                .Result;

        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Absent,
            absentResult.Relationship!.Status);
        Assert.Equal(
            FindingInspectionAbsenceKind.SubjectAbsent,
            Assert.IsType<
                FindingInspection<AllocationOccurrence>.Absent>(
                    absentResult.Finding.Inspection.Value).Kind);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            exactResult.Relationship!.Status);
        Assert.NotNull(exactResult.Finding.Resolution);
        Assert.Same(baseline.Receipt, exactResult.Receipt);
    }

    [Fact]
    public async Task CheckpointRequestRejectsForeignReceiptBeforeExecution()
    {
        ImmutableArray<CellFixture> first =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        ImmutableArray<CellFixture> second =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        CellFixture original = At(first, "1.0.0");
        SettlementExecutor executor = Executor(
            (original, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));
        var baseline = Assert.IsType<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>
                .Evaluated>(
                Assert.IsType<PackageVersionCellAnalysisOutcome<
                    PackageVersionCellBaselineAnalysisResult<
                        AllocationOccurrence>>.Available>(
                            await PackageVersionCellAnalysisInspector
                                .InspectBaselineAllocationsAsync(
                                    BaselineRequest(
                                        original,
                                        new(
                                            "history:ordinal-pick-int",
                                            "OrdinalSelection.Pick(int)")),
                                    executor,
                                    TestContext.Current.CancellationToken))
                    .Result);

        Assert.Throws<ArgumentException>(() =>
            CheckpointRequest(
                At(second, "1.0.0"),
                At(second, "2.0.0"),
                baseline.Receipt));
        Assert.Equal(1, executor.Calls);
    }

    [Fact]
    public async Task PairNoContributionExecutesAndRetainsBothEndpoints()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        CellFixture source = At(population, "1.0.0");
        CellFixture destination = At(population, "2.0.0");
        SettlementExecutor baselineExecutor = Executor(
            (source, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));
        var receipt = Assert.IsType<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>
                .Evaluated>(
                Assert.IsType<PackageVersionCellAnalysisOutcome<
                    PackageVersionCellBaselineAnalysisResult<
                        AllocationOccurrence>>.Available>(
                            await PackageVersionCellAnalysisInspector
                                .InspectBaselineAllocationsAsync(
                                    BaselineRequest(
                                        source,
                                        new(
                                            "history:ordinal-pick-int",
                                            "OrdinalSelection.Pick(int)")),
                                    baselineExecutor,
                                    TestContext.Current.CancellationToken))
                    .Result).Receipt;
        var noContributionExecutor = new SettlementExecutor(
            execution => new PackageHouseSettlement.ResourceFree(
                new PackageHouseResult.Rejected(
                    new PackageHouseEvidence(execution.Request),
                    Reason("Fixture rejection."))));

        var result = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellCheckpointAnalysisResult<AllocationOccurrence>>
            .NoContribution>(
                await PackageVersionCellAnalysisInspector
                    .InspectCheckpointAllocationsAsync(
                        CheckpointRequest(
                            source,
                            destination,
                            receipt),
                        noContributionExecutor,
                        TestContext.Current.CancellationToken));

        Assert.Equal(2, noContributionExecutor.Calls);
        Assert.Equal(2, result.Executions.Length);
        Assert.Equal(2, result.Failures.Length);
        Assert.Equal(
            [
                PackageVersionCellAnalysisEndpointRole.Source,
                PackageVersionCellAnalysisEndpointRole.Destination,
            ],
            result.Failures.Select(failure => failure.Endpoint));
        Assert.All(
            result.Failures,
            failure => Assert.Equal(
                PackageHouseRootNoContributionReason
                    .ResourceFreeSettlement,
                failure.Reason));
        Assert.Null(result.Cleanup);
    }

    [Fact]
    public async Task PinnedAvaloniaMoveRetainsRouteAndAnalyzesTerminalBody()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "Avalonia",
                "11.3.14",
                "12.1.2");
        CellFixture source = At(population, "11.3.14");
        CellFixture destination = At(population, "12.1.2");
        SettlementExecutor executor = PackageExecutor(
            (source, "avalonia.11.3.14.nupkg"),
            (destination, "avalonia.12.1.2.nupkg"));
        var subject = new FindingSubject(
            "history:avalonia-multibinding-ctor",
            "Avalonia.Data.MultiBinding..ctor");
        var baselineRequest = new PackageVersionCellBaselineAnalysisRequest(
            source.AnalysisEndpoint("net8.0"),
            new(
                "Avalonia.Data.MultiBinding",
                ".ctor",
                library: "Avalonia.Markup"),
            subject,
            new(64, 32_000_000, 256_000_000),
            DateTimeOffset.UtcNow.AddMinutes(2));
        var baseline = Assert.IsType<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>
                .Evaluated>(
                Assert.IsType<PackageVersionCellAnalysisOutcome<
                    PackageVersionCellBaselineAnalysisResult<
                        AllocationOccurrence>>.Available>(
                            await PackageVersionCellAnalysisInspector
                                .InspectBaselineAllocationsAsync(
                                    baselineRequest,
                                    executor,
                                    TestContext.Current.CancellationToken))
                    .Result);

        var result = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellCheckpointAnalysisResult<AllocationOccurrence>>
            .Available>(
                await PackageVersionCellAnalysisInspector
                    .InspectCheckpointAllocationsAsync(
                        new(
                            source.AnalysisEndpoint("net8.0"),
                            destination.AnalysisEndpoint("net8.0"),
                            baseline.Receipt,
                            new(64, 32_000_000, 256_000_000),
                            DateTimeOffset.UtcNow.AddMinutes(2)),
                        executor,
                        TestContext.Current.CancellationToken))
            .Result;

        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            result.Relationship!.Status);
        Assert.Equal(
            "Avalonia.Markup",
            result.Relationship.Source.Library.Assembly.Assembly.Name);
        Assert.Equal(
            "Avalonia.Base",
            result.Relationship.Destination!.Library.Assembly.Assembly.Name);
        var resolution = Assert.IsType<
            CoordinateTypeResolutionEvidence.Available>(
                result.Relationship.Resolution);
        Assert.NotEmpty(resolution.Outcome.Hops);
        Assert.Equal(
            "Avalonia.Base",
            result.Finding.Resolution!.ImplementationAssembly!.Name);
        Assert.NotNull(
            result.Finding.Resolution.ImplementationModuleVersionId);
        Assert.NotNull(
            result.Finding.Resolution.ImplementationMethodToken);
    }

    [Fact]
    public async Task PinnedMarkoutBaselineIssuesReceipt()
    {
        CellFixture source = CellFixture.Create(
            "Markout",
            "0.35.2");
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "VersionCell",
            "Markout.dll");
        var executor = new SettlementExecutor(
            execution => source.Realize(
                execution,
                source.Content(
                    ("lib/net10.0/Markout.dll",
                        File.ReadAllBytes(path)))));

        var result = Assert.IsType<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>
                .Evaluated>(
                Assert.IsType<PackageVersionCellAnalysisOutcome<
                    PackageVersionCellBaselineAnalysisResult<
                        AllocationOccurrence>>.Available>(
                            await PackageVersionCellAnalysisInspector
                                .InspectBaselineAllocationsAsync(
                                    new(
                                        source.AnalysisEndpoint("net10.0"),
                                        new(
                                            "Markout.MarkoutWriterOptions",
                                            "MakeReadOnly"),
                                        new(
                                            "history:markout-make-read-only",
                                            "MarkoutWriterOptions.MakeReadOnly"),
                                        AnalysisLimits(),
                                        DateTimeOffset.UtcNow
                                            .AddMinutes(1)),
                                    executor,
                                    TestContext.Current.CancellationToken))
                    .Result);

        Assert.Equal("Markout.dll", result.Receipt.Asset.AssemblyName);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            result.Validation.Status);
        Assert.NotNull(result.Finding.Resolution);
    }

    [Fact]
    public async Task PinnedSystemTextJsonCheckpointBindsReceipt()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "System.Text.Json",
                "9.0.0",
                "10.0.0");
        CellFixture source = At(population, "9.0.0");
        CellFixture destination = At(population, "10.0.0");
        SettlementExecutor executor = PackageExecutor(
            (source, "system.text.json.9.0.0.nupkg"),
            (destination, "system.text.json.10.0.0.nupkg"));
        var baseline = Assert.IsType<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>
                .Evaluated>(
                Assert.IsType<PackageVersionCellAnalysisOutcome<
                    PackageVersionCellBaselineAnalysisResult<
                        AllocationOccurrence>>.Available>(
                            await PackageVersionCellAnalysisInspector
                                .InspectBaselineAllocationsAsync(
                                    new(
                                        source.AnalysisEndpoint("net8.0"),
                                        new(
                                            "System.Text.Json.JsonSerializer",
                                            "Deserialize:1"),
                                        new(
                                            "history:json-deserialize",
                                            "JsonSerializer.Deserialize"),
                                        new(
                                            64,
                                            32_000_000,
                                            256_000_000),
                                        DateTimeOffset.UtcNow
                                            .AddMinutes(2)),
                                    executor,
                                    TestContext.Current.CancellationToken))
                    .Result);

        var result = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellCheckpointAnalysisResult<AllocationOccurrence>>
            .Available>(
                await PackageVersionCellAnalysisInspector
                    .InspectCheckpointAllocationsAsync(
                        new(
                            source.AnalysisEndpoint("net8.0"),
                            destination.AnalysisEndpoint("net8.0"),
                            baseline.Receipt,
                            new(
                                64,
                                32_000_000,
                                256_000_000),
                            DateTimeOffset.UtcNow.AddMinutes(2)),
                        executor,
                        TestContext.Current.CancellationToken))
            .Result;

        Assert.Equal(
            PackageVersionCellSourceBindingStatus.Exact,
            result.SourceBinding.Status);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            result.Relationship!.Status);
        Assert.Same(baseline.Receipt, result.Receipt);
        Assert.NotNull(result.Finding.Resolution);
    }

    [Theory]
    [InlineData("SourceSelection")]
    [InlineData("Correspondence")]
    [InlineData("Analysis")]
    [InlineData("Cleanup")]
    public async Task CancellationAtAnalysisStagePublishesNoOutcome(
        string stageName)
    {
        PackageVersionCellAnalysisExecutionStage cancelledStage =
            Enum.Parse<PackageVersionCellAnalysisExecutionStage>(
                stageName);
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        CellFixture source = At(population, "1.0.0");
        SettlementExecutor executor = Executor(
            (source, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));
        using var cancellation = new CancellationTokenSource();
        bool reached = false;

        OperationCanceledException failure =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => PackageVersionCellAnalysisInspector
                    .InspectBaselineAllocationsWithStageObserverAsync(
                        BaselineRequest(
                            source,
                            new(
                                "history:ordinal-pick-int",
                                "OrdinalSelection.Pick(int)")),
                        executor,
                        stage =>
                        {
                            if (stage == cancelledStage)
                            {
                                reached = true;
                                cancellation.Cancel();
                            }
                        },
                        cancellation.Token));

        Assert.True(reached);
        Assert.Equal(cancellation.Token, failure.CancellationToken);
        Assert.Equal(1, executor.Calls);
        Assert.False(
            PackageVersionCellWorkspaceExceptionEvidence.TryGetCleanup(
                failure,
                out _));
    }

    [Fact]
    public async Task AnalysisCleanupPrecedenceMatchesSharedCellLifecycle()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        CellFixture source = At(population, "1.0.0");
        var available = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>>
            .Available>(
                await PackageVersionCellAnalysisInspector
                    .InspectBaselineAllocationsAsync(
                        BaselineRequest(
                            source,
                            new(
                                "history:ordinal-pick-int",
                                "OrdinalSelection.Pick(int)")),
                        Executor(
                            (source, FixtureCatalog
                                .MetadataApiCorrespondencePair
                                .OldAssemblyPath())),
                        TestContext.Current.CancellationToken));
        var cleanup = new PackageVersionCellWorkspaceCleanupEvidence(
            [
                new(
                    PackageVersionCellWorkspaceCleanupStage.GroupRelease,
                    1),
            ]);
        var workspaceFailure =
            new PackageVersionCellAnalysisOutcome<
                PackageVersionCellBaselineAnalysisResult<
                    AllocationOccurrence>>.WorkspaceFailure(
                        available.Executions,
                        PackageVersionCellAnalysisWorkspaceFailure.Failed(
                            PackageVersionCellAnalysisWorkspaceStage
                                .ScopeRead,
                            ArtifactRootFailure.Absent));

        var cleanupFailure = Assert.IsType<
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellBaselineAnalysisResult<
                    AllocationOccurrence>>.CleanupFailure>(
                        PackageVersionCellAnalysisInspector.Complete(
                            available,
                            cleanup));
        var retainedFailure = Assert.IsType<
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellBaselineAnalysisResult<
                    AllocationOccurrence>>.WorkspaceFailure>(
                        PackageVersionCellAnalysisInspector.Complete(
                            workspaceFailure,
                            cleanup));

        Assert.Same(cleanup, cleanupFailure.Cleanup);
        Assert.Same(
            workspaceFailure.Failure,
            retainedFailure.Failure);
        Assert.Same(cleanup, retainedFailure.Cleanup);
    }

    [Fact]
    public async Task RuntimeSourceLibraryMismatchIsFailedEvaluation()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        CellFixture source = At(population, "1.0.0");
        CellFixture destination = At(population, "2.0.0");
        var baseline = Assert.IsType<
            PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>
                .Evaluated>(
                Assert.IsType<PackageVersionCellAnalysisOutcome<
                    PackageVersionCellBaselineAnalysisResult<
                        AllocationOccurrence>>.Available>(
                            await PackageVersionCellAnalysisInspector
                                .InspectBaselineAllocationsAsync(
                                    BaselineRequest(
                                        source,
                                        new(
                                            "history:ordinal-pick-int",
                                            "OrdinalSelection.Pick(int)")),
                                    Executor(
                                        (source, FixtureCatalog
                                            .MetadataApiCorrespondencePair
                                            .OldAssemblyPath())),
                                    TestContext.Current.CancellationToken))
                    .Result);
        SettlementExecutor mismatched = Executor(
            (source, FixtureCatalog.AnalysisMethodCorrespondenceRuntime
                .AssemblyPath()),
            (destination, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));

        var result = Assert.IsType<PackageVersionCellAnalysisOutcome<
            PackageVersionCellCheckpointAnalysisResult<AllocationOccurrence>>
            .Available>(
                await PackageVersionCellAnalysisInspector
                    .InspectCheckpointAllocationsAsync(
                        CheckpointRequest(
                            source,
                            destination,
                            baseline.Receipt),
                        mismatched,
                        TestContext.Current.CancellationToken))
            .Result;

        Assert.Equal(
            PackageVersionCellSourceBindingStatus.LibraryAbsent,
            result.SourceBinding.Status);
        Assert.Null(result.Relationship);
        Assert.IsType<FindingInspection<AllocationOccurrence>.Failed>(
            result.Finding.Inspection.Value);
        Assert.Equal(2, mismatched.Calls);
    }

    [Fact]
    public async Task AnalysisOutcomePublicAndRuntimeClosureIsResourceFree()
    {
        ImmutableArray<CellFixture> population =
            CellFixture.CreatePopulation(
                "coordinate.sample",
                "1.0.0",
                "2.0.0");
        CellFixture source = At(population, "1.0.0");
        SettlementExecutor executor = Executor(
            (source, FixtureCatalog.MetadataApiCorrespondencePair
                .OldAssemblyPath()));
        object instance =
            await PackageVersionCellAnalysisInspector
                .InspectBaselineAllocationsAsync(
                    BaselineRequest(
                        source,
                        new(
                            "history:ordinal-pick-int",
                            "OrdinalSelection.Pick(int)")),
                    executor,
                    TestContext.Current.CancellationToken);

        Type[] roots =
        [
            typeof(DiffHistoryMemberSourceReceipt),
            typeof(PackageVersionCellSourceBindingEvidence),
            typeof(PackageVersionCellAnalysisFinding<AllocationOccurrence>),
            typeof(PackageVersionCellBaselineAnalysisResult<
                AllocationOccurrence>),
            typeof(PackageVersionCellCheckpointAnalysisResult<
                AllocationOccurrence>),
            typeof(PackageVersionCellAnalysisOutcome<
                PackageVersionCellBaselineAnalysisResult<
                    AllocationOccurrence>>),
            typeof(PackageVersionCellAnalysisOutcome<
                PackageVersionCellCheckpointAnalysisResult<
                    AllocationOccurrence>>),
        ];
        var types = new HashSet<Type>();
        foreach (Type root in roots)
            VisitType(root, types);
        VisitValue(
            instance,
            new HashSet<object>(
                ReferenceEqualityComparer.Instance));
    }

    static PackageVersionCellBaselineAnalysisRequest BaselineRequest(
        CellFixture source,
        FindingSubject subject) =>
        new(
            source.AnalysisEndpoint(),
            new(OrdinalType, OrdinalMember),
            subject,
            AnalysisLimits(),
            DateTimeOffset.UtcNow.AddMinutes(1));

    static PackageVersionCellCheckpointAnalysisRequest CheckpointRequest(
        CellFixture source,
        CellFixture destination,
        DiffHistoryMemberSourceReceipt receipt) =>
        new(
            source.AnalysisEndpoint(),
            destination.AnalysisEndpoint(),
            receipt,
            AnalysisLimits(),
            DateTimeOffset.UtcNow.AddMinutes(1));

    static PackageVersionCellWorkspaceLimits AnalysisLimits() =>
        new(16, 16_000_000, 32_000_000);

    static CellFixture At(
        ImmutableArray<CellFixture> population,
        string version) =>
        Assert.Single(
            population.Where(cell => cell.CellVersion == version));

    static SettlementExecutor Executor(
        params (CellFixture Cell, string AssemblyPath)[] cells)
    {
        Dictionary<string, (CellFixture Cell, string AssemblyPath)> byVersion =
            cells.ToDictionary(cell => cell.Cell.CellVersion);
        return new(execution =>
        {
            string version =
                execution.Cell.Address.Version.ToNormalizedString();
            (CellFixture fixture, string path) = byVersion[version];
            return fixture.Realize(
                execution,
                fixture.Content(
                    ($"lib/{Framework}/{Path.GetFileName(path)}",
                        File.ReadAllBytes(path))));
        });
    }

    static SettlementExecutor PackageExecutor(
        params (CellFixture Cell, string PackageFile)[] cells)
    {
        Dictionary<string, (CellFixture Cell, string PackageFile)> byVersion =
            cells.ToDictionary(cell => cell.Cell.CellVersion);
        return new(execution =>
        {
            string version =
                execution.Cell.Address.Version.ToNormalizedString();
            (CellFixture fixture, string file) = byVersion[version];
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "ApiMatching",
                file);
            return fixture.Realize(
                execution,
                fixture.PackageContent(File.ReadAllBytes(path)));
        });
    }

    static void VisitType(Type candidate, HashSet<Type> seen)
    {
        candidate = Nullable.GetUnderlyingType(candidate) ?? candidate;
        if (candidate.IsGenericParameter || !seen.Add(candidate))
            return;

        AssertAllowed(candidate);
        if (candidate.IsArray)
        {
            VisitType(candidate.GetElementType()!, seen);
            return;
        }
        if (candidate.IsGenericType)
        {
            foreach (Type argument in candidate.GetGenericArguments())
                VisitType(argument, seen);
        }
        if (candidate.IsPrimitive
            || candidate.IsEnum
            || candidate == typeof(string)
            || candidate.Namespace?.StartsWith(
                "System",
                StringComparison.Ordinal) is true)
        {
            return;
        }

        if (candidate.BaseType is { } baseType
            && baseType != typeof(object))
        {
            VisitType(baseType, seen);
        }
        foreach (Type nested in candidate.GetNestedTypes(
            BindingFlags.Public))
        {
            VisitType(CloseNested(candidate, nested), seen);
        }
        foreach (FieldInfo field in candidate.GetFields(
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly))
        {
            if (field.FieldType == typeof(object))
                continue;
            VisitType(field.FieldType, seen);
        }
        foreach (PropertyInfo property in candidate.GetProperties(
            BindingFlags.Instance
            | BindingFlags.Public
            | BindingFlags.DeclaredOnly))
        {
            if (property.PropertyType != typeof(object))
                VisitType(property.PropertyType, seen);
        }
    }

    static void VisitValue(
        object? value,
        HashSet<object> seen)
    {
        if (value is null)
            return;
        Type type = value.GetType();
        AssertAllowed(type);
        if (type.IsPrimitive
            || type.IsEnum
            || value is string
            || value is Type
            || value is MemberInfo)
        {
            return;
        }
        if (!type.IsValueType && !seen.Add(value))
            return;
        if (value is IEnumerable sequence)
        {
            foreach (object? item in sequence)
                VisitValue(item, seen);
            return;
        }

        for (Type? current = type;
            current is not null && current != typeof(object);
            current = current.BaseType)
        {
            foreach (FieldInfo field in current.GetFields(
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly))
            {
                VisitValue(field.GetValue(value), seen);
            }
        }
    }

    static void AssertAllowed(Type type)
    {
        Type[] forbidden =
        [
            typeof(PackageHouseVersionPopulationCell),
            typeof(PackageHouseVersionPopulationCellExecution),
            typeof(InspectionWorkspace),
            typeof(PackageRootBinding),
            typeof(PackageRootRealization),
            typeof(PackageAssemblyContextRealization),
            typeof(PackageAssemblyRoleParticipant),
            typeof(AssemblyContextGroup),
            typeof(AssemblyContextParticipant),
            typeof(ResolvedAssemblyReference),
            typeof(AssemblyAcquisitionRegistration),
            typeof(StructuralSubjectIdentity),
            typeof(CoordinatePackageObservation),
            typeof(ApiCoordinateCorrespondenceResult),
            typeof(IPackageContent),
            typeof(AssemblyImageSnapshot),
            typeof(Stream),
            typeof(Delegate),
            typeof(Exception),
        ];
        Assert.DoesNotContain(
            forbidden,
            blocked => blocked.IsAssignableFrom(type));
        Assert.False(type.IsByRefLike, type.FullName);
        Assert.False(
            typeof(IDisposable).IsAssignableFrom(type),
            type.FullName);
        Assert.False(
            typeof(IAsyncDisposable).IsAssignableFrom(type),
            type.FullName);
    }

    static Type CloseNested(Type owner, Type nested)
    {
        if (!nested.ContainsGenericParameters)
            return nested;
        Type[] arguments = owner.GetGenericArguments();
        return nested.GetGenericTypeDefinition()
            .MakeGenericType(arguments);
    }
}
