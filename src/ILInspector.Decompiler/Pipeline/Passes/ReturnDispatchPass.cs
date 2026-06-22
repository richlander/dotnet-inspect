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
            if (TryFold(container, context.Stepper))
                return;
    }

    sealed record Arm(IReadOnlyList<IrNode> Prefix, IrExpression Condition, IrExpression Value, int TargetIndex);
    sealed record Plan(List<Arm> Arms, IrExpression DefaultValue);

    static bool TryFold(BlockContainer container, Stepper stepper)
    {
        var blocks = container.Blocks;
        if (blocks.Count < MinArms + 1)
            return false;

        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        if (BuildPlan(blocks, offsetToIndex) is not { } plan)
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

        foreach (var old in blocks)
            old.Detach();
        var replacement = new BlockContainer();
        replacement.Add(block);
        stepper.StepOver("fold flat return dispatch", container);
        container.ReplaceWith(replacement);
        return true;
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

    static bool HasControlFlow(IrNode node)
        => node.Descendants.Prepend(node).Any(child => child is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter);
}
