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
}

public sealed class SectionRowSetDeclaration<
    TIdentity,
    TProjection,
    TRow> :
    SectionRowSetDeclaration<TIdentity, TProjection>
    where TIdentity : notnull
{
    private readonly Func<
        TProjection,
        IReadOnlyList<TRow>,
        TProjection> _resultBinder;

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
        Rows = SectionContractSnapshot.Copy(rows);
        _resultBinder = resultBinder;
    }

    public SectionRowSchemaIdentity<TRow> TypedSchema { get; }

    public IReadOnlyList<TRow> Rows { get; }

    internal SectionRowSetResult<
        TIdentity,
        TProjection,
        TRow> BindResult(
        IReadOnlyList<TRow> rows) =>
        new(
            Identity,
            rows,
            _resultBinder);
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
}

public sealed class SectionRowSchemaBinding<TIdentity, TRow> :
    SectionRowSchemaBinding<TIdentity>
    where TIdentity : notnull
{
    private readonly Func<
        IReadOnlyList<RowsCohortSequence<TIdentity, TRow>>,
        RowsCohortResult<TIdentity, TRow>> _executor;

    public SectionRowSchemaBinding(
        SectionRowSchemaIdentity<TRow> schema,
        Func<
            IReadOnlyList<RowsCohortSequence<TIdentity, TRow>>,
            RowsCohortResult<TIdentity, TRow>> executor)
        : base(schema)
    {
        ArgumentNullException.ThrowIfNull(executor);
        TypedSchema = schema;
        _executor = executor;
    }

    public SectionRowSchemaIdentity<TRow> TypedSchema { get; }

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
                _executor);
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
