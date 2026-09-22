using System.Text.Json;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Tests;

public class DiscoveryDocumentFactoryTests
{
    [Fact]
    public void SectionCardinality_ProjectsBesideFormatCapabilities()
    {
        var schema = new DocumentSchema()
            .Add("Scalar", "column", "Value")
            .Add("Inventory", "column", "Name");
        OutputCapabilityCatalog capabilities =
            MarkdownCapabilities("Scalar", "Inventory");
        var cardinalities =
            new Dictionary<string, SectionCardinalityDeclaration>
            {
                ["Scalar"] = SectionCardinalityDeclaration.Scalar,
                ["Inventory"] =
                    SectionCardinalityDeclaration.Inventory,
            };
        DiscoveryDocumentFactory.Projection projection =
            CreateSyntheticProjection(
                schema,
                capabilities,
                cardinalities);

        DiscoveryResource scalar = projection.Document.GetResource(
            Section("Scalar"));
        Assert.Equal(
            SectionSemanticShape.Scalar,
            scalar.Cardinality?.Shape);
        Assert.Empty(scalar.Cardinality!.Terminals);
        Assert.Equal(
            [DiscoveryOutputMode.Markdown],
            scalar.OutputModes);

        DiscoveryResource inventory = projection.Document.GetResource(
            Section("Inventory"));
        Assert.Equal(
            SectionSemanticShape.Inventory,
            inventory.Cardinality?.Shape);
        Assert.Equal(
            [
                SectionTerminalCapability.Rows,
                SectionTerminalCapability.Count,
            ],
            inventory.Cardinality!.Terminals);
        Assert.Equal(
            [DiscoveryOutputMode.Markdown],
            inventory.OutputModes);

        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-cardinality-{Guid.NewGuid():N}.json");
        try
        {
            int exitCode = DetailedDiscoverOutput.Write(
                projection,
                new DiscoveryOutputRequest
                {
                    Format = OutputFormat.Json,
                    OutputPath = path,
                });

            Assert.Equal(0, exitCode);
            using JsonDocument document =
                JsonDocument.Parse(File.ReadAllText(path));
            JsonElement[] rows =
                [.. document.RootElement.EnumerateArray()];
            JsonElement scalarRow = Assert.Single(
                rows,
                row => row.GetProperty("name").GetString() == "Scalar");
            Assert.Equal(
                "scalar",
                scalarRow.GetProperty("shape").GetString());
            Assert.Empty(
                scalarRow.GetProperty("terminals").EnumerateArray());
            Assert.Equal(
                ["--markdown"],
                scalarRow.GetProperty("formats")
                    .EnumerateArray()
                    .Select(item => item.GetString()));

            JsonElement inventoryRow = Assert.Single(
                rows,
                row => row.GetProperty("name").GetString() == "Inventory");
            Assert.Equal(
                "inventory",
                inventoryRow.GetProperty("shape").GetString());
            Assert.Equal(
                ["rows", "count"],
                inventoryRow.GetProperty("terminals")
                    .EnumerateArray()
                    .Select(item => item.GetString()));
            Assert.Equal(
                ["--markdown"],
                inventoryRow.GetProperty("formats")
                    .EnumerateArray()
                    .Select(item => item.GetString()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SectionCardinality_RejectsUnknownSection()
    {
        var schema = new DocumentSchema()
            .Add("Known", "column", "Value");

        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            CreateSynthetic(
                schema,
                new Dictionary<string, string[]>(),
                MarkdownCapabilities("Known"),
                discover: null,
                new Dictionary<string, SectionCardinalityDeclaration>
                {
                    ["Unknown"] =
                        SectionCardinalityDeclaration.Inventory,
                }));

        Assert.Contains("does not name a section", error.Message);
    }

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

    [Fact]
    public void LibraryProjection_RegistersEveryStructuralResourcePath()
    {
        StructuralSchemaProjection projection = LibraryProjection();

        DiscoveryDocumentFactory.Projection structural =
            CreateProjection(projection);

        Assert.Equal(
            structural.Document.Resources.Length,
            structural.ResourcePaths.Length);
        Assert.Equal(
            structural.ResourcePaths.Length,
            structural.ResourcePaths
                .Select(static registration => registration.Identity)
                .Distinct()
                .Count());
        Assert.Equal(
            structural.ResourcePaths.Length,
            structural.ResourcePaths
                .Select(static registration => registration.Path.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(
            structural.ResourcePaths,
            registration => Assert.All(
                registration.Path.Value.Split('/'),
                segment => Assert.True(
                    segment[0] is >= 'a' and <= 'z'
                    or >= '0' and <= '9',
                    $"Path segment '{segment}' must start with a "
                    + "lower-case ASCII letter or digit.")));
        Assert.Contains(
            structural.ResourcePaths,
            registration =>
                registration.Identity
                    == Section(SectionNames.ReferenceHierarchy)
                && registration.Path.Value
                    == "library/sections/reference-hierarchy");
        Assert.Contains(
            structural.ResourcePaths,
            registration =>
                registration.Identity.Kind == DiscoveryResourceKind.Item
                && registration.Identity.ItemKind == "filterable");
        Assert.Contains(
            structural.ResourcePaths,
            registration =>
                registration.Identity.Name == "Triage desc"
                && registration.Identity.ItemKind == "default-order"
                && registration.Path.Value.EndsWith(
                    "/items/default-order/triage-desc",
                    StringComparison.Ordinal));
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

    private static DiscoveryDocumentFactory.Projection CreateProjection(
        StructuralSchemaProjection projection) =>
        DiscoveryDocumentFactory.CreateProjection(
            "library",
            discover: null,
            projection.Schema,
            projection.SectionCategories,
            projection.CatalogHiddenSections,
            projection.ListedCategoryDoors,
            projection.SectionCostAnnotations,
            projection.ExactOnlySections,
            projection.OutputCapabilities!)
        ?? throw new InvalidOperationException(
            "Expected Library discovery projection to succeed.");

    private static DiscoveryDocument CreateSynthetic(
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]> categories,
        OutputCapabilityCatalog capabilities,
        string[]? discover,
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration>? cardinalities = null) =>
        DiscoveryDocumentFactory.Create(
            "synthetic",
            discover,
            schema,
            categories,
            catalogHiddenSections: null,
            listedCategoryDoors: null,
            sectionCostAnnotations: null,
            exactOnlySections: null,
            capabilities,
            sectionCardinalities: cardinalities)
        ?? throw new InvalidOperationException(
            "Expected synthetic discovery construction to succeed.");

    private static DiscoveryDocumentFactory.Projection
        CreateSyntheticProjection(
            DocumentSchema schema,
            OutputCapabilityCatalog capabilities,
            IReadOnlyDictionary<
                string,
                SectionCardinalityDeclaration> cardinalities) =>
        DiscoveryDocumentFactory.CreateProjection(
            "synthetic",
            discover: null,
            schema,
            sectionCategories: null,
            catalogHiddenSections: null,
            listedCategoryDoors: null,
            sectionCostAnnotations: null,
            exactOnlySections: null,
            capabilities,
            sectionCardinalities: cardinalities)
        ?? throw new InvalidOperationException(
            "Expected synthetic discovery projection to succeed.");

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
