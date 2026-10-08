using DotnetInspector.Sections;
using Markout;

namespace DotnetInspector.Presentation;

internal sealed class MarkoutHierarchySink<TNode> :
    IInspectionHierarchySink<TNode>
{
    private readonly StreamingTreeWriter _writer;
    private readonly Func<TNode, string> _format;

    internal MarkoutHierarchySink(
        StreamingTreeWriter writer,
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
        string text = _format(node);
        if (writeChildren is null)
        {
            _writer.WriteNode(
                text,
                isLastSibling);
            return;
        }

        _writer.WriteNode(
            text,
            isLastSibling,
            (Sink: this, WriteChildren: writeChildren),
            static (_, state) =>
                state.WriteChildren(state.Sink));
    }
}
