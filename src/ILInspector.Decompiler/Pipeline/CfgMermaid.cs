using ILInspector.ControlFlow;
using ILInspector.Instructions;
using System.Text;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Renders control-flow blocks as a mermaid <c>flowchart</c>. Raised IR blocks
/// derive their edges from <see cref="Cfg.Build"/>; rung-1 IL blocks carry the
/// same <see cref="BlockEdges"/> vocabulary directly. Mermaid is the default
/// graph export (issue #635) because GitHub renders it natively in issues, PRs,
/// and READMEs and it is plain text an agent can emit and diff without a render
/// step. A DOT export may follow for pathological giant methods and external
/// graph tooling.
/// </summary>
public static class CfgMermaid
{
    /// <summary>
    /// Returns a complete <c>flowchart TD</c> for the raised IR blocks of a
    /// single container. Normal successor edges are solid; edges to targets
    /// outside the container are dashed and labelled <c>external</c>; method
    /// exits and region exits flow to shared stadium-shaped terminals.
    /// </summary>
    public static string Render(IReadOnlyList<Block> blocks)
    {
        var edges = Cfg.Build(blocks);
        return Render(blocks, static block => block.StartOffset, index => edges[index]);
    }

    /// <summary>
    /// Returns a complete <c>flowchart TD</c> for rung-1 IL blocks, using the
    /// EH-aware edges produced by <see cref="BlockGraph"/>.
    /// </summary>
    internal static string Render(IReadOnlyList<InstructionBlock> blocks)
        => Render(blocks, static block => block.Start, index => blocks[index].Edges);

    static string Render<TBlock>(
        IReadOnlyList<TBlock> blocks,
        Func<TBlock, int> startOffset,
        Func<int, BlockEdges> getEdges)
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart TD");

        for (int i = 0; i < blocks.Count; i++)
        {
            int offset = startOffset(blocks[i]);
            sb.AppendLine($"  {NodeId(offset)}[\"IL_{offset:X4}\"]");
        }

        bool exits = false, leaves = false;
        var externals = new SortedSet<int>();

        for (int i = 0; i < blocks.Count; i++)
        {
            int offset = startOffset(blocks[i]);
            var edges = getEdges(i);
            foreach (int successor in edges.Successors)
                sb.AppendLine($"  {NodeId(offset)} --> {NodeId(startOffset(blocks[successor]))}");
            foreach (int target in edges.ExternalTargets)
            {
                externals.Add(target);
                sb.AppendLine($"  {NodeId(offset)} -.->|external| ext_{target:X4}");
            }
            if (edges.ExitsMethod)
            {
                exits = true;
                sb.AppendLine($"  {NodeId(offset)} --> _ret");
            }
            if (edges.LeavesRegion)
            {
                leaves = true;
                sb.AppendLine($"  {NodeId(offset)} --> _leave");
            }
        }

        foreach (int target in externals)
            sb.AppendLine($"  ext_{target:X4}[\"IL_{target:X4} (external)\"]");
        if (exits)
            sb.AppendLine("  _ret([\"return\"])");
        if (leaves)
            sb.AppendLine("  _leave([\"leave region\"])");

        return sb.ToString();
    }

    static string NodeId(int offset) => $"b{offset:X4}";
}
