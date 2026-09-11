namespace DotnetInspector.SourceDelegation;

// One published outcome of one accepted candidate. The algebra is closed, and
// every branch is constructed by the runner from the candidate it selected, so
// the published result carries that candidate — and therefore the residual the
// caller retained for it — as typed structure.
public abstract class SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
    where TMember : notnull
{
    private protected SourceDelegationResult(
        DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate)
    {
        Candidate = candidate;
    }

    public DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> Candidate { get; }

    public DelegationResultShape Shape => Candidate.Shape;

    public DelegationGroup<TMember> Group => Candidate.Group;

    public CompletionRequirement<TMember, TDisposition, TWitness> CompletionRequirement =>
        Candidate.CompletionRequirement;
}

public sealed class RowHandoffResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
    : SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
    where TMember : notnull
{
    internal RowHandoffResult(
        DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate,
        IReadOnlyList<RowMemberOutcome<TMember, TRow, TDisposition, TWitness>> outcomes)
        : base(candidate)
    {
        Outcomes = outcomes;
    }

    // Exactly one outcome per accepted member, in execution-group order.
    public IReadOnlyList<RowMemberOutcome<TMember, TRow, TDisposition, TWitness>> Outcomes { get; }

    // The entries whose rows the completion requirement accepted. Only these
    // are eligible for the caller's retained residual; the caller may still
    // suppress every residual invocation.
    public IEnumerable<RowValuesOutcome<TMember, TRow, TDisposition, TWitness>> UsableOutcomes =>
        Outcomes.OfType<RowValuesOutcome<TMember, TRow, TDisposition, TWitness>>();
}

public sealed class ExactCountResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
    : SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
    where TMember : notnull
{
    internal ExactCountResult(
        DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate,
        IReadOnlyList<ExactCountMemberValue<TMember, TWitness>> counts)
        : base(candidate)
    {
        Counts = counts;
    }

    // Exactly one non-negative exact count per accepted member, in
    // execution-group order. No rows, no partial map, no invented total.
    public IReadOnlyList<ExactCountMemberValue<TMember, TWitness>> Counts { get; }
}

public sealed class NotSatisfiedResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
    : SourceDelegationResult<TMember, TRow, TInput, TOperation, TDisposition, TWitness>
    where TMember : notnull
{
    internal NotSatisfiedResult(
        DelegationCandidate<TMember, TInput, TOperation, TDisposition, TWitness> candidate,
        IReadOnlyList<NotSatisfiedMember<TMember, TDisposition, TWitness>> members)
        : base(candidate)
    {
        Members = members;
    }

    // One disposition-and-evidence entry per accepted member, in
    // execution-group order, and no row or Count payload.
    public IReadOnlyList<NotSatisfiedMember<TMember, TDisposition, TWitness>> Members { get; }
}
