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
        if (source.Failure is { } evidence)
        {
            return new SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount(
                [
                    new(request.Cohort, evidence),
                ]);
        }

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

        return new SectionCountOutcome<
            DiffHistoryCountCohort,
            DiffHistoryChangedVersionCountEvidence>.Completed(
            [
                new(
                    request.Cohort,
                    selected.RowSets[0].Values.Count),
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
                    DiffHistoryChangedVersionCountEvidence>.SourceForCount
                    source:
                DiffHistoryChangedVersionCountEvidence evidence =
                    source.Sources[0].Evidence;
                yield return new(
                    "diff-history.count-source-insufficient",
                    InspectionDiagnosticSeverity.Error,
                    "The selected History evaluations do not establish an exact Changed Versions count.",
                    evidence.FirstUnestablishedAssessment?
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
            DiffHistoryChangedVersionCountEvidence? failure)
        {
            Rows = rows;
            Failure = failure;
        }

        public IReadOnlyList<DiffHistoryChangedVersionAssessment<T>> Rows
        {
            get;
        }

        public DiffHistoryChangedVersionCountEvidence? Failure { get; }

        public static CountSource<T> Create(
            IReadOnlyList<DiffHistoryChangedVersionAssessment<T>>
                assessments,
            RowSelectionIntent<string> selection)
        {
            int? requiredPrefix = RequiredPrefix(selection);
            if (assessments.Count == 0)
            {
                return Failed(
                    assessments,
                    establishedAssessmentCount: 0,
                    requiredPrefix,
                    firstUnestablishedAssessment: null);
            }

            var rows =
                new List<DiffHistoryChangedVersionAssessment<T>>();
            int established = 0;
            foreach (DiffHistoryChangedVersionAssessment<T> assessment
                in assessments)
            {
                if (assessment.State
                    is not DiffHistoryChangedVersionState.Changed
                    and not DiffHistoryChangedVersionState.Unchanged)
                {
                    return Failed(
                        assessments,
                        established,
                        requiredPrefix,
                        assessment);
                }

                established++;
                if (assessment.State
                    == DiffHistoryChangedVersionState.Changed)
                {
                    rows.Add(assessment);
                    if (requiredPrefix is int required
                        && rows.Count == required)
                    {
                        return new(rows, failure: null);
                    }
                }
            }

            return new(rows, failure: null);
        }

        static CountSource<T> Failed(
            IReadOnlyList<DiffHistoryChangedVersionAssessment<T>>
                assessments,
            int establishedAssessmentCount,
            int? requiredPrefix,
            DiffHistoryChangedVersionAssessment<T>?
                firstUnestablishedAssessment) =>
            new(
                [],
                new(
                    assessments.Count,
                    establishedAssessmentCount,
                    requiredPrefix,
                    firstUnestablishedAssessment is null
                        ? null
                        : new(
                            firstUnestablishedAssessment.Predecessor,
                            firstUnestablishedAssessment.Destination,
                            firstUnestablishedAssessment.State)));

        static int? RequiredPrefix(
            RowSelectionIntent<string> selection)
        {
            long offset = 0;
            long? maximumLength = null;
            long required = 0;
            foreach (RowSelectionIntentOperation<string> operation
                in selection.Operations)
            {
                switch (operation.Kind)
                {
                    case RowSelectionStageKind.Head:
                        maximumLength = Math.Min(
                            maximumLength ?? operation.Count,
                            operation.Count);
                        break;
                    case RowSelectionStageKind.Tail:
                        if (maximumLength is not long boundedTail)
                            return null;
                        required = Math.Max(
                            required,
                            offset + boundedTail);
                        long retainedTail = Math.Min(
                            boundedTail,
                            operation.Count);
                        offset += boundedTail - retainedTail;
                        maximumLength = retainedTail;
                        break;
                    case RowSelectionStageKind.Window:
                        int start = operation.Start ?? 1;
                        if (operation.End is int end)
                        {
                            if (maximumLength is long maximum
                                && maximum < end)
                            {
                                return AsPrefix(
                                    Math.Max(required, offset + maximum));
                            }
                            required = Math.Max(required, offset + end);
                            maximumLength = end - start + 1;
                            offset = 0;
                            break;
                        }
                        if (operation.Start is null)
                            break;
                        if (maximumLength is long bounded
                            && bounded < start)
                        {
                            return AsPrefix(
                                Math.Max(required, offset + bounded));
                        }
                        offset += start - 1;
                        if (maximumLength is long length)
                            maximumLength = length - start + 1;
                        required = Math.Max(required, offset + 1);
                        break;
                    case RowSelectionStageKind.Top:
                        return null;
                    default:
                        throw new InvalidOperationException(
                            "Unknown row-selection stage kind.");
                }
            }

            return maximumLength is long maximumPrefix
                ? AsPrefix(Math.Max(required, offset + maximumPrefix))
                : null;
        }

        static int? AsPrefix(long value) =>
            value <= int.MaxValue
                ? (int)value
                : null;
    }
}
