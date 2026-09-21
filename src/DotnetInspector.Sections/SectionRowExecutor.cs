namespace DotnetInspector.Sections;

public static class SectionRowExecutor
{
    public static SectionRowsOutcome<TIdentity, TProjection> ApplyRows<
        TIdentity,
        TProjection>(
        SectionRowExecutionRequest<TIdentity, TProjection> request)
        where TIdentity : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        SectionRowExecution<TIdentity, TProjection> execution =
            Execute(request);
        return execution.Failure is not null
            ? SectionRowsOutcome<TIdentity, TProjection>.Failed(
                execution.Failure)
            : SectionRowsOutcome<TIdentity, TProjection>.Success(
                execution.RowSets);
    }

    public static SectionCountOutcome<TIdentity, TEvidence> ApplyCount<
        TIdentity,
        TProjection,
        TEvidence>(
        SectionRowExecutionRequest<TIdentity, TProjection> request)
        where TIdentity : notnull
        where TEvidence : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        SectionRowExecution<TIdentity, TProjection> execution =
            Execute(request);
        if (execution.Failure is not null)
        {
            RowWindowFailure failure =
                execution.Failure.Failure;
            return new SectionCountOutcome<
                TIdentity,
                TEvidence>.Semantic(
                    execution.Failure.Identity,
                    failure.StageNumber,
                    failure.RequiredPosition,
                    failure.AvailableCount);
        }

        var counts =
            new SectionCountEntry<TIdentity>[
                execution.RowSets.Length];
        for (int index = 0;
             index < execution.RowSets.Length;
             index++)
        {
            SectionRowSetResult<TIdentity, TProjection> rowSet =
                execution.RowSets[index];
            counts[index] =
                new(rowSet.Identity, rowSet.Count);
        }
        return new SectionCountOutcome<
            TIdentity,
            TEvidence>.Completed(counts);
    }

    private static SectionRowExecution<TIdentity, TProjection>
        Execute<TIdentity, TProjection>(
            SectionRowExecutionRequest<TIdentity, TProjection>
                request)
        where TIdentity : notnull
    {
        var outcomesByIdentity =
            new Dictionary<
                TIdentity,
                SectionRowSetResult<TIdentity, TProjection>>();
        foreach (SectionRowCohort<TIdentity, TProjection> cohort
            in request.ExecutableCohorts)
        {
            SectionRowCohortExecution<TIdentity, TProjection>
                execution =
                    cohort.Execute();
            if (execution.Failure is not null)
            {
                return SectionRowExecution<
                    TIdentity,
                    TProjection>.Failed(
                        execution.Failure);
            }

            foreach (SectionRowSetResult<TIdentity, TProjection>
                rowSet in execution.RowSets)
            {
                if (!outcomesByIdentity.TryAdd(
                        rowSet.Identity,
                        rowSet))
                {
                    throw new InvalidOperationException(
                        "Section-row execution returned a declared row set "
                        + "more than once.");
                }
            }
        }

        var ordered =
            new SectionRowSetResult<
                TIdentity,
                TProjection>[request.RowSets.Count];
        for (int index = 0;
             index < request.RowSets.Count;
             index++)
        {
            TIdentity identity =
                request.RowSets[index].Identity;
            if (!outcomesByIdentity.TryGetValue(
                    identity,
                    out SectionRowSetResult<
                        TIdentity,
                        TProjection>? outcome))
            {
                throw new InvalidOperationException(
                    "Section-row execution omitted a participating "
                    + "declared row set.");
            }
            ordered[index] = outcome;
        }

        return SectionRowExecution<
            TIdentity,
            TProjection>.Success(ordered);
    }
}

internal abstract class SectionRowCohort<TIdentity, TProjection>
    where TIdentity : notnull
{
    protected SectionRowCohort(
        SectionRowCohortDescriptor<TIdentity> descriptor)
    {
        Descriptor = descriptor;
    }

    public SectionRowCohortDescriptor<TIdentity> Descriptor
    { get; }

    public abstract SectionRowCohortExecution<
        TIdentity,
        TProjection> Execute();
}

internal sealed class TypedSectionRowCohort<
    TIdentity,
    TProjection,
    TRow> :
    SectionRowCohort<TIdentity, TProjection>
    where TIdentity : notnull
{
    private readonly IReadOnlyList<
        SectionRowSetDeclaration<
            TIdentity,
            TProjection,
            TRow>> _rowSets;
    private readonly IReadOnlyDictionary<TIdentity, RowSequenceKey>
        _keys;
    private readonly Func<
        IReadOnlyList<RowsCohortSequence<TIdentity, TRow>>,
        RowsCohortResult<TIdentity, TRow>> _executor;

    public TypedSectionRowCohort(
        ShapingCohortIdentity identity,
        RowIntentBindingIdentity intentBinding,
        SectionRowSchemaIdentity<TRow> schema,
        IReadOnlyList<
            SectionRowSetDeclaration<
                TIdentity,
                TProjection,
                TRow>> rowSets,
        IReadOnlyDictionary<TIdentity, RowSequenceKey> keys,
        Func<
            IReadOnlyList<RowsCohortSequence<TIdentity, TRow>>,
            RowsCohortResult<TIdentity, TRow>> executor)
        : base(
            new(
                identity,
                intentBinding,
                schema,
                rowSets
                    .Select(static rowSet => rowSet.Identity)
                    .ToArray()))
    {
        _rowSets = rowSets;
        _keys = keys;
        _executor = executor;
    }

    public override SectionRowCohortExecution<
        TIdentity,
        TProjection> Execute()
    {
        var sequences =
            new RowsCohortSequence<
                TIdentity,
                TRow>[_rowSets.Count];
        for (int index = 0; index < _rowSets.Count; index++)
        {
            SectionRowSetDeclaration<
                TIdentity,
                TProjection,
                TRow> rowSet =
                    _rowSets[index];
            sequences[index] =
                RowsCohortSequence<TIdentity, TRow>
                    .CreateBoundFromDeclaration(
                        rowSet,
                        _keys[rowSet.Identity]);
        }

        RowsCohortResult<TIdentity, TRow> selected =
            _executor(sequences)
            ?? throw new InvalidOperationException(
                "A section-row schema binding returned no result.");
        if (!selected.IsSuccess)
        {
            RowsCohortSemanticFailure<TIdentity> failure =
                selected.Failure!;
            if (!_rowSets.Any(
                    rowSet => EqualityComparer<TIdentity>.Default.Equals(
                        rowSet.Identity,
                        failure.Identity)))
            {
                throw new InvalidOperationException(
                    "Section-row execution returned a semantic failure "
                    + "for an unknown row-set identity.");
            }
            return SectionRowCohortExecution<
                TIdentity,
                TProjection>.Failed(failure);
        }

        var selectedByIdentity =
            new Dictionary<TIdentity, IReadOnlyList<TRow>>();
        foreach (SelectedRowSet<TIdentity, TRow> rowSet
            in selected.RowSets)
        {
            if (!_rowSets.Any(
                    declaration =>
                        EqualityComparer<TIdentity>.Default.Equals(
                            declaration.Identity,
                            rowSet.Identity)))
            {
                throw new InvalidOperationException(
                    "Section-row execution returned an unknown row-set "
                    + "identity.");
            }
            if (!selectedByIdentity.TryAdd(
                    rowSet.Identity,
                    rowSet.Values))
            {
                throw new InvalidOperationException(
                    "Section-row execution returned a row-set identity "
                    + "more than once.");
            }
        }

        var outcomes =
            new SectionRowSetResult<
                TIdentity,
                TProjection>[_rowSets.Count];
        for (int index = 0; index < _rowSets.Count; index++)
        {
            SectionRowSetDeclaration<
                TIdentity,
                TProjection,
                TRow> declaration =
                    _rowSets[index];
            if (!selectedByIdentity.TryGetValue(
                    declaration.Identity,
                    out IReadOnlyList<TRow>? rows))
            {
                throw new InvalidOperationException(
                    "Section-row execution omitted a cohort row set.");
            }
            outcomes[index] =
                declaration.BindResult(rows);
        }

        return SectionRowCohortExecution<
            TIdentity,
            TProjection>.Success(outcomes);
    }
}

internal sealed class SectionRowCohortExecution<
    TIdentity,
    TProjection>
    where TIdentity : notnull
{
    private SectionRowCohortExecution(
        SectionRowSetResult<
            TIdentity,
            TProjection>[] rowSets,
        RowsCohortSemanticFailure<TIdentity>? failure)
    {
        RowSets = rowSets;
        Failure = failure;
    }

    public SectionRowSetResult<
        TIdentity,
        TProjection>[] RowSets
    { get; }

    public RowsCohortSemanticFailure<TIdentity>? Failure { get; }

    public static SectionRowCohortExecution<
        TIdentity,
        TProjection> Success(
            SectionRowSetResult<
                TIdentity,
                TProjection>[] rowSets) =>
        new(rowSets, null);

    public static SectionRowCohortExecution<
        TIdentity,
        TProjection> Failed(
            RowsCohortSemanticFailure<TIdentity> failure) =>
        new([], failure);
}

internal sealed class SectionRowExecution<TIdentity, TProjection>
    where TIdentity : notnull
{
    private SectionRowExecution(
        SectionRowSetResult<
            TIdentity,
            TProjection>[] rowSets,
        RowsCohortSemanticFailure<TIdentity>? failure)
    {
        RowSets = rowSets;
        Failure = failure;
    }

    public SectionRowSetResult<
        TIdentity,
        TProjection>[] RowSets
    { get; }

    public RowsCohortSemanticFailure<TIdentity>? Failure { get; }

    public static SectionRowExecution<
        TIdentity,
        TProjection> Success(
            SectionRowSetResult<
                TIdentity,
                TProjection>[] rowSets) =>
        new(rowSets, null);

    public static SectionRowExecution<
        TIdentity,
        TProjection> Failed(
            RowsCohortSemanticFailure<TIdentity> failure) =>
        new([], failure);
}
