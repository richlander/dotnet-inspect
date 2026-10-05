using System.Buffers;
using System.Text;
using System.Text.Json.Serialization;

namespace QuerySpace.Explanation;

internal static class ExplanationContract
{
    internal const int MaximumMetadataUtf8Bytes = 1024;

    internal static void ValidateIdentity(string value, string parameterName)
    {
        ValidateText(value, parameterName, allowEmpty: false);
        if (Encoding.UTF8.GetByteCount(value) > MaximumMetadataUtf8Bytes)
        {
            throw new ArgumentException(
                $"Explanation identities must not exceed "
                + $"{MaximumMetadataUtf8Bytes} UTF-8 bytes.",
                parameterName);
        }
    }

    internal static void ValidateMetadata(
        string value,
        string parameterName,
        bool allowEmpty = false)
    {
        ValidateText(value, parameterName, allowEmpty);
        if (Encoding.UTF8.GetByteCount(value) > MaximumMetadataUtf8Bytes)
        {
            throw new ArgumentException(
                $"Explanation declaration metadata must not exceed "
                + $"{MaximumMetadataUtf8Bytes} UTF-8 bytes.",
                parameterName);
        }
    }

    internal static void ValidateText(
        string value,
        string parameterName,
        bool allowEmpty = true)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Explanation text must not be empty or whitespace.",
                parameterName);
        }

        ReadOnlySpan<char> remaining = value;
        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(
                remaining,
                out _,
                out int charsConsumed);
            if (status != OperationStatus.Done)
            {
                throw new ArgumentException(
                    "Explanation text must contain well-formed UTF-16.",
                    parameterName);
            }

            remaining = remaining[charsConsumed..];
        }
    }
}

/// <summary>The stable identity of one semantic explanation owner.</summary>
public readonly record struct ExplanationOwnerIdentity
{
    [JsonConstructor]
    public ExplanationOwnerIdentity(string value)
    {
        ExplanationContract.ValidateIdentity(value, nameof(value));
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>The stable identity of one independently versioned schema.</summary>
public readonly record struct ExplanationSchemaIdentity
{
    [JsonConstructor]
    public ExplanationSchemaIdentity(
        ExplanationOwnerIdentity owner,
        string value)
    {
        ExplanationContract.ValidateIdentity(owner.Value, nameof(owner));
        ExplanationContract.ValidateIdentity(value, nameof(value));
        Owner = owner;
        Value = value;
    }

    public ExplanationOwnerIdentity Owner { get; }

    public string Value { get; }

    public override string ToString() => $"{Owner}/{Value}";
}

/// <summary>One positive schema revision.</summary>
public readonly record struct ExplanationSchemaVersion
{
    [JsonConstructor]
    public ExplanationSchemaVersion(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        Value = value;
    }

    public int Value { get; }

    public override string ToString() => Value.ToString();
}

/// <summary>The stable identity of one named data shape.</summary>
public readonly record struct ExplanationDataShapeIdentity
{
    [JsonConstructor]
    public ExplanationDataShapeIdentity(
        ExplanationSchemaIdentity schema,
        string value)
    {
        ExplanationContract.ValidateIdentity(schema.Value, nameof(schema));
        ExplanationContract.ValidateIdentity(value, nameof(value));
        Schema = schema;
        Value = value;
    }

    public ExplanationSchemaIdentity Schema { get; }

    public string Value { get; }

    public override string ToString() => $"{Schema}/shapes/{Value}";
}

/// <summary>The stable identity of one resource type.</summary>
public readonly record struct ExplanationResourceTypeIdentity
{
    [JsonConstructor]
    public ExplanationResourceTypeIdentity(
        ExplanationSchemaIdentity schema,
        string value)
    {
        ExplanationContract.ValidateIdentity(schema.Value, nameof(schema));
        ExplanationContract.ValidateIdentity(value, nameof(value));
        Schema = schema;
        Value = value;
    }

    public ExplanationSchemaIdentity Schema { get; }

    public string Value { get; }

    public override string ToString() => $"{Schema}/resources/{Value}";
}

/// <summary>The stable identity of one fact on a resource type.</summary>
public readonly record struct ExplanationFactIdentity
{
    [JsonConstructor]
    public ExplanationFactIdentity(
        ExplanationResourceTypeIdentity resourceType,
        string value)
    {
        ExplanationContract.ValidateIdentity(
            resourceType.Value,
            nameof(resourceType));
        ExplanationContract.ValidateIdentity(value, nameof(value));
        ResourceType = resourceType;
        Value = value;
    }

    public ExplanationResourceTypeIdentity ResourceType { get; }

    public string Value { get; }

    public override string ToString() => $"{ResourceType}/facts/{Value}";
}

/// <summary>The stable identity of one relationship on a resource type.</summary>
public readonly record struct ExplanationRelationshipIdentity
{
    [JsonConstructor]
    public ExplanationRelationshipIdentity(
        ExplanationResourceTypeIdentity resourceType,
        string value)
    {
        ExplanationContract.ValidateIdentity(
            resourceType.Value,
            nameof(resourceType));
        ExplanationContract.ValidateIdentity(value, nameof(value));
        ResourceType = resourceType;
        Value = value;
    }

    public ExplanationResourceTypeIdentity ResourceType { get; }

    public string Value { get; }

    public override string ToString() =>
        $"{ResourceType}/relationships/{Value}";
}

/// <summary>The stable identity of one field in a named record shape.</summary>
public readonly record struct ExplanationFieldIdentity
{
    [JsonConstructor]
    public ExplanationFieldIdentity(
        ExplanationDataShapeIdentity recordShape,
        string value)
    {
        ExplanationContract.ValidateIdentity(
            recordShape.Value,
            nameof(recordShape));
        ExplanationContract.ValidateIdentity(value, nameof(value));
        RecordShape = recordShape;
        Value = value;
    }

    public ExplanationDataShapeIdentity RecordShape { get; }

    public string Value { get; }

    public override string ToString() => $"{RecordShape}/fields/{Value}";
}

/// <summary>The stable identity of one case in a named closed choice.</summary>
public readonly record struct ExplanationChoiceCaseIdentity
{
    [JsonConstructor]
    public ExplanationChoiceCaseIdentity(
        ExplanationDataShapeIdentity choiceShape,
        string value)
    {
        ExplanationContract.ValidateIdentity(
            choiceShape.Value,
            nameof(choiceShape));
        ExplanationContract.ValidateIdentity(value, nameof(value));
        ChoiceShape = choiceShape;
        Value = value;
    }

    public ExplanationDataShapeIdentity ChoiceShape { get; }

    public string Value { get; }

    public override string ToString() => $"{ChoiceShape}/cases/{Value}";
}

/// <summary>The stable identity of one typed public address kind.</summary>
public readonly record struct ExplanationPublicAddressKindIdentity
{
    [JsonConstructor]
    public ExplanationPublicAddressKindIdentity(
        ExplanationSchemaIdentity schema,
        string value)
    {
        ExplanationContract.ValidateIdentity(schema.Value, nameof(schema));
        ExplanationContract.ValidateIdentity(value, nameof(value));
        Schema = schema;
        Value = value;
    }

    public ExplanationSchemaIdentity Schema { get; }

    public string Value { get; }

    public override string ToString() => $"{Schema}/addresses/{Value}";
}

/// <summary>The primitive carrier used by one scalar data shape.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExplanationScalarKind>))]
public enum ExplanationScalarKind
{
    Boolean,
    Integer,
    Decimal,
    BinaryFloatingPoint,
    Text,
    Octets,
}

/// <summary>The declaration kind of one named data shape.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExplanationDataShapeKind>))]
public enum ExplanationDataShapeKind
{
    Scalar,
    VocabularyTerm,
    Record,
    Choice,
    Reference,
}

/// <summary>The number and ordering contract of one value or target slot.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExplanationCardinality>))]
public enum ExplanationCardinality
{
    RequiredOne,
    OptionalOne,
    OrderedMany,
}

/// <summary>The exact state of one fact or relationship observation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExplanationObservationState>))]
public enum ExplanationObservationState
{
    Available,
    Absent,
    Unavailable,
    Failed,
}

/// <summary>The observation states admitted by one declaration.</summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<ExplanationObservationStates>))]
public enum ExplanationObservationStates
{
    None = 0,
    Available = 1,
    Absent = 2,
    Unavailable = 4,
    Failed = 8,
}

/// <summary>Whether a snapshot belongs to an installed or detached subject.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExplanationSnapshotScope>))]
public enum ExplanationSnapshotScope
{
    Installed,
    Detached,
}
