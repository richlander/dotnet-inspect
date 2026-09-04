using DotnetInspector.RowSelection;

namespace DotnetInspector.Sections;

public static class RowsCohortExecutor
{
    public static RowsCohortResult<TIdentity, T> ApplyUnordered<
        TIdentity,
        T,
        TOrderOperand>(
        IReadOnlyList<RowsCohortSequence<TIdentity, T>> sequences,
        RowSelectionIntent<TOrderOperand> intent)
        where TIdentity : notnull
        where TOrderOperand : notnull
    {
        ArgumentNullException.ThrowIfNull(intent);

        var stages =
            new RowSelectionStage<TOrderOperand>[
                intent.Operations.Count];
        for (int index = 0;
             index < intent.Operations.Count;
             index++)
        {
            RowSelectionIntentOperation<TOrderOperand> operation =
                intent.Operations[index];
            stages[index] =
                operation.Kind switch
                {
                    RowSelectionStageKind.Head =>
                        RowSelectionStage<TOrderOperand>.Head(
                            operation.Count),
                    RowSelectionStageKind.Tail =>
                        RowSelectionStage<TOrderOperand>.Tail(
                            operation.Count),
                    RowSelectionStageKind.Window =>
                        RowSelectionStage<TOrderOperand>.Window(
                            operation.Start,
                            operation.End),
                    RowSelectionStageKind.Top =>
                        throw new InvalidOperationException(
                            "An unordered rows cohort cannot apply a Top operation."),
                    _ => throw new InvalidOperationException(
                        $"Unsupported row-selection operation {operation.Kind}.")
                };
        }

        return Apply(
            sequences,
            RowSelectionPlan<TOrderOperand>.Create(stages));
    }

    public static RowsCohortResult<TIdentity, T> Apply<
        TIdentity,
        T,
        TOrder>(
        IReadOnlyList<RowsCohortSequence<TIdentity, T>> sequences,
        RowSelectionPlan<TOrder> plan,
        Func<TOrder, IComparer<T>?>? comparerResolver = null)
        where TIdentity : notnull
        where TOrder : notnull
    {
        ArgumentNullException.ThrowIfNull(sequences);
        ArgumentNullException.ThrowIfNull(plan);
        if (sequences.Count == 0)
        {
            throw new ArgumentException(
                "A rows cohort must contain at least one sequence.",
                nameof(sequences));
        }

        var named =
            new NamedRowSequence<T>[sequences.Count];
        var keyByIdentity =
            new Dictionary<TIdentity, RowSequenceKey>();
        var identityByKey =
            new Dictionary<RowSequenceKey, TIdentity>();

        for (int index = 0; index < sequences.Count; index++)
        {
            RowsCohortSequence<TIdentity, T> sequence =
                sequences[index]
                ?? throw new ArgumentNullException(
                    nameof(sequences),
                    $"Row-set sequence {index + 1} is null.");
            RowSequenceKey key =
                RowSequenceKey.Create(index);
            if (!keyByIdentity.TryAdd(
                    sequence.Identity,
                    key))
            {
                throw new ArgumentException(
                    "A row-set identity is duplicated.",
                    nameof(sequences));
            }

            identityByKey.Add(
                key,
                sequence.Identity);
            named[index] =
                NamedRowSequence<T>.Create(
                    key,
                    sequence.Values);
        }

        NamedRowSelectionResult<T> selected =
            RowSelectionExecutor.ApplyNamed(
                named,
                plan,
                comparerResolver);
        if (!selected.IsSuccess)
        {
            NamedRowWindowFailure failure =
                selected.Failure!;
            if (!identityByKey.TryGetValue(
                    failure.Key,
                    out TIdentity? identity))
            {
                throw UnknownKey(failure.Key);
            }

            return RowsCohortResult<TIdentity, T>.Failed(
                new RowsCohortSemanticFailure<TIdentity>(
                    identity,
                    failure.Failure));
        }

        var rowSets =
            new SelectedRowSet<TIdentity, T>[
                selected.Sequences.Count];
        for (int index = 0;
             index < selected.Sequences.Count;
             index++)
        {
            NamedRowSequence<T> sequence =
                selected.Sequences[index];
            if (!identityByKey.TryGetValue(
                    sequence.Key,
                    out TIdentity? identity))
            {
                throw UnknownKey(sequence.Key);
            }

            rowSets[index] =
                new SelectedRowSet<TIdentity, T>(
                    identity,
                    sequence.Values);
        }

        return RowsCohortResult<TIdentity, T>.Success(rowSets);
    }

    private static InvalidOperationException UnknownKey(
        RowSequenceKey key) =>
        new(
            $"Semantic row selection returned unknown sequence key {key.Value}.");
}
