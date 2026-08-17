using ILInspector.ControlFlow;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Splits proof-backed synthetic stack-slot live ranges when the compiler
/// reused the same evaluation-stack position for unrelated values with
/// different C# types. Block-local ranges use a linear proof. Function-body
/// cross-block ranges use reaching definitions and split only a definition
/// whose every reached load is reached by that definition alone. The rewrite
/// only renumbers the synthetic carrier and its proven loads; it does not move
/// evaluation.
/// </summary>
public sealed class StackSlotLiveRangePass : IIrPass
{
    public string Name => "stack-slot-live-range";

    public void Run(IrFunction function, PassContext context)
    {
        bool hasStructuredEh = function.Descendants.Any(node => node is TryCatch or TryFinally or CatchClause);
        while (SplitOnce(function, context.Stepper, hasStructuredEh)
            || SplitCrossBlockOnce(function, context.Stepper, hasStructuredEh))
        {
        }
    }

    static bool SplitOnce(IrFunction function, Stepper stepper, bool hasStructuredEh)
    {
        foreach (var block in CoercionSinks.ScopeNodes(function.Body).OfType<Block>())
        {
            for (int i = 0; i < block.Children.Count; i++)
            {
                if (block.Children[i] is not StoreStackSlot store || store.Value.ResultType is not { } valueType)
                    continue;

                var previousType = PreviousSlotType(block, i, store.Slot);
                if (previousType is null || previousType.Equals(valueType))
                    continue;

                var liveLoads = LiveLoads(block, i, store.Slot).ToList();
                if (liveLoads.Count == 0)
                    continue;

                // The split only renumbers the loads reached by LiveLoads. A read
                // from another block may be live-out and would be left on the old
                // slot. Structured EH needs the stronger proof: later rewrites can
                // reshape its regions, so every reference must belong to a top-level
                // statement in one top-level try-body block, with no read-before-write
                // hidden under a same-slot store.
                if (hasStructuredEh
                    ? !ReferencesAreStraightLineInBlock(function, store.Slot, block)
                    : function.Descendants.OfType<LoadStackSlot>()
                            .Any(load => load.Slot == store.Slot && !IsDescendantOf(load, block))
                        || HasLoopCarriedLoadBeforeStore(function, block, i, store.Slot))
                    continue;

                int newSlot = FreshStackSlot(function);
                stepper.StepOver($"split stack slot {store.Slot} live range to S_{newSlot}", store);
                foreach (var load in liveLoads)
                    load.ReplaceWith(new LoadStackSlot(newSlot, load.Type ?? valueType));

                var value = (IrExpression)store.DetachChildren()[0];
                var replacement = new StoreStackSlot(newSlot, value);
                replacement.InheritSourceOffset(store);
                store.ReplaceWith(replacement);
                return true;
            }
        }
        return false;
    }

    static bool SplitCrossBlockOnce(IrFunction function, Stepper stepper, bool hasStructuredEh)
    {
        if (hasStructuredEh || function.Body.Blocks.Count < 2)
            return false;

        var blocks = function.Body.Blocks;
        if (!HasUniqueBlockOffsets(blocks) || HasUnmodeledControlTransfer(blocks))
            return false;

        var edges = Cfg.Build(blocks);
        if (edges.Any(edge => edge.LeavesRegion || edge.ExternalTargets.Count > 0))
            return false;

        var scopeNodes = CoercionSinks.ScopeNodes(function.Body).ToList();
        foreach (var storesBySlot in scopeNodes.OfType<StoreStackSlot>().GroupBy(store => store.Slot))
        {
            int slot = storesBySlot.Key;
            var stores = storesBySlot.ToList();
            if (stores.Count < 2 || !ReferencesAreTopLevelFunctionBodyStatements(scopeNodes, slot, function.Body))
                continue;

            int undefined = stores.Count;
            var definitionIds = stores.Select((store, id) => (store, id))
                .ToDictionary(pair => pair.store, pair => pair.id);
            var universe = Enumerable.Range(0, stores.Count + 1).ToHashSet();
            var transfers = new GenKillSet[blocks.Count];
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                var lastStore = blocks[blockIndex].Children
                    .OfType<StoreStackSlot>()
                    .LastOrDefault(store => store.Slot == slot);
                transfers[blockIndex] = lastStore is null
                    ? GenKillSet.Empty
                    : new GenKillSet(
                        new HashSet<int> { definitionIds[lastStore] },
                        universe);
            }

            var flow = ForwardDataflow.Solve(
                edges,
                transfers,
                // Cfg and ForwardDataflow define body block 0 as the external entry.
                entry: new HashSet<int> { undefined },
                universe,
                DataflowMerge.Union,
                DataflowEntry.MergePredecessors);
            var storeInputs = new Dictionary<StoreStackSlot, HashSet<int>>();
            var loadInputs = new Dictionary<LoadStackSlot, HashSet<int>>();
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                if (!flow.Blocks[blockIndex].Reachable)
                    continue;

                var reaching = new HashSet<int>(flow.Blocks[blockIndex].In);
                foreach (var statement in blocks[blockIndex].Children)
                {
                    foreach (var load in ScopeDescendants(statement)
                        .Prepend(statement)
                        .OfType<LoadStackSlot>()
                        .Where(load => load.Slot == slot))
                    {
                        loadInputs[load] = new HashSet<int>(reaching);
                    }

                    if (statement is StoreStackSlot store && store.Slot == slot)
                    {
                        storeInputs[store] = new HashSet<int>(reaching);
                        reaching.Clear();
                        reaching.Add(definitionIds[store]);
                    }
                }
            }

            // An undefined read is evidence that the carrier's lifetime is not
            // fully represented by the modeled definitions. Keep the whole slot
            // intact rather than changing definite-assignment behavior.
            if (loadInputs.Values.Any(input => input.Contains(undefined)))
                continue;

            for (int definitionId = 0; definitionId < stores.Count; definitionId++)
            {
                var store = stores[definitionId];
                if (store.Value.ResultType is not { } valueType
                    || !storeInputs.TryGetValue(store, out var input)
                    || !input.Any(otherId => otherId != definitionId
                        && otherId < stores.Count
                        && stores[otherId].Value.ResultType is { } otherType
                        && !otherType.Equals(valueType)))
                {
                    continue;
                }

                var reachedLoads = loadInputs
                    .Where(pair => pair.Value.Contains(definitionId))
                    .ToList();
                if (reachedLoads.Count == 0
                    || reachedLoads.Any(pair => pair.Value.Count != 1)
                    || reachedLoads.All(pair => ReferenceEquals(EnclosingBlock(pair.Key), store.Parent)))
                {
                    continue;
                }

                int newSlot = FreshStackSlot(function);
                stepper.StepOver($"split stack slot {slot} cross-block live range to S_{newSlot}", store);
                foreach (var (load, _) in reachedLoads)
                    load.ReplaceWith(new LoadStackSlot(newSlot, load.Type ?? valueType));

                var value = (IrExpression)store.DetachChildren()[0];
                var replacement = new StoreStackSlot(newSlot, value);
                replacement.InheritSourceOffset(store);
                store.ReplaceWith(replacement);
                return true;
            }
        }
        return false;
    }

    static bool ReferencesAreTopLevelFunctionBodyStatements(
        IReadOnlyList<IrNode> scopeNodes,
        int slot,
        BlockContainer functionBody)
    {
        foreach (var node in scopeNodes)
        {
            bool isReference = node switch
            {
                StoreStackSlot slotStore => slotStore.Slot == slot,
                LoadStackSlot slotLoad => slotLoad.Slot == slot,
                _ => false,
            };
            if (!isReference)
                continue;

            var statement = EnclosingStatement(node);
            if (statement?.Parent is not Block block
                || !ReferenceEquals(block.Parent, functionBody)
                || node is StoreStackSlot && !ReferenceEquals(node, statement)
                || CoercionSinks.ScopeNodes(statement).Any(descendant => descendant is Block or BlockContainer))
            {
                return false;
            }

            if (node is StoreStackSlot store
                && CoercionSinks.ScopeNodes(store.Value).Prepend(store.Value)
                    .OfType<LoadStackSlot>()
                    .Any(load => load.Slot == slot))
            {
                return false;
            }
        }
        return true;
    }

    static bool HasUnmodeledControlTransfer(IReadOnlyList<Block> blocks)
        => blocks.Any(block =>
            block.Children.SkipLast(1).Any(IsControlTransfer)
            || block.Children.SelectMany(ScopeDescendants).Any(IsControlTransfer));

    static bool HasUniqueBlockOffsets(IReadOnlyList<Block> blocks)
        => blocks.Select(block => block.StartOffset).Distinct().Count() == blocks.Count;

    static IEnumerable<IrNode> ScopeDescendants(IrNode root)
        => root is LocalFunctionStatement or Lambda
            ? []
            : CoercionSinks.ScopeNodes(root);

    static bool IsControlTransfer(IrNode node)
        => node is Branch or ConditionalBranch or SwitchBranch
            or Leave or EndFinally or EndFilter;

    static IrNode? EnclosingStatement(IrNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (current.Parent is Block)
                return current;
        return null;
    }

    static Block? EnclosingBlock(IrNode node)
        => EnclosingStatement(node)?.Parent as Block;

    static bool ReferencesAreStraightLineInBlock(IrFunction function, int slot, Block block)
    {
        if (!IsTopLevelTryBodyBlock(block))
            return false;

        foreach (var node in function.Descendants)
        {
            if (node is StoreStackSlot store && store.Slot == slot
                || node is LoadStackSlot load && load.Slot == slot)
            {
                if (node is StoreStackSlot slotStore
                    && slotStore.Value.Descendants.Prepend(slotStore.Value).OfType<LoadStackSlot>()
                        .Any(nestedLoad => nestedLoad.Slot == slot))
                {
                    return false;
                }

                var statement = node;
                while (statement.Parent is not null && !ReferenceEquals(statement.Parent, block))
                    statement = statement.Parent;

                if (!ReferenceEquals(statement.Parent, block)
                    || statement.Descendants.Any(descendant => descendant is Block or BlockContainer))
                {
                    return false;
                }
            }
        }
        return true;
    }

    static bool IsTopLevelTryBodyBlock(Block block)
    {
        if (block.Parent is not BlockContainer container)
            return false;

        IrNode? owner = container.Parent switch
        {
            TryCatch tryCatch when ReferenceEquals(tryCatch.TryBody, container) => tryCatch,
            TryFinally tryFinally when ReferenceEquals(tryFinally.TryBody, container) => tryFinally,
            _ => null,
        };

        return owner?.Parent is Block containingBlock
            && containingBlock.Parent is BlockContainer functionBody
            && functionBody.Parent is IrFunction;
    }

    static bool HasLoopCarriedLoadBeforeStore(IrFunction function, Block block, int storeChild, int slot)
    {
        bool hasPriorLoad = block.Children.Take(storeChild)
            .Any(statement => statement.Descendants.Prepend(statement)
                .OfType<LoadStackSlot>()
                .Any(load => load.Slot == slot));
        if (!hasPriorLoad)
            return false;
        if (IsWithinStructuredLoop(block))
            return true;

        var blocks = function.Body.Blocks;
        if (HasUnmodeledControlTransfer(blocks)
            || !HasUniqueBlockOffsets(blocks))
        {
            return true;
        }

        var edges = Cfg.Build(blocks);
        if (edges.Any(edge => edge.LeavesRegion || edge.ExternalTargets.Count > 0))
            return true;

        var functionBodyBlock = EnclosingFunctionBodyBlock(block, function.Body);
        if (functionBodyBlock is null)
            return false;

        int blockIndex = -1;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (ReferenceEquals(blocks[i], functionBodyBlock))
            {
                blockIndex = i;
                break;
            }
        }

        if (blockIndex < 0)
            return true;

        var pending = new Stack<int>(edges[blockIndex].Successors);
        var visited = new HashSet<int>();
        while (pending.TryPop(out int candidate))
        {
            if (candidate == blockIndex)
                return true;
            if (!visited.Add(candidate))
                continue;

            foreach (int successor in edges[candidate].Successors)
                pending.Push(successor);
        }
        return false;
    }

    static Block? EnclosingFunctionBodyBlock(Block block, BlockContainer functionBody)
    {
        for (IrNode? current = block; current is not null; current = current.Parent)
        {
            if (current is Lambda or LocalFunctionStatement)
                return null;
            if (current is Block candidate && ReferenceEquals(candidate.Parent, functionBody))
                return candidate;
        }
        return null;
    }

    static bool IsWithinStructuredLoop(Block block)
    {
        for (var current = block.Parent; current is not null; current = current.Parent)
        {
            if (current is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement)
                return true;
        }
        return false;
    }

    static TypeRef? PreviousSlotType(Block block, int beforeChild, int slot)
    {
        TypeRef? previous = null;
        for (int i = 0; i < beforeChild; i++)
        {
            foreach (var node in block.Children[i].Descendants.Prepend(block.Children[i]))
            {
                previous = node switch
                {
                    StoreStackSlot store when store.Slot == slot => store.Value.ResultType ?? previous,
                    LoadStackSlot load when load.Slot == slot => load.Type ?? previous,
                    _ => previous,
                };
            }
        }
        return previous;
    }

    static IEnumerable<LoadStackSlot> LiveLoads(Block block, int storeChild, int slot)
    {
        var store = block.Children[storeChild];
        for (int i = storeChild + 1; i < block.Children.Count; i++)
        {
            var statement = block.Children[i];
            if (statement.Descendants.Prepend(statement).OfType<StoreStackSlot>().Any(s => s.Slot == slot))
                yield break;

            foreach (var load in statement.Descendants.Prepend(statement).OfType<LoadStackSlot>())
                if (load.Slot == slot && !IsDescendantOf(load, store))
                    yield return load;
        }
    }

    static bool IsDescendantOf(IrNode node, IrNode ancestor)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (ReferenceEquals(current, ancestor))
                return true;
        return false;
    }

    static int FreshStackSlot(IrFunction function)
    {
        int maxSlot = StoreStackSlot.DupSlotBase - 1;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreStackSlot store:
                    maxSlot = Math.Max(maxSlot, store.Slot);
                    break;
                case LoadStackSlot load:
                    maxSlot = Math.Max(maxSlot, load.Slot);
                    break;
            }
        }
        return maxSlot + 1;
    }
}
