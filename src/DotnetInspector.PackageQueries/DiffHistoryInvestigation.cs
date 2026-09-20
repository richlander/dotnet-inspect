using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.PackageQueries;

public enum DiffHistoryEvaluationPolicy
{
    FullPopulation,
    ExplicitCheckpoints,
    AdaptiveBisect,
}

/// <summary>One evaluation policy over a settled Diff History population.</summary>
public abstract record DiffHistoryEvaluationPlan
{
    private protected DiffHistoryEvaluationPlan()
    {
    }

    public abstract DiffHistoryEvaluationPolicy Policy { get; }

    public sealed record FullPopulation : DiffHistoryEvaluationPlan
    {
        public override DiffHistoryEvaluationPolicy Policy =>
            DiffHistoryEvaluationPolicy.FullPopulation;
    }

    public sealed record ExplicitCheckpoints : DiffHistoryEvaluationPlan
    {
        public ExplicitCheckpoints(
            IEnumerable<PackageVersionAddress> addresses)
        {
            ArgumentNullException.ThrowIfNull(addresses);
            Addresses = [.. addresses];
            if (Addresses.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "Explicit Diff History evaluation requires at least one checkpoint.",
                    nameof(addresses));
            }
            if (Addresses.Any(static address => address is null))
            {
                throw new ArgumentException(
                    "Explicit Diff History checkpoints cannot contain null.",
                    nameof(addresses));
            }
        }

        public override DiffHistoryEvaluationPolicy Policy =>
            DiffHistoryEvaluationPolicy.ExplicitCheckpoints;

        public ImmutableArray<PackageVersionAddress> Addresses { get; }
    }

    public sealed record AdaptiveBisect : DiffHistoryEvaluationPlan
    {
        public AdaptiveBisect(int maximumProbes)
        {
            if (maximumProbes < 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumProbes),
                    "Adaptive Diff History requires at least two probes.");
            }

            MaximumProbes = maximumProbes;
        }

        public override DiffHistoryEvaluationPolicy Policy =>
            DiffHistoryEvaluationPolicy.AdaptiveBisect;

        public int MaximumProbes { get; }
    }
}

/// <summary>Replayable package-source context retained for typed actions.</summary>
public sealed class DiffHistoryPackageReplayContext
{
    public DiffHistoryPackageReplayContext(
        IEnumerable<string>? sources = null,
        IEnumerable<string>? additionalSources = null,
        string? configFile = null,
        string? configDirectory = null)
    {
        Sources = Copy(sources, nameof(sources));
        AdditionalSources = Copy(
            additionalSources,
            nameof(additionalSources));
        ConfigFile = Optional(configFile, nameof(configFile));
        ConfigDirectory = Optional(
            configDirectory,
            nameof(configDirectory));
    }

    public ImmutableArray<string> Sources { get; }

    public ImmutableArray<string> AdditionalSources { get; }

    public string? ConfigFile { get; }

    public string? ConfigDirectory { get; }

    static ImmutableArray<string> Copy(
        IEnumerable<string>? values,
        string parameterName)
    {
        ImmutableArray<string> copy =
        [
            .. values ?? [],
        ];
        if (copy.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Diff History replay source values cannot be empty.",
                parameterName);
        }
        return copy;
    }

    static string? Optional(string? value, string parameterName) =>
        value is null
            ? null
            : string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException(
                    "Diff History replay configuration values cannot be empty.",
                    parameterName)
                : value;
}

/// <summary>One changed or blocked interval in population order.</summary>
public sealed record DiffHistoryInterval
{
    internal DiffHistoryInterval(
        PackageVersionAddress source,
        PackageVersionAddress destination)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Destination =
            destination ?? throw new ArgumentNullException(nameof(destination));
        if (source.Position >= destination.Position)
        {
            throw new ArgumentException(
                "A Diff History interval must follow population order.",
                nameof(destination));
        }
    }

    public PackageVersionAddress Source { get; }

    public PackageVersionAddress Destination { get; }

    public bool IsAdjacent =>
        Destination.Position == Source.Position + 1;

    public int UnevaluatedCount =>
        Destination.Position - Source.Position - 1;
}

public enum DiffHistoryProbePurpose
{
    PopulationStart,
    PopulationEnd,
    AdaptiveMidpoint,
    ExplicitCheckpoint,
    DenseCensus,
}

public enum DiffHistoryProbeLearningKind
{
    Baseline,
    ObservationRecorded,
    ChangedIntervals,
    NoChangeObserved,
    BlockedByFailure,
}

/// <summary>Knowledge established after one chronological evaluation.</summary>
public sealed record DiffHistoryProbeLearning
{
    internal DiffHistoryProbeLearning(
        DiffHistoryProbeLearningKind kind,
        ImmutableArray<DiffHistoryInterval> changedIntervals = default)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ChangedIntervals = changedIntervals.IsDefault
            ? []
            : changedIntervals;
        if (kind == DiffHistoryProbeLearningKind.ChangedIntervals
            && ChangedIntervals.IsEmpty)
        {
            throw new ArgumentException(
                "Changed-interval learning requires at least one interval.",
                nameof(changedIntervals));
        }
        if (kind is not (
                DiffHistoryProbeLearningKind.ChangedIntervals
                or DiffHistoryProbeLearningKind.BlockedByFailure)
            && !ChangedIntervals.IsEmpty)
        {
            throw new ArgumentException(
                "Only changed or blocked learning can retain intervals.",
                nameof(changedIntervals));
        }

        Kind = kind;
    }

    public DiffHistoryProbeLearningKind Kind { get; }

    public ImmutableArray<DiffHistoryInterval> ChangedIntervals { get; }
}

/// <summary>One chronological evaluation in the settled methodology receipt.</summary>
public sealed record DiffHistoryApiMemberProbe
{
    internal DiffHistoryApiMemberProbe(
        int step,
        DiffHistoryProbePurpose purpose,
        DiffHistoryInterval? selectedInterval,
        DiffHistoryApiMemberEvaluation evaluation,
        DiffHistoryProbeLearning learning)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        if (purpose == DiffHistoryProbePurpose.AdaptiveMidpoint
            && selectedInterval is null)
        {
            throw new ArgumentException(
                "An adaptive midpoint requires its selected interval.",
                nameof(selectedInterval));
        }
        if (purpose != DiffHistoryProbePurpose.AdaptiveMidpoint
            && selectedInterval is not null)
        {
            throw new ArgumentException(
                "Only an adaptive midpoint can retain a selected interval.",
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

    public DiffHistoryApiMemberEvaluation Evaluation { get; }

    public DiffHistoryProbeLearning Learning { get; }
}

/// <summary>One settled terminal interpretation of completed History work.</summary>
public abstract record DiffHistoryTerminalOutcome
{
    private protected DiffHistoryTerminalOutcome()
    {
    }

    public sealed record FullPopulationCompleted :
        DiffHistoryTerminalOutcome;

    public sealed record ExplicitCheckpointsCompleted :
        DiffHistoryTerminalOutcome;

    public sealed record BoundariesResolved : DiffHistoryTerminalOutcome
    {
        internal BoundariesResolved(
            ImmutableArray<DiffHistoryInterval> boundaries)
        {
            Boundaries = RequireNonEmpty(
                boundaries,
                nameof(boundaries));
            if (Boundaries.Any(static interval => !interval.IsAdjacent))
            {
                throw new ArgumentException(
                    "Resolved boundaries must be population-adjacent.",
                    nameof(boundaries));
            }
        }

        public ImmutableArray<DiffHistoryInterval> Boundaries { get; }
    }

    public sealed record EqualEndpoints : DiffHistoryTerminalOutcome
    {
        internal EqualEndpoints(DiffHistoryInterval endpoints)
        {
            Endpoints = endpoints
                ?? throw new ArgumentNullException(nameof(endpoints));
        }

        public DiffHistoryInterval Endpoints { get; }
    }

    public sealed record BudgetExhausted : DiffHistoryTerminalOutcome
    {
        internal BudgetExhausted(
            ImmutableArray<DiffHistoryInterval> resolvedBoundaries,
            ImmutableArray<DiffHistoryInterval> unresolvedIntervals)
        {
            ResolvedBoundaries = resolvedBoundaries.IsDefault
                ? []
                : resolvedBoundaries;
            if (ResolvedBoundaries.Any(
                    static interval => !interval.IsAdjacent))
            {
                throw new ArgumentException(
                    "Resolved boundaries must be population-adjacent.",
                    nameof(resolvedBoundaries));
            }
            UnresolvedIntervals = RequireNonEmpty(
                unresolvedIntervals,
                nameof(unresolvedIntervals));
            if (UnresolvedIntervals.Any(
                    static interval => interval.IsAdjacent))
            {
                throw new ArgumentException(
                    "Unresolved intervals must contain an unevaluated address.",
                    nameof(unresolvedIntervals));
            }
        }

        public ImmutableArray<DiffHistoryInterval> ResolvedBoundaries { get; }

        public ImmutableArray<DiffHistoryInterval> UnresolvedIntervals
        {
            get;
        }
    }

    public sealed record BlockedByFailure : DiffHistoryTerminalOutcome
    {
        internal BlockedByFailure(
            ImmutableArray<DiffHistoryInterval> resolvedBoundaries,
            ImmutableArray<DiffHistoryInterval> unresolvedIntervals,
            ImmutableArray<PackageVersionAddress> failedAddresses,
            ImmutableArray<DiffHistoryInterval> blockedIntervals)
        {
            ResolvedBoundaries = resolvedBoundaries.IsDefault
                ? []
                : resolvedBoundaries;
            if (ResolvedBoundaries.Any(
                    static interval => !interval.IsAdjacent))
            {
                throw new ArgumentException(
                    "Resolved boundaries must be population-adjacent.",
                    nameof(resolvedBoundaries));
            }
            UnresolvedIntervals = unresolvedIntervals.IsDefault
                ? []
                : unresolvedIntervals;
            if (UnresolvedIntervals.Any(
                    static interval => interval.IsAdjacent))
            {
                throw new ArgumentException(
                    "Unresolved intervals must contain an unevaluated address.",
                    nameof(unresolvedIntervals));
            }
            FailedAddresses = failedAddresses.IsDefault
                ? []
                : failedAddresses;
            BlockedIntervals = blockedIntervals.IsDefault
                ? []
                : blockedIntervals;
            if (FailedAddresses.IsEmpty && BlockedIntervals.IsEmpty)
            {
                throw new ArgumentException(
                    "A blocked terminal outcome requires a failed evaluation or comparison.");
            }
        }

        public ImmutableArray<DiffHistoryInterval> ResolvedBoundaries { get; }

        public ImmutableArray<DiffHistoryInterval> UnresolvedIntervals
        {
            get;
        }

        public ImmutableArray<PackageVersionAddress> FailedAddresses { get; }

        public ImmutableArray<DiffHistoryInterval> BlockedIntervals { get; }
    }

    static ImmutableArray<DiffHistoryInterval> RequireNonEmpty(
        ImmutableArray<DiffHistoryInterval> intervals,
        string parameterName)
    {
        if (intervals.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A terminal interval set cannot be empty.",
                parameterName);
        }
        return intervals;
    }
}

/// <summary>One typed follow-up over the completed History evidence.</summary>
public abstract record DiffHistoryNextAction
{
    private protected DiffHistoryNextAction()
    {
    }

    public sealed record Probe : DiffHistoryNextAction
    {
        internal Probe(
            DiffHistoryInterval interval,
            PackageVersionAddress address,
            ImmutableArray<PackageVersionAddress> selectedAddresses)
        {
            Interval = interval
                ?? throw new ArgumentNullException(nameof(interval));
            Address = address
                ?? throw new ArgumentNullException(nameof(address));
            if (address.Position <= interval.Source.Position
                || address.Position >= interval.Destination.Position)
            {
                throw new ArgumentException(
                    "A suggested probe must fall inside its interval.",
                    nameof(address));
            }
            if (selectedAddresses.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "A suggested probe requires the existing checkpoint selection.",
                    nameof(selectedAddresses));
            }
            if (!selectedAddresses.Contains(address)
                || selectedAddresses
                    .GroupBy(static selected => selected.Position)
                    .Any(static group => group.Count() > 1))
            {
                throw new ArgumentException(
                    "A suggested probe selection must contain the probe exactly once.",
                    nameof(selectedAddresses));
            }
            SelectedAddresses = selectedAddresses;
        }

        public DiffHistoryInterval Interval { get; }

        public PackageVersionAddress Address { get; }

        public ImmutableArray<PackageVersionAddress> SelectedAddresses { get; }
    }

    public sealed record PairwiseDiff : DiffHistoryNextAction
    {
        internal PairwiseDiff(
            DiffHistoryInterval boundary,
            string packageId,
            string typeFullName,
            MemberAnchor? member,
            PackageCompileAsset? sourceAsset,
            string finding,
            ApiSurfaceScope scope,
            PackageHouseTargetContext targetContext,
            DiffHistoryPackageReplayContext? replayContext)
        {
            Boundary = boundary
                ?? throw new ArgumentNullException(nameof(boundary));
            if (!boundary.IsAdjacent)
            {
                throw new ArgumentException(
                    "A pairwise Diff action requires an adjacent boundary.",
                    nameof(boundary));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
            ArgumentException.ThrowIfNullOrWhiteSpace(typeFullName);
            if (member is not null
                && !string.Equals(
                    member.TypeFullName,
                    typeFullName,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A pairwise Diff Member must belong to the selected Type.",
                    nameof(member));
            }
            if (sourceAsset is not null && member is null)
            {
                throw new ArgumentException(
                    "A pairwise Diff source asset requires an exact Member.",
                    nameof(sourceAsset));
            }
            ArgumentException.ThrowIfNullOrWhiteSpace(finding);
            if (!Enum.IsDefined(scope))
                throw new ArgumentOutOfRangeException(nameof(scope));

            PackageId = packageId;
            TypeFullName = typeFullName;
            Member = member;
            SourceAsset = sourceAsset;
            Finding = finding;
            Scope = scope;
            TargetContext = targetContext
                ?? throw new ArgumentNullException(nameof(targetContext));
            ReplayContext = replayContext;
        }

        public DiffHistoryInterval Boundary { get; }

        public string PackageId { get; }

        public string TypeFullName { get; }

        public MemberAnchor? Member { get; }

        public PackageCompileAsset? SourceAsset { get; }

        public string Finding { get; }

        public ApiSurfaceScope Scope { get; }

        public PackageHouseTargetContext TargetContext { get; }

        public DiffHistoryPackageReplayContext? ReplayContext { get; }
    }
}
