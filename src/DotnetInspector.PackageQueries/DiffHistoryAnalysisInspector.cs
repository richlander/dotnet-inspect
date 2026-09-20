using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

static class DiffHistoryAnalysisEngine
{
    public static Task<DiffHistoryOutcome> InspectAsync(
        DiffHistoryAnalysisInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executor);
        return request.Finding switch
        {
            PackageVersionCellAnalysisProducerKind.Allocation =>
                InspectAsync(
                    request,
                    executor,
                    new Producer<AllocationOccurrence>(
                        AnalysisFindings.AllocationDescriptor,
                        PackageVersionCellAnalysisInspector
                            .InspectBaselineAllocationsAsync,
                        PackageVersionCellAnalysisInspector
                            .InspectCheckpointAllocationsAsync,
                        AnalysisFindings.CompareAllocations,
                        static document =>
                            new DiffHistoryDocument.Allocations(document)),
                    cancellationToken),
            PackageVersionCellAnalysisProducerKind.CallSite =>
                InspectAsync(
                    request,
                    executor,
                    new Producer<DirectCall>(
                        AnalysisFindings.CallSiteDescriptor,
                        PackageVersionCellAnalysisInspector
                            .InspectBaselineCallSitesAsync,
                        PackageVersionCellAnalysisInspector
                            .InspectCheckpointCallSitesAsync,
                        AnalysisFindings.CompareCallSites,
                        static document =>
                            new DiffHistoryDocument.CallSites(document)),
                    cancellationToken),
            PackageVersionCellAnalysisProducerKind.Unsafety =>
                InspectAsync(
                    request,
                    executor,
                    new Producer<UnsafetyOccurrence>(
                        AnalysisFindings.UnsafetyDescriptor,
                        PackageVersionCellAnalysisInspector
                            .InspectBaselineUnsafetyAsync,
                        PackageVersionCellAnalysisInspector
                            .InspectCheckpointUnsafetyAsync,
                        AnalysisFindings.CompareUnsafety,
                        static document =>
                            new DiffHistoryDocument.Unsafety(document)),
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }

    static async Task<DiffHistoryOutcome> InspectAsync<T>(
        DiffHistoryAnalysisInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        Producer<T> producer,
        CancellationToken cancellationToken)
        where T : notnull
    {
        PackageVersionAddress sourceAddress =
            request.Population.Vector.Addresses[0];
        PackageHouseVersionPopulationCell sourceCell =
            request.Population.SelectCell(sourceAddress);
        var sourceEndpoint = new PackageVersionCellAnalysisEndpoint(
            sourceCell,
            request.Operation,
            request.TargetContext);
        DiffHistoryMemberSourceReceipt? receipt = null;

        async Task<Point<T>> EvaluateAsync(
            PackageVersionAddress address,
            CancellationToken token)
        {
            if (ReferenceEquals(address, sourceAddress))
            {
                var baselineRequest =
                    new PackageVersionCellBaselineAnalysisRequest(
                        sourceEndpoint,
                        request.Selector,
                        request.FindingSubject,
                        request.WorkspaceLimits,
                        request.WorkspaceDeadline);
                PackageVersionCellAnalysisOutcome<
                    PackageVersionCellBaselineAnalysisResult<T>>
                    baselineOutcome =
                        await producer.Baseline(
                                baselineRequest,
                                executor,
                                token)
                            .ConfigureAwait(false);
                Point<T> point = ProjectBaseline(
                    request,
                    sourceCell,
                    baselineOutcome,
                    producer.Descriptor);
                receipt = point.Receipt;
                return point;
            }

            if (receipt is null)
            {
                throw new InvalidOperationException(
                    "An Analysis checkpoint cannot execute without the source receipt.");
            }

            PackageHouseVersionPopulationCell destinationCell =
                request.Population.SelectCell(address);
            var checkpointRequest =
                new PackageVersionCellCheckpointAnalysisRequest(
                    sourceEndpoint,
                    new(
                        destinationCell,
                        request.Operation,
                        request.TargetContext),
                    receipt,
                    request.WorkspaceLimits,
                    request.WorkspaceDeadline);
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellCheckpointAnalysisResult<T>>
                checkpointOutcome =
                    await producer.Checkpoint(
                            checkpointRequest,
                            executor,
                            token)
                        .ConfigureAwait(false);
            return ProjectCheckpoint(
                request,
                destinationCell,
                checkpointOutcome,
                producer.Descriptor,
                receipt);
        }

        DiffHistoryMethodologyExecution<T, Point<T>> execution =
            await DiffHistoryMethodology.ExecuteAsync<T, Point<T>>(
                    request.Population.Vector,
                    request.EvaluationPlan,
                    request.InitialEvaluationSelection,
                    request.EvaluationLimits,
                    EvaluateAsync,
                    (source, destination) => Compare(
                        request,
                        producer,
                        source,
                        destination),
                    boundary => new DiffHistoryNextAction.PairwiseDiff(
                        boundary,
                        request.Population.Vector.PackageId,
                        (receipt ?? throw new InvalidOperationException(
                            "An Analysis action requires the source receipt."))
                            .Member.TypeFullName,
                        receipt
                            .Member,
                        receipt.Asset,
                        producer.Descriptor.Id,
                        request.Selector.IncludeAll
                            ? ApiSurfaceScope.IncludeAll
                            : ApiSurfaceScope.Public,
                        request.TargetContext,
                        request.ReplayContext),
                    cancellationToken)
                .ConfigureAwait(false);

        Dictionary<int, DiffHistoryAnalysisEvaluation<T>> evaluations =
            execution.Points.ToDictionary(
                static point => point.Address.Position,
                static point => point.Evaluation);
        ImmutableArray<DiffHistoryAnalysisProbe<T>> probes =
        [
            .. execution.Probes.Select(probe =>
                new DiffHistoryAnalysisProbe<T>(
                    probe.Step,
                    probe.Purpose,
                    probe.SelectedInterval,
                    evaluations[probe.Point.Address.Position],
                    probe.Learning)),
        ];
        var document = new DiffHistoryAnalysisDocument<T>(
            request.Finding,
            request.Population.Vector,
            request.Selector,
            request.FindingSubject,
            request.TargetContext,
            request.EvaluationLimits,
            request.EvaluationPlan,
            request.WorkspaceLimits,
            [.. execution.Points.Select(static point => point.Address)],
            [.. execution.Points.Select(static point => point.Evaluation)],
            probes,
            receipt,
            execution.Correlation,
            execution.Transitions,
            execution.ChangedVersionAssessments,
            execution.TerminalOutcome,
            execution.NextActions,
            request.MatchAcceptanceThreshold,
            request.ReplayContext);
        return new DiffHistoryOutcome.Available(producer.Wrap(document));
    }

    static Point<T> ProjectBaseline<T>(
        DiffHistoryAnalysisInspectionRequest request,
        PackageHouseVersionPopulationCell cell,
        PackageVersionCellAnalysisOutcome<
            PackageVersionCellBaselineAnalysisResult<T>> outcome,
        FindingDescriptor descriptor)
        where T : notnull
    {
        FindingVersion version = Version(cell);
        return outcome switch
        {
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellBaselineAnalysisResult<T>>.Available
                {
                    Result:
                        PackageVersionCellBaselineAnalysisResult<T>.Evaluated
                        evaluated,
                } available =>
                CompletedBaseline(
                    cell.Address,
                    version,
                    available.Executions,
                    evaluated),
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellBaselineAnalysisResult<T>>.Available
                {
                    Result:
                        PackageVersionCellBaselineAnalysisResult<T>.Unselected
                        unselected,
                } available =>
                Failed<T>(
                    request,
                    cell.Address,
                    version,
                    DiffHistoryAnalysisEvaluationState.SourceUnselected,
                    available.Executions,
                    descriptor,
                    unselected.Selection.Failure?.Detail
                        ?? $"Source selection completed as {unselected.Selection.Status}.",
                    sourceSelection: unselected.Selection,
                    blocksFurtherEvaluation: true),
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellBaselineAnalysisResult<T>>.NoContribution
                noContribution =>
                Failed<T>(
                    request,
                    cell.Address,
                    version,
                    DiffHistoryAnalysisEvaluationState.NoContribution,
                    noContribution.Executions,
                    descriptor,
                    "The source package cell produced no query contribution.",
                    noContributions: noContribution.Failures,
                    blocksFurtherEvaluation: true),
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellBaselineAnalysisResult<T>>.WorkspaceFailure
                workspaceFailure =>
                Failed<T>(
                    request,
                    cell.Address,
                    version,
                    DiffHistoryAnalysisEvaluationState.WorkspaceFailure,
                    workspaceFailure.Executions,
                    descriptor,
                    $"The source Analysis Workspace failed at {workspaceFailure.Failure.Stage}.",
                    cleanup: workspaceFailure.Cleanup,
                    workspaceFailure: workspaceFailure.Failure,
                    blocksFurtherEvaluation: true),
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellBaselineAnalysisResult<T>>.CleanupFailure
                cleanupFailure =>
                Failed<T>(
                    request,
                    cell.Address,
                    version,
                    DiffHistoryAnalysisEvaluationState.CleanupFailure,
                    cleanupFailure.Executions,
                    descriptor,
                    "The source Analysis Workspace failed to close cleanly.",
                    cleanup: cleanupFailure.Cleanup,
                    blocksFurtherEvaluation: true),
            _ => throw new InvalidOperationException(
                "Unknown baseline Analysis outcome."),
        };
    }

    static Point<T> CompletedBaseline<T>(
        PackageVersionAddress address,
        FindingVersion version,
        ImmutableArray<PackageVersionCellExecutionEvidence> executions,
        PackageVersionCellBaselineAnalysisResult<T>.Evaluated result)
        where T : notnull
    {
        PackageVersionCellAnalysisFinding<T> finding = result.Finding;
        var evaluation = new DiffHistoryAnalysisEvaluation<T>(
            address,
            version,
            DiffHistoryAnalysisEvaluationState.Completed,
            finding.Inspection,
            executions,
            sourceReceipt: result.Receipt,
            sourceSelection: result.Selection,
            sourceValidation: result.Validation,
            bodyResolution: finding.Resolution,
            analysisRootFailure: finding.AnalysisRootFailure);
        return new(
            evaluation,
            result.Receipt,
            BlocksFurtherEvaluation: false);
    }

    static Point<T> ProjectCheckpoint<T>(
        DiffHistoryAnalysisInspectionRequest request,
        PackageHouseVersionPopulationCell cell,
        PackageVersionCellAnalysisOutcome<
            PackageVersionCellCheckpointAnalysisResult<T>> outcome,
        FindingDescriptor descriptor,
        DiffHistoryMemberSourceReceipt receipt)
        where T : notnull
    {
        FindingVersion version = Version(cell);
        return outcome switch
        {
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellCheckpointAnalysisResult<T>>.Available
                available =>
                CompletedCheckpoint(
                    cell.Address,
                    version,
                    available.Executions,
                    available.Result),
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellCheckpointAnalysisResult<T>>.NoContribution
                noContribution =>
                Failed<T>(
                    request,
                    cell.Address,
                    version,
                    DiffHistoryAnalysisEvaluationState.NoContribution,
                    noContribution.Executions,
                    descriptor,
                    "The Analysis checkpoint produced no query contribution.",
                    noContributions: noContribution.Failures,
                    sourceReceipt: receipt),
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellCheckpointAnalysisResult<T>>.WorkspaceFailure
                workspaceFailure =>
                Failed<T>(
                    request,
                    cell.Address,
                    version,
                    DiffHistoryAnalysisEvaluationState.WorkspaceFailure,
                    workspaceFailure.Executions,
                    descriptor,
                    $"The checkpoint Analysis Workspace failed at {workspaceFailure.Failure.Stage}.",
                    cleanup: workspaceFailure.Cleanup,
                    workspaceFailure: workspaceFailure.Failure,
                    sourceReceipt: receipt),
            PackageVersionCellAnalysisOutcome<
                PackageVersionCellCheckpointAnalysisResult<T>>.CleanupFailure
                cleanupFailure =>
                Failed<T>(
                    request,
                    cell.Address,
                    version,
                    DiffHistoryAnalysisEvaluationState.CleanupFailure,
                    cleanupFailure.Executions,
                    descriptor,
                    "The checkpoint Analysis Workspace failed to close cleanly.",
                    cleanup: cleanupFailure.Cleanup,
                    sourceReceipt: receipt),
            _ => throw new InvalidOperationException(
                "Unknown checkpoint Analysis outcome."),
        };
    }

    static Point<T> CompletedCheckpoint<T>(
        PackageVersionAddress address,
        FindingVersion version,
        ImmutableArray<PackageVersionCellExecutionEvidence> executions,
        PackageVersionCellCheckpointAnalysisResult<T> result)
        where T : notnull
    {
        PackageVersionCellAnalysisFinding<T> finding = result.Finding;
        var evaluation = new DiffHistoryAnalysisEvaluation<T>(
            address,
            version,
            DiffHistoryAnalysisEvaluationState.Completed,
            finding.Inspection,
            executions,
            sourceReceipt: result.Receipt,
            sourceBinding: result.SourceBinding,
            relationship: result.Relationship,
            bodyResolution: finding.Resolution,
            analysisRootFailure: finding.AnalysisRootFailure);
        return new(
            evaluation,
            result.Receipt,
            BlocksFurtherEvaluation: false);
    }

    static Point<T> Failed<T>(
        DiffHistoryAnalysisInspectionRequest request,
        PackageVersionAddress address,
        FindingVersion version,
        DiffHistoryAnalysisEvaluationState state,
        ImmutableArray<PackageVersionCellExecutionEvidence> executions,
        FindingDescriptor descriptor,
        string reason,
        PackageVersionCellWorkspaceCleanupEvidence? cleanup = null,
        ImmutableArray<PackageVersionCellNoContribution>
            noContributions = default,
        PackageVersionCellAnalysisWorkspaceFailure? workspaceFailure = null,
        ApiCoordinateSourceSelectionEvidence? sourceSelection = null,
        DiffHistoryMemberSourceReceipt? sourceReceipt = null,
        bool blocksFurtherEvaluation = false)
        where T : notnull
    {
        FindingInspection<T> inspection =
            new FindingInspection<T>.Failed(
                new InspectionError(
                    request.FindingSubject,
                    descriptor,
                    reason));
        var evaluation = new DiffHistoryAnalysisEvaluation<T>(
            address,
            version,
            state,
            inspection,
            executions,
            cleanup,
            noContributions,
            workspaceFailure,
            sourceReceipt,
            sourceSelection);
        return new(
            evaluation,
            sourceReceipt,
            blocksFurtherEvaluation);
    }

    static FindingComparison<T> Compare<T>(
        DiffHistoryAnalysisInspectionRequest request,
        Producer<T> producer,
        Point<T> source,
        Point<T> destination)
        where T : notnull
    {
        if (source.Inspection.Value
                is not FindingInspection<T>.Complete sourceComplete
            || destination.Inspection.Value
                is not FindingInspection<T>.Complete destinationComplete)
        {
            return FindingComparison.Compare(
                source.Inspection,
                destination.Inspection);
        }
        return producer.Compare(
            sourceComplete.Findings.Select(static finding =>
                finding.Payload),
            destinationComplete.Findings.Select(static finding =>
                finding.Payload),
            request.FindingSubject,
            request.MatchAcceptanceThreshold);
    }

    static FindingVersion Version(
        PackageHouseVersionPopulationCell cell) =>
        new(
            cell.Address.Selector,
            cell.NormalizedVersion,
            cell.Address.Position);

    delegate Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellBaselineAnalysisResult<T>>> Baseline<T>(
            PackageVersionCellBaselineAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken)
        where T : notnull;

    delegate Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellCheckpointAnalysisResult<T>>> Checkpoint<T>(
            PackageVersionCellCheckpointAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken)
        where T : notnull;

    delegate FindingComparison<T> CompareFindings<T>(
        IEnumerable<T> source,
        IEnumerable<T> destination,
        FindingSubject subject,
        int acceptanceThreshold)
        where T : notnull;

    sealed record Producer<T>(
        FindingDescriptor Descriptor,
        Baseline<T> Baseline,
        Checkpoint<T> Checkpoint,
        CompareFindings<T> Compare,
        Func<DiffHistoryAnalysisDocument<T>, DiffHistoryDocument> Wrap)
        where T : notnull;

    sealed record Point<T>(
        DiffHistoryAnalysisEvaluation<T> Evaluation,
        DiffHistoryMemberSourceReceipt? Receipt,
        bool BlocksFurtherEvaluation)
        : IDiffHistoryPoint<T>
        where T : notnull
    {
        public PackageVersionAddress Address => Evaluation.Address;
        public FindingVersion Version => Evaluation.Version;
        public FindingInspection<T> Inspection => Evaluation.Inspection;
    }
}
