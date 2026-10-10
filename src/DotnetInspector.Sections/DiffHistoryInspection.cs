using DotnetInspector.PackageQueries;
using QuerySpace.Rows;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Sections;

/// <summary>One admitted Count request over Diff History Changed Versions.</summary>
public sealed class DiffHistoryCountRequest
{
    public DiffHistoryCountRequest(
        DiffHistoryCountCohort cohort,
        RowSelectionIntent<string>? rowSelection = null)
    {
        if (cohort != DiffHistoryCountCohort.ChangedVersions)
        {
            throw new ArgumentException(
                "Type and Member Diff History Count admits only the Changed Versions cohort.",
                nameof(cohort));
        }
        RowSelectionIntent<string> selection =
            rowSelection ?? RowSelectionIntent<string>.Empty;
        if (selection.Operations.Any(static operation =>
                operation.Kind == RowSelectionStageKind.Top))
        {
            throw new ArgumentException(
                "Diff History Changed Versions do not define a ranking order.",
                nameof(rowSelection));
        }

        Cohort = cohort;
        RowSelection = selection;
    }

    public DiffHistoryCountCohort Cohort { get; }

    public RowSelectionIntent<string> RowSelection { get; }
}

/// <summary>One normalized API Member History operation.</summary>
public sealed class DiffHistoryApiMemberOperationRequest
{
    public DiffHistoryApiMemberOperationRequest(
        DiffHistoryApiMemberInspectionRequest inspection,
        DiffHistoryCountRequest? count = null)
    {
        Inspection =
            inspection ?? throw new ArgumentNullException(nameof(inspection));
        Count = count;
    }

    public DiffHistoryApiMemberInspectionRequest Inspection { get; }

    public DiffHistoryCountRequest? Count { get; }
}

/// <summary>One normalized API Finding History operation.</summary>
public sealed class DiffHistoryApiOperationRequest
{
    public DiffHistoryApiOperationRequest(
        DiffHistoryApiInspectionRequest inspection,
        DiffHistoryCountRequest? count = null)
    {
        Inspection =
            inspection ?? throw new ArgumentNullException(nameof(inspection));
        Count = count;
    }

    public DiffHistoryApiInspectionRequest Inspection { get; }

    public DiffHistoryCountRequest? Count { get; }
}

/// <summary>One normalized exact-Member Analysis History operation.</summary>
public sealed class DiffHistoryAnalysisOperationRequest
{
    public DiffHistoryAnalysisOperationRequest(
        DiffHistoryAnalysisInspectionRequest inspection,
        DiffHistoryCountRequest? count = null)
    {
        Inspection =
            inspection ?? throw new ArgumentNullException(nameof(inspection));
        Count = count;
    }

    public DiffHistoryAnalysisInspectionRequest Inspection { get; }

    public DiffHistoryCountRequest? Count { get; }
}

/// <summary>Completes shared Diff History through the envelope boundary.</summary>
public static class DiffHistoryInspection
{
    public static Task<InspectionEnvelope<DiffHistoryOutcome>>
        InspectAnalysisAsync(
            DiffHistoryAnalysisInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        InspectAnalysisAsync(
            new DiffHistoryAnalysisOperationRequest(request),
            executor,
            cancellationToken);

    public static async Task<InspectionEnvelope<DiffHistoryOutcome>>
        InspectAnalysisAsync(
            DiffHistoryAnalysisOperationRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DiffHistoryOutcome outcome =
            await DiffHistoryInspector.InspectAnalysisAsync(
                    request.Inspection,
                    executor,
                    cancellationToken)
                .ConfigureAwait(false);
        return Envelope(BindCount(outcome, request.Count));
    }

    public static Task<InspectionEnvelope<DiffHistoryOutcome>>
        InspectApiMembersAsync(
            DiffHistoryApiMemberInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        InspectApiMembersAsync(
            new DiffHistoryApiMemberOperationRequest(request),
            executor,
            cancellationToken);

    public static async Task<InspectionEnvelope<DiffHistoryOutcome>>
        InspectApiMembersAsync(
            DiffHistoryApiMemberOperationRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DiffHistoryOutcome outcome =
            await DiffHistoryInspector.InspectApiMembersAsync(
                    request.Inspection,
                    executor,
                    cancellationToken)
                .ConfigureAwait(false);
        return Envelope(BindCount(outcome, request.Count));
    }

    public static Task<InspectionEnvelope<DiffHistoryOutcome>>
        InspectApiAsync(
            DiffHistoryApiInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        InspectApiAsync(
            new DiffHistoryApiOperationRequest(request),
            executor,
            cancellationToken);

    public static async Task<InspectionEnvelope<DiffHistoryOutcome>>
        InspectApiAsync(
            DiffHistoryApiOperationRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        DiffHistoryOutcome outcome =
            await DiffHistoryInspector.InspectApiAsync(
                    request.Inspection,
                    executor,
                    cancellationToken)
                .ConfigureAwait(false);
        return Envelope(BindCount(outcome, request.Count));
    }

    /// <summary>
    /// Completes Analysis History and captures the House acquisitions its
    /// cells made.
    /// </summary>
    public static async Task<EvidenceInspectionEnvelope<
        DiffHistoryOutcome,
        PackageAcquisitionEvidence>>
        InspectAnalysisWithEvidenceAsync(
            DiffHistoryAnalysisOperationRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executor);
        var recorder = new PackageAcquisitionEvidenceRecorder(executor);
        InspectionEnvelope<DiffHistoryOutcome> inspection =
            await InspectAnalysisAsync(request, recorder, cancellationToken)
                .ConfigureAwait(false);
        return new(inspection, recorder.ToEvidence());
    }

    /// <summary>
    /// Completes API History and captures the House acquisitions its cells
    /// made.
    /// </summary>
    public static async Task<EvidenceInspectionEnvelope<
        DiffHistoryOutcome,
        PackageAcquisitionEvidence>>
        InspectApiWithEvidenceAsync(
            DiffHistoryApiOperationRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executor);
        var recorder = new PackageAcquisitionEvidenceRecorder(executor);
        InspectionEnvelope<DiffHistoryOutcome> inspection =
            await InspectApiAsync(request, recorder, cancellationToken)
                .ConfigureAwait(false);
        return new(inspection, recorder.ToEvidence());
    }

    static InspectionEnvelope<DiffHistoryOutcome> Envelope(
        DiffHistoryOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                "diff-history/share",
                "Diff History does not yet have a canonical Workspace Share projection."),
            Diagnostics(outcome));

    static DiffHistoryOutcome BindCount(
        DiffHistoryOutcome outcome,
        DiffHistoryCountRequest? request)
    {
        if (outcome is not DiffHistoryOutcome.Available available)
        {
            return outcome;
        }

        SectionCountOutcome<
            DiffHistoryCountCohort,
            DiffHistoryChangedVersionCountEvidence>? count =
                request is null
                    ? null
                    : available.Document switch
                    {
                        DiffHistoryDocument.ApiMembers value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request),
                        DiffHistoryDocument.ApiTypes value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request),
                        DiffHistoryDocument.ApiAttributes value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request),
                        DiffHistoryDocument.ExactApiMember value =>
                            Count(
                                value.Content.History
                                    .ChangedVersionAssessments,
                                request),
                        DiffHistoryDocument.Allocations value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request),
                        DiffHistoryDocument.CallSites value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request),
                        DiffHistoryDocument.Unsafety value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request),
                        _ => throw new InvalidOperationException(
                            "Unknown Diff History document."),
                    };
        return new DiffHistorySectionAvailable(
            available.Document,
            count);
    }

    static SectionCountOutcome<
        DiffHistoryCountCohort,
        DiffHistoryChangedVersionCountEvidence> Count<T>(
            IReadOnlyList<DiffHistoryChangedVersionAssessment<T>>
                assessments,
            DiffHistoryCountRequest request)
        where T : notnull
    {
        CountSource<T> source = CountSource<T>.Create(
            assessments,
            request.RowSelection);

        RowsCohortResult<
            DiffHistoryCountCohort,
            DiffHistoryChangedVersionAssessment<T>> selected =
                RowsCohortExecutor.ApplyUnordered(
                    [
                        RowsCohortSequence<
                            DiffHistoryCountCohort,
                            DiffHistoryChangedVersionAssessment<T>>
                            .Create(
                                request.Cohort,
                                source.Rows),
                    ],
                    request.RowSelection);
        if (!selected.IsSuccess)
        {
            RowsCohortSemanticFailure<DiffHistoryCountCohort> failure =
                selected.Failure!;
            return new SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Semantic(
                    failure.Identity,
                    failure.Failure.StageNumber,
                    failure.Failure.RequiredPosition,
                    failure.Failure.AvailableCount);
        }

        int count = selected.RowSets[0].Values.Count;
        if (source.Incompleteness is not { } evidence)
        {
            return new SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.Completed(
                [
                    new(request.Cohort, count),
                ]);
        }

        // Incomplete History still counts the Changed Versions rows it
        // observed, under section-row-shaping.md#incomplete-evaluation.
        return new SectionCountOutcome<
            DiffHistoryCountCohort,
            DiffHistoryChangedVersionCountEvidence>.Completed(
            [
                new(request.Cohort, count, isExact: false),
            ],
            [
                new(request.Cohort, evidence),
            ]);
    }

    static IEnumerable<InspectionDiagnostic> Diagnostics(
        DiffHistoryOutcome outcome)
    {
        if (outcome
            is DiffHistoryOutcome.ExactApiMemberUnavailable unavailable)
        {
            yield return new(
                "diff-history.exact-member-unavailable",
                InspectionDiagnosticSeverity.Error,
                unavailable.Selection.Diagnostic?.Message
                    ?? $"Exact Member selection completed as {unavailable.Selection.State}.",
                unavailable.Selection.SourceEvaluation.Version.Display);
            yield break;
        }
        if (outcome is not DiffHistoryOutcome.Available available)
            yield break;

        IEnumerable<InspectionDiagnostic> evaluationDiagnostics =
            available.Document switch
            {
                DiffHistoryDocument.ApiMembers value =>
                    EvaluationDiagnostics(value.Content.Evaluations),
                DiffHistoryDocument.ApiTypes value =>
                    EvaluationDiagnostics(value.Content.Evaluations),
                DiffHistoryDocument.ApiAttributes value =>
                    EvaluationDiagnostics(value.Content.Evaluations),
                DiffHistoryDocument.ExactApiMember value =>
                    EvaluationDiagnostics(
                        value.Content.History.Evaluations),
                DiffHistoryDocument.Allocations value =>
                    EvaluationDiagnostics(value.Content.Evaluations),
                DiffHistoryDocument.CallSites value =>
                    EvaluationDiagnostics(value.Content.Evaluations),
                DiffHistoryDocument.Unsafety value =>
                    EvaluationDiagnostics(value.Content.Evaluations),
                _ => throw new InvalidOperationException(
                    "Unknown Diff History document."),
            };
        foreach (InspectionDiagnostic diagnostic in evaluationDiagnostics)
            yield return diagnostic;

        if (outcome is not DiffHistorySectionAvailable counted)
            yield break;

        switch (counted.Count)
        {
            case SectionCountOutcome<
                    DiffHistoryCountCohort,
                    DiffHistoryChangedVersionCountEvidence>.Completed
                    { Counts: [{ IsExact: false } observed] } completed:
                DiffHistoryChangedVersionCountEvidence incompleteness =
                    completed.Sources[0].Evidence;
                yield return new(
                    "diff-history.count-incomplete",
                    InspectionDiagnosticSeverity.Warning,
                    $"Diff History inspection incomplete: the selected evaluations do not establish every Changed Versions transition; {observed.Value} observed Changed Versions counted.",
                    incompleteness.FirstUnestablishedAssessment?
                        .Destination.NormalizedVersion);
                break;
            case SectionCountOutcome<
                    DiffHistoryCountCohort,
                    DiffHistoryChangedVersionCountEvidence>.Semantic
                    semantic:
                yield return new(
                    "diff-history.count-selection-failure",
                    InspectionDiagnosticSeverity.Error,
                    $"Count selection stage {semantic.StageNumber} requires position {semantic.RequiredPosition}, but only {semantic.AvailableCount} Changed Versions are available.");
                break;
        }
    }

    static IEnumerable<InspectionDiagnostic> EvaluationDiagnostics<T>(
        IEnumerable<DiffHistoryApiFindingEvaluation<T>> evaluations)
        where T : notnull
    {
        foreach (DiffHistoryApiFindingEvaluation<T> evaluation
            in evaluations)
        {
            if (evaluation.Inspection.Value
                is FindingInspection<T>.Failed failed)
            {
                yield return new(
                    "diff-history.evaluation-failure",
                    InspectionDiagnosticSeverity.Error,
                    failed.Error.Reason,
                    evaluation.Version.Display);
            }
        }
    }

    static IEnumerable<InspectionDiagnostic> EvaluationDiagnostics<T>(
        IEnumerable<DiffHistoryAnalysisEvaluation<T>> evaluations)
        where T : notnull
    {
        foreach (DiffHistoryAnalysisEvaluation<T> evaluation
            in evaluations)
        {
            if (evaluation.Inspection.Value
                is FindingInspection<T>.Failed failed)
            {
                yield return new(
                    "diff-history.evaluation-failure",
                    InspectionDiagnosticSeverity.Error,
                    failed.Error.Reason,
                    evaluation.Version.Display);
            }
        }
    }

    static IEnumerable<InspectionDiagnostic> EvaluationDiagnostics(
        IEnumerable<DiffHistoryApiMemberEvaluation> evaluations)
    {
        foreach (DiffHistoryApiMemberEvaluation evaluation in evaluations)
        {
            if (evaluation.Inspection.Value
                is FindingInspection<ApiMemberHandle>.Failed failed)
            {
                yield return new(
                    "diff-history.evaluation-failure",
                    InspectionDiagnosticSeverity.Error,
                    failed.Error.Reason,
                    evaluation.Version.Display);
            }
        }
    }

    sealed class CountSource<T>
        where T : notnull
    {
        CountSource(
            IReadOnlyList<DiffHistoryChangedVersionAssessment<T>> rows,
            DiffHistoryChangedVersionCountEvidence? incompleteness)
        {
            Rows = rows;
            Incompleteness = incompleteness;
        }

        /// <summary>
        /// The Changed Versions rows observed, in population order, as the
        /// Changed Versions section would render them.
        /// </summary>
        public IReadOnlyList<DiffHistoryChangedVersionAssessment<T>> Rows
        {
            get;
        }

        /// <summary>
        /// Null when the evidence establishes the requested count; otherwise
        /// why the observed rows are not known to be every Changed Version.
        /// </summary>
        public DiffHistoryChangedVersionCountEvidence? Incompleteness
        {
            get;
        }

        public static CountSource<T> Create(
            IReadOnlyList<DiffHistoryChangedVersionAssessment<T>>
                assessments,
            RowSelectionIntent<string> selection)
        {
            int? requiredPrefix = selection.RequiredPrefix();
            var rows =
                new List<DiffHistoryChangedVersionAssessment<T>>();
            int established = 0;
            DiffHistoryChangedVersionAssessment<T>? firstUnestablished =
                null;
            foreach (DiffHistoryChangedVersionAssessment<T> assessment
                in assessments)
            {
                if (assessment.State
                    is not DiffHistoryChangedVersionState.Changed
                    and not DiffHistoryChangedVersionState.Unchanged)
                {
                    firstUnestablished ??= assessment;
                    continue;
                }

                if (firstUnestablished is null)
                {
                    established++;
                }
                if (assessment.State
                    == DiffHistoryChangedVersionState.Changed)
                {
                    rows.Add(assessment);
                    if (requiredPrefix is int required
                        && rows.Count == required)
                    {
                        // A proven prefix is complete for the request only
                        // when no earlier transition is unknown.
                        break;
                    }
                }
            }

            if (assessments.Count > 0 && firstUnestablished is null)
            {
                return new(rows, incompleteness: null);
            }

            return new(
                rows,
                new(
                    assessments.Count,
                    established,
                    requiredPrefix,
                    firstUnestablished is null
                        ? null
                        : new(
                            firstUnestablished.Predecessor,
                            firstUnestablished.Destination,
                            firstUnestablished.State)));
        }
    }
}
