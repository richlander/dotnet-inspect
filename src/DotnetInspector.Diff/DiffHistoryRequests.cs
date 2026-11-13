using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.Diff;

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
