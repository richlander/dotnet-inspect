namespace DotnetInspector.SourceDelegation.Tests;

// Gates for the closed result algebra: row handoff, exact Count, not satisfied.
public sealed class SourceDelegationResultAlgebraTests
{
    private static readonly ToyMember Packages = ToyDelegation.Member("packages");
    private static readonly ToyMember Versions = ToyDelegation.Member("versions");
    private static readonly ToyMember Owners = ToyDelegation.Member("owners");

    [Fact]
    public async Task SourceDelegationRowHandoffIsComplete()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages, Versions, Owners);
        var candidate = ToyDelegation.RowHandoff(group, requirement);
        var caller = new ToyCaller();

        var source = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                member => member == Versions
                    // One member's expected absence leaves the others usable:
                    // the L2 consumer owns that composition.
                    ? Offer.Unavailable(ToyDisposition.Absent, Evidence.Unavailable())
                    : Offer.Rows(
                        ToyDelegation.Rows("a", "b", "c"),
                        ToyDisposition.Acquired,
                        Evidence.Exhaustion())));

        var outcome = await ToyDelegation.RunAsync(source, candidate);
        var handoff = Assert.IsType<ToyHandoff>(outcome.Result);

        // Exactly one outcome per member, in execution-group order.
        Assert.Equal(DelegationResultShape.RowHandoff, handoff.Shape);
        Assert.Equal([Packages, Versions, Owners], handoff.Outcomes.Select(entry => entry.Member));
        Assert.Equal(group.Count, handoff.Outcomes.Count);
        Assert.Equal(
            [true, false, true],
            handoff.Outcomes.Select(entry => entry is ToyRowValues));
        Assert.Equal([Packages, Owners], handoff.UsableOutcomes.Select(entry => entry.Member));
        Assert.Same(ToyDisposition.Absent, handoff.Outcomes[1].Disposition);

        // Only Rows-usable entries are eligible for the caller's residual.
        Dictionary<ToyMember, IReadOnlyList<ToyRow>> admitted =
            caller.AdmitToResidual(handoff, _ => [ToyOperation.Exclude("b")]);
        Assert.Equal([Packages, Owners], caller.ResidualInvocations);
        Assert.Equal(["a", "c"], admitted[Packages].Select(row => row.Id));
        Assert.DoesNotContain(Versions, admitted.Keys);

        // The owning composition may suppress every residual invocation without
        // changing the handoff result.
        var suppressing = new ToyCaller { SuppressResiduals = true };
        Assert.Empty(suppressing.AdmitToResidual(handoff, _ => [ToyOperation.Exclude("b")]));
        Assert.Empty(suppressing.ResidualInvocations);
        Assert.Equal(3, handoff.Outcomes.Count);

        // The two shapes are distinct.
        Assert.Throws<InvalidOperationException>(
            () => candidate.ReplyWithCounts<ToyRow>(
                _ => Offer.Count(1, ToyDisposition.Acquired, Evidence.Exhaustion())));

        // Completeness holds for an empty member map too: no outcomes, no
        // usable rows, and no residual invocation.
        var emptyCandidate = ToyDelegation.RowHandoff(ToyDelegation.Group(), requirement);
        var emptyOutcome = await ToyDelegation.RunAsync(source, emptyCandidate);
        var emptyHandoff = Assert.IsType<ToyHandoff>(emptyOutcome.Result);
        Assert.Empty(emptyHandoff.Outcomes);
        Assert.Empty(emptyHandoff.UsableOutcomes);
        Assert.Empty(caller.AdmitToResidual(emptyHandoff, _ => ToyDelegation.NoOperations));
        Assert.Equal([Packages, Owners], caller.ResidualInvocations);
    }

    [Fact]
    public async Task SourceDelegationExactCountIsAtomic()
    {
        var requirement = new ToyRequirement("toy.count");
        ToyGroup group = ToyDelegation.Group(Packages, Versions);
        var candidate = ToyDelegation.CountCandidate(
            group,
            requirement,
            prefix: _ => [ToyOperation.TakeFirst(3)]);

        var exact = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Counts(
                accepted,
                member => member == Packages
                    ? Offer.Count(
                        3,
                        ToyDisposition.Acquired,
                        Evidence.Witness(requirement, "three applicable rows reached the clamp"))
                    : Offer.Count(0, ToyDisposition.Acquired, Evidence.Exhaustion())));

        var outcome = await ToyDelegation.RunAsync(exact, candidate);
        var counts = Assert.IsType<ToyCounts>(outcome.Result);

        Assert.Equal(DelegationResultShape.ExactCount, counts.Shape);
        Assert.Equal([Packages, Versions], counts.Counts.Select(value => value.Member));
        Assert.Equal([3, 0], counts.Counts.Select(value => value.Count));
        Assert.All(counts.Counts, value => Assert.True(value.Count >= 0));

        // One insufficient member forces NotSatisfied for the whole candidate:
        // no partial map, no cardinality, and no invented total.
        var partial = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Counts(
                accepted,
                member => member == Packages
                    ? Offer.Count(3, ToyDisposition.Acquired, Evidence.Exhaustion())
                    : Offer.NoCount(ToyDisposition.Failed, Evidence.Unavailable())));

        var partialOutcome = await ToyDelegation.RunAsync(partial, candidate);
        var notSatisfied = Assert.IsType<ToyNotSatisfied>(
            partialOutcome.Result);
        Assert.Equal(DelegationResultShape.ExactCount, notSatisfied.Shape);
        Assert.Equal([Packages, Versions], notSatisfied.Members.Select(entry => entry.Member));
        Assert.Same(ToyDisposition.Acquired, notSatisfied.Members[0].Disposition);
        Assert.Same(ToyDisposition.Failed, notSatisfied.Members[1].Disposition);

        // A count is non-negative by construction, and the shapes stay distinct.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Offer.Count(-1, ToyDisposition.Acquired, Evidence.Exhaustion()));
        Assert.Throws<InvalidOperationException>(
            () => candidate.ReplyWithRows(
                _ => Offer.Rows(ToyDelegation.Rows("a"), ToyDisposition.Acquired, Evidence.Exhaustion())));

        // An empty member map stays empty: every member it has is exact, and no
        // group total is invented for the ones it does not.
        var emptyCandidate = ToyDelegation.CountCandidate(ToyDelegation.Group(), requirement);
        var emptyOutcome = await ToyDelegation.RunAsync(exact, emptyCandidate);
        var emptyCounts = Assert.IsType<ToyCounts>(emptyOutcome.Result);
        Assert.Empty(emptyCounts.Counts);
    }

    [Fact]
    public async Task SourceDelegationNotSatisfiedCarriesEvidence()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages, Versions);
        var candidate = ToyDelegation.RowHandoff(group, requirement);
        var caller = new ToyCaller();
        ToyCause candidateFailure = Cause.Of(
            ToyDisposition.Failed,
            Evidence.Unavailable(CompletionEvidenceScope.Candidate));

        var source = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.NotSatisfied(accepted, candidateFailure));

        var outcome = await ToyDelegation.RunAsync(source, candidate);
        var notSatisfied = Assert.IsType<ToyNotSatisfied>(
            outcome.Result);

        // One entry per member, in group order, with no row or Count payload,
        // and one canonical value keeping the failure's candidate scope.
        Assert.Equal([Packages, Versions], notSatisfied.Members.Select(entry => entry.Member));
        Assert.All(notSatisfied.Members, entry => Assert.Same(candidateFailure, entry.Cause));
        Assert.All(
            notSatisfied.Members,
            entry => Assert.Equal(CompletionEvidenceScope.Candidate, entry.Evidence.Scope));
        Assert.Empty(caller.ResidualInvocations);

        // A candidate-wide failure carries candidate-scoped evidence.
        Assert.Throws<ArgumentException>(
            () => candidate.ReplyNotSatisfied<ToyRow>(
                Cause.Of(ToyDisposition.Failed, Evidence.Unavailable())));

        // A determinable member-scoped failure stays Unavailable inside the row
        // handoff instead of collapsing the whole candidate.
        var perMember = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                member => member == Packages
                    ? Offer.Rows(ToyDelegation.Rows("a"), ToyDisposition.Acquired, Evidence.Exhaustion())
                    : Offer.Unavailable(ToyDisposition.Failed, Evidence.Unavailable())));

        var perMemberOutcome = await ToyDelegation.RunAsync(perMember, candidate);
        var handoff = Assert.IsType<ToyHandoff>(
            perMemberOutcome.Result);
        Assert.IsType<ToyRowValues>(handoff.Outcomes[0]);
        Assert.IsType<ToyUnavailable>(handoff.Outcomes[1]);
    }
}
