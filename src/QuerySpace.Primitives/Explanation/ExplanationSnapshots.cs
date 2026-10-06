using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Serialization;

namespace QuerySpace.Explanation;

/// <summary>The heterogeneous graph join key of one explanation resource.</summary>
public sealed class ExplanationResourceKey :
    IEquatable<ExplanationResourceKey>
{
    public ExplanationResourceKey(
        ExplanationOwnerIdentity owner,
        ExplanationResourceTypeIdentity resourceType,
        ExplanationValue identityValue)
    {
        ExplanationContract.ValidateIdentity(owner.Value, nameof(owner));
        ExplanationContract.ValidateIdentity(
            resourceType.Value,
            nameof(resourceType));
        if (resourceType.Schema.Owner != owner)
        {
            throw new ArgumentException(
                "The resource-key owner must issue its resource type.",
                nameof(owner));
        }

        Owner = owner;
        ResourceType = resourceType;
        IdentityValue = identityValue
            ?? throw new ArgumentNullException(nameof(identityValue));
    }

    public ExplanationOwnerIdentity Owner { get; }

    public ExplanationResourceTypeIdentity ResourceType { get; }

    public ExplanationValue IdentityValue { get; }

    public bool Equals(ExplanationResourceKey? other) =>
        ReferenceEquals(this, other)
        || (other is not null
            && Owner == other.Owner
            && ResourceType == other.ResourceType
            && IdentityValue.Equals(other.IdentityValue));

    public override bool Equals(object? obj) =>
        Equals(obj as ExplanationResourceKey);

    public override int GetHashCode() =>
        HashCode.Combine(Owner, ResourceType, IdentityValue);

    public static bool operator ==(
        ExplanationResourceKey? left,
        ExplanationResourceKey? right) =>
        Equals(left, right);

    public static bool operator !=(
        ExplanationResourceKey? left,
        ExplanationResourceKey? right) =>
        !Equals(left, right);

    public override string ToString() =>
        $"{Owner}:{ResourceType.Value}:{IdentityValue.GetHashCode():x8}";
}

/// <summary>One typed public navigation address of a resource.</summary>
public sealed record ExplanationPublicAddress
{
    public ExplanationPublicAddress(
        ExplanationPublicAddressKindIdentity kind,
        ExplanationValue value)
    {
        ExplanationContract.ValidateIdentity(kind.Value, nameof(kind));
        Kind = kind;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public ExplanationPublicAddressKindIdentity Kind { get; }

    public ExplanationValue Value { get; }
}

/// <summary>One observed fact and its exact outcome.</summary>
public sealed record ExplanationFactObservation
{
    public ExplanationFactObservation(
        ExplanationFactIdentity fact,
        ExplanationObservationState state,
        IEnumerable<ExplanationValue>? values = null,
        ExplanationValue? outcomeData = null)
        : this(fact, state, [.. values ?? []], outcomeData)
    {
    }

    [JsonConstructor]
    public ExplanationFactObservation(
        ExplanationFactIdentity fact,
        ExplanationObservationState state,
        ImmutableArray<ExplanationValue> values,
        ExplanationValue? outcomeData)
    {
        ExplanationContract.ValidateIdentity(fact.Value, nameof(fact));
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        Fact = fact;
        State = state;
        Values = values.IsDefault ? [] : values;
        OutcomeData = outcomeData;
        if (Values.Any(static value => value is null))
        {
            throw new ArgumentException(
                "Fact observations must not contain null values.",
                nameof(values));
        }
        ValidatePayload(state, Values.Length, outcomeData, nameof(values));
    }

    public ExplanationFactIdentity Fact { get; }

    public ExplanationObservationState State { get; }

    public ImmutableArray<ExplanationValue> Values { get; }

    public ExplanationValue? OutcomeData { get; }

    internal static void ValidatePayload(
        ExplanationObservationState state,
        int availableCount,
        ExplanationValue? outcomeData,
        string parameterName)
    {
        bool valid = state switch
        {
            ExplanationObservationState.Available =>
                outcomeData is null,
            ExplanationObservationState.Absent =>
                availableCount == 0 && outcomeData is null,
            ExplanationObservationState.Unavailable
                or ExplanationObservationState.Failed =>
                availableCount == 0 && outcomeData is not null,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "Available observations carry only available content; "
                + "absent observations carry none; unavailable and failed "
                + "observations carry only typed outcome data.",
                parameterName);
        }
    }
}

/// <summary>One typed target in a relationship observation.</summary>
public sealed record ExplanationRelationshipTarget
{
    public ExplanationRelationshipTarget(ExplanationResourceKey resource)
    {
        Resource = resource
            ?? throw new ArgumentNullException(nameof(resource));
    }

    public ExplanationResourceKey Resource { get; }
}

/// <summary>One observed relationship and its exact outcome.</summary>
public sealed record ExplanationRelationshipObservation
{
    public ExplanationRelationshipObservation(
        ExplanationRelationshipIdentity relationship,
        ExplanationObservationState state,
        IEnumerable<ExplanationRelationshipTarget>? targets = null,
        ExplanationValue? outcomeData = null)
        : this(relationship, state, [.. targets ?? []], outcomeData)
    {
    }

    [JsonConstructor]
    public ExplanationRelationshipObservation(
        ExplanationRelationshipIdentity relationship,
        ExplanationObservationState state,
        ImmutableArray<ExplanationRelationshipTarget> targets,
        ExplanationValue? outcomeData)
    {
        ExplanationContract.ValidateIdentity(
            relationship.Value,
            nameof(relationship));
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state));
        Relationship = relationship;
        State = state;
        Targets = targets.IsDefault ? [] : targets;
        OutcomeData = outcomeData;
        if (Targets.Any(static target => target is null))
        {
            throw new ArgumentException(
                "Relationship observations must not contain null targets.",
                nameof(targets));
        }
        ExplanationFactObservation.ValidatePayload(
            state,
            Targets.Length,
            outcomeData,
            nameof(targets));
    }

    public ExplanationRelationshipIdentity Relationship { get; }

    public ExplanationObservationState State { get; }

    public ImmutableArray<ExplanationRelationshipTarget> Targets { get; }

    public ExplanationValue? OutcomeData { get; }
}

/// <summary>One eager detached snapshot of a typed explanation resource.</summary>
public sealed record ExplanationResourceSnapshot
{
    public ExplanationResourceSnapshot(
        ExplanationResourceKey key,
        ExplanationSchemaVersion schemaVersion,
        ExplanationSnapshotScope scope,
        IEnumerable<ExplanationPublicAddress>? addresses,
        IEnumerable<ExplanationFactObservation> facts,
        IEnumerable<ExplanationRelationshipObservation> relationships)
        : this(
            key,
            schemaVersion,
            scope,
            [.. addresses ?? []],
            [
                .. facts
                    ?? throw new ArgumentNullException(nameof(facts)),
            ],
            [
                .. relationships
                    ?? throw new ArgumentNullException(nameof(relationships)),
            ])
    {
    }

    [JsonConstructor]
    public ExplanationResourceSnapshot(
        ExplanationResourceKey key,
        ExplanationSchemaVersion schemaVersion,
        ExplanationSnapshotScope scope,
        ImmutableArray<ExplanationPublicAddress> addresses,
        ImmutableArray<ExplanationFactObservation> facts,
        ImmutableArray<ExplanationRelationshipObservation> relationships)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));
        SchemaVersion = schemaVersion;
        Scope = scope;
        Addresses = addresses.IsDefault ? [] : addresses;
        Facts = facts.IsDefault ? [] : facts;
        Relationships = relationships.IsDefault ? [] : relationships;
        if (Addresses.Any(static address => address is null)
            || Addresses.Select(static address => address.Kind)
                .Distinct()
                .Count()
                != Addresses.Length
            || Facts.Any(static fact => fact is null)
            || Facts.Select(static fact => fact.Fact).Distinct().Count()
                != Facts.Length
            || Facts.Any(fact =>
                fact.Fact.ResourceType != key.ResourceType)
            || Relationships.Any(static relationship =>
                relationship is null)
            || Relationships.Select(static relationship =>
                    relationship.Relationship)
                .Distinct()
                .Count()
                != Relationships.Length
            || Relationships.Any(relationship =>
                relationship.Relationship.ResourceType
                    != key.ResourceType))
        {
            throw new ArgumentException(
                "Snapshot address kinds and observations must be non-null, "
                + "unique, and scoped to the snapshot resource type.");
        }
    }

    public ExplanationResourceKey Key { get; }

    public ExplanationSchemaVersion SchemaVersion { get; }

    public ExplanationSnapshotScope Scope { get; }

    public ImmutableArray<ExplanationPublicAddress> Addresses { get; }

    public ImmutableArray<ExplanationFactObservation> Facts { get; }

    public ImmutableArray<ExplanationRelationshipObservation> Relationships
    {
        get;
    }
}

/// <summary>
/// Pure validation and finite measurement over explicitly supplied schemas.
/// </summary>
public static class ExplanationConformance
{
    public static ExplanationValueMeasurement ValidateValue(
        IEnumerable<ExplanationSchema> schemas,
        ExplanationDataShapeIdentity shape,
        ExplanationValue value)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(value);
        SchemaIndex index = SchemaIndex.Create(schemas);
        return ValidateValue(
            index,
            index.GetShape(shape),
            value,
            []);
    }

    public static void ValidateSnapshot(
        IEnumerable<ExplanationSchema> schemas,
        ExplanationResourceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(snapshot);
        SchemaIndex index = SchemaIndex.Create(schemas);
        ValidateSnapshot(index, snapshot);
    }

    public static void ValidateSnapshots(
        IEnumerable<ExplanationSchema> schemas,
        IEnumerable<ExplanationResourceSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(snapshots);
        SchemaIndex index = SchemaIndex.Create(schemas);
        foreach (ExplanationResourceSnapshot snapshot in snapshots)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            ValidateSnapshot(index, snapshot);
        }
    }

    private static void ValidateSnapshot(
        SchemaIndex index,
        ExplanationResourceSnapshot snapshot)
    {
        ExplanationSchema schema = index.GetSchema(
            snapshot.Key.ResourceType.Schema,
            snapshot.SchemaVersion);
        ExplanationResourceTypeDeclaration resource =
            schema.ResourceTypes.FirstOrDefault(candidate =>
                candidate.Identity == snapshot.Key.ResourceType)
            ?? throw new InvalidOperationException(
                $"Schema '{schema.Identity}' does not declare resource type "
                + $"'{snapshot.Key.ResourceType}'.");

        ValidateResourceProjection(
            index,
            resource,
            snapshot.Key,
            snapshot.Addresses,
            snapshot.Facts);

        if (snapshot.Relationships.Length
            != resource.Relationships.Length)
        {
            throw new InvalidOperationException(
                $"Resource '{snapshot.Key}' must observe every declared "
                + "relationship exactly once.");
        }

        for (int indexOfRelationship = 0;
             indexOfRelationship < resource.Relationships.Length;
             indexOfRelationship++)
        {
            ExplanationRelationshipDeclaration declaration =
                resource.Relationships[indexOfRelationship];
            ExplanationRelationshipObservation observation =
                snapshot.Relationships[indexOfRelationship];
            if (observation.Relationship != declaration.Identity)
            {
                throw new InvalidOperationException(
                    $"Resource '{snapshot.Key}' relationship observations "
                    + "must use declaration order.");
            }
            ValidateRelationship(index, declaration, observation);
        }
    }

    public static void ValidateResourceProjection(
        IEnumerable<ExplanationSchema> schemas,
        ExplanationResourceKey key,
        ExplanationSchemaVersion schemaVersion,
        IEnumerable<ExplanationPublicAddress> addresses,
        IEnumerable<ExplanationFactObservation> facts)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(facts);
        SchemaIndex index = SchemaIndex.Create(schemas);
        ExplanationSchema schema = index.GetSchema(
            key.ResourceType.Schema,
            schemaVersion);
        ExplanationResourceTypeDeclaration resource =
            schema.ResourceTypes.FirstOrDefault(candidate =>
                candidate.Identity == key.ResourceType)
            ?? throw new InvalidOperationException(
                $"Schema '{schema.Identity}' does not declare resource type "
                + $"'{key.ResourceType}'.");
        ValidateResourceProjection(
            index,
            resource,
            key,
            [.. addresses],
            [.. facts]);
    }

    public static ExplanationResourceSnapshot CreateSnapshot(
        IEnumerable<ExplanationSchema> schemas,
        ExplanationResourceKey key,
        ExplanationSchemaVersion schemaVersion,
        ExplanationSnapshotScope scope,
        IEnumerable<ExplanationPublicAddress>? addresses,
        IEnumerable<ExplanationFactObservation> facts,
        IEnumerable<ExplanationRelationshipObservation> relationships)
    {
        var snapshot = new ExplanationResourceSnapshot(
            key,
            schemaVersion,
            scope,
            addresses,
            facts,
            relationships);
        ValidateSnapshot(schemas, snapshot);
        return snapshot;
    }

    private static void ValidateResourceProjection(
        SchemaIndex index,
        ExplanationResourceTypeDeclaration resource,
        ExplanationResourceKey key,
        ImmutableArray<ExplanationPublicAddress> addresses,
        ImmutableArray<ExplanationFactObservation> facts)
    {
        ValidateValue(
            index,
            index.GetShape(resource.IdentityShape),
            key.IdentityValue,
            []);

        if (addresses.Any(address =>
                !resource.AddressKinds.Contains(address.Kind)))
        {
            throw new InvalidOperationException(
                $"Resource '{key}' carries an undeclared public address "
                + "kind.");
        }
        foreach (ExplanationPublicAddress address in addresses)
        {
            ExplanationPublicAddressKindDeclaration declaration =
                index.GetAddressKind(address.Kind);
            ValidateValue(
                index,
                index.GetShape(declaration.ValueShape),
                address.Value,
                []);
        }

        if (facts.Length != resource.Facts.Length)
        {
            throw new InvalidOperationException(
                $"Resource '{key}' must observe every declared fact exactly "
                + "once.");
        }
        for (int indexOfFact = 0;
             indexOfFact < resource.Facts.Length;
             indexOfFact++)
        {
            ExplanationFactDeclaration declaration =
                resource.Facts[indexOfFact];
            ExplanationFactObservation observation = facts[indexOfFact];
            if (observation.Fact != declaration.Identity)
            {
                throw new InvalidOperationException(
                    $"Resource '{key}' fact observations must use "
                    + "declaration order.");
            }
            ValidateFact(index, declaration, observation);
        }
    }

    private static void ValidateFact(
        SchemaIndex index,
        ExplanationFactDeclaration declaration,
        ExplanationFactObservation observation)
    {
        ValidateState(declaration.AdmittedStates, observation.State);
        switch (observation.State)
        {
            case ExplanationObservationState.Available:
                ValidateAvailableCardinality(
                    declaration.Cardinality,
                    declaration.MaximumValueCount,
                    observation.Values.Length);
                foreach (ExplanationValue value in observation.Values)
                {
                    ValidateValue(
                        index,
                        index.GetShape(declaration.ValueShape),
                        value,
                        []);
                }
                break;
            case ExplanationObservationState.Unavailable:
                ValidateValue(
                    index,
                    index.GetShape(
                        declaration.UnavailableDataShape!.Value),
                    observation.OutcomeData!,
                    []);
                break;
            case ExplanationObservationState.Failed:
                ValidateValue(
                    index,
                    index.GetShape(declaration.FailureDataShape!.Value),
                    observation.OutcomeData!,
                    []);
                break;
        }
    }

    private static void ValidateRelationship(
        SchemaIndex index,
        ExplanationRelationshipDeclaration declaration,
        ExplanationRelationshipObservation observation)
    {
        ValidateState(declaration.AdmittedStates, observation.State);
        switch (observation.State)
        {
            case ExplanationObservationState.Available:
                ValidateAvailableCardinality(
                    declaration.Cardinality,
                    maximumValueCount: null,
                    observation.Targets.Length);
                foreach (ExplanationRelationshipTarget target
                         in observation.Targets)
                {
                    if (target.Resource.ResourceType
                        != declaration.TargetResourceType)
                    {
                        throw new InvalidOperationException(
                            $"Relationship '{declaration.Identity}' targets "
                            + $"resource type '{target.Resource.ResourceType}' "
                            + $"instead of "
                            + $"'{declaration.TargetResourceType}'.");
                    }
                    ExplanationResourceTypeDeclaration targetType =
                        index.GetResourceType(
                            target.Resource.ResourceType);
                    ValidateValue(
                        index,
                        index.GetShape(targetType.IdentityShape),
                        target.Resource.IdentityValue,
                        []);
                }
                break;
            case ExplanationObservationState.Unavailable:
                ValidateValue(
                    index,
                    index.GetShape(
                        declaration.UnavailableDataShape!.Value),
                    observation.OutcomeData!,
                    []);
                break;
            case ExplanationObservationState.Failed:
                ValidateValue(
                    index,
                    index.GetShape(declaration.FailureDataShape!.Value),
                    observation.OutcomeData!,
                    []);
                break;
        }
    }

    private static ExplanationValueMeasurement ValidateValue(
        SchemaIndex index,
        ExplanationDataShapeDeclaration shape,
        ExplanationValue value,
        HashSet<ExplanationDataShapeIdentity> referencePath)
    {
        ExplanationValueMeasurement measurement = Measure(value);
        if (measurement.CanonicalByteCount
                > shape.Budget.MaximumCanonicalByteCount
            || measurement.Depth > shape.Budget.MaximumDepth
            || measurement.NodeCount > shape.Budget.MaximumNodeCount)
        {
            throw new InvalidOperationException(
                $"Value for shape '{shape.Identity}' exceeds its finite "
                + "canonical-byte, depth, or node budget.");
        }

        switch (shape)
        {
            case ExplanationDataShapeDeclaration.Scalar scalar
                when value is ExplanationValue.Scalar scalarValue
                    && scalarValue.Value.Kind == scalar.ScalarKind:
                return measurement;

            case ExplanationDataShapeDeclaration.VocabularyTerm term
                when value is ExplanationValue.VocabularyTerm termValue
                    && termValue.Identity.Vocabulary == term.Vocabulary:
                return measurement;

            case ExplanationDataShapeDeclaration.Record record
                when value is ExplanationValue.Record recordValue:
                ValidateRecord(index, record, recordValue);
                return measurement;

            case ExplanationDataShapeDeclaration.Choice choice
                when value is ExplanationValue.Choice choiceValue:
                ValidateChoice(index, choice, choiceValue);
                return measurement;

            case ExplanationDataShapeDeclaration.Reference reference:
                if (!referencePath.Add(reference.Identity))
                {
                    throw new InvalidOperationException(
                        $"Shape '{reference.Identity}' participates in a "
                        + "reference-only cycle.");
                }
                try
                {
                    return ValidateValue(
                        index,
                        index.GetShape(reference.Target),
                        value,
                        referencePath);
                }
                finally
                {
                    referencePath.Remove(reference.Identity);
                }

            default:
                throw new InvalidOperationException(
                    $"Value does not conform to shape '{shape.Identity}'.");
        }
    }

    private static void ValidateRecord(
        SchemaIndex index,
        ExplanationDataShapeDeclaration.Record declaration,
        ExplanationValue.Record value)
    {
        if (value.Fields.Length != declaration.Fields.Length)
        {
            throw new InvalidOperationException(
                $"Record value for '{declaration.Identity}' does not observe "
                + "every declared field.");
        }

        for (int fieldIndex = 0;
             fieldIndex < declaration.Fields.Length;
             fieldIndex++)
        {
            ExplanationRecordFieldDeclaration field =
                declaration.Fields[fieldIndex];
            ExplanationRecordFieldValue fieldValue =
                value.Fields[fieldIndex];
            if (fieldValue.Field != field.Identity)
            {
                throw new InvalidOperationException(
                    $"Record value for '{declaration.Identity}' must use "
                    + "declaration field order.");
            }
            ValidateCardinality(
                field.Cardinality,
                field.MaximumValueCount,
                fieldValue.Values.Length);
            foreach (ExplanationValue nested in fieldValue.Values)
            {
                ValidateValue(
                    index,
                    index.GetShape(field.ValueShape),
                    nested,
                    []);
            }
        }
    }

    private static void ValidateChoice(
        SchemaIndex index,
        ExplanationDataShapeDeclaration.Choice declaration,
        ExplanationValue.Choice value)
    {
        ExplanationChoiceCaseDeclaration @case =
            declaration.Cases.FirstOrDefault(candidate =>
                candidate.Identity == value.Case)
            ?? throw new InvalidOperationException(
                $"Choice value names undeclared case '{value.Case}'.");
        if ((@case.ValueShape is null) != (value.Value is null))
        {
            throw new InvalidOperationException(
                $"Choice case '{@case.Identity}' payload does not match its "
                + "declaration.");
        }
        if (@case.ValueShape is { } shape)
        {
            ValidateValue(
                index,
                index.GetShape(shape),
                value.Value!,
                []);
        }
    }

    private static ExplanationValueMeasurement Measure(
        ExplanationValue value)
    {
        return value switch
        {
            ExplanationValue.Scalar scalar =>
                new(
                    scalar.Value.CanonicalByteCount(),
                    1,
                    1),
            ExplanationValue.VocabularyTerm term =>
                new(
                    1 + Encoding.UTF8.GetByteCount(
                        term.Identity.ToString()),
                    1,
                    1),
            ExplanationValue.Record record =>
                MeasureRecord(record),
            ExplanationValue.Choice choice =>
                MeasureChoice(choice),
            _ => throw new InvalidOperationException(
                "Unknown explanation value kind."),
        };
    }

    private static ExplanationValueMeasurement MeasureRecord(
        ExplanationValue.Record record)
    {
        int bytes = 1;
        int depth = 1;
        int nodes = 1;
        foreach (ExplanationRecordFieldValue field in record.Fields)
        {
            bytes += 1 + Encoding.UTF8.GetByteCount(
                field.Field.ToString());
            foreach (ExplanationValue nested in field.Values)
            {
                ExplanationValueMeasurement measurement = Measure(nested);
                bytes = checked(bytes + measurement.CanonicalByteCount);
                depth = Math.Max(depth, checked(1 + measurement.Depth));
                nodes = checked(nodes + measurement.NodeCount);
            }
        }
        return new(bytes, depth, nodes);
    }

    private static ExplanationValueMeasurement MeasureChoice(
        ExplanationValue.Choice choice)
    {
        int bytes =
            1 + Encoding.UTF8.GetByteCount(choice.Case.ToString());
        if (choice.Value is null)
            return new(bytes, 1, 1);

        ExplanationValueMeasurement nested = Measure(choice.Value);
        return new(
            checked(bytes + nested.CanonicalByteCount),
            checked(1 + nested.Depth),
            checked(1 + nested.NodeCount));
    }

    private static void ValidateState(
        ExplanationObservationStates admitted,
        ExplanationObservationState state)
    {
        ExplanationObservationStates flag = state switch
        {
            ExplanationObservationState.Available =>
                ExplanationObservationStates.Available,
            ExplanationObservationState.Absent =>
                ExplanationObservationStates.Absent,
            ExplanationObservationState.Unavailable =>
                ExplanationObservationStates.Unavailable,
            ExplanationObservationState.Failed =>
                ExplanationObservationStates.Failed,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };
        if (!admitted.HasFlag(flag))
        {
            throw new InvalidOperationException(
                $"Observation state '{state}' is not admitted by its "
                + "declaration.");
        }
    }

    private static void ValidateCardinality(
        ExplanationCardinality cardinality,
        int? maximumValueCount,
        int count)
    {
        bool valid = cardinality switch
        {
            ExplanationCardinality.RequiredOne => count == 1,
            ExplanationCardinality.OptionalOne => count is 0 or 1,
            ExplanationCardinality.OrderedMany =>
                maximumValueCount is null || count <= maximumValueCount,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidOperationException(
                $"Observed count '{count}' does not conform to "
                + $"cardinality '{cardinality}'.");
        }
    }

    private static void ValidateAvailableCardinality(
        ExplanationCardinality cardinality,
        int? maximumValueCount,
        int count)
    {
        ValidateCardinality(cardinality, maximumValueCount, count);
        if (cardinality == ExplanationCardinality.OptionalOne
            && count == 0)
        {
            throw new InvalidOperationException(
                "An available optional-one observation must carry one "
                + "value or target; use Absent when none exists.");
        }
    }

    private sealed class SchemaIndex
    {
        private readonly IReadOnlyDictionary<
            (ExplanationSchemaIdentity, ExplanationSchemaVersion),
            ExplanationSchema> _schemas;
        private readonly IReadOnlyDictionary<
            ExplanationDataShapeIdentity,
            ExplanationDataShapeDeclaration> _shapes;
        private readonly IReadOnlyDictionary<
            ExplanationResourceTypeIdentity,
            ExplanationResourceTypeDeclaration> _resourceTypes;
        private readonly IReadOnlyDictionary<
            ExplanationPublicAddressKindIdentity,
            ExplanationPublicAddressKindDeclaration> _addressKinds;

        private SchemaIndex(
            IReadOnlyDictionary<
                (ExplanationSchemaIdentity, ExplanationSchemaVersion),
                ExplanationSchema> schemas,
            IReadOnlyDictionary<
                ExplanationDataShapeIdentity,
                ExplanationDataShapeDeclaration> shapes,
            IReadOnlyDictionary<
                ExplanationResourceTypeIdentity,
                ExplanationResourceTypeDeclaration> resourceTypes,
            IReadOnlyDictionary<
                ExplanationPublicAddressKindIdentity,
                ExplanationPublicAddressKindDeclaration> addressKinds)
        {
            _schemas = schemas;
            _shapes = shapes;
            _resourceTypes = resourceTypes;
            _addressKinds = addressKinds;
        }

        public static SchemaIndex Create(
            IEnumerable<ExplanationSchema> schemas)
        {
            ExplanationSchema[] schemaArray =
            [
                .. schemas
                    ?? throw new ArgumentNullException(nameof(schemas)),
            ];
            if (schemaArray.Length == 0
                || schemaArray.Any(static schema => schema is null))
            {
                throw new ArgumentException(
                    "A non-empty schema closure is required.",
                    nameof(schemas));
            }

            var schemasByIdentity =
                new Dictionary<
                    (ExplanationSchemaIdentity, ExplanationSchemaVersion),
                    ExplanationSchema>();
            var shapes =
                new Dictionary<
                    ExplanationDataShapeIdentity,
                    ExplanationDataShapeDeclaration>();
            var resourceTypes =
                new Dictionary<
                    ExplanationResourceTypeIdentity,
                    ExplanationResourceTypeDeclaration>();
            var addressKinds =
                new Dictionary<
                    ExplanationPublicAddressKindIdentity,
                    ExplanationPublicAddressKindDeclaration>();
            foreach (ExplanationSchema schema in schemaArray)
            {
                if (!schemasByIdentity.TryAdd(
                        (schema.Identity, schema.Version),
                        schema))
                {
                    throw new ArgumentException(
                        $"Schema '{schema.Identity}' version "
                        + $"'{schema.Version}' occurs more than once.",
                        nameof(schemas));
                }
                foreach (ExplanationDataShapeDeclaration shape
                         in schema.DataShapes)
                {
                    if (!shapes.TryAdd(shape.Identity, shape))
                    {
                        throw new ArgumentException(
                            $"Shape '{shape.Identity}' occurs in more than "
                            + "one schema revision in the supplied closure.",
                            nameof(schemas));
                    }
                }
                foreach (ExplanationResourceTypeDeclaration resource
                         in schema.ResourceTypes)
                {
                    if (!resourceTypes.TryAdd(
                            resource.Identity,
                            resource))
                    {
                        throw new ArgumentException(
                            $"Resource type '{resource.Identity}' occurs in "
                            + "more than one schema revision in the supplied "
                            + "closure.",
                            nameof(schemas));
                    }
                }
                foreach (ExplanationPublicAddressKindDeclaration address
                         in schema.AddressKinds)
                {
                    if (!addressKinds.TryAdd(address.Identity, address))
                    {
                        throw new ArgumentException(
                            $"Address kind '{address.Identity}' occurs in "
                            + "more than one schema revision in the supplied "
                            + "closure.",
                            nameof(schemas));
                    }
                }
            }

            ValidateReferences(
                schemaArray,
                shapes,
                resourceTypes,
                addressKinds);
            return new(
                schemasByIdentity,
                shapes,
                resourceTypes,
                addressKinds);
        }

        public ExplanationSchema GetSchema(
            ExplanationSchemaIdentity identity,
            ExplanationSchemaVersion version) =>
            _schemas.TryGetValue((identity, version), out var schema)
                ? schema
                : throw new InvalidOperationException(
                    $"Schema closure does not contain '{identity}' version "
                    + $"'{version}'.");

        public ExplanationDataShapeDeclaration GetShape(
            ExplanationDataShapeIdentity identity) =>
            _shapes.TryGetValue(identity, out var shape)
                ? shape
                : throw new InvalidOperationException(
                    $"Schema closure does not declare shape '{identity}'.");

        public ExplanationResourceTypeDeclaration GetResourceType(
            ExplanationResourceTypeIdentity identity) =>
            _resourceTypes.TryGetValue(identity, out var resource)
                ? resource
                : throw new InvalidOperationException(
                    $"Schema closure does not declare resource type "
                    + $"'{identity}'.");

        public ExplanationPublicAddressKindDeclaration GetAddressKind(
            ExplanationPublicAddressKindIdentity identity) =>
            _addressKinds.TryGetValue(identity, out var address)
                ? address
                : throw new InvalidOperationException(
                    $"Schema closure does not declare address kind "
                    + $"'{identity}'.");

        private static void ValidateReferences(
            IEnumerable<ExplanationSchema> schemas,
            IReadOnlyDictionary<
                ExplanationDataShapeIdentity,
                ExplanationDataShapeDeclaration> shapes,
            IReadOnlyDictionary<
                ExplanationResourceTypeIdentity,
                ExplanationResourceTypeDeclaration> resourceTypes,
            IReadOnlyDictionary<
                ExplanationPublicAddressKindIdentity,
                ExplanationPublicAddressKindDeclaration> addressKinds)
        {
            foreach (ExplanationSchema schema in schemas)
            {
                foreach (ExplanationDataShapeDeclaration shape
                         in schema.DataShapes)
                {
                    switch (shape)
                    {
                        case ExplanationDataShapeDeclaration.Record record:
                            foreach (var field in record.Fields)
                                RequireShape(field.ValueShape);
                            break;
                        case ExplanationDataShapeDeclaration.Choice choice:
                            foreach (var @case in choice.Cases)
                            {
                                if (@case.ValueShape is { } valueShape)
                                    RequireShape(valueShape);
                            }
                            break;
                        case ExplanationDataShapeDeclaration.Reference
                            reference:
                            RequireShape(reference.Target);
                            break;
                    }
                }

                foreach (ExplanationResourceTypeDeclaration resource
                         in schema.ResourceTypes)
                {
                    RequireShape(resource.IdentityShape);
                    foreach (ExplanationFactDeclaration fact
                             in resource.Facts)
                    {
                        RequireShape(fact.ValueShape);
                        if (fact.UnavailableDataShape is { } unavailable)
                            RequireShape(unavailable);
                        if (fact.FailureDataShape is { } failure)
                            RequireShape(failure);
                    }
                    foreach (ExplanationRelationshipDeclaration relationship
                             in resource.Relationships)
                    {
                        if (!resourceTypes.ContainsKey(
                                relationship.TargetResourceType))
                        {
                            throw new InvalidOperationException(
                                $"Relationship '{relationship.Identity}' "
                                + "targets an undeclared resource type.");
                        }
                        if (relationship.UnavailableDataShape
                            is { } unavailable)
                        {
                            RequireShape(unavailable);
                        }
                        if (relationship.FailureDataShape is { } failure)
                            RequireShape(failure);
                    }
                    foreach (var addressKind in resource.AddressKinds)
                    {
                        if (!addressKinds.ContainsKey(addressKind))
                        {
                            throw new InvalidOperationException(
                                $"Resource type '{resource.Identity}' admits "
                                + "an undeclared address kind.");
                        }
                    }
                }

                foreach (ExplanationPublicAddressKindDeclaration address
                         in schema.AddressKinds)
                {
                    RequireShape(address.ValueShape);
                }
            }

            void RequireShape(ExplanationDataShapeIdentity identity)
            {
                if (!shapes.ContainsKey(identity))
                {
                    throw new InvalidOperationException(
                        $"Schema closure does not declare referenced shape "
                        + $"'{identity}'.");
                }
            }
        }
    }
}
