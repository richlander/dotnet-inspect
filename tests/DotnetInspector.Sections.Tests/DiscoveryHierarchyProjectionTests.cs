using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

public sealed class DiscoveryHierarchyProjectionTests
{
    [Fact]
    public void ExplicitSelectionAddressesRenderableLeavesAndRegroupsThem()
    {
        DiscoveryResourceIdentity first = Section("First");
        DiscoveryResourceIdentity second = Section("Second");
        DiscoveryResourceIdentity a = Item("First", "A", "column");
        DiscoveryResourceIdentity query =
            Item("First", "A", "sortable");
        DiscoveryResourceIdentity b = Item("First", "B", "field");
        DiscoveryResourceIdentity c = Item("Second", "C", "column");
        var document = new DiscoveryDocument(
            "library",
            [
                new DiscoveryResource(
                    first,
                    members: [a, query, b]),
                new DiscoveryResource(
                    second,
                    members: [c]),
                new DiscoveryResource(a),
                new DiscoveryResource(query),
                new DiscoveryResource(b),
                new DiscoveryResource(c),
            ],
            [first, second],
            new DiscoverySelection(
                isCatalog: false,
                addressedResources: [first, second],
                rows: [a, query, b, c]));

        var rows = DiscoveryHierarchyProjection.Rows(document);

        Assert.Equal(3, rows.Length);
        Assert.Equal(
            ["A", "B", "C"],
            rows.Select(row => row.Child?.Identity.Name));
        Assert.DoesNotContain(
            rows,
            row => row.Child?.Identity.ItemKind == "sortable");

        var sink = new RecordingSink();
        DiscoveryHierarchyProjection.Write(
            document,
            [rows[1], rows[2]],
            sink);

        Assert.Equal(
            [
                (0, "First", false),
                (1, "B", true),
                (0, "Second", true),
                (1, "C", true),
            ],
            sink.Nodes);
    }

    [Fact]
    public void SingleSectionPromotesRenderableItemsToTopLevelRows()
    {
        DiscoveryResourceIdentity section = Section("Results");
        DiscoveryResourceIdentity name =
            Item("Results", "Name", "column");
        DiscoveryResourceIdentity filter =
            Item("Results", "Name", "filterable");
        var document = new DiscoveryDocument(
            "library",
            [
                new DiscoveryResource(
                    section,
                    members: [name, filter]),
                new DiscoveryResource(name),
                new DiscoveryResource(filter),
            ],
            [section],
            new DiscoverySelection(
                isCatalog: false,
                addressedResources: [section],
                rows: [name, filter]));

        DiscoveryHierarchyRow row =
            Assert.Single(
                DiscoveryHierarchyProjection.Rows(document));

        Assert.Equal("Name", row.Parent.Identity.Name);
        Assert.True(row.Parent.IncludeKind);
        Assert.Null(row.Child);
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

    private sealed class RecordingSink(
        int depth = 0,
        List<(int Depth, string Name, bool Last)>? nodes = null)
        : IInspectionHierarchySink<DiscoveryHierarchyNode>
    {
        public List<(int Depth, string Name, bool Last)> Nodes { get; } =
            nodes ?? [];

        public void WriteNode(
            DiscoveryHierarchyNode node,
            bool isLastSibling,
            Action<IInspectionHierarchySink<DiscoveryHierarchyNode>>?
                writeChildren = null)
        {
            Nodes.Add((depth, node.Identity.Name, isLastSibling));
            writeChildren?.Invoke(
                new RecordingSink(depth + 1, Nodes));
        }
    }
}
