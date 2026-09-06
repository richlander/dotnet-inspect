namespace DotnetInspector.SourceDelegation;

public abstract class RowMemberOutcome<TMember, TRow, TDisposition, TWitness>
    where TMember : notnull
{
    private protected RowMemberOutcome(
        TMember member,
        DelegationCause<TDisposition, TWitness> cause)
    {
        Member = member;
        Cause = cause;
    }

    public TMember Member { get; }

    public DelegationCause<TDisposition, TWitness> Cause { get; }

    public TDisposition Disposition => Cause.Disposition;

    public CompletionEvidence<TWitness> Evidence => Cause.Evidence;
}

public sealed class RowValuesOutcome<TMember, TRow, TDisposition, TWitness>
    : RowMemberOutcome<TMember, TRow, TDisposition, TWitness>
    where TMember : notnull
{
    internal RowValuesOutcome(
        TMember member,
        IReadOnlyList<TRow> values,
        DelegationCause<TDisposition, TWitness> cause)
        : base(member, cause)
    {
        Values = values;
    }

    // A fully acquired immutable snapshot: mutating the source's collection
    // after construction cannot change it.
    public IReadOnlyList<TRow> Values { get; }
}

public sealed class UnavailableOutcome<TMember, TRow, TDisposition, TWitness>
    : RowMemberOutcome<TMember, TRow, TDisposition, TWitness>
    where TMember : notnull
{
    internal UnavailableOutcome(
        TMember member,
        DelegationCause<TDisposition, TWitness> cause)
        : base(member, cause)
    {
    }
}

public sealed class ExactCountMemberValue<TMember, TWitness>
    where TMember : notnull
{
    internal ExactCountMemberValue(
        TMember member,
        int count,
        CompletionEvidence<TWitness> evidence)
    {
        Member = member;
        Count = count;
        Evidence = evidence;
    }

    public TMember Member { get; }

    public int Count { get; }

    public CompletionEvidence<TWitness> Evidence { get; }
}

public sealed class NotSatisfiedMember<TMember, TDisposition, TWitness>
    where TMember : notnull
{
    internal NotSatisfiedMember(
        TMember member,
        DelegationCause<TDisposition, TWitness> cause)
    {
        Member = member;
        Cause = cause;
    }

    public TMember Member { get; }

    public DelegationCause<TDisposition, TWitness> Cause { get; }

    public TDisposition Disposition => Cause.Disposition;

    public CompletionEvidence<TWitness> Evidence => Cause.Evidence;
}
