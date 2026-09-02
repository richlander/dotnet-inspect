using DotnetInspector.RowSelection;

namespace DotnetInspector.Sections;

public static class RowsCohortExecutor
{
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
        var bindings =
            new Binding<TIdentity>[sequences.Count];
        var identities = new HashSet<TIdentity>();
        var identityByKey =
            new Dictionary<RowSequenceKey, TIdentity>();

        for (int index = 0; index < sequences.Count; index++)
        {
            RowsCohortSequence<TIdentity, T> sequence =
                sequences[index]
                ?? throw new ArgumentNullException(
                    nameof(sequences),
                    $"Row-set sequence {index + 1} is null.");
            if (!identities.Add(sequence.Identity))
            {
                throw new ArgumentException(
                    "A row-set identity is duplicated.",
                    nameof(sequences));
            }

            RowSequenceKey key =
                RowSequenceKey.Create(index);
            bindings[index] =
                new Binding<TIdentity>(
                    key,
                    sequence.Identity);
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
                selected.Failure
                ?? throw InvalidSemanticResult();
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

        if (selected.Failure is not null)
            throw InvalidSemanticResult();

        var selectedByKey =
            new Dictionary<RowSequenceKey, NamedRowSequence<T>>();
        for (int index = 0;
             index < selected.Sequences.Count;
             index++)
        {
            NamedRowSequence<T> sequence =
                selected.Sequences[index];
            if (!identityByKey.ContainsKey(sequence.Key))
                throw UnknownKey(sequence.Key);
            if (!selectedByKey.TryAdd(
                    sequence.Key,
                    sequence))
            {
                throw new InvalidOperationException(
                    "Semantic row selection returned a duplicate sequence key.");
            }
        }

        if (selectedByKey.Count != bindings.Length)
        {
            throw new InvalidOperationException(
                "Semantic row selection returned an incomplete sequence set.");
        }

        var rowSets =
            new SelectedRowSet<TIdentity, T>[bindings.Length];
        for (int index = 0; index < bindings.Length; index++)
        {
            Binding<TIdentity> binding =
                bindings[index];
            if (!selectedByKey.TryGetValue(
                    binding.Key,
                    out NamedRowSequence<T>? sequence))
            {
                throw new InvalidOperationException(
                    "Semantic row selection omitted a bound sequence key.");
            }

            rowSets[index] =
                new SelectedRowSet<TIdentity, T>(
                    binding.Identity,
                    sequence.Values);
        }

        return RowsCohortResult<TIdentity, T>.Success(rowSets);
    }

    private static InvalidOperationException UnknownKey(
        RowSequenceKey key) =>
        new(
            $"Semantic row selection returned unknown sequence key {key.Value}.");

    private static InvalidOperationException InvalidSemanticResult() =>
        new(
            "Semantic row selection returned an invalid result branch.");

    private readonly record struct Binding<TIdentity>(
        RowSequenceKey Key,
        TIdentity Identity)
        where TIdentity : notnull;
}
