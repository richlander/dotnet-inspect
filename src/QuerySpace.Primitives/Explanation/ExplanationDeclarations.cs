using System.Collections.Immutable;
using System.Text.Json.Serialization;
using QuerySpace.Vocabulary;

namespace QuerySpace.Explanation;

/// <summary>The finite construction budget of one named embedded value.</summary>
public readonly record struct ExplanationValueBudget
{
    [JsonConstructor]
    public ExplanationValueBudget(
        int maximumCanonicalByteCount,
        int maximumDepth,
        int maximumNodeCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumCanonicalByteCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumNodeCount);
        MaximumCanonicalByteCount = maximumCanonicalByteCount;
        MaximumDepth = maximumDepth;
        MaximumNodeCount = maximumNodeCount;
    }

    public int MaximumCanonicalByteCount { get; }

    public int MaximumDepth { get; }

    public int MaximumNodeCount { get; }
}

/// <summary>One field declaration in a named record shape.</summary>
public sealed record ExplanationRecordFieldDeclaration
{
    public ExplanationRecordFieldDeclaration(
        ExplanationFieldIdentity identity,
        string displayName,
        string meaning,
        ExplanationDataShapeIdentity valueShape,
        ExplanationCardinality cardinality,
        int? maximumValueCount = null)
    {
        ExplanationContract.ValidateIdentity(identity.Value, nameof(identity));
        ExplanationContract.ValidateMetadata(
            displayName,
            nameof(displayName));
        ExplanationContract.ValidateMetadata(meaning, nameof(meaning));
        ExplanationContract.ValidateIdentity(
            valueShape.Value,
            nameof(valueShape));
        ValidateValueCardinality(cardinality, maximumValueCount);
        Identity = identity;
        DisplayName = displayName;
        Meaning = meaning;
        ValueShape = valueShape;
        Cardinality = cardinality;
        MaximumValueCount = maximumValueCount;
    }

    public ExplanationFieldIdentity Identity { get; }

    public string DisplayName { get; }

    public string Meaning { get; }

    public ExplanationDataShapeIdentity ValueShape { get; }

    public ExplanationCardinality Cardinality { get; }

    public int? MaximumValueCount { get; }

    internal static void ValidateValueCardinality(
        ExplanationCardinality cardinality,
        int? maximumValueCount)
    {
        if (!Enum.IsDefined(cardinality))
            throw new ArgumentOutOfRangeException(nameof(cardinality));
        if ((cardinality == ExplanationCardinality.OrderedMany)
            != (maximumValueCount is not null))
        {
            throw new ArgumentException(
                "Ordered-many embedded values require one positive maximum; "
                + "one-valued cardinalities do not accept one.",
                nameof(maximumValueCount));
        }
        if (maximumValueCount is not null)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                maximumValueCount.Value);
        }
    }
}

/// <summary>One case declaration in a named closed choice.</summary>
public sealed record ExplanationChoiceCaseDeclaration
{
    public ExplanationChoiceCaseDeclaration(
        ExplanationChoiceCaseIdentity identity,
        string displayName,
        string meaning,
        ExplanationDataShapeIdentity? valueShape = null)
    {
        ExplanationContract.ValidateIdentity(identity.Value, nameof(identity));
        ExplanationContract.ValidateMetadata(
            displayName,
            nameof(displayName));
        ExplanationContract.ValidateMetadata(meaning, nameof(meaning));
        if (valueShape is { } shape)
        {
            ExplanationContract.ValidateIdentity(
                shape.Value,
                nameof(valueShape));
        }
        Identity = identity;
        DisplayName = displayName;
        Meaning = meaning;
        ValueShape = valueShape;
    }

    public ExplanationChoiceCaseIdentity Identity { get; }

    public string DisplayName { get; }

    public string Meaning { get; }

    public ExplanationDataShapeIdentity? ValueShape { get; }
}

/// <summary>The declaration of one named explanation data shape.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(ExplanationDataShapeDeclaration.Scalar),
    "scalar")]
[JsonDerivedType(
    typeof(ExplanationDataShapeDeclaration.VocabularyTerm),
    "vocabularyTerm")]
[JsonDerivedType(
    typeof(ExplanationDataShapeDeclaration.Record),
    "record")]
[JsonDerivedType(
    typeof(ExplanationDataShapeDeclaration.Choice),
    "choice")]
[JsonDerivedType(
    typeof(ExplanationDataShapeDeclaration.Reference),
    "reference")]
public abstract record ExplanationDataShapeDeclaration
{
    private ExplanationDataShapeDeclaration(
        ExplanationDataShapeIdentity identity,
        string displayName,
        string meaning,
        ExplanationValueBudget budget)
    {
        ExplanationContract.ValidateIdentity(identity.Value, nameof(identity));
        ExplanationContract.ValidateMetadata(
            displayName,
            nameof(displayName));
        ExplanationContract.ValidateMetadata(meaning, nameof(meaning));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            budget.MaximumCanonicalByteCount,
            nameof(budget));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            budget.MaximumDepth,
            nameof(budget));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            budget.MaximumNodeCount,
            nameof(budget));
        Identity = identity;
        DisplayName = displayName;
        Meaning = meaning;
        Budget = budget;
    }

    public ExplanationDataShapeIdentity Identity { get; }

    public string DisplayName { get; }

    public string Meaning { get; }

    public ExplanationValueBudget Budget { get; }

    [JsonIgnore]
    public abstract ExplanationDataShapeKind Kind { get; }

    public sealed record Scalar : ExplanationDataShapeDeclaration
    {
        public Scalar(
            ExplanationDataShapeIdentity identity,
            string displayName,
            string meaning,
            ExplanationValueBudget budget,
            ExplanationScalarKind scalarKind)
            : base(identity, displayName, meaning, budget)
        {
            if (!Enum.IsDefined(scalarKind))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scalarKind));
            }
            ScalarKind = scalarKind;
        }

        public ExplanationScalarKind ScalarKind { get; }

        [JsonIgnore]
        public override ExplanationDataShapeKind Kind =>
            ExplanationDataShapeKind.Scalar;
    }

    public sealed record VocabularyTerm : ExplanationDataShapeDeclaration
    {
        public VocabularyTerm(
            ExplanationDataShapeIdentity identity,
            string displayName,
            string meaning,
            ExplanationValueBudget budget,
            VocabularyIdentity vocabulary)
            : base(identity, displayName, meaning, budget)
        {
            ExplanationContract.ValidateIdentity(
                vocabulary.Value,
                nameof(vocabulary));
            Vocabulary = vocabulary;
        }

        public VocabularyIdentity Vocabulary { get; }

        [JsonIgnore]
        public override ExplanationDataShapeKind Kind =>
            ExplanationDataShapeKind.VocabularyTerm;
    }

    public sealed record Record : ExplanationDataShapeDeclaration
    {
        public Record(
            ExplanationDataShapeIdentity identity,
            string displayName,
            string meaning,
            ExplanationValueBudget budget,
            IEnumerable<ExplanationRecordFieldDeclaration> fields)
            : this(
                identity,
                displayName,
                meaning,
                budget,
                [
                    .. fields
                        ?? throw new ArgumentNullException(nameof(fields)),
                ])
        {
        }

        [JsonConstructor]
        public Record(
            ExplanationDataShapeIdentity identity,
            string displayName,
            string meaning,
            ExplanationValueBudget budget,
            ImmutableArray<ExplanationRecordFieldDeclaration> fields)
            : base(identity, displayName, meaning, budget)
        {
            Fields = fields.IsDefault ? [] : fields;
            if (Fields.Any(static field => field is null)
                || Fields.Select(static field => field.Identity)
                    .Distinct()
                    .Count()
                    != Fields.Length
                || Fields.Any(field =>
                    field.Identity.RecordShape != identity))
            {
                throw new ArgumentException(
                    "Record fields must be non-null, unique, and scoped to "
                    + "their declaring shape.",
                    nameof(fields));
            }
        }

        public ImmutableArray<ExplanationRecordFieldDeclaration> Fields
        {
            get;
        }

        [JsonIgnore]
        public override ExplanationDataShapeKind Kind =>
            ExplanationDataShapeKind.Record;
    }

    public sealed record Choice : ExplanationDataShapeDeclaration
    {
        public Choice(
            ExplanationDataShapeIdentity identity,
            string displayName,
            string meaning,
            ExplanationValueBudget budget,
            IEnumerable<ExplanationChoiceCaseDeclaration> cases)
            : this(
                identity,
                displayName,
                meaning,
                budget,
                [
                    .. cases
                        ?? throw new ArgumentNullException(nameof(cases)),
                ])
        {
        }

        [JsonConstructor]
        public Choice(
            ExplanationDataShapeIdentity identity,
            string displayName,
            string meaning,
            ExplanationValueBudget budget,
            ImmutableArray<ExplanationChoiceCaseDeclaration> cases)
            : base(identity, displayName, meaning, budget)
        {
            Cases = cases.IsDefault ? [] : cases;
            if (Cases.IsEmpty
                || Cases.Any(static @case => @case is null)
                || Cases.Select(static @case => @case.Identity)
                    .Distinct()
                    .Count()
                    != Cases.Length
                || Cases.Any(@case =>
                    @case.Identity.ChoiceShape != identity))
            {
                throw new ArgumentException(
                    "Choice cases must be non-empty, non-null, unique, and "
                    + "scoped to their declaring shape.",
                    nameof(cases));
            }
        }

        public ImmutableArray<ExplanationChoiceCaseDeclaration> Cases
        {
            get;
        }

        [JsonIgnore]
        public override ExplanationDataShapeKind Kind =>
            ExplanationDataShapeKind.Choice;
    }

    public sealed record Reference : ExplanationDataShapeDeclaration
    {
        public Reference(
            ExplanationDataShapeIdentity identity,
            string displayName,
            string meaning,
            ExplanationValueBudget budget,
            ExplanationDataShapeIdentity target)
            : base(identity, displayName, meaning, budget)
        {
            ExplanationContract.ValidateIdentity(
                target.Value,
                nameof(target));
            Target = target;
        }

        public ExplanationDataShapeIdentity Target { get; }

        [JsonIgnore]
        public override ExplanationDataShapeKind Kind =>
            ExplanationDataShapeKind.Reference;
    }
}

/// <summary>The declaration of one fact on a resource type.</summary>
public sealed record ExplanationFactDeclaration
{
    public ExplanationFactDeclaration(
        ExplanationFactIdentity identity,
        string displayName,
        string meaning,
        ExplanationDataShapeIdentity valueShape,
        ExplanationCardinality cardinality,
        ExplanationObservationStates admittedStates,
        int? maximumValueCount = null,
        ExplanationDataShapeIdentity? unavailableDataShape = null,
        ExplanationDataShapeIdentity? failureDataShape = null)
    {
        ExplanationContract.ValidateIdentity(identity.Value, nameof(identity));
        ExplanationContract.ValidateMetadata(
            displayName,
            nameof(displayName));
        ExplanationContract.ValidateMetadata(meaning, nameof(meaning));
        ExplanationContract.ValidateIdentity(
            valueShape.Value,
            nameof(valueShape));
        ExplanationRecordFieldDeclaration.ValidateValueCardinality(
            cardinality,
            maximumValueCount);
        ValidateStates(
            admittedStates,
            cardinality,
            unavailableDataShape,
            failureDataShape);
        Identity = identity;
        DisplayName = displayName;
        Meaning = meaning;
        ValueShape = valueShape;
        Cardinality = cardinality;
        AdmittedStates = admittedStates;
        MaximumValueCount = maximumValueCount;
        UnavailableDataShape = unavailableDataShape;
        FailureDataShape = failureDataShape;
    }

    public ExplanationFactIdentity Identity { get; }

    public string DisplayName { get; }

    public string Meaning { get; }

    public ExplanationDataShapeIdentity ValueShape { get; }

    public ExplanationCardinality Cardinality { get; }

    public ExplanationObservationStates AdmittedStates { get; }

    public int? MaximumValueCount { get; }

    public ExplanationDataShapeIdentity? UnavailableDataShape { get; }

    public ExplanationDataShapeIdentity? FailureDataShape { get; }

    internal static void ValidateStates(
        ExplanationObservationStates states,
        ExplanationCardinality cardinality,
        ExplanationDataShapeIdentity? unavailableDataShape,
        ExplanationDataShapeIdentity? failureDataShape)
    {
        const ExplanationObservationStates all =
            ExplanationObservationStates.Available
            | ExplanationObservationStates.Absent
            | ExplanationObservationStates.Unavailable
            | ExplanationObservationStates.Failed;
        if ((states & ~all) != 0
            || !states.HasFlag(ExplanationObservationStates.Available))
        {
            throw new ArgumentOutOfRangeException(
                nameof(states),
                "Observation declarations must admit Available and only "
                + "known states.");
        }
        if (states.HasFlag(ExplanationObservationStates.Unavailable)
            != (unavailableDataShape is not null)
            || states.HasFlag(ExplanationObservationStates.Failed)
                != (failureDataShape is not null))
        {
            throw new ArgumentException(
                "Unavailable and failed states each require exactly one "
                + "typed outcome-data shape.");
        }
        if (states.HasFlag(ExplanationObservationStates.Absent)
            && cardinality != ExplanationCardinality.OptionalOne)
        {
            throw new ArgumentException(
                "Absent observations require optional-one cardinality.",
                nameof(states));
        }
    }
}

/// <summary>The declaration of one relationship on a resource type.</summary>
public sealed record ExplanationRelationshipDeclaration
{
    public ExplanationRelationshipDeclaration(
        ExplanationRelationshipIdentity identity,
        string displayName,
        string meaning,
        ExplanationResourceTypeIdentity targetResourceType,
        ExplanationCardinality cardinality,
        ExplanationObservationStates admittedStates,
        ExplanationDataShapeIdentity? unavailableDataShape = null,
        ExplanationDataShapeIdentity? failureDataShape = null)
    {
        ExplanationContract.ValidateIdentity(identity.Value, nameof(identity));
        ExplanationContract.ValidateMetadata(
            displayName,
            nameof(displayName));
        ExplanationContract.ValidateMetadata(meaning, nameof(meaning));
        ExplanationContract.ValidateIdentity(
            targetResourceType.Value,
            nameof(targetResourceType));
        if (!Enum.IsDefined(cardinality))
            throw new ArgumentOutOfRangeException(nameof(cardinality));
        ExplanationFactDeclaration.ValidateStates(
            admittedStates,
            cardinality,
            unavailableDataShape,
            failureDataShape);
        Identity = identity;
        DisplayName = displayName;
        Meaning = meaning;
        TargetResourceType = targetResourceType;
        Cardinality = cardinality;
        AdmittedStates = admittedStates;
        UnavailableDataShape = unavailableDataShape;
        FailureDataShape = failureDataShape;
    }

    public ExplanationRelationshipIdentity Identity { get; }

    public string DisplayName { get; }

    public string Meaning { get; }

    public ExplanationResourceTypeIdentity TargetResourceType { get; }

    public ExplanationCardinality Cardinality { get; }

    public ExplanationObservationStates AdmittedStates { get; }

    public ExplanationDataShapeIdentity? UnavailableDataShape { get; }

    public ExplanationDataShapeIdentity? FailureDataShape { get; }
}

/// <summary>The declaration of one typed public navigation address.</summary>
public sealed record ExplanationPublicAddressKindDeclaration
{
    public ExplanationPublicAddressKindDeclaration(
        ExplanationPublicAddressKindIdentity identity,
        string displayName,
        string meaning,
        ExplanationDataShapeIdentity valueShape)
    {
        ExplanationContract.ValidateIdentity(identity.Value, nameof(identity));
        ExplanationContract.ValidateMetadata(
            displayName,
            nameof(displayName));
        ExplanationContract.ValidateMetadata(meaning, nameof(meaning));
        ExplanationContract.ValidateIdentity(
            valueShape.Value,
            nameof(valueShape));
        Identity = identity;
        DisplayName = displayName;
        Meaning = meaning;
        ValueShape = valueShape;
    }

    public ExplanationPublicAddressKindIdentity Identity { get; }

    public string DisplayName { get; }

    public string Meaning { get; }

    public ExplanationDataShapeIdentity ValueShape { get; }
}

/// <summary>The declaration of one typed explanation resource.</summary>
public sealed record ExplanationResourceTypeDeclaration
{
    public ExplanationResourceTypeDeclaration(
        ExplanationResourceTypeIdentity identity,
        string displayName,
        string meaning,
        ExplanationDataShapeIdentity identityShape,
        IEnumerable<ExplanationFactDeclaration>? facts = null,
        IEnumerable<ExplanationRelationshipDeclaration>? relationships = null,
        IEnumerable<ExplanationPublicAddressKindIdentity>? addressKinds = null)
        : this(
            identity,
            displayName,
            meaning,
            identityShape,
            [.. facts ?? []],
            [.. relationships ?? []],
            [.. addressKinds ?? []])
    {
    }

    [JsonConstructor]
    public ExplanationResourceTypeDeclaration(
        ExplanationResourceTypeIdentity identity,
        string displayName,
        string meaning,
        ExplanationDataShapeIdentity identityShape,
        ImmutableArray<ExplanationFactDeclaration> facts,
        ImmutableArray<ExplanationRelationshipDeclaration> relationships,
        ImmutableArray<ExplanationPublicAddressKindIdentity> addressKinds)
    {
        ExplanationContract.ValidateIdentity(identity.Value, nameof(identity));
        ExplanationContract.ValidateMetadata(
            displayName,
            nameof(displayName));
        ExplanationContract.ValidateMetadata(meaning, nameof(meaning));
        ExplanationContract.ValidateIdentity(
            identityShape.Value,
            nameof(identityShape));
        Identity = identity;
        DisplayName = displayName;
        Meaning = meaning;
        IdentityShape = identityShape;
        Facts = facts.IsDefault ? [] : facts;
        Relationships = relationships.IsDefault ? [] : relationships;
        AddressKinds = addressKinds.IsDefault ? [] : addressKinds;
        if (Facts.Any(static fact => fact is null)
            || Facts.Select(static fact => fact.Identity).Distinct().Count()
                != Facts.Length
            || Facts.Any(fact => fact.Identity.ResourceType != identity)
            || Relationships.Any(static relationship =>
                relationship is null)
            || Relationships.Select(static relationship =>
                    relationship.Identity)
                .Distinct()
                .Count()
                != Relationships.Length
            || Relationships.Any(relationship =>
                relationship.Identity.ResourceType != identity)
            || AddressKinds.Distinct().Count() != AddressKinds.Length)
        {
            throw new ArgumentException(
                "Resource facts, relationships, and address kinds must be "
                + "unique and scoped to their resource declaration.");
        }
    }

    public ExplanationResourceTypeIdentity Identity { get; }

    public string DisplayName { get; }

    public string Meaning { get; }

    public ExplanationDataShapeIdentity IdentityShape { get; }

    public ImmutableArray<ExplanationFactDeclaration> Facts { get; }

    public ImmutableArray<ExplanationRelationshipDeclaration> Relationships
    {
        get;
    }

    public ImmutableArray<ExplanationPublicAddressKindIdentity> AddressKinds
    {
        get;
    }
}

/// <summary>One complete owner-issued explanation schema revision.</summary>
public sealed record ExplanationSchema
{
    public ExplanationSchema(
        ExplanationSchemaIdentity identity,
        ExplanationSchemaVersion version,
        IEnumerable<ExplanationDataShapeDeclaration> dataShapes,
        IEnumerable<ExplanationResourceTypeDeclaration> resourceTypes,
        IEnumerable<ExplanationPublicAddressKindDeclaration>? addressKinds =
            null)
        : this(
            identity,
            version,
            [
                .. dataShapes
                    ?? throw new ArgumentNullException(nameof(dataShapes)),
            ],
            [
                .. resourceTypes
                    ?? throw new ArgumentNullException(nameof(resourceTypes)),
            ],
            [.. addressKinds ?? []])
    {
    }

    [JsonConstructor]
    public ExplanationSchema(
        ExplanationSchemaIdentity identity,
        ExplanationSchemaVersion version,
        ImmutableArray<ExplanationDataShapeDeclaration> dataShapes,
        ImmutableArray<ExplanationResourceTypeDeclaration> resourceTypes,
        ImmutableArray<ExplanationPublicAddressKindDeclaration> addressKinds)
    {
        ExplanationContract.ValidateIdentity(identity.Value, nameof(identity));
        Identity = identity;
        Version = version;
        DataShapes = dataShapes.IsDefault ? [] : dataShapes;
        ResourceTypes = resourceTypes.IsDefault ? [] : resourceTypes;
        AddressKinds = addressKinds.IsDefault ? [] : addressKinds;

        if ((DataShapes.IsEmpty
                && ResourceTypes.IsEmpty
                && AddressKinds.IsEmpty)
            || DataShapes.Any(static shape => shape is null)
            || DataShapes.Select(static shape => shape.Identity)
                .Distinct()
                .Count()
                != DataShapes.Length
            || DataShapes.Any(shape => shape.Identity.Schema != identity)
            || ResourceTypes.Any(static resource => resource is null)
            || ResourceTypes.Select(static resource => resource.Identity)
                .Distinct()
                .Count()
                != ResourceTypes.Length
            || ResourceTypes.Any(resource =>
                resource.Identity.Schema != identity)
            || AddressKinds.Any(static address => address is null)
            || AddressKinds.Select(static address => address.Identity)
                .Distinct()
                .Count()
                != AddressKinds.Length
            || AddressKinds.Any(address =>
                address.Identity.Schema != identity))
        {
            throw new ArgumentException(
                "Schema declarations must be non-empty where required, "
                + "unique, and scoped to the schema.");
        }
    }

    public ExplanationSchemaIdentity Identity { get; }

    public ExplanationSchemaVersion Version { get; }

    public ImmutableArray<ExplanationDataShapeDeclaration> DataShapes
    {
        get;
    }

    public ImmutableArray<ExplanationResourceTypeDeclaration> ResourceTypes
    {
        get;
    }

    public ImmutableArray<ExplanationPublicAddressKindDeclaration>
        AddressKinds { get; }
}
