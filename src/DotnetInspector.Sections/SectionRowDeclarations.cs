namespace DotnetInspector.Sections;

public abstract class SectionRowSetDeclaration<TIdentity, TProjection>
    where TIdentity : notnull
{
    private protected SectionRowSetDeclaration(
        TIdentity identity,
        SectionRowSchemaIdentity schema)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(schema);
        Identity = identity;
        Schema = schema;
    }

    public TIdentity Identity { get; }

    public SectionRowSchemaIdentity Schema { get; }

    internal abstract int SourceCount { get; }

    internal abstract bool HasRows { get; }

    internal abstract SectionRowSetDeclaration<TIdentity, TProjection>
        ResolveSnapshot(bool countOnly);

    internal abstract SectionRowSetDeclaration<TIdentity, TProjection>
        ResolveExactCountSnapshot(int exactCount);
}

public sealed class SectionRowSetDeclaration<
    TIdentity,
    TProjection,
    TRow> :
    SectionRowSetDeclaration<TIdentity, TProjection>
    where TIdentity : notnull
{
    private readonly IReadOnlyList<TRow>? _rows;
    private readonly IReadOnlyList<TRow>? _sourceRows;
    private readonly Func<
        TProjection,
        IReadOnlyList<TRow>,
        TProjection>? _resultBinder;
    private readonly int? _sourceCount;

    public SectionRowSetDeclaration(
        TIdentity identity,
        SectionRowSchemaIdentity<TRow> schema,
        IReadOnlyList<TRow> rows,
        Func<
            TProjection,
            IReadOnlyList<TRow>,
            TProjection> resultBinder)
        : base(identity, schema)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(resultBinder);
        TypedSchema = schema;
        _rows = SectionContractSnapshot.Copy(rows);
        _sourceCount = _rows.Count;
        _resultBinder = resultBinder;
    }

    private SectionRowSetDeclaration(
        TIdentity identity,
        SectionRowSchemaIdentity<TRow> schema,
        Func<
            TProjection,
            IReadOnlyList<TRow>,
            TProjection> resultBinder,
        IReadOnlyList<TRow> sourceRows)
        : base(identity, schema)
    {
        ArgumentNullException.ThrowIfNull(sourceRows);
        ArgumentNullException.ThrowIfNull(resultBinder);
        TypedSchema = schema;
        _sourceRows = sourceRows;
        _resultBinder = resultBinder;
    }

    private SectionRowSetDeclaration(
        TIdentity identity,
        SectionRowSchemaIdentity<TRow> schema,
        int sourceCount)
        : base(identity, schema)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceCount);
        TypedSchema = schema;
        _sourceCount = sourceCount;
    }

    public SectionRowSchemaIdentity<TRow> TypedSchema { get; }

    public IReadOnlyList<TRow> Rows =>
        _rows
        ?? throw new InvalidOperationException(
            "A cardinality-only row-set declaration has no row snapshot.");

    internal override int SourceCount =>
        _sourceCount
        ?? throw new InvalidOperationException(
            "A deferred row-set declaration has not captured its source.");

    internal override bool HasRows => _rows is not null;

    internal static SectionRowSetDeclaration<
        TIdentity,
        TProjection,
        TRow> CreateDeferred(
            TIdentity identity,
            SectionRowSchemaIdentity<TRow> schema,
            IReadOnlyList<TRow> sourceRows,
            Func<
                TProjection,
                IReadOnlyList<TRow>,
                TProjection> resultBinder) =>
        new(
            identity,
            schema,
            resultBinder,
            sourceRows);

    internal override SectionRowSetDeclaration<TIdentity, TProjection>
        ResolveSnapshot(bool countOnly)
    {
        if (_sourceRows is null)
            return this;

        return countOnly
            ? new SectionRowSetDeclaration<
                TIdentity,
                TProjection,
                TRow>(
                    Identity,
                    TypedSchema,
                    _sourceRows.Count)
            : new SectionRowSetDeclaration<
                TIdentity,
                TProjection,
                TRow>(
                    Identity,
                    TypedSchema,
                    _sourceRows,
                    _resultBinder!);
    }

    internal override SectionRowSetDeclaration<TIdentity, TProjection>
        ResolveExactCountSnapshot(int exactCount) =>
        new SectionRowSetDeclaration<
            TIdentity,
            TProjection,
            TRow>(
                Identity,
                TypedSchema,
                exactCount);

    internal SectionRowSetResult<
        TIdentity,
        TProjection,
        TRow> BindResult(
        IReadOnlyList<TRow> rows) =>
        new(
            Identity,
            rows,
            _resultBinder
                ?? throw new InvalidOperationException(
                    "A cardinality-only row-set declaration cannot bind "
                    + "Rows."));
}

public abstract class SectionRowSchemaBinding<TIdentity>
    where TIdentity : notnull
{
    private protected SectionRowSchemaBinding(
        SectionRowSchemaIdentity schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        Schema = schema;
    }

    public SectionRowSchemaIdentity Schema { get; }

    internal abstract SectionRowCohort<TIdentity, TProjection>
        CreateCohort<TProjection>(
            ShapingCohortIdentity identity,
            RowIntentBindingIdentity intentBinding,
            IReadOnlyList<
                SectionRowSetDeclaration<TIdentity, TProjection>> rowSets,
            IReadOnlyDictionary<TIdentity, RowSequenceKey> keys);

    internal abstract bool CanExecuteCountWithoutRows { get; }
}

public sealed class SectionRowSchemaBinding<TIdentity, TRow> :
    SectionRowSchemaBinding<TIdentity>
    where TIdentity : notnull
{
    private readonly Func<
        IReadOnlyList<RowsCohortSequence<TIdentity, TRow>>,
        RowsCohortResult<TIdentity, TRow>> _executor;
    private readonly Func<
        IReadOnlyList<RowsCohortCardinality<TIdentity>>,
        RowsCohortCountResult<TIdentity>>? _countExecutor;

    public SectionRowSchemaBinding(
        SectionRowSchemaIdentity<TRow> schema,
        Func<
            IReadOnlyList<RowsCohortSequence<TIdentity, TRow>>,
            RowsCohortResult<TIdentity, TRow>> executor)
        : this(
            schema,
            executor,
            null)
    {
    }

    internal SectionRowSchemaBinding(
        SectionRowSchemaIdentity<TRow> schema,
        Func<
            IReadOnlyList<RowsCohortSequence<TIdentity, TRow>>,
            RowsCohortResult<TIdentity, TRow>> executor,
        Func<
            IReadOnlyList<RowsCohortCardinality<TIdentity>>,
            RowsCohortCountResult<TIdentity>>? countExecutor)
        : base(schema)
    {
        ArgumentNullException.ThrowIfNull(executor);
        TypedSchema = schema;
        _executor = executor;
        _countExecutor = countExecutor;
    }

    public SectionRowSchemaIdentity<TRow> TypedSchema { get; }

    internal override bool CanExecuteCountWithoutRows =>
        _countExecutor is not null;

    internal override SectionRowCohort<TIdentity, TProjection>
        CreateCohort<TProjection>(
            ShapingCohortIdentity identity,
            RowIntentBindingIdentity intentBinding,
            IReadOnlyList<
                SectionRowSetDeclaration<TIdentity, TProjection>> rowSets,
            IReadOnlyDictionary<TIdentity, RowSequenceKey> keys)
    {
        var typed =
            new SectionRowSetDeclaration<
                TIdentity,
                TProjection,
                TRow>[rowSets.Count];
        for (int index = 0; index < rowSets.Count; index++)
        {
            if (rowSets[index] is not SectionRowSetDeclaration<
                    TIdentity,
                    TProjection,
                    TRow> rowSet
                || !ReferenceEquals(
                    rowSet.TypedSchema,
                    TypedSchema))
            {
                throw new InvalidOperationException(
                    "A section-row schema binding received an incompatible "
                    + "row-set declaration.");
            }
            typed[index] = rowSet;
        }

        return new TypedSectionRowCohort<
            TIdentity,
            TProjection,
            TRow>(
                identity,
                intentBinding,
                TypedSchema,
                typed,
                keys,
                _executor,
                _countExecutor);
    }
}

public sealed class SectionRowIntentAssociation<TIdentity>
    where TIdentity : notnull
{
    public SectionRowIntentAssociation(
        IReadOnlyList<TIdentity> rowSets,
        IReadOnlyList<SectionRowSchemaBinding<TIdentity>>
            schemaBindings)
    {
        ArgumentNullException.ThrowIfNull(rowSets);
        ArgumentNullException.ThrowIfNull(schemaBindings);
        if (rowSets.Count == 0)
        {
            throw new ArgumentException(
                "A section-row intent association requires at least one "
                + "row-set identity.",
                nameof(rowSets));
        }
        if (schemaBindings.Count == 0)
        {
            throw new ArgumentException(
                "A section-row intent association requires at least one "
                + "schema binding.",
                nameof(schemaBindings));
        }

        var rowSetCopy = new TIdentity[rowSets.Count];
        for (int index = 0; index < rowSets.Count; index++)
        {
            TIdentity identity = rowSets[index];
            ArgumentNullException.ThrowIfNull(identity);
            rowSetCopy[index] = identity;
        }

        var bindingCopy =
            new SectionRowSchemaBinding<TIdentity>[
                schemaBindings.Count];
        for (int index = 0;
             index < schemaBindings.Count;
             index++)
        {
            bindingCopy[index] =
                schemaBindings[index]
                ?? throw new ArgumentNullException(
                    nameof(schemaBindings),
                    $"Schema binding {index + 1} is null.");
        }

        RowSets = SectionContractSnapshot.Own(rowSetCopy);
        SchemaBindings = SectionContractSnapshot.Own(bindingCopy);
    }

    public IReadOnlyList<TIdentity> RowSets { get; }

    public IReadOnlyList<SectionRowSchemaBinding<TIdentity>>
        SchemaBindings
    { get; }
}
