using QuerySpace.Rows;

namespace DotnetInspector.Sections;

public static class RowsCohortExecutor
{
    internal static RowsCohortCountResult<TIdentity>
        ApplyResolvedCount<TIdentity, T>(
            IReadOnlyList<RowsCohortCardinality<TIdentity>> rowSets,
            ResolvedRowQueryPlan<T> plan)
        where TIdentity : notnull
    {
        ArgumentNullException.ThrowIfNull(rowSets);
        ArgumentNullException.ThrowIfNull(plan);
        if (rowSets.Count == 0)
        {
            throw new ArgumentException(
                "A rows cohort must contain at least one row set.",
                nameof(rowSets));
        }

        var counts =
            new CountedRowSet<TIdentity>[rowSets.Count];
        var identities = new HashSet<TIdentity>();
        var keys = new HashSet<RowSequenceKey>();
        for (int index = 0; index < rowSets.Count; index++)
        {
            RowsCohortCardinality<TIdentity> rowSet =
                rowSets[index]
                ?? throw new ArgumentNullException(
                    nameof(rowSets),
                    $"Row set {index + 1} is null.");
            if (!identities.Add(rowSet.Identity))
            {
                throw new ArgumentException(
                    "A row-set identity is duplicated.",
                    nameof(rowSets));
            }
            if (!keys.Add(rowSet.Key))
            {
                throw new ArgumentException(
                    "A row-sequence key is duplicated.",
                    nameof(rowSets));
            }

            if (!RowQueryExecutor.TryApplyCount(
                    rowSet.Count,
                    plan,
                    out RowSelectionCountResult selected))
            {
                throw new InvalidOperationException(
                    "An admitted row-query Count plan declined during "
                    + "execution.");
            }
            if (!selected.IsSuccess)
            {
                return RowsCohortCountResult<TIdentity>.Failed(
                    new RowsCohortSemanticFailure<TIdentity>(
                        rowSet.Identity,
                        selected.Failure!));
            }
            counts[index] =
                new(
                    rowSet.Identity,
                    selected.Count);
        }

        return RowsCohortCountResult<TIdentity>.Success(counts);
    }

    public static RowsCohortResult<TIdentity, T> ApplyResolved<
        TIdentity,
        T>(
        IReadOnlyList<RowsCohortSequence<TIdentity, T>> sequences,
        ResolvedRowQueryPlan<T> plan)
        where TIdentity : notnull
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
        var identityByKey =
            new Dictionary<RowSequenceKey, TIdentity>();
        var identities = new HashSet<TIdentity>();
        for (int index = 0; index < sequences.Count; index++)
        {
            RowsCohortSequence<TIdentity, T> sequence =
                sequences[index]
                ?? throw new ArgumentNullException(
                    nameof(sequences),
                    $"Row-set sequence {index + 1} is null.");
            RowSequenceKey key =
                sequence.Key
                ?? RowSequenceKey.Create(index);
            if (!identities.Add(sequence.Identity))
            {
                throw new ArgumentException(
                    "A row-set identity is duplicated.",
                    nameof(sequences));
            }
            if (!identityByKey.TryAdd(
                    key,
                    sequence.Identity))
            {
                throw new ArgumentException(
                    "A row-sequence key is duplicated.",
                    nameof(sequences));
            }

            named[index] =
                NamedRowSequence<T>.Create(
                    key,
                    sequence.Values);
        }

        NamedRowSelectionResult<T> selected =
            RowQueryExecutor.ApplyNamed(
                named,
                plan);
        return Rebind(
            selected,
            identityByKey);
    }

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
                sequence.Key
                ?? RowSequenceKey.Create(index);
            if (!keyByIdentity.TryAdd(
                    sequence.Identity,
                    key))
            {
                throw new ArgumentException(
                    "A row-set identity is duplicated.",
                    nameof(sequences));
            }

            if (!identityByKey.TryAdd(
                    key,
                    sequence.Identity))
            {
                throw new ArgumentException(
                    "A row-sequence key is duplicated.",
                    nameof(sequences));
            }
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
        return Rebind(
            selected,
            identityByKey);
    }

    private static RowsCohortResult<TIdentity, T> Rebind<
        TIdentity,
        T>(
        NamedRowSelectionResult<T> selected,
        IReadOnlyDictionary<RowSequenceKey, TIdentity>
            identityByKey)
        where TIdentity : notnull
    {
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
