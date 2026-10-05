using System.Linq;

using QuerySpace.Vocabulary;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Queries declares its accessibility values as a value vocabulary over a
/// host-supplied catalog identity; the declaration is a complete, ordered
/// projection of <see cref="ApiAccessibility.Values"/>.
/// </summary>
public class ApiAccessibilityVocabularyTests
{
    private static readonly VocabularyCatalogIdentity Catalog = new("test.catalog");

    [Fact]
    public void DeclaresEveryAccessibilityValueInOrderWithDefaults()
    {
        VocabularyDefinition accessibility = ApiAccessibilityVocabulary.Declare(Catalog);

        Assert.Equal(new VocabularyIdentity(Catalog, "api.accessibility"), accessibility.Identity);
        Assert.Equal("Accessibility", accessibility.DisplayLabel);
        Assert.Equal(["order", "default"], accessibility.Maps.Select(map => map.Identity.Value));
        Assert.Equal(
            ApiAccessibility.Values.Select(bucket => bucket.Id),
            accessibility.Terms.Select(term => term.Identity.Value));
        foreach ((ApiAccessibilityBucket bucket, VocabularyTerm term) in
            ApiAccessibility.Values.Zip(accessibility.Terms))
        {
            Assert.Equal(bucket.Label, term.DisplayLabel);
            var order = Assert.IsType<VocabularyMapValue.Scalar>(
                Assert.Single(term.GetRequiredValues(accessibility.GetMap("order").Identity)));
            Assert.Equal(bucket.Order, order.Value.Integer);
            var isDefault = Assert.IsType<VocabularyMapValue.Scalar>(
                Assert.Single(term.GetRequiredValues(accessibility.GetMap("default").Identity)));
            Assert.Equal(bucket.IsDefault, isDefault.Value.Boolean);
        }
        Assert.Single(accessibility.Terms, term =>
            ((VocabularyMapValue.Scalar)term.GetRequiredValues(
                accessibility.GetMap("default").Identity)[0]).Value.Boolean);
    }

    [Fact]
    public void DeclarationIsDeterministicAcrossCalls()
    {
        VocabularySnapshot first = VocabularySnapshot.Create(
            1, Catalog, [ApiAccessibilityVocabulary.Declare(Catalog)]);
        VocabularySnapshot second = VocabularySnapshot.Create(
            1, Catalog, [ApiAccessibilityVocabulary.Declare(Catalog)]);

        Assert.Equal(first.Identity, second.Identity);
    }
}
