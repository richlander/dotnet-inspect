using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

public class ResourceExplanationTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Request_RequiresPositiveTraversalLimits(
        int resourceLimit,
        int relationshipLimit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceExplanationRequest(
                depth: 0,
                resourceLimit,
                relationshipLimit));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void TraversalReceipt_RequiresPositiveRequestedLimits(
        int resourceLimit,
        int relationshipLimit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceExplanationTraversalReceipt(
                requestedDepth: 0,
                requestedResourceLimit: resourceLimit,
                requestedRelationshipLimit: relationshipLimit,
                completedDepth: 0,
                visitedResourceCount: 0,
                emittedRelationshipCount: 0,
                ResourceExplanationCompleteness.Complete,
                truncationReasons: []));
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("_private")]
    [InlineData("-option")]
    public void ResourcePath_RejectsLeadingPunctuation(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            new ResourcePath(value));
        Assert.Throws<ArgumentException>(() =>
            new ResourcePath("library").Append(value));
        Assert.False(
            ResourcePath.TryCreate(
                value,
                out ResourcePath? path,
                out string? error));
        Assert.Null(path);
        Assert.NotNull(error);

        Assert.IsType<ResourcePathResolution.Invalid>(
            StructuralCatalog().Resolve(value));
    }

    [Fact]
    public void StructuralCatalog_ProjectsTheCompleteAuthoritativeDomain()
    {
        (DiscoveryDocument discovery, StructuralResourcePathRegistration[]
            registrations) = StructuralFixture();

        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateStructural(
                discovery,
                registrations);

        ResourceExplanationResource[] structural =
        [
            .. catalog.Resources.Where(static resource =>
                resource.Identity
                is ResourceExplanationIdentity.Structural),
        ];
        Assert.Equal(discovery.Resources.Length, structural.Length);
        Assert.All(
            discovery.Resources,
            resource => Assert.Single(
                structural,
                candidate =>
                    candidate.Identity
                    is ResourceExplanationIdentity.Structural identity
                    && identity.Resource == resource.Identity));

        int categoryMembers = discovery.Resources
            .Where(static resource =>
                resource.Identity.Kind
                == DiscoveryResourceKind.Category)
            .Sum(static resource => resource.Members.Length);
        int sectionItems = discovery.Resources
            .Where(static resource =>
                resource.Identity.Kind
                == DiscoveryResourceKind.Section)
            .Sum(static resource => resource.Members.Length);
        Assert.Equal(
            categoryMembers,
            catalog.Relationships.Count(static relationship =>
                relationship.RelationshipKind
                == ResourceExplanationRelationshipKind.CategoryMember));
        Assert.Equal(
            sectionItems,
            catalog.Relationships.Count(static relationship =>
                relationship.RelationshipKind
                == ResourceExplanationRelationshipKind.StructuralItem));

        ResourceExplanationResource filterable = Assert.Single(
            structural,
            resource =>
                resource.Details
                    is ResourceExplanationDetail.StructuralItemDetails
                    {
                        ItemKind: "filterable",
                    });
        Assert.Equal(
            ResourceExplanationResourceKind.StructuralItem,
            filterable.ResourceKind);
        Assert.Equal(ResourceExplanationOwner.SchemaQuery, filterable.Owner);
    }

    [Fact]
    public void StructuralCatalog_CollectionCountsMatchDirectMembers()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();

        foreach (ResourceExplanationResource collection
                 in catalog.Resources.Where(static resource =>
                     resource.Identity
                     is ResourceExplanationIdentity.NavigationCollection))
        {
            var details =
                Assert.IsType<
                    ResourceExplanationDetail.NavigationCollectionDetails>(
                    collection.Details);
            int directMembers = catalog.Relationships.Count(
                relationship =>
                    relationship.Source == collection.Identity
                    && relationship.RelationshipKind
                    == ResourceExplanationRelationshipKind.CollectionMember);

            Assert.Equal(details.MemberCount, directMembers);
        }
    }

    [Fact]
    public void StructuralCatalog_RejectsMissingAndExtraRegistrations()
    {
        (DiscoveryDocument discovery, StructuralResourcePathRegistration[]
            registrations) = StructuralFixture();

        Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.CreateStructural(
                discovery,
                registrations.Skip(1)));
        Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.CreateStructural(
                discovery,
                [
                    .. registrations,
                    new(
                        Section("Extra"),
                        new ResourcePath(
                            "library/sections/extra")),
                ]));
    }

    [Fact]
    public void StructuralCatalog_RejectsCaseInsensitivePathCollisions()
    {
        (DiscoveryDocument discovery, StructuralResourcePathRegistration[]
            registrations) = StructuralFixture();
        StructuralResourcePathRegistration first = registrations[0];
        StructuralResourcePathRegistration second = registrations[1];

        Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.CreateStructural(
                discovery,
                [
                    first,
                    new(second.Identity, first.Path),
                    .. registrations.Skip(2),
                ]));
    }

    [Theory]
    [InlineData(DiscoveryResourceKind.Category)]
    [InlineData(DiscoveryResourceKind.Section)]
    [InlineData(DiscoveryResourceKind.Item)]
    public void StructuralCatalog_RejectsExtraHierarchySegments(
        DiscoveryResourceKind kind)
    {
        (DiscoveryDocument discovery, StructuralResourcePathRegistration[]
            registrations) = StructuralFixture();
        int index = Array.FindIndex(
            registrations,
            registration => registration.Identity.Kind == kind);
        StructuralResourcePathRegistration registration =
            registrations[index];
        registrations[index] = new(
            registration.Identity,
            new ResourcePath($"{registration.Path.Value}/extra"));

        Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.CreateStructural(
                discovery,
                registrations));
    }

    [Fact]
    public void Resource_RejectsMismatchedTypedDetailVariant()
    {
        Assert.Throws<ArgumentException>(() =>
            new ResourceExplanationResource(
                new ResourcePath("library/sections/references"),
                new ResourceExplanationIdentity.Structural(
                    Section("References")),
                ResourceExplanationResourceKind.StructuralSection,
                new ResourceExplanationDetail.StructuralItemDetails(
                    "Name",
                    "column")));
    }

    [Fact]
    public void Catalog_RequiresRegisteredTargetPathInBothDirections()
    {
        ResourceExplanationResource root =
            Collection("graph/root", "Root");
        ResourceExplanationResource target =
            Collection("graph/target", "Target");

        Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.Create(
                [root, target],
                [
                    new(
                        root.Identity,
                        ResourceExplanationRelationshipKind.Navigation,
                        target.Identity,
                        targetPath: null),
                ]));

        var external =
            new ResourceExplanationIdentity.Catalog("external");
        Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.Create(
                [root, target],
                [
                    new(
                        root.Identity,
                        ResourceExplanationRelationshipKind.Navigation,
                        external,
                        target.Path),
                ]));
    }

    [Fact]
    public void DefaultDepth_EmitsRootFactsAndDirectLinksOnly()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library/sections/reference-hierarchy"));

        ResourceExplanationDocument document =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 0,
                    resourceLimit: 100,
                    relationshipLimit: 100))
                .Content;

        Assert.Single(document.Resources);
        Assert.NotEmpty(document.Relationships);
        Assert.All(
            document.Relationships,
            relationship => Assert.Equal(
                resolved.Identity,
                relationship.Source));
        Assert.Equal(
            ResourceExplanationCompleteness.Truncated,
            document.Traversal.Completeness);
        Assert.Contains(
            ResourceExplanationTruncationReason.Depth,
            document.Traversal.TruncationReasons);
    }

    [Fact]
    public void RecursiveTraversal_PreservesCycleEdgesAndVisitsOnce()
    {
        ResourceExplanationResource left =
            Collection("graph/left", "Left");
        ResourceExplanationResource right =
            Collection("graph/right", "Right");
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.Create(
                [left, right],
                [
                    new(
                        left.Identity,
                        ResourceExplanationRelationshipKind.Navigation,
                        right.Identity,
                        right.Path),
                    new(
                        right.Identity,
                        ResourceExplanationRelationshipKind.Navigation,
                        left.Identity,
                        left.Path),
                ]);
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve(left.Path.Value));

        ResourceExplanationDocument document =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 4,
                    resourceLimit: 10,
                    relationshipLimit: 10))
                .Content;

        Assert.Equal(2, document.Resources.Length);
        Assert.Equal(2, document.Relationships.Length);
        Assert.Equal(
            ResourceExplanationCompleteness.Complete,
            document.Traversal.Completeness);
    }

    [Fact]
    public void Traversal_ReportsResourceAndRelationshipLimits()
    {
        ResourceExplanationResource root =
            Collection("graph/root", "Root");
        ResourceExplanationResource left =
            Collection("graph/left", "Left");
        ResourceExplanationResource right =
            Collection("graph/right", "Right");
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.Create(
                [root, left, right],
                [
                    new(
                        root.Identity,
                        ResourceExplanationRelationshipKind.Navigation,
                        left.Identity,
                        left.Path),
                    new(
                        root.Identity,
                        ResourceExplanationRelationshipKind.Navigation,
                        right.Identity,
                        right.Path),
                ]);
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve(root.Path.Value));

        ResourceExplanationDocument resourceLimited =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 1,
                    resourceLimit: 2,
                    relationshipLimit: 10))
                .Content;
        Assert.Contains(
            ResourceExplanationTruncationReason.ResourceLimit,
            resourceLimited.Traversal.TruncationReasons);

        ResourceExplanationDocument relationshipLimited =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 1,
                    resourceLimit: 10,
                    relationshipLimit: 1))
                .Content;
        Assert.Contains(
            ResourceExplanationTruncationReason.RelationshipLimit,
            relationshipLimited.Traversal.TruncationReasons);
        Assert.Single(relationshipLimited.Relationships);
    }

    [Fact]
    public void Resolve_RejectsInvalidPathsAndBoundsSuggestions()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();

        ResourcePathResolution.Invalid invalid =
            Assert.IsType<ResourcePathResolution.Invalid>(
                catalog.Resolve("Library/Sections"));
        Assert.Contains("lower-case", invalid.Reason);

        ResourcePathResolution.Unknown unknown =
            Assert.IsType<ResourcePathResolution.Unknown>(
                catalog.Resolve(
                    "library/sections/reference-hierarch"));
        Assert.InRange(unknown.Suggestions.Length, 1, 5);
        Assert.Contains(
            unknown.Suggestions,
            path =>
                path.Value
                == "library/sections/reference-hierarchy");
    }

    [Fact]
    public void Json_SerializesCanonicalPathsAsStrings()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library/sections/reference-hierarchy"));
        ResourceExplanationDocument explanation =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(0, 100, 100))
                .Content;

        string json = JsonSerializer.Serialize(
            explanation,
            ResourceExplanationJsonContext
                .Default
                .ResourceExplanationDocument);
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Equal(
            "library/sections/reference-hierarchy",
            document.RootElement
                .GetProperty("requested_path")
                .GetString());
        Assert.Equal(
            "library/sections/reference-hierarchy",
            document.RootElement
                .GetProperty("resources")[0]
                .GetProperty("path")
                .GetString());
    }

    [Fact]
    public void Json_SeparatesNavigationCollectionIdentityFromPath()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library/sections"));
        ResourceExplanationDocument explanation =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(0, 100, 100))
                .Content;

        string json = JsonSerializer.Serialize(
            explanation,
            ResourceExplanationJsonContext
                .Default
                .ResourceExplanationDocument);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement identity =
            document.RootElement.GetProperty("root_identity");

        Assert.Equal(
            "navigationCollection",
            identity.GetProperty("kind").GetString());
        Assert.Equal(
            "library",
            identity
                .GetProperty("catalog_identity")
                .GetProperty("catalog_name")
                .GetString());
        Assert.Equal(
            "CatalogSections",
            identity.GetProperty("collection_kind").GetString());
        Assert.False(identity.TryGetProperty("key", out _));
    }

    [Fact]
    public void Json_SourceGeneratedContract_RoundTripsPolymorphicDocument()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library"));
        ResourceExplanationDocument explanation =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(4, 1000, 2000))
                .Content;

        string json = JsonSerializer.Serialize(
            explanation,
            ResourceExplanationJsonContext
                .Default
                .ResourceExplanationDocument);
        ResourceExplanationDocument roundTripped =
            JsonSerializer.Deserialize(
                json,
                ResourceExplanationJsonContext
                    .Default
                    .ResourceExplanationDocument)!;
        string roundTrippedJson = JsonSerializer.Serialize(
            roundTripped,
            ResourceExplanationJsonContext
                .Default
                .ResourceExplanationDocument);

        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse(json),
                JsonNode.Parse(roundTrippedJson)));
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.Identity
                    is ResourceExplanationIdentity.Catalog);
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.Identity
                    is ResourceExplanationIdentity.NavigationCollection);
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.Identity
                    is ResourceExplanationIdentity.Structural);
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.Details
                    is ResourceExplanationDetail.CatalogDetails);
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.Details
                    is ResourceExplanationDetail
                        .NavigationCollectionDetails);
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.Details
                    is ResourceExplanationDetail
                        .StructuralCategoryDetails);
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.Details
                    is ResourceExplanationDetail
                        .StructuralSectionDetails);
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.Details
                    is ResourceExplanationDetail
                        .StructuralItemDetails);
    }

    private static ResourceExplanationCatalog StructuralCatalog()
    {
        (DiscoveryDocument discovery, StructuralResourcePathRegistration[]
            registrations) = StructuralFixture();
        return ResourceExplanationCatalog.CreateStructural(
            discovery,
            registrations);
    }

    private static (
        DiscoveryDocument Document,
        StructuralResourcePathRegistration[] Registrations)
        StructuralFixture()
    {
        DiscoveryResourceIdentity category = Category("@Dependencies");
        DiscoveryResourceIdentity references = Section("References");
        DiscoveryResourceIdentity hierarchy =
            Section("Reference Hierarchy");
        DiscoveryResourceIdentity referenceName =
            Item("References", "Name", "column");
        DiscoveryResourceIdentity hierarchyName =
            Item("Reference Hierarchy", "Name", "column");
        DiscoveryResourceIdentity hierarchyFilter =
            Item("Reference Hierarchy", "Name", "filterable");
        var document =
            new DiscoveryDocument(
                "library",
                [
                    new(
                        category,
                        members: [references, hierarchy]),
                    new(
                        references,
                        members: [referenceName],
                        outputModes: [DiscoveryOutputMode.Markdown]),
                    new(
                        referenceName),
                    new(
                        hierarchy,
                        members: [hierarchyName, hierarchyFilter],
                        outputModes:
                        [
                            DiscoveryOutputMode.Markdown,
                            DiscoveryOutputMode.Json,
                        ]),
                    new(
                        hierarchyName),
                    new(
                        hierarchyFilter),
                ],
                [category, references, hierarchy],
                new DiscoverySelection(
                    isCatalog: true,
                    addressedResources: [],
                    rows: [category, references, hierarchy]));
        StructuralResourcePathRegistration[] registrations =
        [
            new(
                category,
                new ResourcePath(
                    "library/categories/dependencies")),
            new(
                references,
                new ResourcePath(
                    "library/sections/references")),
            new(
                referenceName,
                new ResourcePath(
                    "library/sections/references/items/column/name")),
            new(
                hierarchy,
                new ResourcePath(
                    "library/sections/reference-hierarchy")),
            new(
                hierarchyName,
                new ResourcePath(
                    "library/sections/reference-hierarchy/items/column/name")),
            new(
                hierarchyFilter,
                new ResourcePath(
                    "library/sections/reference-hierarchy/items/filterable/name")),
        ];
        return (document, registrations);
    }

    private static ResourceExplanationResource Collection(
        string path,
        string name)
    {
        var resourcePath = new ResourcePath(path);
        return new ResourceExplanationResource(
            resourcePath,
            new ResourceExplanationIdentity.NavigationCollection(
                new ResourceExplanationIdentity.Catalog(
                    $"test-{name.ToLowerInvariant()}"),
                parentIdentity: null,
                ResourceExplanationNavigationCollectionKind.CatalogSections),
            ResourceExplanationResourceKind.NavigationCollection,
            new ResourceExplanationDetail.NavigationCollectionDetails(
                name,
                memberCount: 0));
    }

    private static DiscoveryResourceIdentity Category(string name) =>
        new(DiscoveryResourceKind.Category, name);

    private static DiscoveryResourceIdentity Section(string name) =>
        new(DiscoveryResourceKind.Section, name);

    private static DiscoveryResourceIdentity Item(
        string section,
        string name,
        string itemKind) =>
        new(
            DiscoveryResourceKind.Item,
            name,
            section,
            itemKind);
}
