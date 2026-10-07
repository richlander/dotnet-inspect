using System.Text;

using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Presentation;

internal sealed class MarkoutHierarchySink<TNode> :
    IInspectionHierarchySink<TNode>
{
    private readonly MarkoutWriter _writer;
    private readonly Func<TNode, string> _format;
    private readonly List<bool> _ancestorLastSibling = [];

    internal MarkoutHierarchySink(
        MarkoutWriter writer,
        Func<TNode, string> format)
    {
        _writer = writer
            ?? throw new ArgumentNullException(nameof(writer));
        _format = format
            ?? throw new ArgumentNullException(nameof(format));
    }

    public void WriteNode(
        TNode node,
        bool isLastSibling,
        Action<IInspectionHierarchySink<TNode>>? writeChildren = null)
    {
        _writer.WriteTreeNode(
            _format(node),
            Prefix(isLastSibling));
        if (writeChildren is null)
            return;

        _ancestorLastSibling.Add(isLastSibling);
        try
        {
            writeChildren(this);
        }
        finally
        {
            _ancestorLastSibling.RemoveAt(
                _ancestorLastSibling.Count - 1);
        }
    }

    private string Prefix(bool isLastSibling)
    {
        var prefix =
            new StringBuilder(
                (_ancestorLastSibling.Count + 1) * 3);
        foreach (bool ancestorIsLast in _ancestorLastSibling)
        {
            prefix.Append(
                ancestorIsLast
                    ? "   "
                    : "│  ");
        }
        prefix.Append(
            isLastSibling
                ? "└─ "
                : "├─ ");
        return prefix.ToString();
    }
}
