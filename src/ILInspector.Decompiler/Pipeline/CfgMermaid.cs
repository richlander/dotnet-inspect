using ILInspector.ControlFlow;
using System.Text;
using ILInspector.Text;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Renders one container's control-flow graph as a mermaid <c>flowchart</c> —
/// the graph form of <see cref="Cfg.Build"/>'s edges. Mermaid is the default
/// graph export (issue #635) because GitHub renders it natively in issues, PRs,
/// and READMEs and it is plain text an agent can emit and diff without a render
/// step. Edges come from the same <see cref="Cfg.Build"/> the printer's
/// definite-assignment dataflow uses, so the picture cannot drift from the
/// analysis. A DOT export may follow for pathological giant methods and
/// external graph tooling.
/// </summary>
public static class CfgMermaid
{
    /// <summary>
    /// Returns a complete <c>flowchart TD</c> for the blocks of a single
    /// container. Normal successor edges are solid; edges to targets outside the
    /// container are dashed and labelled <c>external</c>; method exits and
    /// region exits flow to shared stadium-shaped terminals.
    /// </summary>
    public static string Render(IReadOnlyList<Block> blocks)
    {
        var edges = Cfg.Build(blocks);
        return Render(blocks.Select(block => block.StartOffset).ToArray(), edges);
    }

    /// <summary>
    /// Returns a complete <c>flowchart TD</c> for representation-neutral block
    /// offsets and their already-computed edges.
    /// </summary>
    internal static string Render(
        IReadOnlyList<int> blockOffsets,
        IReadOnlyList<BlockEdges> edges)
    {
        ArgumentNullException.ThrowIfNull(blockOffsets);
        ArgumentNullException.ThrowIfNull(edges);
        if (blockOffsets.Count != edges.Count)
            throw new ArgumentException("Block offsets and edges must have the same count.");

        var sb = new StringBuilder();
        sb.AppendLf("flowchart TD");

        for (int i = 0; i < blockOffsets.Count; i++)
            sb.AppendLf($"  {NodeId(blockOffsets[i])}[\"IL_{blockOffsets[i]:X4}\"]");

        bool exits = false, leaves = false;
        var externals = new SortedSet<int>();

        for (int i = 0; i < blockOffsets.Count; i++)
        {
            var e = edges[i];
            foreach (int s in e.Successors)
                sb.AppendLf($"  {NodeId(blockOffsets[i])} --> {NodeId(blockOffsets[s])}");
            foreach (int t in e.ExternalTargets)
            {
                externals.Add(t);
                sb.AppendLf($"  {NodeId(blockOffsets[i])} -.->|external| ext_{t:X4}");
            }
            if (e.ExitsMethod)
            {
                exits = true;
                sb.AppendLf($"  {NodeId(blockOffsets[i])} --> _ret");
            }
            if (e.LeavesRegion)
            {
                leaves = true;
                sb.AppendLf($"  {NodeId(blockOffsets[i])} --> _leave");
            }
        }

        foreach (int t in externals)
            sb.AppendLf($"  ext_{t:X4}[\"IL_{t:X4} (external)\"]");
        if (exits)
            sb.AppendLf("  _ret([\"return\"])");
        if (leaves)
            sb.AppendLf("  _leave([\"leave region\"])");

        return sb.ToString();
    }

    static string NodeId(int offset) => $"b{offset:X4}";
}
