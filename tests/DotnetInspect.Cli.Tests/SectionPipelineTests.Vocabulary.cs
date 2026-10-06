using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace DotnetInspect.Cli.Tests;

public partial class SectionPipelineTests
{
    [Fact]
    public void VocabularyPipeline_UsesAuthoredCategoriesWithoutComputedPoles()
    {
        SectionPipeline<VocabularyDocument> pipeline =
            VocabularySections.CreatePipeline();
        IReadOnlyDictionary<string, string[]> categories =
            pipeline.GetCategoryMap();

        Assert.Equal(
            [
                SectionCategoryNames.Api,
                SectionCategoryNames.Decompiler,
                SectionCategoryNames.Query,
                SectionCategoryNames.Vocabulary,
            ],
            categories.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                ProductVocabularyComposition.SectionsLabel,
                ApiAccessibilityVocabulary.AccessibilityLabel,
                StyleOptionVocabularies.StyleTiersLabel,
                StyleOptionVocabularies.StyleChoicesLabel,
                BodyShapeVocabulary.BodyKindsLabel,
                PackageQueryDurableRowVocabulary.Label,
            ],
            categories[SectionCategoryNames.Vocabulary]);
        Assert.Equal(
            [ApiAccessibilityVocabulary.AccessibilityLabel],
            categories[SectionCategoryNames.Api]);
        Assert.Equal(
            [
                BodyShapeVocabulary.BodyKindsLabel,
                StyleOptionVocabularies.StyleChoicesLabel,
                StyleOptionVocabularies.StyleTiersLabel,
            ],
            categories[SectionCategoryNames.Decompiler]);
        Assert.Equal(
            [PackageQueryDurableRowVocabulary.Label],
            categories[SectionCategoryNames.Query]);
        Assert.Equal(
            pipeline.SelectableSectionNames,
            pipeline.BaseSectionNames);
        Assert.Equal(
            [ProductVocabularyComposition.SectionsLabel],
            pipeline.InfoSectionNames);
        Assert.Empty(pipeline.GetCatalogHiddenSections());
    }

    [Fact]
    public void VocabularyPipeline_RegistersExactlyTheOwnerIssuedSections()
    {
        SectionPipeline<VocabularyDocument> pipeline =
            VocabularySections.CreatePipeline();

        Assert.Equal(
            CliVocabularyDocument.Document.Sections
                .Select(section => section.Name)
                .Order(StringComparer.OrdinalIgnoreCase),
            pipeline.SelectableSectionNames
                .Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            [
                ApiAccessibilityVocabulary.AccessibilityLabel,
                BodyShapeVocabulary.BodyKindsLabel,
                StyleOptionVocabularies.StyleChoicesLabel,
                StyleOptionVocabularies.StyleTiersLabel,
                PackageQueryDurableRowVocabulary.Label,
                ProductVocabularyComposition.SectionsLabel,
            ],
            pipeline.AlphabeticalSectionOrder);
    }

    [Fact]
    public void VocabularyPipeline_DeclaresExplicitNetworkFreeVocabularies()
    {
        SectionPipeline<VocabularyDocument> pipeline =
            VocabularySections.CreatePipeline();

        Assert.All(
            pipeline.SectionCosts,
            section => Assert.Equal(SectionCost.NetworkFree, section.Cost));
        Assert.True(VocabularySections.Accessibility.ExplicitOnly);
        Assert.True(VocabularySections.BodyKinds.ExplicitOnly);
        Assert.True(VocabularySections.PackageQueryDurableRow.ExplicitOnly);
        Assert.True(VocabularySections.StyleChoices.ExplicitOnly);
        Assert.True(VocabularySections.StyleTiers.ExplicitOnly);
        Assert.Empty(pipeline.BareSelectSectionNames);
    }
}
