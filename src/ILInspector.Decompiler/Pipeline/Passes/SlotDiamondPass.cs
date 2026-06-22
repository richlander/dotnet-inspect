using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a flat slot diamond whose two arms return the slot into a single
/// conditional return, before structuring. csc lowers a bool-producing
/// conjunctive/disjunctive pattern or ternary — <c>y is &gt;= 64 and &lt;= 127</c>,
/// <c>c ? a : b</c> — to a diamond that stores each arm's value into one stack
/// slot and falls into a shared <c>return</c> of that slot:
/// <code>
///   if (c) goto F;       // condition
///   A: S = vFalse; goto J;  // false arm (fallthrough), branches past the true arm
///   F: S = vTrue;           // true arm, falls into the merge
///   J: return S;            // the merge returns the slot
/// </code>
/// This is the flat, pre-structuring analog of
/// <see cref="BooleanFoldingPass"/>'s <c>FoldSlotDiamond</c> (which matches the
/// already-structured <c>if (c) { S = A } else { S = B }</c> tree). It matters
/// when the diamond is a leaf of a container the structurer cannot otherwise raise
/// — most visibly a csc range/equality dispatch (a <c>switch</c> over clustered
/// cases) whose arms compute a bool: the dispatch only raises once every arm is a
/// straight-line terminator, and a diamond arm is not one until it is folded
/// (issue #912). Collapsing it to <c>return c ? vTrue : vFalse</c> makes the arm a
/// terminator the structuring pass inlines, exactly as a plain <c>return</c> leaf
/// already is.
///
/// <para>The folded node is the same <see cref="Conditional"/> the structured
/// <c>FoldSlotDiamond</c>/inlining path converges on (<c>return c ? a : b</c>), so
/// the recompiled IL is unchanged for a diamond that already raised on its own;
/// this only adds the cases where the surrounding container kept it flat.</para>
///
/// <para>Conservative and sound: the two arm blocks and the return block are
/// dropped, so each must be reached only through this diamond — no block outside
/// the run (and no surviving <see cref="Leave"/>) may branch into them. Both arms
/// must store the <em>same</em> slot, and the merge must immediately return that
/// slot, so the stored value is consumed only there. Short-circuit/evaluation
/// order is preserved: the condition keeps its place and each arm value keeps its
/// own block until the fold.</para>
/// </summary>
public sealed class SlotDiamondPass : IIrPass
{
    public string Name => "slot-diamond";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        // A surviving leave may target a block this fold drops — including from
        // another container the per-container scan cannot see. Collect every leave
        // target function-wide and refuse to fold across one.
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

        for (int p = 0; p + 3 < blocks.Count; p++)
        {
            if (Match(blocks, offsetToIndex, p) is { } match
                && NoExternalEntry(blocks, p, leaveTargets))
            {
                Fold(container, p, match, stepper);
                return true;
            }
        }
        return false;
    }

    readonly record struct Diamond(IrExpression Condition, IrExpression WhenTrue, IrExpression WhenFalse, TypeRef? SlotType);

    /// <summary>
    /// The returned slot diamond rooted at <paramref name="p"/>:
    /// <c>p</c> ends in a conditional to the true arm <c>F = p+2</c>; the false arm
    /// <c>A = p+1</c> stores the slot and branches to the merge <c>J = p+3</c>; the
    /// true arm <c>F</c> stores the same slot and falls (or branches) into <c>J</c>;
    /// and <c>J</c> immediately returns the slot. Null when block <paramref name="p"/>
    /// is not such a root.
    /// </summary>
    static Diamond? Match(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int p)
    {
        var head = blocks[p];
        if (head.Children.Count == 0 || head.Children[^1] is not ConditionalBranch branch)
            return null;
        for (int s = 0; s < head.Children.Count - 1; s++)
            if (IsControlFlow(head.Children[s]))
                return null;   // the root may carry straight-line setup, no control flow
        if (!offsetToIndex.TryGetValue(branch.TargetOffset, out int trueArm) || trueArm != p + 2)
            return null;

        int falseArm = p + 1, merge = p + 3;

        // False arm: stores the slot, then branches to the merge past the true arm.
        if (blocks[falseArm].Children is not [StoreStackSlot falseStore, Branch falseExit]
            || !offsetToIndex.TryGetValue(falseExit.TargetOffset, out int falseExitIndex)
            || falseExitIndex != merge)
        {
            return null;
        }

        // True arm: stores the same slot, then reaches the merge by fallthrough or
        // an explicit branch.
        var trueChildren = blocks[trueArm].Children;
        if (trueChildren.Count == 0 || trueChildren[0] is not StoreStackSlot trueStore)
            return null;
        if (trueChildren.Count == 1)
        {
            // Fallthrough into the merge (true arm is the block before it).
        }
        else if (trueChildren.Count == 2 && trueChildren[1] is Branch trueExit
            && offsetToIndex.TryGetValue(trueExit.TargetOffset, out int trueExitIndex) && trueExitIndex == merge)
        {
            // Explicit branch into the merge.
        }
        else
        {
            return null;
        }
        if (trueStore.Slot != falseStore.Slot)
            return null;

        // Merge: returns the slot's value, so the stored value is consumed only
        // here. Two shapes — a direct `return S`, or csc's spill through the
        // method's return local (`L = S; return L`), which expression-inlining
        // would collapse but for it running after structuring.
        LoadStackSlot? load = blocks[merge].Children switch
        {
            [Return { Value: LoadStackSlot direct }] => direct,
            [StoreLocal { Value: LoadStackSlot spilled } store, Return { Value: LoadLocal returned }]
                when store.Index == returned.Index => spilled,
            _ => null,
        };
        if (load is null || load.Slot != falseStore.Slot)
            return null;

        // `if (c) goto trueArm` takes the true arm when c holds, so the true arm's
        // value is the when-true result. A LogicalNot condition reads backwards;
        // unwrap and swap, mirroring BooleanFoldingPass.FoldSlotDiamond.
        var condition = branch.Condition;
        var whenTrue = trueStore.Value;
        var whenFalse = falseStore.Value;
        if (condition is LogicalNot negated)
        {
            condition = negated.Operand;
            (whenTrue, whenFalse) = (whenFalse, whenTrue);
        }
        return new Diamond(condition, whenTrue, whenFalse, load.Type);
    }

    static bool IsControlFlow(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter;

    /// <summary>
    /// No block outside the diamond may branch into the false arm, true arm, or
    /// merge (<c>p+1</c>, <c>p+2</c>, <c>p+3</c>) the fold drops — those edges would
    /// dangle at a vanished offset. The root <c>p</c> stays in place, so edges to it
    /// (the dispatch leaf that reaches the diamond) are fine.
    /// </summary>
    static bool NoExternalEntry(IReadOnlyList<Block> blocks, int p, HashSet<int> leaveTargets)
    {
        var forbidden = new HashSet<int> { blocks[p + 1].StartOffset, blocks[p + 2].StartOffset, blocks[p + 3].StartOffset };
        if (forbidden.Overlaps(leaveTargets))
            return false;
        for (int idx = 0; idx < blocks.Count; idx++)
        {
            if (idx >= p && idx <= p + 3)
                continue;   // the diamond itself: the root's branch to the true arm
                            // and the false arm's branch to the merge are its own edges
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
        Leave leave => [leave.TargetOffset],
        _ => [],
    };

    static void Fold(BlockContainer container, int p, Diamond match, Stepper stepper)
    {
        var blocks = container.Blocks.ToList();

        var condition = match.Condition;
        var whenTrue = match.WhenTrue;
        var whenFalse = match.WhenFalse;
        condition.Detach();
        whenTrue.Detach();
        whenFalse.Detach();
        var conditional = new Conditional(condition, whenTrue, whenFalse) { MergedType = match.SlotType };

        // The folded block keeps the root's straight-line setup and returns the
        // conditional in place of the dispatched-to diamond.
        var folded = new Block(blocks[p].StartOffset);
        var rootChildren = blocks[p].DetachChildren();
        for (int k = 0; k < rootChildren.Count - 1; k++)
            folded.Add(rootChildren[k]);
        folded.Add(new Return(conditional));

        foreach (var block in blocks)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int idx = 0; idx < p; idx++)
            rebuilt.Add(blocks[idx]);
        rebuilt.Add(folded);
        for (int idx = p + 4; idx < blocks.Count; idx++)
            rebuilt.Add(blocks[idx]);
        stepper.StepOver("fold returned slot diamond into conditional return", container);
        container.ReplaceWith(rebuilt);
    }
}
