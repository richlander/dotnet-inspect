namespace DotnetInspector.Sections;

public abstract class SectionRowSetResult<TIdentity, TProjection>
    where TIdentity : notnull
{
    private protected SectionRowSetResult(TIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Identity = identity;
    }

    public TIdentity Identity { get; }

    internal abstract int Count { get; }

    internal abstract TProjection Rebind(TProjection projection);
}

public sealed class SectionRowSetResult<
    TIdentity,
    TProjection,
    TRow> :
    SectionRowSetResult<TIdentity, TProjection>
    where TIdentity : notnull
{
    private readonly Func<
        TProjection,
        IReadOnlyList<TRow>,
        TProjection> _resultBinder;

    internal SectionRowSetResult(
        TIdentity identity,
        IReadOnlyList<TRow> rows,
        Func<
            TProjection,
            IReadOnlyList<TRow>,
            TProjection> resultBinder)
        : base(identity)
    {
        Rows = SectionContractSnapshot.Copy(rows);
        _resultBinder = resultBinder;
    }

    public IReadOnlyList<TRow> Rows { get; }

    internal override int Count => Rows.Count;

    internal override TProjection Rebind(TProjection projection) =>
        _resultBinder(projection, Rows);
}

public sealed class SectionRowsOutcome<TIdentity, TProjection>
    where TIdentity : notnull
{
    private SectionRowsOutcome(
        bool isSuccess,
        IReadOnlyList<
            SectionRowSetResult<TIdentity, TProjection>> rowSets,
        RowsCohortSemanticFailure<TIdentity>? failure)
    {
        IsSuccess = isSuccess;
        RowSets = rowSets;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public IReadOnlyList<
        SectionRowSetResult<TIdentity, TProjection>> RowSets
    { get; }

    public RowsCohortSemanticFailure<TIdentity>? Failure { get; }

    public TProjection Rebind(TProjection projection)
    {
        if (!IsSuccess)
        {
            throw new InvalidOperationException(
                "A failed section-row result cannot be rebound.");
        }

        TProjection current = projection;
        foreach (SectionRowSetResult<TIdentity, TProjection>
            rowSet in RowSets)
        {
            current = rowSet.Rebind(current);
        }
        return current;
    }

    internal static SectionRowsOutcome<TIdentity, TProjection>
        Success(
            SectionRowSetResult<TIdentity, TProjection>[] rowSets) =>
        new(
            true,
            SectionContractSnapshot.Own(rowSets),
            null);

    internal static SectionRowsOutcome<TIdentity, TProjection>
        Failed(
            RowsCohortSemanticFailure<TIdentity> failure) =>
        new(
            false,
            SectionContractSnapshot.Empty<
                SectionRowSetResult<TIdentity, TProjection>>(),
            failure);
}
