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
    internal static readonly ExplanationOwnerIdentity ProductVocabularyOwner =
        new("product-vocabulary");

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
    internal static readonly ExplanationSchemaIdentity
        ProductVocabularySchemaIdentity =
            new(ProductVocabularyOwner, "installed");
    internal static readonly ExplanationSchemaVersion Version = new(1);

    internal static readonly ExplanationDataShapeIdentity TextShape =
        new(CoreSchemaIdentity, "text");
    internal static readonly ExplanationDataShapeIdentity IntegerShape =
        new(CoreSchemaIdentity, "integer");
    internal static readonly ExplanationDataShapeIdentity BooleanShape =
        new(CoreSchemaIdentity, "boolean");
    internal static readonly ExplanationDataShapeIdentity
        OpaqueExternalIdentityShape =
            new(QuerySchemaIdentity, "opaque-external-identity");
    internal static readonly ExplanationDataShapeIdentity
        QueryFacetOptionShape =
            new(QuerySchemaIdentity, "query-facet-option");
    internal static readonly ExplanationPublicAddressKindIdentity
        ResourcePathAddressKind =
            new(CoreSchemaIdentity, "resource-path");

    internal static readonly ExplanationResourceTypeIdentity
        NavigationCollectionType =
            new(CoreSchemaIdentity, "navigation-collection");
    internal static readonly ExplanationResourceTypeIdentity
        ExplanationRootType =
            new(CoreSchemaIdentity, "explanation-root");
    internal static readonly ExplanationResourceTypeIdentity
        RoutingCollectionType =
            new(CoreSchemaIdentity, "routing-collection");
    internal static readonly ExplanationResourceTypeIdentity
        RoutingEntryType =
            new(CoreSchemaIdentity, "routing-entry");
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
    internal static readonly ExplanationResourceTypeIdentity
        RelatedOperationType =
            new(CapabilitySchemaIdentity, "related-operation");
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
    internal static readonly ExplanationResourceTypeIdentity
        ValueVocabularyType =
            new(ProductVocabularySchemaIdentity, "value-vocabulary");
    internal static readonly ExplanationResourceTypeIdentity
        VocabularyValueType =
            new(ProductVocabularySchemaIdentity, "vocabulary-value");

    internal static readonly ExplanationDataShapeIdentity MapDeclarationShape =
        new(ProductVocabularySchemaIdentity, "map-declaration");
    internal static readonly ExplanationDataShapeIdentity MapEntryShape =
        new(ProductVocabularySchemaIdentity, "map-entry");
    internal static readonly ExplanationDataShapeIdentity MapValueShape =
        new(ProductVocabularySchemaIdentity, "map-value");

    /// <summary>The most representative questions one routing entry carries.</summary>
    internal const int RoutingQuestionLimit = 8;

    /// <summary>The most maps one value vocabulary declares.</summary>
    internal const int MapLimit = 64;

    /// <summary>The most map entries one vocabulary value carries.</summary>
    internal const int MapEntryLimit = 256;

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

    internal static ExplanationValue Boolean(bool value) =>
        new ExplanationValue.Scalar(
            ExplanationScalarValue.FromBoolean(value));

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

    internal static ExplanationFactObservation BooleanFact(
        ExplanationResourceTypeIdentity resourceType,
        string identity,
        bool value) =>
        new(
            Fact(resourceType, identity),
            ExplanationObservationState.Available,
            [Boolean(value)]);

    internal static ExplanationFactObservation ValuesFact(
        ExplanationResourceTypeIdentity resourceType,
        string identity,
        IEnumerable<ExplanationValue> values) =>
        new(
            Fact(resourceType, identity),
            ExplanationObservationState.Available,
            values);

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
        var booleanShape =
            new ExplanationDataShapeDeclaration.Scalar(
                BooleanShape,
                "Boolean",
                "A Boolean value.",
                valueBudget,
                ExplanationScalarKind.Boolean);
        var queryFacetOptionShape =
            new ExplanationDataShapeDeclaration.Record(
                QueryFacetOptionShape,
                "Query facet option",
                "One owner-issued query-facet option or suggestion.",
                valueBudget,
                [
                    MapField(
                        QueryFacetOptionShape,
                        "value",
                        "Value",
                        "The portable operand value.",
                        TextShape),
                    MapField(
                        QueryFacetOptionShape,
                        "label",
                        "Label",
                        "The display label.",
                        TextShape),
                    MapField(
                        QueryFacetOptionShape,
                        "summary",
                        "Summary",
                        "The option meaning.",
                        TextShape),
                ]);
        var mapDeclarationShape =
            new ExplanationDataShapeDeclaration.Record(
                MapDeclarationShape,
                "Map declaration",
                "One owner-declared vocabulary map.",
                valueBudget,
                [
                    MapField(
                        MapDeclarationShape,
                        "identity",
                        "Identity",
                        "The map identity within its vocabulary.",
                        TextShape),
                    MapField(
                        MapDeclarationShape,
                        "name",
                        "Name",
                        "The owner-issued display label.",
                        TextShape),
                    MapField(
                        MapDeclarationShape,
                        "summary",
                        "Summary",
                        "The owner-issued summary.",
                        TextShape),
                    MapField(
                        MapDeclarationShape,
                        "value-kind",
                        "Value kind",
                        "text, integer, boolean, or term.",
                        TextShape),
                    new(
                        new(MapDeclarationShape, "target-vocabulary"),
                        "Target vocabulary",
                        "The vocabulary a term map targets.",
                        TextShape,
                        ExplanationCardinality.OptionalOne),
                    MapField(
                        MapDeclarationShape,
                        "cardinality",
                        "Cardinality",
                        "exactly-one, optional-one, one-or-more, or "
                        + "zero-or-more values per value.",
                        TextShape),
                    MapField(
                        MapDeclarationShape,
                        "coverage",
                        "Coverage",
                        "complete or partial.",
                        TextShape),
                ]);
        var mapValueShape =
            new ExplanationDataShapeDeclaration.Choice(
                MapValueShape,
                "Map value",
                "One owner-issued map value.",
                valueBudget,
                [
                    MapValueCase("text", "Text", TextShape),
                    MapValueCase("integer", "Integer", IntegerShape),
                    MapValueCase("boolean", "Boolean", BooleanShape),
                    new(
                        new(MapValueShape, "term"),
                        "Term",
                        "The exact identity of a value in the map's target "
                        + "vocabulary.",
                        TextShape),
                ]);
        var mapEntryShape =
            new ExplanationDataShapeDeclaration.Record(
                MapEntryShape,
                "Map entry",
                "One map value a vocabulary value carries.",
                valueBudget,
                [
                    MapField(
                        MapEntryShape,
                        "map",
                        "Map",
                        "The map identity within the value's vocabulary.",
                        TextShape),
                    MapField(
                        MapEntryShape,
                        "value",
                        "Value",
                        "The map value.",
                        MapValueShape),
                ]);
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
                    Relationship(
                        "collection-vocabulary",
                        "Collection member",
                        ValueVocabularyType),
                ]);

        ExplanationResourceTypeDeclaration root =
            Type(
                ExplanationRootType,
                "Explanation root",
                "The one entry from which every installed explanation "
                + "resource of this host's catalog is reachable.",
                [
                    TextFact("name", "Name"),
                    TextFact("coverage", "Coverage"),
                    IntegerFact("members", "Members"),
                ],
                [
                    Relationship(
                        "root-collection",
                        "Routing collection",
                        RoutingCollectionType),
                ]);
        ExplanationResourceTypeDeclaration routingCollection =
            Type(
                RoutingCollectionType,
                "Routing collection",
                "The ordered routing entries of one role under the "
                + "explanation root.",
                [
                    TextFact("name", "Name"),
                    IntegerFact("members", "Members"),
                ],
                [
                    Relationship(
                        "collection-routing-entry",
                        "Collection member",
                        RoutingEntryType),
                ]);
        ExplanationResourceTypeDeclaration routingEntry =
            Type(
                RoutingEntryType,
                "Routing entry",
                "One owner-issued routing declaration: a subject kind, "
                + "operation, or reference collection a reader can choose "
                + "from the root.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("role", "Role"),
                    TextFact("name", "Name"),
                    TextFact("issuer", "Issuer"),
                    TextFact("purpose", "Purpose"),
                    TextsFact("questions", "Questions", RoutingQuestionLimit),
                    OptionalTextFact("grain", "Grain"),
                    OptionalTextFact("scope", "Scope"),
                    OptionalTextFact("not-yet-explainable", "Not yet explainable"),
                ],
                [
                    Relationship(
                        "routing-destination-catalog",
                        "Routing destination",
                        CatalogType),
                    Relationship(
                        "routing-destination-document",
                        "Routing destination",
                        InspectionDocumentType),
                    Relationship(
                        "routing-destination-collection",
                        "Routing destination",
                        NavigationCollectionType),
                    Relationship(
                        "accepts-subject-kind",
                        "Accepts subject kind",
                        RoutingEntryType),
                    Relationship(
                        "accepted-by-operation",
                        "Accepted by operation",
                        RoutingEntryType),
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
                    OptionalTextFact("summary", "Summary"),
                    OptionalTextFact("basis", "Basis"),
                    OptionalTextFact("empty", "Empty"),
                    OptionalTextFact("boundary", "Boundary"),
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
        ExplanationResourceTypeDeclaration relatedOperation =
            Type(
                RelatedOperationType,
                "Related operation",
                "One owner-issued operation related to another resource.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("name", "Name"),
                    TextFact("summary", "Summary"),
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
                    TextsFact("input-rules", "Input rules", 64),
                    OptionalIntegerFact(
                        "presentation-order",
                        "Presentation order"),
                    OptionalTextFact("control-kind", "Control kind"),
                    OptionalTextFact(
                        "presentation-kind",
                        "Presentation kind"),
                    OptionalTextFact(
                        "execution-class",
                        "Execution class"),
                    OptionalTextFact(
                        "selection-group",
                        "Selection group"),
                    OptionalBooleanFact(
                        "combines-within-selection-group",
                        "Combines within selection group"),
                    OptionalTextFact(
                        "replacement-group",
                        "Replacement group"),
                    OptionalTextFact(
                        "option-source",
                        "Option source"),
                    ShapedFact(
                        "options",
                        "Options",
                        QueryFacetOptionShape,
                        256),
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
                        "related-operation",
                        "Related operation",
                        RelatedOperationType),
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
                    TextsFact("input-rules", "Input rules", 64),
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
        ExplanationResourceTypeDeclaration valueVocabulary =
            Type(
                ValueVocabularyType,
                "Value vocabulary",
                "One product vocabulary whose values rich-query inputs "
                + "accept.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("name", "Name"),
                    OptionalTextFact("summary", "Summary"),
                    IntegerFact("members", "Members"),
                    OpaqueExternalIdentitiesFact(
                        "accepted-by",
                        "Accepted by",
                        64),
                    ShapedFact(
                        "maps",
                        "Maps",
                        MapDeclarationShape,
                        MapLimit),
                    TextsFact("defaults", "Defaults", 64),
                ],
                [
                    Relationship(
                        "vocabulary-value",
                        "Value",
                        VocabularyValueType),
                    Relationship(
                        "term-map-target",
                        "Term map target",
                        ValueVocabularyType),
                ]);
        ExplanationResourceTypeDeclaration vocabularyValue =
            Type(
                VocabularyValueType,
                "Vocabulary value",
                "One stable value of a product vocabulary.",
                [
                    TextFact("identity", "Identity"),
                    TextFact("name", "Name"),
                    OptionalTextFact("summary", "Summary"),
                    ShapedFact(
                        "map-entries",
                        "Map entries",
                        MapEntryShape,
                        MapEntryLimit),
                ],
                [
                    Relationship(
                        "term-map-value",
                        "Term map value",
                        VocabularyValueType),
                ]);

        return
        [
            new(
                CoreSchemaIdentity,
                Version,
                [textShape, integerShape, booleanShape],
                [navigation, root, routingCollection, routingEntry],
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
                [document, route, relatedOperation]),
            new(
                QuerySchemaIdentity,
                Version,
                [opaqueExternalIdentityShape, queryFacetOptionShape],
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
            new(
                ProductVocabularySchemaIdentity,
                Version,
                [mapDeclarationShape, mapValueShape, mapEntryShape],
                [valueVocabulary, vocabularyValue]),
        ];

        static ExplanationRecordFieldDeclaration MapField(
            ExplanationDataShapeIdentity record,
            string identity,
            string displayName,
            string meaning,
            ExplanationDataShapeIdentity valueShape) =>
            new(
                new(record, identity),
                displayName,
                meaning,
                valueShape,
                ExplanationCardinality.RequiredOne);

        static ExplanationChoiceCaseDeclaration MapValueCase(
            string identity,
            string displayName,
            ExplanationDataShapeIdentity valueShape) =>
            new(
                new(MapValueShape, identity),
                displayName,
                $"A {displayName.ToLowerInvariant()} map value.",
                valueShape);

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
            ExplanationFactDeclaration> OpaqueExternalIdentitiesFact(
            string identity,
            string displayName,
            int maximumCount) =>
            resourceType =>
                new(
                    Fact(resourceType, identity),
                    displayName,
                    $"The ordered {displayName.ToLowerInvariant()} facts.",
                    OpaqueExternalIdentityShape,
                    ExplanationCardinality.OrderedMany,
                    ExplanationObservationStates.Available,
                    maximumValueCount: maximumCount);

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
            ExplanationFactDeclaration> OptionalIntegerFact(
            string identity,
            string displayName) =>
            resourceType =>
                new(
                    Fact(resourceType, identity),
                    displayName,
                    $"The optional {displayName.ToLowerInvariant()} fact.",
                    IntegerShape,
                    ExplanationCardinality.OptionalOne,
                    ExplanationObservationStates.Available
                        | ExplanationObservationStates.Absent);

        Func<
            ExplanationResourceTypeIdentity,
            ExplanationFactDeclaration> OptionalBooleanFact(
            string identity,
            string displayName) =>
            resourceType =>
                new(
                    Fact(resourceType, identity),
                    displayName,
                    $"The optional {displayName.ToLowerInvariant()} fact.",
                    BooleanShape,
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
            ExplanationFactDeclaration> ShapedFact(
            string identity,
            string displayName,
            ExplanationDataShapeIdentity shape,
            int maximumCount) =>
            resourceType =>
                new(
                    Fact(resourceType, identity),
                    displayName,
                    $"The ordered {displayName.ToLowerInvariant()} facts.",
                    shape,
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
