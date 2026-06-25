namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Splits block-local synthetic stack-slot live ranges when the compiler reused
/// the same evaluation-stack position for unrelated values with different C#
/// types. The rewrite only renumbers the synthetic carrier and the loads reached
/// before the next write to that slot; it does not move evaluation.
/// </summary>
public sealed class StackSlotLiveRangePass : IIrPass
{
    public string Name => "stack-slot-live-range";

    public void Run(IrFunction function, PassContext context)
    {
        if (function.Descendants.Any(node => node is TryCatch or TryFinally or CatchClause))
            return;

        while (SplitOnce(function, context.Stepper))
        {
        }
    }

    static bool SplitOnce(IrFunction function, Stepper stepper)
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

                // The split only renumbers loads inside this block (LiveLoads scans
                // block.Children). If the slot is read from any other block, that value
                // may be live-out and the out-of-block read would be left on the old
                // slot — now holding a different range. The pass only handles block-local
                // ranges, so decline when the slot escapes the block.
                if (function.Descendants.OfType<LoadStackSlot>()
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
