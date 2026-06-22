using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds comparison-tree bool arms that branch to a shared constant-return tail.
/// csc lowers clustered/range switch arms such as <c>y is &gt;= 64 and &lt;= 127</c>
/// to one or more guard blocks whose taken edge stores <c>false</c> in a shared
/// return tail and whose fallthrough stores <c>true</c> locally before returning:
/// <code>
///   if (y &lt; 64) goto F;
///   if (y &gt; 127) goto F;
///   V = true; return V;
/// F:
///   V = false; return V;
/// </code>
/// That arm is not a straight-line terminator, so the surrounding sparse-switch
/// comparison tree cannot inline it as a leaf. Gated to genuine comparison-tree
/// containers, this pass folds the arm to <c>return y &gt;= 64 &amp;&amp; y &lt;= 127;</c>
/// before structuring. It deliberately does not duplicate generic shared return
/// tails, preserving ordinary short-circuit guard chains.
/// </summary>
public sealed class ComparisonTreeBoolArmPass : IIrPass
{
    public string Name => "comparison-tree-bool-arm";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (ComparisonTrees.IsLikely(container)
                && TryFold(container, leaveTargets, stepper))
            {
                return true;
            }
        }
        return false;
    }

    enum ReturnPlaceKind { Direct, Local, StackSlot }

    readonly record struct ReturnPlace(ReturnPlaceKind Kind, int Index);
    readonly record struct BoolReturn(bool Value, ReturnPlace Place);
    sealed record Match(int SuccessIndex, int TailIndex, int GuardCount, bool TailValue);

    static bool TryFold(BlockContainer container, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        for (int p = 0; p < blocks.Count; p++)
        {
            if (TryMatch(blocks, offsetToIndex, p, out var match)
                && NoExternalEntry(blocks, p, match.SuccessIndex, leaveTargets))
            {
                Fold(container, p, match, stepper);
                return true;
            }
        }
        return false;
    }

    static bool TryMatch(
        IReadOnlyList<Block> blocks,
        Dictionary<int, int> offsetToIndex,
        int p,
        out Match match)
    {
        match = null!;

        if (GuardTargetAtEnd(blocks[p], allowPrefix: true) is not { } tailOffset
            || !offsetToIndex.TryGetValue(tailOffset, out int tailIndex)
            || tailIndex <= p + 1
            || !TryConstantBoolReturn(blocks[tailIndex], out var tailReturn))
        {
            return false;
        }

        int i = p;
        while (i < tailIndex
            && GuardTargetAtEnd(blocks[i], allowPrefix: i == p) == tailOffset)
        {
            i++;
        }

        int guardCount = i - p;
        int successIndex = i;
        if (guardCount == 0
            || successIndex >= tailIndex
            || !TryConstantBoolReturn(blocks[successIndex], out var successReturn)
            || !tailReturn.Place.Equals(successReturn.Place)
            || tailReturn.Value == successReturn.Value)
        {
            return false;
        }

        match = new Match(successIndex, tailIndex, guardCount, tailReturn.Value);
        return true;
    }

    /// <summary>
    /// The target of a block whose last statement is a conditional branch and
    /// whose earlier statements are straight-line. Only the arm root may carry a
    /// prefix; inner guards must be pure so their setup is not hoisted out of
    /// short-circuit order.
    /// </summary>
    static int? GuardTargetAtEnd(Block block, bool allowPrefix)
    {
        if (block.Children.Count == 0 || block.Children[^1] is not ConditionalBranch conditional)
            return null;
        if (!allowPrefix && block.Children.Count != 1)
            return null;
        for (int s = 0; s < block.Children.Count - 1; s++)
        {
            if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return null;
        }
        return conditional.TargetOffset;
    }

    static bool TryConstantBoolReturn(Block block, out BoolReturn result)
    {
        result = default;
        switch (block.Children)
        {
            case [Return { Value: Constant { Value: bool value, Type: var type } }]:
                if (!IsBool(type))
                    return false;
                result = new BoolReturn(value, new ReturnPlace(ReturnPlaceKind.Direct, -1));
                return true;

            case [StoreLocal { Index: var index, Type: var type, Value: Constant { Value: bool value } },
                Return { Value: LoadLocal returned }]
                when returned.Index == index && IsBool(type) && IsBool(returned.Type):
                result = new BoolReturn(value, new ReturnPlace(ReturnPlaceKind.Local, index));
                return true;

            case [StoreStackSlot { Slot: var slot, Value: Constant { Value: bool value } },
                Return { Value: LoadStackSlot returned }]
                when returned.Slot == slot && IsBool(returned.Type):
                result = new BoolReturn(value, new ReturnPlace(ReturnPlaceKind.StackSlot, slot));
                return true;

            default:
                return false;
        }
    }

    static bool IsBool(TypeRef? type)
        => type is { Namespace: "System", Name: "Boolean" };

    static bool NoExternalEntry(IReadOnlyList<Block> blocks, int p, int successIndex, HashSet<int> leaveTargets)
    {
        var forbidden = new HashSet<int>();
        for (int idx = p + 1; idx <= successIndex; idx++)
            forbidden.Add(blocks[idx].StartOffset);

        if (forbidden.Overlaps(leaveTargets))
            return false;

        for (int idx = 0; idx < blocks.Count; idx++)
        {
            if (idx >= p && idx <= successIndex)
                continue;
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

    static void Fold(BlockContainer container, int p, Match match, Stepper stepper)
    {
        var blocks = container.Blocks.ToList();

        var conditions = new List<IrExpression>();
        for (int idx = p; idx < p + match.GuardCount; idx++)
        {
            var conditional = (ConditionalBranch)blocks[idx].Children[^1];
            conditions.Add((IrExpression)conditional.DetachChildren()[0]);
        }

        var result = BuildResult(conditions, match.TailValue);

        var folded = new Block(blocks[p].StartOffset);
        var rootChildren = blocks[p].DetachChildren();
        for (int k = 0; k < rootChildren.Count - 1; k++)
            folded.Add(rootChildren[k]);
        folded.Add(new Return(result));

        var kept = new List<Block>();
        for (int idx = 0; idx < p; idx++)
            kept.Add(blocks[idx]);
        kept.Add(folded);
        for (int idx = p + 1; idx < blocks.Count; idx++)
            if (idx > match.SuccessIndex)
                kept.Add(blocks[idx]);

        var tail = blocks[match.TailIndex];
        if (kept.Contains(tail) && !HasIncoming(kept, tail.StartOffset))
            kept.Remove(tail);

        foreach (var block in blocks)
            block.Detach();

        var rebuilt = new BlockContainer();
        foreach (var block in kept)
            rebuilt.Add(block);
        stepper.StepOver("fold comparison-tree bool guard arm", container);
        container.ReplaceWith(rebuilt);
    }

    static IrExpression BuildResult(IReadOnlyList<IrExpression> conditions, bool tailValue)
    {
        IrExpression? result = null;
        foreach (var condition in conditions)
        {
            var term = tailValue ? condition : Conditions.Negate(condition);
            result = result is null
                ? term
                : new LogicalBinary(tailValue ? LogicalKind.Or : LogicalKind.And, result, term);
        }
        return result!;
    }

    static bool HasIncoming(IReadOnlyList<Block> blocks, int targetOffset)
    {
        for (int idx = 0; idx < blocks.Count; idx++)
        {
            foreach (var node in blocks[idx].Children)
                foreach (int target in Targets(node))
                    if (target == targetOffset)
                        return true;
            if (idx + 1 < blocks.Count
                && blocks[idx + 1].StartOffset == targetOffset
                && FallsThrough(blocks[idx]))
            {
                return true;
            }
        }
        return false;
    }

    static bool FallsThrough(Block block)
        => block.Children.Count == 0
        || block.Children[^1] is not (Return or Throw or Branch or Leave or EndFinally or EndFilter);
}
