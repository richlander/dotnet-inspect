using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
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
        Assert.DoesNotContain(
            DiscoveryOutputMode.Json,
            LibraryOutputCapabilities.Catalog.FormatsForSection(
                SectionNames.NameFamilies));
        Assert.DoesNotContain(
            DiscoveryOutputMode.Json,
            LibraryOutputCapabilities.Catalog.FormatsForSection(
                SectionNames.NameFamilyRoles));
        Assert.DoesNotContain(
            DiscoveryOutputMode.Json,
            LibraryOutputCapabilities.Catalog.FormatsForSection(
                SectionNames.NameFamilyRoleTypes));
    }

    [Fact]
    public void ApiCatalogAdvertisesOnlyExecutedFormatsPerRoute()
    {
        // The executed set: Tables lower to the row formats, Texts to their
        // payload, JSON only where the type document or a dedicated lowering
        // carries the section (derived from the JSON code's own tables), and
        // the type-command-only JSON sections only on the type command's view.
        OutputCapabilityCatalog detail = ApiOutputCapabilities.For(
            StructuralViewRegistry.Route(
                StructuralViewIdentity.MemberTarget,
                InspectionCatalogIdentity.ApiMemberDetail));
        OutputCapabilityCatalog typeView = ApiOutputCapabilities.For(
            StructuralViewRegistry.Route(
                StructuralViewIdentity.Type,
                InspectionCatalogIdentity.ApiMember));
        OutputCapabilityCatalog memberTypeView = ApiOutputCapabilities.For(
            StructuralViewRegistry.Route(
                StructuralViewIdentity.MemberType,
                InspectionCatalogIdentity.ApiMember));
        OutputCapabilityCatalog listing = ApiOutputCapabilities.For(
            StructuralViewRegistry.Route(
                StructuralViewIdentity.Type,
                InspectionCatalogIdentity.ApiType));

        Assert.Equal(ApiOutputCapabilities.TextFormats, detail.FormatsForSection(SectionNames.DecompiledSource));
        Assert.Equal(ApiOutputCapabilities.TextFormats, detail.FormatsForSection(SectionNames.IL));
        Assert.Equal(
            [DiscoveryOutputMode.Markdown, DiscoveryOutputMode.PlainText, DiscoveryOutputMode.Json],
            detail.FormatsForSection(SectionNames.Source));
        Assert.Equal(
            [DiscoveryOutputMode.Markdown, DiscoveryOutputMode.PlainText, DiscoveryOutputMode.Json],
            detail.FormatsForSection(SectionNames.FindingCensus));
        Assert.Equal(ApiOutputCapabilities.TableFormats, detail.FormatsForSection(SectionNames.Signature));
        Assert.Equal(ApiOutputCapabilities.TableFormats, detail.FormatsForSection(SectionNames.ExceptionRegions));
        Assert.Equal(OutputCapabilityCatalog.StandardSectionFormats, detail.FormatsForSection(SectionNames.Calls));
        Assert.Equal(OutputCapabilityCatalog.FormatOrder, detail.FormatsForSection(SectionNames.CallGraph));
        Assert.Equal(ApiOutputCapabilities.TableFormats, typeView.FormatsForSection(SectionNames.TypeMetrics));
        Assert.Equal(ApiOutputCapabilities.TableFormats, typeView.FormatsForSection(SectionNames.PerformanceTriage));
        Assert.Equal(OutputCapabilityCatalog.StandardSectionFormats, typeView.FormatsForSection(SectionNames.Methods));
        Assert.Equal(OutputCapabilityCatalog.StandardSectionFormats, typeView.FormatsForSection(SectionNames.TypeInfo));
        Assert.Contains(DiscoveryOutputMode.Json, typeView.FormatsForSection(SectionNames.ApiDeclarations));
        Assert.DoesNotContain(DiscoveryOutputMode.Json, memberTypeView.FormatsForSection(SectionNames.ApiDeclarations));
        Assert.Equal(OutputCapabilityCatalog.StandardSectionFormats, listing.FormatsForSection(SectionNames.Classes));
    }

    [Fact]
    public void ApiCatalogDerivesFormatsFromShapesAndKeepsCallGraphFormats()
    {
        // Type and member formats derive from each section's declared shape;
        // Call Graph is a Graph rather than a shape, so it alone keeps the
        // tree and Mermaid lowerings beside the standard formats. A Text
        // advertises only the formats the CLI executes for it today: the row
        // formats need a fact row the type and member owners do not have yet
        // (round-4 finding: --jsonl on Decompiled Source returned nothing).
        OutputCapabilityCatalog catalog = ApiOutputCapabilities.For(
            StructuralViewRegistry.Route(
                StructuralViewIdentity.MemberTarget,
                InspectionCatalogIdentity.ApiMemberDetail));

        // Methods lives in the overload catalog, not the exact-member one.
        Assert.Equal(
            OutputCapabilityCatalog.StandardSectionFormats,
            ApiOutputCapabilities.For(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberTarget,
                    InspectionCatalogIdentity.ApiMemberOverload))
                .FormatsForSection(SectionNames.Methods));
        Assert.Equal(
            [
                DiscoveryOutputMode.Markdown,
                DiscoveryOutputMode.PlainText,
                DiscoveryOutputMode.Json,
            ],
            catalog.FormatsForSection(SectionNames.Source));
        Assert.Equal(
            ApiOutputCapabilities.TextFormats,
            catalog.FormatsForSection(SectionNames.SemanticsOverlay));
        Assert.Equal(
            OutputCapabilityCatalog.FormatOrder,
            catalog.FormatsForSection(SectionNames.CallGraph));
        Assert.Empty(catalog.FormatsForSection("Not A Section"));
    }
}
