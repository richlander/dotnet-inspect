using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public class OutputCapabilityCatalogTests
{
    [Fact]
    public void LibraryReferencesAndHierarchyDeclareDistinctTopologyFormats()
    {
        OutputCapabilityCatalog catalog =
            LibraryOutputCapabilities.Catalog;

        Assert.Equal(
            [
                OutputMode.Markdown,
                OutputMode.PlainText,
                OutputMode.Json,
                OutputMode.Table,
                OutputMode.Tsv,
                OutputMode.Jsonl,
            ],
            catalog.FormatsForSection(SectionNames.References));
        Assert.Equal(
            OutputCapabilityCatalog.FormatOrder,
            catalog.FormatsForSection(
                SectionNames.ReferenceHierarchy));
    }

    [Fact]
    public void LibraryDependencyCategoryPreservesCompleteExpansion()
    {
        OutputCapabilityCatalog catalog =
            LibraryOutputCapabilities.Catalog;
        string[] sections =
            LibrarySections.SectionCatalog.SelectionCategoryMap[
                SectionCategoryNames.Dependencies];

        Assert.Equal(
            [OutputMode.Markdown, OutputMode.PlainText],
            catalog.FormatsForSelection(sections));
        Assert.False(
            catalog.Supports(OutputMode.Tree, sections));
        Assert.False(
            catalog.Supports(OutputMode.Table, sections));
        Assert.False(
            catalog.Supports(OutputMode.Json, sections));
    }

    [Fact]
    public void LibraryPerformanceKindsRemainOneHomogeneousRowFamily()
    {
        OutputCapabilityCatalog catalog =
            LibraryOutputCapabilities.Catalog;

        Assert.True(
            catalog.Supports(
                OutputMode.Table,
                PerformanceKinds.Sections));
        Assert.True(
            catalog.Supports(
                OutputMode.Tsv,
                PerformanceKinds.Sections));
        Assert.True(
            catalog.Supports(
                OutputMode.Jsonl,
                PerformanceKinds.Sections));
        Assert.False(
            catalog.Supports(
                OutputMode.Table,
                LibrarySections.SectionCatalog.SelectionCategoryMap[
                    SectionCategoryNames.Performance]));
    }

    [Fact]
    public void LibraryImplementationProfilesExcludesDocumentJson()
    {
        Assert.DoesNotContain(
            OutputMode.Json,
            LibraryOutputCapabilities.Catalog.FormatsForSection(
                SectionNames.ImplementationProfiles));
    }
}
