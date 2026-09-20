using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Tests;

public class DiscoveryDocumentFactoryTests
{
    [Fact]
    public void LibraryCatalog_PreservesResourcesAndStableCatalogOrder()
    {
        StructuralSchemaProjection projection = LibraryProjection();

        DiscoveryDocument document = Create(
            projection,
            discover: null);

        Assert.Equal("library", document.Catalog);
        Assert.NotEmpty(document.CatalogEntries);
        Assert.Equal(
            DiscoveryResourceKind.Category,
            document.CatalogEntries[0].Kind);
        Assert.Contains(
            document.Resources,
            resource =>
                resource.Identity
                    == Section(SectionNames.ReferenceHierarchy)
                && resource.Members.Length > 0);
        Assert.Contains(
            document.Resources,
            resource =>
                resource.Identity.Kind == DiscoveryResourceKind.Section
                && resource.Members.IsEmpty);
    }

    [Fact]
    public void DependencyCategory_ReferencesCanonicalSectionsAndCapabilities()
    {
        StructuralSchemaProjection projection = LibraryProjection();

        DiscoveryDocument document = Create(
            projection,
            [SectionCategoryNames.Dependencies]);
        DiscoveryResource category = document.GetResource(
            Category(SectionCategoryNames.Dependencies));

        Assert.Equal(
            [
                Section(SectionNames.ReferenceHierarchy),
                Section(SectionNames.References),
            ],
            category.Members);
        Assert.Equal(
            [
                DiscoveryOutputMode.Markdown,
                DiscoveryOutputMode.PlainText,
            ],
            category.OutputModes);
        Assert.Equal(
            [Category(SectionCategoryNames.Dependencies)],
            document.Selection.AddressedResources);
        Assert.Equal(category.Members, document.Selection.Rows);
    }

    [Fact]
    public void SharedSection_HasOneResourceAcrossCategoryMemberships()
    {
        StructuralSchemaProjection projection = LibraryProjection();
        DiscoveryDocument document = Create(
            projection,
            discover: null);
        DiscoveryResourceIdentity references =
            Section(SectionNames.References);

        Assert.Single(
            document.Resources,
            resource => resource.Identity == references);
        Assert.True(
            document.Resources.Count(resource =>
                resource.Identity.Kind == DiscoveryResourceKind.Category
                && resource.Members.Contains(references)) >= 2);
    }

    [Fact]
    public void SameNamedItems_RetainOwningSectionIdentity()
    {
        StructuralSchemaProjection projection = LibraryProjection();
        DiscoveryDocument document = Create(
            projection,
            discover: null);
        var duplicateName = document.Resources
            .Where(resource =>
                resource.Identity.Kind == DiscoveryResourceKind.Item)
            .GroupBy(
                resource => resource.Identity.Name,
                StringComparer.Ordinal)
            .First(group => group
                .Select(resource => resource.Identity.Section)
                .Distinct(StringComparer.Ordinal)
                .Count() > 1);

        Assert.All(
            duplicateName,
            resource => Assert.NotNull(resource.Identity.Section));
        Assert.Equal(
            duplicateName.Count(),
            duplicateName
                .Select(resource => resource.Identity)
                .Distinct()
                .Count());
    }

    [Fact]
    public void QueryItems_RetainKindBesideSameNamedColumns()
    {
        StructuralSchemaProjection projection = LibraryProjection();
        DiscoveryDocument document = Create(
            projection,
            [SectionNames.PerformanceBoxing]);

        Assert.Contains(
            document.Selection.Rows,
            identity =>
                identity.Name == "Confidence"
                && identity.ItemKind == "column");
        Assert.Contains(
            document.Selection.Rows,
            identity =>
                identity.Name == "Confidence"
                && identity.ItemKind == "filterable");
        Assert.Contains(
            document.Selection.Rows,
            identity =>
                identity.Name == "Confidence"
                && identity.ItemKind == "sortable");
    }

    private static StructuralSchemaProjection LibraryProjection() =>
        StructuralViewRegistry.Project(
            StructuralViewRegistry.Route(
                StructuralViewIdentity.DirectLibrary,
                InspectionCatalogIdentity.Library));

    private static DiscoveryDocument Create(
        StructuralSchemaProjection projection,
        string[]? discover) =>
        DiscoveryDocumentFactory.Create(
            "library",
            discover,
            projection.Schema,
            projection.SectionCategories,
            projection.CatalogHiddenSections,
            projection.ListedCategoryDoors,
            projection.SectionCostAnnotations,
            projection.ExactOnlySections,
            projection.OutputCapabilities!)
        ?? throw new InvalidOperationException(
            "Expected Library discovery construction to succeed.");

    private static DiscoveryResourceIdentity Category(string name) =>
        new(DiscoveryResourceKind.Category, name);

    private static DiscoveryResourceIdentity Section(string name) =>
        new(DiscoveryResourceKind.Section, name);
}
