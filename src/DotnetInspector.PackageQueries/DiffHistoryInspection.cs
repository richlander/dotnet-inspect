using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

/// <summary>Finite work bound for one Diff History evaluation selection.</summary>
public sealed class DiffHistoryEvaluationLimits
{
    public DiffHistoryEvaluationLimits(int maximumEvaluations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumEvaluations);
        MaximumEvaluations = maximumEvaluations;
    }

    public int MaximumEvaluations { get; }
}

/// <summary>
/// One Count-free whole-Type API Member History request over a settled
/// package-version population.
/// </summary>
public sealed class DiffHistoryApiMemberInspectionRequest
{
    public DiffHistoryApiMemberInspectionRequest(
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
        DiffHistoryPackageReplayContext? replayContext = null)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(evaluationPlan);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(targetContext);
        ArgumentNullException.ThrowIfNull(evaluationLimits);
        ArgumentNullException.ThrowIfNull(workspaceLimits);
        ArgumentNullException.ThrowIfNull(apiInspection);
        if (operation.Profile != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Diff History evaluation requires a Realize operation.",
                nameof(operation));
        }
        if (workspaceDeadline == DateTimeOffset.MinValue
            || workspaceDeadline == DateTimeOffset.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workspaceDeadline),
                "Diff History requires a finite Workspace deadline.");
        }
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
        int authorizedEvaluations =
            evaluationPlan.ResolveAuthorizedEvaluationCount(
                population.Vector);
        if (authorizedEvaluations > evaluationLimits.MaximumEvaluations)
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

        Population = population;
        EvaluationPlan = evaluationPlan;
        InitialEvaluationSelection = selected;
        Operation = operation;
        TargetContext = targetContext;
        EvaluationLimits = evaluationLimits;
        WorkspaceLimits = workspaceLimits;
        WorkspaceDeadline = workspaceDeadline;
        ApiInspection = apiInspection;
        ComparisonOptions = comparisonOptions ?? ApiDiffOptions.Default;
        MatchAcceptanceThreshold = matchAcceptanceThreshold;
        ReplayContext = replayContext;
    }

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

    public PackageVersionCellApiInspectionRequest ApiInspection { get; }

    public ApiDiffOptions ComparisonOptions { get; }

    public int MatchAcceptanceThreshold { get; }

    public DiffHistoryPackageReplayContext? ReplayContext { get; }
}

/// <summary>
/// One evaluated package Version and its native whole-Type Member census.
/// </summary>
public sealed record DiffHistoryApiMemberEvaluation
{
    internal DiffHistoryApiMemberEvaluation(
        PackageVersionAddress address,
        FindingVersion version,
        FindingInspection<ApiMemberHandle> inspection,
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

    public FindingInspection<ApiMemberHandle> Inspection { get; }

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

/// <summary>
/// Resource-free identity and provenance for the participant that supplied a
/// Type census.
/// </summary>
public sealed record DiffHistoryResolvedAssembly(
    AssemblyReferenceIdentity Identity,
    AssemblyResolutionProvenance Provenance);

/// <summary>Detached resolution of one Type focus within a Version cell.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "resolution")]
[JsonDerivedType(
    typeof(DiffHistoryApiMemberSubjectResolution.Resolved),
    "resolved")]
[JsonDerivedType(
    typeof(DiffHistoryApiMemberSubjectResolution.SubjectAbsent),
    "subjectAbsent")]
[JsonDerivedType(
    typeof(DiffHistoryApiMemberSubjectResolution.NoApplicableInput),
    "noApplicableInput")]
[JsonDerivedType(
    typeof(DiffHistoryApiMemberSubjectResolution.Ambiguous),
    "ambiguous")]
[JsonDerivedType(
    typeof(DiffHistoryApiMemberSubjectResolution.Failed),
    "failed")]
public abstract record DiffHistoryApiMemberSubjectResolution
{
    private protected DiffHistoryApiMemberSubjectResolution()
    {
    }

    public sealed record Resolved : DiffHistoryApiMemberSubjectResolution
    {
        internal Resolved(DiffHistoryResolvedAssembly assembly)
        {
            Assembly =
                assembly ?? throw new ArgumentNullException(nameof(assembly));
        }

        public DiffHistoryResolvedAssembly Assembly { get; }
    }

    public sealed record SubjectAbsent :
        DiffHistoryApiMemberSubjectResolution;

    public sealed record NoApplicableInput :
        DiffHistoryApiMemberSubjectResolution;

    public sealed record Ambiguous :
        DiffHistoryApiMemberSubjectResolution
    {
        internal Ambiguous(
            ImmutableArray<DiffHistoryResolvedAssembly> assemblies)
        {
            if (assemblies.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "Ambiguous Type resolution requires candidate assemblies.",
                    nameof(assemblies));
            }

            Assemblies = assemblies;
        }

        public ImmutableArray<DiffHistoryResolvedAssembly> Assemblies { get; }
    }

    public sealed record Failed :
        DiffHistoryApiMemberSubjectResolution;
}

/// <summary>Detached API projection evidence for one Version participant.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(DiffHistoryApiParticipantEvidence.Available),
    "available")]
[JsonDerivedType(
    typeof(DiffHistoryApiParticipantEvidence.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(DiffHistoryApiParticipantEvidence.Failed),
    "failed")]
public abstract record DiffHistoryApiParticipantEvidence
{
    private protected DiffHistoryApiParticipantEvidence(
        DiffHistoryResolvedAssembly subject)
    {
        Subject =
            subject ?? throw new ArgumentNullException(nameof(subject));
    }

    public DiffHistoryResolvedAssembly Subject { get; }

    public sealed record Available :
        DiffHistoryApiParticipantEvidence
    {
        internal Available(
            DiffHistoryResolvedAssembly subject,
            ImmutableArray<ApiSurfaceInspectionFailure>
                inspectionFailures)
            : base(subject)
        {
            if (inspectionFailures.IsDefault)
            {
                throw new ArgumentException(
                    "Inspection failures must be initialized.",
                    nameof(inspectionFailures));
            }

            InspectionFailures = inspectionFailures;
        }

        public ImmutableArray<ApiSurfaceInspectionFailure>
            InspectionFailures { get; }
    }

    public sealed record Rejected :
        DiffHistoryApiParticipantEvidence
    {
        internal Rejected(
            DiffHistoryResolvedAssembly subject,
            CandidateOpenFailure failure)
            : base(subject)
        {
            Failure =
                failure ?? throw new ArgumentNullException(nameof(failure));
        }

        public CandidateOpenFailure Failure { get; }
    }

    public sealed record Failed :
        DiffHistoryApiParticipantEvidence
    {
        internal Failed(
            DiffHistoryResolvedAssembly subject,
            DiffHistoryExceptionEvidence error)
            : base(subject)
        {
            Error =
                error ?? throw new ArgumentNullException(nameof(error));
        }

        public DiffHistoryExceptionEvidence Error { get; }
    }
}

/// <summary>Exception evidence without the exception's arbitrary object graph.</summary>
public sealed record DiffHistoryExceptionEvidence(
    string Type,
    int HResult,
    string Message,
    string Detail);

/// <summary>
/// One comparison between consecutive evaluated points in population order.
/// </summary>
public sealed record DiffHistoryTransition<T>
    where T : notnull
{
    internal DiffHistoryTransition(
        PackageVersionAddress source,
        PackageVersionAddress destination,
        ImmutableArray<PackageVersionAddress> unevaluatedBetween,
        FindingComparison<T> comparison)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Destination =
            destination ?? throw new ArgumentNullException(nameof(destination));
        if (source.Position >= destination.Position)
        {
            throw new ArgumentException(
                "A Diff History transition must follow population order.",
                nameof(destination));
        }
        if (unevaluatedBetween.IsDefault)
        {
            throw new ArgumentException(
                "Unevaluated addresses must be initialized.",
                nameof(unevaluatedBetween));
        }
        if (unevaluatedBetween.Any(address =>
                address.Position <= source.Position
                || address.Position >= destination.Position))
        {
            throw new ArgumentException(
                "Unevaluated addresses must fall strictly between the transition endpoints.",
                nameof(unevaluatedBetween));
        }

        UnevaluatedBetween = unevaluatedBetween;
        Comparison =
            comparison ?? throw new ArgumentNullException(nameof(comparison));
    }

    public PackageVersionAddress Source { get; }

    public PackageVersionAddress Destination { get; }

    public ImmutableArray<PackageVersionAddress> UnevaluatedBetween { get; }

    public bool IsPopulationAdjacent => UnevaluatedBetween.IsEmpty;

    public FindingComparison<T> Comparison { get; }
}

public enum DiffHistoryChangedVersionState
{
    Changed,
    Unchanged,
    Inapplicable,
    Failed,
    Unevaluated,
}

public enum DiffHistoryCountCohort
{
    ChangedVersions,
}

/// <summary>
/// One destination Version assessed only against its immediate population
/// predecessor.
/// </summary>
public sealed record DiffHistoryChangedVersionAssessment<T>
    where T : notnull
{
    internal DiffHistoryChangedVersionAssessment(
        PackageVersionAddress predecessor,
        PackageVersionAddress destination,
        DiffHistoryChangedVersionState state,
        FindingComparison<T>? comparison,
        bool predecessorEvaluated,
        bool destinationEvaluated)
    {
        Predecessor =
            predecessor ?? throw new ArgumentNullException(nameof(predecessor));
        Destination =
            destination ?? throw new ArgumentNullException(nameof(destination));
        if (destination.Position != predecessor.Position + 1)
        {
            throw new ArgumentException(
                "A changed-Version assessment requires immediate population neighbors.",
                nameof(destination));
        }

        switch (state)
        {
            case DiffHistoryChangedVersionState.Changed:
            case DiffHistoryChangedVersionState.Unchanged:
                if (comparison is null
                    || comparison.Value
                        is not FindingComparison<T>.Complete)
                {
                    throw new ArgumentException(
                        "A changed or unchanged assessment requires a completed comparison.",
                        nameof(comparison));
                }
                break;
            case DiffHistoryChangedVersionState.Inapplicable:
                if (comparison?.Value
                        is not FindingComparison<T>.Complete complete
                    || (complete.Transition.Old
                            != FindingInspectionState.NoApplicableInput
                        && complete.Transition.New
                            != FindingInspectionState.NoApplicableInput))
                {
                    throw new ArgumentException(
                        "An inapplicable assessment requires a completed comparison with an inapplicable endpoint.",
                        nameof(comparison));
                }
                break;
            case DiffHistoryChangedVersionState.Failed:
                if (comparison is null
                    || comparison.Value
                        is not FindingComparison<T>.Failed)
                {
                    throw new ArgumentException(
                        "A failed assessment requires a failed comparison.",
                        nameof(comparison));
                }
                break;
            case DiffHistoryChangedVersionState.Unevaluated:
                if (comparison is not null
                    || (predecessorEvaluated && destinationEvaluated))
                {
                    throw new ArgumentException(
                        "An unevaluated assessment requires a missing endpoint.",
                        nameof(comparison));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state));
        }

        State = state;
        Comparison = comparison;
        PredecessorEvaluated = predecessorEvaluated;
        DestinationEvaluated = destinationEvaluated;
    }

    public PackageVersionAddress Predecessor { get; }

    public PackageVersionAddress Destination { get; }

    public DiffHistoryChangedVersionState State { get; }

    public FindingComparison<T>? Comparison { get; }

    public bool PredecessorEvaluated { get; }

    public bool DestinationEvaluated { get; }
}

/// <summary>
/// Completion evidence retained when Changed Versions cannot supply an exact
/// Count for the requested logical prefix.
/// </summary>
public sealed record DiffHistoryChangedVersionCountAssessment(
    PackageVersionAddress Predecessor,
    PackageVersionAddress Destination,
    DiffHistoryChangedVersionState State)
{
    public PackageVersionAddress Predecessor { get; } =
        Predecessor
        ?? throw new ArgumentNullException(nameof(Predecessor));

    public PackageVersionAddress Destination { get; } =
        Destination
        ?? throw new ArgumentNullException(nameof(Destination));
}

public sealed class DiffHistoryChangedVersionCountEvidence
{
    internal DiffHistoryChangedVersionCountEvidence(
        int totalAssessmentCount,
        int establishedAssessmentCount,
        int? requiredChangedVersionPrefix,
        DiffHistoryChangedVersionCountAssessment?
            firstUnestablishedAssessment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalAssessmentCount);
        if (establishedAssessmentCount < 0
            || establishedAssessmentCount > totalAssessmentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(establishedAssessmentCount));
        }
        if (requiredChangedVersionPrefix is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requiredChangedVersionPrefix));
        }

        TotalAssessmentCount = totalAssessmentCount;
        EstablishedAssessmentCount = establishedAssessmentCount;
        RequiredChangedVersionPrefix = requiredChangedVersionPrefix;
        FirstUnestablishedAssessment = firstUnestablishedAssessment;
    }

    public int TotalAssessmentCount { get; }

    public int EstablishedAssessmentCount { get; }

    public int? RequiredChangedVersionPrefix { get; }

    public DiffHistoryChangedVersionCountAssessment?
        FirstUnestablishedAssessment { get; }
}

/// <summary>
/// Typed whole-Type API Member content for one settled Diff History document.
/// </summary>
public sealed class DiffHistoryApiMemberDocument
{
    internal DiffHistoryApiMemberDocument(
        PackageVersionVector population,
        string typeFullName,
        ApiSurfaceScope scope,
        PackageHouseTargetContext targetContext,
        DiffHistoryEvaluationLimits evaluationLimits,
        DiffHistoryEvaluationPlan evaluationPlan,
        PackageVersionCellWorkspaceLimits workspaceLimits,
        ApiSurfaceProjectionLimits projectionLimits,
        ImmutableArray<PackageVersionAddress> evaluationSelection,
        ImmutableArray<DiffHistoryApiMemberEvaluation> evaluations,
        ImmutableArray<DiffHistoryApiMemberProbe> probes,
        FindingCensusCorrelation<ApiMemberHandle> correlation,
        ImmutableArray<DiffHistoryTransition<ApiMemberHandle>> transitions,
        ImmutableArray<
            DiffHistoryChangedVersionAssessment<ApiMemberHandle>>
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
            .. changedVersionAssessments
                .Where(static assessment =>
                    assessment.State
                        == DiffHistoryChangedVersionState.Changed)
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
            _ => null,
        };

    public int UsedProbeCount => Probes.Length;

    public PackageVersionCellWorkspaceLimits WorkspaceLimits { get; }

    public ApiSurfaceProjectionLimits ProjectionLimits { get; }

    public ImmutableArray<PackageVersionAddress> EvaluationSelection { get; }

    public ImmutableArray<DiffHistoryApiMemberEvaluation> Evaluations { get; }

    public ImmutableArray<DiffHistoryApiMemberProbe> Probes { get; }

    public FindingCensusCorrelation<ApiMemberHandle> Correlation { get; }

    public ImmutableArray<DiffHistoryTransition<ApiMemberHandle>> Transitions
    {
        get;
    }

    public ImmutableArray<
        DiffHistoryChangedVersionAssessment<ApiMemberHandle>>
        ChangedVersionAssessments { get; }

    public DiffHistoryTerminalOutcome TerminalOutcome { get; }

    public ImmutableArray<DiffHistoryNextAction> NextActions { get; }

    public ApiDiffOptions ComparisonOptions { get; }

    public int MatchAcceptanceThreshold { get; }

    public DiffHistoryPackageReplayContext? ReplayContext { get; }

    public ImmutableArray<PackageVersionAddress> UnevaluatedAddresses { get; }

    public ImmutableArray<
        DiffHistoryChangedVersionAssessment<ApiMemberHandle>>
        ChangedVersions { get; }

    public ImmutableArray<PackageVersionAddress> ChangedVersionAddresses
    {
        get;
    }
}

/// <summary>One producer-specific document arm of shared Diff History.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "document")]
[JsonDerivedType(typeof(DiffHistoryDocument.ApiMembers), "apiMembers")]
[JsonDerivedType(typeof(DiffHistoryDocument.ApiTypes), "apiTypes")]
[JsonDerivedType(
    typeof(DiffHistoryDocument.ApiAttributes),
    "apiAttributes")]
[JsonDerivedType(
    typeof(DiffHistoryDocument.ExactApiMember),
    "exactApiMember")]
[JsonDerivedType(typeof(DiffHistoryDocument.Allocations), "allocations")]
[JsonDerivedType(typeof(DiffHistoryDocument.CallSites), "callSites")]
[JsonDerivedType(typeof(DiffHistoryDocument.Unsafety), "unsafety")]
public abstract partial record DiffHistoryDocument
{
    private protected DiffHistoryDocument()
    {
    }

    public sealed record ApiMembers : DiffHistoryDocument
    {
        internal ApiMembers(DiffHistoryApiMemberDocument content)
        {
            Content =
                content ?? throw new ArgumentNullException(nameof(content));
        }

        public DiffHistoryApiMemberDocument Content { get; }
    }
}

/// <summary>Shared terminal outcome for one constructed Diff History.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(typeof(DiffHistoryOutcome.Available), "available")]
[JsonDerivedType(
    typeof(DiffHistoryOutcome.ExactApiMemberUnavailable),
    "exactApiMemberUnavailable")]
public abstract record DiffHistoryOutcome
{
    private protected DiffHistoryOutcome()
    {
    }

    public record Available : DiffHistoryOutcome
    {
        internal Available(DiffHistoryDocument document)
        {
            Document =
                document ?? throw new ArgumentNullException(nameof(document));
        }

        public DiffHistoryDocument Document { get; }
    }

    public sealed record ExactApiMemberUnavailable : DiffHistoryOutcome
    {
        internal ExactApiMemberUnavailable(
            DiffHistoryExactApiMemberSelection selection)
        {
            if (selection.State
                == DiffHistoryExactApiMemberSelectionState.Selected)
            {
                throw new ArgumentException(
                    "Unavailable exact-Member History requires selection non-success.",
                    nameof(selection));
            }
            Selection = selection;
        }

        public DiffHistoryExactApiMemberSelection Selection { get; }
    }
}
