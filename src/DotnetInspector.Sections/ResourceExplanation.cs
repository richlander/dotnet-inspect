using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<ResourceExplanationCompleteness>))]
public enum ResourceExplanationCompleteness
{
    Complete,
    Truncated,
}

[JsonConverter(typeof(JsonStringEnumConverter<ResourceExplanationTruncationReason>))]
public enum ResourceExplanationTruncationReason
{
    Depth,
    ResourceLimit,
    RelationshipLimit,
    RelationshipTargetLimit,
    SchemaDeclarationLimit,
}

[JsonConverter(
    typeof(JsonStringEnumConverter<ResourceExplanationTargetProjectionCompleteness>))]
public enum ResourceExplanationTargetProjectionCompleteness
{
    Complete,
    Truncated,
}

[JsonConverter(typeof(ResourcePathJsonConverter))]
public sealed record ResourcePath
{
    public ResourcePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!IsCanonical(value))
        {
            throw new ArgumentException(
                "Resource paths must contain lower-case ASCII path segments "
                + "separated by '/'. Each segment starts with a letter or "
                + "digit and may then contain '.', '_', and '-'.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public ResourcePath Append(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Length == 0)
            return this;
        foreach (string segment in segments)
        {
            if (!IsCanonicalSegment(segment))
            {
                throw new ArgumentException(
                    $"'{segment}' is not a canonical resource-path segment.",
                    nameof(segments));
            }
        }

        return new ResourcePath($"{Value}/{string.Join('/', segments)}");
    }

    public override string ToString() => Value;

    public static bool TryCreate(
        string? value,
        out ResourcePath? path,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            path = null;
            error = "A resource path is required.";
            return false;
        }
        if (!IsCanonical(value))
        {
            path = null;
            error =
                "Resource paths use lower-case ASCII segments separated by "
                + "'/'; each segment starts with a letter or digit and may "
                + "then contain '.', '_', and '-'.";
            return false;
        }

        path = new ResourcePath(value);
        error = null;
        return true;
    }

    public static bool IsCanonicalSegment(string value)
    {
        if (value.Length == 0
            || !IsCanonicalSegmentStart(value[0]))
        {
            return false;
        }

        for (int index = 1; index < value.Length; index++)
        {
            if (!IsCanonicalSegmentCharacter(value[index]))
                return false;
        }
        return true;
    }

    private static bool IsCanonical(string value) =>
        value[0] != '/'
        && value[^1] != '/'
        && value.Split('/').All(IsCanonicalSegment);

    private static bool IsCanonicalSegmentStart(char character) =>
        character is >= 'a' and <= 'z'
        or >= '0' and <= '9';

    private static bool IsCanonicalSegmentCharacter(char character) =>
        IsCanonicalSegmentStart(character)
        || character is '.' or '_' or '-';
}

public sealed class ResourcePathJsonConverter : JsonConverter<ResourcePath>
{
    public override ResourcePath Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        new(reader.GetString()
            ?? throw new JsonException("A resource path must be a string."));

    public override void Write(
        Utf8JsonWriter writer,
        ResourcePath value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

public sealed record StructuralResourcePathRegistration
{
    public StructuralResourcePathRegistration(
        DiscoveryResourceIdentity identity,
        ResourcePath path)
    {
        Identity =
            identity ?? throw new ArgumentNullException(nameof(identity));
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public DiscoveryResourceIdentity Identity { get; }

    public ResourcePath Path { get; }
}

[JsonConverter(
    typeof(JsonStringEnumConverter<InspectionCapabilityResourceKind>))]
public enum InspectionCapabilityResourceKind
{
    Document,
    Route,
    QuerySpace,
    QueryFacet,
    RelatedOperation,
    ConsumerBinding,
    Analysis,
    AnalysisCollection,
}

public sealed record InspectionCapabilityResourceIdentity
{
    public InspectionCapabilityResourceIdentity(
        InspectionCapabilityResourceKind kind,
        string identity,
        string? parentIdentity = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        if (parentIdentity is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(parentIdentity);
        if ((kind == InspectionCapabilityResourceKind.QueryFacet)
            != (parentIdentity is not null))
        {
            throw new ArgumentException(
                "Only query-facet identities require a parent query-space "
                + "identity.",
                nameof(parentIdentity));
        }
        Kind = kind;
        Identity = identity;
        ParentIdentity = parentIdentity;
    }

    public InspectionCapabilityResourceKind Kind { get; }

    public string Identity { get; }

    public string? ParentIdentity { get; }
}

public sealed record InspectionCapabilityResourcePathRegistration
{
    public InspectionCapabilityResourcePathRegistration(
        InspectionCapabilityResourceIdentity identity,
        ResourcePath path)
    {
        Identity = identity
            ?? throw new ArgumentNullException(nameof(identity));
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public InspectionCapabilityResourceIdentity Identity { get; }

    public ResourcePath Path { get; }
}

/// <summary>One installed snapshot and its canonical Resource Explanation path.</summary>
public sealed record ResourceExplanationResource
{
    public ResourceExplanationResource(
        ResourcePath? path,
        ExplanationResourceKey key,
        ExplanationSchemaVersion schemaVersion,
        ExplanationSnapshotScope scope,
        IEnumerable<ExplanationPublicAddress> addresses,
        IEnumerable<ExplanationFactObservation> facts)
        : this(
            path,
            key,
            schemaVersion,
            scope,
            [
                .. addresses
                    ?? throw new ArgumentNullException(nameof(addresses)),
            ],
            [
                .. facts
                    ?? throw new ArgumentNullException(nameof(facts)),
            ])
    {
    }

    [JsonConstructor]
    public ResourceExplanationResource(
        ResourcePath? path,
        ExplanationResourceKey key,
        ExplanationSchemaVersion schemaVersion,
        ExplanationSnapshotScope scope,
        ImmutableArray<ExplanationPublicAddress> addresses,
        ImmutableArray<ExplanationFactObservation> facts)
    {
        Path = path;
        Key = key ?? throw new ArgumentNullException(nameof(key));
        SchemaVersion = schemaVersion;
        Scope = scope;
        Addresses = addresses.IsDefault ? [] : addresses;
        Facts = facts.IsDefault ? [] : facts;
    }

    public ResourcePath? Path { get; }

    public ExplanationResourceKey Key { get; }

    public ExplanationSchemaVersion SchemaVersion { get; }

    public ExplanationSnapshotScope Scope { get; }

    public ImmutableArray<ExplanationPublicAddress> Addresses { get; }

    public ImmutableArray<ExplanationFactObservation> Facts { get; }

    [JsonIgnore]
    public ExplanationResourceTypeIdentity ResourceType => Key.ResourceType;

    [JsonIgnore]
    public ExplanationOwnerIdentity Owner => Key.Owner;

    internal static ResourceExplanationResource FromSnapshot(
        ResourcePath? path,
        ExplanationResourceSnapshot snapshot) =>
        new(
            path,
            snapshot.Key,
            snapshot.SchemaVersion,
            snapshot.Scope,
            snapshot.Addresses,
            snapshot.Facts);
}

/// <summary>One relationship target projected into a bounded Document.</summary>
public sealed record ResourceExplanationRelationshipTarget
{
    public ResourceExplanationRelationshipTarget(
        ExplanationResourceKey resource,
        IEnumerable<ExplanationPublicAddress>? addresses = null)
        : this(resource, [.. addresses ?? []])
    {
    }

    [JsonConstructor]
    public ResourceExplanationRelationshipTarget(
        ExplanationResourceKey resource,
        ImmutableArray<ExplanationPublicAddress> addresses)
    {
        Resource = resource
            ?? throw new ArgumentNullException(nameof(resource));
        Addresses = addresses.IsDefault ? [] : addresses;
        if (Addresses.Any(static address => address is null)
            || Addresses.Distinct().Count() != Addresses.Length)
        {
            throw new ArgumentException(
                "Projected target addresses must be unique and non-null.",
                nameof(addresses));
        }
    }

    public ExplanationResourceKey Resource { get; }

    public ImmutableArray<ExplanationPublicAddress> Addresses { get; }
}

/// <summary>
/// One source relationship observation with bounded target projection.
/// </summary>
public sealed record ResourceExplanationRelationship
{
    public ResourceExplanationRelationship(
        ExplanationResourceKey source,
        ExplanationRelationshipIdentity relationship,
        ExplanationObservationState state,
        IEnumerable<ResourceExplanationRelationshipTarget>? targets,
        ExplanationValue? outcomeData,
        ResourceExplanationTargetProjectionCompleteness targetCompleteness)
        : this(
            source,
            relationship,
            state,
            [.. targets ?? []],
            outcomeData,
            targetCompleteness)
    {
    }

    [JsonConstructor]
    public ResourceExplanationRelationship(
        ExplanationResourceKey source,
        ExplanationRelationshipIdentity relationship,
        ExplanationObservationState state,
        ImmutableArray<ResourceExplanationRelationshipTarget> targets,
        ExplanationValue? outcomeData,
        ResourceExplanationTargetProjectionCompleteness targetCompleteness)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Relationship = relationship;
        State = state;
        Targets = targets.IsDefault ? [] : targets;
        OutcomeData = outcomeData;
        TargetCompleteness = targetCompleteness;
        if (relationship.ResourceType != source.ResourceType)
        {
            throw new ArgumentException(
                "A projected relationship must belong to its source "
                + "resource type.",
                nameof(relationship));
        }
        if (Targets.Any(static target => target is null))
        {
            throw new ArgumentException(
                "Projected relationship targets must not contain null.",
                nameof(targets));
        }
        bool payloadValid = state switch
        {
            ExplanationObservationState.Available =>
                outcomeData is null,
            ExplanationObservationState.Absent =>
                Targets.IsEmpty && outcomeData is null,
            ExplanationObservationState.Unavailable
                or ExplanationObservationState.Failed =>
                Targets.IsEmpty && outcomeData is not null,
            _ => false,
        };
        if (!payloadValid)
        {
            throw new ArgumentException(
                "The projected relationship payload does not match its "
                + "observation state.",
                nameof(targets));
        }
        if (state != ExplanationObservationState.Available
            && targetCompleteness
                != ResourceExplanationTargetProjectionCompleteness.Complete)
        {
            throw new ArgumentException(
                "Only available relationship targets can be truncated.",
                nameof(targetCompleteness));
        }
    }

    public ExplanationResourceKey Source { get; }

    public ExplanationRelationshipIdentity Relationship { get; }

    public ExplanationObservationState State { get; }

    public ImmutableArray<ResourceExplanationRelationshipTarget> Targets
    {
        get;
    }

    public ExplanationValue? OutcomeData { get; }

    public ResourceExplanationTargetProjectionCompleteness TargetCompleteness
    {
        get;
    }
}

public sealed record ResourceExplanationRequest
{
    /// <summary>The resource limit every product host applies.</summary>
    public const int HostResourceLimit = 256;

    /// <summary>The relationship limit every product host applies.</summary>
    public const int HostRelationshipLimit = 2048;

    /// <summary>
    /// The request every product host issues for <paramref name="depth"/>,
    /// so equal catalogs explain to equal Content in each host.
    /// </summary>
    public static ResourceExplanationRequest ForHost(int depth) =>
        new(depth, HostResourceLimit, HostRelationshipLimit);

    public ResourceExplanationRequest(
        int depth,
        int resourceLimit,
        int relationshipLimit,
        int relationshipTargetLimit = 4096,
        int schemaDeclarationLimit = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfLessThan(resourceLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            relationshipLimit,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            relationshipTargetLimit,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            schemaDeclarationLimit,
            1);

        Depth = depth;
        ResourceLimit = resourceLimit;
        RelationshipLimit = relationshipLimit;
        RelationshipTargetLimit = relationshipTargetLimit;
        SchemaDeclarationLimit = schemaDeclarationLimit;
    }

    public int Depth { get; }

    public int ResourceLimit { get; }

    public int RelationshipLimit { get; }

    public int RelationshipTargetLimit { get; }

    public int SchemaDeclarationLimit { get; }
}

public sealed record ResourceExplanationTraversalReceipt
{
    public ResourceExplanationTraversalReceipt(
        int requestedDepth,
        int requestedResourceLimit,
        int requestedRelationshipLimit,
        int requestedRelationshipTargetLimit,
        int requestedSchemaDeclarationLimit,
        int completedDepth,
        int visitedResourceCount,
        int emittedRelationshipCount,
        int emittedRelationshipTargetCount,
        int emittedSchemaDeclarationCount,
        ResourceExplanationCompleteness completeness,
        IEnumerable<ResourceExplanationTruncationReason>? truncationReasons)
        : this(
            requestedDepth,
            requestedResourceLimit,
            requestedRelationshipLimit,
            requestedRelationshipTargetLimit,
            requestedSchemaDeclarationLimit,
            completedDepth,
            visitedResourceCount,
            emittedRelationshipCount,
            emittedRelationshipTargetCount,
            emittedSchemaDeclarationCount,
            completeness,
            (truncationReasons ?? []).ToImmutableArray())
    {
    }

    [JsonConstructor]
    public ResourceExplanationTraversalReceipt(
        int requestedDepth,
        int requestedResourceLimit,
        int requestedRelationshipLimit,
        int requestedRelationshipTargetLimit,
        int requestedSchemaDeclarationLimit,
        int completedDepth,
        int visitedResourceCount,
        int emittedRelationshipCount,
        int emittedRelationshipTargetCount,
        int emittedSchemaDeclarationCount,
        ResourceExplanationCompleteness completeness,
        ImmutableArray<ResourceExplanationTruncationReason> truncationReasons)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requestedDepth);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            requestedResourceLimit,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            requestedRelationshipLimit,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            requestedRelationshipTargetLimit,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            requestedSchemaDeclarationLimit,
            1);
        ArgumentOutOfRangeException.ThrowIfNegative(completedDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(visitedResourceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            emittedRelationshipCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            emittedRelationshipTargetCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            emittedSchemaDeclarationCount);
        if (completedDepth > requestedDepth
            || visitedResourceCount > requestedResourceLimit
            || emittedRelationshipCount > requestedRelationshipLimit
            || emittedRelationshipTargetCount
                > requestedRelationshipTargetLimit
            || emittedSchemaDeclarationCount
                > requestedSchemaDeclarationLimit)
        {
            throw new ArgumentException(
                "The traversal receipt exceeds one of its requested bounds.");
        }

        ImmutableArray<ResourceExplanationTruncationReason> reasons =
            truncationReasons.IsDefault ? [] : truncationReasons;
        if (reasons.Distinct().Count() != reasons.Length
            || (completeness == ResourceExplanationCompleteness.Complete)
                != reasons.IsEmpty)
        {
            throw new ArgumentException(
                "Completeness and unique truncation reasons must agree.",
                nameof(truncationReasons));
        }

        RequestedDepth = requestedDepth;
        RequestedResourceLimit = requestedResourceLimit;
        RequestedRelationshipLimit = requestedRelationshipLimit;
        RequestedRelationshipTargetLimit = requestedRelationshipTargetLimit;
        RequestedSchemaDeclarationLimit = requestedSchemaDeclarationLimit;
        CompletedDepth = completedDepth;
        VisitedResourceCount = visitedResourceCount;
        EmittedRelationshipCount = emittedRelationshipCount;
        EmittedRelationshipTargetCount = emittedRelationshipTargetCount;
        EmittedSchemaDeclarationCount = emittedSchemaDeclarationCount;
        Completeness = completeness;
        TruncationReasons = reasons;
    }

    public int RequestedDepth { get; }

    public int RequestedResourceLimit { get; }

    public int RequestedRelationshipLimit { get; }

    public int RequestedRelationshipTargetLimit { get; }

    public int RequestedSchemaDeclarationLimit { get; }

    public int CompletedDepth { get; }

    public int VisitedResourceCount { get; }

    public int EmittedRelationshipCount { get; }

    public int EmittedRelationshipTargetCount { get; }

    public int EmittedSchemaDeclarationCount { get; }

    public ResourceExplanationCompleteness Completeness { get; }

    public ImmutableArray<ResourceExplanationTruncationReason>
        TruncationReasons { get; }
}

public sealed record ResourceExplanationDocument
{
    public const int CurrentSchemaVersion = 2;

    public ResourceExplanationDocument(
        ResourcePath? requestedPath,
        ExplanationResourceKey root,
        IEnumerable<ExplanationSchema> schemas,
        IEnumerable<ResourceExplanationResource> resources,
        IEnumerable<ResourceExplanationRelationship> relationships,
        ResourceExplanationTraversalReceipt traversal)
        : this(
            CurrentSchemaVersion,
            requestedPath,
            root,
            [.. schemas ?? throw new ArgumentNullException(nameof(schemas))],
            [
                .. resources
                    ?? throw new ArgumentNullException(nameof(resources)),
            ],
            [
                .. relationships
                    ?? throw new ArgumentNullException(
                        nameof(relationships)),
            ],
            traversal)
    {
    }

    [JsonConstructor]
    public ResourceExplanationDocument(
        int schemaVersion,
        ResourcePath? requestedPath,
        ExplanationResourceKey root,
        ImmutableArray<ExplanationSchema> schemas,
        ImmutableArray<ResourceExplanationResource> resources,
        ImmutableArray<ResourceExplanationRelationship> relationships,
        ResourceExplanationTraversalReceipt traversal)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                "Unsupported Resource Explanation schema version.");
        }

        SchemaVersion = schemaVersion;
        RequestedPath = requestedPath;
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Schemas = schemas.IsDefault ? [] : schemas;
        Resources = resources.IsDefault ? [] : resources;
        Relationships = relationships.IsDefault ? [] : relationships;
        Traversal =
            traversal ?? throw new ArgumentNullException(nameof(traversal));
        if (Schemas.IsEmpty
            || Resources.IsEmpty
            || Resources[0].Key != Root)
        {
            throw new ArgumentException(
                "The Document must contain a schema slice and begin with the "
                + "root resource.",
                nameof(resources));
        }
        if (Resources[0].Path != RequestedPath)
        {
            throw new ArgumentException(
                "The requested path and first resource path must agree.",
                nameof(requestedPath));
        }
        if (Resources.Select(static resource => resource.Key)
                .Distinct()
                .Count()
            != Resources.Length
            || Resources
                .Where(static resource => resource.Path is not null)
                .Select(static resource => resource.Path!.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
            != Resources.Count(static resource =>
                resource.Path is not null))
        {
            throw new ArgumentException(
                "Explanation resources must have unique keys and paths.",
                nameof(resources));
        }
        if (Traversal.VisitedResourceCount != Resources.Length
            || Traversal.EmittedRelationshipCount != Relationships.Length
            || Traversal.EmittedRelationshipTargetCount
                != Relationships.Sum(static relationship =>
                    relationship.Targets.Length))
        {
            throw new ArgumentException(
                "The traversal receipt must describe the emitted graph.",
                nameof(traversal));
        }

        HashSet<ExplanationResourceKey> included =
            [.. Resources.Select(static resource => resource.Key)];
        if (Relationships.Any(relationship =>
                !included.Contains(relationship.Source)))
        {
            throw new ArgumentException(
                "Every emitted relationship source must be an emitted "
                + "resource.",
                nameof(relationships));
        }
        foreach (ResourceExplanationResource resource in Resources)
        {
            ExplanationConformance.ValidateResourceProjection(
                Schemas,
                resource.Key,
                resource.SchemaVersion,
                resource.Addresses,
                resource.Facts);
        }
    }

    public int SchemaVersion { get; }

    public ResourcePath? RequestedPath { get; }

    public ExplanationResourceKey Root { get; }

    public ImmutableArray<ExplanationSchema> Schemas { get; }

    public ImmutableArray<ResourceExplanationResource> Resources { get; }

    public ImmutableArray<ResourceExplanationRelationship> Relationships
    {
        get;
    }

    public ResourceExplanationTraversalReceipt Traversal { get; }
}

public abstract record ResourcePathResolution
{
    private ResourcePathResolution()
    {
    }

    public sealed record Resolved(
        ResourcePath Path,
        ExplanationResourceKey Key) :
        ResourcePathResolution;

    public sealed record Invalid(
        string RequestedPath,
        string Reason) :
        ResourcePathResolution;

    public sealed record Unknown(
        string RequestedPath,
        ImmutableArray<ResourcePath> Suggestions) :
        ResourcePathResolution;
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    Converters = new[] { typeof(ExplanationBigIntegerJsonConverter) })]
[JsonSerializable(typeof(ResourceExplanationDocument))]
[JsonSerializable(typeof(ResourceExplanationTraversalReceipt))]
[JsonSerializable(
    typeof(ExplanationValue.Scalar),
    TypeInfoPropertyName = "ExplanationValueScalar")]
[JsonSerializable(
    typeof(ExplanationValue.VocabularyTerm),
    TypeInfoPropertyName = "ExplanationValueVocabularyTerm")]
[JsonSerializable(
    typeof(ExplanationValue.Record),
    TypeInfoPropertyName = "ExplanationValueRecord")]
[JsonSerializable(
    typeof(ExplanationValue.Choice),
    TypeInfoPropertyName = "ExplanationValueChoice")]
[JsonSerializable(
    typeof(ExplanationDataShapeDeclaration.Scalar),
    TypeInfoPropertyName = "ExplanationShapeScalar")]
[JsonSerializable(
    typeof(ExplanationDataShapeDeclaration.VocabularyTerm),
    TypeInfoPropertyName = "ExplanationShapeVocabularyTerm")]
[JsonSerializable(
    typeof(ExplanationDataShapeDeclaration.Record),
    TypeInfoPropertyName = "ExplanationShapeRecord")]
[JsonSerializable(
    typeof(ExplanationDataShapeDeclaration.Choice),
    TypeInfoPropertyName = "ExplanationShapeChoice")]
[JsonSerializable(
    typeof(ExplanationDataShapeDeclaration.Reference),
    TypeInfoPropertyName = "ExplanationShapeReference")]
public partial class ResourceExplanationJsonContext : JsonSerializerContext;

internal sealed class ExplanationBigIntegerJsonConverter :
    JsonConverter<BigInteger>
{
    public override BigInteger Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string? text = reader.TokenType == JsonTokenType.String
            ? reader.GetString()
            : null;
        if (text is null
            || !BigInteger.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out BigInteger value)
            || !string.Equals(
                text,
                value.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            throw new JsonException(
                "Explanation arbitrary-precision integers must be "
                + "canonical decimal strings.");
        }
        return value;
    }

    public override void Write(
        Utf8JsonWriter writer,
        BigInteger value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(
            value.ToString(CultureInfo.InvariantCulture));
}
