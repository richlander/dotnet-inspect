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

    [Fact]
    public void StructuralSuggestionsRemainTreeMembers()
    {
        DiscoveryResourceIdentity section = Section("Unresolved");
        DiscoveryResourceIdentity suggestion =
            Item("Unresolved", "Available", "suggestion");
        DiscoveryResourceIdentity query =
            Item("Unresolved", "Available", "sortable");
        var document = new DiscoveryDocument(
            "library",
            [
                new DiscoveryResource(
                    section,
                    members: [suggestion, query]),
                new DiscoveryResource(suggestion),
                new DiscoveryResource(query),
            ],
            [section],
            new DiscoverySelection(
                isCatalog: false,
                addressedResources: [section],
                rows: [suggestion, query]));

        DiscoveryHierarchyRow row =
            Assert.Single(
                DiscoveryHierarchyProjection.Rows(document));

        Assert.Equal("Available", row.Parent.Identity.Name);
        Assert.Equal("suggestion", row.Parent.Identity.ItemKind);
        Assert.True(row.Parent.IncludeKind);
    }

    [Fact]
    public void RepeatedParentOccurrencesRemainDistinctAcrossSelectedRows()
    {
        DiscoveryResourceIdentity first = Section("First");
        DiscoveryResourceIdentity second = Section("Second");
        DiscoveryResourceIdentity a = Item("First", "A", "column");
        DiscoveryResourceIdentity b = Item("First", "B", "column");
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
                addressedResources: [first, second, first],
                rows: [a, b, c]));

        var rows =
            DiscoveryHierarchyProjection.Rows(document);

        Assert.Equal([0, 0, 1, 2, 2], rows.Select(
            static row => row.ParentOrdinal));

        var sink = new RecordingSink();
        DiscoveryHierarchyProjection.Write(
            document,
            [rows[1], rows[2], rows[3]],
            sink);

        Assert.Equal(
            [
                (0, "First", false),
                (1, "B", true),
                (0, "Second", false),
                (1, "C", true),
                (0, "First", true),
                (1, "A", true),
            ],
            sink.Nodes);
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
