using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a whole-method flat guard dispatch into ordered guard-return statements.
/// This targets csc's lowering for type-test dispatches:
/// <code>
///   if (a) goto A;
///   setupB; if (b) goto B;
///   return default;
///   A: return valueA;
///   B: return valueB;
/// </code>
/// The pass is deliberately narrow: every block in the container must be either
/// one guard/default block in the dispatch prefix or one terminal return arm.
/// </summary>
public sealed class ReturnDispatchPass : IIrPass
{
    public string Name => "return-dispatch";
    const int MinArms = 4;

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
            if (TryFold(function, container, context.Stepper))
                return;
    }

    sealed record Arm(IReadOnlyList<IrNode> Prefix, IrExpression Condition, IrExpression Value, int TargetIndex);
    sealed record Plan(List<Arm> Arms, IrExpression DefaultValue);
    sealed record TreePlan(Block Body, int LeafCount);

    static bool TryFold(IrFunction function, BlockContainer container, Stepper stepper)
    {
        var blocks = container.Blocks;
        if (blocks.Count < MinArms + 1)
            return false;

        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        if (BuildPlan(blocks, offsetToIndex) is { } plan)
        {
            if (HasExternalEntry(function, container, blocks))
                return false;

            var block = new Block(blocks[0].StartOffset);
            foreach (var arm in plan.Arms)
            {
                foreach (var prefix in arm.Prefix)
                    block.Add(prefix.Clone());
                var then = new Block(blocks[arm.TargetIndex].StartOffset);
                then.Add(new Return((IrExpression)arm.Value.Clone()));
                block.Add(new IfStatement((IrExpression)arm.Condition.Clone(), then, null));
            }
            block.Add(new Return((IrExpression)plan.DefaultValue.Clone()));

            ReplaceContainer(container, block);
            stepper.StepOver("fold flat return dispatch", container);
            return true;
        }

        if (ComparisonTrees.IsLikely(container)
            && BuildTreePlan(blocks, offsetToIndex) is { } treePlan)
        {
            if (HasExternalEntry(function, container, blocks))
                return false;

            ReplaceContainer(container, treePlan.Body);
            stepper.StepOver("fold comparison-tree return dispatch", container);
            return true;
        }

        return false;
    }

    static void ReplaceContainer(BlockContainer container, Block block)
    {
        foreach (var old in container.Blocks.ToList())
            old.Detach();
        var replacement = new BlockContainer();
        replacement.Add(block);
        container.ReplaceWith(replacement);
    }

    static Plan? BuildPlan(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex)
    {
        var arms = new List<Arm>();
        var consumed = new HashSet<int>();
        int i = 0;
        while (i < blocks.Count)
        {
            var current = blocks[i];
            if (current.Children.Count > 0 && current.Children[^1] is ConditionalBranch conditional)
            {
                if (!offsetToIndex.TryGetValue(conditional.TargetOffset, out int target)
                    || target <= i
                    || ReturnValue(blocks[target]) is not { } armValue)
                {
                    return null;
                }

                var prefix = current.Children.Take(current.Children.Count - 1).ToArray();
                if (prefix.Any(HasControlFlow))
                    return null;

                arms.Add(new Arm(prefix, conditional.Condition, armValue, target));
                consumed.Add(i);
                consumed.Add(target);
                i++;
                continue;
            }

            if (ReturnValue(current) is not { } defaultValue)
                return null;
            consumed.Add(i);
            if (arms.Count < MinArms || consumed.Count != blocks.Count)
                return null;
            return new Plan(arms, defaultValue);
        }
        return null;
    }

    static IrExpression? ReturnValue(Block block) => block.Children switch
    {
        [Return { Value: { } value }] => value,
        [StoreLocal { Index: var index, Value: { } value }, Return { Value: LoadLocal returned }]
            when returned.Index == index => value,
        _ => null,
    };

    static TreePlan? BuildTreePlan(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex)
    {
        var consumed = new HashSet<int>();
        var active = new HashSet<int>();
        var visitedBranches = new HashSet<int>();
        if (!TryBuildTree(blocks, offsetToIndex, 0, consumed, active, visitedBranches, out var body, out int leafCount))
            return null;
        return leafCount >= MinArms && consumed.Count == blocks.Count
            ? new TreePlan(body, leafCount)
            : null;
    }

    static bool TryBuildTree(
        IReadOnlyList<Block> blocks,
        Dictionary<int, int> offsetToIndex,
        int index,
        HashSet<int> consumed,
        HashSet<int> active,
        HashSet<int> visitedBranches,
        out Block body,
        out int leafCount)
    {
        body = null!;
        leafCount = 0;
        if (index < 0 || index >= blocks.Count || active.Contains(index))
            return false;

        var block = blocks[index];
        if (ReturnValue(block) is { } value)
        {
            body = new Block(block.StartOffset);
            body.Add(new Return((IrExpression)value.Clone()));
            consumed.Add(index);
            leafCount = 1;
            return true;
        }

        if (block.Children.Count == 0
            || block.Children[^1] is not ConditionalBranch conditional
            || !offsetToIndex.TryGetValue(conditional.TargetOffset, out int trueIndex)
            || trueIndex <= index
            || index + 1 >= blocks.Count)
        {
            return false;
        }

        if (!visitedBranches.Add(index))
            return false;
        for (int i = 0; i < block.Children.Count - 1; i++)
            if (HasControlFlow(block.Children[i]))
                return false;

        active.Add(index);
        if (!TryBuildTree(blocks, offsetToIndex, trueIndex, consumed, active, visitedBranches, out var thenBody, out int thenLeaves)
            || !TryBuildTree(blocks, offsetToIndex, index + 1, consumed, active, visitedBranches, out var elseBody, out int elseLeaves))
        {
            active.Remove(index);
            return false;
        }
        active.Remove(index);

        body = new Block(block.StartOffset);
        for (int i = 0; i < block.Children.Count - 1; i++)
            body.Add(block.Children[i].Clone());
        body.Add(new IfStatement((IrExpression)conditional.Condition.Clone(), thenBody, elseBody));
        consumed.Add(index);
        leafCount = thenLeaves + elseLeaves;
        return true;
    }

    static bool HasControlFlow(IrNode node)
        => node.Descendants.Prepend(node).Any(child => child is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter);

    static bool HasExternalEntry(IrFunction function, BlockContainer container, IReadOnlyList<Block> blocks)
    {
        var hiddenOffsets = blocks.Skip(1).Select(block => block.StartOffset).ToHashSet();
        foreach (var node in function.Descendants)
        {
            if (ReferenceOwnership.IsInside(node, container))
                continue;
            foreach (int target in Targets(node))
                if (hiddenOffsets.Contains(target))
                    return true;
        }
        return false;
    }

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch sw => sw.TargetOffsets,
        Leave leave => [leave.TargetOffset],
        _ => [],
    };
}
