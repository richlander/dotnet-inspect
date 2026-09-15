namespace DotnetInspector.SourceDelegation;

// A cooperating source. Supports is a pure planning question answered from
// immutable candidate and capability facts; ExecuteAsync is the single accepted
// invocation.
public interface IDelegationSource<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
    where TMember : notnull
{
    bool Supports(
        DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate);

    // Expected acquisition and cancellation outcomes use typed causes;
    // unexpected implementation exceptions propagate to the caller.
    ValueTask<SourceDelegationReply<TMember, TRow, TDisposition, TWitness>> ExecuteAsync(
        DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate,
        CancellationToken cancellationToken);
}
