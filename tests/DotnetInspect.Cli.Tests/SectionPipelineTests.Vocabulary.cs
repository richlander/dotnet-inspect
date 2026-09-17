using DotnetInspect.Cli.Sections;
using DotnetInspector.Vocabulary;

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
                SectionCategoryNames.Vocabulary,
            ],
            categories.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            [
                VocabularyCatalog.SectionsSection,
                VocabularyCatalog.AccessibilitySection,
                VocabularyCatalog.StyleTiersSection,
                VocabularyCatalog.StyleChoicesSection,
                VocabularyCatalog.BodyKindsSection,
            ],
            categories[SectionCategoryNames.Vocabulary]);
        Assert.Equal(
            [VocabularyCatalog.AccessibilitySection],
            categories[SectionCategoryNames.Api]);
        Assert.Equal(
            [
                VocabularyCatalog.BodyKindsSection,
                VocabularyCatalog.StyleChoicesSection,
                VocabularyCatalog.StyleTiersSection,
            ],
            categories[SectionCategoryNames.Decompiler]);
        Assert.Equal(
            pipeline.SelectableSectionNames,
            pipeline.BaseSectionNames);
        Assert.Equal(
            [VocabularyCatalog.SectionsSection],
            pipeline.InfoSectionNames);
        Assert.Empty(pipeline.GetCatalogHiddenSections());
    }

    [Fact]
    public void VocabularyPipeline_RegistersExactlyTheOwnerIssuedSections()
    {
        SectionPipeline<VocabularyDocument> pipeline =
            VocabularySections.CreatePipeline();

        Assert.Equal(
            VocabularyCatalog.Document.Sections
                .Select(section => section.Name)
                .Order(StringComparer.OrdinalIgnoreCase),
            pipeline.SelectableSectionNames
                .Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            [
                VocabularyCatalog.AccessibilitySection,
                VocabularyCatalog.BodyKindsSection,
                VocabularyCatalog.StyleChoicesSection,
                VocabularyCatalog.StyleTiersSection,
                VocabularyCatalog.SectionsSection,
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
        Assert.True(VocabularySections.StyleChoices.ExplicitOnly);
        Assert.True(VocabularySections.StyleTiers.ExplicitOnly);
        Assert.Empty(pipeline.BareSelectSectionNames);
    }
}
