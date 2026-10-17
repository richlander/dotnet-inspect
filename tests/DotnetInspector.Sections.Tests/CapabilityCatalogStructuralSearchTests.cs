using System.Text.Json;
using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

public sealed class CapabilityCatalogStructuralSearchTests
{
    private static readonly DiscoveryResourceIdentity Audit =
        new(DiscoveryResourceKind.Category, "@Audit");
    private static readonly DiscoveryResourceIdentity UnsafeMembers =
        new(DiscoveryResourceKind.Section, "Unsafe Members");
    private static readonly DiscoveryResourceIdentity LibraryMetrics =
        new(DiscoveryResourceKind.Section, "Library Metrics");
    private static readonly DiscoveryResourceIdentity Undeclared =
        new(DiscoveryResourceKind.Section, "Undeclared Section");
    private static readonly DiscoveryResourceIdentity OrdinalColumn =
        new(
            DiscoveryResourceKind.Item,
            "Ordinal",
            "Unsafe Members",
            "column");

    [Fact]
    public void SectionName_ReachesTheStructuralSection()
    {
        CapabilityCatalogSearchDocument document = Search("unsafe");

        CapabilityCatalogSearchResult result = document.Results[0];
        Assert.Equal(1.0, result.Similarity);
        Assert.Equal("Unsafe", result.MatchedTerm);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.OwnerIdentity,
            result.MatchSource);
        Assert.Null(result.ResourceIdentity);
        Assert.Equal(
            new CapabilityCatalogSearchStructuralIdentity(
                "library",
                DiscoveryResourceKind.Section,
                "Unsafe Members"),
            result.StructuralIdentity);
        Assert.Equal(
            CapabilityCatalogSearchResourceKind.Section,
            result.ResourceKind);
        Assert.Equal("Unsafe Members", result.ResourceName);
        Assert.Empty(result.CanonicalKeys);
        Assert.Equal("library/sections/unsafe-members", result.ResourcePath);
        Assert.Equal(
            "library",
            Assert.Single(result.OwningRoutes).ResourcePath);
        CapabilityCatalogSearchBinding binding =
            Assert.Single(result.ProductionBindings);
        Assert.Equal("library -S", binding.Gesture);
        Assert.Equal(InspectionConsumerKind.Cli, binding.ConsumerKind);
        Assert.Equal("library", binding.ResourcePath);
    }

    [Fact]
    public void CategoryName_ReachesTheStructuralCategory()
    {
        CapabilityCatalogSearchResult result = Search("audit").Results[0];

        Assert.Equal(
            CapabilityCatalogSearchResourceKind.Category,
            result.ResourceKind);
        Assert.Equal(
            new CapabilityCatalogSearchStructuralIdentity(
                "library",
                DiscoveryResourceKind.Category,
                "@Audit"),
            result.StructuralIdentity);
        Assert.Equal("library/categories/audit", result.ResourcePath);
    }

    [Fact]
    public void DescriptionSummaryParticipates()
    {
        CapabilityCatalogSearchResult result =
            Assert.Single(Search("pointers").Results);

        Assert.Equal("Unsafe Members", result.ResourceName);
        Assert.Equal(
            CapabilityCatalogSearchMatchSource.Summary,
            result.MatchSource);
    }

    [Theory]
    [InlineData("zebra")]
    [InlineData("quokka")]
    [InlineData("wombat")]
    [InlineData("ordinal")]
    public void DescriptionBasisEmptyBoundaryAndColumnsDoNotParticipate(
        string text)
    {
        CapabilityCatalogSearchDocument document = Search(text);

        Assert.Equal(0, document.MatchCount);
        Assert.Empty(document.Results);
    }

    [Fact]
    public void PopulationIsEverySectionAndCategoryAndNoItem()
    {
        CapabilityCatalogSearchDocument document = Search("unsafe");

        // One category and three sections; the column item, the catalog
        // root, and the navigation collections are not candidates.
        Assert.Equal(4, document.CandidateResourceCount);
    }

    [Fact]
    public void StructuralResultSerializesItsOneOfIdentity()
    {
        CapabilityCatalogSearchResult result = Search("unsafe").Results[0];

        using JsonDocument json = JsonDocument.Parse(
            JsonSerializer.Serialize(
                result,
                CapabilityCatalogSearchJsonContext.Default
                    .CapabilityCatalogSearchResult));

        JsonElement root = json.RootElement;
        Assert.Equal(
            JsonValueKind.Null,
            root.GetProperty("resource_identity").ValueKind);
        JsonElement structural = root.GetProperty("structural_identity");
        Assert.Equal("library", structural.GetProperty("catalog").GetString());
        Assert.Equal("Section", structural.GetProperty("kind").GetString());
        Assert.Equal(
            "Unsafe Members",
            structural.GetProperty("name").GetString());
        Assert.Equal("Section", root.GetProperty("resource_kind").GetString());
    }

    [Fact]
    public void StructuralPathMustResolveInTheExactExplanationCatalog()
    {
        ResourceExplanationCatalog structural = Structural();
        ResourceExplanationCatalog other = Structural("package");

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CapabilityCatalogSearch.Search(
                EmptyCapabilities(),
                ResourceExplanationCatalog.Combine([other]),
                [Admit(structural)],
                new("unsafe")));
        Assert.Contains("does not resolve", exception.Message);
    }

    [Fact]
    public void ExplainedStructuralCatalogMustBeAdmitted()
    {
        ResourceExplanationCatalog structural = Structural();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CapabilityCatalogSearch.Search(
                EmptyCapabilities(),
                ResourceExplanationCatalog.Combine([structural]),
                [],
                new("unsafe")));
        Assert.Contains("not admitted", exception.Message);
    }

    [Fact]
    public void StructuralCatalogIsAdmittedOnce()
    {
        ResourceExplanationCatalog structural = Structural();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CapabilityCatalogSearch.Search(
                EmptyCapabilities(),
                ResourceExplanationCatalog.Combine([structural]),
                [Admit(structural), Admit(structural)],
                new("unsafe")));
        Assert.Contains("more than once", exception.Message);
    }

    [Fact]
    public void StructuralRoutePathMustBeTheCatalogRoot()
    {
        ResourceExplanationCatalog structural = Structural();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            CapabilityCatalogSearch.Search(
                EmptyCapabilities(),
                ResourceExplanationCatalog.Combine([structural]),
                [Admit(structural, root: "package")],
                new("unsafe")));
        Assert.Contains("is not the root", exception.Message);
    }

    [Fact]
    public void StructuralRouteAndBindingShareTheCatalogRoot()
    {
        Assert.Throws<ArgumentException>(() =>
            new CapabilityCatalogSearchStructuralCatalog(
                Structural(),
                new("test/library-catalog", "library", "library"),
                new(
                    "test/library-sections",
                    "test CLI",
                    InspectionConsumerKind.Cli,
                    "library -S",
                    "package")));
    }

    private static CapabilityCatalogSearchDocument Search(string text)
    {
        ResourceExplanationCatalog structural = Structural();
        return CapabilityCatalogSearch.Search(
                EmptyCapabilities(),
                ResourceExplanationCatalog.Combine([structural]),
                [Admit(structural)],
                new(text))
            .Content;
    }

    private static InspectionCapabilityCatalog EmptyCapabilities() =>
        InspectionCapabilityCatalog.Create([]);

    private static CapabilityCatalogSearchStructuralCatalog Admit(
        ResourceExplanationCatalog catalog,
        string root = "library") =>
        new(
            catalog,
            new($"test/{root}-catalog", root, root),
            new(
                $"test/{root}-sections",
                "test CLI",
                InspectionConsumerKind.Cli,
                "library -S",
                root));

    // The fixture catalog under a chosen root; a second root gives a
    // catalog whose paths do not resolve the library fixture's resources.
    private static ResourceExplanationCatalog Structural(
        string root = "library")
    {
        var document = new DiscoveryDocument(
            root,
            [
                new DiscoveryResource(
                    Audit,
                    members: [UnsafeMembers, LibraryMetrics]),
                new DiscoveryResource(
                    UnsafeMembers,
                    members: [OrdinalColumn],
                    outputModes: [DiscoveryOutputMode.Markdown],
                    description: new SectionDescription(
                        "Members whose bodies use pointers.",
                        "Compiled zebra bodies.",
                        "No quokka was found.",
                        "Not a wombat verdict.")),
                new DiscoveryResource(OrdinalColumn),
                new DiscoveryResource(
                    LibraryMetrics,
                    outputModes: [DiscoveryOutputMode.Markdown],
                    description: new SectionDescription(
                        "Implementation volume.",
                        "Compiled method bodies.",
                        "The library has no method bodies.")),
                new DiscoveryResource(
                    Undeclared,
                    outputModes: [DiscoveryOutputMode.Markdown]),
            ],
            [Audit, UnsafeMembers, LibraryMetrics, Undeclared],
            new DiscoverySelection(
                isCatalog: true,
                addressedResources: [],
                rows: [Audit, UnsafeMembers, LibraryMetrics, Undeclared]));
        return ResourceExplanationCatalog.CreateStructural(
            document,
            [
                new(Audit, new ResourcePath($"{root}/categories/audit")),
                new(
                    UnsafeMembers,
                    new ResourcePath($"{root}/sections/unsafe-members")),
                new(
                    OrdinalColumn,
                    new ResourcePath(
                        $"{root}/sections/unsafe-members/items/column/ordinal")),
                new(
                    LibraryMetrics,
                    new ResourcePath($"{root}/sections/library-metrics")),
                new(
                    Undeclared,
                    new ResourcePath($"{root}/sections/undeclared-section")),
            ]);
    }
}
