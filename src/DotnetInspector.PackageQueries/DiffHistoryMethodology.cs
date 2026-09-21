using System.Collections.Immutable;

using DotnetInspector.Packages;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

interface IDiffHistoryPoint<T>
    where T : notnull
{
    PackageVersionAddress Address { get; }
    FindingVersion Version { get; }
    FindingInspection<T> Inspection { get; }
    bool BlocksFurtherEvaluation { get; }
}

sealed record DiffHistoryMethodologyProbe<T, TPoint>(
    int Step,
    DiffHistoryProbePurpose Purpose,
    DiffHistoryInterval? SelectedInterval,
    TPoint Point,
    DiffHistoryProbeLearning Learning)
    where T : notnull
    where TPoint : IDiffHistoryPoint<T>;

sealed record DiffHistoryMethodologyExecution<T, TPoint>(
    ImmutableArray<TPoint> Points,
    ImmutableArray<DiffHistoryMethodologyProbe<T, TPoint>> Probes,
    FindingCensusCorrelation<T> Correlation,
    ImmutableArray<DiffHistoryTransition<T>> Transitions,
    ImmutableArray<DiffHistoryChangedVersionAssessment<T>>
        ChangedVersionAssessments,
    DiffHistoryTerminalOutcome TerminalOutcome,
    ImmutableArray<DiffHistoryNextAction> NextActions)
    where T : notnull
    where TPoint : IDiffHistoryPoint<T>;

static class DiffHistoryMethodology
{
    public static async Task<DiffHistoryMethodologyExecution<T, TPoint>>
        ExecuteAsync<T, TPoint>(
            PackageVersionVector population,
            DiffHistoryEvaluationPlan evaluationPlan,
            ImmutableArray<PackageVersionAddress> initialEvaluationSelection,
            DiffHistoryEvaluationLimits evaluationLimits,
            Func<
                PackageVersionAddress,
                CancellationToken,
                Task<TPoint>> evaluate,
            Func<TPoint, TPoint, FindingComparison<T>> compare,
            Func<
                DiffHistoryInterval,
                DiffHistoryNextAction.PairwiseDiff> pairwiseAction,
            CancellationToken cancellationToken)
        where T : notnull
        where TPoint : IDiffHistoryPoint<T>
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(evaluationPlan);
        ArgumentNullException.ThrowIfNull(evaluationLimits);
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentNullException.ThrowIfNull(compare);
        ArgumentNullException.ThrowIfNull(pairwiseAction);
        cancellationToken.ThrowIfCancellationRequested();

        int maximumEvaluations = Math.Min(
            evaluationLimits.MaximumEvaluations,
            evaluationPlan.ResolveMaximumRealizableEvaluationCount(
                population));
        var chronological = ImmutableArray.CreateBuilder<TPoint>(
            maximumEvaluations);
        var probes = ImmutableArray.CreateBuilder<
            DiffHistoryMethodologyProbe<T, TPoint>>(
                maximumEvaluations);
        if (evaluationPlan
            is DiffHistoryEvaluationPlan.AdaptiveBisect adaptive)
        {
            await EvaluateAdaptiveAsync(
                    population,
                    adaptive,
                    evaluate,
                    compare,
                    chronological,
                    probes,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            DiffHistoryProbePurpose purpose =
                evaluationPlan switch
                {
                    DiffHistoryEvaluationPlan.FullPopulation =>
                        DiffHistoryProbePurpose.DenseCensus,
                    DiffHistoryEvaluationPlan.ExplicitCheckpoints =>
                        DiffHistoryProbePurpose.ExplicitCheckpoint,
                    DiffHistoryEvaluationPlan.RepresentativeSurvey =>
                        DiffHistoryProbePurpose.RepresentativeSample,
                    _ => throw new InvalidOperationException(
                        "Unknown Diff History evaluation plan."),
                };
            foreach (PackageVersionAddress address
                in initialEvaluationSelection)
            {
                TPoint point = await evaluate(address, cancellationToken)
                    .ConfigureAwait(false);
                chronological.Add(point);
                probes.Add(
                    new(
                        probes.Count + 1,
                        purpose,
                        SelectedInterval: null,
                        point,
                        new(
                            probes.Count == 0
                                ? DiffHistoryProbeLearningKind.Baseline
                                : DiffHistoryProbeLearningKind
                                    .ObservationRecorded)));
                if (point.BlocksFurtherEvaluation)
                    break;
            }
        }

        ImmutableArray<TPoint> points =
            InPopulationOrder<T, TPoint>(chronological);
        FindingCensusCorrelation<T> correlation =
            FindingCensusCorrelation<T>.Create(
                points.Select(static point =>
                    new VersionedFindingInspection<T>(
                        point.Version,
                        point.Inspection)));
        ImmutableArray<DiffHistoryTransition<T>> transitions =
            BuildTransitions(population, points, compare);
        ImmutableArray<DiffHistoryChangedVersionAssessment<T>> assessments =
            BuildChangedVersionAssessments(
                population,
                points,
                transitions);
        return new(
            points,
            probes.ToImmutable(),
            correlation,
            transitions,
            assessments,
            BuildTerminalOutcome(
                population,
                evaluationPlan,
                points,
                transitions),
            BuildNextActions(
                population,
                evaluationPlan,
                points,
                transitions,
                pairwiseAction));
    }

    static async Task EvaluateAdaptiveAsync<T, TPoint>(
        PackageVersionVector population,
        DiffHistoryEvaluationPlan.AdaptiveBisect plan,
        Func<
            PackageVersionAddress,
            CancellationToken,
            Task<TPoint>> evaluate,
        Func<TPoint, TPoint, FindingComparison<T>> compare,
        ImmutableArray<TPoint>.Builder chronological,
        ImmutableArray<
            DiffHistoryMethodologyProbe<T, TPoint>>.Builder probes,
        CancellationToken cancellationToken)
        where T : notnull
        where TPoint : IDiffHistoryPoint<T>
    {
        TPoint first = await evaluate(
                population.Addresses[0],
                cancellationToken)
            .ConfigureAwait(false);
        chronological.Add(first);
        probes.Add(
            new(
                1,
                DiffHistoryProbePurpose.PopulationStart,
                SelectedInterval: null,
                first,
                new(DiffHistoryProbeLearningKind.Baseline)));
        if (first.BlocksFurtherEvaluation)
            return;

        TPoint last = await evaluate(
                population.Addresses[^1],
                cancellationToken)
            .ConfigureAwait(false);
        chronological.Add(last);
        ImmutableArray<DiffHistoryTransition<T>> transitions =
            BuildTransitions(
                population,
                InPopulationOrder<T, TPoint>(chronological),
                compare);
        probes.Add(
            new(
                2,
                DiffHistoryProbePurpose.PopulationEnd,
                SelectedInterval: null,
                last,
                Learning(transitions)));
        if (last.BlocksFurtherEvaluation)
            return;

        while (chronological.Count < plan.MaximumProbes)
        {
            if (HasFailedTransition(transitions))
                break;
            DiffHistoryInterval? selected =
                SelectNextInterval(transitions);
            if (selected is null)
                break;

            int midpoint = selected.Source.Position
                + ((selected.Destination.Position
                    - selected.Source.Position) / 2);
            TPoint point = await evaluate(
                    population.Addresses[midpoint],
                    cancellationToken)
                .ConfigureAwait(false);
            chronological.Add(point);
            transitions = BuildTransitions(
                population,
                InPopulationOrder<T, TPoint>(chronological),
                compare);
            probes.Add(
                new(
                    probes.Count + 1,
                    DiffHistoryProbePurpose.AdaptiveMidpoint,
                    selected,
                    point,
                    Learning(transitions)));
            if (point.BlocksFurtherEvaluation)
                break;
        }
    }

    static ImmutableArray<TPoint> InPopulationOrder<T, TPoint>(
        ImmutableArray<TPoint>.Builder chronological)
        where T : notnull
        where TPoint : IDiffHistoryPoint<T> =>
        [
            .. chronological.OrderBy(
                static point => point.Address.Position),
        ];

    static ImmutableArray<DiffHistoryTransition<T>> BuildTransitions<T, TPoint>(
        PackageVersionVector population,
        ImmutableArray<TPoint> points,
        Func<TPoint, TPoint, FindingComparison<T>> compare)
        where T : notnull
        where TPoint : IDiffHistoryPoint<T>
    {
        var transitions =
            ImmutableArray.CreateBuilder<DiffHistoryTransition<T>>(
                Math.Max(0, points.Length - 1));
        for (int i = 1; i < points.Length; i++)
        {
            TPoint source = points[i - 1];
            TPoint destination = points[i];
            ImmutableArray<PackageVersionAddress> skipped =
            [
                .. population.Addresses.Where(address =>
                    address.Position > source.Address.Position
                    && address.Position < destination.Address.Position),
            ];
            transitions.Add(
                new(
                    source.Address,
                    destination.Address,
                    skipped,
                    compare(source, destination)));
        }
        return transitions.ToImmutable();
    }

    static ImmutableArray<DiffHistoryChangedVersionAssessment<T>>
        BuildChangedVersionAssessments<T, TPoint>(
            PackageVersionVector population,
            ImmutableArray<TPoint> points,
            ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull
        where TPoint : IDiffHistoryPoint<T>
    {
        HashSet<int> evaluated =
        [
            .. points.Select(static point => point.Address.Position),
        ];
        Dictionary<int, DiffHistoryTransition<T>> adjacentByDestination =
            transitions
                .Where(static transition =>
                    transition.IsPopulationAdjacent)
                .ToDictionary(static transition =>
                    transition.Destination.Position);
        var assessments =
            ImmutableArray.CreateBuilder<
                DiffHistoryChangedVersionAssessment<T>>(
                Math.Max(0, population.Addresses.Length - 1));
        for (int i = 1; i < population.Addresses.Length; i++)
        {
            PackageVersionAddress predecessor =
                population.Addresses[i - 1];
            PackageVersionAddress destination =
                population.Addresses[i];
            bool predecessorEvaluated =
                evaluated.Contains(predecessor.Position);
            bool destinationEvaluated =
                evaluated.Contains(destination.Position);
            if (!predecessorEvaluated || !destinationEvaluated)
            {
                assessments.Add(
                    new(
                        predecessor,
                        destination,
                        DiffHistoryChangedVersionState.Unevaluated,
                        comparison: null,
                        predecessorEvaluated,
                        destinationEvaluated));
                continue;
            }

            FindingComparison<T> comparison =
                adjacentByDestination[destination.Position].Comparison;
            DiffHistoryChangedVersionState state =
                comparison.Value switch
                {
                    FindingComparison<T>.Failed =>
                        DiffHistoryChangedVersionState.Failed,
                    FindingComparison<T>.Complete complete
                        when complete.Transition.Old
                                == FindingInspectionState.NoApplicableInput
                            || complete.Transition.New
                                == FindingInspectionState.NoApplicableInput =>
                        DiffHistoryChangedVersionState.Inapplicable,
                    FindingComparison<T>.Complete =>
                        comparison.IsExact
                            ? DiffHistoryChangedVersionState.Unchanged
                            : DiffHistoryChangedVersionState.Changed,
                    _ => throw new InvalidOperationException(
                        "Unknown Finding comparison outcome."),
                };
            assessments.Add(
                new(
                    predecessor,
                    destination,
                    state,
                    comparison,
                    predecessorEvaluated: true,
                    destinationEvaluated: true));
        }
        return assessments.ToImmutable();
    }

    static DiffHistoryProbeLearning Learning<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull
    {
        ImmutableArray<DiffHistoryInterval> changed =
            ChangedIntervals(transitions);
        if (HasFailedTransition(transitions))
        {
            return new(
                DiffHistoryProbeLearningKind.BlockedByFailure,
                changed);
        }
        return changed.IsEmpty
            ? new(DiffHistoryProbeLearningKind.NoChangeObserved)
            : new(
                DiffHistoryProbeLearningKind.ChangedIntervals,
                changed);
    }

    static DiffHistoryTerminalOutcome BuildTerminalOutcome<T, TPoint>(
        PackageVersionVector population,
        DiffHistoryEvaluationPlan evaluationPlan,
        ImmutableArray<TPoint> points,
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull
        where TPoint : IDiffHistoryPoint<T>
    {
        ImmutableArray<DiffHistoryInterval> changed =
            ChangedIntervals(transitions);
        ImmutableArray<DiffHistoryInterval> boundaries =
        [
            .. changed.Where(static interval => interval.IsAdjacent),
        ];
        ImmutableArray<DiffHistoryInterval> unresolved =
        [
            .. changed.Where(static interval => !interval.IsAdjacent),
        ];
        ImmutableArray<PackageVersionAddress> failedAddresses =
        [
            .. points
                .Where(static point =>
                    point.Inspection.Value is FindingInspection<T>.Failed)
                .Select(static point => point.Address),
        ];
        ImmutableArray<DiffHistoryInterval> failed =
            FailedIntervals(transitions);
        if (!failedAddresses.IsEmpty || !failed.IsEmpty)
        {
            return new DiffHistoryTerminalOutcome.BlockedByFailure(
                boundaries,
                unresolved,
                failedAddresses,
                failed);
        }
        if (evaluationPlan
            is DiffHistoryEvaluationPlan.FullPopulation)
        {
            return new DiffHistoryTerminalOutcome
                .FullPopulationCompleted();
        }
        if (evaluationPlan
            is DiffHistoryEvaluationPlan.ExplicitCheckpoints)
        {
            return new DiffHistoryTerminalOutcome
                .ExplicitCheckpointsCompleted();
        }
        if (evaluationPlan
            is DiffHistoryEvaluationPlan.RepresentativeSurvey)
        {
            return new DiffHistoryTerminalOutcome
                .RepresentativeSurveyCompleted();
        }
        if (!unresolved.IsEmpty)
        {
            return new DiffHistoryTerminalOutcome.BudgetExhausted(
                boundaries,
                unresolved);
        }
        if (!boundaries.IsEmpty)
        {
            return new DiffHistoryTerminalOutcome.BoundariesResolved(
                boundaries);
        }
        return new DiffHistoryTerminalOutcome.EqualEndpoints(
            new(
                population.Addresses[0],
                population.Addresses[^1]));
    }

    static ImmutableArray<DiffHistoryNextAction> BuildNextActions<T, TPoint>(
        PackageVersionVector population,
        DiffHistoryEvaluationPlan evaluationPlan,
        ImmutableArray<TPoint> points,
        ImmutableArray<DiffHistoryTransition<T>> transitions,
        Func<
            DiffHistoryInterval,
            DiffHistoryNextAction.PairwiseDiff> pairwiseAction)
        where T : notnull
        where TPoint : IDiffHistoryPoint<T>
    {
        if (evaluationPlan
            is DiffHistoryEvaluationPlan.ExplicitCheckpoints)
        {
            DiffHistoryInterval? interval =
                SelectNextInterval(transitions);
            if (interval is null)
                return [];
            int midpoint = interval.Source.Position
                + ((interval.Destination.Position
                    - interval.Source.Position) / 2);
            PackageVersionAddress address =
                population.Addresses[midpoint];
            return
            [
                new DiffHistoryNextAction.Probe(
                    interval,
                    address,
                    points.Select(static point => point.Address)
                        .Append(address)
                        .OrderBy(static value => value.Position)
                        .ToImmutableArray()),
            ];
        }
        if (evaluationPlan
            is DiffHistoryEvaluationPlan.RepresentativeSurvey)
        {
            return
            [
                .. ChangedIntervals(transitions)
                    .Where(static interval => interval.IsAdjacent)
                    .Select(interval =>
                        (DiffHistoryNextAction)pairwiseAction(interval)),
            ];
        }
        if (evaluationPlan
            is not DiffHistoryEvaluationPlan.AdaptiveBisect)
        {
            return [];
        }
        return
        [
            .. ChangedIntervals(transitions)
                .Where(static interval => interval.IsAdjacent)
                .Select(interval =>
                    (DiffHistoryNextAction)pairwiseAction(interval)),
        ];
    }

    static DiffHistoryInterval? SelectNextInterval<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull =>
        ChangedIntervals(transitions)
            .Where(static interval => !interval.IsAdjacent)
            .OrderByDescending(static interval =>
                interval.Destination.Position - interval.Source.Position)
            .ThenBy(static interval => interval.Source.Position)
            .FirstOrDefault();

    static ImmutableArray<DiffHistoryInterval> ChangedIntervals<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull =>
        [
            .. transitions
                .Where(static transition =>
                    transition.Comparison.Value
                        is FindingComparison<T>.Complete
                    && !transition.Comparison.IsExact)
                .Select(static transition => new DiffHistoryInterval(
                    transition.Source,
                    transition.Destination)),
        ];

    static ImmutableArray<DiffHistoryInterval> FailedIntervals<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull =>
        [
            .. transitions
                .Where(static transition =>
                    transition.Comparison.Value
                        is FindingComparison<T>.Failed)
                .Select(static transition => new DiffHistoryInterval(
                    transition.Source,
                    transition.Destination)),
        ];

    static bool HasFailedTransition<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull =>
        transitions.Any(static transition =>
            transition.Comparison.Value
                is FindingComparison<T>.Failed);
}
