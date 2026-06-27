using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds an N-way (≥3 arm) conditional-store dispatch into one nested
/// conditional store, before structuring. This is the multi-arm sibling of
/// <see cref="SlotStoreDiamondPass"/> over a local: csc lowers a C#
/// <c>switch (v) { case k0: x = a0; break; … default: x = aN; break; }</c>
/// followed by a live continuation as a <em>dispatch-first</em> shape — a run of
/// guard blocks that each test <c>v</c> and jump to a separated arm block, then
/// the arms (each <c>x = aJ; goto MERGE;</c>), then the merge:
/// <code>
///   D0: if (v == k0) goto A0;     // dispatch chain (each falls into the next)
///   D1: if (v == k1) goto A1;
///   D2: goto A2;                  // default
///   A0: x = a0; goto MERGE;       // arms (separated from their guard)
///   A1: x = a1; goto MERGE;
///   A2: x = a2;                   // last arm falls into the merge
///   MERGE: …continuation…         // a live successor uses x
/// </code>
/// The arms are physically non-contiguous with their guards, so the index-range
/// <see cref="StructuringPass"/> cannot name the merge join and leaves the whole
/// method flat (<c>cond-target-past-region</c>). The equivalent <c>if/else if</c>
/// chain interleaves guard+arm and already structures; only this switch lowering
/// is stranded. Folding the arms into one
/// <c>x = c0 ? a0 : (c1 ? a1 : a2)</c> store eliminates the merge, so structuring
/// (and the lambda/continuation raising it gates) then succeeds.
///
/// <para>Strictly gated to the provably safe shape: every guard tests with a
/// straight-line condition and selects a distinct arm; every arm is exactly one
/// store of the <em>same</em> local followed by a branch to one common merge (the
/// last may fall through); arm values carry no control flow; and no block outside
/// the dispatch reaches an arm or an inner guard. Anything else declines and
/// stays flat — never a guess. Requires ≥2 guards (≥3 arms) so the two-arm
/// <c>if/else</c> store, which already structures cleanly, is left untouched.</para>
/// </summary>
public sealed class ConditionalStoreChainPass : IIrPass
{
    public string Name => "conditional-store-chain";

    /// <summary>Cap on arms folded into one nested conditional: a small value
    /// selection stays readable; a larger table is left flat for switch raising.</summary>
    const int MaxArms = 8;

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            var blocks = container.Blocks;
            var offsetToIndex = new Dictionary<int, int>();
            for (int i = 0; i < blocks.Count; i++)
                offsetToIndex[blocks[i].StartOffset] = i;

            for (int head = 0; head < blocks.Count; head++)
            {
                if (Match(function, blocks, offsetToIndex, head) is { } shape)
                {
                    Fold(container, blocks, shape, stepper);
                    return true;
                }
            }
        }
        return false;
    }

    sealed record Arm(int BlockIndex, IrExpression Value);

    sealed record Shape(
        int HeadIndex,
        IReadOnlyList<int> GuardIndices,
        IReadOnlyList<Arm> Arms,
        int StoreLocalIndex,
        TypeRef StoreType,
        int MergeOffset,
        IReadOnlyList<IrExpression> Conditions,
        IReadOnlyList<int> RemovedIndices);

    static Shape? Match(IrFunction function, IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int head)
    {
        // 1. Walk the dispatch chain: consecutive blocks each ending in a
        //    ConditionalBranch (the head may carry a straight-line prologue), the
        //    chain ending in a block whose last statement is an unconditional
        //    Branch (the default arm). Each guard's fallthrough is the next guard.
        var guardIndices = new List<int>();
        var conditions = new List<IrExpression>();
        var armTargets = new List<int>();
        int terminalIndex = -1;
        int cursor = head;
        while (true)
        {
            if (cursor >= blocks.Count)
                return null;
            var children = blocks[cursor].Children;
            if (children.Count == 0)
                return null;
            var last = children[^1];

            // Only the head may carry leading statements (the switch-value
            // prologue); inner guards must be a pure single conditional so no
            // observable work is hoisted across the short-circuit dispatch order.
            bool pureGuard = children.Count == 1
                || (cursor == head && children.Take(children.Count - 1).All(c => !IsControlFlow(c)));

            if (last is ConditionalBranch conditional)
            {
                if (!pureGuard)
                    return null;
                if (cursor != head && children.Count != 1)
                    return null;
                guardIndices.Add(cursor);
                conditions.Add(conditional.Condition);
                armTargets.Add(conditional.TargetOffset);
                cursor++;
                continue;
            }
            if (last is Branch branch)
            {
                // Only the head may carry a prologue ahead of the terminal branch;
                // an inner default block must be just the branch.
                if (cursor != head && children.Count != 1)
                    return null;
                if (cursor == head && children.Count > 1 && !children.Take(children.Count - 1).All(c => !IsControlFlow(c)))
                    return null;
                armTargets.Add(branch.TargetOffset);  // the default arm
                terminalIndex = cursor;
                break;
            }
            return null;
        }
        if (terminalIndex < 0)
            return null;

        // Need at least two guards (so at least three arms): the two-arm store
        // already structures as if/else and must not be reshaped into a ternary.
        if (guardIndices.Count < 2)
            return null;
        if (armTargets.Count > MaxArms)
            return null;

        // 2. Resolve and validate the arms. Each arm block must be exactly
        //    `StoreLocal target = value; (Branch merge)` — the last arm may fall
        //    through to the merge — with every arm storing the same local and
        //    converging on one merge. Arm targets must be distinct blocks.
        var armIndexSet = new HashSet<int>();
        var arms = new List<Arm>();
        int? storeLocalIndex = null;
        TypeRef? storeType = null;
        int? mergeOffset = null;

        for (int a = 0; a < armTargets.Count; a++)
        {
            if (!offsetToIndex.TryGetValue(armTargets[a], out int armIndex))
                return null;
            if (!armIndexSet.Add(armIndex))
                return null;  // two guards select the same arm — not a clean dispatch
            var children = blocks[armIndex].Children;
            if (children.Count is < 1 or > 2)
                return null;

            IrExpression? exit = null;
            int storeCount = children.Count;
            if (children[^1] is Branch armBranch)
            {
                storeCount--;
                exit = null;
                if (!RegisterMerge(armBranch.TargetOffset, ref mergeOffset))
                    return null;
            }
            else
            {
                // No trailing branch: the arm falls into the next block, which
                // must be the merge. Record the fallthrough successor's offset.
                if (armIndex + 1 >= blocks.Count)
                    return null;
                if (!RegisterMerge(blocks[armIndex + 1].StartOffset, ref mergeOffset))
                    return null;
            }
            _ = exit;

            if (storeCount != 1 || children[0] is not StoreLocal store)
                return null;
            if (ContainsControlFlow(store.Value))
                return null;
            if (storeLocalIndex is { } existing && existing != store.Index)
                return null;
            storeLocalIndex ??= store.Index;
            storeType ??= store.Type;
            arms.Add(new Arm(armIndex, store.Value));
        }

        if (storeLocalIndex is not { } localIndex || storeType is not { } type || mergeOffset is not { } merge)
            return null;
        if (!offsetToIndex.ContainsKey(merge))
            return null;

        // 3. No external entry: every dispatch/arm block other than the head must
        //    be reached only from inside this dispatch (so removing them is safe).
        //    The dispatch blocks are the conditional guards plus the terminal
        //    (default) branch block.
        var dispatchBlocks = new List<int>(guardIndices) { terminalIndex };
        var interior = new HashSet<int>();
        foreach (int d in dispatchBlocks)
            if (d != head)
                interior.Add(d);
        foreach (var arm in arms)
            interior.Add(arm.BlockIndex);
        var interiorOffsets = interior.Select(i => blocks[i].StartOffset).ToHashSet();
        var allowedSources = new HashSet<int>(dispatchBlocks) { head };
        foreach (var (blockIndex, target) in AllBranchTargets(blocks))
        {
            if (interiorOffsets.Contains(target) && !allowedSources.Contains(blockIndex))
                return null;
        }
        // The dispatch blocks must be physically consecutive: each guard falls
        // through to the next dispatch block (guard or terminal), so the chain is
        // a contiguous short-circuit run with no interleaved foreign block.
        for (int g = 0; g < dispatchBlocks.Count - 1; g++)
            if (dispatchBlocks[g] + 1 != dispatchBlocks[g + 1])
                return null;

        var removed = interior.OrderBy(i => i).ToList();
        return new Shape(head, guardIndices, arms, localIndex, type, merge, conditions, removed);
    }

    static bool RegisterMerge(int offset, ref int? mergeOffset)
    {
        if (mergeOffset is { } existing)
            return existing == offset;
        mergeOffset = offset;
        return true;
    }

    static void Fold(BlockContainer container, IReadOnlyList<Block> blocks, Shape shape, Stepper stepper)
    {
        var ordered = container.Blocks.ToList();

        // Build the nested conditional right-to-left: the last arm is the final
        // else, each guard's condition selecting its arm over the rest.
        // Arms[j] aligns with Conditions[j]; the final arm is the default.
        IrExpression value = (IrExpression)shape.Arms[^1].Value.Clone();
        for (int j = shape.Conditions.Count - 1; j >= 0; j--)
        {
            var conditional = new Conditional(
                (IrExpression)shape.Conditions[j].Clone(),
                (IrExpression)shape.Arms[j].Value.Clone(),
                value)
            {
                MergedType = shape.StoreType,
            };
            value = conditional;
        }

        var folded = new Block(ordered[shape.HeadIndex].StartOffset);
        var headChildren = ordered[shape.HeadIndex].DetachChildren();
        // Keep the head's prologue (everything before its terminal branch); the
        // conditions still load whatever the prologue computed.
        for (int k = 0; k < headChildren.Count - 1; k++)
            folded.Add(headChildren[k]);
        folded.Add(new StoreLocal(shape.StoreLocalIndex, shape.StoreType, value));

        var removedSet = shape.RemovedIndices.ToHashSet();
        // The merge is reached by fall-through when it is the next surviving block
        // after the folded head (every dispatch/arm block between them is removed);
        // only emit an explicit branch when something else survives in between.
        int mergeIndex = ordered.FindIndex(b => b.StartOffset == shape.MergeOffset);
        bool mergeFallsThrough = true;
        for (int i = shape.HeadIndex + 1; i < mergeIndex; i++)
        {
            if (!removedSet.Contains(i))
            {
                mergeFallsThrough = false;
                break;
            }
        }
        if (!mergeFallsThrough)
            folded.Add(new Branch(shape.MergeOffset));

        foreach (var block in ordered)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i == shape.HeadIndex)
            {
                rebuilt.Add(folded);
                continue;
            }
            if (removedSet.Contains(i))
                continue;
            rebuilt.Add(ordered[i]);
        }

        stepper.StepOver(
            $"fold {shape.Arms.Count}-way conditional store of local {shape.StoreLocalIndex} into a nested conditional",
            container);
        container.ReplaceWith(rebuilt);
    }

    static IEnumerable<(int BlockIndex, int Target)> AllBranchTargets(IReadOnlyList<Block> blocks)
    {
        for (int i = 0; i < blocks.Count; i++)
            foreach (var node in blocks[i].Descendants)
                switch (node)
                {
                    case Branch branch:
                        yield return (i, branch.TargetOffset);
                        break;
                    case ConditionalBranch conditional:
                        yield return (i, conditional.TargetOffset);
                        break;
                    case SwitchBranch sw:
                        foreach (int target in sw.TargetOffsets)
                            yield return (i, target);
                        break;
                    case Leave leave:
                        yield return (i, leave.TargetOffset);
                        break;
                }
    }

    static bool ContainsControlFlow(IrExpression value)
        => value.Descendants.Prepend(value).Any(IsControlFlow);

    static bool IsControlFlow(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter;
}
