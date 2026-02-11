namespace DotnetInspector.Decompiler;

/// <summary>
/// A conditional branch pattern: a block ending in a two-way branch
/// where both targets converge at a common follow block.
/// </summary>
public sealed class ConditionalPattern
{
    /// <summary>Block index of the condition block (ends with conditional branch).</summary>
    public int ConditionIndex { get; }

    /// <summary>Block index of the "then" branch target.</summary>
    public int ThenIndex { get; }

    /// <summary>Block index of the "else" branch target (-1 if no else body).</summary>
    public int ElseIndex { get; }

    /// <summary>Block index where both branches converge (-1 if unknown).</summary>
    public int FollowIndex { get; }

    public ConditionalPattern(int conditionIndex, int thenIndex, int elseIndex, int followIndex)
    {
        ConditionIndex = conditionIndex;
        ThenIndex = thenIndex;
        ElseIndex = elseIndex;
        FollowIndex = followIndex;
    }
}

/// <summary>
/// Detects if/else patterns from conditional branches in the CFG.
/// </summary>
public static class ConditionalDetector
{
    /// <summary>
    /// Find if/else patterns in the CFG.
    /// </summary>
    public static List<ConditionalPattern> DetectConditionals(
        ControlFlowGraph cfg,
        DominatorTree domTree,
        List<NaturalLoop> loops)
    {
        List<ConditionalPattern> patterns = [];
        var loopHeaders = new HashSet<int>(loops.Select(l => l.HeaderIndex));

        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
        {
            var block = cfg.BasicBlocks[i];

            // Need exactly 2 successors (conditional branch)
            if (block.Targets.Count != 2) continue;

            var targets = block.Targets.ToList();
            int targetA = cfg.LookupIndex(targets[0].Start);
            int targetB = cfg.LookupIndex(targets[1].Start);
            if (targetA < 0 || targetB < 0) continue;

            // Skip if this is a loop back-edge
            if (loopHeaders.Contains(targetA) || loopHeaders.Contains(targetB))
            {
                // One target might be the loop header (back-edge) — that's the loop condition
                // Only skip if both targets are loop headers
                if (loopHeaders.Contains(targetA) && loopHeaders.Contains(targetB))
                    continue;
            }

            // Determine then/else: the fall-through target is typically then
            int thenIdx = targetA;
            int elseIdx = targetB;

            // Try to find a common follow block
            int followIdx = FindFollowBlock(cfg, domTree, i, thenIdx, elseIdx);

            // Simple if (no else): one branch goes directly to the follow block
            if (followIdx == -1)
            {
                if (IsDirectPredecessor(cfg, thenIdx, elseIdx))
                {
                    followIdx = elseIdx;
                    elseIdx = -1;
                }
                else if (IsDirectPredecessor(cfg, elseIdx, thenIdx))
                {
                    followIdx = thenIdx;
                    thenIdx = elseIdx;
                    elseIdx = -1;
                }
            }

            patterns.Add(new ConditionalPattern(i, thenIdx, elseIdx, followIdx));
        }

        return patterns;
    }

    static int FindFollowBlock(ControlFlowGraph cfg, DominatorTree domTree, int condIdx, int thenIdx, int elseIdx)
    {
        // The follow block is typically the block immediately dominated by the condition
        // block that both branches can reach. Simple heuristic: check if both then/else
        // have a common successor.
        var thenBlock = cfg.BasicBlocks[thenIdx];
        var elseBlock = cfg.BasicBlocks[elseIdx];

        var thenTargets = new HashSet<int>(thenBlock.Targets.Select(t => cfg.LookupIndex(t.Start)));
        foreach (var target in elseBlock.Targets)
        {
            int idx = cfg.LookupIndex(target.Start);
            if (idx >= 0 && thenTargets.Contains(idx))
                return idx;
        }

        return -1;
    }

    static bool IsDirectPredecessor(ControlFlowGraph cfg, int blockIdx, int successorIdx)
    {
        var block = cfg.BasicBlocks[blockIdx];
        return block.Targets.Any(t => cfg.LookupIndex(t.Start) == successorIdx);
    }
}
