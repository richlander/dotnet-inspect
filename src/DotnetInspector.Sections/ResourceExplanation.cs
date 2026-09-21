using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<ResourceExplanationOwner>))]
public enum ResourceExplanationOwner
{
    ResourceExplanation,
    SchemaQuery,
}

[JsonConverter(typeof(JsonStringEnumConverter<ResourceExplanationResourceKind>))]
public enum ResourceExplanationResourceKind
{
    Catalog,
    NavigationCollection,
    StructuralCategory,
    StructuralSection,
    StructuralItem,
}

[JsonConverter(typeof(JsonStringEnumConverter<ResourceExplanationNavigationCollectionKind>))]
public enum ResourceExplanationNavigationCollectionKind
{
    CatalogCategories,
    CatalogSections,
    StructuralItems,
    StructuralItemKind,
}

[JsonConverter(typeof(JsonStringEnumConverter<ResourceExplanationRelationshipKind>))]
public enum ResourceExplanationRelationshipKind
{
    Navigation,
    CatalogEntry,
    CollectionMember,
    CategoryMember,
    StructuralItem,
}

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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(ResourceExplanationIdentity.Catalog),
    "catalog")]
[JsonDerivedType(
    typeof(ResourceExplanationIdentity.NavigationCollection),
    "navigationCollection")]
[JsonDerivedType(
    typeof(ResourceExplanationIdentity.Structural),
    "structural")]
public abstract record ResourceExplanationIdentity
{
    private ResourceExplanationIdentity()
    {
    }

    [JsonIgnore]
    public abstract ResourceExplanationOwner Owner { get; }

    public sealed record Catalog : ResourceExplanationIdentity
    {
        public Catalog(string catalogName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(catalogName);
            CatalogName = catalogName;
        }

        public string CatalogName { get; }

        public override ResourceExplanationOwner Owner =>
            ResourceExplanationOwner.SchemaQuery;
    }

    public sealed record NavigationCollection :
        ResourceExplanationIdentity
    {
        public NavigationCollection(
            Catalog catalogIdentity,
            ResourceExplanationIdentity? parentIdentity,
            ResourceExplanationNavigationCollectionKind collectionKind,
            string? itemKind = null)
        {
            CatalogIdentity = catalogIdentity
                ?? throw new ArgumentNullException(nameof(catalogIdentity));
            ValidateCollectionIdentity(
                catalogIdentity,
                parentIdentity,
                collectionKind,
                itemKind);
            ParentIdentity = parentIdentity;
            CollectionKind = collectionKind;
            ItemKind = itemKind;
        }

        public Catalog CatalogIdentity { get; }

        public ResourceExplanationIdentity? ParentIdentity { get; }

        public ResourceExplanationNavigationCollectionKind CollectionKind
        {
            get;
        }

        public string? ItemKind { get; }

        public override ResourceExplanationOwner Owner =>
            ResourceExplanationOwner.ResourceExplanation;

        private static void ValidateCollectionIdentity(
            Catalog catalogIdentity,
            ResourceExplanationIdentity? parentIdentity,
            ResourceExplanationNavigationCollectionKind collectionKind,
            string? itemKind)
        {
            bool valid = collectionKind switch
            {
                ResourceExplanationNavigationCollectionKind
                    .CatalogCategories
                    or ResourceExplanationNavigationCollectionKind
                        .CatalogSections =>
                    parentIdentity is null && itemKind is null,
                ResourceExplanationNavigationCollectionKind
                    .StructuralItems =>
                    parentIdentity
                        is Structural
                        {
                            Resource.Kind:
                                DiscoveryResourceKind.Section,
                        }
                    && itemKind is null,
                ResourceExplanationNavigationCollectionKind
                    .StructuralItemKind =>
                    parentIdentity
                        is NavigationCollection
                        {
                            CollectionKind:
                                ResourceExplanationNavigationCollectionKind
                                    .StructuralItems,
                        } parent
                    && parent.CatalogIdentity == catalogIdentity
                    && !string.IsNullOrWhiteSpace(itemKind),
                _ => false,
            };
            if (!valid)
            {
                throw new ArgumentException(
                    "The collection kind, parent identity, and item kind "
                    + "must describe one valid navigation collection.",
                    nameof(collectionKind));
            }
        }
    }

    public sealed record Structural : ResourceExplanationIdentity
    {
        public Structural(DiscoveryResourceIdentity resource)
        {
            Resource =
                resource ?? throw new ArgumentNullException(nameof(resource));
        }

        public DiscoveryResourceIdentity Resource { get; }

        public override ResourceExplanationOwner Owner =>
            ResourceExplanationOwner.SchemaQuery;
    }
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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(ResourceExplanationDetail.CatalogDetails),
    "catalog")]
[JsonDerivedType(
    typeof(ResourceExplanationDetail.NavigationCollectionDetails),
    "navigationCollection")]
[JsonDerivedType(
    typeof(ResourceExplanationDetail.StructuralCategoryDetails),
    "structuralCategory")]
[JsonDerivedType(
    typeof(ResourceExplanationDetail.StructuralSectionDetails),
    "structuralSection")]
[JsonDerivedType(
    typeof(ResourceExplanationDetail.StructuralItemDetails),
    "structuralItem")]
public abstract record ResourceExplanationDetail
{
    private ResourceExplanationDetail()
    {
    }

    public abstract string Name { get; }

    public sealed record CatalogDetails : ResourceExplanationDetail
    {
        public CatalogDetails(string name, int entryCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentOutOfRangeException.ThrowIfNegative(entryCount);
            Name = name;
            EntryCount = entryCount;
        }

        public override string Name { get; }

        public int EntryCount { get; }
    }

    public sealed record NavigationCollectionDetails :
        ResourceExplanationDetail
    {
        public NavigationCollectionDetails(
            string name,
            int memberCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentOutOfRangeException.ThrowIfNegative(memberCount);
            Name = name;
            MemberCount = memberCount;
        }

        public override string Name { get; }

        public int MemberCount { get; }
    }

    public sealed record StructuralCategoryDetails :
        ResourceExplanationDetail
    {
        public StructuralCategoryDetails(
            string name,
            ImmutableArray<DiscoveryOutputMode> outputModes,
            int memberCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentOutOfRangeException.ThrowIfNegative(memberCount);
            Name = name;
            OutputModes = NormalizeOutputModes(outputModes);
            MemberCount = memberCount;
        }

        public override string Name { get; }

        public ImmutableArray<DiscoveryOutputMode> OutputModes { get; }

        public int MemberCount { get; }
    }

    public sealed record StructuralSectionDetails :
        ResourceExplanationDetail
    {
        public StructuralSectionDetails(
            string name,
            ImmutableArray<DiscoveryOutputMode> outputModes,
            int memberCount)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentOutOfRangeException.ThrowIfNegative(memberCount);
            Name = name;
            OutputModes = NormalizeOutputModes(outputModes);
            MemberCount = memberCount;
        }

        public override string Name { get; }

        public ImmutableArray<DiscoveryOutputMode> OutputModes { get; }

        public int MemberCount { get; }
    }

    public sealed record StructuralItemDetails :
        ResourceExplanationDetail
    {
        public StructuralItemDetails(string name, string itemKind)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(itemKind);
            Name = name;
            ItemKind = itemKind;
        }

        public override string Name { get; }

        public string ItemKind { get; }
    }

    private static ImmutableArray<DiscoveryOutputMode> NormalizeOutputModes(
        ImmutableArray<DiscoveryOutputMode> outputModes)
    {
        ImmutableArray<DiscoveryOutputMode> normalized =
            outputModes.IsDefault ? [] : outputModes;
        if (normalized.Distinct().Count() != normalized.Length)
        {
            throw new ArgumentException(
                "Explanation output modes must be unique.",
                nameof(outputModes));
        }
        return normalized;
    }
}

public sealed record ResourceExplanationResource
{
    public ResourceExplanationResource(
        ResourcePath path,
        ResourceExplanationIdentity identity,
        ResourceExplanationResourceKind resourceKind,
        ResourceExplanationDetail details)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Identity =
            identity ?? throw new ArgumentNullException(nameof(identity));
        Details =
            details ?? throw new ArgumentNullException(nameof(details));
        ValidateKind(identity, resourceKind, details);

        ResourceKind = resourceKind;
    }

    public ResourcePath Path { get; }

    public ResourceExplanationIdentity Identity { get; }

    public ResourceExplanationResourceKind ResourceKind { get; }

    public ResourceExplanationOwner Owner => Identity.Owner;

    public ResourceExplanationDetail Details { get; }

    private static void ValidateKind(
        ResourceExplanationIdentity identity,
        ResourceExplanationResourceKind resourceKind,
        ResourceExplanationDetail details)
    {
        bool valid = (identity, resourceKind, details) switch
        {
            (
                ResourceExplanationIdentity.Catalog,
                ResourceExplanationResourceKind.Catalog,
                ResourceExplanationDetail.CatalogDetails) => true,
            (
                ResourceExplanationIdentity.NavigationCollection,
                ResourceExplanationResourceKind.NavigationCollection,
                ResourceExplanationDetail.NavigationCollectionDetails) => true,
            (
                ResourceExplanationIdentity.Structural
                {
                    Resource.Kind: DiscoveryResourceKind.Category,
                },
                ResourceExplanationResourceKind.StructuralCategory,
                ResourceExplanationDetail.StructuralCategoryDetails) => true,
            (
                ResourceExplanationIdentity.Structural
                {
                    Resource.Kind: DiscoveryResourceKind.Section,
                },
                ResourceExplanationResourceKind.StructuralSection,
                ResourceExplanationDetail.StructuralSectionDetails) => true,
            (
                ResourceExplanationIdentity.Structural
                {
                    Resource.Kind: DiscoveryResourceKind.Item,
                },
                ResourceExplanationResourceKind.StructuralItem,
                ResourceExplanationDetail.StructuralItemDetails) => true,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "The resource kind does not match its typed identity.",
                nameof(resourceKind));
        }
    }
}

public sealed record ResourceExplanationRelationship
{
    public ResourceExplanationRelationship(
        ResourceExplanationIdentity source,
        ResourceExplanationRelationshipKind relationshipKind,
        ResourceExplanationIdentity target,
        ResourcePath? targetPath)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        RelationshipKind = relationshipKind;
        Target = target ?? throw new ArgumentNullException(nameof(target));
        TargetPath = targetPath;
    }

    public ResourceExplanationIdentity Source { get; }

    public ResourceExplanationOwner SourceOwner => Source.Owner;

    public ResourceExplanationRelationshipKind RelationshipKind { get; }

    public ResourceExplanationIdentity Target { get; }

    public ResourceExplanationOwner TargetOwner => Target.Owner;

    public ResourcePath? TargetPath { get; }
}

public sealed record ResourceExplanationRequest
{
    public ResourceExplanationRequest(
        int depth,
        int resourceLimit,
        int relationshipLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        ArgumentOutOfRangeException.ThrowIfLessThan(resourceLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            relationshipLimit,
            1);

        Depth = depth;
        ResourceLimit = resourceLimit;
        RelationshipLimit = relationshipLimit;
    }

    public int Depth { get; }

    public int ResourceLimit { get; }

    public int RelationshipLimit { get; }
}

public sealed record ResourceExplanationTraversalReceipt
{
    public ResourceExplanationTraversalReceipt(
        int requestedDepth,
        int requestedResourceLimit,
        int requestedRelationshipLimit,
        int completedDepth,
        int visitedResourceCount,
        int emittedRelationshipCount,
        ResourceExplanationCompleteness completeness,
        IEnumerable<ResourceExplanationTruncationReason>? truncationReasons)
        : this(
            requestedDepth,
            requestedResourceLimit,
            requestedRelationshipLimit,
            completedDepth,
            visitedResourceCount,
            emittedRelationshipCount,
            completeness,
            (truncationReasons ?? []).ToImmutableArray())
    {
    }

    [JsonConstructor]
    public ResourceExplanationTraversalReceipt(
        int requestedDepth,
        int requestedResourceLimit,
        int requestedRelationshipLimit,
        int completedDepth,
        int visitedResourceCount,
        int emittedRelationshipCount,
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
        ArgumentOutOfRangeException.ThrowIfNegative(completedDepth);
        ArgumentOutOfRangeException.ThrowIfNegative(visitedResourceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(
            emittedRelationshipCount);
        if (completedDepth > requestedDepth)
        {
            throw new ArgumentException(
                "Completed depth cannot exceed requested depth.",
                nameof(completedDepth));
        }
        if (visitedResourceCount > requestedResourceLimit)
        {
            throw new ArgumentException(
                "Visited resources cannot exceed the requested limit.",
                nameof(visitedResourceCount));
        }
        if (emittedRelationshipCount > requestedRelationshipLimit)
        {
            throw new ArgumentException(
                "Emitted relationships cannot exceed the requested limit.",
                nameof(emittedRelationshipCount));
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
        CompletedDepth = completedDepth;
        VisitedResourceCount = visitedResourceCount;
        EmittedRelationshipCount = emittedRelationshipCount;
        Completeness = completeness;
        TruncationReasons = reasons;
    }

    public int RequestedDepth { get; }

    public int RequestedResourceLimit { get; }

    public int RequestedRelationshipLimit { get; }

    public int CompletedDepth { get; }

    public int VisitedResourceCount { get; }

    public int EmittedRelationshipCount { get; }

    public ResourceExplanationCompleteness Completeness { get; }

    public ImmutableArray<ResourceExplanationTruncationReason>
        TruncationReasons { get; }
}

public sealed record ResourceExplanationDocument
{
    public const int CurrentSchemaVersion = 1;

    public ResourceExplanationDocument(
        ResourcePath requestedPath,
        ResourceExplanationIdentity rootIdentity,
        IEnumerable<ResourceExplanationResource> resources,
        IEnumerable<ResourceExplanationRelationship> relationships,
        ResourceExplanationTraversalReceipt traversal)
        : this(
            CurrentSchemaVersion,
            requestedPath,
            rootIdentity,
            (resources ?? throw new ArgumentNullException(nameof(resources)))
                .ToImmutableArray(),
            (relationships
                ?? throw new ArgumentNullException(nameof(relationships)))
                .ToImmutableArray(),
            traversal)
    {
    }

    [JsonConstructor]
    public ResourceExplanationDocument(
        int schemaVersion,
        ResourcePath requestedPath,
        ResourceExplanationIdentity rootIdentity,
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
        RequestedPath =
            requestedPath
            ?? throw new ArgumentNullException(nameof(requestedPath));
        RootIdentity =
            rootIdentity
            ?? throw new ArgumentNullException(nameof(rootIdentity));
        Resources = resources.IsDefault ? [] : resources;
        Relationships = relationships.IsDefault ? [] : relationships;
        Traversal =
            traversal ?? throw new ArgumentNullException(nameof(traversal));
        if (Resources.IsEmpty || Resources[0].Identity != RootIdentity)
        {
            throw new ArgumentException(
                "The first explanation resource must be the root identity.",
                nameof(resources));
        }
        if (Resources[0].Path != RequestedPath)
        {
            throw new ArgumentException(
                "The requested path must identify the first resource.",
                nameof(requestedPath));
        }
        if (Resources.Select(static resource => resource.Identity)
                .Distinct()
                .Count()
            != Resources.Length
            || Resources.Select(static resource => resource.Path.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
            != Resources.Length)
        {
            throw new ArgumentException(
                "Explanation resources must have unique identities and paths.",
                nameof(resources));
        }
        if (Traversal.VisitedResourceCount != Resources.Length
            || Traversal.EmittedRelationshipCount != Relationships.Length)
        {
            throw new ArgumentException(
                "The traversal receipt must describe the emitted graph.",
                nameof(traversal));
        }

        HashSet<ResourceExplanationIdentity> included =
            [.. Resources.Select(static resource => resource.Identity)];
        if (Relationships.Any(relationship =>
                !included.Contains(relationship.Source)))
        {
            throw new ArgumentException(
                "Every emitted relationship source must be an emitted "
                + "resource.",
                nameof(relationships));
        }
    }

    public int SchemaVersion { get; }

    public ResourcePath RequestedPath { get; }

    public ResourceExplanationIdentity RootIdentity { get; }

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
        ResourceExplanationIdentity Identity) :
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
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ResourceExplanationDocument))]
public partial class ResourceExplanationJsonContext : JsonSerializerContext;
