using System.Collections.Immutable;
using System.Numerics;
using QuerySpace.Explanation;

namespace DotnetInspector.Sections;

internal static class ResourceExplanationVocabulary
{
    internal static readonly ExplanationOwnerIdentity ResourceExplanationOwner =
        new("resource-explanation");
    internal static readonly ExplanationOwnerIdentity SchemaQueryOwner =
        new("schema-query");
    internal static readonly ExplanationOwnerIdentity CapabilityOwner =
        new("inspection-capability-composition");
    internal static readonly ExplanationOwnerIdentity QuerySpaceOwner =
        new("query-space");
    internal static readonly ExplanationOwnerIdentity ConsumerOwner =
        new("consumer");
    internal static readonly ExplanationOwnerIdentity AnalysisOwner =
        new("analysis-requests");
    internal static readonly ExplanationOwnerIdentity FindingsOwner =
        new("findings");

    internal static readonly ExplanationSchemaIdentity CoreSchemaIdentity =
        new(ResourceExplanationOwner, "installed");
    internal static readonly ExplanationSchemaIdentity StructuralSchemaIdentity =
        new(SchemaQueryOwner, "installed");
    internal static readonly ExplanationSchemaIdentity CapabilitySchemaIdentity =
        new(CapabilityOwner, "installed");
    internal static readonly ExplanationSchemaIdentity QuerySchemaIdentity =
        new(QuerySpaceOwner, "installed");
    internal static readonly ExplanationSchemaIdentity ConsumerSchemaIdentity =
        new(ConsumerOwner, "installed");
    internal static readonly ExplanationSchemaIdentity AnalysisSchemaIdentity =
        new(AnalysisOwner, "installed");
    internal static readonly ExplanationSchemaIdentity FindingsSchemaIdentity =
        new(FindingsOwner, "installed");
    internal static readonly ExplanationSchemaVersion Version = new(1);

    internal static readonly ExplanationDataShapeIdentity TextShape =
        new(CoreSchemaIdentity, "text");
    internal static readonly ExplanationDataShapeIdentity IntegerShape =
        new(CoreSchemaIdentity, "integer");
    internal static readonly ExplanationDataShapeIdentity
        OpaqueExternalIdentityShape =
            new(QuerySchemaIdentity, "opaque-external-identity");
    internal static readonly ExplanationPublicAddressKindIdentity
        ResourcePathAddressKind =
            new(CoreSchemaIdentity, "resource-path");

    internal static readonly ExplanationResourceTypeIdentity
        NavigationCollectionType =
            new(CoreSchemaIdentity, "navigation-collection");
    internal static readonly ExplanationResourceTypeIdentity CatalogType =
        new(StructuralSchemaIdentity, "catalog");
    internal static readonly ExplanationResourceTypeIdentity
        StructuralCategoryType =
            new(StructuralSchemaIdentity, "structural-category");
    internal static readonly ExplanationResourceTypeIdentity
        StructuralSectionType =
            new(StructuralSchemaIdentity, "structural-section");
    internal static readonly ExplanationResourceTypeIdentity StructuralItemType =
        new(StructuralSchemaIdentity, "structural-item");
    internal static readonly ExplanationResourceTypeIdentity
        InspectionDocumentType =
            new(CapabilitySchemaIdentity, "inspection-document");
    internal static readonly ExplanationResourceTypeIdentity
        HostNeutralRouteType =
            new(CapabilitySchemaIdentity, "host-neutral-route");
    internal static readonly ExplanationResourceTypeIdentity QuerySpaceType =
        new(QuerySchemaIdentity, "query-space");
    internal static readonly ExplanationResourceTypeIdentity QueryFacetType =
        new(QuerySchemaIdentity, "query-facet");
    internal static readonly ExplanationResourceTypeIdentity
        ConsumerBindingType =
            new(ConsumerSchemaIdentity, "consumer-binding");
    internal static readonly ExplanationResourceTypeIdentity AnalysisType =
        new(AnalysisSchemaIdentity, "analysis");
    internal static readonly ExplanationResourceTypeIdentity
        OperationSurfaceType =
            new(AnalysisSchemaIdentity, "operation-surface");
    internal static readonly ExplanationResourceTypeIdentity IssuedFindingType =
        new(FindingsSchemaIdentity, "issued-finding");

    internal static readonly ImmutableArray<ExplanationSchema> Schemas =
        CreateSchemas();

    internal static ExplanationFactIdentity Fact(
        ExplanationResourceTypeIdentity resourceType,
        string identity) =>
        new(resourceType, identity);

    internal static ExplanationRelationshipIdentity Relationship(
        ExplanationResourceTypeIdentity resourceType,
        string identity) =>
        new(resourceType, identity);

    internal static ExplanationResourceKey Key(
        ExplanationResourceTypeIdentity resourceType,
        string identity) =>
        new(
            resourceType.Schema.Owner,
            resourceType,
            Text(identity));

    internal static ExplanationPublicAddress Address(ResourcePath path) =>
        new(ResourcePathAddressKind, Text(path.Value));

    internal static ExplanationValue Text(string value) =>
        new ExplanationValue.Scalar(
            ExplanationScalarValue.FromText(value));

    internal static ExplanationValue Integer(int value) =>
        new ExplanationValue.Scalar(
            ExplanationScalarValue.FromInteger(new BigInteger(value)));

    internal static ExplanationFactObservation TextFact(
        ExplanationResourceTypeIdentity resourceType,
        string identity,
        string value) =>
        new(
            Fact(resourceType, identity),
            ExplanationObservationState.Available,
            [Text(value)]);

    internal static ExplanationFactObservation IntegerFact(
        ExplanationResourceTypeIdentity resourceType,
        string identity,
        int value) =>
        new(
            Fact(resourceType, identity),
            ExplanationObservationState.Available,
            [Integer(value)]);

    internal static ExplanationFactObservation TextsFact(
        ExplanationResourceTypeIdentity resourceType,
        string identity,
        IEnumerable<string> values) =>
        new(
            Fact(resourceType, identity),
            ExplanationObservationState.Available,
            values.Select(Text));

    internal static ExplanationFactObservation AbsentFact(
        ExplanationResourceTypeIdentity resourceType,
        string identity) =>
        new(
            Fact(resourceType, identity),
            ExplanationObservationState.Absent);

    internal static ExplanationRelationshipObservation Targets(
        ExplanationResourceTypeIdentity sourceType,
        string identity,
        IEnumerable<ExplanationResourceKey> targets) =>
        new(
            Relationship(sourceType, identity),
            ExplanationObservationState.Available,
            targets.Select(static target =>
                new ExplanationRelationshipTarget(target)));

    internal static ExplanationRelationshipObservation EmptyTargets(
        ExplanationResourceTypeIdentity sourceType,
        string identity) =>
        Targets(sourceType, identity, []);

    internal static ResourceExplanationResource Resource(
        ResourcePath path,
        ExplanationResourceKey key,
        IEnumerable<ExplanationFactObservation> facts,
        IEnumerable<ExplanationRelationshipObservation> relationships) =>
        ResourceExplanationResource.FromSnapshot(
            path,
            ExplanationConformance.CreateSnapshot(
                Schemas,
                key,
                Version,
                ExplanationSnapshotScope.Installed,
                [Address(path)],
                facts,
                relationships));

    internal static string RequiredText(
        ResourceExplanationResource resource,
        string identity) =>
        Values(resource, identity).Single() is ExplanationValue.Scalar
        {
            Value:
            {
                Kind: ExplanationScalarKind.Text,
                Text: { } text,
            },
        }
            ? text
            : throw new InvalidOperationException(
                $"Resource '{resource.Key}' fact '{identity}' is not text.");

    internal static int RequiredInteger(
        ResourceExplanationResource resource,
        string identity) =>
        Values(resource, identity).Single() is ExplanationValue.Scalar
        {
            Value:
            {
                Kind: ExplanationScalarKind.Integer,
                Integer: { } integer,
            },
        }
            && integer >= int.MinValue
            && integer <= int.MaxValue
            ? (int)integer
            : throw new InvalidOperationException(
                $"Resource '{resource.Key}' fact '{identity}' is not an "
                + "Int32 integer.");

    internal static ImmutableArray<string> Texts(
        ResourceExplanationResource resource,
        string identity) =>
    [
        .. Values(resource, identity).Select(value =>
            value is ExplanationValue.Scalar
            {
                Value:
                {
                    Kind: ExplanationScalarKind.Text,
                    Text: { } text,
                },
            }
                ? text
                : throw new InvalidOperationException(
                    $"Resource '{resource.Key}' fact '{identity}' contains "
                    + "a non-text value.")),
    ];

    internal static string? OptionalText(
        ResourceExplanationResource resource,
        string identity)
    {
        ExplanationFactObservation? fact = resource.Facts
            .FirstOrDefault(candidate =>
                candidate.Fact.Value == identity);
        if (fact is null
            || fact.State == ExplanationObservationState.Absent)
        {
            return null;
        }
        return RequiredText(resource, identity);
    }

    internal static ExplanationResourceTypeDeclaration ResourceType(
        ExplanationResourceTypeIdentity identity) =>
        Schemas.SelectMany(static schema => schema.ResourceTypes)
            .Single(resource => resource.Identity == identity);

    internal static ExplanationRelationshipDeclaration
        RelationshipDeclaration(
            ExplanationRelationshipIdentity identity) =>
        ResourceType(identity.ResourceType).Relationships.Single(
            relationship => relationship.Identity == identity);

    internal static int DeclarationCount(
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

    private static ImmutableArray<ExplanationValue> Values(
        ResourceExplanationResource resource,
        string identity)
    {
        ExplanationFactObservation fact = resource.Facts.Single(
            candidate => candidate.Fact.Value == identity);
        if (fact.State != ExplanationObservationState.Available)
        {
            throw new InvalidOperationException(
                $"Resource '{resource.Key}' fact '{identity}' is "
                + $"'{fact.State}'.");
        }
        return fact.Values;
    }

    private static ImmutableArray<ExplanationSchema> CreateSchemas()
    {
        var valueBudget = new ExplanationValueBudget(64 * 1024, 8, 4096);
        var textShape =
            new ExplanationDataShapeDeclaration.Scalar(
                TextShape,
                "Text",
                "Well-formed Unicode text.",
                valueBudget,
                ExplanationScalarKind.Text);
        var integerShape =
            new ExplanationDataShapeDeclaration.Scalar(
                IntegerShape,
                "Integer",
                "An arbitrary-precision integer.",
                valueBudget,
                ExplanationScalarKind.Integer);
        var opaqueExternalIdentityShape =
            new ExplanationDataShapeDeclaration.Scalar(
                OpaqueExternalIdentityShape,
                "Opaque external identity",
                "An identity issued by an owner that has not yet published "
                + "an explanation resource type and key.",
                valueBudget,
                ExplanationScalarKind.Text);
        var address =
            new ExplanationPublicAddressKindDeclaration(
                ResourcePathAddressKind,
                "Resource path",
                "A canonical Resource Explanation navigation path.",
                TextShape);

        ExplanationResourceTypeDeclaration navigation =
            Type(
                NavigationCollectionType,
                "Navigation collection",
                "A synthetic Resource Explanation navigation collection.",
                [
                    TextFact("name", "Name"),
                    IntegerFact("members", "Members"),
                ],
                [
                    Relationship(
                        "navigation-collection",
                        "Navigation",
                        NavigationCollectionType),
                    Relationship(
                        "collection-category",
                        "Collection member",
                        StructuralCategoryType),
                    Relationship(
                        "collection-section",
                        "Collection member",
                        StructuralSectionType),
                    Relationship(
                        "collection-item",
                        "Collection member",
                        StructuralItemType),
                    Relationship(
                        "collection-analysis",
                        "Collection member",
                        AnalysisType),
                ]);

        ExplanationResourceTypeDeclaration catalog =
            Type(
                CatalogType,
                "Catalog",
                "One installed structural catalog.",
                [
                    TextFact("name", "Name"),
                    IntegerFact("members", "Members"),
                ],
                [
                    Relationship(
                        "navigation",
                        "Navigation",
                        NavigationCollectionType),
                    Relationship(
                        "catalog-category-entry",
                        "Catalog entry",
                        StructuralCategoryType),
                    Relationship(
                        "catalog-section-entry",
                        "Catalog entry",
                        StructuralSectionType),
                ]);
        ExplanationResourceTypeDeclaration category =
            Type(
                StructuralCategoryType,
                "Structural category",
                "One structural discovery category.",
                [
                    TextFact("name", "Name"),
                    TextsFact("formats", "Formats", 16),
                    IntegerFact("members", "Members"),
                ],
                [
                    Relationship(
                        "category-member",
                        "Category member",
                        StructuralSectionType),
                ]);
        ExplanationResourceTypeDeclaration section =
            Type(
                StructuralSectionType,
                "Structural section",
                "One structural discovery section.",
                [
                    TextFact("name", "Name"),
                    TextsFact("formats", "Formats", 16),
                    IntegerFact("members", "Members"),
                    OptionalTextFact("shape", "Shape"),
                    OptionalTextFact("cardinality", "Cardinality"),
                ],
                [
                    Relationship(
                        "navigation",
                        "Navigation",
                        NavigationCollectionType),
                    Relationship(
                        "structural-item",
                        "Structural item",
                        StructuralItemType),
                ]);
        ExplanationResourceTypeDeclaration item =
            Type(
                StructuralItemType,
                "Structural item",
                "One structural discovery item.",
                [
                    TextFact("name", "Name"),
                    TextFact("item-kind", "Item kind"),
                ]);

        ExplanationResourceTypeDeclaration document =
            Type(
                InspectionDocumentType,
                "Inspection document",
                "One host-neutral inspection result contract.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("name", "Name"),
                    TextFact("summary", "Summary"),
                    OpaqueExternalIdentityFact(
                        "result-contract",
                        "Result contract"),
                ],
                [
                    Relationship(
                        "route",
                        "Route",
                        HostNeutralRouteType),
                ]);
        ExplanationResourceTypeDeclaration route =
            Type(
                HostNeutralRouteType,
                "Host-neutral route",
                "One executable host-neutral inspection route.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("name", "Name"),
                    TextFact("summary", "Summary"),
                    TextFact("subject-role", "Subject role"),
                    TextFact("result-grain", "Result grain"),
                    TextFact("profile", "Profile"),
                    OpaqueExternalIdentityFact(
                        "result-contract",
                        "Result contract"),
                ],
                [
                    Relationship(
                        "produces",
                        "Produces",
                        InspectionDocumentType),
                    Relationship(
                        "query-surface",
                        "Query surface",
                        QuerySpaceType),
                    Relationship(
                        "consumer-binding",
                        "Consumer binding",
                        ConsumerBindingType),
                ]);
        ExplanationResourceTypeDeclaration querySpace =
            Type(
                QuerySpaceType,
                "Query Space",
                "One effective executable Query Space.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("name", "Name"),
                    TextFact("summary", "Summary"),
                    IntegerFact("members", "Members"),
                ],
                [
                    Relationship(
                        "query-facet",
                        "Query facet",
                        QueryFacetType),
                ]);
        ExplanationResourceTypeDeclaration queryFacet =
            Type(
                QueryFacetType,
                "Query facet",
                "One queryable operation term.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("key", "Key"),
                    TextFact("name", "Name"),
                    TextFact("summary", "Summary"),
                    TextsFact("operators", "Operators", 32),
                    TextFact("value-kind", "Value kind"),
                    TextsFact("values", "Values", 256),
                    TextsFact("examples", "Examples", 256),
                    TextsFact("effects", "Effects", 256),
                ],
                [
                    Relationship(
                        "route",
                        "Route",
                        HostNeutralRouteType),
                    Relationship(
                        "required-context",
                        "Required context",
                        QueryFacetType),
                    Relationship(
                        "exposed-by",
                        "Exposed by",
                        ConsumerBindingType),
                ]);
        ExplanationResourceTypeDeclaration binding =
            Type(
                ConsumerBindingType,
                "Consumer binding",
                "One production host binding.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("name", "Name"),
                    TextFact("summary", "Summary"),
                    TextFact("consumer-kind", "Consumer kind"),
                    TextFact("gesture", "Gesture"),
                    IntegerFact("members", "Members"),
                ],
                [
                    Relationship(
                        "invokes",
                        "Invokes",
                        HostNeutralRouteType),
                    Relationship(
                        "exposes",
                        "Exposes",
                        QueryFacetType),
                ]);
        ExplanationResourceTypeDeclaration analysis =
            Type(
                AnalysisType,
                "Analysis",
                "One registered analysis descriptor.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("name", "Name"),
                    IntegerFact("revision", "Revision"),
                    TextFact("cost", "Cost"),
                    TextsFact("participations", "Participations", 256),
                ],
                [
                    Relationship(
                        "participates",
                        "Participates",
                        OperationSurfaceType),
                    Relationship(
                        "issues",
                        "Issues",
                        IssuedFindingType),
                ]);
        ExplanationResourceTypeDeclaration operationSurface =
            Type(
                OperationSurfaceType,
                "Operation surface",
                "One operation and report surface.",
                []);
        ExplanationResourceTypeDeclaration finding =
            Type(
                IssuedFindingType,
                "Issued Finding",
                "One Finding descriptor issued at an operation surface.",
                []);

        return
        [
            new(
                CoreSchemaIdentity,
                Version,
                [textShape, integerShape],
                [navigation],
                [address]),
            new(
                StructuralSchemaIdentity,
                Version,
                [],
                [catalog, category, section, item]),
            new(
                CapabilitySchemaIdentity,
                Version,
                [],
                [document, route]),
            new(
                QuerySchemaIdentity,
                Version,
                [opaqueExternalIdentityShape],
                [querySpace, queryFacet]),
            new(
                ConsumerSchemaIdentity,
                Version,
                [],
                [binding]),
            new(
                AnalysisSchemaIdentity,
                Version,
                [],
                [analysis, operationSurface]),
            new(
                FindingsSchemaIdentity,
                Version,
                [],
                [finding]),
        ];

        ExplanationResourceTypeDeclaration Type(
            ExplanationResourceTypeIdentity identity,
            string displayName,
            string meaning,
            IEnumerable<
                Func<
                    ExplanationResourceTypeIdentity,
                    ExplanationFactDeclaration>> facts,
            IEnumerable<
                Func<
                    ExplanationResourceTypeIdentity,
                    ExplanationRelationshipDeclaration>>? relationships =
                null) =>
            new(
                identity,
                displayName,
                meaning,
                TextShape,
                facts.Select(factory => factory(identity)),
                relationships?.Select(factory => factory(identity)),
                [ResourcePathAddressKind]);

        Func<
            ExplanationResourceTypeIdentity,
            ExplanationFactDeclaration> TextFact(
            string identity,
            string displayName) =>
            resourceType =>
                new(
                    Fact(resourceType, identity),
                    displayName,
                    $"The {displayName.ToLowerInvariant()} fact.",
                    TextShape,
                    ExplanationCardinality.RequiredOne,
                    ExplanationObservationStates.Available);

        Func<
            ExplanationResourceTypeIdentity,
            ExplanationFactDeclaration> IntegerFact(
            string identity,
            string displayName) =>
            resourceType =>
                new(
                    Fact(resourceType, identity),
                    displayName,
                    $"The {displayName.ToLowerInvariant()} fact.",
                    IntegerShape,
                    ExplanationCardinality.RequiredOne,
                    ExplanationObservationStates.Available);

        Func<
            ExplanationResourceTypeIdentity,
            ExplanationFactDeclaration> OpaqueExternalIdentityFact(
            string identity,
            string displayName) =>
            resourceType =>
                new(
                    Fact(resourceType, identity),
                    displayName,
                    $"The {displayName.ToLowerInvariant()} fact.",
                    OpaqueExternalIdentityShape,
                    ExplanationCardinality.RequiredOne,
                    ExplanationObservationStates.Available);

        Func<
            ExplanationResourceTypeIdentity,
            ExplanationFactDeclaration> OptionalTextFact(
            string identity,
            string displayName) =>
            resourceType =>
                new(
                    Fact(resourceType, identity),
                    displayName,
                    $"The optional {displayName.ToLowerInvariant()} fact.",
                    TextShape,
                    ExplanationCardinality.OptionalOne,
                    ExplanationObservationStates.Available
                        | ExplanationObservationStates.Absent);

        Func<
            ExplanationResourceTypeIdentity,
            ExplanationFactDeclaration> TextsFact(
            string identity,
            string displayName,
            int maximumCount) =>
            resourceType =>
                new(
                    Fact(resourceType, identity),
                    displayName,
                    $"The ordered {displayName.ToLowerInvariant()} facts.",
                    TextShape,
                    ExplanationCardinality.OrderedMany,
                    ExplanationObservationStates.Available,
                    maximumValueCount: maximumCount);

        Func<
            ExplanationResourceTypeIdentity,
            ExplanationRelationshipDeclaration> Relationship(
            string identity,
            string displayName,
            ExplanationResourceTypeIdentity target) =>
            resourceType =>
                new(
                    ResourceExplanationVocabulary.Relationship(
                        resourceType,
                        identity),
                    displayName,
                    $"The {displayName.ToLowerInvariant()} relationship.",
                    target,
                    ExplanationCardinality.OrderedMany,
                    ExplanationObservationStates.Available);
    }
}
