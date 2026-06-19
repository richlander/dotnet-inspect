namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Forward-substitutes single-use temporaries — the inverse of the
/// compiler's expression spilling. A store inlines into the load
/// only when the rewrite provably cannot reorder effects: single store,
/// single load, address never taken, and the load sits in the immediately
/// following statement either as its first-evaluated leaf (the stored value
/// still evaluates first) or with a pure stored value (evaluation order
/// cannot matter). Runs to fixpoint so spill chains collapse.
/// </summary>
public sealed class ExpressionInliningPass : IIrPass
{
    public string Name => "expression-inlining";

    public void Run(IrFunction function, PassContext context)
    {
        while (InlineOnce(function, context))
        {
        }
    }

    static bool InlineOnce(IrFunction function, PassContext context)
    {
        var locals = new Dictionary<(bool IsSlot, int Index), (List<IrNode> Loads, List<IrNode> Stores, bool AddressTaken)>();
        var argumentAddresses = new HashSet<int>();

        (List<IrNode> Loads, List<IrNode> Stores, bool AddressTaken) Entry(bool isSlot, int index)
        {
            if (!locals.TryGetValue((isSlot, index), out var entry))
                locals[(isSlot, index)] = entry = ([], [], false);
            return entry;
        }

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocal load: Entry(false, load.Index).Loads.Add(load); break;
                case StoreLocal store when store.Parent is Block: Entry(false, store.Index).Stores.Add(store); break;
                case LoadLocalAddress address: locals[(false, address.Index)] = Entry(false, address.Index) with { AddressTaken = true }; break;
                case LoadArgumentAddress argumentAddress: argumentAddresses.Add(argumentAddress.Index); break;
                case StoreArgument argumentStore: argumentAddresses.Add(argumentStore.Index); break;
                case LoadStackSlot load: Entry(true, load.Slot).Loads.Add(load); break;
                case StoreStackSlot store when store.Parent is Block: Entry(true, store.Slot).Stores.Add(store); break;
            }
        }

        foreach (var ((isSlot, _), (loads, stores, addressTaken)) in locals)
        {
            if (addressTaken || loads.Count != 1 || stores.Count != 1)
                continue;
            var store = stores[0];
            var load = loads[0];
            var block = (Block)store.Parent!;
            IrNode next;
            if (store.ChildIndex + 1 < block.Children.Count)
            {
                next = block.Children[store.ChildIndex + 1];
            }
            else if (FallthroughFirstStatement(function, block) is { } following)
            {
                // The store ends its block; the only path to the next block
                // is the fallthrough edge (no branches target it), so the
                // following block's first statement is "next".
                next = following;
            }
            else
            {
                continue;
            }
            if (!IsInside(load, next))
                continue;

            bool pure = IsPure(store is StoreLocal sl ? sl.Value : ((StoreStackSlot)store).Value, locals, argumentAddresses, function);
            if (!IsFirstEvaluatedLeaf(load, next) && !pure)
                continue;  // inlining would move the computation past whatever evaluates before the load

            var value = (IrExpression)store.DetachChildren()[0];

            context.Stepper.StepOver(
                $"inline {(isSlot ? "stack slot" : "local")} {(store is StoreLocal s ? s.Index : ((StoreStackSlot)store).Slot)} into its single use",
                load);

            store.Detach();
            load.ReplaceWith(value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The first statement of the block after <paramref name="block"/>, when
    /// fallthrough is its only incoming edge: no branch targets it, and both
    /// blocks sit in exactly the same exception regions — physical adjacency
    /// across a region boundary is not normal control flow, and moving a
    /// computation across one changes which instructions are protected.
    /// </summary>
    static IrNode? FallthroughFirstStatement(IrFunction function, Block block)
    {
        // The block's own container, not function.Body: after EH structuring
        // blocks live in nested containers, and indexing the top-level list
        // with a nested ChildIndex would alias an unrelated block. Staying
        // inside one container also keeps the edge inside one region.
        if (block.Parent is not BlockContainer container)
            return null;
        var blocks = container.Blocks;
        int index = block.ChildIndex;
        if (index + 1 >= blocks.Count)
            return null;
        var following = blocks[index + 1];
        if (following.Children.Count == 0)
            return null;
        if (!SameRegions(function, block.StartOffset, following.StartOffset))
            return null;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case Branch b when b.TargetOffset == following.StartOffset:
                case ConditionalBranch c when c.TargetOffset == following.StartOffset:
                case Leave l when l.TargetOffset == following.StartOffset:
                    return null;
                case SwitchBranch s when s.TargetOffsets.Contains(following.StartOffset):
                    return null;
            }
        }
        return following.Children[0];
    }

    /// <summary>True when both offsets sit inside exactly the same try, handler, and filter ranges.</summary>
    static bool SameRegions(IrFunction function, int offsetA, int offsetB)
    {
        foreach (var region in function.Regions)
        {
            if (Inside(offsetA, region.TryOffset, region.TryLength) != Inside(offsetB, region.TryOffset, region.TryLength))
                return false;
            if (Inside(offsetA, region.HandlerOffset, region.HandlerLength) != Inside(offsetB, region.HandlerOffset, region.HandlerLength))
                return false;
            if (region.Kind == HandlerKind.Filter
                && Inside(offsetA, region.FilterOffset, region.HandlerOffset - region.FilterOffset)
                    != Inside(offsetB, region.FilterOffset, region.HandlerOffset - region.FilterOffset))
            {
                return false;
            }
        }
        return true;

        static bool Inside(int offset, int start, int length) => offset >= start && offset < start + length;
    }

    static bool IsInside(IrNode node, IrNode root)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }

    /// <summary>True when the node is the first thing the statement evaluates: the spine of first children.</summary>
    static bool IsFirstEvaluatedLeaf(IrNode node, IrNode statement)
    {
        var current = statement;
        while (current.Children.Count > 0)
        {
            current = current.Children[0];
            if (ReferenceEquals(current, node))
                return true;
        }
        return false;
    }

    /// <summary>Expressions whose evaluation cannot observe or produce effects, so reordering them is invisible.</summary>
    static bool IsPure(
        IrExpression value,
        Dictionary<(bool IsSlot, int Index), (List<IrNode> Loads, List<IrNode> Stores, bool AddressTaken)> locals,
        HashSet<int> argumentAddresses,
        IrFunction function) => value switch
    {
        Constant or SizeOf or LoadToken => true,
        // Reads are reorder-safe only if nothing can mutate the place from
        // inside an expression: stores are statement-level in this IR, so
        // the remaining hazard is a call writing through an escaped address.
        // For instance methods, arg 0 may be a byref struct receiver that
        // any instance call mutates — TypeRef cannot yet tell struct from
        // class, so the receiver is never pure.
        LoadArgument argument => !argumentAddresses.Contains(argument.Index)
            && !(function.Signature.HasThis && argument.Index == 0),
        LoadLocal load => !locals.TryGetValue((false, load.Index), out var entry) || !entry.AddressTaken,
        _ => false,
    };
}
