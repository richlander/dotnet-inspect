using System.Diagnostics.CodeAnalysis;

namespace QuerySpace.Rows;

public sealed class ForwardRowQueryPlan<TRow>
{
    private readonly IReadOnlyList<Predicate<TRow>> _predicates;

    internal ForwardRowQueryPlan(
        ResolvedRowQueryPlan<TRow> resolvedPlan,
        IReadOnlyList<Predicate<TRow>> predicates,
        int? maximumResultRows)
    {
        ResolvedPlan = resolvedPlan;
        _predicates = predicates;
        MaximumResultRows = maximumResultRows;
    }

    public ResolvedRowQueryPlan<TRow> ResolvedPlan { get; }

    public int? MaximumResultRows { get; }

    public bool Matches(TRow row)
    {
        for (int index = 0; index < _predicates.Count; index++)
        {
            if (!_predicates[index](row))
                return false;
        }

        return true;
    }

    public bool IsSatisfied(int resultRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(resultRows);
        return MaximumResultRows is int maximum
            && resultRows >= maximum;
    }
}

public static class ForwardRowQueryPlanner
{
    public static bool TryCreate<TRow>(
        ResolvedRowQueryPlan<TRow> resolvedPlan,
        [NotNullWhen(true)] out ForwardRowQueryPlan<TRow>? forwardPlan)
    {
        ArgumentNullException.ThrowIfNull(resolvedPlan);

        if (resolvedPlan.BaselineOrder is not null)
        {
            forwardPlan = null;
            return false;
        }

        int? maximumResultRows = null;
        foreach (RowSelectionStage<ResolvedRowQueryOrderIdentity> stage
            in resolvedPlan.SelectionPlan.Stages)
        {
            if (stage.Kind is not RowSelectionStageKind.Head)
            {
                forwardPlan = null;
                return false;
            }

            maximumResultRows = maximumResultRows is int maximum
                ? Math.Min(maximum, stage.Count)
                : stage.Count;
        }

        forwardPlan = new(
            resolvedPlan,
            resolvedPlan.Predicates,
            maximumResultRows);
        return true;
    }
}
