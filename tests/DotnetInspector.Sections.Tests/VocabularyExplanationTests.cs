using DotnetInspector.Queries;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using QuerySpace.Explanation;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Sections.Tests;

/// <summary>
/// Value-vocabulary explanation (<c>docs/design/resource-explanation.md</c>,
/// "Value-vocabulary resources"): every vocabulary in the composed sections
/// index is exactly one <c>vocabularies/&lt;id&gt;</c> resource, the index
/// itself is the <c>vocabularies</c> collection, every value is one
/// <c>vocabularies/&lt;id&gt;/values/&lt;segment&gt;</c> resource, and term
/// maps are typed relationships between vocabularies and between values.
/// </summary>
public sealed class VocabularyExplanationTests
{
    [Fact]
    public void CollectionListsEveryIndexedVocabularyInIndexOrder()
    {
        VocabularySnapshot snapshot = ProductSnapshot();
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateVocabularies(snapshot);
        string[] productVocabularyIds =
        [
            .. snapshot.GetVocabulary(new VocabularyIdentity(
                    snapshot.Catalog,
                    ProductVocabularyComposition.SectionsId))
                .Terms.Select(term => term.Identity.Value),
        ];
        Assert.Equal(4, productVocabularyIds.Length);

        Assert.Equal(
            productVocabularyIds.Select(id => $"vocabularies/{id}"),
            catalog.Resources
                .Where(resource =>
                    resource.ResourceType.Value == "value-vocabulary")
                .Select(resource => resource.Path!.Value));
        Assert.DoesNotContain(
            catalog.Resources,
            resource => resource.Path!.Value.Contains(
                ProductVocabularyComposition.SectionsId,
                StringComparison.Ordinal));

        ResourceExplanationDocument document = Explain(catalog, "vocabularies", depth: 1);
        ResourceExplanationRelationship members = Assert.Single(
            document.Relationships,
            relationship => relationship.Relationship.Value == "collection-vocabulary");
        Assert.Equal(
            productVocabularyIds,
            members.Targets.Select(target => KeyIdentity(target.Resource)));
        Assert.Equal(
            productVocabularyIds.Length,
            Integer(document.Resources[0], "members"));
    }

    [Fact]
    public void VocabularyResourceCarriesItsDescriptorNotItsValueListing()
    {
        VocabularySnapshot snapshot = ProductSnapshot();
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateVocabularies(snapshot);

        ResourceExplanationResource bodyKinds = Explain(
            catalog,
            $"vocabularies/{BodyShapeVocabulary.BodyKindsId}",
            depth: 0).Resources[0];
        VocabularyDefinition declared = snapshot.GetVocabulary(
            new VocabularyIdentity(snapshot.Catalog, BodyShapeVocabulary.BodyKindsId));
        Assert.Equal([BodyShapeVocabulary.BodyKindsId], Texts(bodyKinds, "identity"));
        Assert.Equal([BodyShapeVocabulary.BodyKindsLabel], Texts(bodyKinds, "name"));
        Assert.Equal(declared.Terms.Length, Integer(bodyKinds, "members"));
        Assert.Equal(["decompiler.body-kind"], Texts(bodyKinds, "accepted-by"));
        Assert.Empty(Records(bodyKinds, "maps"));
        Assert.DoesNotContain(
            bodyKinds.Facts,
            observation => observation.Fact.Value is "fields" or "examples");

        ResourceExplanationResource accessibility = Explain(
            catalog,
            $"vocabularies/{ApiAccessibilityVocabulary.AccessibilityId}",
            depth: 0).Resources[0];
        Assert.Equal(
            ["api.type-inventory", "api.member-inventory"],
            Texts(accessibility, "accepted-by"));
        VocabularyDefinition accessibilityDeclared = snapshot.GetVocabulary(
            new VocabularyIdentity(snapshot.Catalog, ApiAccessibilityVocabulary.AccessibilityId));
        string[] declaredDefaults =
        [
            .. accessibilityDeclared.Terms
                .Where(term => term.GetRequiredValues(
                        accessibilityDeclared.GetMap("default").Identity)
                    .Contains(ExplanationValue.Boolean(true)))
                .Select(term => term.Identity.Value),
        ];
        Assert.NotEmpty(declaredDefaults);
        Assert.Equal(declaredDefaults, Texts(accessibility, "defaults"));
        Assert.Empty(Texts(bodyKinds, "defaults"));
    }

    [Fact]
    public void VocabularyMapsCarryTheirOwnerIssuedDeclarations()
    {
        VocabularySnapshot snapshot = ProductSnapshot();
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateVocabularies(snapshot);
        VocabularyDefinition declared = snapshot.GetVocabulary(
            new VocabularyIdentity(
                snapshot.Catalog,
                StyleOptionVocabularies.StyleChoicesId));

        ExplanationValue.Record[] maps = Records(
            Explain(
                catalog,
                $"vocabularies/{StyleOptionVocabularies.StyleChoicesId}",
                depth: 0).Resources[0],
            "maps");

        Assert.Equal(
            declared.Maps.Select(map => map.Identity.Value),
            maps.Select(map => Field(map, "identity")));
        ExplanationValue.Record endorsed = Assert.Single(
            maps,
            map => Field(map, "identity") == "oracle_endorsed");
        VocabularyMapDefinition endorsedMap = declared.GetMap("oracle_endorsed");
        Assert.Equal(endorsedMap.DisplayLabel, Field(endorsed, "name"));
        Assert.Equal(endorsedMap.Summary, Field(endorsed, "summary"));
        Assert.Equal("boolean", Field(endorsed, "value-kind"));
        Assert.Null(OptionalField(endorsed, "target-vocabulary"));
        ExplanationValue.Record tier = Assert.Single(
            maps,
            map => Field(map, "identity") == "tier");
        Assert.Equal("term", Field(tier, "value-kind"));
        Assert.Equal(
            StyleOptionVocabularies.StyleTiersId,
            OptionalField(tier, "target-vocabulary"));
        Assert.Equal("exactly-one", Field(tier, "cardinality"));
        Assert.Equal("complete", Field(tier, "coverage"));
    }

    [Fact]
    public void EveryValueIsAnOrderedResourceAtItsRegisteredSegment()
    {
        VocabularySnapshot snapshot = ProductSnapshot();
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateVocabularies(snapshot);

        foreach (VocabularyDefinition vocabulary in snapshot.Vocabularies
                     .Where(vocabulary => vocabulary.Identity.Value
                         != ProductVocabularyComposition.SectionsId))
        {
            string id = vocabulary.Identity.Value;
            ResourceExplanationDocument document =
                Explain(catalog, $"vocabularies/{id}", depth: 1);
            Assert.DoesNotContain(
                document.Traversal.TruncationReasons,
                reason => reason != ResourceExplanationTruncationReason.Depth);
            ResourceExplanationRelationship values = Assert.Single(
                document.Relationships,
                relationship => relationship.Source == document.Resources[0].Key
                    && relationship.Relationship.Value == "vocabulary-value");
            string[] paths =
            [
                .. vocabulary.Terms.Select(term =>
                    $"vocabularies/{id}/values/"
                    + term.Identity.Value.ToLowerInvariant().Replace(':', '.')),
            ];
            Assert.Equal(paths.Length, values.Targets.Length);
            ResourceExplanationResource[] expanded =
            [
                .. document.Resources.Where(resource =>
                    resource.ResourceType.Value == "vocabulary-value"),
            ];
            Assert.Equal(paths, expanded.Select(resource => resource.Path!.Value));
            Assert.Equal(
                vocabulary.Terms.Select(term => term.Identity.Value),
                expanded.Select(resource => Texts(resource, "identity").Single()));
            Assert.Equal(
                vocabulary.Terms.Select(term => term.DisplayLabel),
                expanded.Select(resource => Texts(resource, "name").Single()));
        }

        ResourceExplanationResource kind = Explain(
            catalog,
            $"vocabularies/{BodyShapeVocabulary.BodyKindsId}/values/"
                + "objectcreationexpression",
            depth: 0).Resources[0];
        Assert.Equal(["ObjectCreationExpression"], Texts(kind, "identity"));
        Assert.IsType<ResourcePathResolution.Invalid>(catalog.Resolve(
            $"vocabularies/{BodyShapeVocabulary.BodyKindsId}/values/"
            + "ObjectCreationExpression"));
        Assert.IsType<ResourcePathResolution.Unknown>(catalog.Resolve(
            $"vocabularies/{BodyShapeVocabulary.BodyKindsId}/values"));
    }

    [Fact]
    public void ValueCarriesTypedMapEntriesAndLinksTermMapTargets()
    {
        VocabularySnapshot snapshot = ProductSnapshot();
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateVocabularies(snapshot);
        VocabularyDefinition choices = snapshot.GetVocabulary(
            new VocabularyIdentity(
                snapshot.Catalog,
                StyleOptionVocabularies.StyleChoicesId));
        VocabularyTerm choice = choices.Terms.First(term =>
            term.Identity.Value.Contains(':', StringComparison.Ordinal));
        var tier = Assert.IsType<ExplanationValue.VocabularyTerm>(
            Assert.Single(choice.GetRequiredValues(choices.GetMap("tier").Identity)));

        ResourceExplanationDocument document = Explain(
            catalog,
            $"vocabularies/{StyleOptionVocabularies.StyleChoicesId}/values/"
                + choice.Identity.Value.Replace(':', '.'),
            depth: 1);
        ResourceExplanationResource value = document.Resources[0];
        Assert.Equal([choice.Identity.Value], Texts(value, "identity"));

        ExplanationValue.Record[] entries = Records(value, "map-entries");
        Assert.Equal(
            choices.Maps
                .Where(map => choice.TryGetValues(map.Identity, out _))
                .Select(map => map.Identity.Value),
            entries.Select(entry => Field(entry, "map")));
        ExplanationValue.Choice tierEntry = MapValue(
            entries.Single(entry => Field(entry, "map") == "tier"));
        Assert.Equal("term", tierEntry.Case.Value);
        Assert.Equal(
            ExplanationValue.Text(tier.Identity.Value),
            tierEntry.Value);
        ExplanationValue.Choice endorsed = MapValue(
            entries.Single(entry => Field(entry, "map") == "oracle_endorsed"));
        Assert.Equal("boolean", endorsed.Case.Value);
        Assert.Equal(
            Assert.Single(choice.GetRequiredValues(
                choices.GetMap("oracle_endorsed").Identity)),
            endorsed.Value);

        ResourceExplanationRelationship link = Assert.Single(
            document.Relationships,
            relationship => relationship.Source == value.Key
                && relationship.Relationship.Value == "term-map-value");
        ResourceExplanationRelationshipTarget target = Assert.Single(link.Targets);
        Assert.Contains(
            document.Resources,
            resource => resource.Key == target.Resource
                && resource.Path!.Value
                    == $"vocabularies/{StyleOptionVocabularies.StyleTiersId}/"
                        + $"values/{tier.Identity.Value.ToLowerInvariant()}");
    }

    [Fact]
    public void ValueSegmentIsTheLowerCasedIdentityWithDotsForColons()
    {
        Assert.Equal(
            "objectcreationexpression",
            ResourceExplanationCatalog.VocabularyValueSegment(
                "ObjectCreationExpression"));
        Assert.Equal(
            "var-spelling-style.var-elsewhere",
            ResourceExplanationCatalog.VocabularyValueSegment(
                "var-spelling-style:var-elsewhere"));
        Assert.Throws<InvalidOperationException>(() =>
            ResourceExplanationCatalog.VocabularyValueSegment("Ünicode"));
        Assert.Throws<InvalidOperationException>(() =>
            ResourceExplanationCatalog.VocabularyValueSegment("two words"));
    }

    [Fact]
    public void ValuesSharingOneSegmentFailVisibly()
    {
        VocabularyCatalogIdentity catalog = ProductVocabularyComposition.Catalog;
        var colors = new VocabularyIdentity(catalog, "test.colors");
        VocabularySnapshot snapshot = ProductVocabularyComposition.Compose(
            [
                new(
                    new VocabularyDefinition(
                        colors,
                        "Colors",
                        "Test colors.",
                        maps: [],
                        [
                            new(new(colors, "Red"), "Red", null),
                            new(new(colors, "red"), "Lower red", null),
                        ]),
                    "test.paint"),
            ]);

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => ResourceExplanationCatalog.CreateVocabularies(snapshot));
        Assert.Contains("share one resource-path segment", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TermMapIsATypedRelationshipToItsTargetVocabulary()
    {
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateVocabularies(ProductSnapshot());

        ResourceExplanationDocument choices = Explain(
            catalog,
            $"vocabularies/{StyleOptionVocabularies.StyleChoicesId}",
            depth: 1);
        ResourceExplanationRelationship tier = Assert.Single(
            choices.Relationships,
            relationship => relationship.Source == choices.Resources[0].Key
                && relationship.Relationship.Value == "term-map-target");
        ResourceExplanationRelationshipTarget target = Assert.Single(tier.Targets);
        Assert.Equal(StyleOptionVocabularies.StyleTiersId, KeyIdentity(target.Resource));
        Assert.Contains(
            choices.Resources,
            resource => resource.Path!.Value
                == $"vocabularies/{StyleOptionVocabularies.StyleTiersId}");

        ResourceExplanationDocument tiers = Explain(
            catalog,
            $"vocabularies/{StyleOptionVocabularies.StyleTiersId}",
            depth: 0);
        Assert.Empty(Assert.Single(
            tiers.Relationships,
            relationship => relationship.Relationship.Value == "term-map-target").Targets);
    }

    [Fact]
    public void AnyIndexedVocabularyIsExplainedFromTheSnapshotAlone()
    {
        VocabularyCatalogIdentity catalog = ProductVocabularyComposition.Catalog;
        VocabularySnapshot snapshot = ProductVocabularyComposition.Compose(
            [
                .. ProductContributions(catalog),
                new(
                    new VocabularyDefinition(
                        new(catalog, "test.colors"),
                        "Colors",
                        "Test colors.",
                        maps: [],
                        [new(new(new(catalog, "test.colors"), "red"), "Red", null)]),
                    "test.paint"),
            ]);

        ResourceExplanationCatalog explained =
            ResourceExplanationCatalog.CreateVocabularies(snapshot);
        ResourceExplanationResource red = Explain(
            explained,
            "vocabularies/test.colors/values/red",
            depth: 0).Resources[0];
        Assert.Equal(["red"], Texts(red, "identity"));
        Assert.Equal(["Red"], Texts(red, "name"));
        Assert.Equal(
            ExplanationObservationState.Absent,
            Assert.Single(red.Facts, fact => fact.Fact.Value == "summary").State);
        Assert.Equal(
            ["test.paint"],
            Texts(
                Explain(explained, "vocabularies/test.colors", depth: 0)
                    .Resources[0],
                "accepted-by"));
    }

    [Fact]
    public void UnknownVocabularyPathSuggestsCanonicalPaths()
    {
        ResourceExplanationCatalog catalog =
            ResourceExplanationCatalog.CreateVocabularies(ProductSnapshot());

        var unknown = Assert.IsType<ResourcePathResolution.Unknown>(
            catalog.Resolve("vocabularies/csharp.body-kind"));
        Assert.Contains(
            $"vocabularies/{BodyShapeVocabulary.BodyKindsId}",
            unknown.Suggestions.Select(suggestion => suggestion.Value));
    }

    private static VocabularySnapshot ProductSnapshot() =>
        ProductVocabularyComposition.Compose(
            ProductContributions(ProductVocabularyComposition.Catalog));

    private static ProductVocabularyContribution[] ProductContributions(
        VocabularyCatalogIdentity catalog) =>
            [
                new(
                    ApiAccessibilityVocabulary.Declare(catalog),
                    "api.type-inventory",
                    "api.member-inventory"),
                new(
                    StyleOptionVocabularies.DeclareStyleTiers(catalog),
                    "decompiler.style-picker"),
                new(
                    StyleOptionVocabularies.DeclareStyleChoices(catalog),
                    "decompiler.style-picker",
                    "decompiler.render"),
                new(BodyShapeVocabulary.Declare(catalog), "decompiler.body-kind"),
            ];

    private static ResourceExplanationDocument Explain(
        ResourceExplanationCatalog catalog,
        string path,
        int depth)
    {
        var resolved = Assert.IsType<ResourcePathResolution.Resolved>(
            catalog.Resolve(path));
        return catalog.Explain(
            resolved,
            ResourceExplanationRequest.ForHost(depth)).Content;
    }

    private static ExplanationValue.Record[] Records(
        ResourceExplanationResource resource,
        string fact) =>
        [
            .. Assert.Single(
                    resource.Facts,
                    observation => observation.Fact.Value == fact)
                .Values
                .Select(value => Assert.IsType<ExplanationValue.Record>(value)),
        ];

    private static string Field(ExplanationValue.Record record, string field) =>
        OptionalField(record, field)
            ?? throw new InvalidOperationException($"Field '{field}' is empty.");

    private static string? OptionalField(
        ExplanationValue.Record record,
        string field) =>
        record.Fields.Single(value => value.Field.Value == field).Values switch
        {
            [] => null,
            [ExplanationValue.Scalar { Value.Text: { } text }] => text,
            _ => throw new InvalidOperationException(
                $"Field '{field}' is not one text value."),
        };

    private static ExplanationValue.Choice MapValue(ExplanationValue.Record entry) =>
        Assert.IsType<ExplanationValue.Choice>(
            Assert.Single(
                entry.Fields.Single(field => field.Field.Value == "value").Values));

    private static string KeyIdentity(ExplanationResourceKey key)
    {
        string packed = Assert.IsType<ExplanationValue.Scalar>(key.IdentityValue)
            .Value.Text!;
        return packed[(packed.IndexOf(':', StringComparison.Ordinal) + 1)..];
    }

    private static string[] Texts(
        ResourceExplanationResource resource,
        string fact) =>
        [
            .. Assert.Single(
                    resource.Facts,
                    observation => observation.Fact.Value == fact)
                .Values
                .Select(value =>
                    Assert.IsType<ExplanationValue.Scalar>(value).Value.Text!),
        ];

    private static int Integer(
        ResourceExplanationResource resource,
        string fact) =>
        (int)Assert.IsType<ExplanationValue.Scalar>(
            Assert.Single(
                Assert.Single(
                    resource.Facts,
                    observation => observation.Fact.Value == fact).Values))
            .Value.Integer!.Value;
}
