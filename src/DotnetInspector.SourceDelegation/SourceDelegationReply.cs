namespace DotnetInspector.SourceDelegation;

// The source offers values and evidence; the runner's selected candidate owns
// member binding and completion decisions. Offer selection finishes inside
// RunAsync, before any result is published.
public abstract class SourceDelegationReply<TMember, TRow, TDisposition, TWitness>
    where TMember : notnull
{
    private protected SourceDelegationReply()
    {
    }

    internal abstract SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
        BindTo<TInput, TOperation>(
            DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate);
}

public sealed class RowHandoffReply<TMember, TRow, TDisposition, TWitness>
    : SourceDelegationReply<TMember, TRow, TDisposition, TWitness>
    where TMember : notnull
{
    private readonly Func<TMember, RowMemberOffer<TRow, TDisposition, TWitness>> _outcome;

    internal RowHandoffReply(
        Func<TMember, RowMemberOffer<TRow, TDisposition, TWitness>> outcome)
    {
        _outcome = outcome;
    }

    internal override SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
        BindTo<TInput, TOperation>(
            DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate) =>
        candidate.BindRows(_outcome);
}

public sealed class ExactCountReply<TMember, TRow, TDisposition, TWitness>
    : SourceDelegationReply<TMember, TRow, TDisposition, TWitness>
    where TMember : notnull
{
    private readonly Func<TMember, CountMemberOffer<TDisposition, TWitness>> _count;

    internal ExactCountReply(Func<TMember, CountMemberOffer<TDisposition, TWitness>> count)
    {
        _count = count;
    }

    internal override SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
        BindTo<TInput, TOperation>(
            DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate) =>
        candidate.BindCounts<TRow>(_count);
}

public sealed class NotSatisfiedReply<TMember, TRow, TDisposition, TWitness>
    : SourceDelegationReply<TMember, TRow, TDisposition, TWitness>
    where TMember : notnull
{
    private readonly DelegationCause<TDisposition, TWitness> _cause;

    internal NotSatisfiedReply(
        DelegationCause<TDisposition, TWitness> cause)
    {
        _cause = cause;
    }

    internal override SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
        BindTo<TInput, TOperation>(
            DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate) =>
        candidate.BindNotSatisfied<TRow>(_cause);
}
