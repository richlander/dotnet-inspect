namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Splits straight-line synthetic stack-slot live ranges when the compiler
/// reused the same evaluation-stack position for unrelated values with
/// different C# types. The rewrite only renumbers the synthetic carrier and
/// the loads reached before the next write to that slot; it does not move
/// evaluation.
/// </summary>
public sealed class StackSlotLiveRangePass : IIrPass
{
    public string Name => "stack-slot-live-range";

    public void Run(IrFunction function, PassContext context)
    {
        bool hasStructuredEh = function.Descendants.Any(node => node is TryCatch or TryFinally or CatchClause);
        while (SplitOnce(function, context.Stepper, hasStructuredEh))
        {
        }
    }

    static bool SplitOnce(IrFunction function, Stepper stepper, bool hasStructuredEh)
    {
        foreach (var block in function.Descendants.OfType<Block>())
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
                        .Any(load => load.Slot == store.Slot && !IsDescendantOf(load, block)))
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
