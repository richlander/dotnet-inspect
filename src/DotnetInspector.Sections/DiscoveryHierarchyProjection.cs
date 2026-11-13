using System.Collections.Immutable;

namespace DotnetInspector.Sections;

public sealed record DiscoveryHierarchyNode(
    DiscoveryResourceIdentity Identity,
    bool IncludeKind,
    bool IsTopLevelCatalogEntry = false);

public readonly record struct DiscoveryHierarchyRow(
    int ParentOrdinal,
    DiscoveryHierarchyNode Parent,
    DiscoveryHierarchyNode? Child);

public static class DiscoveryHierarchyProjection
{
    public static ImmutableArray<DiscoveryHierarchyRow> Rows(
        DiscoveryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var rows = ImmutableArray.CreateBuilder<DiscoveryHierarchyRow>();
        ImmutableArray<DiscoveryHierarchyNode> parents =
            TopLevelNodes(document);
        for (int parentOrdinal = 0;
            parentOrdinal < parents.Length;
            parentOrdinal++)
        {
            DiscoveryHierarchyNode parent = parents[parentOrdinal];
            if (document.Selection.IsCatalog)
            {
                rows.Add(new(parentOrdinal, parent, Child: null));
                continue;
            }

            ImmutableArray<DiscoveryHierarchyNode> children =
                ChildNodes(document, parent);
            if (children.IsEmpty)
            {
                rows.Add(new(parentOrdinal, parent, Child: null));
                continue;
            }

            foreach (DiscoveryHierarchyNode child in children)
                rows.Add(new(parentOrdinal, parent, child));
        }

        return rows.ToImmutable();
    }

    public static void Write(
        DiscoveryDocument document,
        IReadOnlyList<DiscoveryHierarchyRow> rows,
        IInspectionHierarchySink<DiscoveryHierarchyNode> sink)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(sink);

        ImmutableArray<DiscoveryHierarchyRow> available = Rows(document);
        var availableSet = available.ToHashSet();
        if (rows.Any(row => !availableSet.Contains(row)))
        {
            throw new ArgumentException(
                "Discovery hierarchy rows must belong to the supplied document.",
                nameof(rows));
        }

        if (document.Selection.IsCatalog)
        {
            for (int index = 0; index < rows.Count; index++)
            {
                DiscoveryHierarchyNode parent = rows[index].Parent;
                WriteNode(
                    document,
                    parent,
                    expandAllChildren: true,
                    selectedChildren: [],
                    sink,
                    isLastSibling: index == rows.Count - 1);
            }
            return;
        }

        IGrouping<
            int,
            DiscoveryHierarchyRow>[] groups =
        [
            .. rows.GroupBy(static row => row.ParentOrdinal),
        ];
        for (int index = 0; index < groups.Length; index++)
        {
            IGrouping<
                int,
                DiscoveryHierarchyRow> group = groups[index];
            WriteNode(
                document,
                group.First().Parent,
                expandAllChildren: false,
                selectedChildren:
                [
                    .. group
                        .Where(static row => row.Child is not null)
                        .Select(static row => row.Child!),
                ],
                sink,
                isLastSibling: index == groups.Length - 1);
        }
    }

    private static void WriteNode(
        DiscoveryDocument document,
        DiscoveryHierarchyNode parent,
        bool expandAllChildren,
        IReadOnlyList<DiscoveryHierarchyNode> selectedChildren,
        IInspectionHierarchySink<DiscoveryHierarchyNode> sink,
        bool isLastSibling)
    {
        IReadOnlyList<DiscoveryHierarchyNode> children =
            expandAllChildren
                ? ChildNodes(document, parent)
                : selectedChildren;
        sink.WriteNode(
            parent,
            isLastSibling,
            children.Count == 0
                ? null
                : childSink =>
                {
                    for (int index = 0; index < children.Count; index++)
                    {
                        childSink.WriteNode(
                            children[index],
                            isLastSibling: index == children.Count - 1);
                    }
                });
    }

    private static ImmutableArray<DiscoveryHierarchyNode> TopLevelNodes(
        DiscoveryDocument document)
    {
        if (document.Selection.IsCatalog)
        {
            return
            [
                .. document.Selection.Rows.Select(identity =>
                    new DiscoveryHierarchyNode(
                        identity,
                        IncludeKind:
                            identity.Kind
                            == DiscoveryResourceKind.Category,
                        IsTopLevelCatalogEntry: true)),
            ];
        }

        if (document.Selection.AddressedResources
            is [{ Kind: DiscoveryResourceKind.Section } section])
        {
            ImmutableArray<DiscoveryHierarchyNode> members =
                TreeMembers(document, section);
            if (!members.IsEmpty)
                return members;
        }

        return
        [
            .. document.Selection.AddressedResources.Select(identity =>
                new DiscoveryHierarchyNode(
                    identity,
                    IncludeKind: false)),
        ];
    }

    private static ImmutableArray<DiscoveryHierarchyNode> ChildNodes(
        DiscoveryDocument document,
        DiscoveryHierarchyNode node) =>
        node.Identity.Kind switch
        {
            DiscoveryResourceKind.Category =>
            [
                .. document
                    .GetResource(node.Identity)
                    .Members
                    .Select(identity =>
                        new DiscoveryHierarchyNode(
                            identity,
                            IncludeKind: false)),
            ],
            DiscoveryResourceKind.Section =>
                TreeMembers(document, node.Identity),
            DiscoveryResourceKind.Item => [],
            _ => throw new InvalidOperationException(
                "Unknown discovery resource kind."),
        };

    private static ImmutableArray<DiscoveryHierarchyNode> TreeMembers(
        DiscoveryDocument document,
        DiscoveryResourceIdentity section) =>
    [
        .. document
            .GetResource(section)
            .Members
            .Where(static identity =>
                identity.Kind == DiscoveryResourceKind.Item
                && identity.ItemKind
                    is not (
                        "default-order"
                        or "order-step"
                        or "filterable"
                        or "sortable"))
            .Select(identity =>
                new DiscoveryHierarchyNode(
                    identity,
                    IncludeKind: true)),
    ];
}
