using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using QuerySpace.Rows;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Diff;

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
public sealed class DiffHistoryApiMemberOperationRequest : DiffHistoryRequest
{
    public DiffHistoryApiMemberOperationRequest(
        DiffHistoryApiMemberInspectionRequest inspection,
        DiffHistoryCountRequest? count = null)
        : base(count)
    {
        Inspection =
            inspection ?? throw new ArgumentNullException(nameof(inspection));
    }

    public DiffHistoryApiMemberInspectionRequest Inspection { get; }
}

/// <summary>One normalized API Finding History operation.</summary>
public sealed class DiffHistoryApiOperationRequest : DiffHistoryRequest
{
    public DiffHistoryApiOperationRequest(
        DiffHistoryApiInspectionRequest inspection,
        DiffHistoryCountRequest? count = null)
        : base(count)
    {
        Inspection =
            inspection ?? throw new ArgumentNullException(nameof(inspection));
    }

    public DiffHistoryApiInspectionRequest Inspection { get; }
}

/// <summary>One normalized exact-Member Analysis History operation.</summary>
public sealed class DiffHistoryAnalysisOperationRequest : DiffHistoryRequest
{
    public DiffHistoryAnalysisOperationRequest(
        DiffHistoryAnalysisInspectionRequest inspection,
        DiffHistoryCountRequest? count = null)
        : base(count)
    {
        Inspection =
            inspection ?? throw new ArgumentNullException(nameof(inspection));
    }

    public DiffHistoryAnalysisInspectionRequest Inspection { get; }
}

/// <summary>
/// The host's acquisition capabilities for one diff
/// (diff-system.md#operation). Acquisition authority stays with the host;
/// every decision stays in the operation.
/// </summary>
public sealed class DiffAcquisition
{
    public DiffAcquisition(IPackageHouseVersionPopulationCellExecutor cells)
    {
        Cells = cells ?? throw new ArgumentNullException(nameof(cells));
    }

    /// <summary>Opens population addresses as candidate-bound cells.</summary>
    public IPackageHouseVersionPopulationCellExecutor Cells { get; }
}

/// <summary>
/// The one host-neutral diff operation (diff-system.md#operation). Each
/// request returns its content owner's terminal; Diff History returns
/// <see cref="DiffHistoryOutcome"/> in the shared inspection envelope.
/// </summary>
public static class DiffOperation
{
    public static async Task<InspectionEnvelope<DiffHistoryOutcome>>
        ExecuteAsync(
            DiffHistoryRequest request,
            DiffAcquisition acquisition,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(acquisition);
        IPackageHouseVersionPopulationCellExecutor cells = acquisition.Cells;
        DiffHistoryOutcome outcome = request switch
        {
            DiffHistoryAnalysisOperationRequest analysis =>
                await DiffHistoryEngine.InspectAnalysisAsync(
                        analysis.Inspection,
                        cells,
                        cancellationToken)
                    .ConfigureAwait(false),
            DiffHistoryApiMemberOperationRequest members =>
                await DiffHistoryEngine.InspectApiMembersAsync(
                        members.Inspection,
                        cells,
                        cancellationToken)
                    .ConfigureAwait(false),
            DiffHistoryApiOperationRequest api =>
                await DiffHistoryEngine.InspectApiAsync(
                        api.Inspection,
                        cells,
                        cancellationToken)
                    .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
        return DiffHistoryResults.Envelope(
            DiffHistoryResults.BindCount(outcome, request.Count));
    }

    /// <summary>
    /// Runs the request and captures the House acquisitions its cells made.
    /// </summary>
    public static async Task<EvidenceInspectionEnvelope<
        DiffHistoryOutcome,
        PackageAcquisitionEvidence>>
        ExecuteWithEvidenceAsync(
            DiffHistoryRequest request,
            DiffAcquisition acquisition,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        var recorder = new PackageAcquisitionEvidenceRecorder(acquisition.Cells);
        InspectionEnvelope<DiffHistoryOutcome> inspection =
            await ExecuteAsync(
                    request,
                    new DiffAcquisition(recorder),
                    cancellationToken)
                .ConfigureAwait(false);
        return new(inspection, recorder.ToEvidence());
    }

    /// <summary>
    /// Runs a single-Library pair request over a host-opened Library pair
    /// (diff-system.md#single-library-api-diff-terminal).
    /// </summary>
    /// <param name="reuse">
    /// Lets a host return a previously completed comparison of the same
    /// endpoint pair, view, and bounds instead of computing it.
    /// </param>
    public static InspectionEnvelope<DiffAnalysisDocument> ExecuteLibraryPair(
        LibraryPairDiffRequest request,
        DiffLibraryPair pair,
        DiffPairContent content,
        DiffComparisonReuse? reuse = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(pair);
        ArgumentNullException.ThrowIfNull(content);
        return LibraryPairDiffEngine.Execute(request, pair, content, reuse);
    }

    /// <summary>
    /// Runs an analysis-set request over host-opened API surfaces, one
    /// Library or a multi-Library Package or Platform assembly set, through
    /// the same input builder as <see cref="ExecuteLibraryPair"/>
    /// (diff-system.md#single-library-api-diff-terminal). Target resolution
    /// and the host's implementation comparison run, and may throw,
    /// before this method returns; the analyses then run through Diff's
    /// Compare route.
    /// </summary>
    public static ValueTask<InspectionEnvelope<DiffAnalysisDocument>>
        ExecuteSurfaceSetAsync(
            SurfaceSetDiffRequest request,
            DiffSurfaceSet surfaces,
            DiffPairContent content,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(content);
        DiffAnalysisTargets targets = DiffAnalysisTargets.Resolve(
            request.Targets,
            request.Selection.Surface,
            surfaces.Before,
            surfaces.After);
        DiffAnalysisInput input = DiffAnalysisInputBuilder.Create(
            request.Selection,
            new DiffAnalysisApiSource.Available(
                surfaces.Before,
                surfaces.After,
                PrecomputedComparison: null),
            surfaces.BeforePaths,
            surfaces.AfterPaths,
            targets,
            request.StringLiteralQuery,
            content,
            sourceStringLiterals: null);
        return DiffAnalysisCatalog.Route.ExecuteAsync(
            new DiffAnalysisInspectionRequest(
                request.Name,
                request.BeforeVersion,
                request.AfterVersion,
                request.Catalog,
                request.Selection,
                input,
                request.Views),
            cancellationToken);
    }
}

/// <summary>Envelope, Count and diagnostics for Diff History results.</summary>
internal static class DiffHistoryResults
{
    internal static InspectionEnvelope<DiffHistoryOutcome> Envelope(
        DiffHistoryOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                "diff-history/share",
                "Diff History does not yet have a canonical Workspace Share projection."),
            Diagnostics(outcome));

    internal static DiffHistoryOutcome BindCount(
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
                                request,
                                OneVersionEvaluated(
                                    value.Content.Population,
                                    value.Content.Evaluations)),
                        DiffHistoryDocument.ApiTypes value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request,
                                OneVersionEvaluated(
                                    value.Content.Population,
                                    value.Content.Evaluations)),
                        DiffHistoryDocument.ApiAttributes value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request,
                                OneVersionEvaluated(
                                    value.Content.Population,
                                    value.Content.Evaluations)),
                        DiffHistoryDocument.ExactApiMember value =>
                            Count(
                                value.Content.History
                                    .ChangedVersionAssessments,
                                request,
                                OneVersionEvaluated(
                                    value.Content.History.Population,
                                    value.Content.History.Evaluations)),
                        DiffHistoryDocument.Allocations value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request,
                                OneVersionEvaluated(
                                    value.Content.Population,
                                    value.Content.Evaluations)),
                        DiffHistoryDocument.CallSites value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request,
                                OneVersionEvaluated(
                                    value.Content.Population,
                                    value.Content.Evaluations)),
                        DiffHistoryDocument.Unsafety value =>
                            Count(
                                value.Content.ChangedVersionAssessments,
                                request,
                                OneVersionEvaluated(
                                    value.Content.Population,
                                    value.Content.Evaluations)),
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
            DiffHistoryCountRequest request,
            bool oneVersionEvaluated)
        where T : notnull
    {
        CountSource<T> source = CountSource<T>.Create(
            assessments,
            request.RowSelection,
            oneVersionEvaluated);

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

    // A one-version population evaluated without failure has no adjacent
    // transitions, so its Changed Versions cohort is exactly empty
    // (diff-history.md#subject-specific-history-count).
    static bool OneVersionEvaluated<T>(
        PackageVersionVector population,
        IReadOnlyList<DiffHistoryApiFindingEvaluation<T>> evaluations)
        where T : notnull =>
        population.Addresses.Length == 1
        && evaluations is
            [{ Inspection.Value: not FindingInspection<T>.Failed }];

    static bool OneVersionEvaluated<T>(
        PackageVersionVector population,
        IReadOnlyList<DiffHistoryAnalysisEvaluation<T>> evaluations)
        where T : notnull =>
        population.Addresses.Length == 1
        && evaluations is
            [{ Inspection.Value: not FindingInspection<T>.Failed }];

    static bool OneVersionEvaluated(
        PackageVersionVector population,
        IReadOnlyList<DiffHistoryApiMemberEvaluation> evaluations) =>
        population.Addresses.Length == 1
        && evaluations is
            [{ Inspection.Value: not FindingInspection<ApiMemberHandle>.Failed }];

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
            RowSelectionIntent<string> selection,
            bool oneVersionEvaluated)
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

            if (firstUnestablished is null
                && (assessments.Count > 0 || oneVersionEvaluated))
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
