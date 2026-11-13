namespace QuerySpace.Rows;

/// <summary>
/// The Exists answer of a selection evaluated from source cardinality alone,
/// or the strict-window failure that the same selection would report.
/// </summary>
public readonly struct RowSelectionExistsResult
{
    private readonly bool _hasResult;

    private RowSelectionExistsResult(
        bool exists,
        RowWindowFailure? failure)
    {
        _hasResult = true;
        Exists = exists;
        Failure = failure;
    }

    public bool IsSuccess => _hasResult && Failure is null;

    /// <summary>Whether the selection yields at least one row.</summary>
    public bool Exists { get; }

    public RowWindowFailure? Failure { get; }

    internal static RowSelectionExistsResult Success(bool exists) =>
        new(exists, null);

    internal static RowSelectionExistsResult Failed(
        RowWindowFailure failure) =>
        new(false, failure);
}

/// <summary>
/// Exists from cardinality: when a plan carries no residual predicate, whether
/// its selection yields a row depends only on the source count and the
/// selection stages, so the answer is read without reading a row. Order never
/// changes whether a row exists, so a ranking stage is admitted here where
/// Count rejects it.
/// </summary>
public static class RowSelectionExistsExecutor
{
    public static bool TryApply<TOrder>(
        int sourceCount,
        RowSelectionPlan<TOrder> plan,
        out RowSelectionExistsResult result)
        where TOrder : notnull
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceCount);
        ArgumentNullException.ThrowIfNull(plan);

        if (RowSelectionCountExecutor.TryReduce(
                sourceCount,
                plan,
                rankingSelectsCardinality: true,
                out int count,
                out RowWindowFailure? failure))
        {
            result = RowSelectionExistsResult.Success(count > 0);
        }
        else
        {
            result = RowSelectionExistsResult.Failed(failure!);
        }

        return true;
    }
}
