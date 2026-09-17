using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class SectionPipelineTests
{
    [Fact]
    public void ProjectPipeline_UsesAuthoredCategoryWithoutComputedPoles()
    {
        SectionPipeline<ProjectDiscoveryModel> pipeline =
            ProjectSections.CreatePipeline();

        var category = Assert.Single(pipeline.GetCategoryMap());
        Assert.Equal(SectionCategoryNames.Project, category.Key);
        Assert.Equal(
            [
                ProjectSections.Skills.Name,
                ProjectSections.PackageReadme.Name,
            ],
            category.Value);
        Assert.Equal(category.Value, pipeline.BaseSectionNames);
        Assert.Equal(
            [
                ProjectSections.Skills.Name,
                ProjectSections.PackageReadme.Name,
            ],
            pipeline.SelectableSectionNames);
        Assert.Equal(
            [ProjectSections.Skills.Name],
            pipeline.InfoSectionNames);
        Assert.Empty(pipeline.GetCatalogHiddenSections());
    }

    [Fact]
    public void ProjectPipeline_DeclaresDocumentCostsAndGrowth()
    {
        SectionPipeline<ProjectDiscoveryModel> pipeline =
            ProjectSections.CreatePipeline();

        Assert.Equal(
            SectionSizeClass.Verbose,
            ProjectSections.Skills.SizeClass);
        Assert.Equal(
            SectionCost.NetworkFree,
            Assert.Single(
                pipeline.SectionCosts,
                section => section.Name == ProjectSections.Skills.Name).Cost);
        Assert.Equal(
            SectionSizeClass.Verbose,
            ProjectSections.PackageReadme.SizeClass);
        Assert.True(ProjectSections.PackageReadme.ExplicitOnly);
        Assert.Equal(
            SectionCost.Unbounded,
            Assert.Single(
                pipeline.SectionCosts,
                section =>
                    section.Name == ProjectSections.PackageReadme.Name).Cost);
        Assert.Empty(pipeline.BareSelectSectionNames);
    }
}
