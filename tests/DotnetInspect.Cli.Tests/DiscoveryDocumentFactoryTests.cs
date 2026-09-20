using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Tests;

public class DiscoveryDocumentFactoryTests
{
    [Fact]
    public void Catalog_ExposesCategoryAndSectionResourcesInStableOrder()
    {
        var schema = new DocumentSchema()
            .Add("Rows", "column", "Name")
            .AddSection("Empty");
        DiscoveryDocument document = CreateSynthetic(
            schema,
            new Dictionary<string, string[]>
            {
                ["@Group"] = ["Rows", "Empty"],
            },
            MarkdownCapabilities("Rows", "Empty"),
            discover: null);

        Assert.Equal("synthetic", document.Catalog);
        Assert.Equal(
            [
                Category("@Group"),
                Section("Empty"),
                Section("Rows"),
            ],
            document.CatalogEntries);
        Assert.Contains(
            document.Resources,
            resource =>
                resource.Identity == Section("Rows")
                && resource.Members.Length > 0);
        Assert.Contains(
            document.Resources,
            resource =>
                resource.Identity == Section("Empty")
                && resource.Members.IsEmpty);
    }

    [Fact]
    public void CategorySelection_ProjectsDeclaredMembersAndSharedCapabilities()
    {
        var schema = new DocumentSchema()
            .Add("Summary", "column", "Name")
            .Add("Details", "column", "Value")
            .Add("Other", "column", "Value");
        var capabilities = new OutputCapabilityCatalog(
            new Dictionary<string, SectionOutputCapabilities>
            {
                ["Summary"] = SectionOutputCapabilities.Create(
                    [
                        DiscoveryOutputMode.Markdown,
                        DiscoveryOutputMode.PlainText,
                        DiscoveryOutputMode.Json,
                    ]),
                ["Details"] = SectionOutputCapabilities.Create(
                    [
                        DiscoveryOutputMode.Markdown,
                        DiscoveryOutputMode.PlainText,
                    ]),
                ["Other"] = SectionOutputCapabilities.Create(
                    [DiscoveryOutputMode.Markdown]),
            });

        DiscoveryDocument document = CreateSynthetic(
            schema,
            new Dictionary<string, string[]>
            {
                ["@Evidence"] = ["Summary", "Details"],
            },
            capabilities,
            ["@Evidence"]);
        DiscoveryResource category = document.GetResource(
            Category("@Evidence"));

        Assert.Equal(
            [Section("Details"), Section("Summary")],
            category.Members);
        Assert.Equal(
            [
                DiscoveryOutputMode.Markdown,
                DiscoveryOutputMode.PlainText,
            ],
            category.OutputModes);
        Assert.Equal(
            [Category("@Evidence")],
            document.Selection.AddressedResources);
        Assert.Equal(category.Members, document.Selection.Rows);
    }

    [Fact]
    public void LibraryDependencyCategory_ExposesEcosystemDependencies()
    {
        StructuralSchemaProjection projection = LibraryProjection();

        DiscoveryDocument document = Create(
            projection,
            [SectionCategoryNames.Dependencies]);
        DiscoveryResource category = document.GetResource(
            Category(SectionCategoryNames.Dependencies));

        Assert.Contains(
            Section(SectionNames.EcosystemDependencies),
            category.Members);
        Assert.Equal(
            [
                DiscoveryOutputMode.Markdown,
                DiscoveryOutputMode.PlainText,
            ],
            category.OutputModes);
    }

    [Fact]
    public void SharedSection_HasOneResourceAcrossCategoryMemberships()
    {
        var schema = new DocumentSchema()
            .Add("First", "column", "Value")
            .Add("Shared", "column", "Value")
            .Add("Second", "column", "Value");
        DiscoveryDocument document = CreateSynthetic(
            schema,
            new Dictionary<string, string[]>
            {
                ["@First"] = ["First", "Shared"],
                ["@Second"] = ["Second", "Shared"],
            },
            MarkdownCapabilities("First", "Shared", "Second"),
            discover: null);
        DiscoveryResourceIdentity shared = Section("Shared");

        Assert.Single(
            document.Resources,
            resource => resource.Identity == shared);
        Assert.True(
            document.Resources.Count(resource =>
                resource.Identity.Kind == DiscoveryResourceKind.Category
                && resource.Members.Contains(shared)) >= 2);
    }

    [Fact]
    public void SameNamedItems_RetainOwningSectionIdentity()
    {
        var schema = new DocumentSchema()
            .Add("First", "column", "Name")
            .Add("Second", "column", "Name");
        DiscoveryDocument document = CreateSynthetic(
            schema,
            new Dictionary<string, string[]>(),
            MarkdownCapabilities("First", "Second"),
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
        var schema = new DocumentSchema()
            .Add(SectionNames.PerformanceBoxing, "column", "Confidence");
        DiscoveryDocument document = CreateSynthetic(
            schema,
            new Dictionary<string, string[]>(),
            MarkdownCapabilities(SectionNames.PerformanceBoxing),
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

    private static DiscoveryDocument CreateSynthetic(
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]> categories,
        OutputCapabilityCatalog capabilities,
        string[]? discover) =>
        DiscoveryDocumentFactory.Create(
            "synthetic",
            discover,
            schema,
            categories,
            catalogHiddenSections: null,
            listedCategoryDoors: null,
            sectionCostAnnotations: null,
            exactOnlySections: null,
            capabilities)
        ?? throw new InvalidOperationException(
            "Expected synthetic discovery construction to succeed.");

    private static OutputCapabilityCatalog MarkdownCapabilities(
        params string[] sections) =>
        new(sections.ToDictionary(
            static section => section,
            static _ => SectionOutputCapabilities.Create(
                [DiscoveryOutputMode.Markdown]),
            StringComparer.OrdinalIgnoreCase));

    private static DiscoveryResourceIdentity Category(string name) =>
        new(DiscoveryResourceKind.Category, name);

    private static DiscoveryResourceIdentity Section(string name) =>
        new(DiscoveryResourceKind.Section, name);
}
