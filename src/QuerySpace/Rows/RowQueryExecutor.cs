
namespace QuerySpace.Rows;

public static class RowQueryExecutor
{
    public static bool CanApplyCount<TRow>(
        ResolvedRowQueryPlan<TRow> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Predicates.Count != 0
            || plan.BaselineOrder is not null)
        {
            return false;
        }

        return RowSelectionCountExecutor.CanApply(
            plan.SelectionPlan);
    }

    public static bool TryApplyCount<TRow>(
        int sourceCount,
        ResolvedRowQueryPlan<TRow> plan,
        out RowSelectionCountResult result)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceCount);
        ArgumentNullException.ThrowIfNull(plan);

        if (!CanApplyCount(plan))
        {
            result = default;
            return false;
        }

        return RowSelectionCountExecutor.TryApply(
            sourceCount,
            plan.SelectionPlan,
            out result);
    }

    public static RowSelectionResult<TRow> Apply<TRow>(
        IReadOnlyList<TRow> rows,
        ResolvedRowQueryPlan<TRow> plan)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Predicates.Count == 0
            && plan.BaselineOrder is null)
        {
            return RowSelectionExecutor.Apply(
                rows,
                plan.SelectionPlan,
                plan.ResolveOrder);
        }

        List<TRow> selected =
            Filter(rows, plan);
        ApplyBaselineOrder(
            selected,
            ResolveBaselineComparer(plan));

        return RowSelectionExecutor.Apply(
            selected,
            plan.SelectionPlan,
            plan.ResolveOrder);
    }

    public static NamedRowSelectionResult<TRow> ApplyNamed<TRow>(
        IReadOnlyList<NamedRowSequence<TRow>> sequences,
        ResolvedRowQueryPlan<TRow> plan)
    {
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Predicates.Count == 0
            && plan.BaselineOrder is null)
        {
            return RowSelectionExecutor.ApplyNamed(
                sequences,
                plan.SelectionPlan,
                plan.ResolveOrder);
        }

        var prepared =
            new NamedRowSequence<TRow>[sequences.Count];
        var preparedRows =
            new List<TRow>[sequences.Count];
        var keys = new HashSet<RowSequenceKey>();
        for (int index = 0; index < sequences.Count; index++)
        {
            NamedRowSequence<TRow> sequence =
                sequences[index]
                ?? throw new ArgumentNullException(
                    nameof(sequences),
                    $"Sequence {index + 1} is null.");
            if (!keys.Add(sequence.Key))
            {
                throw new ArgumentException(
                    $"Sequence key {sequence.Key.Value} is duplicated.",
                    nameof(sequences));
            }
            preparedRows[index] = Filter(
                sequence.Values,
                plan);
        }

        IComparer<TRow>? baselineComparer =
            ResolveBaselineComparer(plan);
        for (int index = 0; index < sequences.Count; index++)
        {
            List<TRow> rows = preparedRows[index];
            ApplyBaselineOrder(rows, baselineComparer);
            prepared[index] =
                NamedRowSequence<TRow>.Create(
                    sequences[index].Key,
                    rows);
        }

        return RowSelectionExecutor.ApplyNamed(
            prepared,
            plan.SelectionPlan,
            plan.ResolveOrder);
    }

    private static List<TRow> Filter<TRow>(
        IReadOnlyList<TRow> rows,
        ResolvedRowQueryPlan<TRow> plan)
    {
        var selected = new List<TRow>(rows.Count);
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            TRow row = rows[rowIndex];
            if (plan.MatchesPredicates(row))
                selected.Add(row);
        }

        return selected;
    }

    private static void ApplyBaselineOrder<TRow>(
        List<TRow> rows,
        IComparer<TRow>? baselineComparer)
    {
        if (baselineComparer is not null && rows.Count > 1)
            StableSort(rows, baselineComparer);
    }

    private static IComparer<TRow>? ResolveBaselineComparer<TRow>(
        ResolvedRowQueryPlan<TRow> plan)
    {
        if (plan.BaselineOrder is null)
            return null;

        return plan.CreateBaselineComparer()
            ?? throw new InvalidOperationException(
                "A resolved baseline order produced no comparer.");
    }

    private static void StableSort<TRow>(
        List<TRow> rows,
        IComparer<TRow> comparer)
    {
        TRow[] values = [.. rows];
        var buffer = new TRow[values.Length];
        StableMergeSort(
            values,
            buffer,
            0,
            values.Length,
            comparer);
        rows.Clear();
        rows.AddRange(values);
    }

    private static void StableMergeSort<TRow>(
        TRow[] values,
        TRow[] buffer,
        int start,
        int length,
        IComparer<TRow> comparer)
    {
        if (length <= 1)
            return;

        int leftLength = length / 2;
        int rightStart = start + leftLength;
        int rightLength = length - leftLength;
        StableMergeSort(
            values,
            buffer,
            start,
            leftLength,
            comparer);
        StableMergeSort(
            values,
            buffer,
            rightStart,
            rightLength,
            comparer);

        int left = start;
        int leftEnd = rightStart;
        int right = rightStart;
        int rightEnd = start + length;
        int destination = start;

        while (left < leftEnd && right < rightEnd)
        {
            if (comparer.Compare(
                    values[left],
                    values[right]) <= 0)
            {
                buffer[destination++] = values[left++];
            }
            else
            {
                buffer[destination++] = values[right++];
            }
        }

        while (left < leftEnd)
            buffer[destination++] = values[left++];
        while (right < rightEnd)
            buffer[destination++] = values[right++];
        Array.Copy(
            buffer,
            start,
            values,
            start,
            length);
    }
}
