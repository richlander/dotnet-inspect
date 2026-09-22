using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;

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
                DiscoveryOutputMode.Markdown,
                DiscoveryOutputMode.PlainText,
                DiscoveryOutputMode.Json,
                DiscoveryOutputMode.Table,
                DiscoveryOutputMode.Tsv,
                DiscoveryOutputMode.Jsonl,
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
            [
                DiscoveryOutputMode.Markdown,
                DiscoveryOutputMode.PlainText,
            ],
            catalog.FormatsForSelection(sections));
        Assert.False(
            catalog.Supports(DiscoveryOutputMode.Tree, sections));
        Assert.False(
            catalog.Supports(DiscoveryOutputMode.Table, sections));
        Assert.False(
            catalog.Supports(DiscoveryOutputMode.Json, sections));
    }

    [Fact]
    public void LibraryPerformanceKindsRemainOneHomogeneousRowFamily()
    {
        OutputCapabilityCatalog catalog =
            LibraryOutputCapabilities.Catalog;

        Assert.True(
            catalog.Supports(
                DiscoveryOutputMode.Table,
                PerformanceKinds.Sections));
        Assert.True(
            catalog.Supports(
                DiscoveryOutputMode.Tsv,
                PerformanceKinds.Sections));
        Assert.True(
            catalog.Supports(
                DiscoveryOutputMode.Jsonl,
                PerformanceKinds.Sections));
        Assert.False(
            catalog.Supports(
                DiscoveryOutputMode.Table,
                LibrarySections.SectionCatalog.SelectionCategoryMap[
                    SectionCategoryNames.Performance]));
    }

    [Fact]
    public void LibraryMetricsSectionsExcludeDocumentJson()
    {
        Assert.DoesNotContain(
            DiscoveryOutputMode.Json,
            LibraryOutputCapabilities.Catalog.FormatsForSection(
                SectionNames.MemberMetrics));
        Assert.DoesNotContain(
            DiscoveryOutputMode.Json,
            LibraryOutputCapabilities.Catalog.FormatsForSection(
                SectionNames.LibraryMetrics));
    }
}
