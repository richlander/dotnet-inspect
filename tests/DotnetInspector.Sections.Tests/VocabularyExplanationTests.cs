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
/// itself is the <c>vocabularies</c> collection, and term maps are typed
/// relationships between vocabularies.
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
                .Select(resource => resource.Path?.Value ?? ""));
        Assert.DoesNotContain(
            catalog.Resources,
            resource => resource.Path?.Value.Contains(
                ProductVocabularyComposition.SectionsId,
                StringComparison.Ordinal) == true);

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
        Assert.Equal(
            ["id (text): equals", "label (text): equals, glob"],
            Texts(bodyKinds, "fields"));
        Assert.Equal(
            declared.Terms.Take(5).Select(term => term.Identity.Value),
            Texts(bodyKinds, "examples"));
        Assert.True(declared.Terms.Length > Texts(bodyKinds, "examples").Length);

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
            resource => resource.Path?.Value
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
    public void IndexedVocabularyWithoutAProjectedSectionFailsVisibly()
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

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => ResourceExplanationCatalog.CreateVocabularies(snapshot));
        Assert.Contains("test.colors", failure.Message, StringComparison.Ordinal);
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
            new ResourceExplanationRequest(depth, 64, 256)).Content;
    }

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
