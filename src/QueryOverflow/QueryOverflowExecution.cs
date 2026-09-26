using System.Collections.ObjectModel;
using QuerySpace.Composition;

namespace QueryOverflow;

public sealed class QueryOverflowExecution<TRow>
{
    private static readonly IReadOnlyList<TRow> EmptyRows =
        Array.AsReadOnly(Array.Empty<TRow>());

    private readonly QueryOverflowPlan<TRow> _plan;
    private readonly object _identity = new();
    private int _generation;
    private bool _requestOutstanding;
    private int _candidateRowsConsumed;
    private int _applicableRowsSeen;
    private int _publishedRows;

    internal QueryOverflowExecution(QueryOverflowPlan<TRow> plan)
    {
        _plan = plan;
    }

    public QueryOverflowExecutionState State { get; private set; } =
        QueryOverflowExecutionState.Created;

    public int CandidateRowsConsumed => _candidateRowsConsumed;

    public int ApplicableRowsSeen => _applicableRowsSeen;

    public int PublishedRows => _publishedRows;

    public bool TryRequestInput(
        int ownerBatchCeiling,
        int finalRowCredit,
        out QueryOverflowInputRequest request)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            ownerBatchCeiling);
        ArgumentOutOfRangeException.ThrowIfNegative(
            finalRowCredit);

        if (IsTerminal(State))
        {
            request = default;
            return false;
        }

        if (_requestOutstanding)
        {
            throw new InvalidOperationException(
                "The prior candidate-row request has not been advanced.");
        }

        if (_plan.Terminal
                is QuerySpaceTerminalRequirement.Rows
            && finalRowCredit == 0)
        {
            request = default;
            return false;
        }

        int maximumCandidateRows = ownerBatchCeiling;
        if (_plan.Terminal
            is QuerySpaceTerminalRequirement.Rows)
        {
            maximumCandidateRows =
                Math.Min(maximumCandidateRows, finalRowCredit);
        }

        if (_plan.HeadCount is int headCount)
        {
            int remaining = headCount - _applicableRowsSeen;
            if (remaining <= 0)
            {
                throw new InvalidOperationException(
                    "A completed Head execution requested more input.");
            }

            maximumCandidateRows =
                Math.Min(maximumCandidateRows, remaining);
        }

        _generation = checked(_generation + 1);
        _requestOutstanding = true;
        State = QueryOverflowExecutionState.NeedsInput;
        request =
            new(
                _identity,
                _generation,
                maximumCandidateRows);
        return true;
    }

    public QueryOverflowStep<TRow> Advance(
        QueryOverflowInputRequest request,
        IReadOnlyList<TRow> candidateRows,
        bool sourceCompleted)
    {
        ArgumentNullException.ThrowIfNull(candidateRows);
        if (IsTerminal(State))
        {
            throw new InvalidOperationException(
                "A terminal QueryOverflow execution cannot advance.");
        }

        if (!_requestOutstanding
            || !request.Matches(_identity, _generation))
        {
            throw new InvalidOperationException(
                "The input does not match the outstanding candidate-row request.");
        }

        if (candidateRows.Count > request.MaximumCandidateRows)
        {
            throw new ArgumentException(
                "The candidate batch exceeds the requested maximum.",
                nameof(candidateRows));
        }

        if (candidateRows.Count == 0 && !sourceCompleted)
        {
            throw new ArgumentException(
                "An empty batch must report source completion.",
                nameof(candidateRows));
        }

        _requestOutstanding = false;
        var published = new List<TRow>(candidateRows.Count);
        int consumedRows = 0;
        bool stepSucceeded = false;
        try
        {
            for (int index = 0; index < candidateRows.Count; index++)
            {
                TRow row = candidateRows[index];
                consumedRows++;
                _candidateRowsConsumed =
                    checked(_candidateRowsConsumed + 1);

                if (!_plan.MatchesPredicates(row))
                    continue;

                _applicableRowsSeen =
                    checked(_applicableRowsSeen + 1);
                if (_plan.Terminal
                    is QuerySpaceTerminalRequirement.Rows)
                {
                    published.Add(_plan.SnapshotRow(row));
                    _publishedRows =
                        checked(_publishedRows + 1);
                }

                if (_plan.HeadCount == _applicableRowsSeen)
                    break;
            }

            bool semanticCompleted =
                _plan.HeadCount == _applicableRowsSeen;
            bool completed =
                semanticCompleted || sourceCompleted;
            State = completed
                ? QueryOverflowExecutionState.Completed
                : published.Count == 0
                    ? QueryOverflowExecutionState.NeedsInput
                    : QueryOverflowExecutionState.RowsAvailable;

            bool hasCount =
                completed
                && _plan.Terminal
                    is QuerySpaceTerminalRequirement.Count;
            stepSucceeded = true;
            return new(
                State,
                consumedRows,
                Snapshot(published),
                hasCount,
                _applicableRowsSeen);
        }
        finally
        {
            if (!stepSucceeded)
                State = QueryOverflowExecutionState.Failed;
        }
    }

    public void Fail() =>
        Stop(QueryOverflowExecutionState.Failed);

    public void Cancel() =>
        Stop(QueryOverflowExecutionState.Cancelled);

    private void Stop(QueryOverflowExecutionState terminalState)
    {
        if (IsTerminal(State))
        {
            throw new InvalidOperationException(
                "A terminal QueryOverflow execution cannot transition.");
        }

        _requestOutstanding = false;
        State = terminalState;
    }

    private static bool IsTerminal(
        QueryOverflowExecutionState state) =>
        state is QueryOverflowExecutionState.Completed
            or QueryOverflowExecutionState.Failed
            or QueryOverflowExecutionState.Cancelled;

    private static IReadOnlyList<TRow> Snapshot(
        List<TRow> rows)
    {
        if (rows.Count == 0)
            return EmptyRows;

        return new ReadOnlyCollection<TRow>([.. rows]);
    }
}
