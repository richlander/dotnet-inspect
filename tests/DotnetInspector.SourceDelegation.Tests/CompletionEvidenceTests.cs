namespace DotnetInspector.SourceDelegation.Tests;

// Gates for completion evidence: which basis and scope establish Rows
// usability, which establish exact Count, and which prove nothing at all.
public sealed class CompletionEvidenceTests
{
    private static readonly ToyMember Packages = ToyDelegation.Member("packages");
    private static readonly ToyMember Versions = ToyDelegation.Member("versions");

    [Fact]
    public async Task SourceDelegationCompletionEvidenceBasisIsAccepted()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages);
        var handoffCandidate = ToyDelegation.RowHandoff(
            group,
            requirement,
            prefix: _ => [ToyOperation.TakeFirst(2)]);

        // Member-scoped exhaustion and a matching requirement witness establish
        // Rows usability.
        Assert.True(await RowsAreUsable(handoffCandidate, Evidence.Exhaustion()));
        Assert.True(await RowsAreUsable(
            handoffCandidate,
            Evidence.Witness(requirement, "two applicable rows reached the clamp")));

        // A candidate-scoped `Acquired` proof is a group aggregate: this
        // caller's requirement, holding the member and candidate context,
        // judges that it does not establish an individual member's claim.
        Assert.False(await RowsAreUsable(
            handoffCandidate,
            Evidence.Exhaustion(CompletionEvidenceScope.Candidate)));
        Assert.False(await RowsAreUsable(
            handoffCandidate,
            Evidence.Witness(requirement, "aggregate", CompletionEvidenceScope.Candidate)));

        // Evidence answers the caller's requirement, never one the source
        // selected for itself.
        Assert.False(await RowsAreUsable(
            handoffCandidate,
            Evidence.Witness(CompletionRequirementIdentity.Create("toy.rows"), "borrowed witness")));

        // Absence is not exhaustion, and an unavailable outcome proves nothing.
        Assert.False(await RowsAreUsable(handoffCandidate, Evidence.Unavailable()));
        Assert.False(await RowsAreUsable(
            handoffCandidate,
            Evidence.Exhaustion(),
            ToyDisposition.Absent));

        // The caller's requirement decides within those rules and may refuse
        // more.
        var strict = new ToyRequirement("toy.rows.strict", acceptsCappedRows: false);
        var strictAcquisition = ToyDelegation.RowHandoff(group, strict);
        Assert.True(await RowsAreUsable(strictAcquisition, Evidence.Exhaustion()));
        Assert.False(await RowsAreUsable(strictAcquisition, Evidence.Stop()));

        // Exact Count needs proof of every member value; this requirement
        // judges a candidate-scoped `Acquired` aggregate insufficient for the
        // member it cannot speak for.
        var countRequirement = new ToyRequirement("toy.count");
        ToyGroup pair = ToyDelegation.Group(Packages, Versions);
        var countCandidate = ToyDelegation.CountCandidate(pair, countRequirement);

        var aggregate = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Counts(
                accepted,
                member => Offer.Count(
                    2,
                    ToyDisposition.Acquired,
                    member == Packages
                        ? Evidence.Exhaustion()
                        : Evidence.Exhaustion(CompletionEvidenceScope.Candidate))));

        var aggregateOutcome = await ToyDelegation.RunAsync(aggregate, countCandidate);
        Assert.IsType<ToyNotSatisfied>(
            aggregateOutcome.Result);

        var perMember = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Counts(
                accepted,
                _ => Offer.Count(2, ToyDisposition.Acquired, Evidence.Exhaustion())));

        var perMemberOutcome = await ToyDelegation.RunAsync(perMember, countCandidate);
        Assert.IsType<ToyCounts>(perMemberOutcome.Result);
    }

    [Fact]
    public async Task OperationalBoundsNeverProveCompletion()
    {
        // The canonical boundary: a provider cap equal to the requested bound
        // returns exactly as many rows as a real witness would.
        const int RequestedBound = 2;
        var requirement = new ToyRequirement("toy.head");
        ToyGroup group = ToyDelegation.Group(Packages);
        IReadOnlyList<ToyRow> capped = ToyDelegation.Rows("a", "b");
        var clamped = ToyDelegation.RowHandoff(
            group,
            requirement,
            prefix: _ => [ToyOperation.TakeFirst(RequestedBound)]);
        var countCandidate = ToyDelegation.CountCandidate(
            group,
            requirement,
            prefix: _ => [ToyOperation.TakeFirst(RequestedBound)]);

        // A cap that happens to equal the bound is an incomplete stop. After a
        // nonempty delegated prefix it leaves the member Unavailable.
        var cappedSource = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                _ => Offer.Rows(capped, ToyDisposition.ProviderCapped, Evidence.Stop())));

        var cappedOutcome = await ToyDelegation.RunAsync(cappedSource, clamped);
        var cappedHandoff = Assert.IsType<ToyHandoff>(
            cappedOutcome.Result);
        Assert.IsType<ToyUnavailable>(
            cappedHandoff.Outcomes[0]);
        Assert.Equal(RequestedBound, capped.Count);

        // The same numbers with a real witness are usable.
        var witnessed = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                _ => Offer.Rows(
                    capped,
                    ToyDisposition.Acquired,
                    Evidence.Witness(requirement, "two applicable ordered rows reached the clamp"))));

        var witnessedOutcome = await ToyDelegation.RunAsync(witnessed, clamped);
        var witnessedHandoff = Assert.IsType<ToyHandoff>(
            witnessedOutcome.Result);
        var usable = Assert.IsType<ToyRowValues>(
            witnessedHandoff.Outcomes[0]);
        Assert.Equal(["a", "b"], usable.Values.Select(row => row.Id));

        // No cap, page size, or row count ever establishes exact Count.
        var cappedCount = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Counts(
                accepted,
                _ => Offer.Count(RequestedBound, ToyDisposition.ProviderCapped, Evidence.Stop())));

        var cappedCountOutcome = await ToyDelegation.RunAsync(cappedCount, countCandidate);
        var notSatisfied = Assert.IsType<ToyNotSatisfied>(
            cappedCountOutcome.Result);
        Assert.Same(ToyDisposition.ProviderCapped, notSatisfied.Members[0].Disposition);
        Assert.Equal(CompletionEvidenceBasis.IncompleteStop, notSatisfied.Members[0].Evidence.Basis);

        // Evidence carries no numbers of its own: a count reaches the caller
        // only inside an owner-issued typed witness.
        ToyEvidence stop = Evidence.Stop();
        Assert.Null(stop.Witness);
        Assert.Null(stop.Requirement);
    }

    [Fact]
    public async Task RowsUsabilityAndCountSufficiencyStayDistinct()
    {
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages);
        var acquisitionOnly = ToyDelegation.RowHandoff(group, requirement);
        var caller = new ToyCaller();
        ToyCause cappedAcquisition =
            Cause.Of(ToyDisposition.ProviderCapped, Evidence.Stop());

        // An acquisition-only handoff delegates the empty prefix, so the caller
        // owner's existing source-result contract decides usability and its
        // incompleteness evidence survives into the residual.
        Assert.True(acquisitionOnly.IsAcquisitionOnly);
        var source = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                _ => Offer.Rows(ToyDelegation.Rows("a", "b", "c"), cappedAcquisition)));

        var outcome = await ToyDelegation.RunAsync(source, acquisitionOnly);
        var handoff = Assert.IsType<ToyHandoff>(outcome.Result);
        var usable = Assert.IsType<ToyRowValues>(
            handoff.Outcomes[0]);
        Assert.Equal(CompletionEvidenceBasis.IncompleteStop, usable.Evidence.Basis);
        Assert.Same(ToyDisposition.ProviderCapped, usable.Disposition);

        Dictionary<ToyMember, IReadOnlyList<ToyRow>> admitted =
            caller.AdmitToResidual(handoff, _ => [ToyOperation.Exclude("b")]);
        Assert.Equal(["a", "c"], admitted[Packages].Select(row => row.Id));

        // The same evidence after a nonempty delegated prefix leaves the member
        // Unavailable: reverse semantic work happened on incomplete input.
        var clamped = ToyDelegation.RowHandoff(
            group,
            requirement,
            prefix: _ => [ToyOperation.TakeFirst(2)]);
        var clampedOutcome = await ToyDelegation.RunAsync(source, clamped);
        var clampedHandoff = Assert.IsType<ToyHandoff>(
            clampedOutcome.Result);
        Assert.IsType<ToyUnavailable>(
            clampedHandoff.Outcomes[0]);
        Assert.Same(cappedAcquisition.Disposition, clampedHandoff.Outcomes[0].Disposition);

        // The same evidence is never Count-sufficient, whatever the shape.
        var countCandidate = ToyDelegation.CountCandidate(group, requirement);
        var countSource = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Counts(
                accepted,
                _ => Offer.Count(3, cappedAcquisition)));

        var countOutcome = await ToyDelegation.RunAsync(countSource, countCandidate);
        var notSatisfied = Assert.IsType<ToyNotSatisfied>(
            countOutcome.Result);
        Assert.Same(cappedAcquisition, notSatisfied.Members[0].Cause);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CandidateScopedProofIsJudgedWithMemberContext(bool useWitness)
    {
        ToyEvidence Proof(ToyRequirement target, string claim) => useWitness
            ? Evidence.Witness(target, claim, CompletionEvidenceScope.Candidate)
            : Evidence.Exhaustion(CompletionEvidenceScope.Candidate);

        // One candidate-wide fact — the source's whole domain is empty — is
        // also every member's own fact, so the caller's requirement accepts it
        // for each member from a single shared cause.
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages, Versions);
        var candidate = ToyDelegation.RowHandoff(group, requirement);
        ToyCause emptyDomain = Cause.Of(
            ToyDisposition.DomainEmpty,
            Proof(requirement, "source domain empty"));

        var empty = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(accepted, _ => Offer.Rows([], emptyDomain)));

        var outcome = await ToyDelegation.RunAsync(empty, candidate);
        var handoff = Assert.IsType<ToyHandoff>(outcome.Result);
        Assert.Equal([Packages, Versions], handoff.UsableOutcomes.Select(entry => entry.Member));
        Assert.All(handoff.UsableOutcomes, entry => Assert.Empty(entry.Values));
        Assert.All(handoff.Outcomes, entry => Assert.Same(emptyDomain, entry.Cause));

        // The requirement judged each member with its own typed context.
        Assert.Equal([Packages, Versions], requirement.Contexts.Select(context => context.Member));
        Assert.All(requirement.Contexts, context => Assert.Same(group, context.Group));

        // The same candidate-wide proof establishes every member's exact Count.
        var countRequirement = new ToyRequirement("toy.count");
        var countCandidate = ToyDelegation.CountCandidate(group, countRequirement);
        ToyCause emptyCountDomain = Cause.Of(
            ToyDisposition.DomainEmpty,
            Proof(countRequirement, "source domain empty"));

        var emptyCounts = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Counts(accepted, _ => Offer.Count(0, emptyCountDomain)));

        var countOutcome = await ToyDelegation.RunAsync(emptyCounts, countCandidate);
        var counts = Assert.IsType<ToyCounts>(countOutcome.Result);
        Assert.Equal([Packages, Versions], counts.Counts.Select(value => value.Member));
        Assert.Equal([0, 0], counts.Counts.Select(value => value.Count));

        // A candidate-scoped aggregate that cannot establish individual member
        // values is rejected, even though its basis and scope are identical.
        var aggregateRequirement = new ToyRequirement("toy.count.aggregate");
        var aggregateCandidate = ToyDelegation.CountCandidate(group, aggregateRequirement);
        var aggregate = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Counts(
                accepted,
                _ => Offer.Count(
                    4,
                    ToyDisposition.Acquired,
                    Proof(aggregateRequirement, "aggregate"))));

        var aggregateOutcome = await ToyDelegation.RunAsync(aggregate, aggregateCandidate);
        var notSatisfied = Assert.IsType<ToyNotSatisfied>(aggregateOutcome.Result);
        Assert.Equal([Packages, Versions], notSatisfied.Members.Select(entry => entry.Member));
        Assert.All(
            notSatisfied.Members,
            entry => Assert.Equal(CompletionEvidenceScope.Candidate, entry.Evidence.Scope));
    }

    [Fact]
    public async Task MixedPrefixCandidateIsJudgedMemberByMember()
    {
        // One candidate, two members, two different delegated prefixes. The
        // completion rule for an incomplete stop is member-local, so the same
        // shared cause resolves differently for each member.
        var requirement = new ToyRequirement("toy.rows");
        ToyGroup group = ToyDelegation.Group(Packages, Versions);
        var mixed = ToyDelegation.RowHandoff(
            group,
            requirement,
            prefix: member => member == Packages
                ? ToyDelegation.NoOperations
                : [ToyOperation.TakeFirst(2)]);
        ToyCause capped = Cause.Of(
            ToyDisposition.ProviderCapped,
            Evidence.Stop(CompletionEvidenceScope.Candidate));

        var source = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                _ => Offer.Rows(ToyDelegation.Rows("a", "b"), capped)));

        var outcome = await ToyDelegation.RunAsync(source, mixed);
        var handoff = Assert.IsType<ToyHandoff>(outcome.Result);

        Assert.False(mixed.IsAcquisitionOnly);
        Assert.IsType<ToyRowValues>(handoff.Outcomes[0]);

        // Incomplete rows on the member whose prefix delegated reverse semantic
        // work are unavailable, with the source owner's disposition retained.
        Assert.IsType<ToyUnavailable>(handoff.Outcomes[1]);
        Assert.Same(capped, handoff.Outcomes[1].Cause);

        // The judged member's context carries the whole candidate's prefix
        // structure, not only its own.
        ToyContext context = Assert.Single(requirement.Contexts);
        Assert.Same(Packages, context.Member);
        Assert.True(context.DelegatedPrefixIsEmpty);
        Assert.True(context.DelegatedPrefixIsEmptyFor(Packages));
        Assert.False(context.DelegatedPrefixIsEmptyFor(Versions));
        Assert.False(context.CandidateIsAcquisitionOnly);
        Assert.Equal(DelegationResultShape.RowHandoff, context.Shape);
    }

    private static async Task<bool> RowsAreUsable(
        ToyCandidate candidate,
        ToyEvidence evidence,
        ToyDisposition? disposition = null)
    {
        var source = new ToySource(
            _ => true,
            (accepted, _) => ToyExecution.Handoff(
                accepted,
                _ => Offer.Rows(
                    ToyDelegation.Rows("a", "b"),
                    disposition ?? ToyDisposition.Acquired,
                    evidence)));

        var outcome = await ToyDelegation.RunAsync(source, candidate);
        var handoff = Assert.IsType<ToyHandoff>(outcome.Result);
        return handoff.Outcomes[0] is ToyRowValues;
    }
}
