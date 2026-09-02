namespace DotnetInspector.RowSelection;

public static class RowSelectionExecutor
{
    public static RowSelectionResult<T> Apply<T, TOrder>(
        IReadOnlyList<T> values,
        RowSelectionPlan<TOrder> plan,
        Func<TOrder, IComparer<T>?>? comparerResolver = null)
        where TOrder : notnull
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(plan);

        var context =
            new EvaluationContext<T, TOrder>(
                plan,
                comparerResolver);
        EvaluationResult<T> result = context.Apply(values);
        return result.Failure is null
            ? RowSelectionResult<T>.Success(result.Values)
            : RowSelectionResult<T>.Failed(result.Failure);
    }

    public static NamedRowSelectionResult<T> ApplyNamed<T, TOrder>(
        IReadOnlyList<NamedRowSequence<T>> sequences,
        RowSelectionPlan<TOrder> plan,
        Func<TOrder, IComparer<T>?>? comparerResolver = null)
        where TOrder : notnull
    {
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(plan);

        var context =
            new EvaluationContext<T, TOrder>(
                plan,
                comparerResolver);
        var inputs =
            new NamedRowSequence<T>[sequences.Count];
        var keys = new HashSet<RowSequenceKey>();
        for (int index = 0; index < sequences.Count; index++)
        {
            NamedRowSequence<T> sequence =
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

            inputs[index] = sequence;
        }

        var outputs =
            new NamedRowSequence<T>[inputs.Length];
        for (int index = 0; index < inputs.Length; index++)
        {
            NamedRowSequence<T> input = inputs[index];
            EvaluationResult<T> result =
                context.Apply(input.Values);
            if (result.Failure is not null)
            {
                return NamedRowSelectionResult<T>.Failed(
                    new NamedRowWindowFailure(
                        input.Key,
                        result.Failure));
            }

            outputs[index] =
                NamedRowSequence<T>.Create(
                    input.Key,
                    result.Values);
        }

        return NamedRowSelectionResult<T>.Success(outputs);
    }

    private sealed class EvaluationContext<T, TOrder>
        where TOrder : notnull
    {
        private readonly RowSelectionPlan<TOrder> _plan;
        private readonly Func<TOrder, IComparer<T>?>? _resolver;
        private readonly IComparer<T>?[] _comparers;
        private readonly bool[] _resolved;

        public EvaluationContext(
            RowSelectionPlan<TOrder> plan,
            Func<TOrder, IComparer<T>?>? resolver)
        {
            _plan = plan;
            _resolver = resolver;
            _comparers =
                new IComparer<T>?[plan.Stages.Count];
            _resolved = new bool[plan.Stages.Count];

            if (resolver is not null)
                return;

            for (int index = 0;
                 index < plan.Stages.Count;
                 index++)
            {
                if (plan.Stages[index].Kind
                    is RowSelectionStageKind.Top)
                {
                    throw new InvalidOperationException(
                        $"Top stage {index + 1} requires a comparer resolver.");
                }
            }
        }

        public EvaluationResult<T> Apply(
            IReadOnlyList<T> values)
        {
            var current = new List<T>(values.Count);
            for (int index = 0; index < values.Count; index++)
                current.Add(values[index]);

            for (int stageIndex = 0;
                 stageIndex < _plan.Stages.Count;
                 stageIndex++)
            {
                RowSelectionStage<TOrder> stage =
                    _plan.Stages[stageIndex];
                switch (stage.Kind)
                {
                    case RowSelectionStageKind.Head:
                        current =
                            Head(current, stage.Count);
                        break;
                    case RowSelectionStageKind.Tail:
                        current =
                            Tail(current, stage.Count);
                        break;
                    case RowSelectionStageKind.Window:
                    {
                        RowWindowFailure? failure =
                            Window(
                                current,
                                stage.Start,
                                stage.End,
                                stageIndex + 1,
                                out List<T> selected);
                        if (failure is not null)
                            return new([], failure);
                        current = selected;
                        break;
                    }
                    case RowSelectionStageKind.Top:
                        current =
                            Top(
                                current,
                                stage.Count,
                                Comparer(stageIndex, stage));
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported row-selection stage kind {stage.Kind}.");
                }
            }

            return new(current, null);
        }

        private IComparer<T> Comparer(
            int stageIndex,
            RowSelectionStage<TOrder> stage)
        {
            if (_resolved[stageIndex])
                return _comparers[stageIndex]!;

            IComparer<T>? comparer =
                _resolver!(stage.Order);
            if (comparer is null)
            {
                throw new InvalidOperationException(
                    $"Top stage {stageIndex + 1} resolved no comparer.");
            }

            _comparers[stageIndex] = comparer;
            _resolved[stageIndex] = true;
            return comparer;
        }
    }

    private static List<T> Head<T>(
        List<T> values,
        int count)
    {
        int length = Math.Min(count, values.Count);
        var selected = new List<T>(length);
        for (int index = 0; index < length; index++)
            selected.Add(values[index]);
        return selected;
    }

    private static List<T> Tail<T>(
        List<T> values,
        int count)
    {
        int length = Math.Min(count, values.Count);
        int start = values.Count - length;
        var selected = new List<T>(length);
        for (int index = start; index < values.Count; index++)
            selected.Add(values[index]);
        return selected;
    }

    private static RowWindowFailure? Window<T>(
        List<T> values,
        int? start,
        int? end,
        int stageNumber,
        out List<T> selected)
    {
        if (start is null && end is null)
        {
            selected = values;
            return null;
        }

        int requiredPosition =
            end ?? start!.Value;
        if (requiredPosition > values.Count)
        {
            selected = [];
            return new RowWindowFailure(
                stageNumber,
                requiredPosition,
                values.Count);
        }

        int firstIndex = (start ?? 1) - 1;
        int endExclusive = end ?? values.Count;
        selected =
            new List<T>(endExclusive - firstIndex);
        for (int index = firstIndex;
             index < endExclusive;
             index++)
        {
            selected.Add(values[index]);
        }

        return null;
    }

    private static List<T> Top<T>(
        List<T> values,
        int count,
        IComparer<T> comparer)
    {
        T[] ranked = [.. values];
        if (ranked.Length > 1)
        {
            var buffer = new T[ranked.Length];
            StableMergeSort(
                ranked,
                buffer,
                0,
                ranked.Length,
                comparer);
        }

        int length = Math.Min(count, ranked.Length);
        var selected = new List<T>(length);
        for (int index = 0; index < length; index++)
            selected.Add(ranked[index]);
        return selected;
    }

    private static void StableMergeSort<T>(
        T[] values,
        T[] buffer,
        int start,
        int length,
        IComparer<T> comparer)
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

    private readonly record struct EvaluationResult<T>(
        IReadOnlyList<T> Values,
        RowWindowFailure? Failure);
}
