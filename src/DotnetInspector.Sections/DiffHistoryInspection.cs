using DotnetInspector.PackageQueries;
using DotnetInspector.RowSelection;
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

/// <summary>
/// One normalized shared API Member History operation, including optional
/// Count reduction.
/// </summary>
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

/// <summary>
/// Completes shared Diff History through the host-neutral envelope boundary.
/// </summary>
public static class DiffHistoryInspection
{
    public static async Task<InspectionEnvelope<DiffHistoryOutcome>>
        InspectApiMembersAsync(
            DiffHistoryApiMemberInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default)
        => await InspectApiMembersAsync(
                new DiffHistoryApiMemberOperationRequest(request),
                executor,
                cancellationToken)
            .ConfigureAwait(false);

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
        outcome = BindCount(outcome, request.Count);
        return new(
            outcome,
            new InspectionShare.NonProjectable(
                "diff-history/share",
                "Diff History does not yet have a canonical Workspace Share projection."),
            Diagnostics(outcome));
    }

    static DiffHistoryOutcome BindCount(
        DiffHistoryOutcome outcome,
        DiffHistoryCountRequest? request)
    {
        if (request is null)
            return outcome;
        if (outcome is not DiffHistoryOutcome.Available available
            || available.Document
                is not DiffHistoryDocument.ApiMembers apiMembers)
        {
            return outcome;
        }

        SectionCountOutcome<
            DiffHistoryCountCohort,
            DiffHistoryChangedVersionCountEvidence> count =
                Count(apiMembers.Content, request);
        return new DiffHistoryOutcome.Available(
            available.Document,
            count);
    }

    static SectionCountOutcome<
        DiffHistoryCountCohort,
        DiffHistoryChangedVersionCountEvidence> Count(
            DiffHistoryApiMemberDocument document,
            DiffHistoryCountRequest request)
    {
        CountSource source = CountSource.Create(
            document,
            request.RowSelection);
        if (source.Failure is { } evidence)
        {
            return new SectionCountOutcome<
                DiffHistoryCountCohort,
                DiffHistoryChangedVersionCountEvidence>.SourceForCount(
                [
                    new(
                        request.Cohort,
                        evidence),
                ]);
        }

        RowsCohortResult<
            DiffHistoryCountCohort,
            DiffHistoryChangedVersionAssessment<
                ILInspector.Metadata.ApiMemberHandle>> selected =
                    RowsCohortExecutor.ApplyUnordered(
                        [
                            RowsCohortSequence<
                                DiffHistoryCountCohort,
                                DiffHistoryChangedVersionAssessment<
                                    ILInspector.Metadata.ApiMemberHandle>>
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
        if (outcome is not DiffHistoryOutcome.Available available)
            yield break;
        if (available.Document
            is not DiffHistoryDocument.ApiMembers apiMembers)
        {
            yield break;
        }

        foreach (DiffHistoryApiMemberEvaluation evaluation
            in apiMembers.Content.Evaluations)
        {
            if (evaluation.Inspection.Value
                is not FindingInspection<
                    ILInspector.Metadata.ApiMemberHandle>.Failed
                    failed)
            {
                continue;
            }

            yield return new(
                "diff-history.evaluation-failure",
                InspectionDiagnosticSeverity.Error,
                failed.Error.Reason,
                evaluation.Version.Display);
        }

        switch (available.Count)
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

    sealed class CountSource
    {
        CountSource(
            IReadOnlyList<
                DiffHistoryChangedVersionAssessment<
                    ILInspector.Metadata.ApiMemberHandle>> rows,
            DiffHistoryChangedVersionCountEvidence? failure)
        {
            Rows = rows;
            Failure = failure;
        }

        public IReadOnlyList<
            DiffHistoryChangedVersionAssessment<
                ILInspector.Metadata.ApiMemberHandle>> Rows { get; }

        public DiffHistoryChangedVersionCountEvidence? Failure { get; }

        public static CountSource Create(
            DiffHistoryApiMemberDocument document,
            RowSelectionIntent<string> selection)
        {
            int? requiredPrefix = RequiredPrefix(selection);
            if (document.ChangedVersionAssessments.IsEmpty)
            {
                return Failed(
                    document,
                    establishedAssessmentCount: 0,
                    requiredPrefix,
                    firstUnestablishedAssessment: null);
            }

            var rows =
                new List<
                    DiffHistoryChangedVersionAssessment<
                        ILInspector.Metadata.ApiMemberHandle>>();
            int established = 0;
            foreach (DiffHistoryChangedVersionAssessment<
                ILInspector.Metadata.ApiMemberHandle> assessment
                in document.ChangedVersionAssessments)
            {
                if (assessment.State
                    is not DiffHistoryChangedVersionState.Changed
                    and not DiffHistoryChangedVersionState.Unchanged)
                {
                    return Failed(
                        document,
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

        static CountSource Failed(
            DiffHistoryApiMemberDocument document,
            int establishedAssessmentCount,
            int? requiredPrefix,
            DiffHistoryChangedVersionAssessment<
                ILInspector.Metadata.ApiMemberHandle>?
                firstUnestablishedAssessment) =>
            new(
                [],
                new(
                    document.ChangedVersionAssessments.Length,
                    establishedAssessmentCount,
                    requiredPrefix,
                    firstUnestablishedAssessment));

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
                    case RowSelectionStageKind.Tail:
                        maximumLength = Math.Min(
                            maximumLength ?? operation.Count,
                            operation.Count);
                        break;
                    case RowSelectionStageKind.Window:
                        int start = operation.Start ?? 1;
                        if (operation.End is int end)
                        {
                            if (maximumLength is long maximum
                                && maximum < end)
                            {
                                return AsPrefix(
                                    Math.Max(
                                        required,
                                        offset + maximum));
                            }

                            required = Math.Max(
                                required,
                                offset + end);
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
                                Math.Max(
                                    required,
                                    offset + bounded));
                        }

                        offset += start - 1;
                        if (maximumLength is long length)
                            maximumLength = length - start + 1;
                        required = Math.Max(
                            required,
                            offset + 1);
                        break;
                    case RowSelectionStageKind.Top:
                        return null;
                    default:
                        throw new InvalidOperationException(
                            "Unknown row-selection stage kind.");
                }
            }

            return maximumLength is long maximumPrefix
                ? AsPrefix(
                    Math.Max(
                        required,
                        offset + maximumPrefix))
                : null;
        }

        static int? AsPrefix(long value) =>
            value <= int.MaxValue
                ? (int)value
                : null;
    }
}
