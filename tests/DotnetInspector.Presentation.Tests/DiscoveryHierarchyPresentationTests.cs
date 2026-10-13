using DotnetInspector.Sections;

namespace DotnetInspector.Presentation.Tests;

public sealed class DiscoveryHierarchyPresentationTests
{
    [Fact]
    public void Write_StreamsSelectedRowsWithOwnerIssuedGroupingAndPaths()
    {
        DiscoveryResourceIdentity first = Section("First");
        DiscoveryResourceIdentity second = Section("Second");
        DiscoveryResourceIdentity a = Item("First", "A", "column");
        DiscoveryResourceIdentity b = Item("First", "B", "field");
        DiscoveryResourceIdentity c = Item("Second", "C", "column");
        var document = new DiscoveryDocument(
            "library",
            [
                new DiscoveryResource(first, members: [a, b]),
                new DiscoveryResource(second, members: [c]),
                new DiscoveryResource(a),
                new DiscoveryResource(b),
                new DiscoveryResource(c),
            ],
            [first, second],
            new DiscoverySelection(
                isCatalog: false,
                addressedResources: [first, second],
                rows: [a, b, c]));
        var rows = DiscoveryHierarchyProjection.Rows(document);
        using var output = new StringWriter();

        DiscoveryHierarchyPresentation.Write(
            document,
            [rows[1], rows[2]],
            new DiscoveryHierarchyPresentationPlan(
                resourcePaths:
                    new Dictionary<
                        DiscoveryResourceIdentity,
                        ResourcePath>
                    {
                        [first] =
                            new("library/sections/first"),
                        [second] =
                            new("library/sections/second"),
                        [b] =
                            new(
                                "library/sections/first/items/field/b"),
                        [c] =
                            new(
                                "library/sections/second/items/column/c"),
                    }),
            output);

        Assert.Equal(
            """
            ├─ First [library/sections/first]
            │  └─ B (field) [library/sections/first/items/field/b]
            └─ Second [library/sections/second]
               └─ C (column) [library/sections/second/items/column/c]

            """.ReplaceLineEndings(),
            output.ToString());
    }

    [Fact]
    public void Write_CatalogCanWrapAndAnnotateLegacySections()
    {
        DiscoveryResourceIdentity regular = Section("Regular");
        DiscoveryResourceIdentity optIn = Section("Optional");
        var document = new DiscoveryDocument(
            "library",
            [
                new DiscoveryResource(regular),
                new DiscoveryResource(optIn),
            ],
            [regular, optIn],
            new DiscoverySelection(
                isCatalog: true,
                addressedResources: [],
                rows: [regular, optIn]));
        using var output = new StringWriter();

        DiscoveryHierarchyPresentation.Write(
            document,
            DiscoveryHierarchyProjection.Rows(document),
            new DiscoveryHierarchyPresentationPlan(
                rootLabel: "Library",
                sectionAnnotations:
                    new Dictionary<string, string>
                    {
                        ["Optional"] = "opt-in",
                    },
                annotateCatalogSections: true),
            output);

        Assert.Equal(
            """
            └─ Library
               ├─ Regular
               └─ Optional (opt-in)

            """.ReplaceLineEndings(),
            output.ToString());
    }

    private static DiscoveryResourceIdentity Section(string name) =>
        new(DiscoveryResourceKind.Section, name);

    private static DiscoveryResourceIdentity Item(
        string section,
        string name,
        string kind) =>
        new(
            DiscoveryResourceKind.Item,
            name,
            section,
            kind);
}
