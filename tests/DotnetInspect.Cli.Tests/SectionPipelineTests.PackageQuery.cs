using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

public partial class SectionPipelineTests
{
    [Fact]
    public void PackageQueryPipeline_UsesAuthoredCategoryWithoutComputedPoles()
    {
        SectionPipeline<PackageQueryView> pipeline =
            PackageQuerySections.Catalog.Pipeline;

        var category = Assert.Single(pipeline.GetCategoryMap());
        Assert.Equal(SectionCategoryNames.Query, category.Key);
        Assert.Equal(
            [
                PackageProfileSections.Packages,
                PackageQuerySections.LiteralStringsName,
                PackageQuerySections.QuerySummaryName,
            ],
            category.Value);
        Assert.Equal(category.Value, pipeline.SelectableSectionNames);
        Assert.Equal(category.Value, pipeline.BaseSectionNames);
        Assert.Empty(pipeline.GetCatalogHiddenSections());
    }

    [Fact]
    public void PackageQueryPipeline_DeclaresOrderCostsAndPreset()
    {
        SectionPipeline<PackageQueryView> pipeline =
            PackageQuerySections.Catalog.Pipeline;

        Assert.Equal(
            pipeline.SelectableSectionNames
                .Order(StringComparer.OrdinalIgnoreCase),
            pipeline.AlphabeticalSectionOrder);
        Assert.Equal(
            [
                SectionCost.Unbounded,
                SectionCost.Unbounded,
                SectionCost.NetworkFree,
            ],
            pipeline.SectionCosts.Select(section => section.Cost));
        Assert.Equal(
            [PackageProfileSections.Packages],
            PackageQuerySections.BareSelectSectionNames);
    }
}
