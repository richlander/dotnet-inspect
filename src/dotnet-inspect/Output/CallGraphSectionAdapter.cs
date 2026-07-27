using System.Text;
using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace DotnetInspector.Output;

/// <summary>
/// Turns the format-neutral <see cref="CallGraphProjection"/> into the generic
/// <see cref="Markout.Graph"/> shape the writer lowers per format.
/// </summary>
/// <remarks>
/// <para>
/// This is the third layer of the call-graph pipeline: <c>ILInspector</c> owns the graph facts and
/// knows no output format, Markout owns the shape and knows no call-graph vocabulary, and this
/// adapter owns everything in between — how a member is spelled, which analysis cues a
/// <c>--fields</c> request projects onto a label, which nodes are noteworthy, and how a node is
/// grouped. Neither neighbour needs to learn the other's concerns.
/// </para>
/// <para>
/// It emits one shape for every sink. A Mermaid diagram, a Markdown edge table, and a plain-text
/// tree are the same graph lowered three ways rather than three independently maintained
/// renderings that can disagree.
/// </para>
/// </remarks>
internal static class CallGraphSectionAdapter
{
    /// <summary>
    /// Builds the section's graph.
    /// </summary>
    /// <param name="projection">The projected bidirectional graph centred on the selected member.</param>
    /// <param name="spellMember">
    /// The CLI's member spelling. The projection offers a host-neutral default label, but this
    /// command already owns how a member is written everywhere else in its output, so the graph
    /// uses the same spelling rather than a second one.
    /// </param>
    /// <param name="requestedFields">
    /// The <c>--fields</c>/<c>-D</c> selection. When empty, a default set of scale cues is used, the
    /// same defaulting the tree rendering has always applied.
    /// </param>
    public static Markout.Graph ToGraph(
        CallGraphProjection projection,
        Func<MemberRef, string> spellMember,
        IReadOnlyList<string>? requestedFields = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(spellMember);

        var nodes = new List<Markout.GraphNode>(projection.Nodes.Length);
        foreach (var node in projection.Nodes)
        {
            nodes.Add(new Markout.GraphNode(Key(node.Id), Label(node, spellMember, requestedFields))
            {
                Group = Group(node),
                // The selected member is what the reader asked about; every sink that can
                // distinguish it should.
                Emphasized = node.Kind == CallGraphNodeKind.Focus,
            });
        }

        var edges = new List<Markout.GraphEdge>(projection.Edges.Length);
        foreach (var edge in projection.Edges)
            edges.Add(new Markout.GraphEdge(Key(edge.From), Key(edge.To)) { Label = edge.LoopLabel });

        return new Markout.Graph(nodes, edges, focusKey: Key(projection.Focus.Id));
    }

    // The projection's dense ids are the node identity. They are opaque to Markout and never
    // emitted, so they carry no display meaning and need none.
    private static string Key(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Groups a node by how it relates to the analyzed assembly set, which is the distinction a
    /// reader needs when a graph crosses a library boundary. In-assembly nodes are ungrouped so a
    /// single-library graph stays a plain graph with no clustering and no extra table columns.
    /// </summary>
    private static string? Group(CallGraphNode node) => node.Kind switch
    {
        CallGraphNodeKind.External => "External",
        _ => null,
    };

    private static string Label(CallGraphNode node, Func<MemberRef, string> spellMember, IReadOnlyList<string>? requestedFields)
    {
        var member = spellMember(node.Member);
        var suffixes = new List<string>();

        switch (node.Kind)
        {
            // A boundary node is a place the graph stopped, not a leaf. Say so, or a reader reads
            // a depth limit as "this method calls nothing".
            case CallGraphNodeKind.Truncated:
                suffixes.Add("…");
                break;
            // Grouping clusters this node in sinks that draw containers, but a tree or a plain
            // table has no container to draw, so the fact also has to survive in the label.
            case CallGraphNodeKind.External:
                suffixes.Add("external");
                break;
        }

        if (requestedFields is { Count: > 0 })
        {
            foreach (var field in requestedFields)
            {
                if (Annotation(node.Perf, field) is { } annotation)
                    suffixes.Add(annotation);
            }
        }
        else if (node.Perf is { } perf)
        {
            if (perf.Fanout > 0)
                suffixes.Add($"fanout {perf.Fanout}");
            if (perf.Fanin > 0)
                suffixes.Add($"fanin {perf.Fanin}");
            if (perf.MaxDepth > 1)
                suffixes.Add($"depth {perf.MaxDepth}");
            if (!string.IsNullOrEmpty(perf.RootKind))
                suffixes.Add(perf.RootKind);
            if (!string.IsNullOrEmpty(perf.Source))
                suffixes.Add($"from {perf.Source}");
        }

        return suffixes.Count > 0 ? $"{member} ({string.Join(", ", suffixes)})" : member;
    }

    private static string? RootAnnotation(CallTreePerf perf)
    {
        // The Root field combines the reverse-graph classification (target/entrypoint) with the
        // source assembly for callers pulled in from the --bin/--project/--caller-package scope, so
        // reach evidence can name the caller library when requested.
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(perf.RootKind))
            parts.Add(perf.RootKind);
        if (!string.IsNullOrEmpty(perf.Source))
            parts.Add($"from {perf.Source}");
        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static string? Annotation(CallTreePerf? perf, string fieldName)
    {
        if (perf is null)
            return null;

        var normalized = NormalizeField(fieldName);
        var signals = perf.SignalsOrNone;
        return normalized switch
        {
            "fanin" or "fanincount" => $"fanin {perf.Fanin}",
            "fanout" or "fanoutcount" => $"fanout {perf.Fanout}",
            "depth" or "maxdepth" => $"depth {perf.MaxDepth}",
            "loop" or "inloop" or "looping" => perf.InLoop ? (perf.LoopHint ?? "loop") : null,
            "root" or "rootkind" or "classification" => RootAnnotation(perf),
            "source" or "assembly" => perf.Source is { } source ? $"from {source}" : null,
            "alloc" or "allocs" or "allocations" => signals.Allocations > 0 ? $"alloc {signals.Allocations}" : null,
            "copy" or "copies" => signals.Copies > 0 ? $"copy {signals.Copies}" : null,
            "unsafe" => signals.Unsafe ? "unsafe" : null,
            "reflection" or "reflect" => signals.Reflection > 0 ? $"reflection {signals.Reflection}" : null,
            "throw" or "throws" or "throwsites" => signals.Throws > 0 ? $"throw {signals.Throws}" : null,
            "catch" or "catches" => signals.Catches > 0 ? $"catch {signals.Catches}" : null,
            "finally" or "finallys" => signals.Finallys > 0 ? $"finally {signals.Finallys}" : null,
            "exceptions" or "exceptiontypes" or "constructedexceptions" => signals.ExceptionTypes.Length > 0
                ? "exceptions " + string.Join(",", signals.ExceptionTypes)
                : null,
            "evidenceil" or "evidence" or "il" => EvidenceIL(signals),
            _ => null,
        };
    }

    // Compact IL receipts for the projected signals: the offsets of the signal-bearing
    // instructions (newobj/newarr/throw/ldftn/reflection calls).
    private static string? EvidenceIL(MethodSignals signals)
    {
        var offsets = signals.Evidence;
        if (offsets.Length == 0)
            return null;
        return "il " + string.Join(",", offsets.Select(offset => $"IL_{offset:X4}"));
    }

    private static string NormalizeField(string fieldName)
    {
        var builder = new StringBuilder();
        foreach (var ch in fieldName)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }
}
