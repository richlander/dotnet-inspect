using System.Text.Json;
using DotnetInspector.Sections;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections.Tests;

public class ResourceExplanationTests
{
    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    public void Request_RequiresPositiveTraversalLimits(
        int resourceLimit,
        int relationshipLimit,
        int targetLimit,
        int schemaLimit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceExplanationRequest(
                depth: 0,
                resourceLimit,
                relationshipLimit,
                targetLimit,
                schemaLimit));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1, 0)]
    public void TraversalReceipt_RequiresPositiveRequestedLimits(
        int resourceLimit,
        int relationshipLimit,
        int targetLimit,
        int schemaLimit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ResourceExplanationTraversalReceipt(
                requestedDepth: 0,
                requestedResourceLimit: resourceLimit,
                requestedRelationshipLimit: relationshipLimit,
                requestedRelationshipTargetLimit: targetLimit,
                requestedSchemaDeclarationLimit: schemaLimit,
                completedDepth: 0,
                visitedResourceCount: 0,
                emittedRelationshipCount: 0,
                emittedRelationshipTargetCount: 0,
                emittedSchemaDeclarationCount: 0,
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
    public void ExactProbe_ReturnsOnlyRegisteredPaths()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();

        Assert.True(
            catalog.TryResolveExact(
                new("library"),
                out ResourcePathResolution.Resolved? resolved));
        Assert.Equal("library", resolved.Path.Value);
        Assert.Equal("catalog", resolved.Key.ResourceType.Value);
        Assert.False(
            catalog.TryResolveExact(
                new("library/not-real"),
                out resolved));
        Assert.Null(resolved);
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
                resource.Owner.Value == "schema-query"
                && resource.ResourceType.Value != "catalog"),
        ];
        Assert.Equal(discovery.Resources.Length, structural.Length);
        Assert.Equal(
            discovery.Resources.Count(static resource =>
                resource.Identity.Kind
                    == DiscoveryResourceKind.Category),
            structural.Count(static resource =>
                resource.ResourceType.Value == "structural-category"));
        Assert.Equal(
            discovery.Resources.Count(static resource =>
                resource.Identity.Kind
                    == DiscoveryResourceKind.Section),
            structural.Count(static resource =>
                resource.ResourceType.Value == "structural-section"));
        Assert.Equal(
            discovery.Resources.Count(static resource =>
                resource.Identity.Kind
                    == DiscoveryResourceKind.Item),
            structural.Count(static resource =>
                resource.ResourceType.Value == "structural-item"));

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
            TargetCount(catalog, "category-member"));
        Assert.Equal(
            sectionItems,
            TargetCount(catalog, "structural-item"));

        ResourceExplanationResource filterable = Assert.Single(
            structural,
            resource =>
                resource.ResourceType.Value == "structural-item"
                && OptionalText(resource, "item-kind") == "filterable");
        Assert.Equal("schema-query", filterable.Owner.Value);
    }

    [Fact]
    public void StructuralCatalog_CollectionCountsMatchDirectMembers()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();

        foreach (ResourceExplanationResource collection
                 in catalog.Resources.Where(static resource =>
                     resource.ResourceType.Value
                        == "navigation-collection"))
        {
            int directMembers = catalog.Relationships
                .Where(relationship =>
                    relationship.Source == collection.Key)
                .Sum(static relationship =>
                    relationship.Targets.Length);

            Assert.Equal(
                RequiredInteger(collection, "members"),
                directMembers);
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
    public void DefaultDepth_EmitsRootFactsDirectLinksAndSchemaSlice()
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

        ResourceExplanationResource root = Assert.Single(document.Resources);
        Assert.Equal(resolved.Key, root.Key);
        Assert.NotEmpty(root.Facts);
        Assert.NotEmpty(document.Relationships);
        Assert.All(
            document.Relationships,
            relationship => Assert.Equal(
                resolved.Key,
                relationship.Source));
        Assert.Equal(
            ResourceExplanationCompleteness.Truncated,
            document.Traversal.Completeness);
        Assert.Contains(
            ResourceExplanationTruncationReason.Depth,
            document.Traversal.TruncationReasons);
        Assert.Equal(
            document.Traversal.EmittedSchemaDeclarationCount,
            DeclarationCount(document.Schemas));
        Assert.Equal(
            [
                "resource-explanation",
                "schema-query",
                "query-space",
                "analysis-requests",
                "findings",
                "product-vocabulary",
            ],
            document.Schemas.Select(static schema =>
                schema.Identity.Owner.Value));
        Assert.All(
            document.Schemas.SelectMany(static schema =>
                schema.ResourceTypes),
            declaration => Assert.Same(
                catalog.Schemas
                    .SelectMany(static schema => schema.ResourceTypes)
                    .Single(candidate =>
                        candidate.Identity == declaration.Identity),
                declaration));
    }

    [Fact]
    public void RecursiveTraversal_VisitsEachResourceOnce()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library"));

        ResourceExplanationDocument document =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 8,
                    resourceLimit: 100,
                    relationshipLimit: 100))
                .Content;

        Assert.Equal(
            document.Resources.Length,
            document.Resources.Select(static resource => resource.Key)
                .Distinct()
                .Count());
        Assert.All(
            document.Relationships.SelectMany(static relationship =>
                relationship.Targets),
            target => Assert.Contains(
                document.Resources,
                resource => resource.Key == target.Resource));
        Assert.Equal(
            ResourceExplanationCompleteness.Complete,
            document.Traversal.Completeness);
    }

    [Fact]
    public void Traversal_ReportsIndependentResourceRelationshipAndTargetLimits()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library"));

        ResourceExplanationDocument resourceLimited =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 8,
                    resourceLimit: 2,
                    relationshipLimit: 100))
                .Content;
        Assert.Contains(
            ResourceExplanationTruncationReason.ResourceLimit,
            resourceLimited.Traversal.TruncationReasons);

        ResourceExplanationDocument relationshipLimited =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 8,
                    resourceLimit: 100,
                    relationshipLimit: 1))
                .Content;
        Assert.Contains(
            ResourceExplanationTruncationReason.RelationshipLimit,
            relationshipLimited.Traversal.TruncationReasons);
        Assert.Single(relationshipLimited.Relationships);

        ResourceExplanationDocument targetLimited =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 8,
                    resourceLimit: 100,
                    relationshipLimit: 100,
                    relationshipTargetLimit: 1))
                .Content;
        Assert.Contains(
            ResourceExplanationTruncationReason.RelationshipTargetLimit,
            targetLimited.Traversal.TruncationReasons);
        Assert.Equal(
            1,
            targetLimited.Traversal.EmittedRelationshipTargetCount);
        Assert.Contains(
            targetLimited.Relationships,
            static relationship =>
                relationship.TargetCompleteness
                    == ResourceExplanationTargetProjectionCompleteness
                        .Truncated);
    }

    [Fact]
    public void SchemaLimit_RejectsRootClosureThatDoesNotFit()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library/sections/reference-hierarchy"));
        ResourceExplanationDocument complete =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 0,
                    resourceLimit: 1,
                    relationshipLimit: 100))
                .Content;
        int limit =
            complete.Traversal.EmittedSchemaDeclarationCount - 1;

        Assert.Throws<InvalidOperationException>(() =>
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 0,
                    resourceLimit: 1,
                    relationshipLimit: 100,
                    schemaDeclarationLimit: limit)));
    }

    [Fact]
    public void SchemaDeclarationCount_IncludesComposedRelationships()
    {
        var baseSchemas = StructuralCatalog().Schemas;
        ExplanationSchema core = baseSchemas.Single(
            static schema =>
                schema.Identity.Owner.Value == "resource-explanation");
        ExplanationDataShapeIdentity textShape =
            core.DataShapes.Single(
                static shape => shape.Identity.Value == "text").Identity;
        ExplanationPublicAddressKindIdentity pathKind =
            Assert.Single(core.AddressKinds).Identity;
        var owner = new ExplanationOwnerIdentity("test");
        var schemaIdentity =
            new ExplanationSchemaIdentity(owner, "declarations");
        var version = new ExplanationSchemaVersion(1);
        var recordShape =
            new ExplanationDataShapeIdentity(schemaIdentity, "record");
        var choiceShape =
            new ExplanationDataShapeIdentity(schemaIdentity, "choice");
        var field = new ExplanationFieldIdentity(recordShape, "value");
        var @case = new ExplanationChoiceCaseIdentity(choiceShape, "value");
        var resourceType =
            new ExplanationResourceTypeIdentity(schemaIdentity, "resource");
        var targetType =
            new ExplanationResourceTypeIdentity(schemaIdentity, "target");
        var recordFact =
            new ExplanationFactIdentity(resourceType, "record");
        var choiceFact =
            new ExplanationFactIdentity(resourceType, "choice");
        var relationship =
            new ExplanationRelationshipIdentity(resourceType, "target");
        var budget = new ExplanationValueBudget(4096, 4, 16);
        var schema =
            new ExplanationSchema(
                schemaIdentity,
                version,
                [
                    new ExplanationDataShapeDeclaration.Record(
                        recordShape,
                        "Record",
                        "One record.",
                        budget,
                        [
                            new(
                                field,
                                "Value",
                                "One value.",
                                textShape,
                                ExplanationCardinality.RequiredOne),
                        ]),
                    new ExplanationDataShapeDeclaration.Choice(
                        choiceShape,
                        "Choice",
                        "One choice.",
                        budget,
                        [
                            new(
                                @case,
                                "Value",
                                "A choice with one value.",
                                textShape),
                        ]),
                ],
                [
                    new ExplanationResourceTypeDeclaration(
                        resourceType,
                        "Resource",
                        "One test resource.",
                        textShape,
                        [
                            new(
                                recordFact,
                                "Record",
                                "The record fact.",
                                recordShape,
                                ExplanationCardinality.RequiredOne,
                                ExplanationObservationStates.Available),
                            new(
                                choiceFact,
                                "Choice",
                                "The choice fact.",
                                choiceShape,
                                ExplanationCardinality.RequiredOne,
                                ExplanationObservationStates.Available),
                        ],
                        [
                            new(
                                relationship,
                                "Target",
                                "An available-empty target relationship.",
                                targetType,
                                ExplanationCardinality.OrderedMany,
                                ExplanationObservationStates.Available),
                        ],
                        [pathKind]),
                    new ExplanationResourceTypeDeclaration(
                        targetType,
                        "Target",
                        "One target resource.",
                        textShape,
                        addressKinds: [pathKind]),
                ]);
        ExplanationResourceKey key =
            new(owner, resourceType, ExplanationText("root"));
        ExplanationResourceSnapshot snapshot =
            ExplanationConformance.CreateSnapshot(
                [.. baseSchemas, schema],
                key,
                version,
                ExplanationSnapshotScope.Installed,
                [
                    new(pathKind, ExplanationText("test/root")),
                ],
                [
                    new(
                        recordFact,
                        ExplanationObservationState.Available,
                        [
                            new ExplanationValue.Record(
                            [
                                new(field, [ExplanationText("record")]),
                            ]),
                        ]),
                    new(
                        choiceFact,
                        ExplanationObservationState.Available,
                        [
                            new ExplanationValue.Choice(
                                @case,
                                ExplanationText("choice")),
                        ]),
                ],
                [
                    new(
                        relationship,
                        ExplanationObservationState.Available,
                        []),
                ]);
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.Create(
                [.. baseSchemas, schema],
                [snapshot]);
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("test/root"));

        ResourceExplanationDocument document =
            catalog.Explain(
                resolved,
                new(
                    depth: 0,
                    resourceLimit: 1,
                    relationshipLimit: 1))
                .Content;

        JsonElement compact = ResourceExplanationDataProjection.Create(document,
            static path => "inspect-resource:/" + path.Value);
        Assert.Equal("record", compact.GetProperty("facts").GetProperty("record")
            .GetProperty("value").GetString());
        Assert.Equal("value", compact.GetProperty("facts").GetProperty("choice")
            .GetProperty("case").GetString());
        Assert.Equal("choice", compact.GetProperty("facts").GetProperty("choice")
            .GetProperty("value").GetString());
        Assert.Equal("Available", compact.GetProperty("relationships").GetProperty("target")
            .GetProperty("state").GetString());
        Assert.Empty(compact.GetProperty("relationships").GetProperty("target")
            .GetProperty("targets").EnumerateArray());

        Assert.Equal(13, document.Traversal.EmittedSchemaDeclarationCount);
        Assert.Throws<InvalidOperationException>(() =>
            catalog.Explain(
                resolved,
                new(
                    depth: 0,
                    resourceLimit: 1,
                    relationshipLimit: 1,
                    schemaDeclarationLimit: 12)));
    }

    [Fact]
    public void OptionalStructuralFactsDeclareOptionalOne()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library/sections/references"));
        ResourceExplanationDocument document =
            catalog.Explain(
                resolved,
                new(
                    depth: 0,
                    resourceLimit: 1,
                    relationshipLimit: 100))
                .Content;
        ResourceExplanationResource root =
            Assert.Single(document.Resources);

        ExplanationResourceTypeDeclaration declaration =
            document.Schemas
                .SelectMany(static schema => schema.ResourceTypes)
                .Single(static resource =>
                    resource.Identity.Value == "structural-section");
        foreach (string factName in new[] { "shape", "cardinality" })
        {
            Assert.Equal(
                ExplanationObservationState.Absent,
                root.Facts.Single(fact =>
                    fact.Fact.Value == factName).State);
            Assert.Equal(
                ExplanationCardinality.OptionalOne,
                declaration.Facts.Single(fact =>
                    fact.Identity.Value == factName).Cardinality);
        }
    }

    [Fact]
    public void Catalog_RejectsDuplicateTypedAddressValues()
    {
        CatalogAdmissionFixture fixture = CatalogAdmissionFixture.Create();
        ExplanationSchema schema = fixture.Schema("The resource name.");
        ExplanationResourceSnapshot first = fixture.Snapshot(
            schema,
            "first",
            "probe/first",
            "shared");
        ExplanationResourceSnapshot second = fixture.Snapshot(
            schema,
            "second",
            "probe/second",
            "shared");

        Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.Create(
                [.. fixture.BaseSchemas, schema],
                [first, second]));
    }

    [Fact]
    public void Combine_RequiresEquivalentSchemaRevisions()
    {
        CatalogAdmissionFixture fixture = CatalogAdmissionFixture.Create();
        ExplanationSchema firstSchema =
            fixture.Schema("The resource name.");
        ExplanationSchema equivalentSchema =
            fixture.Schema("The resource name.");
        ResourceExplanationCatalog first =
            ResourceExplanationCatalog.Create(
                [.. fixture.BaseSchemas, firstSchema],
                [
                    fixture.Snapshot(
                        firstSchema,
                        "first",
                        "probe/first",
                        "first"),
                ]);
        ResourceExplanationCatalog equivalent =
            ResourceExplanationCatalog.Create(
                [.. fixture.BaseSchemas, equivalentSchema],
                [
                    fixture.Snapshot(
                        equivalentSchema,
                        "second",
                        "probe/second",
                        "second"),
                ]);

        Assert.Equal(
            2,
            ResourceExplanationCatalog.Combine(first, equivalent)
                .Resources.Length);

        ExplanationSchema conflictingSchema =
            fixture.Schema("A conflicting resource name.");
        ResourceExplanationCatalog conflicting =
            ResourceExplanationCatalog.Create(
                [.. fixture.BaseSchemas, conflictingSchema],
                [
                    fixture.Snapshot(
                        conflictingSchema,
                        "third",
                        "probe/third",
                        "third"),
                ]);

        Assert.Throws<ArgumentException>(() =>
            ResourceExplanationCatalog.Combine(first, conflicting));
    }

    [Fact]
    public void AvailableEmptyRelationship_PreservesDeclaredTargetType()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library/categories"));

        ResourceExplanationDocument document =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(
                    depth: 0,
                    resourceLimit: 1,
                    relationshipLimit: 100))
                .Content;

        ResourceExplanationRelationship relationship =
            document.Relationships.Single(candidate =>
                candidate.Relationship.Value == "collection-analysis");
        Assert.Equal(
            ExplanationObservationState.Available,
            relationship.State);
        Assert.Empty(relationship.Targets);
        Assert.Contains(
            document.Schemas.SelectMany(static schema =>
                schema.ResourceTypes),
            static declaration =>
                declaration.Identity.Value == "analysis");
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
    public void Json_EmitsCanonicalPathsAndSelfContainedSchema()
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
        Assert.True(
            document.RootElement.GetProperty("schemas").GetArrayLength()
            > 0);
        Assert.DoesNotContain("\"octets\"", json);
        Assert.Equal(
            "structural-section",
            document.RootElement
                .GetProperty("root")
                .GetProperty("resource_type")
                .GetProperty("value")
                .GetString());
    }

    [Fact]
    public void Json_SourceGeneratedContract_RoundTripsTypedDocument()
    {
        ResourceExplanationCatalog catalog = StructuralCatalog();
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve("library"));
        ResourceExplanationDocument explanation =
            catalog.Explain(
                resolved,
                new ResourceExplanationRequest(8, 1000, 2000))
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

        Assert.Equal(json, roundTrippedJson);
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.ResourceType.Value == "catalog");
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.ResourceType.Value == "navigation-collection");
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.ResourceType.Value == "structural-category");
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.ResourceType.Value == "structural-section");
        Assert.Contains(
            roundTripped.Resources,
            static resource =>
                resource.ResourceType.Value == "structural-item");
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

    private static int TargetCount(
        ResourceExplanationCatalog catalog,
        string relationship) =>
        catalog.Relationships
            .Where(candidate =>
                candidate.Relationship.Value == relationship)
            .Sum(static candidate => candidate.Targets.Length);

    private static string? OptionalText(
        ResourceExplanationResource resource,
        string fact)
    {
        ExplanationFactObservation? observation = resource.Facts
            .FirstOrDefault(candidate => candidate.Fact.Value == fact);
        if (observation is null
            || observation.State == ExplanationObservationState.Absent)
        {
            return null;
        }
        return Assert.IsType<ExplanationValue.Scalar>(
            Assert.Single(observation.Values)).Value.Text;
    }

    private static int RequiredInteger(
        ResourceExplanationResource resource,
        string fact)
    {
        ExplanationFactObservation observation = resource.Facts.Single(
            candidate => candidate.Fact.Value == fact);
        return (int)Assert.IsType<ExplanationValue.Scalar>(
            Assert.Single(observation.Values)).Value.Integer!.Value;
    }

    private static int DeclarationCount(
        IEnumerable<ExplanationSchema> schemas) =>
        schemas.Sum(schema =>
            1
            + schema.DataShapes.Sum(static shape =>
                1 + (shape switch
                {
                    ExplanationDataShapeDeclaration.Record record =>
                        record.Fields.Length,
                    ExplanationDataShapeDeclaration.Choice choice =>
                        choice.Cases.Length,
                    _ => 0,
                }))
            + schema.ResourceTypes.Length
            + schema.AddressKinds.Length
            + schema.ResourceTypes.Sum(resource =>
                resource.Facts.Length
                + resource.Relationships.Length));

    private static ExplanationValue ExplanationText(string value) =>
        new ExplanationValue.Scalar(
            ExplanationScalarValue.FromText(value));

    private sealed class CatalogAdmissionFixture
    {
        private CatalogAdmissionFixture(
            ExplanationSchema[] baseSchemas,
            ExplanationDataShapeIdentity textShape,
            ExplanationPublicAddressKindIdentity pathKind,
            ExplanationOwnerIdentity owner,
            ExplanationSchemaIdentity schemaIdentity,
            ExplanationSchemaVersion version,
            ExplanationResourceTypeIdentity resourceType,
            ExplanationPublicAddressKindIdentity alternateKind,
            ExplanationFactIdentity fact,
            ExplanationDataShapeIdentity recordShape,
            ExplanationFieldIdentity field)
        {
            BaseSchemas = baseSchemas;
            TextShape = textShape;
            PathKind = pathKind;
            Owner = owner;
            SchemaIdentity = schemaIdentity;
            Version = version;
            ResourceType = resourceType;
            AlternateKind = alternateKind;
            Fact = fact;
            RecordShape = recordShape;
            Field = field;
        }

        internal ExplanationSchema[] BaseSchemas { get; }

        private ExplanationDataShapeIdentity TextShape { get; }

        private ExplanationPublicAddressKindIdentity PathKind { get; }

        private ExplanationOwnerIdentity Owner { get; }

        private ExplanationSchemaIdentity SchemaIdentity { get; }

        private ExplanationSchemaVersion Version { get; }

        private ExplanationResourceTypeIdentity ResourceType { get; }

        private ExplanationPublicAddressKindIdentity AlternateKind { get; }

        private ExplanationFactIdentity Fact { get; }

        private ExplanationDataShapeIdentity RecordShape { get; }

        private ExplanationFieldIdentity Field { get; }

        internal static CatalogAdmissionFixture Create()
        {
            ExplanationSchema[] baseSchemas =
                [.. StructuralCatalog().Schemas];
            ExplanationSchema core = baseSchemas.Single(
                static schema =>
                    schema.Identity.Owner.Value == "resource-explanation");
            ExplanationDataShapeIdentity textShape =
                core.DataShapes.Single(
                    static shape => shape.Identity.Value == "text").Identity;
            ExplanationPublicAddressKindIdentity pathKind =
                Assert.Single(core.AddressKinds).Identity;
            var owner = new ExplanationOwnerIdentity("catalog-probe");
            var schemaIdentity =
                new ExplanationSchemaIdentity(owner, "installed");
            var resourceType =
                new ExplanationResourceTypeIdentity(
                    schemaIdentity,
                    "resource");
            var alternateKind =
                new ExplanationPublicAddressKindIdentity(
                    schemaIdentity,
                    "alternate");
            var recordShape =
                new ExplanationDataShapeIdentity(
                    schemaIdentity,
                    "record");
            return new(
                baseSchemas,
                textShape,
                pathKind,
                owner,
                schemaIdentity,
                new(1),
                resourceType,
                alternateKind,
                new(resourceType, "name"),
                recordShape,
                new(recordShape, "value"));
        }

        internal ExplanationSchema Schema(string factMeaning) =>
            new(
                SchemaIdentity,
                Version,
                [
                    new ExplanationDataShapeDeclaration.Record(
                        RecordShape,
                        "Record",
                        "A catalog-admission record.",
                        new(4096, 2, 2),
                        [
                            new(
                                Field,
                                "Value",
                                "The record value.",
                                TextShape,
                                ExplanationCardinality.RequiredOne),
                        ]),
                ],
                [
                    new ExplanationResourceTypeDeclaration(
                        ResourceType,
                        "Resource",
                        "A catalog-admission resource.",
                        TextShape,
                        [
                            new(
                                Fact,
                                "Name",
                                factMeaning,
                                RecordShape,
                                ExplanationCardinality.RequiredOne,
                                ExplanationObservationStates.Available),
                        ],
                        addressKinds: [PathKind, AlternateKind]),
                ],
                [
                    new(
                        AlternateKind,
                        "Alternate address",
                        "An alternate catalog address.",
                        TextShape),
                ]);

        internal ExplanationResourceSnapshot Snapshot(
            ExplanationSchema schema,
            string identity,
            string path,
            string alternate) =>
            ExplanationConformance.CreateSnapshot(
                [.. BaseSchemas, schema],
                new(Owner, ResourceType, ExplanationText(identity)),
                Version,
                ExplanationSnapshotScope.Installed,
                [
                    new(PathKind, ExplanationText(path)),
                    new(AlternateKind, ExplanationText(alternate)),
                ],
                [
                    new(
                        Fact,
                        ExplanationObservationState.Available,
                        [
                            new ExplanationValue.Record(
                            [
                                new(
                                    Field,
                                    [ExplanationText(identity)]),
                            ]),
                        ]),
                ],
                []);
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
