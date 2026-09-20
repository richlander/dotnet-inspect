using System.Diagnostics;
using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Ephemeral selected-target state passed only to the next phase of the same
/// closed House operation.
/// </summary>
public sealed class PlatformTargetSelectionContext
{
    private readonly IReadOnlyDictionary<
        PlatformSourceCapabilityIdentity,
        SelectedAssociation> _selectedByCapability;

    internal PlatformTargetSelectionContext(
        PlatformTargetSettlement.Selected targetSettlement,
        IEnumerable<PlatformTargetDiscoveryCandidate> selectedCandidates,
        IReadOnlyDictionary<
            PlatformSourceCapabilityIdentity,
            SelectedAssociation> selectedByCapability,
        IEnumerable<PlatformSourceSettlement> sourceSettlements,
        PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(targetSettlement);
        ArgumentNullException.ThrowIfNull(selectedCandidates);
        ArgumentNullException.ThrowIfNull(selectedByCapability);
        ArgumentNullException.ThrowIfNull(sourceSettlements);
        ArgumentNullException.ThrowIfNull(consumedWork);
        TargetSettlement = targetSettlement;
        SelectedCandidates = Array.AsReadOnly(
            selectedCandidates.ToArray());
        _selectedByCapability = selectedByCapability;
        SourceSettlements = Array.AsReadOnly(
            sourceSettlements.ToArray());
        ConsumedWork = consumedWork;
    }

    public PlatformFamilyTarget Target =>
        TargetSettlement.SettledTarget!;
    public PlatformTargetSettlement.Selected TargetSettlement { get; }
    public IReadOnlyList<PlatformTargetDiscoveryCandidate>
        SelectedCandidates { get; }
    public IReadOnlyList<PlatformSourceSettlement> SourceSettlements { get; }
    public PlatformHouseConsumedWork ConsumedWork { get; }

    internal PlatformTargetDiscoveryCandidate? CandidateFor(
        PlatformSourceCapabilityIdentity capability,
        PlatformSourceAssociationRouteIdentity route)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(route);
        return _selectedByCapability.TryGetValue(
                capability,
                out SelectedAssociation? association)
            && ReferenceEquals(association.Route, route)
                ? association.Candidate
                : null;
    }

    internal sealed record SelectedAssociation(
        PlatformTargetDiscoveryCandidate Candidate,
        PlatformSourceAssociationRouteIdentity? Route);
}

public delegate ValueTask<PlatformHouseOutcome<TValue>>
    PlatformTargetSelectionContinuation<TValue>(
        PlatformTargetSelectionContext selection)
    where TValue : notnull;

/// <summary>
/// Executes the typed preferred/fallback target policy and continues the same
/// closed House operation after one exact target is frozen.
/// </summary>
public static class PlatformHouseTargetSelector
{
    public static async ValueTask<PlatformHouseOutcome<TValue>> ExecuteAsync<
        TValue>(
        PlatformHouseRequest request,
        IReadOnlyList<PlatformTargetDiscoverySource> sources,
        PlatformTargetSelectionContinuation<TValue> continuation)
        where TValue : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(continuation);

        request.CancellationToken.ThrowIfCancellationRequested();

        PlatformHouseRequestValidation validation =
            PlatformHouseRequestValidation.Validate(request);
        if (validation
            is PlatformHouseRequestValidation.Rejected invalidRequest)
        {
            return Rejected<TValue>(
                request,
                ZeroConsumed(),
                [],
                invalidRequest.Rejection);
        }
        if (request.Target
            is not PlatformTargetDemand.FamilyDefault demand)
        {
            return Rejected<TValue>(
                request,
                ZeroConsumed(),
                [],
                OwnerRejection(
                    PlatformHouseRejectionKind.InvalidRequest,
                    "target-selection-family-default-required"));
        }

        PlatformTargetDiscoverySource[] sourceSnapshot = [.. sources];
        if (!TryIndexSources(
                demand,
                sourceSnapshot,
                out IReadOnlyDictionary<
                    PlatformSourceCapabilityIdentity,
                    PlatformTargetDiscoverySource> indexedSources))
        {
            return Rejected<TValue>(
                request,
                ZeroConsumed(),
                [],
                OwnerRejection(
                    PlatformHouseRejectionKind.InvalidSourcePlan,
                    "target-selection-source-set-invalid"));
        }

        var work = new SelectionWork(request);
        var executed = new List<ExecutedDiscovery>();
        if (demand.Policy.Preferred is { } preferred)
        {
            StageExecution<TValue> preferredResult =
                await ExecuteStageAsync<TValue>(
                    request,
                    demand,
                    preferred,
                    indexedSources,
                    executed,
                    work)
                .ConfigureAwait(false);
            if (preferredResult.Terminal is not null)
                return preferredResult.Terminal;
            if (preferredResult.Winner is not null)
            {
                return await ContinueAsync(
                    request,
                    demand,
                    preferred,
                    preferredResult.Winner,
                    executed,
                    work,
                    continuation)
                    .ConfigureAwait(false);
            }
        }

        StageExecution<TValue> fallbackResult =
            await ExecuteStageAsync<TValue>(
                request,
                demand,
                demand.Policy.Fallback,
                indexedSources,
                executed,
                work)
            .ConfigureAwait(false);
        if (fallbackResult.Terminal is not null)
            return fallbackResult.Terminal;
        if (fallbackResult.Winner is not null)
        {
            return await ContinueAsync(
                request,
                demand,
                demand.Policy.Fallback,
                fallbackResult.Winner,
                executed,
                work,
                continuation)
                .ConfigureAwait(false);
        }

        request.CancellationToken.ThrowIfCancellationRequested();
        PlatformHouseConsumedWork finalWork = work.Consumed();
        if (finalWork.Elapsed > request.Work.MaxDuration)
        {
            return Incomplete<TValue>(
                request,
                finalWork,
                OutcomeRelevant(executed),
                "target-selection-duration-exhausted");
        }
        return Unavailable<TValue>(
            request,
            finalWork,
            OutcomeRelevant(executed),
            "target-selection-no-eligible-target");
    }

    static async ValueTask<StageExecution<TValue>> ExecuteStageAsync<TValue>(
        PlatformHouseRequest request,
        PlatformTargetDemand.FamilyDefault demand,
        PlatformTargetDiscoveryStage stage,
        IReadOnlyDictionary<
            PlatformSourceCapabilityIdentity,
            PlatformTargetDiscoverySource> sources,
        List<ExecutedDiscovery> executed,
        SelectionWork work)
        where TValue : notnull
    {
        StageWinner? winner = null;
        foreach (PlatformSourceCapabilityIdentity capability
            in stage.Capabilities)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            if (!work.CanInvokeSource())
            {
                return new(
                    null,
                    Incomplete<TValue>(
                        request,
                        work.Consumed(),
                        OutcomeRelevant(executed),
                        "target-selection-source-work-exhausted"));
            }

            PlatformTargetDiscoverySource source =
                sources[capability];
            PlatformHouseWorkBudget remainingWork =
                work.Remaining();
            PlatformTargetDiscoveryAttempt attempt =
                await source.DiscoverAsync(request, remainingWork)
                    .ConfigureAwait(false);
            work.ChargeSourceOperation();
            request.CancellationToken.ThrowIfCancellationRequested();
            PlatformHouseConsumedWork postSourceWork = work.Consumed();
            if (postSourceWork.Elapsed > request.Work.MaxDuration)
            {
                if (AttemptCorresponds(
                    request,
                    capability,
                    attempt))
                {
                    executed.Add(
                        new(
                            capability,
                            attempt,
                            stage,
                            source.AssociationRoute));
                }
                return new(
                    null,
                    Incomplete<TValue>(
                        request,
                        postSourceWork,
                        OutcomeRelevant(executed),
                        "target-selection-duration-exhausted"));
            }
            if (!AttemptCorresponds(
                request,
                capability,
                attempt))
            {
                return new(
                    null,
                    Rejected<TValue>(
                        request,
                        postSourceWork,
                        OutcomeRelevant(executed),
                        OwnerRejection(
                            PlatformHouseRejectionKind.InvalidOwnerResult,
                            "target-selection-attempt-mismatch")));
            }

            var executedAttempt =
                new ExecutedDiscovery(
                    capability,
                    attempt,
                    stage,
                    source.AssociationRoute);
            executed.Add(executedAttempt);
            if (attempt
                is PlatformTargetDiscoveryAttempt.NotSucceeded terminal)
            {
                if (terminal.Contribution
                    is PlatformSourceContribution.Unavailable
                    {
                        Reason:
                            PlatformSourceUnavailabilityKind.Absent,
                    })
                {
                    continue;
                }

                PlatformHouseOutcome<TValue> outcome =
                    terminal.Contribution switch
                    {
                        PlatformSourceContribution.Unavailable =>
                            Unavailable<TValue>(
                                request,
                                postSourceWork,
                                OutcomeRelevant(executed),
                                "target-selection-source-unavailable"),
                        PlatformSourceContribution.Rejected =>
                            Rejected<TValue>(
                                request,
                                postSourceWork,
                                OutcomeRelevant(executed),
                                OwnerRejection(
                                    terminal.RejectionKind!.Value,
                                    "target-selection-source-rejected")),
                        PlatformSourceContribution.Incomplete =>
                            Incomplete<TValue>(
                                request,
                                postSourceWork,
                                OutcomeRelevant(executed),
                                "target-selection-source-incomplete"),
                        PlatformSourceContribution.Failed =>
                            Failed<TValue>(
                                request,
                                postSourceWork,
                                OutcomeRelevant(executed),
                                "target-selection-source-failed"),
                        _ => throw new InvalidOperationException(
                            "Unknown target-discovery terminal contribution."),
                    };
                return new(null, outcome);
            }

            var success =
                (PlatformTargetDiscoveryAttempt.Succeeded)attempt;
            if (!work.ChargeCandidates(success.Candidates.Count))
            {
                return new(
                    null,
                    Incomplete<TValue>(
                        request,
                        work.Consumed(),
                        OutcomeRelevant(executed),
                        "target-selection-candidate-work-exhausted"));
            }
            foreach (PlatformTargetDiscoveryCandidate candidate
                in success.Candidates)
            {
                request.CancellationToken.ThrowIfCancellationRequested();
                if (work.DurationExceeded
                    || !work.TryChargeComparison())
                {
                    return new(
                        null,
                        Incomplete<TValue>(
                            request,
                            work.Consumed(),
                            OutcomeRelevant(executed),
                            work.DurationExceeded
                                ? "target-selection-duration-exhausted"
                                : "target-selection-comparison-work-exhausted"));
                }

                int minimumComparison =
                    PlatformVersion.SemanticPrecedenceComparer.Compare(
                        candidate.Target.Version,
                        demand.Policy.MinimumPreferredVersion);
                if (!demand.IsEligibleSelection(
                    capability,
                    candidate.Target,
                    minimumComparison))
                {
                    continue;
                }

                if (winner is null)
                {
                    winner = new(candidate.Target, [candidate]);
                    continue;
                }
                if (!work.TryChargeComparison())
                {
                    return new(
                        null,
                        Incomplete<TValue>(
                            request,
                            work.Consumed(),
                            OutcomeRelevant(executed),
                            "target-selection-comparison-work-exhausted"));
                }

                int precedence =
                    PlatformVersion.SemanticPrecedenceComparer.Compare(
                        candidate.Target.Version,
                        winner.Target.Version);
                if (precedence > 0)
                {
                    winner = new(candidate.Target, [candidate]);
                    continue;
                }
                if (precedence < 0)
                    continue;

                if (!work.TryChargeComparison())
                {
                    return new(
                        null,
                        Incomplete<TValue>(
                            request,
                            work.Consumed(),
                            OutcomeRelevant(executed),
                            "target-selection-comparison-work-exhausted"));
                }

                int identity = string.CompareOrdinal(
                    candidate.Target.Version.Value,
                    winner.Target.Version.Value);
                if (identity > 0)
                {
                    winner = new(candidate.Target, [candidate]);
                }
                else if (identity == 0)
                {
                    winner.Candidates.Add(candidate);
                }
            }
        }

        return new(winner, null);
    }

    static async ValueTask<PlatformHouseOutcome<TValue>> ContinueAsync<TValue>(
        PlatformHouseRequest request,
        PlatformTargetDemand.FamilyDefault demand,
        PlatformTargetDiscoveryStage selectedStage,
        StageWinner winner,
        IReadOnlyList<ExecutedDiscovery> executed,
        SelectionWork work,
        PlatformTargetSelectionContinuation<TValue> continuation)
        where TValue : notnull
    {
        request.CancellationToken.ThrowIfCancellationRequested();
        PlatformHouseConsumedWork consumed = work.Consumed();
        if (consumed.Elapsed >= request.Work.MaxDuration)
        {
            return Incomplete<TValue>(
                request,
                consumed,
                OutcomeRelevant(executed),
                "target-selection-duration-exhausted");
        }

        IReadOnlyList<PlatformSourceSettlement> settlements =
            SelectedSettlements(
                selectedStage,
                winner.Target,
                executed);
        PlatformSourceContribution[] selectedContributions =
            [.. settlements
                .Where(
                    settlement => settlement.Disposition
                        == PlatformSourceSettlementDisposition.Selected)
                .Select(settlement => settlement.Contribution)];
        var targetSettlement = new PlatformTargetSettlement.Selected(
            demand,
            winner.Target,
            selectedContributions);
        var context = new PlatformTargetSelectionContext(
            targetSettlement,
            winner.Candidates,
            SelectedCandidatesByCapability(
                winner.Target,
                executed),
            settlements,
            consumed);
        return await continuation(context).ConfigureAwait(false);
    }

    static IReadOnlyDictionary<
        PlatformSourceCapabilityIdentity,
        PlatformTargetSelectionContext.SelectedAssociation>
        SelectedCandidatesByCapability(
            PlatformFamilyTarget target,
            IReadOnlyList<ExecutedDiscovery> executed)
    {
        var result = new Dictionary<
            PlatformSourceCapabilityIdentity,
            PlatformTargetSelectionContext.SelectedAssociation>(
                ReferenceEqualityComparer.Instance);
        foreach (ExecutedDiscovery discovery in executed)
        {
            if (discovery.Attempt
                is not PlatformTargetDiscoveryAttempt.Succeeded success)
            {
                continue;
            }
            PlatformTargetDiscoveryCandidate? candidate =
                success.Candidates.SingleOrDefault(
                    item => item.Target == target);
            if (candidate is not null)
            {
                result.Add(
                    discovery.Capability,
                    new(
                        candidate,
                        discovery.AssociationRoute));
            }
        }
        return result;
    }

    static bool TryIndexSources(
        PlatformTargetDemand.FamilyDefault demand,
        IEnumerable<PlatformTargetDiscoverySource> sources,
        out IReadOnlyDictionary<
            PlatformSourceCapabilityIdentity,
            PlatformTargetDiscoverySource> indexed)
    {
        var result = new Dictionary<
            PlatformSourceCapabilityIdentity,
            PlatformTargetDiscoverySource>(
            ReferenceEqualityComparer.Instance);
        foreach (PlatformTargetDiscoverySource source in sources)
        {
            if (source is null
                || !demand.AuthorizesDiscoveryCapability(source.Capability)
                || !result.TryAdd(source.Capability, source))
            {
                indexed = result;
                return false;
            }
        }

        if (result.Count != demand.AuthorizedDiscoveryCapabilities.Count
            || demand.AuthorizedDiscoveryCapabilities.Any(
                capability => !result.ContainsKey(capability)))
        {
            indexed = result;
            return false;
        }

        indexed = result;
        return true;
    }

    static bool AttemptCorresponds(
        PlatformHouseRequest request,
        PlatformSourceCapabilityIdentity capability,
        PlatformTargetDiscoveryAttempt attempt) =>
        attempt is not null
        && ReferenceEquals(attempt.Contribution.Request, request.Snapshot)
        && ReferenceEquals(
            attempt.Contribution.Capability,
            capability);

    static IReadOnlyList<PlatformSourceSettlement> SelectedSettlements(
        PlatformTargetDiscoveryStage selectedStage,
        PlatformFamilyTarget target,
        IEnumerable<ExecutedDiscovery> executed) =>
        Array.AsReadOnly(
            executed.Select(
                    item => new PlatformSourceSettlement(
                        item.Attempt.Contribution,
                        ReferenceEquals(item.Stage, selectedStage)
                            ? item.Attempt
                                    is PlatformTargetDiscoveryAttempt.Succeeded
                                        success
                                && success.Candidates.Any(
                                    candidate =>
                                        candidate.Target == target)
                                    ? PlatformSourceSettlementDisposition
                                        .Selected
                                    : item.Attempt
                                        is PlatformTargetDiscoveryAttempt
                                            .Succeeded
                                        ? PlatformSourceSettlementDisposition
                                            .Shadowed
                                        : PlatformSourceSettlementDisposition
                                            .OutcomeRelevant
                            : PlatformSourceSettlementDisposition
                                .OutcomeRelevant))
                .ToArray());

    static IReadOnlyList<PlatformSourceSettlement> OutcomeRelevant(
        IEnumerable<ExecutedDiscovery> executed) =>
        Array.AsReadOnly(
            executed.Select(
                    item => new PlatformSourceSettlement(
                        item.Attempt.Contribution,
                        PlatformSourceSettlementDisposition.OutcomeRelevant))
                .ToArray());

    static PlatformHouseOutcome<TValue> Unavailable<TValue>(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork work,
        IEnumerable<PlatformSourceSettlement> settlements,
        string evidenceName)
        where TValue : notnull
    {
        var termination = new PlatformHouseTermination.Unavailable(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Unsettled(request.Target),
            settlements,
            work,
            termination: termination);
        return new PlatformHouseOutcome<TValue>.Unavailable(
            termination,
            receipt);
    }

    static PlatformHouseOutcome<TValue> Rejected<TValue>(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork work,
        IEnumerable<PlatformSourceSettlement> settlements,
        PlatformHouseRejection rejection)
        where TValue : notnull
    {
        var termination = new PlatformHouseTermination.Rejected(rejection);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Unsettled(request.Target),
            settlements,
            work,
            termination: termination);
        return new PlatformHouseOutcome<TValue>.Rejected(
            termination,
            receipt);
    }

    static PlatformHouseOutcome<TValue> Incomplete<TValue>(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork work,
        IEnumerable<PlatformSourceSettlement> settlements,
        string evidenceName)
        where TValue : notnull
    {
        var termination = new PlatformHouseTermination.Incomplete(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Unsettled(request.Target),
            settlements,
            work,
            termination: termination);
        return new PlatformHouseOutcome<TValue>.Incomplete(
            termination,
            receipt);
    }

    static PlatformHouseOutcome<TValue> Failed<TValue>(
        PlatformHouseRequest request,
        PlatformHouseConsumedWork work,
        IEnumerable<PlatformSourceSettlement> settlements,
        string evidenceName)
        where TValue : notnull
    {
        var termination = new PlatformHouseTermination.Failed(
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName),
            [PlatformHouseFailureKind.Source]);
        var receipt = new PlatformHouseReceipt(
            request.Snapshot,
            new PlatformTargetSettlement.Unsettled(request.Target),
            settlements,
            work,
            termination: termination);
        return new PlatformHouseOutcome<TValue>.Failed(
            termination,
            receipt);
    }

    static PlatformHouseRejection OwnerRejection(
        PlatformHouseRejectionKind kind,
        string evidenceName) =>
        new PlatformHouseRejection.OwnerEvidence(
            kind,
            PlatformHouseTerminalEvidenceIdentity.Create(evidenceName));

    static PlatformHouseConsumedWork ZeroConsumed() =>
        new(
            sourceOperations: 0,
            targetCandidates: 0,
            assemblies: 0,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: 0,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

    sealed record ExecutedDiscovery(
        PlatformSourceCapabilityIdentity Capability,
        PlatformTargetDiscoveryAttempt Attempt,
        PlatformTargetDiscoveryStage Stage,
        PlatformSourceAssociationRouteIdentity? AssociationRoute);

    sealed class StageWinner
    {
        public StageWinner(
            PlatformFamilyTarget target,
            IEnumerable<PlatformTargetDiscoveryCandidate> candidates)
        {
            Target = target;
            Candidates = [.. candidates];
        }

        public PlatformFamilyTarget Target { get; set; }
        public List<PlatformTargetDiscoveryCandidate> Candidates { get; }
    }

    sealed record StageExecution<TValue>(
        StageWinner? Winner,
        PlatformHouseOutcome<TValue>? Terminal)
        where TValue : notnull;

    sealed class SelectionWork
    {
        private readonly PlatformHouseRequest _request;
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _sourceOperations;
        private int _targetCandidates;
        private int _targetComparisons;

        public SelectionWork(PlatformHouseRequest request) =>
            _request = request;

        public bool DurationExceeded =>
            Elapsed > _request.Work.MaxDuration;

        public bool CanInvokeSource() =>
            _sourceOperations < _request.Work.MaxSourceOperations
            && Elapsed < _request.Work.MaxDuration;

        public void ChargeSourceOperation() =>
            _sourceOperations = checked(_sourceOperations + 1);

        public PlatformHouseWorkBudget Remaining() =>
            new(
                Remaining(_request.Work.MaxSourceOperations, _sourceOperations),
                Remaining(_request.Work.MaxTargetCandidates, _targetCandidates),
                _request.Work.MaxAssemblies,
                _request.Work.MaxXmlDocuments,
                _request.Work.MaxPortablePdbs,
                _request.Work.MaxSourceDocuments,
                _request.Work.MaxBytes,
                _request.Work.MaxForwardingHops,
                Remaining(_request.Work.MaxDuration, Elapsed));

        public bool ChargeCandidates(int count)
        {
            long total = (long)_targetCandidates + count;
            _targetCandidates =
                total > int.MaxValue ? int.MaxValue : (int)total;
            PlatformTargetDiscoveryBudget budget =
                _request.Target.DiscoveryWork!;
            return total <= budget.MaxCandidates
                && total <= _request.Work.MaxTargetCandidates;
        }

        public bool TryChargeComparison()
        {
            PlatformTargetDiscoveryBudget budget =
                _request.Target.DiscoveryWork!;
            if (_targetComparisons >= budget.MaxComparisons)
                return false;
            _targetComparisons++;
            return true;
        }

        public PlatformHouseConsumedWork Consumed() =>
            new(
                _sourceOperations,
                _targetCandidates,
                assemblies: 0,
                xmlDocuments: 0,
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes: 0,
                forwardingHops: 0,
                _targetComparisons,
                Elapsed);

        static int Remaining(int maximum, int consumed) =>
            Math.Max(0, maximum - consumed);

        static TimeSpan Remaining(
            TimeSpan maximum,
            TimeSpan consumed) =>
            consumed >= maximum
                ? TimeSpan.Zero
                : maximum - consumed;

        TimeSpan Elapsed => Stopwatch.GetElapsedTime(_started);
    }
}
