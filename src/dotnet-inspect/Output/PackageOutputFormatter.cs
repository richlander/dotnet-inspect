using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Formats package file tree output for display.
/// </summary>
public static class PackageOutputFormatter
{
    public static void WriteFileTree(List<string> paths)
    {
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

        var view = new FileTreeView { Files = BuildTreeNodes(root) };
        MarkoutSerializer.Serialize(view, Console.Out, FileTreeContext.Default);
    }

    private static List<TreeNode> BuildTreeNodes(Dictionary<string, object> dict)
    {
        List<TreeNode> nodes = [];

        foreach (var kvp in dict.OrderBy(k => k.Key))
        {
            var children = (Dictionary<string, object>)kvp.Value;
            if (children.Count == 0)
            {
                nodes.Add(new TreeNode(kvp.Key));
            }
            else
            {
                nodes.Add(new TreeNode(kvp.Key, BuildTreeNodes(children)));
            }
        }

        return nodes;
    }
}
