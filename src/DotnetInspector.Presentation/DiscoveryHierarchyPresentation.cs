using DotnetInspector.Sections;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Presentation;

public sealed record DiscoveryHierarchyPresentationPlan
{
    public DiscoveryHierarchyPresentationPlan(
        string? rootLabel = null,
        IReadOnlyDictionary<string, string>? sectionAnnotations = null,
        bool annotateCatalogSections = false,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath>? resourcePaths = null)
    {
        RootLabel = rootLabel;
        SectionAnnotations = sectionAnnotations;
        AnnotateCatalogSections = annotateCatalogSections;
        ResourcePaths = resourcePaths;
    }

    public string? RootLabel { get; }
    public IReadOnlyDictionary<string, string>? SectionAnnotations { get; }
    public bool AnnotateCatalogSections { get; }
    public IReadOnlyDictionary<
        DiscoveryResourceIdentity,
        ResourcePath>? ResourcePaths { get; }
}

public static class DiscoveryHierarchyPresentation
{
    public static void Write(
        DiscoveryDocument document,
        IReadOnlyList<DiscoveryHierarchyRow> rows,
        DiscoveryHierarchyPresentationPlan plan,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);

        var writer = new MarkoutWriter(
            output,
            new MarkdownFormatter());
        writer.WriteTree(tree =>
        {
            if (plan.RootLabel is not { } rootLabel)
            {
                WriteProjection(tree, document, rows, plan);
                return;
            }

            tree.WriteNode(
                rootLabel,
                isLastSibling: true,
                (Document: document, Rows: rows, Plan: plan),
                static (children, state) =>
                    WriteProjection(
                        children,
                        state.Document,
                        state.Rows,
                        state.Plan));
        });
        writer.Flush();
    }

    private static void WriteProjection(
        StreamingTreeWriter writer,
        DiscoveryDocument document,
        IReadOnlyList<DiscoveryHierarchyRow> rows,
        DiscoveryHierarchyPresentationPlan plan)
    {
        var sink =
            new MarkoutHierarchySink<DiscoveryHierarchyNode>(
                writer,
                node => FormatNode(
                    document,
                    node,
                    plan));
        DiscoveryHierarchyProjection.Write(
            document,
            rows,
            sink);
    }

    private static string FormatNode(
        DiscoveryDocument document,
        DiscoveryHierarchyNode node,
        DiscoveryHierarchyPresentationPlan plan)
    {
        DiscoveryResourceIdentity identity = node.Identity;
        string label = identity.Name;
        if (node.IncludeKind)
        {
            string kind =
                identity.Kind == DiscoveryResourceKind.Item
                    ? identity.ItemKind!
                    : identity.Kind.ToString().ToLowerInvariant();
            label += $" ({kind})";
        }
        else if (plan.AnnotateCatalogSections
            && document.Selection.IsCatalog
            && identity.Kind == DiscoveryResourceKind.Section
            && plan.SectionAnnotations?.TryGetValue(
                identity.Name,
                out string? annotation) == true)
        {
            label += $" ({annotation})";
        }

        if (plan.ResourcePaths?.TryGetValue(
                identity,
                out ResourcePath? path) == true)
        {
            label += $" [{path.Value}]";
        }

        return label;
    }
}
