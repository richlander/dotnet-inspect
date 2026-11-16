using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Presentation;

public static class MemberCallGraphHierarchyPresentation
{
    public static void Write(
        MemberCallGraphHierarchy hierarchy,
        Func<MemberCallGraphHierarchyNode, string> formatNode,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(formatNode);
        ArgumentNullException.ThrowIfNull(output);
        var writer = new MarkoutWriter(output, new PlainTextFormatter());
        writer.WriteTree(tree =>
            MemberCallGraphHierarchyProjection.Write(
                hierarchy,
                new MarkoutHierarchySink<MemberCallGraphHierarchyNode>(
                    tree,
                    formatNode)));
        writer.Flush();
    }
}
