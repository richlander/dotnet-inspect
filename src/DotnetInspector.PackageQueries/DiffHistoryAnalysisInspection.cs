using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One Count-free exact-Member Analysis History request over a settled
/// package-version population.
/// </summary>
public sealed class DiffHistoryAnalysisInspectionRequest
{
    public DiffHistoryAnalysisInspectionRequest(
        PackageVersionCellAnalysisProducerKind finding,
        PackageHouseVersionPopulationResult.Available population,
        DiffHistoryEvaluationPlan evaluationPlan,
        PackageHouseOperation operation,
        PackageHouseTargetContext targetContext,
        DiffHistoryEvaluationLimits evaluationLimits,
        PackageVersionCellWorkspaceLimits workspaceLimits,
        DateTimeOffset workspaceDeadline,
        PackageVersionCellMemberSelector selector,
        FindingSubject findingSubject,
        int matchAcceptanceThreshold = 100,
        DiffHistoryPackageReplayContext? replayContext = null)
    {
        if (!Enum.IsDefined(finding))
            throw new ArgumentOutOfRangeException(nameof(finding));
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(evaluationPlan);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(evaluationLimits);
        ArgumentNullException.ThrowIfNull(workspaceLimits);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(findingSubject);
        if (operation.Profile != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Diff History Analysis requires a Realize operation.",
                nameof(operation));
        }
        PackageVersionCellBaselineAnalysisRequest.ValidateDeadline(
            workspaceDeadline);
        if (matchAcceptanceThreshold is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchAcceptanceThreshold));
        }
        if (evaluationPlan is DiffHistoryEvaluationPlan.AdaptiveBisect
            && population.Vector.Addresses.Length < 2)
        {
            throw new ArgumentException(
                "Adaptive Diff History requires at least two population versions.",
                nameof(evaluationPlan));
        }

        ImmutableArray<PackageVersionAddress> selected =
            evaluationPlan.ResolveInitialSelection(population.Vector);
        int maximumEvaluations =
            evaluationPlan.ResolveMaximumRealizableEvaluationCount(
                population.Vector);
        if (maximumEvaluations > evaluationLimits.MaximumEvaluations)
        {
            throw new ArgumentException(
                "The Diff History evaluation plan exceeds its work limit.",
                nameof(evaluationPlan));
        }
        foreach (PackageVersionAddress address in selected)
        {
            if (!population.Vector.Addresses.Any(candidate =>
                    ReferenceEquals(candidate, address)))
            {
                throw new ArgumentException(
                    "A Diff History evaluation address belongs to another population.",
                    nameof(evaluationPlan));
            }
        }
        if (selected
            .GroupBy(static address => address.Position)
            .Any(static group => group.Count() > 1))
        {
            throw new ArgumentException(
                "A Diff History evaluation address cannot be selected more than once.",
                nameof(evaluationPlan));
        }
        PackageVersionAddress sourceAddress =
            evaluationPlan
                is DiffHistoryEvaluationPlan.MajorVersionRepresentatives
                ? selected[0]
                : population.Vector.Addresses[0];
        if (!selected.Any(address => ReferenceEquals(
                address,
                sourceAddress)))
        {
            throw new ArgumentException(
                "Exact-Member Analysis History must evaluate the first population version as its source.",
                nameof(evaluationPlan));
        }

        Finding = finding;
        Population = population;
        EvaluationPlan = evaluationPlan;
        InitialEvaluationSelection = selected;
        Operation = operation;
        TargetContext = targetContext;
        EvaluationLimits = evaluationLimits;
        WorkspaceLimits = workspaceLimits;
        WorkspaceDeadline = workspaceDeadline;
        Selector = selector;
        FindingSubject = findingSubject;
        MatchAcceptanceThreshold = matchAcceptanceThreshold;
        ReplayContext = replayContext;
        SourceAddress = sourceAddress;
    }

    public PackageVersionCellAnalysisProducerKind Finding { get; }
    public PackageHouseVersionPopulationResult.Available Population { get; }
    public DiffHistoryEvaluationPlan EvaluationPlan { get; }
    internal ImmutableArray<PackageVersionAddress> InitialEvaluationSelection
    {
        get;
    }
    public PackageHouseOperation Operation { get; }
    public PackageHouseTargetContext TargetContext { get; }
    public DiffHistoryEvaluationLimits EvaluationLimits { get; }
    public PackageVersionCellWorkspaceLimits WorkspaceLimits { get; }
    public DateTimeOffset WorkspaceDeadline { get; }
    public PackageVersionCellMemberSelector Selector { get; }
    public FindingSubject FindingSubject { get; }
    public int MatchAcceptanceThreshold { get; }
    public DiffHistoryPackageReplayContext? ReplayContext { get; }

    public PackageVersionAddress SourceAddress { get; }
}

public enum DiffHistoryAnalysisEvaluationState
{
    Completed,
    SourceUnselected,
    NoContribution,
    WorkspaceFailure,
    CleanupFailure,
}

/// <summary>
/// One detached exact-Member Analysis evaluation and its owner-issued evidence.
/// </summary>
public sealed record DiffHistoryAnalysisEvaluation<T>
    where T : notnull
{
    internal DiffHistoryAnalysisEvaluation(
        PackageVersionAddress address,
        FindingVersion version,
        DiffHistoryAnalysisEvaluationState state,
        FindingInspection<T> inspection,
        ImmutableArray<PackageVersionCellExecutionEvidence> executions,
        PackageVersionCellWorkspaceCleanupEvidence? cleanup = null,
        ImmutableArray<PackageVersionCellNoContribution>
            noContributions = default,
        PackageVersionCellAnalysisWorkspaceFailure? workspaceFailure = null,
        DiffHistoryMemberSourceReceipt? sourceReceipt = null,
        ApiCoordinateSourceSelectionEvidence? sourceSelection = null,
        ApiCoordinateCorrespondenceEvidence? sourceValidation = null,
        PackageVersionCellSourceBindingEvidence? sourceBinding = null,
        ApiCoordinateCorrespondenceEvidence? relationship = null,
        MatchedApiMemberBodyResolutionEvidence? bodyResolution = null,
        ArtifactRootFailure? analysisRootFailure = null)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        State = state;
        Inspection =
            inspection ?? throw new ArgumentNullException(nameof(inspection));
        if (executions.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "An Analysis History evaluation requires execution evidence.",
                nameof(executions));
        }
        Executions = executions;
        Cleanup = cleanup;
        NoContributions = noContributions.IsDefault
            ? []
            : noContributions;
        WorkspaceFailure = workspaceFailure;
        SourceReceipt = sourceReceipt;
        SourceSelection = sourceSelection;
        SourceValidation = sourceValidation;
        SourceBinding = sourceBinding;
        Relationship = relationship;
        BodyResolution = bodyResolution;
        AnalysisRootFailure = analysisRootFailure;
    }

    public PackageVersionAddress Address { get; }
    public FindingVersion Version { get; }
    public DiffHistoryAnalysisEvaluationState State { get; }
    public FindingInspection<T> Inspection { get; }
    public ImmutableArray<PackageVersionCellExecutionEvidence> Executions
    {
        get;
    }
    public PackageVersionCellWorkspaceCleanupEvidence? Cleanup { get; }
    public ImmutableArray<PackageVersionCellNoContribution> NoContributions
    {
        get;
    }
    public PackageVersionCellAnalysisWorkspaceFailure? WorkspaceFailure
    {
        get;
    }
    public DiffHistoryMemberSourceReceipt? SourceReceipt { get; }
    public ApiCoordinateSourceSelectionEvidence? SourceSelection { get; }
    public ApiCoordinateCorrespondenceEvidence? SourceValidation { get; }
    public PackageVersionCellSourceBindingEvidence? SourceBinding { get; }
    public ApiCoordinateCorrespondenceEvidence? Relationship { get; }
    public MatchedApiMemberBodyResolutionEvidence? BodyResolution { get; }
    public ArtifactRootFailure? AnalysisRootFailure { get; }
}

/// <summary>One chronological Analysis evaluation in the methodology receipt.</summary>
public sealed record DiffHistoryAnalysisProbe<T>
    where T : notnull
{
    internal DiffHistoryAnalysisProbe(
        int step,
        DiffHistoryProbePurpose purpose,
        DiffHistoryInterval? selectedInterval,
        DiffHistoryAnalysisEvaluation<T> evaluation,
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
    public DiffHistoryAnalysisEvaluation<T> Evaluation { get; }
    public DiffHistoryProbeLearning Learning { get; }
}

/// <summary>Typed exact-Member Analysis content for Diff History.</summary>
public sealed class DiffHistoryAnalysisDocument<T>
    where T : notnull
{
    internal DiffHistoryAnalysisDocument(
        PackageVersionCellAnalysisProducerKind finding,
        PackageVersionVector population,
        PackageVersionCellMemberSelector selector,
        FindingSubject findingSubject,
        PackageHouseTargetContext targetContext,
        DiffHistoryEvaluationLimits evaluationLimits,
        DiffHistoryEvaluationPlan evaluationPlan,
        PackageVersionCellWorkspaceLimits workspaceLimits,
        ImmutableArray<PackageVersionAddress> evaluationSelection,
        ImmutableArray<DiffHistoryAnalysisEvaluation<T>> evaluations,
        ImmutableArray<DiffHistoryAnalysisProbe<T>> probes,
        DiffHistoryMemberSourceReceipt? sourceReceipt,
        FindingCensusCorrelation<T> correlation,
        ImmutableArray<DiffHistoryTransition<T>> transitions,
        ImmutableArray<DiffHistoryChangedVersionAssessment<T>>
            changedVersionAssessments,
        DiffHistoryTerminalOutcome terminalOutcome,
        ImmutableArray<DiffHistoryNextAction> nextActions,
        int matchAcceptanceThreshold,
        DiffHistoryPackageReplayContext? replayContext)
    {
        if (!Enum.IsDefined(finding))
            throw new ArgumentOutOfRangeException(nameof(finding));
        Finding = finding;
        Population =
            population ?? throw new ArgumentNullException(nameof(population));
        Selector =
            selector ?? throw new ArgumentNullException(nameof(selector));
        FindingSubject = findingSubject
            ?? throw new ArgumentNullException(nameof(findingSubject));
        TargetContext = targetContext
            ?? throw new ArgumentNullException(nameof(targetContext));
        EvaluationLimits = evaluationLimits
            ?? throw new ArgumentNullException(nameof(evaluationLimits));
        EvaluationPlan = evaluationPlan
            ?? throw new ArgumentNullException(nameof(evaluationPlan));
        WorkspaceLimits = workspaceLimits
            ?? throw new ArgumentNullException(nameof(workspaceLimits));
        if (evaluationSelection.IsDefault
            || evaluations.IsDefault
            || probes.IsDefault
            || transitions.IsDefault
            || changedVersionAssessments.IsDefault
            || nextActions.IsDefault)
        {
            throw new ArgumentException(
                "Diff History Analysis document arrays must be initialized.");
        }
        if (probes.Length != evaluations.Length)
        {
            throw new ArgumentException(
                "Every Diff History evaluation requires one probe receipt.",
                nameof(probes));
        }
        if ((sourceReceipt is null
                && evaluations.Any(static evaluation =>
                    evaluation.SourceReceipt is not null))
            || (sourceReceipt is not null
                && evaluations.Any(evaluation =>
                    !ReferenceEquals(
                        evaluation.SourceReceipt,
                        sourceReceipt))))
        {
            throw new ArgumentException(
                "Every Analysis evaluation must retain the document's source receipt.",
                nameof(sourceReceipt));
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
        SourceReceipt = sourceReceipt;
        Correlation = correlation
            ?? throw new ArgumentNullException(nameof(correlation));
        Transitions = transitions;
        ChangedVersionAssessments = changedVersionAssessments;
        TerminalOutcome = terminalOutcome
            ?? throw new ArgumentNullException(nameof(terminalOutcome));
        NextActions = nextActions;
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

    public PackageVersionCellAnalysisProducerKind Finding { get; }
    public PackageVersionVector Population { get; }
    public PackageVersionCellMemberSelector Selector { get; }
    public FindingSubject FindingSubject { get; }
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
    public ImmutableArray<PackageVersionAddress> EvaluationSelection { get; }
    public ImmutableArray<DiffHistoryAnalysisEvaluation<T>> Evaluations
    {
        get;
    }
    public ImmutableArray<DiffHistoryAnalysisProbe<T>> Probes { get; }
    public DiffHistoryMemberSourceReceipt? SourceReceipt { get; }
    public FindingCensusCorrelation<T> Correlation { get; }
    public ImmutableArray<DiffHistoryTransition<T>> Transitions { get; }
    public ImmutableArray<DiffHistoryChangedVersionAssessment<T>>
        ChangedVersionAssessments { get; }
    public DiffHistoryTerminalOutcome TerminalOutcome { get; }
    public ImmutableArray<DiffHistoryNextAction> NextActions { get; }
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

public abstract partial record DiffHistoryDocument
{
    public sealed record Allocations : DiffHistoryDocument
    {
        internal Allocations(
            DiffHistoryAnalysisDocument<AllocationOccurrence> content)
        {
            Content =
                content ?? throw new ArgumentNullException(nameof(content));
        }

        public DiffHistoryAnalysisDocument<AllocationOccurrence> Content
        {
            get;
        }
    }

    public sealed record CallSites : DiffHistoryDocument
    {
        internal CallSites(
            DiffHistoryAnalysisDocument<DirectCall> content)
        {
            Content =
                content ?? throw new ArgumentNullException(nameof(content));
        }

        public DiffHistoryAnalysisDocument<DirectCall> Content { get; }
    }

    public sealed record Unsafety : DiffHistoryDocument
    {
        internal Unsafety(
            DiffHistoryAnalysisDocument<UnsafetyOccurrence> content)
        {
            Content =
                content ?? throw new ArgumentNullException(nameof(content));
        }

        public DiffHistoryAnalysisDocument<UnsafetyOccurrence> Content
        {
            get;
        }
    }
}
