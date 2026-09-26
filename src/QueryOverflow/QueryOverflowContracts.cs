namespace QueryOverflow;

public enum QueryOverflowExecutionState
{
    Created,
    NeedsInput,
    RowsAvailable,
    Completed,
    Failed,
    Cancelled
}

public enum QueryOverflowDeclineReason
{
    RowsWithPredicatesRequireAtomicPublication,
    PredicatesWithHeadRequireCompletePopulation,
    BaselineOrderRequiresCompletePopulation,
    UnsupportedSelectionStage
}

public readonly struct QueryOverflowInputRequest
{
    private readonly object? _executionIdentity;
    private readonly int _generation;

    internal QueryOverflowInputRequest(
        object executionIdentity,
        int generation,
        int maximumCandidateRows)
    {
        _executionIdentity = executionIdentity;
        _generation = generation;
        MaximumCandidateRows = maximumCandidateRows;
    }

    public int MaximumCandidateRows { get; }

    internal bool Matches(object executionIdentity, int generation) =>
        ReferenceEquals(_executionIdentity, executionIdentity)
        && _generation == generation;
}

public sealed class QueryOverflowAdmission<TRow>
{
    private QueryOverflowAdmission(
        QueryOverflowPlan<TRow>? plan,
        QueryOverflowDeclineReason? declineReason)
    {
        Plan = plan;
        DeclineReason = declineReason;
    }

    public bool IsAccepted => Plan is not null;

    public QueryOverflowPlan<TRow>? Plan { get; }

    public QueryOverflowDeclineReason? DeclineReason { get; }

    internal static QueryOverflowAdmission<TRow> Accepted(
        QueryOverflowPlan<TRow> plan) =>
        new(plan, null);

    internal static QueryOverflowAdmission<TRow> Declined(
        QueryOverflowDeclineReason reason) =>
        new(null, reason);
}

public sealed class QueryOverflowStep<TRow>
{
    private readonly int _count;

    internal QueryOverflowStep(
        QueryOverflowExecutionState state,
        int consumedRows,
        IReadOnlyList<TRow> rows,
        bool hasCount,
        int count)
    {
        State = state;
        ConsumedRows = consumedRows;
        Rows = rows;
        HasCount = hasCount;
        _count = count;
    }

    public QueryOverflowExecutionState State { get; }

    public int ConsumedRows { get; }

    public IReadOnlyList<TRow> Rows { get; }

    public bool HasCount { get; }

    public int Count =>
        HasCount
            ? _count
            : throw new InvalidOperationException(
                "This step has no terminal Count result.");
}
