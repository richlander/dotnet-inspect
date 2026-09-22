using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

public class DiscoveryDocumentTests
{
    [Fact]
    public void SectionCapabilitiesPairRowsAndCount()
    {
        SectionCardinalityDeclaration scalar =
            SectionCardinalityDeclaration.Scalar;
        SectionCardinalityDeclaration inventory =
            SectionCardinalityDeclaration.Inventory;

        Assert.Equal(SectionSemanticShape.Scalar, scalar.Shape);
        Assert.Empty(scalar.Terminals);
        Assert.Equal(SectionSemanticShape.Inventory, inventory.Shape);
        Assert.Equal(
            [
                SectionTerminalCapability.Rows,
                SectionTerminalCapability.Count,
            ],
            inventory.Terminals);

        Assert.Throws<ArgumentException>(() =>
            new SectionCardinalityDeclaration(
                SectionSemanticShape.Inventory,
                [SectionTerminalCapability.Rows]));
        Assert.Throws<ArgumentException>(() =>
            new SectionCardinalityDeclaration(
                SectionSemanticShape.Inventory,
                [SectionTerminalCapability.Count]));
        Assert.Throws<ArgumentException>(() =>
            new SectionCardinalityDeclaration(
                SectionSemanticShape.Scalar,
                [
                    SectionTerminalCapability.Rows,
                    SectionTerminalCapability.Count,
                ]));
        Assert.Throws<ArgumentException>(() =>
            new SectionCardinalityDeclaration(
                SectionSemanticShape.Inventory,
                []));
    }

    [Fact]
    public void Cardinality_RoundTripsForScalarAndInventoryDiscovery()
    {
        DiscoveryResourceIdentity scalar = Section("Library Info");
        DiscoveryResourceIdentity inventory = Section("References");
        var document = new DiscoveryDocument(
            "library",
            [
                new DiscoveryResource(
                    scalar,
                    outputModes: [DiscoveryOutputMode.Markdown],
                    cardinality: SectionCardinalityDeclaration.Scalar),
                new DiscoveryResource(
                    inventory,
                    outputModes:
                    [
                        DiscoveryOutputMode.Markdown,
                        DiscoveryOutputMode.Json,
                    ],
                    cardinality: SectionCardinalityDeclaration.Inventory),
            ],
            [scalar, inventory],
            new DiscoverySelection(
                isCatalog: true,
                addressedResources: [],
                rows: [scalar, inventory]));

        string json = JsonSerializer.Serialize(document);
        DiscoveryDocument roundTrip =
            JsonSerializer.Deserialize<DiscoveryDocument>(json)
            ?? throw new InvalidOperationException(
                "Expected Discovery Document round trip.");

        DiscoveryResource scalarResource =
            roundTrip.GetResource(scalar);
        Assert.Equal(
            SectionSemanticShape.Scalar,
            scalarResource.Cardinality?.Shape);
        Assert.Empty(scalarResource.Cardinality!.Terminals);
        Assert.Equal(
            [DiscoveryOutputMode.Markdown],
            scalarResource.OutputModes);

        DiscoveryResource inventoryResource =
            roundTrip.GetResource(inventory);
        Assert.Equal(
            SectionSemanticShape.Inventory,
            inventoryResource.Cardinality?.Shape);
        Assert.Equal(
            [
                SectionTerminalCapability.Rows,
                SectionTerminalCapability.Count,
            ],
            inventoryResource.Cardinality!.Terminals);
        Assert.Equal(
            [
                DiscoveryOutputMode.Markdown,
                DiscoveryOutputMode.Json,
            ],
            inventoryResource.OutputModes);
    }

    [Fact]
    public void Constructor_RejectsCardinalityOnNonSectionResource()
    {
        Assert.Throws<ArgumentException>(() =>
            new DiscoveryResource(
                Category("@Library"),
                cardinality: SectionCardinalityDeclaration.Inventory));
        Assert.Throws<ArgumentException>(() =>
            new DiscoveryResource(
                Item("References", "Name", "column"),
                cardinality: SectionCardinalityDeclaration.Inventory));
    }

    [Fact]
    public void Constructor_AcceptsSharedSectionAcrossCategories()
    {
        DiscoveryResourceIdentity section = Section("References");
        var document = new DiscoveryDocument(
            "library",
            [
                new(
                    Category("@Library"),
                    members: [section]),
                new(
                    Category("@Dependencies"),
                    members: [section]),
                new(
                    section,
                    members: [Item("References", "Name", "column")],
                    outputModes: [DiscoveryOutputMode.Table]),
                new(
                    Item("References", "Name", "column")),
            ],
            [Category("@Library"), Category("@Dependencies"), section],
            new DiscoverySelection(
                isCatalog: true,
                addressedResources: [],
                rows:
                [
                    Category("@Library"),
                    Category("@Dependencies"),
                    section,
                ]));

        Assert.Single(
            document.Resources,
            resource => resource.Identity == section);
        Assert.Equal(
            2,
            document.Resources.Count(resource =>
                resource.Members.Contains(section)));
        Assert.DoesNotContain(
            "\"Cardinality\"",
            JsonSerializer.Serialize(document),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_RejectsDanglingCategoryMember()
    {
        Assert.Throws<ArgumentException>(() =>
            new DiscoveryDocument(
                "library",
                [
                    new DiscoveryResource(
                        Category("@Dependencies"),
                        members: [Section("References")]),
                ],
                [Category("@Dependencies")],
                new DiscoverySelection(
                    isCatalog: true,
                    addressedResources: [],
                    rows: [Category("@Dependencies")])));
    }

    [Fact]
    public void Constructor_DistinguishesSameNamedItemsBySection()
    {
        DiscoveryResourceIdentity left =
            Item("References", "Name", "column");
        DiscoveryResourceIdentity right =
            Item("Reference Hierarchy", "Name", "column");

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Constructor_DistinguishesSameNamedItemsByItemKind()
    {
        DiscoveryResourceIdentity column =
            Item("Performance: Boxing", "RootReach", "column");
        DiscoveryResourceIdentity sortable =
            Item("Performance: Boxing", "RootReach", "sortable");

        Assert.NotEqual(column, sortable);
    }

    [Fact]
    public void SharedAssemblyHasNoDirectCliOrMarkoutReference()
    {
        string[] references =
        [
            .. typeof(DiscoveryDocument)
                .Assembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name!),
        ];

        Assert.DoesNotContain(
            references,
            reference => reference.StartsWith(
                "DotnetInspect.Cli",
                StringComparison.Ordinal));
        Assert.DoesNotContain("Markout", references);
    }

    private static DiscoveryResourceIdentity Category(string name) =>
        new(DiscoveryResourceKind.Category, name);

    private static DiscoveryResourceIdentity Section(string name) =>
        new(DiscoveryResourceKind.Section, name);

    private static DiscoveryResourceIdentity Item(
        string section,
        string name,
        string itemKind) =>
        new(
            DiscoveryResourceKind.Item,
            name,
            section,
            itemKind);
}
