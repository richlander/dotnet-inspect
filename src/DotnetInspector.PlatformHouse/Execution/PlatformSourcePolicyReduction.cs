using DotnetInspector.SourceSelection;

namespace DotnetInspector.PlatformHouse;

internal interface IPlatformSourcePolicyAttempt
{
    PlatformSourceContribution Contribution { get; }
    bool Succeeded { get; }
    PlatformHouseCandidateIdentity? Candidate { get; }
    PlatformHouseRejectionKind? RejectionKind { get; }
}

internal enum PlatformSourcePolicyDecisionKind
{
    Selected,
    Unavailable,
    Ambiguous,
    Rejected,
    Incomplete,
    Failed,
}

internal sealed record PlatformSourcePolicyDecision<TAttempt>(
    PlatformSourcePolicyDecisionKind Kind,
    IReadOnlyList<PlatformSourceSettlement> Settlements,
    TAttempt? Selected = default,
    IReadOnlyList<PlatformHouseCandidateIdentity>? AmbiguousCandidates = null,
    PlatformHouseRejectionKind? RejectionKind = null)
    where TAttempt : class, IPlatformSourcePolicyAttempt
{
    internal IReadOnlyList<PlatformHouseCandidateIdentity> Candidates =>
        AmbiguousCandidates
        ?? Array.Empty<PlatformHouseCandidateIdentity>();
}

internal static class PlatformSourcePolicyReducer
{
    internal static PlatformSourcePolicyDecision<TAttempt> Select<TAttempt>(
        PlatformSourceSelection selection,
        IReadOnlyDictionary<
            PlatformSourceCapabilityIdentity,
            TAttempt> attempts)
        where TAttempt : class, IPlatformSourcePolicyAttempt
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(attempts);
        return selection.Mode == PlatformSourceSelectionMode.Aggregation
            ? SelectAggregation(selection, attempts)
            : SelectOrdered(selection, attempts);
    }

    internal static IReadOnlyList<PlatformSourceSettlement>
        TerminalSettlements(
            IEnumerable<PlatformSourceSettlement> settlements)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        return Array.AsReadOnly(
            settlements.Select(
                settlement => new PlatformSourceSettlement(
                    settlement.Contribution,
                    settlement.Disposition
                        == PlatformSourceSettlementDisposition.Selected
                            ? PlatformSourceSettlementDisposition
                                .OutcomeRelevant
                            : settlement.Disposition))
                .ToArray());
    }

    static PlatformSourcePolicyDecision<TAttempt> SelectOrdered<TAttempt>(
        PlatformSourceSelection selection,
        IReadOnlyDictionary<
            PlatformSourceCapabilityIdentity,
            TAttempt> attempts)
        where TAttempt : class, IPlatformSourcePolicyAttempt
    {
        bool sawFallbackFailure = false;
        for (int index = 0;
            index < selection.Capabilities.Count;
            index++)
        {
            PlatformSourceCapabilityIdentity capability =
                selection.Capabilities[index];
            if (!attempts.TryGetValue(capability, out TAttempt? attempt))
            {
                return Incomplete<TAttempt>(
                    BuildTerminalSettlements(
                        selection,
                        attempts,
                        index - 1));
            }

            if (attempt.Succeeded)
            {
                return Selected(
                    attempt,
                    BuildSelectedSettlements(
                        selection,
                        attempts,
                        index,
                        aggregation: false));
            }

            switch (attempt.Contribution)
            {
                case PlatformSourceContribution.Rejected:
                    return Rejected<TAttempt>(
                        attempt.RejectionKind!.Value,
                        BuildTerminalSettlements(
                            selection,
                            attempts,
                            index));
                case PlatformSourceContribution.Incomplete:
                    return Incomplete<TAttempt>(
                        BuildTerminalSettlements(
                            selection,
                            attempts,
                            index));
                case PlatformSourceContribution.Failed
                    when selection.Mode
                        == PlatformSourceSelectionMode.Precedence:
                    return Failed<TAttempt>(
                        BuildTerminalSettlements(
                            selection,
                            attempts,
                            index));
                case PlatformSourceContribution.Failed:
                    sawFallbackFailure = true;
                    break;
            }
        }

        IReadOnlyList<PlatformSourceSettlement> settlements =
            BuildTerminalSettlements(
                selection,
                attempts,
                selection.Capabilities.Count - 1);
        return sawFallbackFailure
            ? Failed<TAttempt>(settlements)
            : Unavailable<TAttempt>(settlements);
    }

    static PlatformSourcePolicyDecision<TAttempt>
        SelectAggregation<TAttempt>(
            PlatformSourceSelection selection,
            IReadOnlyDictionary<
                PlatformSourceCapabilityIdentity,
                TAttempt> attempts)
        where TAttempt : class, IPlatformSourcePolicyAttempt
    {
        IReadOnlyList<PlatformSourceSettlement> terminalSettlements =
            BuildAggregationTerminalSettlements(selection, attempts);
        if (attempts.Values.Any(
                attempt => attempt.Contribution
                    is PlatformSourceContribution.Failed))
        {
            return Failed<TAttempt>(terminalSettlements);
        }
        if (selection.Capabilities.Any(
                capability => !attempts.ContainsKey(capability)))
        {
            return Incomplete<TAttempt>(terminalSettlements);
        }

        foreach (PlatformSourceCapabilityIdentity capability
            in selection.Capabilities)
        {
            TAttempt attempt = attempts[capability];
            if (attempt.Contribution
                    is PlatformSourceContribution.Rejected)
            {
                return Rejected<TAttempt>(
                    attempt.RejectionKind!.Value,
                    terminalSettlements);
            }
        }
        if (attempts.Values.Any(
                attempt => attempt.Contribution
                    is PlatformSourceContribution.Incomplete))
        {
            return Incomplete<TAttempt>(terminalSettlements);
        }
        TAttempt[] successes =
            [.. selection.Capabilities
                .Select(capability => attempts[capability])
                .Where(attempt => attempt.Succeeded)];
        if (successes.Length > 1)
        {
            return Ambiguous<TAttempt>(
                [.. successes.Select(success => success.Candidate!)],
                terminalSettlements);
        }
        if (successes.Length == 1
            && attempts.Values
                .Where(attempt => !ReferenceEquals(
                    attempt,
                    successes[0]))
                .All(
                    attempt => attempt.Contribution
                        is PlatformSourceContribution.Unavailable
                        {
                            Reason:
                                PlatformSourceUnavailabilityKind.Absent,
                        }))
        {
            int selectedIndex = IndexOf(
                selection.Capabilities,
                successes[0].Contribution.Capability);
            return Selected(
                successes[0],
                BuildSelectedSettlements(
                    selection,
                    attempts,
                    selectedIndex,
                    aggregation: true));
        }
        return Unavailable<TAttempt>(terminalSettlements);
    }

    static IReadOnlyList<PlatformSourceSettlement>
        BuildSelectedSettlements<TAttempt>(
            PlatformSourceSelection selection,
            IReadOnlyDictionary<
                PlatformSourceCapabilityIdentity,
                TAttempt> attempts,
            int selectedIndex,
            bool aggregation)
        where TAttempt : class, IPlatformSourcePolicyAttempt
    {
        var settlements = new List<PlatformSourceSettlement>(
            attempts.Count);
        for (int index = 0;
            index < selection.Capabilities.Count;
            index++)
        {
            if (!attempts.TryGetValue(
                    selection.Capabilities[index],
                    out TAttempt? attempt))
            {
                continue;
            }
            PlatformSourceSettlementDisposition disposition =
                index == selectedIndex
                    ? PlatformSourceSettlementDisposition.Selected
                    : aggregation || index < selectedIndex
                        ? PlatformSourceSettlementDisposition.OutcomeRelevant
                        : PlatformSourceSettlementDisposition.Shadowed;
            settlements.Add(
                new PlatformSourceSettlement(
                    attempt.Contribution,
                    disposition));
        }
        return settlements.AsReadOnly();
    }

    static IReadOnlyList<PlatformSourceSettlement>
        BuildTerminalSettlements<TAttempt>(
            PlatformSourceSelection selection,
            IReadOnlyDictionary<
                PlatformSourceCapabilityIdentity,
                TAttempt> attempts,
            int terminalIndex)
        where TAttempt : class, IPlatformSourcePolicyAttempt
    {
        var settlements = new List<PlatformSourceSettlement>(
            attempts.Count);
        for (int index = 0;
            index < selection.Capabilities.Count;
            index++)
        {
            if (!attempts.TryGetValue(
                    selection.Capabilities[index],
                    out TAttempt? attempt))
            {
                continue;
            }
            settlements.Add(
                new PlatformSourceSettlement(
                    attempt.Contribution,
                    index <= terminalIndex
                        ? PlatformSourceSettlementDisposition.OutcomeRelevant
                        : PlatformSourceSettlementDisposition.Shadowed));
        }
        return settlements.AsReadOnly();
    }

    static IReadOnlyList<PlatformSourceSettlement>
        BuildAggregationTerminalSettlements<TAttempt>(
            PlatformSourceSelection selection,
            IReadOnlyDictionary<
                PlatformSourceCapabilityIdentity,
                TAttempt> attempts)
        where TAttempt : class, IPlatformSourcePolicyAttempt
    {
        var settlements = new List<PlatformSourceSettlement>(
            attempts.Count);
        foreach (PlatformSourceCapabilityIdentity capability
            in selection.Capabilities)
        {
            if (attempts.TryGetValue(
                    capability,
                    out TAttempt? attempt))
            {
                settlements.Add(
                    new PlatformSourceSettlement(
                        attempt.Contribution,
                        PlatformSourceSettlementDisposition
                            .OutcomeRelevant));
            }
        }
        return settlements.AsReadOnly();
    }

    static PlatformSourcePolicyDecision<TAttempt> Selected<TAttempt>(
        TAttempt selected,
        IReadOnlyList<PlatformSourceSettlement> settlements)
        where TAttempt : class, IPlatformSourcePolicyAttempt =>
        new(
            PlatformSourcePolicyDecisionKind.Selected,
            settlements,
            Selected: selected);

    static PlatformSourcePolicyDecision<TAttempt> Unavailable<TAttempt>(
        IReadOnlyList<PlatformSourceSettlement> settlements)
        where TAttempt : class, IPlatformSourcePolicyAttempt =>
        new(PlatformSourcePolicyDecisionKind.Unavailable, settlements);

    static PlatformSourcePolicyDecision<TAttempt> Ambiguous<TAttempt>(
        IReadOnlyList<PlatformHouseCandidateIdentity> candidates,
        IReadOnlyList<PlatformSourceSettlement> settlements)
        where TAttempt : class, IPlatformSourcePolicyAttempt =>
        new(
            PlatformSourcePolicyDecisionKind.Ambiguous,
            settlements,
            AmbiguousCandidates: candidates);

    static PlatformSourcePolicyDecision<TAttempt> Rejected<TAttempt>(
        PlatformHouseRejectionKind rejectionKind,
        IReadOnlyList<PlatformSourceSettlement> settlements)
        where TAttempt : class, IPlatformSourcePolicyAttempt =>
        new(
            PlatformSourcePolicyDecisionKind.Rejected,
            settlements,
            RejectionKind: rejectionKind);

    static PlatformSourcePolicyDecision<TAttempt> Incomplete<TAttempt>(
        IReadOnlyList<PlatformSourceSettlement> settlements)
        where TAttempt : class, IPlatformSourcePolicyAttempt =>
        new(PlatformSourcePolicyDecisionKind.Incomplete, settlements);

    static PlatformSourcePolicyDecision<TAttempt> Failed<TAttempt>(
        IReadOnlyList<PlatformSourceSettlement> settlements)
        where TAttempt : class, IPlatformSourcePolicyAttempt =>
        new(PlatformSourcePolicyDecisionKind.Failed, settlements);

    static int IndexOf(
        IReadOnlyList<PlatformSourceCapabilityIdentity> capabilities,
        PlatformSourceCapabilityIdentity capability)
    {
        for (int index = 0; index < capabilities.Count; index++)
        {
            if (ReferenceEquals(capabilities[index], capability))
                return index;
        }
        return -1;
    }
}
