using System.Globalization;
using System.Text;
using ILInspector.Analysis;

namespace ILInspector.CallGraph;

/// <summary>
/// Emits a <see cref="CallGraphProjection"/> as a deterministic Mermaid
/// <c>flowchart</c>.
/// <para>
/// This type owns <em>only</em> the Mermaid format: node id spelling, escaping, shape and
/// class syntax, and edge/label syntax. Every graph decision — node identity and
/// deduplication, edge direction, cycle collapse, boundary classification, and ordering —
/// belongs to <see cref="CallGraphProjection"/>, so a host that wants a different format
/// (a table, a tree, its own diagram) consumes the projection directly and does not
/// re-derive graph semantics from rendered text.
/// </para>
/// </summary>
public static class CallGraphMermaid
{
    /// <summary>
    /// Renders the combined caller/target/callee view. Both roots are the selected
    /// overload: <paramref name="callerRoot"/>'s children are its inbound callers
    /// (edges flow <em>into</em> the target) and <paramref name="calleeRoot"/>'s
    /// children are its outbound callees (edges flow <em>out of</em> the target).
    /// Either root may be null (e.g. the browser's first caller-only view), but not
    /// both. When both are supplied they must name the same selected member.
    /// </summary>
    public static string Render(CallTreeNode? callerRoot, CallTreeNode? calleeRoot)
        => Render(CallGraphProjection.Create(callerRoot, calleeRoot));

    /// <summary>Renders the inbound (caller) half only, centered on the selected overload.</summary>
    public static string RenderCallers(CallTreeNode callerRoot)
        => Render(CallGraphProjection.FromCallers(callerRoot));

    /// <summary>Renders the outbound (callee) half only, centered on the selected overload.</summary>
    public static string RenderCallees(CallTreeNode calleeRoot)
        => Render(CallGraphProjection.FromCallees(calleeRoot));

    /// <summary>
    /// Renders an already-built projection. Node and edge order come from the projection,
    /// so the document is byte-stable for a given graph.
    /// </summary>
    public static string Render(CallGraphProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var sb = new StringBuilder();
        sb.Append("flowchart LR\n");

        // Nodes in projection order (focus first, then caller DFS, then callee DFS).
        foreach (var node in projection.Nodes)
        {
            sb.Append("    ").Append(NodeId(node.Id)).Append("[\"").Append(Escape(node.Label)).Append("\"]");
            if (ClassName(node.Kind) is { } className)
                sb.Append(":::").Append(className);
            sb.Append('\n');
        }

        // Edges in projection (first-seen) order.
        foreach (var edge in projection.Edges)
        {
            sb.Append("    ").Append(NodeId(edge.From));
            if (edge.LoopLabel is { } loop)
                sb.Append(" -->|").Append(Escape(loop, edgeLabel: true)).Append("| ");
            else
                sb.Append(" --> ");
            sb.Append(NodeId(edge.To)).Append('\n');
        }

        // Emit a classDef only when the class is used, in a fixed order.
        if (HasKind(projection, CallGraphNodeKind.Focus))
            sb.Append("    classDef target fill:#dae8fc,stroke:#6c8ebf,stroke-width:2px;\n");
        if (HasKind(projection, CallGraphNodeKind.External))
            sb.Append("    classDef external fill:#f5f5f5,stroke:#999999,stroke-dasharray:4 3,color:#666666;\n");
        if (HasKind(projection, CallGraphNodeKind.Truncated))
            sb.Append("    classDef truncated fill:#fff2cc,stroke:#d6b656,stroke-dasharray:2 2;\n");

        return sb.ToString();
    }

    static bool HasKind(CallGraphProjection projection, CallGraphNodeKind kind)
    {
        foreach (var node in projection.Nodes)
        {
            if (node.Kind == kind)
                return true;
        }
        return false;
    }

    static string? ClassName(CallGraphNodeKind kind) => kind switch
    {
        CallGraphNodeKind.Focus => "target",
        CallGraphNodeKind.External => "external",
        CallGraphNodeKind.Truncated => "truncated",
        _ => null,
    };

    static string NodeId(int id) => "n" + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Escapes text for a Mermaid quoted label / edge label using Mermaid entity codes,
    /// so hostile or unusual member names (quotes, angle brackets, pipes, <c>#</c>)
    /// cannot break out of the label or the flowchart grammar. Node labels are wrapped in
    /// <c>["..."]</c> so brackets/parentheses are already safe there; edge labels
    /// (<c>|...|</c>) are unquoted, so <paramref name="edgeLabel"/> additionally entity-
    /// encodes the structural delimiters that would otherwise corrupt an edge label.
    /// </summary>
    static string Escape(string text, bool edgeLabel = false)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '#': sb.Append("#35;"); break;
                case '"': sb.Append("#quot;"); break;
                case '<': sb.Append("#60;"); break;
                case '>': sb.Append("#62;"); break;
                case '&': sb.Append("#38;"); break;
                case '|': sb.Append("#124;"); break;
                case '\r': sb.Append("#13;"); break;
                case '\n': sb.Append("#10;"); break;
                case '(' when edgeLabel: sb.Append("#40;"); break;
                case ')' when edgeLabel: sb.Append("#41;"); break;
                case '[' when edgeLabel: sb.Append("#91;"); break;
                case ']' when edgeLabel: sb.Append("#93;"); break;
                case '{' when edgeLabel: sb.Append("#123;"); break;
                case '}' when edgeLabel: sb.Append("#125;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }
}
