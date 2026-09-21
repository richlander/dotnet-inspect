
namespace QuerySpace.Rows;

public static class RowQueryExecutor
{
    public static RowSelectionResult<TRow> Apply<TRow>(
        IReadOnlyList<TRow> rows,
        ResolvedRowQueryPlan<TRow> plan)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(plan);

        IComparer<TRow>? baselineComparer =
            ResolveBaselineComparer(plan);
        List<TRow> selected =
            Prepare(
                rows,
                plan,
                baselineComparer);

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

        var prepared =
            new NamedRowSequence<TRow>[sequences.Count];
        var keys = new HashSet<RowSequenceKey>();
        IComparer<TRow>? baselineComparer =
            ResolveBaselineComparer(plan);
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
            List<TRow> rows = Prepare(
                sequence.Values,
                plan,
                baselineComparer);
            prepared[index] =
                NamedRowSequence<TRow>.Create(
                    sequence.Key,
                    rows);
        }

        return RowSelectionExecutor.ApplyNamed(
            prepared,
            plan.SelectionPlan,
            plan.ResolveOrder);
    }

    private static List<TRow> Prepare<TRow>(
        IReadOnlyList<TRow> rows,
        ResolvedRowQueryPlan<TRow> plan,
        IComparer<TRow>? baselineComparer)
    {
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

        if (baselineComparer is not null)
        {
            if (selected.Count > 1)
                StableSort(selected, baselineComparer);
        }

        return selected;
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
