using System.Collections.Immutable;
using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

public class DiscoveryDocumentTests
{
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
