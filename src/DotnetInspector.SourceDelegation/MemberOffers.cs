namespace DotnetInspector.SourceDelegation;

// One immutable owner-issued disposition paired with the completion evidence
// that accompanies it. A candidate-wide stop, proof, or failure is one cause
// value referenced by every affected member entry, so it keeps its scope
// instead of being relabelled member by member.
public sealed class DelegationCause<TDisposition, TWitness>
{
    internal DelegationCause(
        TDisposition disposition,
        CompletionEvidence<TWitness> evidence)
    {
        Disposition = disposition;
        Evidence = evidence;
    }

    public TDisposition Disposition { get; }

    public CompletionEvidence<TWitness> Evidence { get; }
}

public static class DelegationCause
{
    public static DelegationCause<TDisposition, TWitness> Create<TDisposition, TWitness>(
        TDisposition disposition,
        CompletionEvidence<TWitness> evidence)
    {
        ArgumentNullException.ThrowIfNull(disposition);
        ArgumentNullException.ThrowIfNull(evidence);
        return new(disposition, evidence);
    }
}

// What a source offers for one member of a row-handoff candidate.
public sealed class RowMemberOffer<TRow, TDisposition, TWitness>
{
    private RowMemberOffer(
        IReadOnlyList<TRow>? values,
        DelegationCause<TDisposition, TWitness> cause)
    {
        Values = values;
        Cause = cause;
    }

    internal IReadOnlyList<TRow>? Values { get; }

    internal DelegationCause<TDisposition, TWitness> Cause { get; }

    // Acquired values are snapshotted here, before publication: a deferred
    // sequence is enumerated now, so no source enumeration, acquisition, or
    // source failure can survive into the published result. The row objects
    // themselves are caller-owned and are never cloned.
    public static RowMemberOffer<TRow, TDisposition, TWitness> Rows(
        IEnumerable<TRow> values,
        DelegationCause<TDisposition, TWitness> cause)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(cause);
        return new(DelegationSnapshot.Copy(values), cause);
    }

    public static RowMemberOffer<TRow, TDisposition, TWitness> Unavailable(
        DelegationCause<TDisposition, TWitness> cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return new(null, cause);
    }
}

// What a source offers for one member of an exact-Count candidate.
public sealed class CountMemberOffer<TDisposition, TWitness>
{
    private CountMemberOffer(
        int? count,
        DelegationCause<TDisposition, TWitness> cause)
    {
        Count = count;
        Cause = cause;
    }

    internal int? Count { get; }

    internal DelegationCause<TDisposition, TWitness> Cause { get; }

    public static CountMemberOffer<TDisposition, TWitness> Exact(
        int count,
        DelegationCause<TDisposition, TWitness> cause)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(cause);
        return new(count, cause);
    }

    public static CountMemberOffer<TDisposition, TWitness> NotExact(
        DelegationCause<TDisposition, TWitness> cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return new(null, cause);
    }
}
