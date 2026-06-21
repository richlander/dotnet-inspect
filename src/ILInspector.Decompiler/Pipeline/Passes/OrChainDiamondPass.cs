using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a short-circuit OR chain that selects between two arms of a diamond
/// into one guard, before structuring. csc lowers
/// <c>x = (a || b) ? TRUE : FALSE;</c> (and the <c>?.</c>/<c>??</c> forms that
/// expand to it) to a run of single-condition blocks that all branch to the
/// shared true arm, an unconditional false arm that jumps past it to the merge,
/// then the true arm:
/// <code>
///   if (a) goto T;    // → true arm
///   if (b) goto T;    // → true arm
///   FALSE: …; goto M;  // else arm, jumps past the true arm
///   T: TRUE …          // true arm, falls (or branches) to M
///   M: REST
/// </code>
/// The two conditionals share one target, so the false arm's <c>goto M</c> lands
/// past the true arm — a forward branch the <see cref="StructuringPass"/> diamond
/// model (one conditional per diamond) cannot name, and the whole container stays
/// flat (the <c>forward-branch-not-region-exit</c> stop, issue #911). This pass
/// rebuilds the run of guards into a single <c>ConditionalBranch</c> whose
/// condition is <c>a || b</c> to the true arm, collapsing the chain to the
/// single-conditional diamond the structuring pass already raises (it negates the
/// condition back to <c>if (a || b) { TRUE } else { FALSE }</c>).
///
/// Distinct from <see cref="OrChainGuardPass"/>: there the chain ends in a
/// conditional <em>skip</em> guard and the consequence falls through to the join
/// (no else arm); here the block after the chain is an unconditional <em>false
/// arm</em> that branches past the shared true arm. The two shapes are disjoint
/// in the block right after the guard run (a conditional vs. a goto-terminated
/// arm), so they never contend for the same chain.
///
/// Conservative and sound: only a run of two or more pure single-condition blocks
/// folds (a lone conditional is already the diamond the structuring pass raises),
/// every guard must branch to the same true arm, a non-empty false arm must sit
/// between the chain and that arm and end by branching strictly past it, and
/// nothing outside the run may branch into the inner guard blocks the fold drops.
/// Short-circuit order is preserved exactly: <c>a || b</c> evaluates <c>b</c> only
/// when <c>a</c> was false, just as the fall-through chain did, and re-lowers to
/// the same branch sequence (the fidelity invariant <see cref="OrChainGuardPass"/>
/// also relies on).
/// </summary>
public sealed class OrChainDiamondPass : IIrPass
{
    public string Name => "or-chain-diamond";

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
        // Collect every leave target function-wide and refuse to fold across one.
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
                Fold(container, p, chain.LastGuard, chain.TrueArmOffset, stepper);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The OR-chain diamond rooted at <paramref name="p"/>: guards
    /// [p, LastGuard] are all single-condition blocks (the root may carry leading
    /// setup) branching to the same true arm; a non-empty false arm sits right
    /// after the chain and ends by branching strictly past the true arm. Null when
    /// block p does not start such a chain.
    /// </summary>
    static (int LastGuard, int TrueArmOffset)? Chain(
        IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int p)
    {
        if (ConditionTargetAtEnd(blocks[p]) is not { } trueArmOffset
            || !offsetToIndex.TryGetValue(trueArmOffset, out int trueArm))
        {
            return null;
        }

        // Extend the run while each following block is a pure single-condition
        // guard branching to the same true arm.
        int lastGuard = p;
        while (lastGuard + 1 < blocks.Count
            && ConditionTarget(blocks[lastGuard + 1]) == trueArmOffset)
        {
            lastGuard++;
        }

        // A real chain needs two or more guards: a lone conditional is already the
        // single-target diamond the structuring pass raises, so folding it would
        // be a no-op reshape.
        if (lastGuard == p)
            return null;

        // The false arm occupies [lastGuard+1, trueArm): at least one block, and
        // the block right before the true arm ends with an unconditional branch
        // strictly past it (the merge). The intervening arm shape is the
        // structuring pass's to validate.
        int falseStart = lastGuard + 1;
        if (falseStart >= trueArm)
            return null;
        if (UnconditionalBranchTarget(blocks[trueArm - 1]) is not { } mergeOffset
            || !offsetToIndex.TryGetValue(mergeOffset, out int mergeIndex)
            || mergeIndex <= trueArm)
        {
            return null;
        }
        return (lastGuard, trueArmOffset);
    }

    /// <summary>The single branch target of a pure one-statement condition block, or null.</summary>
    static int? ConditionTarget(Block block)
        => block.Children is [ConditionalBranch conditional] ? conditional.TargetOffset : null;

    /// <summary>
    /// The branch target of a block whose last statement is a conditional branch
    /// and whose earlier statements are all straight-line (no control flow), or
    /// null. Only the chain root may carry such setup — the unconditional prologue
    /// that computes the first guard's operands; inner guards must be pure, since
    /// their leading statements would move out of short-circuit order if folded.
    /// </summary>
    static int? ConditionTargetAtEnd(Block block)
    {
        if (block.Children.Count == 0 || block.Children[^1] is not ConditionalBranch conditional)
            return null;
        for (int s = 0; s < block.Children.Count - 1; s++)
        {
            if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return null;
        }
        return conditional.TargetOffset;
    }

    /// <summary>
    /// The target of a block that ends with an unconditional <see cref="Branch"/>
    /// and is otherwise straight-line (no control flow among its earlier
    /// statements), or null. The false arm's terminating goto.
    /// </summary>
    static int? UnconditionalBranchTarget(Block block)
    {
        if (block.Children.Count == 0 || block.Children[^1] is not Branch branch)
            return null;
        for (int s = 0; s < block.Children.Count - 1; s++)
        {
            if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return null;
        }
        return branch.TargetOffset;
    }

    /// <summary>
    /// No block outside the chain may branch into the inner guards [p+1, LastGuard]
    /// the fold drops — those edges would dangle at a vanished offset. The chain
    /// entry (p) and every block from the false arm onward stay in place, so edges
    /// to them are fine; a leave into a dropped guard aborts the fold too.
    /// </summary>
    static bool NoExternalEntry(
        IReadOnlyList<Block> blocks, int p, int lastGuard, HashSet<int> leaveTargets)
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

    static void Fold(BlockContainer container, int p, int lastGuard, int trueArmOffset, Stepper stepper)
    {
        var blocks = container.Blocks.ToList();

        // Build the OR condition in evaluation order: each guard contributes its
        // own taken-path condition (the test that branches to the true arm). The
        // guard conditional is always the block's last statement; only the root
        // may carry leading setup ahead of it.
        IrExpression? condition = null;
        for (int j = p; j <= lastGuard; j++)
        {
            var conditional = (ConditionalBranch)blocks[j].Children[^1];
            var guard = (IrExpression)conditional.DetachChildren()[0];
            condition = condition is null
                ? guard
                : new LogicalBinary(LogicalKind.Or, condition, guard);
        }

        // The folded guard keeps the root block's leading statements (the
        // unconditional prologue) and branches to the true arm when the OR holds;
        // the structuring pass negates it back to `if (a || b) { TRUE } else { … }`.
        var folded = new Block(blocks[p].StartOffset);
        var rootChildren = blocks[p].DetachChildren();
        for (int k = 0; k < rootChildren.Count - 1; k++)
            folded.Add(rootChildren[k]);
        folded.Add(new ConditionalBranch(condition!, trueArmOffset));

        foreach (var block in blocks)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int idx = 0; idx < p; idx++)
            rebuilt.Add(blocks[idx]);
        rebuilt.Add(folded);
        for (int idx = lastGuard + 1; idx < blocks.Count; idx++)
            rebuilt.Add(blocks[idx]);
        stepper.StepOver("fold short-circuit OR chain diamond", container);
        container.ReplaceWith(rebuilt);
    }
}
