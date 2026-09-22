using QuerySpace.Rows;

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

internal sealed class CountedRowSet<TIdentity>
    where TIdentity : notnull
{
    public CountedRowSet(
        TIdentity identity,
        int count)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Identity = identity;
        Count = count;
    }

    public TIdentity Identity { get; }

    public int Count { get; }
}

internal sealed class RowsCohortCountResult<TIdentity>
    where TIdentity : notnull
{
    private RowsCohortCountResult(
        IReadOnlyList<CountedRowSet<TIdentity>> rowSets,
        RowsCohortSemanticFailure<TIdentity>? failure)
    {
        RowSets = rowSets;
        Failure = failure;
    }

    public bool IsSuccess => Failure is null;

    public IReadOnlyList<CountedRowSet<TIdentity>> RowSets
    { get; }

    public RowsCohortSemanticFailure<TIdentity>? Failure { get; }

    public static RowsCohortCountResult<TIdentity> Success(
        CountedRowSet<TIdentity>[] rowSets) =>
        new(
            SectionContractSnapshot.Own(rowSets),
            null);

    public static RowsCohortCountResult<TIdentity> Failed(
        RowsCohortSemanticFailure<TIdentity> failure) =>
        new(
            SectionContractSnapshot.Empty<
                CountedRowSet<TIdentity>>(),
            failure);
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
            SectionContractSnapshot.Own(rowSets),
            null);

    internal static RowsCohortResult<TIdentity, T> Failed(
        RowsCohortSemanticFailure<TIdentity> failure) =>
        new(
            false,
            SectionContractSnapshot.Empty<SelectedRowSet<TIdentity, T>>(),
            failure);
}
