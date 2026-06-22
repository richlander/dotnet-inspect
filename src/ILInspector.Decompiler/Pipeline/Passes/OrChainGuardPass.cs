using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a short-circuit OR guard chain into one guard, before structuring.
/// csc lowers <c>if (a || b || !c) { CONSEQUENCE } REST</c> to a run of
/// single-condition blocks where every guard but the last branches to the
/// consequence and the last branches PAST it:
/// <code>
///   if (a)  goto T;   // → consequence
///   if (b)  goto T;   // → consequence
///   if (c)  goto J;   // → skip consequence
///   T: CONSEQUENCE    // falls through (or returns/throws) to J
///   J: REST
/// </code>
/// The mixed polarity (some branches to T, one past it to J) breaks the
/// structuring pass's single-target guard/diamond shapes — it rejects the
/// jump-past at <c>target &gt; stop</c> and the whole method stays flat. This
/// pass rebuilds the chain into a single <c>ConditionalBranch</c> whose
/// condition is <c>!(a || b || !c)</c> (the skip condition) to J, leaving the
/// consequence as the guard arm. The structuring pass then negates it back to
/// the clean <c>if (a || b || !c) { CONSEQUENCE }</c> form.
///
/// Conservative and sound: only runs of pure single-condition blocks fold,
/// the consequence must sit immediately after the chain, and nothing outside
/// the chain may branch into the blocks it removes — otherwise it is left
/// flat. Short-circuit evaluation order is preserved exactly: a guard's
/// condition is still evaluated only when every earlier one fell through, just
/// as <c>||</c> short-circuits.
/// </summary>
public sealed class OrChainGuardPass : IIrPass
{
    public string Name => "or-chain-guard";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        // A surviving leave (an early exit out of an EH region) may target a
        // block this fold would drop or re-parent — including from another
        // container, which the per-container scan below cannot see. Collecting
        // every leave target function-wide and refusing to fold across one
        // keeps such a goto from dangling at a vanished offset.
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
                && NoExternalEntry(blocks, p, chain.SkipGuard, chain.JoinIndex, leaveTargets))
            {
                Fold(container, p, chain.SkipGuard, chain.JoinOffset, stepper);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The chain rooted at <paramref name="p"/>: guards [p, SkipGuard] are all
    /// pure single-condition blocks; [p, SkipGuard-1] branch to the consequence
    /// (the block right after the chain) and SkipGuard branches past it to a
    /// later block (the join). Null when block p does not start such a chain.
    /// </summary>
    static (int SkipGuard, int JoinIndex, int JoinOffset)? Chain(
        IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int p)
    {
        if (ConditionTargetAtEnd(blocks[p]) is not { } firstTarget
            || !offsetToIndex.TryGetValue(firstTarget, out int consequence))
        {
            return null;
        }

        // The throw-guards [p, m] all branch to the same consequence; the skip
        // guard is the first one in the run that branches elsewhere.
        int m = p;
        while (m + 1 < blocks.Count
            && ConditionTarget(blocks[m + 1]) == firstTarget)
        {
            m++;
        }

        int skipGuard = m + 1;
        if (skipGuard >= blocks.Count                       // no skip guard follows the run
            || ConditionTarget(blocks[skipGuard]) is not { } joinOffset
            || !offsetToIndex.TryGetValue(joinOffset, out int joinIndex))
        {
            return null;
        }

        // The consequence must sit immediately after the whole chain, and the
        // skip must land strictly past it (so the consequence is the arm).
        int consequenceStart = skipGuard + 1;
        if (consequence != consequenceStart || joinIndex <= consequenceStart)
            return null;
        return (skipGuard, joinIndex, joinOffset);
    }

    /// <summary>The single branch target of a pure one-statement condition block, or null.</summary>
    static int? ConditionTarget(Block block)
        => block.Children is [ConditionalBranch conditional] ? conditional.TargetOffset : null;

    /// <summary>
    /// The branch target of a block whose last statement is a conditional branch
    /// and whose earlier statements are all straight-line (no control flow), or
    /// null. The chain root may carry such setup — the method's unconditional
    /// prologue that computes the guard operands — ahead of its branch; only the
    /// root qualifies, since inner guards run conditionally (their leading
    /// statements would move out of short-circuit order if folded).
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
    /// No block outside the folded chain may branch into guards this fold removes
    /// or into the consequence arm the structurer may consume next. Sibling arms
    /// between a one-block consequence and the join remain real blocks with real
    /// labels, so they do not need to reject external entries. The chain entry
    /// (p) and the join may be targeted freely; a leave from anywhere (collected
    /// function-wide) into a protected block aborts the fold too.
    /// </summary>
    static bool NoExternalEntry(
        IReadOnlyList<Block> blocks, int p, int skipGuard, int joinIndex, HashSet<int> leaveTargets)
    {
        var forbidden = new HashSet<int>();
        var internalIndices = new HashSet<int>();
        for (int idx = p; idx <= skipGuard; idx++)
        {
            internalIndices.Add(idx);
            if (idx > p)
                forbidden.Add(blocks[idx].StartOffset);
        }

        int consequenceStart = skipGuard + 1;
        if (IsSingleBlockConsequence(blocks[consequenceStart], blocks[joinIndex].StartOffset))
        {
            forbidden.Add(blocks[consequenceStart].StartOffset);
            internalIndices.Add(consequenceStart);
        }
        else
        {
            for (int idx = consequenceStart; idx < joinIndex; idx++)
            {
                forbidden.Add(blocks[idx].StartOffset);
                internalIndices.Add(idx);
            }
        }

        if (forbidden.Overlaps(leaveTargets))
            return false;

        for (int idx = 0; idx < blocks.Count; idx++)
        {
            if (internalIndices.Contains(idx))
                continue;   // inside the protected chain/consequence blocks — internal edges are fine
            foreach (var node in blocks[idx].Children)
                foreach (int target in Targets(node))
                    if (forbidden.Contains(target))
                        return false;
        }
        return true;
    }

    static bool IsSingleBlockConsequence(Block block, int joinOffset)
    {
        if (block.Children.Count == 0)
            return false;
        for (int i = 0; i < block.Children.Count - 1; i++)
        {
            if (IsControlFlow(block.Children[i]))
                return false;
        }
        return block.Children[^1] switch
        {
            Branch branch => branch.TargetOffset == joinOffset,
            Return or Throw => true,
            _ => false,
        };
    }

    static bool IsControlFlow(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter;

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch sw => sw.TargetOffsets,
        _ => [],
    };

    static void Fold(BlockContainer container, int p, int skipGuard, int joinOffset, Stepper stepper)
    {
        var blocks = container.Blocks.ToList();

        // Build the throw condition in evaluation order: each guard before the
        // skip contributes its condition; the skip guard contributes the
        // negation of its own (its taken path skips the consequence). The guard
        // conditional is always the block's last statement — the root may carry
        // leading setup statements ahead of it.
        IrExpression? throwCondition = null;
        for (int j = p; j <= skipGuard; j++)
        {
            var conditional = (ConditionalBranch)blocks[j].Children[^1];
            var condition = (IrExpression)conditional.DetachChildren()[0];
            if (j == skipGuard)
                condition = Conditions.Negate(condition);
            throwCondition = throwCondition is null
                ? condition
                : new LogicalBinary(LogicalKind.Or, throwCondition, condition);
        }

        // The folded guard keeps the root block's leading statements (the
        // unconditional prologue, everything before its trailing conditional)
        // and branches to the join when the throw condition is false; the
        // structuring pass negates !throwCondition back to the clean
        // `if (throwCondition) { consequence }`.
        var folded = new Block(blocks[p].StartOffset);
        var rootChildren = blocks[p].DetachChildren();
        for (int k = 0; k < rootChildren.Count - 1; k++)
            folded.Add(rootChildren[k]);
        folded.Add(new ConditionalBranch(new LogicalNot(throwCondition!), joinOffset));

        foreach (var block in blocks)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int idx = 0; idx < p; idx++)
            rebuilt.Add(blocks[idx]);
        rebuilt.Add(folded);
        for (int idx = skipGuard + 1; idx < blocks.Count; idx++)
            rebuilt.Add(blocks[idx]);
        stepper.StepOver("fold short-circuit OR guard chain", container);
        container.ReplaceWith(rebuilt);
    }
}
