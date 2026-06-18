namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Dissolves a shared return-merge: a short <c>return</c>-only block that two or
/// more blocks reach by an unconditional <c>goto</c>. csc lowers a <c>switch</c>
/// (and other multi-way selection) to a comparison tree whose every arm stores
/// its result and jumps forward to one <c>ldloc; ret</c> tail — a join the
/// structuring pass cannot nest, because the arms branch past their region to
/// reach it. Inlining a copy of the tail into each such predecessor (the shape
/// the prior emitter produced) turns those arms into straight returns, so the
/// guard tree above them nests cleanly and the definite-assignment walk sees no
/// surviving goto.
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
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
            Fold(container, context);
    }

    static void Fold(BlockContainer container, PassContext context)
    {
        // Only dissolve merges inside a genuine comparison tree; a two- or
        // three-way selection's shared return reads better kept (the folding and
        // boolean passes raise it as a ternary).
        if (!ComparisonTrees.IsLikely(container))
            return;

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

                // A genuine multi-way join, not a two-way diamond.
                if (branchPreds.Count < 2)
                    continue;

                var fallthroughPred = m > 0 && FallsThrough(blocks[m - 1]) ? blocks[m - 1] : null;
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
                // tail; if no conditional guard still targets the merge, nothing
                // reaches it (and the block before it now terminates), so drop it.
                if (!hasConditionalOrSwitchPred)
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
