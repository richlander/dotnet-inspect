using ILInspector.Metadata;
using InertText;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// Projects the flat, depth-annotated assembly-reference model into a rendered tree.
/// </summary>
internal static class AssemblyReferenceTreeView
{
    public static List<TreeNode> Build(List<AssemblyReferenceNode> nodes)
    {
        List<TreeNode> result = [];
        int index = 0;
        Build(nodes, ref index, currentDepth: 0, result);
        return result;
    }

    private static void Build(
        List<AssemblyReferenceNode> nodes,
        ref int index,
        int currentDepth,
        List<TreeNode> target)
    {
        while (index < nodes.Count && nodes[index].Depth == currentDepth)
        {
            var node = nodes[index];
            InertString label = node.Company is { IsEmpty: false } company
                ? InertString.Format(TextPolicy.Field, $"{node.Name} {node.Version} [{company}]")
                : InertString.Format(TextPolicy.Field, $"{node.Name} {node.Version}");
            index++;

            List<TreeNode> children = [];
            if (index < nodes.Count && nodes[index].Depth > currentDepth)
            {
                Build(nodes, ref index, currentDepth + 1, children);
            }

            string text = label.ToString();
            target.Add(children.Count > 0
                ? new TreeNode(text) { Children = children }
                : new TreeNode(text));
        }
    }
}
