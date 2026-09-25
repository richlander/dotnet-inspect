using QuerySpace.Composition;
using QuerySpace.Rows;

namespace QueryOverflow;

public sealed class QueryOverflowPlan<TRow>
{
    private readonly ResolvedRowQueryPlan<TRow> _rowPlan;

    private QueryOverflowPlan(
        ResolvedRowQueryPlan<TRow> rowPlan,
        QuerySpaceTerminalRequirement terminal,
        int? headCount)
    {
        _rowPlan = rowPlan;
        Terminal = terminal;
        HeadCount = headCount;
    }

    public QuerySpaceTerminalRequirement Terminal { get; }

    internal int? HeadCount { get; }

    public static QueryOverflowAdmission<TRow> Admit(
        ResolvedRowQueryPlan<TRow> rowPlan,
        QuerySpaceTerminalRequirement terminal)
    {
        ArgumentNullException.ThrowIfNull(rowPlan);
        if (!Enum.IsDefined(terminal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminal),
                terminal,
                "Unsupported QuerySpace terminal requirement.");
        }

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
            new(rowPlan, terminal, headCount));
    }

    public QueryOverflowExecution<TRow> Start() =>
        new(this);

    internal bool MatchesPredicates(TRow row) =>
        _rowPlan.MatchesPredicates(row);
}
