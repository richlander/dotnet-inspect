using System.Diagnostics;

namespace DotnetInspector.SourceDelegation.Tests;

// Gates for the four effect rules: pure planning, single acceptance, no
// fallback after acceptance, atomic publication.
public sealed class SourceDelegationEffectProtocolTests
{
    private static readonly ToyMember Packages = ToyDelegation.Member("packages");
    private static readonly ToyMember Versions = ToyDelegation.Member("versions");

    [Fact]
    public async Task SourceDelegationPlanningIsPure()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages, Versions);
        var first = ToyDelegation.RowHandoff(group, requirement, capability: ToyDelegation.Unsupported);
        var second = ToyDelegation.RowHandoff(
            group,
            requirement,
            prefix: _ => [ToyOperation.TakeFirst(2)],
            capability: ToyDelegation.Unsupported);

        var source = new ToySource(
            candidate =>
            {
                // Planning reads immutable candidate structure only.
                Assert.Equal(2, candidate.Group.Count);
                Assert.Equal(candidate.Group.Count, candidate.Prefixes.Count);
                return candidate.Capability == ToyDelegation.RowPrefix;
            },
            (_, _) => throw new UnreachableException("Planning must not execute."));

        var outcome = await ToyDelegation.RunAsync(source, first, second);

        Assert.True(outcome.IsDeclined);
        Assert.Equal(0, source.ExecuteCount);
        Assert.Equal(0, source.AcquisitionCount);
        Assert.Equal([first, second], source.Planned);

        // Candidates are unchanged by planning.
        Assert.Empty(first.Prefixes[0].Operations);
        Assert.Equal("take-first(2)", second.Prefixes[0].Operations[0].Name);
    }

    [Fact]
    public async Task SourceDelegationDeclineAllowsReferenceFallback()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages);
        IReadOnlyList<ToyOperation> plan = [ToyOperation.Exclude("b"), ToyOperation.TakeFirst(2)];
        IReadOnlyList<ToyRow> complete = ToyDelegation.Rows("a", "b", "c", "d");

        var source = new ToySource(
            _ => false,
            (_, _) => throw new UnreachableException("A decline performs no source work."));

        // An empty candidate list is a decline that never reaches the source.
        var noCandidates = await ToyDelegation.RunAsync(source);
        Assert.True(noCandidates.IsDeclined);
        Assert.Null(noCandidates.Result);
        Assert.Empty(source.Planned);

        var outcome = await ToyDelegation.RunAsync(
            source,
            ToyDelegation.RowHandoff(group, requirement));

        // A decline is a capability answer, not a source failure, so the caller
        // keeps its complete reference path.
        Assert.True(outcome.IsDeclined);
        Assert.Null(outcome.Result);
        Assert.Equal(0, source.AcquisitionCount);
        Assert.Equal(["a", "c"], ToyDelegation.Execute(complete, plan).Select(row => row.Id));

        // An unsupported member shape — here, an empty member map — is an
        // ordinary planning decline rather than a construction error.
        var emptyMap = new ToySource(
            candidate => candidate.Group.Count > 0,
            (_, _) => throw new UnreachableException("A decline performs no source work."));

        var emptyOutcome = await ToyDelegation.RunAsync(
            emptyMap,
            ToyDelegation.RowHandoff(ToyDelegation.Group(), requirement));

        Assert.True(emptyOutcome.IsDeclined);
        Assert.Equal(0, emptyMap.ExecuteCount);
    }

    [Fact]
    public async Task SourceDelegationAcceptanceExecutesOnce()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages);
        var unsupported = ToyDelegation.RowHandoff(group, requirement, capability: ToyDelegation.Unsupported);
        var supported = ToyDelegation.RowHandoff(group, requirement);
        var later = ToyDelegation.RowHandoff(group, requirement);

        var source = new ToySource(
            candidate => candidate.Capability == ToyDelegation.RowPrefix,
            (candidate, _) => ToyExecution.Handoff(
                candidate,
                _ => Offer.Rows(
                    ToyDelegation.Rows("a"),
                    ToyDisposition.Acquired,
                    Evidence.Exhaustion())));

        var outcome = await ToyDelegation.RunAsync(source, unsupported, supported, later);

        Assert.False(outcome.IsDeclined);
        Assert.Equal(1, source.ExecuteCount);

        // Planning stopped at the first supported candidate, in declaration
        // order, and never reached the one after it.
        Assert.Equal([unsupported, supported], source.Planned);

        var handoff = Assert.IsType<ToyHandoff>(outcome.Result);
        Assert.Same(supported, handoff.Candidate);
        Assert.Same(group, handoff.Group);
        Assert.Same(requirement, handoff.CompletionRequirement);

        // No accepted-plan handle escapes: the outcome carries a published
        // result, and re-executing means planning again through the one public
        // entry point.
        var second = await ToyDelegation.RunAsync(source, supported);
        Assert.Equal(2, source.ExecuteCount);
        Assert.NotSame(outcome.Result, second.Result);
    }

    [Fact]
    public async Task SourceDelegationAcceptedFailureNeverFallsBack()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages, Versions);
        var accepted = ToyDelegation.RowHandoff(group, requirement);
        var never = ToyDelegation.RowHandoff(group, requirement);
        var caller = new ToyCaller();

        // A candidate-scoped expected failure after acceptance.
        var failing = new ToySource(
            _ => true,
            (candidate, _) => ToyExecution.NotSatisfied(
                candidate,
                Cause.Of(ToyDisposition.Failed, Evidence.Unavailable(CompletionEvidenceScope.Candidate))));

        var outcome = await ToyDelegation.RunAsync(failing, accepted, never);

        Assert.Equal(1, failing.ExecuteCount);
        Assert.Equal([accepted], failing.Planned);
        Assert.IsType<ToyNotSatisfied>(outcome.Result);
        Assert.Empty(caller.ResidualInvocations);

        // Expected cancellation keeps the source owner's typed disposition
        // instead of becoming a decline or a success-shaped empty result.
        var cancelled = new ToySource(
            _ => true,
            (candidate, _) => ToyExecution.Handoff(
                candidate,
                _ => Offer.Unavailable(ToyDisposition.Cancelled, Evidence.Stop())));

        var cancelledOutcome = await ToyDelegation.RunAsync(cancelled, accepted, never);
        var handoff = Assert.IsType<ToyHandoff>(
            cancelledOutcome.Result);
        Assert.False(cancelledOutcome.IsDeclined);
        Assert.All(
            handoff.Outcomes,
            entry => Assert.Same(ToyDisposition.Cancelled, entry.Disposition));
        Assert.Empty(handoff.UsableOutcomes);
        Assert.Empty(caller.AdmitToResidual(handoff, _ => ToyDelegation.NoOperations));
        Assert.Equal([accepted], cancelled.Planned);

        // An unexpected implementation fault propagates and tries nothing else.
        var faulting = new ToySource(
            _ => true,
            (_, _) => throw new InvalidTimeZoneException("unexpected source fault"));

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            async () => await ToyDelegation.RunAsync(faulting, accepted, never));
        Assert.Equal(1, faulting.ExecuteCount);
        Assert.Equal([accepted], faulting.Planned);

        // A missing reply is terminal for the accepted plan: nothing is
        // published, nothing is admitted to a residual, and nothing else is
        // tried.
        var silent = new ToySource(
            _ => true,
            (_, _) => ValueTask.FromResult<ToyReply>(null!));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await ToyDelegation.RunAsync(silent, accepted, never));
        Assert.Equal(1, silent.ExecuteCount);
        Assert.Equal([accepted], silent.Planned);
        Assert.Empty(caller.ResidualInvocations);
    }

    [Fact]
    public async Task SourceDelegationOutcomePublicationIsAtomic()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages);
        var candidate = ToyDelegation.RowHandoff(group, requirement);
        var acquired = new List<ToyRow>(ToyDelegation.Rows("a", "b"));
        ToyRow first = acquired[0];

        var mutating = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                _ => Offer.Rows(acquired, ToyDisposition.Acquired, Evidence.Exhaustion())));

        var outcome = await ToyDelegation.RunAsync(mutating, candidate);
        var handoff = Assert.IsType<ToyHandoff>(outcome.Result);
        var values = Assert.IsType<ToyRowValues>(
            handoff.Outcomes[0]);

        // Mutating the source collection after construction cannot change the
        // published result, and the opaque row objects are not cloned.
        acquired.Add(new ToyRow("c"));
        acquired.RemoveAt(0);
        Assert.Equal(["a", "b"], values.Values.Select(row => row.Id));
        Assert.Same(first, values.Values[0]);

        // Membership and order snapshot too: the list the group was built from
        // may move afterwards.
        var members = new List<ToyMember> { Packages, Versions };
        ToyGroup snapshot = ToyGroup.Create(members);
        members.Reverse();
        Assert.Equal([Packages, Versions], snapshot.Members);

        // A deferred source sequence is enumerated during execution, so no
        // acquisition or source failure can survive into publication.
        int enumerated = 0;
        IEnumerable<ToyRow> Deferred()
        {
            enumerated++;
            yield return new ToyRow("late");
        }

        var deferring = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                _ => Offer.Rows(Deferred(), ToyDisposition.Acquired, Evidence.Exhaustion())));

        var deferredOutcome = await ToyDelegation.RunAsync(deferring, candidate);
        Assert.Equal(1, enumerated);
        var deferredHandoff = Assert.IsType<ToyHandoff>(
            deferredOutcome.Result);
        var deferredValues = Assert.IsType<ToyRowValues>(
            deferredHandoff.Outcomes[0]);
        Assert.Equal(["late"], deferredValues.Values.Select(row => row.Id));
        Assert.Equal(1, enumerated);

        // A deferred source failure surfaces before any outcome is published.
        IEnumerable<ToyRow> Faulting()
        {
            yield return new ToyRow("a");
            throw new InvalidTimeZoneException("late source failure");
        }

        var failingPair = ToyDelegation.RowHandoff(ToyDelegation.Group(Packages, Versions), requirement);
        var faulting = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                member => Offer.Rows(
                    member == Packages ? ToyDelegation.Rows("complete") : Faulting(),
                    ToyDisposition.Acquired,
                    Evidence.Exhaustion())));

        await Assert.ThrowsAsync<InvalidTimeZoneException>(
            async () => await ToyDelegation.RunAsync(faulting, failingPair));
    }

    [Fact]
    public async Task AcceptedCandidateAssociationSelectsTheRetainedResidual()
    {
        // Two supported alternatives that share their group and completion
        // requirement, and differ only in the prefix delegated and the residual
        // the caller kept beside it.
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages);
        var caller = new ToyCaller();
        IReadOnlyList<ToyRow> inventory = ToyDelegation.Rows("a", "b", "c", "d");

        ToyCandidate delegatesHead = caller.Retain(
            ToyDelegation.RowHandoff(group, requirement, prefix: _ => [ToyOperation.TakeFirst(3)]),
            _ => [ToyOperation.Exclude("b")]);
        ToyCandidate acquisitionOnly = caller.Retain(
            ToyDelegation.RowHandoff(group, requirement),
            _ => [ToyOperation.TakeFirst(3), ToyOperation.Exclude("b")]);

        ToySource SourceFor(Func<ToyCandidate, bool> supports) =>
            new(
                supports,
                (accepted, _) => ToyExecution.Handoff(
                    accepted,
                    member => Offer.Rows(
                        ToyDelegation.Execute(inventory, accepted.PrefixFor(member).Operations),
                        ToyDisposition.Acquired,
                        Evidence.Exhaustion())));

        var headOutcome = await ToyDelegation.RunAsync(
            SourceFor(_ => true),
            delegatesHead,
            acquisitionOnly);
        var headHandoff = Assert.IsType<ToyHandoff>(headOutcome.Result);

        var acquisitionOutcome = await ToyDelegation.RunAsync(
            SourceFor(candidate => candidate.IsAcquisitionOnly),
            delegatesHead,
            acquisitionOnly);
        var acquisitionHandoff = Assert.IsType<ToyHandoff>(acquisitionOutcome.Result);

        // The published result carries the candidate the runner selected, so
        // the caller can look up the exact residual it retained for it without
        // tracking the selection itself.
        Assert.Same(delegatesHead, headHandoff.Candidate);
        Assert.Same(acquisitionOnly, acquisitionHandoff.Candidate);
        Assert.Equal(["a", "c"], caller.AdmitToResidual(headHandoff)[Packages].Select(row => row.Id));
        Assert.Equal(
            ["a", "c"],
            caller.AdmitToResidual(acquisitionHandoff)[Packages].Select(row => row.Id));

        // Both alternatives reproduce the same reference answer through
        // different partitions of the same plan.
        Assert.NotSame(headHandoff.Candidate, acquisitionHandoff.Candidate);
        Assert.Same(headHandoff.Group, acquisitionHandoff.Group);
    }

    [Fact]
    public void PlanningVisitsCandidatesInDeclarationOrder()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages);
        var candidate = ToyDelegation.RowHandoff(group, requirement);

        // A group binds each owner-issued identity exactly once, and an empty
        // member map is a legitimate shape.
        Assert.Throws<ArgumentException>(() => ToyDelegation.Group(Packages, Packages));
        Assert.Empty(ToyDelegation.Group().Members);

        // Prefix entries come from the group's member collection, in order.
        Assert.Equal([Packages], candidate.Prefixes.Select(prefix => prefix.Member));
        Assert.True(candidate.IsAcquisitionOnly);
        Assert.Same(candidate.Prefixes[0], candidate.PrefixFor(Packages));
        Assert.Throws<ArgumentException>(() => candidate.PrefixFor(Versions));
    }
}
