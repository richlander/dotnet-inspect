using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class SectionPipelineTests
{
    [Fact]
    public void LibraryCallUsePipeline_UsesAuthoredCategoryWithoutComputedPoles()
    {
        SectionPipeline<LibraryCallUseDiscoveryModel> pipeline =
            LibraryCallUseSections.CreatePipeline();

        var category = Assert.Single(pipeline.GetCategoryMap());
        Assert.Equal(SectionCategoryNames.Libraries, category.Key);
        Assert.Equal(
            [
                LibraryCallUseSections.CallSites,
                LibraryCallUseSections.ConsumerUseSites,
                LibraryCallUseSections.DirectUseClusters,
                LibraryCallUseSections.ProviderApiTypes,
            ],
            category.Value);
        Assert.Equal(
            [
                LibraryCallUseSections.ConsumerUseSites,
                LibraryCallUseSections.ProviderApiTypes,
                LibraryCallUseSections.DirectUseClusters,
                LibraryCallUseSections.CallSites,
                LibraryCallUseSections.PublicRootPaths,
            ],
            pipeline.SelectableSectionNames);
        Assert.Equal(
            category.Value.Order(StringComparer.OrdinalIgnoreCase),
            pipeline.BaseSectionNames.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            [LibraryCallUseSections.PublicRootPaths],
            pipeline.GetCatalogHiddenSections());
        Assert.DoesNotContain(
            LibraryCallUseSections.PublicRootPaths,
            category.Value);
    }

    [Fact]
    public void LibraryCallUsePipeline_DeclaresOrderCostsAndPresets()
    {
        SectionPipeline<LibraryCallUseDiscoveryModel> pipeline =
            LibraryCallUseSections.CreatePipeline();

        Assert.Equal(
            pipeline.SelectableSectionNames
                .Order(StringComparer.OrdinalIgnoreCase),
            pipeline.AlphabeticalSectionOrder);
        Assert.All(
            pipeline.SectionCosts,
            section => Assert.Equal(SectionCost.Unbounded, section.Cost));
        Assert.Equal(
            [
                LibraryCallUseSections.ConsumerUseSites,
                LibraryCallUseSections.ProviderApiTypes,
            ],
            LibraryCallUseSections.BareSelectSectionNames);
        Assert.Equal(
            [LibraryCallUseSections.PublicRootPaths],
            LibraryCallUseSections.ExactOnlySectionNames);
    }
}
