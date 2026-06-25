namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Dissolves a shared return-merge: a short <c>return</c>-only block that two or
/// more blocks reach by an unconditional <c>goto</c>. csc lowers many multi-way
/// selections — a <c>switch</c> comparison tree, but equally a plain forward
/// common-exit (two nested guards both jumping to one shared <c>ldloc; ret</c>) —
/// to arms that each store their result and branch forward to a single return
/// tail, a join the index-range structuring pass cannot nest because the arms
/// branch past their region to reach it. Inlining a copy of the tail into each
/// such predecessor (the shape the prior emitter produced) turns those arms into
/// straight returns, so the guard tree above them nests cleanly and the
/// definite-assignment walk sees no surviving goto.
///
/// The fold is gated structurally rather than by a selection-shape heuristic: a
/// merge qualifies only when it is a short return tail reached by two or more
/// <em>unconditional</em> predecessors. Those two guards together make the tail
/// the immediate post-dominator of its arms, so duplicating it is a pure copy of
/// a <c>return</c> that reorders nothing.
///
/// Conservative by construction:
///   • only short tails (the terminator-inlining budget) are duplicated;
///   • a merge needs two or more <em>unconditional</em> predecessors — a
///     two-way <c>if/else</c> join (one goto + one fallthrough) is left to the
///     structuring pass's diamond form, which already nests it without
///     duplicating the tail;
///   • a conditional <c>if (c) goto tail</c> predecessor is left in place for
///     the structuring pass to raise as a guard.
/// Runs before structuring.
/// </summary>
public sealed class ReturnMergePass : IIrPass
{
    public string Name => "return-merge";

    /// <summary>A tail longer than this is not duplicated (keeps the inlining bounded).</summary>
    const int MaxTailStatements = 3;

    public void Run(IrFunction function, PassContext context)
    {
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
            Fold(container, context, leaveTargets);
    }

    static void Fold(BlockContainer container, PassContext context, HashSet<int> leaveTargets)
    {
        // Folding one merge can turn its own predecessor (a default arm that
        // fell into it) into a fresh return tail, so iterate to a fixpoint.
        bool changed = true;
        while (changed)
        {
            changed = false;
            var blocks = container.Blocks;
            for (int m = 0; m < blocks.Count; m++)
            {
                var merge = blocks[m];
                if (!IsShortReturnTail(merge))
                    continue;

                var branchPreds = new List<Block>();
                bool hasConditionalOrSwitchPred = false;
                foreach (var block in blocks)
                {
                    if (ReferenceEquals(block, merge) || block.Children.Count == 0)
                        continue;
                    switch (block.Children[^1])
                    {
                        case Branch b when b.TargetOffset == merge.StartOffset:
                            branchPreds.Add(block);
                            break;
                        case ConditionalBranch c when c.TargetOffset == merge.StartOffset:
                            hasConditionalOrSwitchPred = true;
                            break;
                        case SwitchBranch s when s.TargetOffsets.Contains(merge.StartOffset):
                            hasConditionalOrSwitchPred = true;
                            break;
                    }
                }

                var fallthroughPred = m > 0 && FallsThrough(blocks[m - 1]) ? blocks[m - 1] : null;
                // A genuine multi-way join, or a single goto to an isolated
                // return tail. The latter is not a two-way diamond (no fallthrough
                // or conditional predecessor reaches the tail), so inlining it
                // cannot split a short-circuit false-exit merge.
                bool isolatedSingleGoto = branchPreds.Count == 1
                    && fallthroughPred is null
                    && !hasConditionalOrSwitchPred;
                if (branchPreds.Count < 2 && !isolatedSingleGoto)
                    continue;

                var tail = merge.Children.ToList();   // snapshot before any mutation

                foreach (var pred in branchPreds)
                {
                    pred.Children[^1].Detach();   // drop the `goto merge`
                    foreach (var statement in tail)
                        pred.Add(statement.Clone());
                }
                if (fallthroughPred is not null)
                    foreach (var statement in tail)
                        fallthroughPred.Add(statement.Clone());

                context.Stepper.StepOver(
                    $"inline return-merge IL_{merge.StartOffset:X4} into {branchPreds.Count} arm(s)", merge);

                // Every unconditional and the fallthrough edge now carries the
                // tail; if no conditional guard or surviving leave still targets
                // the merge, nothing reaches it (and the block before it now
                // terminates), so drop it.
                if (!hasConditionalOrSwitchPred && !leaveTargets.Contains(merge.StartOffset))
                    merge.Detach();

                changed = true;
                break;   // the block list changed — restart the scan
            }
        }
    }

    /// <summary>A short block whose only control flow is a trailing <see cref="Return"/>.</summary>
    static bool IsShortReturnTail(Block block)
    {
        int count = block.Children.Count;
        if (count == 0 || count > MaxTailStatements)
            return false;
        if (block.Children[^1] is not Return)
            return false;
        for (int s = 0; s < count - 1; s++)
            if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return false;
        return true;
    }

    /// <summary>Whether control reaching the end of this block continues into its successor.</summary>
    static bool FallsThrough(Block block) =>
        block.Children.Count == 0
        || block.Children[^1] is not (Return or Throw or Branch or Leave or EndFinally or EndFilter);
}
