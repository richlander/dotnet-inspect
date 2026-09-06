namespace DotnetInspector.SourceDelegation;

// What one delegation attempt produced: either the published result of the one
// accepted candidate, or a decline that leaves the caller's reference path
// untouched.
public sealed class SourceDelegationOutcome<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
    where TMember : notnull
{
    private SourceDelegationOutcome(
        SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>? result)
    {
        Result = result;
    }

    public SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>? Result { get; }

    public bool IsDeclined => Result is null;

    internal static SourceDelegationOutcome<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
        Declined { get; } = new(null);

    internal static SourceDelegationOutcome<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
        Accepted(
            SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness> result) =>
        new(result);
}

// The single public entry point of the protocol. Planning and acceptance are
// one invocation, so no accepted-plan execution handle escapes to be replayed.
public static class SourceDelegationRunner
{
    public static async ValueTask<SourceDelegationOutcome<TMember, TRow, TInput, TOperation, TDisposition, TWitness>>
        RunAsync<TMember, TRow, TInput, TOperation, TDisposition, TWitness>(
            IDelegationSource<TMember, TRow, TInput, TOperation, TDisposition, TWitness> source,
            IEnumerable<DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness>> candidates,
            CancellationToken cancellationToken = default)
        where TMember : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(candidates);

        // Planning: immutable candidate and capability facts only, in
        // declaration order, with no source work and no partial effect.
        DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness>? selected = null;
        foreach (DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate, nameof(candidates));
            if (source.Supports(candidate))
            {
                selected = candidate;
                break;
            }
        }

        if (selected is null)
        {
            return SourceDelegationOutcome<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
                .Declined;
        }

        // Acceptance: exactly one execution, one publication, and no fallback to
        // another candidate afterwards, whatever the reply says.
        SourceDelegationReply<TMember, TRow, TDisposition, TWitness> reply =
            await source.ExecuteAsync(selected, cancellationToken).ConfigureAwait(false);

        if (reply is null)
        {
            throw new InvalidOperationException(
                "The source published no reply for the accepted candidate.");
        }

        // The published result is constructed here, from the candidate this
        // runner selected: the association is made by construction rather than
        // asserted by the source.
        return SourceDelegationOutcome<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
            .Accepted(reply.BindTo(selected));
    }
}
