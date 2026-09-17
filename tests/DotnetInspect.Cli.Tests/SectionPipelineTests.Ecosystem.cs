using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class SectionPipelineTests
{
    [Fact]
    public void EcosystemPipelines_UseRouteSpecificAuthoredCategories()
    {
        AssertCatalog(
            EcosystemSections.CreateCatalogWidePipeline(),
            EcosystemSections.EcosystemsSection,
            includePruning: false);
        AssertCatalog(
            EcosystemSections.CreateFocusedPipeline(),
            EcosystemSections.InfoSection,
            includePruning: false);
        AssertCatalog(
            EcosystemSections.CreatePlatformPipeline(),
            EcosystemSections.InfoSection,
            includePruning: true);
    }

    [Fact]
    public void EcosystemPipelines_DeclareBareSelectionAndCosts()
    {
        AssertAutomaticMetadata(
            EcosystemSections.CreateCatalogWidePipeline(),
            includePruning: false);
        AssertAutomaticMetadata(
            EcosystemSections.CreateFocusedPipeline(),
            includePruning: false);
        AssertAutomaticMetadata(
            EcosystemSections.CreatePlatformPipeline(),
            includePruning: true);
    }

    private static void AssertCatalog(
        SectionPipeline<EcosystemDiscoveryModel> pipeline,
        string identity,
        bool includePruning)
    {
        IReadOnlyDictionary<string, string[]> categories =
            pipeline.GetCategoryMap();
        string[] expectedSections =
        [
            identity,
            EcosystemSections.NamespaceHintsSection,
            EcosystemSections.CorePackagesSection,
            EcosystemSections.ToolPackagesSection,
            EcosystemSections.KnownIntegrationsSection,
            EcosystemSections.DemosSection,
            .. includePruning
                ? [EcosystemSections.PruningSection]
                : Array.Empty<string>(),
        ];

        Assert.Equal(
            [
                SectionCategoryNames.Ecosystem,
                SectionCategoryNames.Integrations,
            ],
            categories.Keys);
        Assert.Equal(
            expectedSections,
            categories[SectionCategoryNames.Ecosystem]);
        Assert.Equal(
            [EcosystemSections.KnownIntegrationsSection],
            categories[SectionCategoryNames.Integrations]);
        Assert.Equal(expectedSections, pipeline.SelectableSectionNames);
        Assert.Equal(expectedSections, pipeline.BaseSectionNames);
        Assert.Empty(pipeline.GetCatalogHiddenSections());
    }

    private static void AssertAutomaticMetadata(
        SectionPipeline<EcosystemDiscoveryModel> pipeline,
        bool includePruning)
    {
        Assert.Equal(pipeline.SelectableSectionNames, pipeline.InfoSectionNames);
        Assert.All(
            pipeline.SectionCosts,
            section => Assert.Equal(SectionCost.NetworkFree, section.Cost));
        Assert.Equal(
            includePruning,
            pipeline.SelectableSectionNames.Contains(
                EcosystemSections.PruningSection));
        Assert.Equal(
            pipeline.SelectableSectionNames
                .Order(StringComparer.OrdinalIgnoreCase),
            pipeline.AlphabeticalSectionOrder);
    }
}
