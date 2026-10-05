using DotnetInspector.Sections;
using QuerySpace.Vocabulary;

namespace DotnetInspector.Sections.Tests;

/// <summary>
/// Hosts compose the product vocabulary from owner declarations. The
/// composition adds only the catalog identity and the section index, rejects
/// foreign-catalog declarations, and yields equal identities for equal lists.
/// </summary>
public sealed class ProductVocabularyCompositionTests
{
    [Fact]
    public void ComposesTheSectionIndexFirstWithAcceptedInputsAndValueCounts()
    {
        VocabularySnapshot snapshot = ProductVocabularyComposition.Compose(
            [
                new(Declare("test.colors", "red", "green"), "test.paint"),
                new(Declare("test.sizes", "small"), "test.paint", "test.cut"),
            ]);

        Assert.Equal(ProductVocabularyComposition.FormatVersion, snapshot.FormatVersion);
        Assert.Equal(ProductVocabularyComposition.Catalog, snapshot.Catalog);
        Assert.Equal(
            ["vocabulary.sections", "test.colors", "test.sizes"],
            snapshot.Vocabularies.Select(vocabulary => vocabulary.Identity.Value));

        VocabularyDefinition index = snapshot.Vocabularies[0];
        Assert.Equal(ProductVocabularyComposition.SectionsLabel, index.DisplayLabel);
        Assert.Equal(["test.colors", "test.sizes"], index.Terms.Select(term => term.Identity.Value));
        VocabularyTerm sizes = index.GetTerm("test.sizes");
        Assert.Equal(
            ["test.paint", "test.cut"],
            sizes.GetRequiredValues(index.GetMap("accepted_by").Identity)
                .Select(value => ((VocabularyMapValue.Scalar)value).Value.Text));
        var count = Assert.IsType<VocabularyMapValue.Scalar>(
            Assert.Single(sizes.GetRequiredValues(index.GetMap("values").Identity)));
        Assert.Equal(1, count.Value.Integer);
    }

    [Fact]
    public void EqualContributionListsYieldOneIdentity()
    {
        VocabularySnapshot first = ProductVocabularyComposition.Compose(
            [new(Declare("test.colors", "red"), "test.paint")]);
        VocabularySnapshot second = ProductVocabularyComposition.Compose(
            [new(Declare("test.colors", "red"), "test.paint")]);
        VocabularySnapshot differentInput = ProductVocabularyComposition.Compose(
            [new(Declare("test.colors", "red"), "test.cut")]);

        Assert.Equal(first.Identity, second.Identity);
        Assert.NotEqual(first.Identity, differentInput.Identity);
    }

    [Fact]
    public void RejectsADeclarationUnderAnotherCatalog()
    {
        var foreign = new VocabularyCatalogIdentity("test.other");
        VocabularyDefinition vocabulary = new(
            new VocabularyIdentity(foreign, "test.colors"),
            "Colors",
            "Test colors.",
            maps: [],
            [new VocabularyTerm(new(new VocabularyIdentity(foreign, "test.colors"), "red"), "Red", summary: null)]);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => ProductVocabularyComposition.Compose([new(vocabulary, "test.paint")]));
        Assert.Contains("test.other", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAContributionWithNoAcceptingInput()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new ProductVocabularyContribution(Declare("test.colors", "red")));
        Assert.Contains("test.colors", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsTwoContributionsWithOneIdentity()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => ProductVocabularyComposition.Compose(
                [
                    new(Declare("test.colors", "red"), "test.paint"),
                    new(Declare("test.colors", "blue"), "test.paint"),
                ]));
        Assert.Contains("test.colors", error.Message, StringComparison.Ordinal);
    }

    private static VocabularyDefinition Declare(string id, params string[] terms)
    {
        var identity = new VocabularyIdentity(ProductVocabularyComposition.Catalog, id);
        return new(
            identity,
            id,
            $"Summary of {id}.",
            maps: [],
            terms.Select(term => new VocabularyTerm(new(identity, term), term, summary: null)));
    }
}
