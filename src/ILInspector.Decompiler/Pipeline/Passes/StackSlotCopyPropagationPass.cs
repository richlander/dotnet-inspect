using ILInspector.ControlFlow;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Eliminates a proof-backed synthetic stack-slot copy, <c>S_dst = S_src</c>, by
/// replacing the single destination use with a direct source-slot load. This is
/// not expression inlining: the source value is still loaded at the original use
/// site, so no user expression or side effect moves. The pass only proves that
/// the copied source slot cannot be rewritten between the copy and the use, and
/// that no control-flow edge can enter the use region while bypassing the copy.
/// </summary>
public sealed class StackSlotCopyPropagationPass : IIrPass
{
    public string Name => "stack-slot-copy-propagation";

    public void Run(IrFunction function, PassContext context)
    {
        while (PropagateOnce(function, function.Body, context.Stepper))
        {
        }
    }

    static bool PropagateOnce(IrFunction function, IrNode scope, Stepper stepper)
    {
        if (PropagateInScope(function, scope, stepper))
            return true;

        foreach (var node in CoercionSinks.ScopeNodes(scope))
        {
            switch (node)
            {
                case Lambda lambda when PropagateOnce(function, lambda, stepper):
                    return true;
                case LocalFunctionStatement local when PropagateOnce(function, local, stepper):
                    return true;
            }
        }
        return false;
    }

    static bool PropagateInScope(IrFunction function, IrNode scope, Stepper stepper)
    {
        var nodes = CoercionSinks.ScopeNodes(scope).ToList();
        var storesBySlot = nodes.OfType<StoreStackSlot>().GroupBy(store => store.Slot)
            .ToDictionary(group => group.Key, group => group.ToList());
        var loadsBySlot = nodes.OfType<LoadStackSlot>().GroupBy(load => load.Slot)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var store in nodes.OfType<StoreStackSlot>().ToList())
        {
            if (store.Value is not LoadStackSlot source || source.Slot == store.Slot)
                continue;
            if (storesBySlot.GetValueOrDefault(store.Slot) is not { Count: 1 })
                continue;
            if (loadsBySlot.GetValueOrDefault(store.Slot) is not [var use])
                continue;
            if (!IsMultiBlockSourceResidual(source.Slot, storesBySlot))
                continue;
            if (FeedsLockReceiverCarrier(use))
                continue;
            if (use.Parent is IncrementDecrement)
                continue;
            if (TryGetCopyPath(store, use) is not { } path)
                continue;
            if (!SourceStableOnEveryPath(path, source.Slot))
                continue;
            if (RegionHasCycle(path))
                continue;
            if (!RegionDominatedByCopy(path))
                continue;

            var replacement = new LoadStackSlot(source.Slot, use.Type ?? source.Type);
            replacement.InheritSourceOffset(use);
            stepper.StepOver($"propagate stack-slot copy S_{store.Slot} from S_{source.Slot}", use);
            use.ReplaceWith(replacement);
            store.Detach();
            return true;
        }
        return false;
    }

    static bool IsMultiBlockSourceResidual(int sourceSlot, Dictionary<int, List<StoreStackSlot>> storesBySlot)
        => storesBySlot.GetValueOrDefault(sourceSlot) is { Count: > 1 } stores
            && stores.Select(store => store.Parent).Distinct().Count() > 1;

    static bool FeedsLockReceiverCarrier(LoadStackSlot use)
        => EnclosingStatement(use) is StoreLocal store
            && ReferenceOwnership.IsInside(use, store.Value)
            && LocalFeedsLockObject(store);

    static bool LocalFeedsLockObject(StoreLocal store)
        => Root(store).Descendants.OfType<Lock>()
            .Any(lockNode => lockNode.LockObject is LoadLocal load && load.Index == store.Index);

    static IrNode Root(IrNode node)
    {
        var current = node;
        while (current.Parent is { } parent)
            current = parent;
        return current;
    }

    sealed record CopyPath(
        IReadOnlyList<Block> Blocks,
        IReadOnlyList<BlockEdges> Edges,
        int CopyBlockIndex,
        int CopyStatementIndex,
        int UseBlockIndex,
        IrNode UseStatement,
        HashSet<int> RegionBlocks);

    static CopyPath? TryGetCopyPath(StoreStackSlot copy, LoadStackSlot use)
    {
        if (copy.Parent is not Block copyBlock || copyBlock.Parent is not BlockContainer container)
            return null;
        if (EnclosingStatement(use) is not { } useStatement || useStatement.Parent is not Block useBlock)
            return null;
        if (!ReferenceEquals(useBlock.Parent, container))
            return null;

        if (!IsIndependentBodyScope(container))
            return null;

        var blocks = container.Blocks;
        if (HasUnmodeledControlFlow(blocks))
            return null;

        // Consume the single shared structural CFG rather than a private edge
        // model. Cfg.Build reports EH leaves and out-of-container branch targets
        // as flags instead of resolving them, so bail on any incomplete graph —
        // the same contract DefiniteAssignment uses. This turns the previously
        // accidental exclusion of leave edges (a side effect of guard
        // interaction) into an explicit, pinned invariant.
        var edges = Cfg.Build(blocks);
        if (edges.Any(edge => edge.LeavesRegion || edge.ExternalTargets.Count > 0))
            return null;

        int copyBlockIndex = copyBlock.ChildIndex;
        int useBlockIndex = useBlock.ChildIndex;
        if (copyBlockIndex < 0 || useBlockIndex < 0)
            return null;

        var region = new HashSet<int>();
        var stack = new Stack<(int BlockIndex, int StartStatement)>();
        var visited = new HashSet<(int BlockIndex, int StartStatement)>();
        bool foundUse = false;

        stack.Push((copyBlockIndex, copy.ChildIndex + 1));
        while (stack.Count > 0)
        {
            var state = stack.Pop();
            if (!visited.Add(state))
                continue;
            if ((uint)state.BlockIndex >= (uint)blocks.Count)
                return null;

            region.Add(state.BlockIndex);
            var block = blocks[state.BlockIndex];
            for (int i = state.StartStatement; i < block.Children.Count; i++)
            {
                var statement = block.Children[i];
                if (ReferenceEquals(statement, useStatement))
                {
                    foundUse = true;
                    continue;
                }
                if (ReferenceOwnership.IsInside(use, statement))
                    return null; // The use must be the statement we identified.
            }

            if (state.BlockIndex == useBlockIndex)
                continue;

            foreach (int successor in edges[state.BlockIndex].Successors)
                stack.Push((successor, 0));
        }

        return foundUse
            ? new CopyPath(blocks, edges, copyBlockIndex, copy.ChildIndex, useBlockIndex, useStatement, region)
            : null;
    }

    static bool SourceStableOnEveryPath(CopyPath path, int sourceSlot)
    {
        foreach (int blockIndex in path.RegionBlocks)
        {
            var block = path.Blocks[blockIndex];
            int start = blockIndex == path.CopyBlockIndex ? path.CopyStatementIndex + 1 : 0;
            int end = blockIndex == path.UseBlockIndex ? path.UseStatement.ChildIndex : block.Children.Count - 1;
            for (int i = start; i <= end; i++)
            {
                var statement = block.Children[i];
                if (WritesSlot(statement, sourceSlot))
                    return false;
            }
        }
        return true;
    }

    static bool RegionHasCycle(CopyPath path)
    {
        var state = new Dictionary<int, int>();
        foreach (int blockIndex in path.RegionBlocks)
            if (Visit(blockIndex))
                return true;
        return false;

        bool Visit(int blockIndex)
        {
            if (!path.RegionBlocks.Contains(blockIndex))
                return false;
            if (state.GetValueOrDefault(blockIndex) == 1)
                return true;
            if (state.GetValueOrDefault(blockIndex) == 2)
                return false;

            state[blockIndex] = 1;
            foreach (int successor in path.Edges[blockIndex].Successors)
                if (Visit(successor))
                    return true;
            state[blockIndex] = 2;
            return false;
        }
    }

    static bool RegionDominatedByCopy(CopyPath path)
    {
        var predecessors = Predecessors(path.Edges);
        foreach (int blockIndex in path.RegionBlocks)
        {
            if (blockIndex == path.CopyBlockIndex)
                continue;
            foreach (int predecessor in predecessors[blockIndex])
                if (!path.RegionBlocks.Contains(predecessor))
                    return false;
        }
        return true;
    }

    static List<int>[] Predecessors(IReadOnlyList<BlockEdges> edges)
    {
        var predecessors = new List<int>[edges.Count];
        for (int i = 0; i < predecessors.Length; i++)
            predecessors[i] = [];
        for (int i = 0; i < edges.Count; i++)
        {
            foreach (int successor in edges[i].Successors)
                predecessors[successor].Add(i);
        }
        return predecessors;
    }

    static bool IsIndependentBodyScope(BlockContainer container)
        => container.Parent is IrFunction or Lambda or LocalFunctionStatement;

    static bool HasUnmodeledControlFlow(IReadOnlyList<Block> blocks)
    {
        foreach (var block in blocks)
            foreach (var statement in block.Children)
                if (ContainsUnmodeledControlFlow(statement))
                    return true;
        return false;
    }

    static bool ContainsUnmodeledControlFlow(IrNode node)
    {
        if (node is Lambda or LocalFunctionStatement)
            return false;
        if (node is Break or Continue)
            return true;
        if (node is Block or BlockContainer)
            return true;

        foreach (var child in node.Children)
            if (ContainsUnmodeledControlFlow(child))
                return true;
        return false;
    }

    static IrNode? EnclosingStatement(IrNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (current.Parent is Block)
                return current;
        return null;
    }

    static bool WritesSlot(IrNode statement, int slot)
        => statement.Descendants.Prepend(statement)
            .OfType<StoreStackSlot>()
            .Any(store => store.Slot == slot);
}
