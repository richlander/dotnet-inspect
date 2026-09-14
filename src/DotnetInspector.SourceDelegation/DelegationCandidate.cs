namespace DotnetInspector.SourceDelegation;

// One member's delegated operation prefix: the caller's proven safe prefix as
// typed, owner-issued operation content. The protocol transports it without
// interpreting it, and the source executes it exactly. An empty prefix
// delegates no reverse semantic work for that member.
public sealed class DelegatedPrefix<TMember, TOperation>
    where TMember : notnull
{
    internal DelegatedPrefix(TMember member, IReadOnlyList<TOperation> operations)
    {
        Member = member;
        Operations = operations;
    }

    public TMember Member { get; }

    public IReadOnlyList<TOperation> Operations { get; }

    public bool IsEmpty => Operations.Count == 0;
}

// One immutable caller-formed delegation candidate. The caller owns the
// partition that produced the prefix and retains the residual; the source
// receives this candidate and never the residual.
public sealed class DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness>
    where TMember : notnull
{
    private readonly DelegatedPrefix<TMember, TOperation>[] _prefixes;
    private readonly bool[] _prefixIsEmpty;

    internal DelegationCandidate(
        TInput input,
        DelegationGroup<TMember> group,
        DelegatedPrefix<TMember, TOperation>[] prefixes,
        SourceCapability capability,
        CompletionRequirement<TMember, TDisposition, TWitness> completionRequirement,
        DelegationResultShape shape)
    {
        Input = input;
        Group = group;
        _prefixes = prefixes;
        _prefixIsEmpty = Array.ConvertAll(prefixes, prefix => prefix.IsEmpty);
        Prefixes = DelegationSnapshot.Own(prefixes);
        Capability = capability;
        CompletionRequirement = completionRequirement;
        Shape = shape;
    }

    // Already-authorized source input. This contract authorizes no source,
    // endpoint, credential, cache, or path of its own.
    public TInput Input { get; }

    public DelegationGroup<TMember> Group { get; }

    // One prefix entry per execution-group member, in the same order, derived
    // from the group's member collection rather than a parallel identity list.
    public IReadOnlyList<DelegatedPrefix<TMember, TOperation>> Prefixes { get; }

    public SourceCapability Capability { get; }

    public CompletionRequirement<TMember, TDisposition, TWitness> CompletionRequirement { get; }

    public DelegationResultShape Shape { get; }

    public bool IsAcquisitionOnly => Array.TrueForAll(_prefixIsEmpty, empty => empty);

    public DelegatedPrefix<TMember, TOperation> PrefixFor(TMember member)
    {
        ArgumentNullException.ThrowIfNull(member);
        int index = Group.IndexOf(member);
        return index < 0
            ? throw new ArgumentException(
                "The member is not part of this candidate's execution group.",
                nameof(member))
            : _prefixes[index];
    }

    // Selectors run once per selected member inside RunAsync, before publication.
    public RowHandoffReply<TMember, TRow, TDisposition, TWitness> ReplyWithRows<TRow>(
        Func<TMember, RowMemberOffer<TRow, TDisposition, TWitness>> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        RequireShape(DelegationResultShape.RowHandoff);
        return new(outcome);
    }

    public SourceDelegationReply<TMember, TRow, TDisposition, TWitness> ReplyWithCounts<TRow>(
        Func<TMember, CountMemberOffer<TDisposition, TWitness>> count)
    {
        ArgumentNullException.ThrowIfNull(count);
        RequireShape(DelegationResultShape.ExactCount);
        return new ExactCountReply<TMember, TRow, TDisposition, TWitness>(count);
    }

    public NotSatisfiedReply<TMember, TRow, TDisposition, TWitness> ReplyNotSatisfied<TRow>(
        DelegationCause<TDisposition, TWitness> candidateCause)
    {
        ArgumentNullException.ThrowIfNull(candidateCause);
        if (candidateCause.Evidence.Scope != CompletionEvidenceScope.Candidate)
        {
            throw new ArgumentException(
                "A candidate-wide failure carries candidate-scoped evidence.",
                nameof(candidateCause));
        }

        return new(candidateCause);
    }

    // Only the runner binds offers to its selected candidate. Member identity,
    // ordering, and completion context all come from that same candidate.
    internal RowHandoffResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness> BindRows<TRow>(
        Func<TMember, RowMemberOffer<TRow, TDisposition, TWitness>> outcome)
    {
        RequireShape(DelegationResultShape.RowHandoff);

        IReadOnlyList<TMember> members = Group.Members;
        var outcomes = new RowMemberOutcome<TMember, TRow, TDisposition, TWitness>[members.Count];
        for (int index = 0; index < members.Count; index++)
        {
            TMember member = members[index];
            RowMemberOffer<TRow, TDisposition, TWitness> offer =
                outcome(member) ?? throw new InvalidOperationException(
                    $"The source offered no outcome for member {index + 1} of {members.Count}.");

            bool usable = offer.Values is not null
                && CompletionAcceptance.RowsAreUsable(
                    CompletionRequirement,
                    CreateContext(index, offer.Cause));

            outcomes[index] = usable
                ? new RowValuesOutcome<TMember, TRow, TDisposition, TWitness>(
                    member,
                    offer.Values!,
                    offer.Cause)
                : new UnavailableOutcome<TMember, TRow, TDisposition, TWitness>(member, offer.Cause);
        }

        return new(this, DelegationSnapshot.Own(outcomes));
    }

    // Publish Count only after every member's evidence has been accepted.
    internal SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness> BindCounts<TRow>(
        Func<TMember, CountMemberOffer<TDisposition, TWitness>> count)
    {
        RequireShape(DelegationResultShape.ExactCount);

        IReadOnlyList<TMember> members = Group.Members;
        var offers = new CountMemberOffer<TDisposition, TWitness>[members.Count];
        bool everyMemberIsExact = true;
        for (int index = 0; index < members.Count; index++)
        {
            CountMemberOffer<TDisposition, TWitness> offer =
                count(members[index]) ?? throw new InvalidOperationException(
                    $"The source offered no count for member {index + 1} of {members.Count}.");
            offers[index] = offer;
            everyMemberIsExact &= offer.Count is not null
                && CompletionAcceptance.CountIsExact(
                    CompletionRequirement,
                    CreateContext(index, offer.Cause));
        }

        if (!everyMemberIsExact)
        {
            var dispositions = new NotSatisfiedMember<TMember, TDisposition, TWitness>[members.Count];
            for (int index = 0; index < members.Count; index++)
                dispositions[index] = new(members[index], offers[index].Cause);

            return new NotSatisfiedResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>(
                this,
                DelegationSnapshot.Own(dispositions));
        }

        var counts = new ExactCountMemberValue<TMember, TWitness>[members.Count];
        for (int index = 0; index < members.Count; index++)
        {
            CountMemberOffer<TDisposition, TWitness> offer = offers[index];
            counts[index] = new(members[index], offer.Count!.Value, offer.Cause.Evidence);
        }

        return new ExactCountResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>(
            this,
            DelegationSnapshot.Own(counts));
    }

    internal NotSatisfiedResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness> BindNotSatisfied<TRow>(
        DelegationCause<TDisposition, TWitness> candidateCause)
    {
        IReadOnlyList<TMember> members = Group.Members;
        var dispositions = new NotSatisfiedMember<TMember, TDisposition, TWitness>[members.Count];
        for (int index = 0; index < members.Count; index++)
            dispositions[index] = new(members[index], candidateCause);

        return new(this, DelegationSnapshot.Own(dispositions));
    }

    private CompletionContext<TMember, TDisposition, TWitness> CreateContext(
        int memberIndex,
        DelegationCause<TDisposition, TWitness> cause) =>
        new(Group, memberIndex, _prefixIsEmpty, Shape, cause);

    private void RequireShape(DelegationResultShape shape)
    {
        if (Shape != shape)
        {
            throw new InvalidOperationException(
                $"A {Shape} candidate cannot publish a {shape} result.");
        }
    }
}

// Forms delegation candidates. Prefix entries are derived from the execution
// group's unique ordered member collection, so a candidate cannot bind a
// missing, extra, duplicated, or reordered member.
public static class DelegationCandidate
{
    public static DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness>
        RowHandoff<TMember, TInput, TOperation, TDisposition, TWitness>(
            TInput input,
            DelegationGroup<TMember> group,
            SourceCapability capability,
            CompletionRequirement<TMember, TDisposition, TWitness> completionRequirement,
            Func<TMember, IReadOnlyList<TOperation>> delegatedPrefix)
        where TMember : notnull =>
        Create(
            input,
            group,
            capability,
            completionRequirement,
            delegatedPrefix,
            DelegationResultShape.RowHandoff);

    public static DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness>
        ExactCount<TMember, TInput, TOperation, TDisposition, TWitness>(
            TInput input,
            DelegationGroup<TMember> group,
            SourceCapability capability,
            CompletionRequirement<TMember, TDisposition, TWitness> completionRequirement,
            Func<TMember, IReadOnlyList<TOperation>> delegatedPrefix)
        where TMember : notnull =>
        Create(
            input,
            group,
            capability,
            completionRequirement,
            delegatedPrefix,
            DelegationResultShape.ExactCount);

    private static DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness>
        Create<TMember, TInput, TOperation, TDisposition, TWitness>(
            TInput input,
            DelegationGroup<TMember> group,
            SourceCapability capability,
            CompletionRequirement<TMember, TDisposition, TWitness> completionRequirement,
            Func<TMember, IReadOnlyList<TOperation>> delegatedPrefix,
            DelegationResultShape shape)
        where TMember : notnull
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(completionRequirement);
        ArgumentNullException.ThrowIfNull(delegatedPrefix);

        IReadOnlyList<TMember> members = group.Members;
        var prefixes = new DelegatedPrefix<TMember, TOperation>[members.Count];
        for (int index = 0; index < members.Count; index++)
        {
            TMember member = members[index];
            IReadOnlyList<TOperation> operations =
                delegatedPrefix(member) ?? throw new ArgumentException(
                    $"No delegated prefix was derived for member {index + 1} of {members.Count}.",
                    nameof(delegatedPrefix));
            prefixes[index] = new(member, DelegationSnapshot.Copy(operations));
        }

        return new(input, group, prefixes, capability, completionRequirement, shape);
    }
}
