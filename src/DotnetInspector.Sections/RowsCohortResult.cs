using DotnetInspector.RowSelection;

namespace DotnetInspector.Sections;

public sealed class SelectedRowSet<TIdentity, T>
    where TIdentity : notnull
{
    internal SelectedRowSet(
        TIdentity identity,
        IReadOnlyList<T> values)
    {
        Identity = identity;
        Values = values;
    }

    public TIdentity Identity { get; }

    public IReadOnlyList<T> Values { get; }
}

public sealed class RowsCohortSemanticFailure<TIdentity>
    where TIdentity : notnull
{
    internal RowsCohortSemanticFailure(
        TIdentity identity,
        RowWindowFailure failure)
    {
        Identity = identity;
        Failure = failure;
    }

    public TIdentity Identity { get; }

    public RowWindowFailure Failure { get; }
}

public sealed class RowsCohortResult<TIdentity, T>
    where TIdentity : notnull
{
    private RowsCohortResult(
        bool isSuccess,
        IReadOnlyList<SelectedRowSet<TIdentity, T>> rowSets,
        RowsCohortSemanticFailure<TIdentity>? failure)
    {
        IsSuccess = isSuccess;
        RowSets = rowSets;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public IReadOnlyList<SelectedRowSet<TIdentity, T>> RowSets { get; }

    public RowsCohortSemanticFailure<TIdentity>? Failure { get; }

    internal static RowsCohortResult<TIdentity, T> Success(
        SelectedRowSet<TIdentity, T>[] rowSets) =>
        new(
            true,
            RowsCohortSnapshot.Own(rowSets),
            null);

    internal static RowsCohortResult<TIdentity, T> Failed(
        RowsCohortSemanticFailure<TIdentity> failure) =>
        new(
            false,
            RowsCohortSnapshot.Empty<SelectedRowSet<TIdentity, T>>(),
            failure);
}

internal static class RowsCohortSnapshot
{
    public static IReadOnlyList<T> Empty<T>() =>
        Array.AsReadOnly(Array.Empty<T>());

    public static IReadOnlyList<T> Copy<T>(
        IReadOnlyList<T> values)
    {
        var copy = new T[values.Count];
        for (int index = 0; index < values.Count; index++)
            copy[index] = values[index];
        return Own(copy);
    }

    public static IReadOnlyList<T> Own<T>(T[] values) =>
        Array.AsReadOnly(values);
}
