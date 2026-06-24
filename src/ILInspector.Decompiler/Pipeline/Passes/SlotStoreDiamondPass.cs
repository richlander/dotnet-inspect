using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a flat stack-slot diamond into one conditional store before structuring.
/// This is the non-returning sibling of <see cref="SlotDiamondPass"/>: csc often
/// lowers <c>S = c ? T : F</c> as a shared-forward merge, then uses <c>S</c> in a
/// later condition or assignment. The structurer cannot name the shared merge
/// while it is still split across gotos, but a single <see cref="Conditional"/>
/// store preserves the evaluation order and lets the surrounding container raise.
/// </summary>
public sealed class SlotStoreDiamondPass : IIrPass
{
    public string Name => "slot-store-diamond";

    public void Run(IrFunction function, PassContext context)
    {
        if (function.Descendants.OfType<EndFilter>().Any())
            return;

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
            var blocks = container.Blocks;
            var offsetToIndex = new Dictionary<int, int>();
            for (int i = 0; i < blocks.Count; i++)
                offsetToIndex[blocks[i].StartOffset] = i;

            for (int p = 0; p + 2 < blocks.Count; p++)
            {
                if (Match(function, blocks, offsetToIndex, leaveTargets, p) is { } shape)
                {
                    Fold(container, blocks, p, shape, stepper);
                    return true;
                }
            }
        }
        return false;
    }

    sealed record Shape(
        int FalseIndex,
        int TrueStart,
        int TrueEnd,
        int MergeIndex,
        int ResultSlot,
        TypeRef? ResultType,
        IrExpression Condition,
        IrExpression WhenTrue,
        IrExpression WhenFalse);

    static Shape? Match(
        IrFunction function,
        IReadOnlyList<Block> blocks,
        Dictionary<int, int> offsetToIndex,
        HashSet<int> leaveTargets,
        int p)
    {
        var head = blocks[p];
        if (head.Children.Count == 0 || head.Children[^1] is not ConditionalBranch branch)
            return null;
        for (int s = 0; s < head.Children.Count - 1; s++)
            if (IsControlFlow(head.Children[s]))
                return null;

        int falseIndex = p + 1;
        if (blocks[falseIndex].Children is not [StoreStackSlot falseStore, Branch falseExit]
            || !offsetToIndex.TryGetValue(falseExit.TargetOffset, out int mergeIndex)
            || !offsetToIndex.TryGetValue(branch.TargetOffset, out int trueStart)
            || trueStart <= falseIndex
            || trueStart >= mergeIndex)
        {
            return null;
        }

        if (FindTrueRegion(blocks, trueStart, mergeIndex) is not { } trueRegion)
            return null;
        if (trueRegion.Store.Slot != falseStore.Slot)
            return null;

        var prefixStores = trueRegion.Prefix.OfType<StoreStackSlot>().ToList();
        if (prefixStores.Count != trueRegion.Prefix.Count
            || !PrefixSlotsDeadOutside(function, blocks, trueStart, trueRegion.EndIndex, prefixStores.Select(store => store.Slot).ToHashSet()))
        {
            return null;
        }

        var whenTrue = (IrExpression)trueRegion.Store.Value.Clone();
        if (!SubstitutePrefixStores(whenTrue, prefixStores, trueRegion.Store.Slot, out whenTrue))
            return null;
        var whenFalse = (IrExpression)falseStore.Value.Clone();
        var condition = (IrExpression)branch.Condition.Clone();
        if (condition is LogicalNot negated)
        {
            condition = (IrExpression)negated.Operand.Clone();
            (whenTrue, whenFalse) = (whenFalse, whenTrue);
        }
        if (!NormalizeArmTypes(ref whenTrue, ref whenFalse))
            return null;
        var resultType = whenTrue.ResultType ?? whenFalse.ResultType;
        if (HasNullArmForNonNullableValue(function, whenTrue, whenFalse, resultType))
            return null;

        if (!NoExternalEntry(blocks, p, falseIndex, trueStart, trueRegion.EndIndex, leaveTargets))
            return null;

        return new Shape(
            falseIndex,
            trueStart,
            trueRegion.EndIndex,
            mergeIndex,
            falseStore.Slot,
            resultType,
            condition,
            whenTrue,
            whenFalse);
    }

    static bool HasNullArmForNonNullableValue(IrFunction function, IrExpression whenTrue, IrExpression whenFalse, TypeRef? resultType)
        => (whenTrue is Constant { Value: null } || whenFalse is Constant { Value: null })
            && TypeFamilies.IsKnownNonNullableValueType(resultType, function.TypeShapes);

    static bool NormalizeArmTypes(ref IrExpression whenTrue, ref IrExpression whenFalse)
    {
        var trueType = whenTrue.ResultType;
        var falseType = whenFalse.ResultType;
        if (trueType is null
            || falseType is null
            || trueType.Equals(falseType)
            || whenTrue is Constant { Value: null }
            || whenFalse is Constant { Value: null })
        {
            return true;
        }

        if (IsBool(trueType) && TryBoolConstant(whenFalse, out var falseBool))
        {
            whenFalse = falseBool;
            return true;
        }
        if (IsBool(falseType) && TryBoolConstant(whenTrue, out var trueBool))
        {
            whenTrue = trueBool;
            return true;
        }
        return false;
    }

    static bool IsBool(TypeRef type) => type.Equals(TypeRef.CoreLib("System", "Boolean"));

    static bool TryBoolConstant(IrExpression expression, out IrExpression normalized)
    {
        if (expression is Constant { Value: int value } constant && (value == 0 || value == 1))
        {
            normalized = new Constant(value != 0, TypeRef.CoreLib("System", "Boolean"));
            normalized.InheritSourceOffset(constant);
            return true;
        }
        normalized = expression;
        return false;
    }

    sealed record TrueRegionShape(int EndIndex, IReadOnlyList<IrNode> Prefix, StoreStackSlot Store);

    static TrueRegionShape? FindTrueRegion(IReadOnlyList<Block> blocks, int trueStart, int mergeIndex)
    {
        var statements = new List<IrNode>();
        for (int i = trueStart; i < blocks.Count && i <= mergeIndex; i++)
        {
            if (i == mergeIndex)
                return null;
            var children = blocks[i].Children;
            if (children.Count == 0)
                return null;

            bool exitsToMerge = false;
            int statementCount = children.Count;
            if (children[^1] is Branch branch)
            {
                if (branch.TargetOffset != blocks[mergeIndex].StartOffset)
                    return null;
                exitsToMerge = true;
                statementCount--;
            }
            else if (i + 1 == mergeIndex)
            {
                exitsToMerge = true;
            }

            for (int s = 0; s < statementCount; s++)
            {
                if (IsControlFlow(children[s]))
                    return null;
                statements.Add(children[s]);
            }
            if (!exitsToMerge)
                continue;

            if (statements.Count == 0 || statements[^1] is not StoreStackSlot store)
                return null;
            return new TrueRegionShape(i, statements.Take(statements.Count - 1).ToList(), store);
        }
        return null;
    }

    static bool SubstitutePrefixStores(
        IrExpression initial,
        IReadOnlyList<StoreStackSlot> prefixStores,
        int resultSlot,
        out IrExpression substituted)
    {
        substituted = initial;
        for (int i = prefixStores.Count - 1; i >= 0; i--)
        {
            var store = prefixStores[i];
            if (store.Slot == resultSlot)
                return false;

            var loads = substituted.Descendants.Prepend(substituted)
                .OfType<LoadStackSlot>()
                .Where(load => load.Slot == store.Slot)
                .ToList();
            if (loads.Count != 1)
                return false;

            var replacement = (IrExpression)store.Value.Clone();
            if (ReferenceEquals(loads[0], substituted))
            {
                substituted = replacement;
            }
            else
            {
                loads[0].ReplaceWith(replacement);
            }
        }
        return true;
    }

    static bool PrefixSlotsDeadOutside(
        IrFunction function,
        IReadOnlyList<Block> blocks,
        int trueStart,
        int trueEnd,
        HashSet<int> prefixSlots)
    {
        if (prefixSlots.Count == 0)
            return true;
        var trueBlocks = blocks.Skip(trueStart).Take(trueEnd - trueStart + 1).ToHashSet();
        foreach (var node in function.Descendants)
        {
            bool touches = node switch
            {
                LoadStackSlot load => prefixSlots.Contains(load.Slot),
                StoreStackSlot store => prefixSlots.Contains(store.Slot),
                _ => false,
            };
            if (touches && !InAnyBlock(node, trueBlocks))
                return false;
        }
        return true;
    }

    static bool InAnyBlock(IrNode node, HashSet<Block> blocks)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (current is Block block && blocks.Contains(block))
                return true;
        return false;
    }

    static bool NoExternalEntry(
        IReadOnlyList<Block> blocks,
        int root,
        int falseIndex,
        int trueStart,
        int trueEnd,
        HashSet<int> leaveTargets)
    {
        var forbidden = new HashSet<int> { blocks[falseIndex].StartOffset };
        for (int i = trueStart; i <= trueEnd; i++)
            forbidden.Add(blocks[i].StartOffset);
        if (forbidden.Overlaps(leaveTargets))
            return false;

        for (int i = 0; i < blocks.Count; i++)
        {
            if (i == root || i == falseIndex || (i >= trueStart && i <= trueEnd))
                continue;
            foreach (var node in blocks[i].Children)
                foreach (int target in Targets(node))
                    if (forbidden.Contains(target))
                        return false;
        }
        return true;
    }

    static void Fold(BlockContainer container, IReadOnlyList<Block> blocks, int p, Shape shape, Stepper stepper)
    {
        var ordered = container.Blocks.ToList();
        var folded = new Block(ordered[p].StartOffset);
        var rootChildren = ordered[p].DetachChildren();
        for (int k = 0; k < rootChildren.Count - 1; k++)
            folded.Add(rootChildren[k]);
        var conditional = new Conditional(shape.Condition, shape.WhenTrue, shape.WhenFalse)
        {
            MergedType = shape.ResultType,
        };
        folded.Add(new StoreStackSlot(shape.ResultSlot, conditional));
        if (NextKeptIndex(ordered.Count, shape) is { } next && next != shape.MergeIndex)
            folded.Add(new Branch(ordered[shape.MergeIndex].StartOffset));

        foreach (var block in ordered)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i == p)
            {
                rebuilt.Add(folded);
                continue;
            }
            if (i == shape.FalseIndex || (i >= shape.TrueStart && i <= shape.TrueEnd))
                continue;
            rebuilt.Add(ordered[i]);
        }
        stepper.StepOver("fold slot-store diamond into conditional store", container);
        container.ReplaceWith(rebuilt);
    }

    static int? NextKeptIndex(int count, Shape shape)
    {
        for (int i = shape.FalseIndex; i < count; i++)
            if (i != shape.FalseIndex && (i < shape.TrueStart || i > shape.TrueEnd))
                return i;
        return null;
    }

    static bool IsControlFlow(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter;

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch sw => sw.TargetOffsets,
        Leave leave => [leave.TargetOffset],
        _ => [],
    };
}
