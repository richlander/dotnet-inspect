using System.Text;

using ILInspector.ControlFlow;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;

namespace ILInspector.DecompilerHarness;

enum CfgDumpStage
{
    Raised,
    Il,
}

static class CfgDumpStageParser
{
    public static CfgDumpStage Parse(string value)
        => value.ToLowerInvariant() switch
        {
            "raised" => CfgDumpStage.Raised,
            "il" => CfgDumpStage.Il,
            _ => throw new ArgumentException(
                $"Unknown CFG stage '{value}'. Expected raised or il."),
        };
}

sealed record CfgDumpGraph(
    IReadOnlyList<int> BlockOffsets,
    IReadOnlyList<BlockEdges> Edges)
{
    public static CfgDumpGraph FromRaised(IReadOnlyList<Block> blocks)
        => new(
            blocks.Select(block => block.StartOffset).ToArray(),
            Cfg.Build(blocks));

    public static CfgDumpGraph FromIl(BlockGraph graph)
        => new(
            graph.Blocks.Select(block => block.Start).ToArray(),
            graph.Blocks.Select(block => block.Edges).ToArray());
}

sealed record IlCfgDump(CfgDumpGraph Graph, string? IncompleteReason)
{
    public int ExitCode => IncompleteReason is null ? 0 : 1;

    public string? Diagnostic => IncompleteReason is null
        ? null
        : $"// IL CFG incomplete: {IncompleteReason}";

    public static IlCfgDump From(MethodInstructions instructions)
        => new(
            CfgDumpGraph.FromIl(instructions.Blocks),
            instructions.IsComplete
                ? null
                : instructions.Blocks.IncompleteReason ?? "unknown reason");

    public static IlCfgDump Failed(string reason)
        => new(
            new CfgDumpGraph(
                Array.Empty<int>(),
                Array.Empty<BlockEdges>()),
            reason);
}

static class CfgDumpRenderer
{
    public static string RenderText(CfgDumpGraph graph)
    {
        var predecessors = new List<int>[graph.BlockOffsets.Count];
        for (int i = 0; i < predecessors.Length; i++)
            predecessors[i] = [];
        for (int i = 0; i < graph.Edges.Count; i++)
            foreach (int successor in graph.Edges[i].Successors)
                predecessors[successor].Add(i);

        var output = new StringBuilder();
        for (int i = 0; i < graph.BlockOffsets.Count; i++)
        {
            var predecessorOffsets = predecessors[i]
                .Select(predecessor => graph.BlockOffsets[predecessor])
                .Order()
                .ToArray();
            output.AppendLine(
                $"  IL_{graph.BlockOffsets[i]:X4}  preds: {OffsetSet(predecessorOffsets),-28}  succs: {Successors(graph, graph.Edges[i])}");
        }
        return output.ToString();
    }

    public static string RenderMermaid(CfgDumpGraph graph)
        => CfgMermaid.Render(graph.BlockOffsets, graph.Edges);

    static string OffsetSet(IReadOnlyList<int> offsets)
        => offsets.Count == 0
            ? "-"
            : string.Join(", ", offsets.Select(offset => $"IL_{offset:X4}"));

    static string Successors(CfgDumpGraph graph, BlockEdges edges)
    {
        var parts = new List<string>();
        foreach (int successor in edges.Successors)
            parts.Add($"IL_{graph.BlockOffsets[successor]:X4}");
        foreach (int target in edges.ExternalTargets)
            parts.Add($"IL_{target:X4} (external)");
        if (edges.ExitsMethod)
            parts.Add("(return)");
        if (edges.LeavesRegion)
            parts.Add("(leave region)");
        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }
}
