using QuerySpace.Composition;
using QuerySpace.Rows;

namespace QueryOverflow;

public sealed class QueryOverflowPlan<TRow>
{
    private readonly ResolvedRowQueryPlan<TRow> _rowPlan;
    private readonly Func<TRow, TRow>? _snapshotRow;

    private QueryOverflowPlan(
        ResolvedRowQueryPlan<TRow> rowPlan,
        QuerySpaceTerminalRequirement terminal,
        int? headCount,
        Func<TRow, TRow>? snapshotRow)
    {
        _rowPlan = rowPlan;
        Terminal = terminal;
        HeadCount = headCount;
        _snapshotRow = snapshotRow;
    }

    public QuerySpaceTerminalRequirement Terminal { get; }

    internal int? HeadCount { get; }

    public static QueryOverflowAdmission<TRow> AdmitRows(
        ResolvedRowQueryPlan<TRow> rowPlan,
        Func<TRow, TRow> snapshotRow)
    {
        ArgumentNullException.ThrowIfNull(snapshotRow);
        return Admit(
            rowPlan,
            QuerySpaceTerminalRequirement.Rows,
            snapshotRow);
    }

    public static QueryOverflowAdmission<TRow> AdmitCount(
        ResolvedRowQueryPlan<TRow> rowPlan) =>
        Admit(
            rowPlan,
            QuerySpaceTerminalRequirement.Count,
            snapshotRow: null);

    private static QueryOverflowAdmission<TRow> Admit(
        ResolvedRowQueryPlan<TRow> rowPlan,
        QuerySpaceTerminalRequirement terminal,
        Func<TRow, TRow>? snapshotRow)
    {
        ArgumentNullException.ThrowIfNull(rowPlan);

        if (rowPlan.BaselineOrder is not null)
        {
            return QueryOverflowAdmission<TRow>.Declined(
                QueryOverflowDeclineReason
                    .BaselineOrderRequiresCompletePopulation);
        }

        int? headCount = null;
        for (int index = 0;
             index < rowPlan.SelectionPlan.Stages.Count;
             index++)
        {
            RowSelectionStage<ResolvedRowQueryOrderIdentity> stage =
                rowPlan.SelectionPlan.Stages[index];
            if (stage.Kind is not RowSelectionStageKind.Head)
            {
                return QueryOverflowAdmission<TRow>.Declined(
                    QueryOverflowDeclineReason
                        .UnsupportedSelectionStage);
            }

            headCount = headCount is null
                ? stage.Count
                : Math.Min(headCount.Value, stage.Count);
        }

        if (terminal is QuerySpaceTerminalRequirement.Rows
            && rowPlan.PredicateKeyIdentities.Count != 0)
        {
            return QueryOverflowAdmission<TRow>.Declined(
                QueryOverflowDeclineReason
                    .RowsWithPredicatesRequireAtomicPublication);
        }

        if (headCount is not null
            && rowPlan.PredicateKeyIdentities.Count != 0)
        {
            return QueryOverflowAdmission<TRow>.Declined(
                QueryOverflowDeclineReason
                    .PredicatesWithHeadRequireCompletePopulation);
        }

        return QueryOverflowAdmission<TRow>.Accepted(
            new(rowPlan, terminal, headCount, snapshotRow));
    }

    public QueryOverflowExecution<TRow> Start() =>
        new(this);

    internal bool MatchesPredicates(TRow row) =>
        _rowPlan.MatchesPredicates(row);

    internal TRow SnapshotRow(TRow row) =>
        _snapshotRow is not null
            ? _snapshotRow(row)
            : throw new InvalidOperationException(
                "A Count plan cannot snapshot a row.");
}
