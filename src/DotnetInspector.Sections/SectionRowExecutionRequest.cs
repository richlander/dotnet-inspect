using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Sections;

public sealed class SectionRowCohortDescriptor<TIdentity>
    where TIdentity : notnull
{
    internal SectionRowCohortDescriptor(
        ShapingCohortIdentity identity,
        RowIntentBindingIdentity intentBinding,
        SectionRowSchemaIdentity schema,
        IReadOnlyList<TIdentity> rowSets)
    {
        Identity = identity;
        IntentBinding = intentBinding;
        Schema = schema;
        RowSets = SectionContractSnapshot.Copy(rowSets);
    }

    public ShapingCohortIdentity Identity { get; }

    public RowIntentBindingIdentity IntentBinding { get; }

    public SectionRowSchemaIdentity Schema { get; }

    public IReadOnlyList<TIdentity> RowSets { get; }
}

public sealed class SectionRowExecutionRequest<TIdentity, TProjection>
    where TIdentity : notnull
{
    private readonly IReadOnlyDictionary<TIdentity, RowSequenceKey>
        _keysByIdentity;
    private readonly IReadOnlyDictionary<RowSequenceKey, TIdentity>
        _identitiesByKey;

    private SectionRowExecutionRequest(
        IReadOnlyList<
            SectionRowSetDeclaration<TIdentity, TProjection>> rowSets,
        SectionRowIntentAssociation<TIdentity> association,
        RowIntentBindingIdentity intentBinding,
        IReadOnlyList<SectionRowCohortDescriptor<TIdentity>>
            cohortDescriptors,
        IReadOnlyList<SectionRowCohort<TIdentity, TProjection>>
            cohorts,
        IReadOnlyDictionary<TIdentity, RowSequenceKey>
            keysByIdentity,
        IReadOnlyDictionary<RowSequenceKey, TIdentity>
            identitiesByKey)
    {
        RowSets = rowSets;
        Association = association;
        IntentBinding = intentBinding;
        Cohorts = cohortDescriptors;
        ExecutableCohorts = cohorts;
        _keysByIdentity = keysByIdentity;
        _identitiesByKey = identitiesByKey;
    }

    public IReadOnlyList<
        SectionRowSetDeclaration<TIdentity, TProjection>> RowSets
    { get; }

    public SectionRowIntentAssociation<TIdentity> Association
    { get; }

    public RowIntentBindingIdentity IntentBinding { get; }

    public IReadOnlyList<SectionRowCohortDescriptor<TIdentity>>
        Cohorts
    { get; }

    internal IReadOnlyList<
        SectionRowCohort<TIdentity, TProjection>> ExecutableCohorts
    { get; }

    public static SectionRowExecutionRequest<TIdentity, TProjection>
        Create(
            IReadOnlyList<
                SectionRowSetDeclaration<TIdentity, TProjection>>
                rowSets,
            SectionRowIntentAssociation<TIdentity> association)
    {
        ArgumentNullException.ThrowIfNull(rowSets);
        ArgumentNullException.ThrowIfNull(association);
        if (rowSets.Count == 0)
        {
            throw new ArgumentException(
                "A section-row execution request requires at least one "
                + "declared row set.",
                nameof(rowSets));
        }

        var rowSetCopy =
            new SectionRowSetDeclaration<
                TIdentity,
                TProjection>[rowSets.Count];
        var rowSetsByIdentity =
            new Dictionary<
                TIdentity,
                SectionRowSetDeclaration<TIdentity, TProjection>>();
        var keysByIdentity =
            new Dictionary<TIdentity, RowSequenceKey>();
        var identitiesByKey =
            new Dictionary<RowSequenceKey, TIdentity>();
        for (int index = 0; index < rowSets.Count; index++)
        {
            SectionRowSetDeclaration<TIdentity, TProjection>
                rowSet =
                    rowSets[index]
                    ?? throw new ArgumentNullException(
                        nameof(rowSets),
                        $"Declared row set {index + 1} is null.");
            if (!rowSetsByIdentity.TryAdd(
                    rowSet.Identity,
                    rowSet))
            {
                throw new ArgumentException(
                    "A declared row-set identity is duplicated.",
                    nameof(rowSets));
            }

            RowSequenceKey key = RowSequenceKey.Create(index);
            keysByIdentity.Add(rowSet.Identity, key);
            identitiesByKey.Add(key, rowSet.Identity);
            rowSetCopy[index] = rowSet;
        }

        var assigned = new HashSet<TIdentity>();
        for (int index = 0;
             index < association.RowSets.Count;
             index++)
        {
            TIdentity identity = association.RowSets[index];
            if (!rowSetsByIdentity.ContainsKey(identity))
            {
                throw new ArgumentException(
                    $"Row-intent association reference {index + 1} names "
                    + "an unknown declared row set.",
                    nameof(association));
            }
            if (!assigned.Add(identity))
            {
                throw new ArgumentException(
                    "A declared row set is associated more than once.",
                    nameof(association));
            }
        }

        foreach (SectionRowSetDeclaration<TIdentity, TProjection>
            rowSet in rowSetCopy)
        {
            if (!assigned.Contains(rowSet.Identity))
            {
                throw new ArgumentException(
                    "A participating declared row set has no row-intent "
                    + "association.",
                    nameof(association));
            }
        }

        var bindingsBySchema =
            new Dictionary<
                SectionRowSchemaIdentity,
                SectionRowSchemaBinding<TIdentity>>(
                    ReferenceEqualityComparer.Instance);
        foreach (SectionRowSchemaBinding<TIdentity> binding
            in association.SchemaBindings)
        {
            if (!bindingsBySchema.TryAdd(
                    binding.Schema,
                    binding))
            {
                throw new ArgumentException(
                    "A row schema has more than one execution binding.",
                    nameof(association));
            }
        }

        var declarationsBySchema =
            new Dictionary<
                SectionRowSchemaIdentity,
                List<
                    SectionRowSetDeclaration<
                        TIdentity,
                        TProjection>>>(
                            ReferenceEqualityComparer.Instance);
        var schemaOrder = new List<SectionRowSchemaIdentity>();
        foreach (SectionRowSetDeclaration<TIdentity, TProjection>
            rowSet in rowSetCopy)
        {
            if (!bindingsBySchema.TryGetValue(
                    rowSet.Schema,
                    out SectionRowSchemaBinding<TIdentity>? binding))
            {
                throw new ArgumentException(
                    "A participating row schema has no execution binding.",
                    nameof(association));
            }
            if (!rowSet.HasRows)
            {
                _ = rowSet.SourceCount;
                if (!binding.CanExecuteCountWithoutRows)
                {
                    throw new ArgumentException(
                        "A cardinality-only row-set declaration requires a "
                        + "Count execution binding.",
                        nameof(rowSets));
                }
            }

            if (!declarationsBySchema.TryGetValue(
                    rowSet.Schema,
                    out List<
                        SectionRowSetDeclaration<
                            TIdentity,
                            TProjection>>? declarations))
            {
                declarations = [];
                declarationsBySchema.Add(
                    rowSet.Schema,
                    declarations);
                schemaOrder.Add(rowSet.Schema);
            }
            declarations.Add(rowSet);
        }

        foreach (SectionRowSchemaIdentity schema
            in bindingsBySchema.Keys)
        {
            if (!declarationsBySchema.ContainsKey(schema))
            {
                throw new ArgumentException(
                    "A row-schema execution binding is not used by any "
                    + "participating declared row set.",
                    nameof(association));
            }
        }

        var intentBinding = new RowIntentBindingIdentity();
        var cohorts =
            new SectionRowCohort<
                TIdentity,
                TProjection>[schemaOrder.Count];
        var descriptors =
            new SectionRowCohortDescriptor<TIdentity>[
                schemaOrder.Count];
        for (int index = 0; index < schemaOrder.Count; index++)
        {
            SectionRowSchemaIdentity schema =
                schemaOrder[index];
            SectionRowCohort<TIdentity, TProjection> cohort =
                bindingsBySchema[schema].CreateCohort(
                    new ShapingCohortIdentity(),
                    intentBinding,
                    declarationsBySchema[schema],
                    keysByIdentity);
            cohorts[index] = cohort;
            descriptors[index] = cohort.Descriptor;
        }

        return new(
            SectionContractSnapshot.Own(rowSetCopy),
            association,
            intentBinding,
            SectionContractSnapshot.Own(descriptors),
            SectionContractSnapshot.Own(cohorts),
            keysByIdentity,
            identitiesByKey);
    }

    internal SectionRowExecutionRequest<TIdentity, TProjection>?
        CreateSubset(
            IReadOnlySet<TIdentity> includedRowSets)
    {
        ArgumentNullException.ThrowIfNull(includedRowSets);
        if (includedRowSets.Count == 0)
        {
            return null;
        }

        var rowSets =
            new List<
                SectionRowSetDeclaration<TIdentity, TProjection>>();
        var schemas =
            new HashSet<SectionRowSchemaIdentity>(
                ReferenceEqualityComparer.Instance);
        var keysByIdentity =
            new Dictionary<TIdentity, RowSequenceKey>();
        var identitiesByKey =
            new Dictionary<RowSequenceKey, TIdentity>();
        foreach (SectionRowSetDeclaration<TIdentity, TProjection>
            rowSet in RowSets)
        {
            if (!includedRowSets.Contains(rowSet.Identity))
            {
                continue;
            }

            rowSets.Add(rowSet);
            schemas.Add(rowSet.Schema);
            RowSequenceKey key =
                _keysByIdentity[rowSet.Identity];
            keysByIdentity.Add(rowSet.Identity, key);
            identitiesByKey.Add(key, rowSet.Identity);
        }

        if (rowSets.Count != includedRowSets.Count)
        {
            throw new ArgumentException(
                "A section-row subset names an unknown declared row set.",
                nameof(includedRowSets));
        }

        var cohorts =
            new List<
                SectionRowCohort<TIdentity, TProjection>>();
        foreach (SectionRowCohort<TIdentity, TProjection> cohort
            in ExecutableCohorts)
        {
            SectionRowCohort<TIdentity, TProjection>? subset =
                cohort.CreateSubset(includedRowSets);
            if (subset is not null)
            {
                cohorts.Add(subset);
            }
        }

        var association =
            new SectionRowIntentAssociation<TIdentity>(
                Association.RowSets
                    .Where(includedRowSets.Contains)
                    .ToArray(),
                Association.SchemaBindings
                    .Where(binding => schemas.Contains(binding.Schema))
                    .ToArray());
        return new(
            SectionContractSnapshot.Copy(rowSets),
            association,
            IntentBinding,
            SectionContractSnapshot.Copy(
                cohorts.Select(static cohort => cohort.Descriptor)
                    .ToArray()),
            SectionContractSnapshot.Copy(cohorts),
            keysByIdentity,
            identitiesByKey);
    }

    public bool TryGetSequenceKey(
        TIdentity identity,
        [NotNullWhen(true)] out RowSequenceKey? key)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return _keysByIdentity.TryGetValue(identity, out key);
    }

    public bool TryGetRowSetIdentity(
        RowSequenceKey key,
        [MaybeNullWhen(false)] out TIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _identitiesByKey.TryGetValue(key, out identity);
    }
}
