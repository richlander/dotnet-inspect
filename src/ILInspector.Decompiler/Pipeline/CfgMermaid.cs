using ILInspector.ControlFlow;
using System.Text;

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
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");

        for (int i = 0; i < blocks.Count; i++)
            sb.AppendLine($"  {NodeId(blocks[i])}[\"IL_{blocks[i].StartOffset:X4}\"]");

        bool exits = false, leaves = false;
        var externals = new SortedSet<int>();

        for (int i = 0; i < blocks.Count; i++)
        {
            var e = edges[i];
            foreach (int s in e.Successors)
                sb.AppendLine($"  {NodeId(blocks[i])} --> {NodeId(blocks[s])}");
            foreach (int t in e.ExternalTargets)
            {
                externals.Add(t);
                sb.AppendLine($"  {NodeId(blocks[i])} -.->|external| ext_{t:X4}");
            }
            if (e.ExitsMethod)
            {
                exits = true;
                sb.AppendLine($"  {NodeId(blocks[i])} --> _ret");
            }
            if (e.LeavesRegion)
            {
                leaves = true;
                sb.AppendLine($"  {NodeId(blocks[i])} --> _leave");
            }
        }

        foreach (int t in externals)
            sb.AppendLine($"  ext_{t:X4}[\"IL_{t:X4} (external)\"]");
        if (exits)
            sb.AppendLine("  _ret([\"return\"])");
        if (leaves)
            sb.AppendLine("  _leave([\"leave region\"])");

        return sb.ToString();
    }

    static string NodeId(Block block) => $"b{block.StartOffset:X4}";
}
