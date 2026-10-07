using DotnetInspector.Sections;

namespace DotnetInspector.Presentation;

internal sealed class MermaidHierarchySink<TNode> :
    IInspectionHierarchySink<TNode>
{
    private readonly TextWriter _output;
    private readonly Func<TNode, string> _format;
    private readonly List<int> _ancestors;
    private int _nextNodeId;

    internal MermaidHierarchySink(
        TextWriter output,
        int rootNodeId,
        Func<TNode, string> format)
    {
        _output = output
            ?? throw new ArgumentNullException(nameof(output));
        _format = format
            ?? throw new ArgumentNullException(nameof(format));
        _ancestors = [rootNodeId];
        _nextNodeId = rootNodeId + 1;
    }

    public void WriteNode(
        TNode node,
        bool isLastSibling,
        Action<IInspectionHierarchySink<TNode>>? writeChildren = null)
    {
        int nodeId = _nextNodeId++;
        _output.Write("  n");
        _output.Write(nodeId);
        _output.Write("[\"");
        _output.Write(Escape(_format(node)));
        _output.WriteLine("\"]");
        _output.Write("  n");
        _output.Write(_ancestors[^1]);
        _output.Write(" --> n");
        _output.WriteLine(nodeId);
        if (writeChildren is null)
            return;

        _ancestors.Add(nodeId);
        try
        {
            writeChildren(this);
        }
        finally
        {
            _ancestors.RemoveAt(_ancestors.Count - 1);
        }
    }

    internal static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
    }
}
