using DotnetInspector.Sections;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Presentation;

public static class DependencyHierarchyPresentation
{
    public static void WriteTree(
        DependencyHierarchyDocument document,
        IReadOnlyList<DependencyHierarchyOccurrenceIdentity>
            selectedOccurrences,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedOccurrences);
        ArgumentNullException.ThrowIfNull(output);

        var writer = new MarkoutWriter(
            output,
            new PlainTextFormatter());
        writer.WriteTree(tree =>
        {
            var sink =
                new MarkoutHierarchySink<DependencyHierarchyNode>(
                    tree,
                    FormatNode);
            DependencyHierarchyProjection.Write(
                document,
                selectedOccurrences,
                sink);
        });
        writer.Flush();
    }

    private static string FormatNode(DependencyHierarchyNode node)
    {
        string marker = node.IsFragment
            ? "(fragment) "
            : node.Disposition switch
            {
                DependencyHierarchyOccurrenceDisposition.Revisit =>
                    "(revisit) ",
                DependencyHierarchyOccurrenceDisposition.Cycle =>
                    "(cycle) ",
                _ => "",
            };
        string boundary = node.BoundaryDepths.Length switch
        {
            0 => "",
            1 => $"(bounded at depth {node.BoundaryDepths[0]}) ",
            _ => $"(bounded at depths {string.Join(", ", node.BoundaryDepths)}) ",
        };
        string relationship = node.Relationship is { } value
            ? $" ({value})"
            : "";
        return $"{marker}{boundary}{node.Label}{relationship}";
    }
}
