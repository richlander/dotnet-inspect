using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a <em>mixed</em> short-circuit guard chain — one whose guards branch to
/// <em>both</em> arms of a diamond — into a single <see cref="ConditionalBranch"/>,
/// before structuring. <see cref="OrChainDiamondPass"/> already handles the pure
/// case where every guard branches to the same true arm (<c>a || b</c>); a
/// condition that mixes <c>||</c> and <c>&amp;&amp;</c>, e.g.
/// <c>a || (b &amp;&amp; c)</c>, lowers to a run of guards that branch to the
/// true arm and the false arm in turn:
/// <code>
///   if (a)        goto THEN;   // → the branch (taken) arm
///   if (!b || !c) goto ELSE;   // → the fall arm, the OTHER arm
///   THEN: …; goto M;            // (the block right after the chain)
///   ELSE: …;                    // falls to M
///   M: REST
/// </code>
/// The guards target two distinct arms, so neither <see cref="OrChainDiamondPass"/>
/// (same true arm) nor the lexical <see cref="StructuringPass"/> diamond model
/// (one conditional per diamond) can name the join, and the container stays flat.
///
/// <para>This pass reverses the short-circuit lowering. The chain is a maximal run
/// of pure single-condition guards <c>[p, q]</c> that fall through to each other;
/// the last guard <c>q</c> branches to one arm (<c>branchArm</c>) and falls into
/// the other (<c>fallArm</c>, the block right after the chain). Every earlier
/// guard branches to one of those two arms. Reconstruct the condition under which
/// control reaches <c>branchArm</c>, processed back to front so evaluation order
/// and short-circuiting are preserved exactly:</para>
/// <list type="bullet">
/// <item>last guard: <c>reach = cond_q</c> (it branches to <c>branchArm</c>).</item>
/// <item>earlier guard to <c>branchArm</c>: <c>reach = cond_i || reach</c>.</item>
/// <item>earlier guard to <c>fallArm</c>: <c>reach = !cond_i &amp;&amp; reach</c>.</item>
/// </list>
/// The chain collapses to <c>if (reach) goto branchArm</c> with the fall arm as
/// the fall-through region — the single-conditional diamond the structuring pass
/// raises (it negates the condition back to <c>if (!reach) { fallArm } else
/// { branchArm }</c>).
///
/// <para>Sound and order-preserving, mirroring <see cref="OrChainDiamondPass"/>'s
/// discipline: inner guards must be pure single-condition blocks (only the root
/// may carry leading operand setup), the run must genuinely mix the two arms (at
/// least one earlier guard branches to <c>fallArm</c>, else it is the pure-OR
/// shape the other pass owns), the fall arm must end by branching strictly past
/// <c>branchArm</c> to a real merge (a true diamond), and nothing outside the run
/// may branch into the inner guards the fold drops. Each guard condition is used
/// exactly once, left to right, so <c>a</c> is evaluated before <c>b</c> just as
/// the fall-through chain did, and re-lowers to the same branch sequence.</para>
/// </summary>
public sealed class MixedShortCircuitChainPass : IIrPass
{
    public string Name => "mixed-short-circuit-chain";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        // A surviving leave may target a block this fold drops (an inner guard) —
        // including from another container the per-container scan cannot see.
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (TryFold(container, leaveTargets, stepper))
                return true;
        }
        return false;
    }

    static bool TryFold(BlockContainer container, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        for (int p = 0; p < blocks.Count; p++)
        {
            if (Chain(blocks, offsetToIndex, p) is { } chain
                && NoExternalEntry(blocks, p, chain.LastGuard, leaveTargets))
            {
                Fold(container, chain, stepper);
                return true;
            }
        }
        return false;
    }

    readonly record struct ChainShape(int Root, int LastGuard, int BranchArmOffset, int FallArmOffset);

    /// <summary>
    /// The mixed-arm guard chain rooted at <paramref name="p"/>, or null. Guards
    /// [p, LastGuard] are pure single-condition blocks (the root may carry leading
    /// setup) that fall through to each other; the last branches to
    /// <c>BranchArmOffset</c> and falls into <c>FallArmOffset</c>; every guard
    /// branches to one of those two arms and at least one earlier guard branches
    /// to the fall arm (a genuine mix, not the pure-OR shape).
    /// </summary>
    static ChainShape? Chain(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int p)
    {
        if (ConditionTargetAtEnd(blocks[p]) is null)
            return null;

        // Extend the run while each following block is a pure single-condition
        // guard that falls through from the previous one (consecutive blocks; a
        // conditional's fall-through is the next block).
        int q = p;
        while (q + 1 < blocks.Count && ConditionTarget(blocks[q + 1]) is not null)
            q++;
        if (q == p)
            return null;   // a lone conditional is the single-target diamond already

        // The fall arm is the block right after the chain; the branch arm is the
        // last guard's taken target. Both must be real blocks outside the chain.
        int fallArmIndex = q + 1;
        if (fallArmIndex >= blocks.Count)
            return null;
        int fallArmOffset = blocks[fallArmIndex].StartOffset;

        if (TakenTarget(blocks[q]) is not { } branchArmOffset
            || !offsetToIndex.TryGetValue(branchArmOffset, out int branchArmIndex))
            return null;
        if (branchArmOffset == fallArmOffset)
            return null;   // last guard branches where it falls — not a two-arm split
        if (branchArmIndex <= q)
            return null;   // the branch arm must be strictly forward of the chain:
                           // a target at or before the chain is a back-edge (loop
                           // latch) or an entry into the dropped run, not a forward
                           // diamond arm. With fallArm = q+1, both arms are then
                           // forward, so every guard target is a forward edge.

        // Every guard must branch to one of the two arms, and at least one earlier
        // guard must branch to the fall arm (else all target the branch arm — the
        // pure-OR chain OrChainDiamondPass owns). The fall arm is reached by a
        // guard's taken edge, not the chain fall-through, so the mix is genuine.
        bool mixed = false;
        for (int i = p; i <= q; i++)
        {
            int target = TakenTarget(blocks[i]) ?? -1;
            if (target != branchArmOffset && target != fallArmOffset)
                return null;
            if (i < q && target == fallArmOffset)
                mixed = true;
        }
        if (!mixed)
            return null;

        // A true diamond: the fall arm ends by branching strictly past the branch
        // arm to a real merge, so structuring can name the join after the fold.
        if (UnconditionalBranchTarget(blocks[branchArmIndex - 1]) is not { } mergeOffset
            || !offsetToIndex.TryGetValue(mergeOffset, out int mergeIndex)
            || mergeIndex <= branchArmIndex)
            return null;

        return new ChainShape(p, q, branchArmOffset, fallArmOffset);
    }

    /// <summary>The taken branch target of a block ending in a conditional branch, or null.</summary>
    static int? TakenTarget(Block block)
        => block.Children.Count > 0 && block.Children[^1] is ConditionalBranch conditional
            ? conditional.TargetOffset
            : null;

    /// <summary>The single branch target of a pure one-statement condition block, or null.</summary>
    static int? ConditionTarget(Block block)
        => block.Children is [ConditionalBranch conditional] ? conditional.TargetOffset : null;

    /// <summary>
    /// The branch target of a block whose last statement is a conditional branch
    /// and whose earlier statements are all straight-line (no control flow), or
    /// null. Only the chain root may carry such setup — inner guards must be pure,
    /// since their leading statements would move out of short-circuit order.
    /// </summary>
    static int? ConditionTargetAtEnd(Block block)
    {
        if (block.Children.Count == 0 || block.Children[^1] is not ConditionalBranch conditional)
            return null;
        for (int s = 0; s < block.Children.Count - 1; s++)
            if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return null;
        return conditional.TargetOffset;
    }

    /// <summary>
    /// The target of a block that ends with an unconditional <see cref="Branch"/>
    /// and is otherwise straight-line, or null. The fall arm's terminating goto.
    /// </summary>
    static int? UnconditionalBranchTarget(Block block)
    {
        if (block.Children.Count == 0 || block.Children[^1] is not Branch branch)
            return null;
        for (int s = 0; s < block.Children.Count - 1; s++)
            if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return null;
        return branch.TargetOffset;
    }

    /// <summary>
    /// No block outside the chain may branch into the inner guards [Root+1, LastGuard]
    /// the fold drops — those edges would dangle at a vanished offset. The root and
    /// every block from the fall arm onward stay, so edges to them are fine; a
    /// leave into a dropped guard aborts the fold too.
    /// </summary>
    static bool NoExternalEntry(IReadOnlyList<Block> blocks, int p, int lastGuard, HashSet<int> leaveTargets)
    {
        var forbidden = new HashSet<int>();
        for (int idx = p + 1; idx <= lastGuard; idx++)
            forbidden.Add(blocks[idx].StartOffset);

        if (forbidden.Overlaps(leaveTargets))
            return false;

        for (int idx = 0; idx < blocks.Count; idx++)
        {
            if (idx > p && idx <= lastGuard)
                continue;   // inside the dropped run — internal fall-throughs are fine
            foreach (var node in blocks[idx].Children)
                foreach (int target in Targets(node))
                    if (forbidden.Contains(target))
                        return false;
        }
        return true;
    }

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch sw => sw.TargetOffsets,
        _ => [],
    };

    /// <summary>
    /// Negation that pushes through the short-circuit operators (De Morgan) and
    /// cancels double negations, so a folded condition reads as its clean dual
    /// (<c>!(!b || !c)</c> becomes <c>b &amp;&amp; c</c>) rather than a nested
    /// <see cref="LogicalNot"/>. Falls back to <see cref="Conditions.Negate"/> for
    /// leaves (comparisons invert, everything else wraps once).
    /// </summary>
    static IrExpression NegateClean(IrExpression condition)
    {
        switch (condition)
        {
            case LogicalNot not:
                return (IrExpression)not.DetachChildren()[0];
            case LogicalBinary binary:
                var operands = binary.DetachChildren();
                var left = NegateClean((IrExpression)operands[0]);
                var right = NegateClean((IrExpression)operands[1]);
                return new LogicalBinary(binary.Kind == LogicalKind.Or ? LogicalKind.And : LogicalKind.Or, left, right);
            default:
                return Conditions.Negate(condition);
        }
    }

    static void Fold(BlockContainer container, ChainShape chain, Stepper stepper)
    {
        var blocks = container.Blocks.ToList();
        int p = chain.Root, q = chain.LastGuard;

        // Each guard's taken target and condition, in chain order.
        var targets = new int[q - p + 1];
        var conditions = new IrExpression[q - p + 1];
        for (int i = p; i <= q; i++)
        {
            var conditional = (ConditionalBranch)blocks[i].Children[^1];
            targets[i - p] = conditional.TargetOffset;
            conditions[i - p] = (IrExpression)conditional.DetachChildren()[0];
        }

        // Reconstruct "reach the FALL arm" back to front as a clean positive
        // expression, then branch on its negation. Building the fall-arm condition
        // (rather than the branch-arm one) means the structuring pass's own
        // negation cancels the wrapper to a clean positive instead of a double
        // negation. The last guard branches to branchArm, so it reaches fallArm iff
        // its condition is false; an earlier guard to fallArm contributes
        // `cond || reach`, to branchArm contributes `!cond && reach`. Leftmost-first
        // preserves evaluation order; NegateClean keeps the De Morgan dual readable.
        IrExpression reachFall = NegateClean(conditions[q - p]);
        for (int i = q - 1; i >= p; i--)
        {
            int idx = i - p;
            reachFall = targets[idx] == chain.FallArmOffset
                ? new LogicalBinary(LogicalKind.Or, conditions[idx], reachFall)
                : new LogicalBinary(LogicalKind.And, NegateClean(conditions[idx]), reachFall);
        }

        // The folded guard keeps the root block's leading setup and branches to the
        // branch arm when the fall-arm condition does NOT hold. Wrapping in a single
        // LogicalNot lets the structuring pass's Negate unwrap it back to the clean
        // positive `if (reachFall) { fallArm } else { branchArm }`.
        var folded = new Block(blocks[p].StartOffset);
        var rootChildren = blocks[p].DetachChildren();
        for (int k = 0; k < rootChildren.Count - 1; k++)
            folded.Add(rootChildren[k]);
        folded.Add(new ConditionalBranch(new LogicalNot(reachFall), chain.BranchArmOffset));

        foreach (var block in blocks)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int idx = 0; idx < p; idx++)
            rebuilt.Add(blocks[idx]);
        rebuilt.Add(folded);
        for (int idx = q + 1; idx < blocks.Count; idx++)
            rebuilt.Add(blocks[idx]);
        stepper.StepOver("fold mixed short-circuit guard chain", container);
        container.ReplaceWith(rebuilt);
    }
}
