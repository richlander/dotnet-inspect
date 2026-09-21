
namespace QuerySpace.Rows;

public static class RowQueryExecutor
{
    public static RowSelectionResult<TRow> Apply<TRow>(
        IReadOnlyList<TRow> rows,
        ResolvedRowQueryPlan<TRow> plan)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(plan);

        var selected = new List<TRow>(rows.Count);
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            TRow row = rows[rowIndex];
            bool matches = true;
            for (int predicateIndex = 0;
                 predicateIndex < plan.Predicates.Count;
                 predicateIndex++)
            {
                if (plan.Predicates[predicateIndex](row))
                    continue;

                matches = false;
                break;
            }

            if (matches)
                selected.Add(row);
        }

        if (plan.BaselineOrder is not null)
        {
            IComparer<TRow> comparer =
                plan.CreateBaselineComparer()
                ?? throw new InvalidOperationException(
                    "A resolved baseline order produced no comparer.");
            if (selected.Count > 1)
                StableSort(selected, comparer);
        }

        return RowSelectionExecutor.Apply(
            selected,
            plan.SelectionPlan,
            plan.ResolveOrder);
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
