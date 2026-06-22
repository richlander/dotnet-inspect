using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a flat stack-slot diamond that immediately feeds a return:
/// <code>
///   if (c) goto T;
///   S = whenFalse; goto J;
///   T: S = whenTrue;
///   J: return S;
/// </code>
/// into <c>return c ? whenTrue : whenFalse;</c>. The shape appears inside
/// non-nested type-test dispatches after <see cref="ReturnMergePass"/> has
/// inlined the shared return tail, but before <see cref="StructuringPass"/> can
/// inline the arm as a guard-return terminator.
/// </summary>
public sealed class ConditionalReturnDiamondPass : IIrPass
{
    public string Name => "conditional-return-diamond";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
            if (TryFold(container, stepper))
                return true;
        return false;
    }

    static bool TryFold(BlockContainer container, Stepper stepper)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        for (int p = 0; p + 3 < blocks.Count; p++)
        {
            if (Match(blocks, offsetToIndex, p) is not { } match
                || HasExternalEntry(blocks, p, match))
            {
                continue;
            }

            Fold(container, p, match, stepper);
            return true;
        }
        return false;
    }

    sealed record DiamondMatch(
        ConditionalBranch Branch,
        int FalseIndex,
        int TrueIndex,
        int JoinIndex,
        StoreStackSlot FalseStore,
        StoreStackSlot TrueStore,
        LoadStackSlot JoinLoad);

    static DiamondMatch? Match(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int p)
    {
        var block = blocks[p];
        if (block.Children.Count == 0 || block.Children[^1] is not ConditionalBranch branch)
            return null;
        for (int i = 0; i < block.Children.Count - 1; i++)
            if (block.Children[i] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return null;

        int falseIndex = p + 1;
        if (!offsetToIndex.TryGetValue(branch.TargetOffset, out int trueIndex)
            || trueIndex != falseIndex + 1)
        {
            return null;
        }

        if (blocks[falseIndex].Children is not [StoreStackSlot falseStore, Branch falseBranch])
            return null;
        if (blocks[trueIndex].Children is not [StoreStackSlot trueStore]
            || trueStore.Slot != falseStore.Slot)
        {
            return null;
        }

        int joinIndex = trueIndex + 1;
        if (!offsetToIndex.TryGetValue(falseBranch.TargetOffset, out int falseJoin)
            || falseJoin != joinIndex
            || joinIndex >= blocks.Count)
        {
            return null;
        }

        var joinLoad = JoinLoad(blocks[joinIndex]);
        if (joinLoad is null || joinLoad.Slot != falseStore.Slot)
            return null;

        return new DiamondMatch(branch, falseIndex, trueIndex, joinIndex, falseStore, trueStore, joinLoad);
    }

    static LoadStackSlot? JoinLoad(Block join) => join.Children switch
    {
        [Return { Value: LoadStackSlot load }] => load,
        [StoreLocal { Index: var index, Value: LoadStackSlot load }, Return { Value: LoadLocal returned }]
            when returned.Index == index => load,
        _ => null,
    };

    static bool HasExternalEntry(IReadOnlyList<Block> blocks, int p, DiamondMatch match)
    {
        var forbidden = new HashSet<int>
        {
            blocks[match.FalseIndex].StartOffset,
            blocks[match.TrueIndex].StartOffset,
            blocks[match.JoinIndex].StartOffset,
        };

        for (int i = 0; i < blocks.Count; i++)
        {
            if (i == p || i == match.FalseIndex || i == match.TrueIndex)
                continue;
            foreach (var node in blocks[i].Children)
                foreach (int target in Targets(node))
                    if (forbidden.Contains(target))
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

    static void Fold(BlockContainer container, int p, DiamondMatch match, Stepper stepper)
    {
        var blocks = container.Blocks.ToList();
        var rootChildren = blocks[p].DetachChildren();
        var condition = (IrExpression)match.Branch.DetachChildren()[0];
        var whenFalse = (IrExpression)match.FalseStore.DetachChildren()[0];
        var whenTrue = (IrExpression)match.TrueStore.DetachChildren()[0];
        var mergedType = match.JoinLoad.Type ?? whenTrue.ResultType ?? whenFalse.ResultType;

        var folded = new Block(blocks[p].StartOffset);
        for (int i = 0; i < rootChildren.Count - 1; i++)
            folded.Add(rootChildren[i]);
        folded.Add(new Return(new Conditional(condition, whenTrue, whenFalse) { MergedType = mergedType }));

        foreach (var block in blocks)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int i = 0; i < p; i++)
            rebuilt.Add(blocks[i]);
        rebuilt.Add(folded);
        for (int i = match.JoinIndex + 1; i < blocks.Count; i++)
            rebuilt.Add(blocks[i]);

        stepper.StepOver("fold conditional stack-slot return diamond", container);
        container.ReplaceWith(rebuilt);
    }
}
