using System.Reflection.Metadata;

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

    /// <summary>Whether the condition should be negated when emitting (due to then/else swap).</summary>
    public bool NegateCondition { get; }

    public ConditionalPattern(int conditionIndex, int thenIndex, int elseIndex, int followIndex, bool negateCondition = false)
    {
        ConditionIndex = conditionIndex;
        ThenIndex = thenIndex;
        ElseIndex = elseIndex;
        FollowIndex = followIndex;
        NegateCondition = negateCondition;
    }
}

/// <summary>
/// Detects if/else patterns from conditional branches in the CFG.
/// </summary>
internal static class ConditionalDetector
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

            // Use the structured branch/fallthrough info when available
            int branchIdx = block.BranchTarget is not null ? cfg.LookupIndex(block.BranchTarget.Start) : -1;
            int fallthroughIdx = block.FallthroughTarget is not null ? cfg.LookupIndex(block.FallthroughTarget.Start) : -1;

            int thenIdx, elseIdx;

            if (branchIdx >= 0 && fallthroughIdx >= 0)
            {
                // For brfalse: branch is taken when false, fall-through when true
                //   → then = fall-through (condition true path), else = branch target
                // For brtrue: branch is taken when true, fall-through when false
                //   → then = branch target (condition true path), else = fall-through
                bool isBrfalse = block.TerminatingBranch is ILOpCode.Brfalse or ILOpCode.Brfalse_s;
                if (isBrfalse)
                {
                    thenIdx = fallthroughIdx;
                    elseIdx = branchIdx;
                }
                else
                {
                    thenIdx = branchIdx;
                    elseIdx = fallthroughIdx;
                }
            }
            else
            {
                // Fallback: use target ordering
                var targets = block.Targets.ToList();
                thenIdx = cfg.LookupIndex(targets[0].Start);
                elseIdx = cfg.LookupIndex(targets[1].Start);
                if (thenIdx < 0 || elseIdx < 0) continue;
            }

            // Skip if this block IS a loop header — its branch is the loop condition,
            // not a conditional pattern. It will be emitted as part of the loop structure.
            if (loopHeaders.Contains(i))
                continue;

            // Skip if this is a loop back-edge
            if (loopHeaders.Contains(thenIdx) || loopHeaders.Contains(elseIdx))
            {
                // One target might be the loop header (back-edge) — that's the loop condition
                // Only skip if both targets are loop headers
                if (loopHeaders.Contains(thenIdx) && loopHeaders.Contains(elseIdx))
                    continue;
            }

            // Try to find a common follow block
            int followIdx = FindFollowBlock(cfg, domTree, i, thenIdx, elseIdx);

            // Simple if (no else): one branch goes directly to the follow block
            bool simpleIfSwapped = false;
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
                    simpleIfSwapped = true;
                }
            }

            // If the "then" block is trivial (just leave/br/endfinally) and the
            // "else" block has substance, swap them and negate the condition.
            // This produces more natural code: if (!cond) { body } instead of
            // if (cond) { leave } else { body }
            bool negate = simpleIfSwapped;
            if (elseIdx >= 0 && IsTrivialBlock(cfg, thenIdx) && !IsTrivialBlock(cfg, elseIdx))
            {
                (thenIdx, elseIdx) = (elseIdx, thenIdx);
                negate = true;
                // If the swapped else is trivial, treat as simple-if (no else)
                if (IsTrivialBlock(cfg, elseIdx))
                {
                    followIdx = elseIdx;
                    elseIdx = -1;
                }
            }

            // Guard-clause pattern: if the "else" is a terminal block (return/throw)
            // and "then" is a continuation, swap to produce: if (cond) { return; } continuation
            if (elseIdx >= 0 && !negate
                && IsTerminalBlock(cfg, elseIdx) && !IsTerminalBlock(cfg, thenIdx))
            {
                (thenIdx, elseIdx) = (elseIdx, thenIdx);
                negate = true;
                // The swapped else is the continuation — treat as simple-if
                followIdx = elseIdx;
                elseIdx = -1;
            }

            patterns.Add(new ConditionalPattern(i, thenIdx, elseIdx, followIdx, negate));
        }

        return patterns;
    }

    /// <summary>
    /// A block is "trivial" if it contains only control flow transfer
    /// instructions with no meaningful computation (leave, br, endfinally, ret with no value).
    /// </summary>
    static bool IsTrivialBlock(ControlFlowGraph cfg, int blockIdx)
    {
        if (blockIdx < 0 || blockIdx >= cfg.BasicBlocks.Count)
            return false;

        var block = cfg.BasicBlocks[blockIdx];
        // Blocks of 1-2 bytes are typically just leave.s, br.s, endfinally, ret
        return block.Size <= 2;
    }

    /// <summary>
    /// A block is "terminal" if it has no successors (ends with ret, throw, or rethrow).
    /// Guard-clause patterns end with a terminal block.
    /// </summary>
    static bool IsTerminalBlock(ControlFlowGraph cfg, int blockIdx)
    {
        if (blockIdx < 0 || blockIdx >= cfg.BasicBlocks.Count)
            return false;

        return cfg.BasicBlocks[blockIdx].Targets.Count == 0;
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
