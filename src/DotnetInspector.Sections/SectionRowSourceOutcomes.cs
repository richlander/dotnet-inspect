namespace DotnetInspector.Sections;

public sealed class SectionRowSourceEvidence<
    TDisposition,
    TCompletionEvidence>
    where TDisposition : notnull
    where TCompletionEvidence : notnull
{
    public SectionRowSourceEvidence(
        TDisposition disposition,
        TCompletionEvidence completion)
    {
        ArgumentNullException.ThrowIfNull(disposition);
        ArgumentNullException.ThrowIfNull(completion);
        Disposition = disposition;
        Completion = completion;
    }

    public TDisposition Disposition { get; }

    public TCompletionEvidence Completion { get; }
}

public sealed class SectionRowSourceState<
    TIdentity,
    TDisposition,
    TCompletionEvidence>
    where TIdentity : notnull
    where TDisposition : notnull
    where TCompletionEvidence : notnull
{
    public SectionRowSourceState(
        TIdentity identity,
        SectionRowSourceEvidence<
            TDisposition,
            TCompletionEvidence> evidence,
        bool rowsAreUsable,
        bool countIsSufficient)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(evidence);
        if (countIsSufficient && !rowsAreUsable)
        {
            throw new ArgumentException(
                "A source cannot satisfy residual Count without supplying "
                + "usable rows.",
                nameof(countIsSufficient));
        }

        Identity = identity;
        Evidence = evidence;
        RowsAreUsable = rowsAreUsable;
        CountIsSufficient = countIsSufficient;
    }

    public TIdentity Identity { get; }

    public SectionRowSourceEvidence<
        TDisposition,
        TCompletionEvidence> Evidence
    { get; }

    public bool RowsAreUsable { get; }

    public bool CountIsSufficient { get; }
}

public sealed class SectionSourceRowSetResult<
    TIdentity,
    TProjection,
    TDisposition,
    TCompletionEvidence>
    where TIdentity : notnull
    where TDisposition : notnull
    where TCompletionEvidence : notnull
{
    internal SectionSourceRowSetResult(
        SectionRowSourceState<
            TIdentity,
            TDisposition,
            TCompletionEvidence> source,
        SectionRowSetResult<TIdentity, TProjection>? selectedRows)
    {
        Source = source;
        SelectedRows = selectedRows;
    }

    public TIdentity Identity => Source.Identity;

    public SectionRowSourceState<
        TIdentity,
        TDisposition,
        TCompletionEvidence> Source
    { get; }

    public SectionRowSetResult<TIdentity, TProjection>? SelectedRows
    { get; }

    public bool RowsAreAvailable => SelectedRows is not null;

    internal TProjection Rebind(TProjection projection) =>
        SelectedRows is null
            ? projection
            : SelectedRows.Rebind(projection);
}

public sealed class SectionSourceRowsOutcome<
    TIdentity,
    TProjection,
    TDisposition,
    TCompletionEvidence>
    where TIdentity : notnull
    where TDisposition : notnull
    where TCompletionEvidence : notnull
{
    private SectionSourceRowsOutcome(
        bool isSuccess,
        IReadOnlyList<
            SectionSourceRowSetResult<
                TIdentity,
                TProjection,
                TDisposition,
                TCompletionEvidence>> rowSets,
        RowsCohortSemanticFailure<TIdentity>? failure)
    {
        IsSuccess = isSuccess;
        RowSets = rowSets;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public IReadOnlyList<
        SectionSourceRowSetResult<
            TIdentity,
            TProjection,
            TDisposition,
            TCompletionEvidence>> RowSets
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
        foreach (SectionSourceRowSetResult<
            TIdentity,
            TProjection,
            TDisposition,
            TCompletionEvidence> rowSet in RowSets)
        {
            current = rowSet.Rebind(current);
        }
        return current;
    }

    internal static SectionSourceRowsOutcome<
        TIdentity,
        TProjection,
        TDisposition,
        TCompletionEvidence> Success(
            SectionSourceRowSetResult<
                TIdentity,
                TProjection,
                TDisposition,
                TCompletionEvidence>[] rowSets) =>
        new(
            true,
            SectionContractSnapshot.Own(rowSets),
            null);

    internal static SectionSourceRowsOutcome<
        TIdentity,
        TProjection,
        TDisposition,
        TCompletionEvidence> Failed(
            RowsCohortSemanticFailure<TIdentity> failure) =>
        new(
            false,
            SectionContractSnapshot.Empty<
                SectionSourceRowSetResult<
                    TIdentity,
                    TProjection,
                    TDisposition,
                    TCompletionEvidence>>(),
            failure);
}
