namespace ILInspector.Decompiler;

/// <summary>
/// A natural loop: a header block that dominates all blocks in the loop body,
/// with at least one back-edge from a body block to the header.
/// </summary>
public sealed class NaturalLoop
{
    /// <summary>Block index of the loop header.</summary>
    public int HeaderIndex { get; }

    /// <summary>All block indices in the loop body (including the header).</summary>
    public HashSet<int> BodyIndices { get; }

    /// <summary>Block indices that have back-edges to the header.</summary>
    public List<int> BackEdgeSources { get; }

    public NaturalLoop(int headerIndex, HashSet<int> bodyIndices, List<int> backEdgeSources)
    {
        HeaderIndex = headerIndex;
        BodyIndices = bodyIndices;
        BackEdgeSources = backEdgeSources;
    }
}

/// <summary>
/// Detects natural loops in a CFG using back-edge analysis on the dominator tree.
/// A back-edge is an edge t→h where h dominates t.
/// </summary>
internal static class LoopDetector
{
    /// <summary>
    /// Find all natural loops in the CFG.
    /// </summary>
    public static List<NaturalLoop> DetectLoops(ControlFlowGraph cfg, DominatorTree domTree)
    {
        var loops = new Dictionary<int, (HashSet<int> Body, List<int> BackEdges)>();

        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
        {
            var block = cfg.BasicBlocks[i];
            foreach (var target in block.Targets)
            {
                int targetIdx = cfg.LookupIndex(target.Start);
                if (targetIdx < 0) continue;

                // Back-edge: target dominates source
                if (domTree.Dominates(targetIdx, i))
                {
                    if (!loops.TryGetValue(targetIdx, out var loopInfo))
                    {
                        loopInfo = ([], []);
                        loops[targetIdx] = loopInfo;
                    }

                    loopInfo.BackEdges.Add(i);

                    // Collect loop body: walk predecessors from back-edge source to header
                    CollectLoopBody(cfg, targetIdx, i, loopInfo.Body);
                }
            }
        }

        List<NaturalLoop> result = [];
        foreach (var (header, (body, backEdges)) in loops)
            result.Add(new NaturalLoop(header, body, backEdges));

        // Sort by header index for deterministic ordering
        result.Sort((a, b) => a.HeaderIndex.CompareTo(b.HeaderIndex));
        return result;
    }

    static void CollectLoopBody(ControlFlowGraph cfg, int headerIdx, int backEdgeSourceIdx, HashSet<int> body)
    {
        body.Add(headerIdx);
        if (backEdgeSourceIdx == headerIdx) return;

        var worklist = new Stack<int>();
        worklist.Push(backEdgeSourceIdx);

        while (worklist.Count > 0)
        {
            int idx = worklist.Pop();
            if (!body.Add(idx)) continue;

            var block = cfg.BasicBlocks[idx];
            foreach (var source in block.Sources)
            {
                int sourceIdx = cfg.LookupIndex(source.Start);
                if (sourceIdx >= 0 && !body.Contains(sourceIdx))
                    worklist.Push(sourceIdx);
            }
        }
    }
}
