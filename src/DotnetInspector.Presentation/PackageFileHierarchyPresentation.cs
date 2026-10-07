using DotnetInspector.Queries;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Presentation;

public static class PackageFileHierarchyPresentation
{
    public static void WriteTree(
        IReadOnlyList<PackageFileInventoryEntry> files,
        string? title,
        TextWriter output,
        bool collapseDirectoryChains = true)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (title is not null)
            output.WriteLine(title);
        var writer = new MarkoutWriter(output, new MarkdownFormatter());
        var sink = new MarkoutHierarchySink<PackageFileHierarchyNode>(
            writer, node => node.Name.ToString());
        PackageFileHierarchyProjection.Write(files, sink, collapseDirectoryChains);
        writer.Flush();
    }
}
