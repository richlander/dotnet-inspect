using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(JsonStringEnumConverter<DiscoveryResourceKind>))]
public enum DiscoveryResourceKind
{
    Category,
    Section,
    Item,
}

[JsonConverter(typeof(JsonStringEnumConverter<DiscoveryOutputMode>))]
public enum DiscoveryOutputMode
{
    Markdown,
    PlainText,
    Json,
    Table,
    Tsv,
    Jsonl,
    Tree,
    Mermaid,
}

public sealed record DiscoveryResourceIdentity
{
    [JsonConstructor]
    public DiscoveryResourceIdentity(
        DiscoveryResourceKind kind,
        string name,
        string? section = null,
        string? itemKind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (kind == DiscoveryResourceKind.Item)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(section);
            ArgumentException.ThrowIfNullOrWhiteSpace(itemKind);
        }
        else if (section is not null || itemKind is not null)
            throw new ArgumentException(
                "Only item identities have an owning section and item kind.",
                nameof(section));

        Kind = kind;
        Name = name;
        Section = section;
        ItemKind = itemKind;
    }

    public DiscoveryResourceKind Kind { get; }

    public string Name { get; }

    public string? Section { get; }

    public string? ItemKind { get; }
}

public sealed record DiscoveryResource
{
    public DiscoveryResource(
        DiscoveryResourceIdentity identity,
        IEnumerable<DiscoveryResourceIdentity>? members = null,
        IEnumerable<DiscoveryOutputMode>? outputModes = null,
        SectionCardinalityDeclaration? cardinality = null)
        : this(
            identity,
            (members ?? []).ToImmutableArray(),
            (outputModes ?? []).ToImmutableArray(),
            cardinality)
    {
    }

    [JsonConstructor]
    public DiscoveryResource(
        DiscoveryResourceIdentity identity,
        ImmutableArray<DiscoveryResourceIdentity> members,
        ImmutableArray<DiscoveryOutputMode> outputModes,
        SectionCardinalityDeclaration? cardinality = null)
    {
        Identity =
            identity ?? throw new ArgumentNullException(nameof(identity));
        if (identity.Kind == DiscoveryResourceKind.Item
            && !members.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "Item resources cannot contain members.",
                nameof(members));
        }
        if (identity.Kind != DiscoveryResourceKind.Section
            && cardinality is not null)
        {
            throw new ArgumentException(
                "Only section resources can declare semantic cardinality.",
                nameof(cardinality));
        }

        Members = members.IsDefault ? [] : members;
        OutputModes = outputModes.IsDefault ? [] : outputModes;
        Cardinality = cardinality;
        if (Members.Distinct().Count() != Members.Length)
        {
            throw new ArgumentException(
                "A discovery resource cannot contain duplicate members.",
                nameof(members));
        }
        if (OutputModes.Distinct().Count() != OutputModes.Length)
        {
            throw new ArgumentException(
                "A discovery resource cannot contain duplicate output modes.",
                nameof(outputModes));
        }

    }

    public DiscoveryResourceIdentity Identity { get; }

    public ImmutableArray<DiscoveryResourceIdentity> Members { get; }

    public ImmutableArray<DiscoveryOutputMode> OutputModes { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SectionCardinalityDeclaration? Cardinality { get; }
}

public sealed record DiscoverySelection
{
    public DiscoverySelection(
        bool isCatalog,
        IEnumerable<DiscoveryResourceIdentity>? addressedResources,
        IEnumerable<DiscoveryResourceIdentity>? rows)
        : this(
            isCatalog,
            (addressedResources ?? []).ToImmutableArray(),
            (rows ?? []).ToImmutableArray())
    {
    }

    [JsonConstructor]
    public DiscoverySelection(
        bool isCatalog,
        ImmutableArray<DiscoveryResourceIdentity> addressedResources,
        ImmutableArray<DiscoveryResourceIdentity> rows)
    {
        AddressedResources =
            addressedResources.IsDefault ? [] : addressedResources;
        Rows = rows.IsDefault ? [] : rows;
        if (isCatalog && !AddressedResources.IsEmpty)
        {
            throw new ArgumentException(
                "A catalog selection cannot address individual resources.",
                nameof(addressedResources));
        }
        if (!isCatalog && AddressedResources.IsEmpty)
        {
            throw new ArgumentException(
                "A resource selection must address at least one resource.",
                nameof(addressedResources));
        }

        IsCatalog = isCatalog;
    }

    public bool IsCatalog { get; }

    public ImmutableArray<DiscoveryResourceIdentity> AddressedResources
    {
        get;
    }

    public ImmutableArray<DiscoveryResourceIdentity> Rows { get; }
}

public sealed record DiscoveryDocument
{
    public DiscoveryDocument(
        string catalog,
        IEnumerable<DiscoveryResource> resources,
        IEnumerable<DiscoveryResourceIdentity> catalogEntries,
        DiscoverySelection selection)
        : this(
            catalog,
            (resources
                ?? throw new ArgumentNullException(nameof(resources)))
                .ToImmutableArray(),
            (catalogEntries
                ?? throw new ArgumentNullException(nameof(catalogEntries)))
                .ToImmutableArray(),
            selection)
    {
    }

    [JsonConstructor]
    public DiscoveryDocument(
        string catalog,
        ImmutableArray<DiscoveryResource> resources,
        ImmutableArray<DiscoveryResourceIdentity> catalogEntries,
        DiscoverySelection selection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        Catalog = catalog;
        Resources = resources.IsDefault ? [] : resources;
        CatalogEntries = catalogEntries.IsDefault ? [] : catalogEntries;
        Selection =
            selection ?? throw new ArgumentNullException(nameof(selection));

        var resourcesByIdentity =
            new Dictionary<DiscoveryResourceIdentity, DiscoveryResource>();
        foreach (DiscoveryResource resource in Resources)
        {
            if (!resourcesByIdentity.TryAdd(resource.Identity, resource))
            {
                throw new ArgumentException(
                    $"Duplicate discovery resource '{resource.Identity}'.",
                    nameof(resources));
            }
        }

        ValidateReferences(
            CatalogEntries,
            resourcesByIdentity,
            nameof(catalogEntries));
        if (CatalogEntries.Distinct().Count() != CatalogEntries.Length
            || CatalogEntries.Any(identity =>
                identity.Kind == DiscoveryResourceKind.Item))
        {
            throw new ArgumentException(
                "Catalog entries must be unique categories or sections.",
                nameof(catalogEntries));
        }
        ValidateReferences(
            Selection.AddressedResources,
            resourcesByIdentity,
            nameof(selection));
        ValidateReferences(
            Selection.Rows,
            resourcesByIdentity,
            nameof(selection));

        foreach (DiscoveryResource resource in Resources)
        {
            ValidateReferences(
                resource.Members,
                resourcesByIdentity,
                nameof(resources));
            ValidateMemberKinds(resource);
        }
    }

    public string Catalog { get; }

    public ImmutableArray<DiscoveryResource> Resources { get; }

    public ImmutableArray<DiscoveryResourceIdentity> CatalogEntries { get; }

    public DiscoverySelection Selection { get; }

    public DiscoveryResource GetResource(
        DiscoveryResourceIdentity identity) =>
        Resources.First(resource => resource.Identity == identity);

    private static void ValidateReferences(
        ImmutableArray<DiscoveryResourceIdentity> references,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            DiscoveryResource> resources,
        string parameterName)
    {
        foreach (DiscoveryResourceIdentity reference in references)
        {
            if (!resources.ContainsKey(reference))
            {
                throw new ArgumentException(
                    $"Unknown discovery resource reference '{reference}'.",
                    parameterName);
            }
        }
    }

    private static void ValidateMemberKinds(DiscoveryResource resource)
    {
        foreach (DiscoveryResourceIdentity member in resource.Members)
        {
            if (resource.Identity.Kind == DiscoveryResourceKind.Category
                && member.Kind != DiscoveryResourceKind.Section)
            {
                throw new ArgumentException(
                    "Category members must reference sections.",
                    nameof(Resources));
            }
            if (resource.Identity.Kind == DiscoveryResourceKind.Section
                && (member.Kind != DiscoveryResourceKind.Item
                    || !string.Equals(
                        member.Section,
                        resource.Identity.Name,
                        StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "Section members must reference items owned by that section.",
                    nameof(Resources));
            }
        }
    }
}
