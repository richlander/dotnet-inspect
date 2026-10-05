using DotnetInspect.Cli.Views;
using Markout;

using ILInspector.CSharp;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// Formats package file tree output for display.
/// </summary>
public static class PackageOutputFormatter
{
    public static void WriteFileTree(List<string> paths) =>
        WriteFileTree(
            Console.Out,
            title: null,
            paths,
            collapseSingleChildDirectories: false);

    /// <summary>
    /// Writes a directory tree over <paramref name="paths"/>. The optional
    /// title line precedes the tree (the subject identity plus issued
    /// properties, per <c>docs/design/section-shapes.md#properties</c>).
    /// When <paramref name="collapseSingleChildDirectories"/> is set, a
    /// directory chain with one child at each level renders as one node whose
    /// label joins the segments with '/', so a deep single-file branch reads
    /// as its path rather than as a ladder of context nodes.
    /// </summary>
    public static void WriteFileTree(
        TextWriter output,
        string? title,
        IEnumerable<string> paths,
        bool collapseSingleChildDirectories)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(paths);

        // Build tree structure from file paths
        var root = new Dictionary<string, object>();

        foreach (var path in paths)
        {
            var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i == parts.Length - 1)
                {
                    current[part] = new Dictionary<string, object>();
                }
                else
                {
                    if (!current.TryGetValue(part, out var next))
                    {
                        next = new Dictionary<string, object>();
                        current[part] = next;
                    }
                    current = (Dictionary<string, object>)next;
                }
            }
        }

        if (title is not null)
            output.WriteLine(title);
        var view = new FileTreeView
        {
            Files = BuildTreeNodes(root, collapseSingleChildDirectories),
        };
        MarkoutSerializer.Serialize(view, output, FileTreeContext.Default);
    }

    private static List<TreeNode> BuildTreeNodes(
        Dictionary<string, object> dict,
        bool collapseSingleChildDirectories)
    {
        List<TreeNode> nodes = [];

        foreach (var kvp in dict.OrderBy(k => k.Key))
        {
            string key = kvp.Key;
            var children = (Dictionary<string, object>)kvp.Value;

            // A ZIP entry name is attacker-chosen, and a tree node is rendered
            // straight into the gutter, so contain it here at the presentation
            // boundary. The dictionary is still keyed and ordered by the raw
            // path so grouping stays identity-based (issue #3319).
            var label = CSharpIdentifier.ContainRenderedText(key);

            // Collapse a directory whose only entry is another directory into
            // one labelled segment chain; a directory holding one file keeps
            // its own node so the file stays a leaf row.
            while (collapseSingleChildDirectories
                && children.Count == 1
                && children.Single().Value is Dictionary<string, object> { Count: > 0 } onlyChild)
            {
                label += "/" + CSharpIdentifier.ContainRenderedText(children.Single().Key);
                children = onlyChild;
            }

            if (children.Count == 0)
            {
                nodes.Add(new TreeNode(label));
            }
            else
            {
                nodes.Add(new TreeNode(label)
                {
                    Children = BuildTreeNodes(children, collapseSingleChildDirectories),
                });
            }
        }

        return nodes;
    }
}
