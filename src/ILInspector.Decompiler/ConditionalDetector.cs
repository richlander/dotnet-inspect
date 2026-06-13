using System.Reflection.Metadata;

namespace ILInspector.Decompiler;

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

    /// <summary>
    /// Short-circuit continuation condition blocks absorbed into this pattern
    /// (a &amp;&amp; b, a || b chains), in evaluation order after
    /// <see cref="ConditionIndex"/>. Empty for plain conditionals.
    /// </summary>
    public IReadOnlyList<ConditionChainTerm> ChainTerms { get; }

    public ConditionalPattern(int conditionIndex, int thenIndex, int elseIndex, int followIndex, bool negateCondition = false, IReadOnlyList<ConditionChainTerm>? chainTerms = null)
    {
        ConditionIndex = conditionIndex;
        ThenIndex = thenIndex;
        ElseIndex = elseIndex;
        FollowIndex = followIndex;
        NegateCondition = negateCondition;
        ChainTerms = chainTerms ?? [];
    }
}

/// <summary>
/// One absorbed short-circuit condition block. <see cref="CombineWithOr"/>
/// gives the operator joining the preceding condition to the rest of the
/// chain starting at this block: prev &amp;&amp; rest or prev || rest.
/// </summary>
public sealed record ConditionChainTerm(int BlockIndex, bool CombineWithOr);

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
        List<NaturalLoop> loops,
        int[]? entryStackHeights = null,
        ILAstMethod? ast = null)
    {
        List<ConditionalPattern> patterns = [];
        var loopHeaders = new HashSet<int>(loops.Select(l => l.HeaderIndex));

        // Short-circuit chains: a condition block whose fallthrough is another
        // single-predecessor condition block forms one combined a && b / a || b
        // condition. Detect chains first; absorbed continuation blocks don't get
        // their own pattern.
        var chains = DetectShortCircuitChains(cfg, loopHeaders, entryStackHeights, ast, out var absorbed);

        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
        {
            var block = cfg.BasicBlocks[i];

            // Need exactly 2 successors (conditional branch)
            if (block.Targets.Count != 2) continue;

            // Continuation blocks of a short-circuit chain are rendered as part
            // of the chain head's combined condition.
            if (absorbed.Contains(i)) continue;

            // Chain heads: then/else come from the LAST chain block (the head's
            // own fallthrough just enters the chain), and the readability swaps
            // don't apply — the combined condition's polarity is fixed.
            if (chains.TryGetValue(i, out var chainInfo) && chainInfo.Terms.Count > 0)
            {
                var lastEdges = ConditionEdges(cfg, chainInfo.Terms[^1].BlockIndex)!.Value;
                int chainThen = chainInfo.NegateLast ? lastEdges.FalseIdx : lastEdges.TrueIdx;
                int chainElse = chainInfo.NegateLast ? lastEdges.TrueIdx : lastEdges.FalseIdx;
                int chainFollow = FindFollowBlock(cfg, domTree, chainInfo.Terms[^1].BlockIndex, chainThen, chainElse);
                if (chainFollow == -1 && IsDirectPredecessor(cfg, chainThen, chainElse))
                {
                    chainFollow = chainElse;
                    chainElse = -1;
                }
                // For chains, NegateCondition means: negate the LAST leaf only.
                patterns.Add(new ConditionalPattern(i, chainThen, chainElse, chainFollow, chainInfo.NegateLast, chainInfo.Terms));
                continue;
            }

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
    /// Walks fallthrough successions of condition blocks. For each chain head,
    /// resolves then/else from the LAST block's branch sense, then validates
    /// every leading block against them: a block whose false-edge goes to the
    /// overall else continues on true (an &amp;&amp; link); a block whose true-edge
    /// goes to the overall then continues on false (an || link). Chains are
    /// only kept when every link classifies cleanly.
    /// </summary>
    static Dictionary<int, (List<ConditionChainTerm> Terms, bool NegateLast)> DetectShortCircuitChains(
        ControlFlowGraph cfg, HashSet<int> loopHeaders, int[]? entryStackHeights, ILAstMethod? ast, out HashSet<int> absorbed)
    {
        // Absorbing a continuation block hides everything in it except its
        // condition, so it must contain NOTHING but the condition computation:
        // nops, foldable local-free temps the branch consumes, and the branch.
        // Without the AST to verify that, don't chain at all.
        if (ast is null)
        {
            absorbed = [];
            return [];
        }

        absorbed = [];
        var result = new Dictionary<int, (List<ConditionChainTerm> Terms, bool NegateLast)>();

        // Entry stack height of a block; > 0 means it consumes stack-carried
        // values (a value merge, not a statement-level condition).
        int EntryHeight(int idx)
        {
            if (entryStackHeights is null) return 0;
            int start = cfg.BasicBlocks[idx].Start;
            return start >= 0 && start < entryStackHeights.Length
                ? Math.Max(entryStackHeights[start], 0)
                : 0;
        }

        // Predecessor counts: a continuation must be reachable only from the
        // preceding condition, or its evaluation isn't conditional on it.
        var predCount = new int[cfg.BasicBlocks.Count];
        foreach (var b in cfg.BasicBlocks)
        {
            foreach (var t in b.Targets)
            {
                int ti = cfg.LookupIndex(t.Start);
                if (ti >= 0) predCount[ti]++;
            }
        }

        var chainMembers = new HashSet<int>();

        for (int i = 0; i < cfg.BasicBlocks.Count; i++)
        {
            if (chainMembers.Contains(i) || loopHeaders.Contains(i))
                continue;
            if (ConditionEdges(cfg, i) is null)
                continue;

            // Collect the fallthrough succession of single-pred condition blocks.
            var chain = new List<int> { i };
            int cur = i;
            while (true)
            {
                int ft = cfg.LookupIndex(cfg.BasicBlocks[cur].FallthroughTarget?.Start ?? -1);
                if (ft < 0 || predCount[ft] != 1 || loopHeaders.Contains(ft) || ConditionEdges(cfg, ft) is null
                    || EntryHeight(ft) > 0
                    || !IsPureConditionBlock(ast, ft))
                    break;
                chain.Add(ft);
                cur = ft;
            }

            if (chain.Count < 2)
                continue;

            // Resolve overall then/else from the last block — trying both
            // polarities, since the final test may be inverted (a || b lowers
            // to jump-if-a-true / jump-if-NOT-b-true) — then validate links.
            // Trim from the end until validation succeeds or the chain is gone.
            bool found = false;
            while (!found && chain.Count >= 2)
            {
                var last = ConditionEdges(cfg, chain[^1])!.Value;
                foreach (bool negateLast in (ReadOnlySpan<bool>)[false, true])
                {
                    int thenIdx = negateLast ? last.FalseIdx : last.TrueIdx;
                    int elseIdx = negateLast ? last.TrueIdx : last.FalseIdx;

                    var terms = new List<ConditionChainTerm>();
                    bool valid = EntryHeight(thenIdx) == 0 && EntryHeight(elseIdx) == 0;
                    int join = FindFollowBlock(cfg, null!, chain[^1], thenIdx, elseIdx);
                    if (join >= 0 && EntryHeight(join) > 0)
                        valid = false;
                    for (int c = 0; valid && c < chain.Count - 1; c++)
                    {
                        var e = ConditionEdges(cfg, chain[c])!.Value;
                        int next = chain[c + 1];
                        if (e.TrueIdx == next && e.FalseIdx == elseIdx)
                            terms.Add(new ConditionChainTerm(next, CombineWithOr: false)); // cond && rest
                        else if (e.FalseIdx == next && e.TrueIdx == thenIdx)
                            terms.Add(new ConditionChainTerm(next, CombineWithOr: true));  // cond || rest
                        else
                            valid = false;
                    }

                    if (valid)
                    {
                        result[chain[0]] = (terms, negateLast);
                        foreach (var t in terms)
                            absorbed.Add(t.BlockIndex);
                        chainMembers.UnionWith(chain);
                        found = true;
                        break;
                    }
                }

                if (!found)
                    chain.RemoveAt(chain.Count - 1);
            }
        }

        return result;
    }

    /// <summary>
    /// A block that computes ONLY a condition: optional nops, optional
    /// single-use local-free temp stores the branch consumes (Debug bool
    /// routing — mirroring what the emitter's condition folding can absorb),
    /// and a trailing conditional branch. Anything else (assignments, calls,
    /// stores) would be silently dropped if the block were absorbed.
    /// </summary>
    static bool IsPureConditionBlock(ILAstMethod ast, int blockIdx)
    {
        if (blockIdx < 0 || blockIdx >= ast.Blocks.Count)
            return false;
        var nodes = ast.Blocks[blockIdx].Nodes;
        if (nodes.Count == 0)
            return false;
        if (nodes[^1] is not ILAstStatement { Expression: var branch }
            || branch.OpCode is not (ILOpCode.Brtrue or ILOpCode.Brtrue_s
                or ILOpCode.Brfalse or ILOpCode.Brfalse_s
                or ILOpCode.Beq or ILOpCode.Beq_s or ILOpCode.Bne_un or ILOpCode.Bne_un_s
                or ILOpCode.Bge or ILOpCode.Bge_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s
                or ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s
                or ILOpCode.Ble or ILOpCode.Ble_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s
                or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Blt_un or ILOpCode.Blt_un_s))
        {
            return false;
        }

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            if (nodes[i] is not ILAstStatement { Expression: var expr })
                return false;
            if (expr.OpCode == ILOpCode.Nop)
                continue;
            // Foldable temp: a store the branch reads, whose value loads no
            // locals. The branch must actually load the stored local — the
            // emitter folds by substituting that load; an unreferenced store
            // would be dropped.
            if (expr.OpCode is ILOpCode.Stloc or ILOpCode.Stloc_s
                    or ILOpCode.Stloc_0 or ILOpCode.Stloc_1 or ILOpCode.Stloc_2 or ILOpCode.Stloc_3
                && expr.Arguments.Count == 1
                && !TreeLoadsAnyLocal(expr.Arguments[0])
                && StoredLocalName(expr) is string stored
                && TreeLoadsLocal(branch, stored))
            {
                continue;
            }
            return false;
        }
        return true;
    }

    static string? StoredLocalName(ILAstExpression store) => store.Operand ?? store.OpCode switch
    {
        ILOpCode.Stloc_0 => "V_0",
        ILOpCode.Stloc_1 => "V_1",
        ILOpCode.Stloc_2 => "V_2",
        ILOpCode.Stloc_3 => "V_3",
        _ => null,
    };

    static bool TreeLoadsLocal(ILAstExpression expr, string name)
    {
        if (expr.OpCode is ILOpCode.Ldloc or ILOpCode.Ldloc_s
            or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3)
        {
            string? loaded = expr.Operand ?? expr.OpCode switch
            {
                ILOpCode.Ldloc_0 => "V_0",
                ILOpCode.Ldloc_1 => "V_1",
                ILOpCode.Ldloc_2 => "V_2",
                ILOpCode.Ldloc_3 => "V_3",
                _ => null,
            };
            if (loaded == name)
                return true;
        }
        foreach (var arg in expr.Arguments)
        {
            if (TreeLoadsLocal(arg, name))
                return true;
        }
        return false;
    }

    static bool TreeLoadsAnyLocal(ILAstExpression expr)
    {
        if (expr.OpCode is ILOpCode.Ldloc or ILOpCode.Ldloc_s
            or ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
            or ILOpCode.Ldloca or ILOpCode.Ldloca_s)
        {
            return true;
        }
        foreach (var arg in expr.Arguments)
        {
            if (TreeLoadsAnyLocal(arg))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True/false targets of a two-way condition block, normalized for branch
    /// sense: comparison branches and brtrue jump when the condition is TRUE;
    /// brfalse jumps when it is FALSE.
    /// </summary>
    internal static (int TrueIdx, int FalseIdx)? ConditionEdges(ControlFlowGraph cfg, int idx)
    {
        var b = cfg.BasicBlocks[idx];
        if (b.Targets.Count != 2 || b.BranchTarget is null || b.FallthroughTarget is null)
            return null;
        int bt = cfg.LookupIndex(b.BranchTarget.Start);
        int ft = cfg.LookupIndex(b.FallthroughTarget.Start);
        if (bt < 0 || ft < 0)
            return null;
        bool jumpWhenFalse = b.TerminatingBranch is ILOpCode.Brfalse or ILOpCode.Brfalse_s;
        return jumpWhenFalse ? (ft, bt) : (bt, ft);
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
