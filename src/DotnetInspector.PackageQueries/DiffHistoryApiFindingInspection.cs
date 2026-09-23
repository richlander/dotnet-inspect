using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

public enum DiffHistoryApiFindingKind
{
    Type,
    Members,
    Attributes,
}

/// <summary>
/// One Count-free whole-Type API Finding History request over a settled
/// package-version population.
/// </summary>
public sealed class DiffHistoryApiInspectionRequest
{
    readonly DiffHistoryApiMemberInspectionRequest _request;

    public DiffHistoryApiInspectionRequest(
        DiffHistoryApiFindingKind finding,
        PackageHouseVersionPopulationResult.Available population,
        DiffHistoryEvaluationPlan evaluationPlan,
        PackageHouseOperation operation,
        PackageHouseTargetContext targetContext,
        DiffHistoryEvaluationLimits evaluationLimits,
        PackageVersionCellWorkspaceLimits workspaceLimits,
        DateTimeOffset workspaceDeadline,
        PackageVersionCellApiInspectionRequest apiInspection,
        ApiDiffOptions? comparisonOptions = null,
        int matchAcceptanceThreshold = 100,
        DiffHistoryPackageReplayContext? replayContext = null,
        MemberTargetSelector? member = null)
    {
        if (!Enum.IsDefined(finding))
            throw new ArgumentOutOfRangeException(nameof(finding));
        if (member is not null
            && finding != DiffHistoryApiFindingKind.Members)
        {
            throw new ArgumentException(
                "Only api.member History accepts an exact Member focus.",
                nameof(member));
        }

        Finding = finding;
        Member = member;
        _request = new(
            population,
            evaluationPlan,
            operation,
            targetContext,
            evaluationLimits,
            workspaceLimits,
            workspaceDeadline,
            apiInspection,
            comparisonOptions,
            matchAcceptanceThreshold,
            replayContext);
    }

    public DiffHistoryApiFindingKind Finding { get; }

    public MemberTargetSelector? Member { get; }

    public PackageHouseVersionPopulationResult.Available Population =>
        _request.Population;

    public DiffHistoryEvaluationPlan EvaluationPlan =>
        _request.EvaluationPlan;

    public PackageHouseOperation Operation => _request.Operation;

    public PackageHouseTargetContext TargetContext =>
        _request.TargetContext;

    public DiffHistoryEvaluationLimits EvaluationLimits =>
        _request.EvaluationLimits;

    public PackageVersionCellWorkspaceLimits WorkspaceLimits =>
        _request.WorkspaceLimits;

    public DateTimeOffset WorkspaceDeadline => _request.WorkspaceDeadline;

    public PackageVersionCellApiInspectionRequest ApiInspection =>
        _request.ApiInspection;

    public ApiDiffOptions ComparisonOptions => _request.ComparisonOptions;

    public int MatchAcceptanceThreshold =>
        _request.MatchAcceptanceThreshold;

    public DiffHistoryPackageReplayContext? ReplayContext =>
        _request.ReplayContext;

    internal DiffHistoryApiMemberInspectionRequest Common => _request;
}

/// <summary>One evaluated package Version and its native API census.</summary>
public sealed record DiffHistoryApiFindingEvaluation<T>
    where T : notnull
{
    internal DiffHistoryApiFindingEvaluation(
        PackageVersionAddress address,
        FindingVersion version,
        FindingInspection<T> inspection,
        PackageVersionCellMetadataInspectionOutcome cellOutcome,
        DiffHistoryApiMemberSubjectResolution subjectResolution,
        ApiSurfaceProjectionTruncation? projectionTruncation = null,
        IEnumerable<DiffHistoryApiParticipantEvidence>? participants = null)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Inspection =
            inspection ?? throw new ArgumentNullException(nameof(inspection));
        ArgumentNullException.ThrowIfNull(cellOutcome);
        Execution = cellOutcome.Evidence;
        Cleanup = cellOutcome.Cleanup;
        NoContributionReason = cellOutcome
            is PackageVersionCellMetadataInspectionOutcome.NoContribution
                noContribution
            ? noContribution.Reason
            : null;
        WorkspaceFailure = cellOutcome
            is PackageVersionCellMetadataInspectionOutcome.WorkspaceFailure
                workspaceFailure
            ? workspaceFailure.Failure
            : null;
        SubjectResolution = subjectResolution
            ?? throw new ArgumentNullException(nameof(subjectResolution));
        ProjectionTruncation = projectionTruncation;
        Participants =
        [
            .. participants ?? [],
        ];
    }

    public PackageVersionAddress Address { get; }

    public FindingVersion Version { get; }

    public FindingInspection<T> Inspection { get; }

    public PackageVersionCellExecutionEvidence Execution { get; }

    public PackageVersionCellWorkspaceCleanupEvidence? Cleanup { get; }

    public PackageHouseRootNoContributionReason? NoContributionReason { get; }

    public PackageVersionCellMetadataWorkspaceFailure? WorkspaceFailure
    {
        get;
    }

    public DiffHistoryApiMemberSubjectResolution SubjectResolution { get; }

    public ApiSurfaceProjectionTruncation? ProjectionTruncation { get; }

    public ImmutableArray<DiffHistoryApiParticipantEvidence> Participants
    {
        get;
    }
}

/// <summary>One chronological API evaluation in the methodology receipt.</summary>
public sealed record DiffHistoryApiFindingProbe<T>
    where T : notnull
{
    internal DiffHistoryApiFindingProbe(
        int step,
        DiffHistoryProbePurpose purpose,
        DiffHistoryInterval? selectedInterval,
        DiffHistoryApiFindingEvaluation<T> evaluation,
        DiffHistoryProbeLearning learning)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        if ((purpose == DiffHistoryProbePurpose.AdaptiveMidpoint)
            != (selectedInterval is not null))
        {
            throw new ArgumentException(
                "Only an adaptive midpoint retains its selected interval.",
                nameof(selectedInterval));
        }

        Step = step;
        Purpose = purpose;
        SelectedInterval = selectedInterval;
        Evaluation = evaluation
            ?? throw new ArgumentNullException(nameof(evaluation));
        Learning =
            learning ?? throw new ArgumentNullException(nameof(learning));
    }

    public int Step { get; }

    public DiffHistoryProbePurpose Purpose { get; }

    public DiffHistoryInterval? SelectedInterval { get; }

    public DiffHistoryApiFindingEvaluation<T> Evaluation { get; }

    public DiffHistoryProbeLearning Learning { get; }
}

/// <summary>Typed whole-Type API Finding content for Diff History.</summary>
public sealed class DiffHistoryApiFindingDocument<T>
    where T : notnull
{
    internal DiffHistoryApiFindingDocument(
        PackageVersionVector population,
        string typeFullName,
        ApiSurfaceScope scope,
        PackageHouseTargetContext targetContext,
        DiffHistoryEvaluationLimits evaluationLimits,
        DiffHistoryEvaluationPlan evaluationPlan,
        PackageVersionCellWorkspaceLimits workspaceLimits,
        ApiSurfaceProjectionLimits projectionLimits,
        ImmutableArray<PackageVersionAddress> evaluationSelection,
        ImmutableArray<DiffHistoryApiFindingEvaluation<T>> evaluations,
        ImmutableArray<DiffHistoryApiFindingProbe<T>> probes,
        FindingCensusCorrelation<T> correlation,
        ImmutableArray<DiffHistoryTransition<T>> transitions,
        ImmutableArray<DiffHistoryChangedVersionAssessment<T>>
            changedVersionAssessments,
        DiffHistoryTerminalOutcome terminalOutcome,
        ImmutableArray<DiffHistoryNextAction> nextActions,
        ApiDiffOptions comparisonOptions,
        int matchAcceptanceThreshold,
        DiffHistoryPackageReplayContext? replayContext)
    {
        Population =
            population ?? throw new ArgumentNullException(nameof(population));
        ArgumentException.ThrowIfNullOrWhiteSpace(typeFullName);
        TypeFullName = typeFullName;
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        Scope = scope;
        TargetContext = targetContext
            ?? throw new ArgumentNullException(nameof(targetContext));
        EvaluationLimits = evaluationLimits
            ?? throw new ArgumentNullException(nameof(evaluationLimits));
        EvaluationPlan = evaluationPlan
            ?? throw new ArgumentNullException(nameof(evaluationPlan));
        WorkspaceLimits = workspaceLimits
            ?? throw new ArgumentNullException(nameof(workspaceLimits));
        ProjectionLimits = projectionLimits
            ?? throw new ArgumentNullException(nameof(projectionLimits));
        if (evaluationSelection.IsDefault
            || evaluations.IsDefault
            || probes.IsDefault
            || transitions.IsDefault
            || changedVersionAssessments.IsDefault
            || nextActions.IsDefault)
        {
            throw new ArgumentException(
                "Diff History document arrays must be initialized.");
        }
        if (probes.Length != evaluations.Length)
        {
            throw new ArgumentException(
                "Every Diff History evaluation requires one probe receipt.",
                nameof(probes));
        }
        for (int i = 0; i < probes.Length; i++)
        {
            if (probes[i].Step != i + 1
                || !evaluations.Contains(probes[i].Evaluation))
            {
                throw new ArgumentException(
                    "Diff History probe receipts must be chronological and retain completed evaluations.",
                    nameof(probes));
            }
        }

        EvaluationSelection = evaluationSelection;
        Evaluations = evaluations;
        Probes = probes;
        Correlation = correlation
            ?? throw new ArgumentNullException(nameof(correlation));
        Transitions = transitions;
        ChangedVersionAssessments = changedVersionAssessments;
        TerminalOutcome = terminalOutcome
            ?? throw new ArgumentNullException(nameof(terminalOutcome));
        NextActions = nextActions;
        ComparisonOptions = comparisonOptions
            ?? throw new ArgumentNullException(nameof(comparisonOptions));
        if (matchAcceptanceThreshold is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchAcceptanceThreshold));
        }
        MatchAcceptanceThreshold = matchAcceptanceThreshold;
        ReplayContext = replayContext;
        UnevaluatedAddresses =
        [
            .. population.Addresses.Where(address =>
                !evaluations.Any(evaluation =>
                    ReferenceEquals(evaluation.Address, address))),
        ];
        ChangedVersions =
        [
            .. changedVersionAssessments.Where(static assessment =>
                assessment.State == DiffHistoryChangedVersionState.Changed),
        ];
        ChangedVersionAddresses =
        [
            .. ChangedVersions.Select(static assessment =>
                assessment.Destination),
        ];
    }

    public PackageVersionVector Population { get; }
    public string TypeFullName { get; }
    public ApiSurfaceScope Scope { get; }
    public PackageHouseTargetContext TargetContext { get; }
    public DiffHistoryEvaluationLimits EvaluationLimits { get; }
    public DiffHistoryEvaluationPlan EvaluationPlan { get; }
    public int? AuthorizedProbeCount =>
        EvaluationPlan switch
        {
            DiffHistoryEvaluationPlan.AdaptiveBisect adaptive =>
                adaptive.MaximumProbes,
            DiffHistoryEvaluationPlan.RepresentativeSurvey =>
                EvaluationPlan.ResolveAuthorizedEvaluationCount(Population),
            DiffHistoryEvaluationPlan.MajorVersionRepresentatives =>
                EvaluationPlan.ResolveAuthorizedEvaluationCount(Population),
            _ => null,
        };
    public int UsedProbeCount => Probes.Length;
    public PackageVersionCellWorkspaceLimits WorkspaceLimits { get; }
    public ApiSurfaceProjectionLimits ProjectionLimits { get; }
    public ImmutableArray<PackageVersionAddress> EvaluationSelection { get; }
    public ImmutableArray<DiffHistoryApiFindingEvaluation<T>> Evaluations
    {
        get;
    }
    public ImmutableArray<DiffHistoryApiFindingProbe<T>> Probes { get; }
    public FindingCensusCorrelation<T> Correlation { get; }
    public ImmutableArray<DiffHistoryTransition<T>> Transitions { get; }
    public ImmutableArray<DiffHistoryChangedVersionAssessment<T>>
        ChangedVersionAssessments { get; }
    public DiffHistoryTerminalOutcome TerminalOutcome { get; }
    public ImmutableArray<DiffHistoryNextAction> NextActions { get; }
    public ApiDiffOptions ComparisonOptions { get; }
    public int MatchAcceptanceThreshold { get; }
    public DiffHistoryPackageReplayContext? ReplayContext { get; }
    public ImmutableArray<PackageVersionAddress> UnevaluatedAddresses { get; }
    public ImmutableArray<DiffHistoryChangedVersionAssessment<T>>
        ChangedVersions { get; }
    public ImmutableArray<PackageVersionAddress> ChangedVersionAddresses
    {
        get;
    }
}

public enum DiffHistoryExactApiMemberSelectionState
{
    Selected,
    SubjectAbsent,
    MemberUnresolved,
    Failed,
}

/// <summary>One source-cell resolution of an exact API Member focus.</summary>
public sealed class DiffHistoryExactApiMemberSelection
{
    internal DiffHistoryExactApiMemberSelection(
        MemberTargetSelector selector,
        DiffHistoryExactApiMemberSelectionState state,
        DiffHistoryApiFindingEvaluation<ApiMemberHandle> sourceEvaluation,
        ApiMemberHandle? member = null,
        FindingCorrelationKey? correlationKey = null,
        MemberTargetDiagnostic? diagnostic = null)
    {
        Selector =
            selector ?? throw new ArgumentNullException(nameof(selector));
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        if ((state == DiffHistoryExactApiMemberSelectionState.Selected)
            != (member is not null && correlationKey is not null))
        {
            throw new ArgumentException(
                "Only selected exact-Member evidence carries a Member and correlation key.",
                nameof(member));
        }

        State = state;
        SourceEvaluation = sourceEvaluation
            ?? throw new ArgumentNullException(nameof(sourceEvaluation));
        Member = member;
        CorrelationKey = correlationKey;
        Diagnostic = diagnostic;
    }

    public MemberTargetSelector Selector { get; }
    public DiffHistoryExactApiMemberSelectionState State { get; }
    public DiffHistoryApiFindingEvaluation<ApiMemberHandle> SourceEvaluation
    {
        get;
    }
    public ApiMemberHandle? Member { get; }
    public FindingCorrelationKey? CorrelationKey { get; }
    public MemberTargetDiagnostic? Diagnostic { get; }
}

/// <summary>Typed exact API Member content for Diff History.</summary>
public sealed class DiffHistoryExactApiMemberDocument
{
    internal DiffHistoryExactApiMemberDocument(
        DiffHistoryExactApiMemberSelection selection,
        DiffHistoryApiFindingDocument<ApiMemberHandle> history,
        FindingCorrelation<ApiMemberHandle> identity)
    {
        if (selection.State
            != DiffHistoryExactApiMemberSelectionState.Selected)
        {
            throw new ArgumentException(
                "An exact API Member document requires selected source evidence.",
                nameof(selection));
        }
        Selection = selection;
        History =
            history ?? throw new ArgumentNullException(nameof(history));
        Identity =
            identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public DiffHistoryExactApiMemberSelection Selection { get; }
    public DiffHistoryApiFindingDocument<ApiMemberHandle> History { get; }
    public FindingCorrelation<ApiMemberHandle> Identity { get; }
}

public abstract partial record DiffHistoryDocument
{
    public sealed record ApiTypes : DiffHistoryDocument
    {
        internal ApiTypes(
            DiffHistoryApiFindingDocument<ApiTypeHandle> content)
        {
            Content =
                content ?? throw new ArgumentNullException(nameof(content));
        }

        public DiffHistoryApiFindingDocument<ApiTypeHandle> Content { get; }
    }

    public sealed record ApiAttributes : DiffHistoryDocument
    {
        internal ApiAttributes(
            DiffHistoryApiFindingDocument<ApiAttributeHandle> content)
        {
            Content =
                content ?? throw new ArgumentNullException(nameof(content));
        }

        public DiffHistoryApiFindingDocument<ApiAttributeHandle> Content
        {
            get;
        }
    }

    public sealed record ExactApiMember : DiffHistoryDocument
    {
        internal ExactApiMember(
            DiffHistoryExactApiMemberDocument content)
        {
            Content =
                content ?? throw new ArgumentNullException(nameof(content));
        }

        public DiffHistoryExactApiMemberDocument Content { get; }
    }
}
