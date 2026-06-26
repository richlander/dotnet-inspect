namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Forward-substitutes single-use temporaries — the inverse of the
/// compiler's expression spilling. A store inlines into the load
/// only when the rewrite provably cannot reorder effects: single store,
/// single load, address never taken, and the load sits in the immediately
/// following statement either as its first-evaluated leaf (the stored value
/// still evaluates first) or with a pure stored value (evaluation order
/// cannot matter). Runs to fixpoint so spill chains collapse.
///
/// <para>A second mode, <see cref="InlineLiveRangeOnce"/>, collapses the
/// spilled-call-chain shape the simple mode cannot: a fluent call chain
/// (<c>xs.Where(p).Select(f)</c>) spills its receiver, each lambda, and every
/// intermediate result into reused stack slots — so a slot has several stores
/// and several loads function-wide (defeating the single-store/single-load
/// keys), and a store's use sits past an interleaved statement (defeating the
/// adjacency rule). This mode reasons per-store over its live range: a movable
/// value (an effect-free read or a non-capturing delegate creation) inlines
/// into the one load it reaches before the slot is rewritten, provided nothing
/// in between writes what the value reads. Effect-free values reorder freely;
/// the interference scan keeps a value that reads a place from crossing a write
/// to it.</para>
/// </summary>
public sealed class ExpressionInliningPass : IIrPass
{
    public string Name => "expression-inlining";

    public void Run(IrFunction function, PassContext context)
    {
        while (InlineOnce(function, context) || InlineLiveRangeOnce(function, context))
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
            if (IsInsideCatchFilter(load))
                continue;
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
            if (!ReferenceOwnership.IsInside(load, next))
                continue;
            if (store is StoreLocal { Value: LoadField { Instance: null } } && IsLockObject(load))
                continue;  // keep copied static lock receivers; inlining can fail to bind in reconstructed shells

            if (store is StoreLocal typedStore && !typedStore.Type.Equals(typedStore.Value.ResultType))
                continue;  // the local declaration carries a required cast/type witness

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

    static bool IsLockObject(IrNode node)
        => node.Parent is Lock lockNode && ReferenceEquals(lockNode.LockObject, node);

    // A place the dataflow tracks: a method argument, a local, or a stack slot.
    enum PlaceKind { Argument, Local, Slot }

    /// <summary>
    /// One inline of a movable value into the single load it reaches within its
    /// live range — the spilled-call-chain collapse. Unlike <see cref="InlineOnce"/>
    /// this tolerates a slot that is reused (several stores/loads function-wide)
    /// and a use that sits past interleaved statements, by reasoning over the
    /// store's live range instead of function-wide counts.
    /// </summary>
    static bool InlineLiveRangeOnce(IrFunction function, PassContext context)
    {
        var argumentAddresses = new HashSet<int>();
        var addressTakenLocals = new HashSet<int>();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadArgumentAddress a: argumentAddresses.Add(a.Index); break;
                case StoreArgument s: argumentAddresses.Add(s.Index); break;
                case LoadLocalAddress a: addressTakenLocals.Add(a.Index); break;
            }
        }

        foreach (var block in function.Descendants.OfType<Block>())
        {
            for (int si = 0; si < block.Children.Count; si++)
            {
                // Only stack slots — the compiler's spill scratch — are inlined
                // here. A user local carries source meaning and the structuring
                // passes shape constructs (??=, deconstruction) around it; moving
                // those reshapes already-raised code into a different opcode stream.
                if (block.Children[si] is not StoreStackSlot store)
                    continue;
                var (targetKind, targetIndex, value) = (PlaceKind.Slot, store.Slot, store.Value);
                if (!TryMovableReads(value, out var reads))
                    continue;
                if (reads.Any(r => !ReadIsStable(r, argumentAddresses, addressTakenLocals, function)))
                    continue;
                // Confine to a slot whose every store and load lives in this one
                // block. Reaching-definition analysis across blocks is what the
                // simple mode's function-wide single-load key stands in for; a
                // block-local slot makes the forward scan below exact, so a store
                // in a successor block (or read back across a loop edge) can never
                // be the definition we silently drop. The spilled call chain is
                // block-local by construction.
                if (!IsConfinedToBlock(function, targetKind, targetIndex, block))
                    continue;

                // Walk forward to the one load this store reaches: the first
                // statement that loads the slot is its use (even if that statement
                // also rewrites the slot — the load on the right evaluates first).
                // Bail on a write to anything the value reads before the use, and
                // require the definition to be dead after that single use (a second
                // load before the slot is rewritten would read a value we moved).
                IrNode? use = null;
                IrNode? useStatement = null;
                bool blocked = false;
                for (int k = si + 1; k < block.Children.Count; k++)
                {
                    var stmt = block.Children[k];
                    var uses = LoadsOf(stmt, targetKind, targetIndex);
                    bool rewritesTarget = Writes(stmt, targetKind, targetIndex);
                    if (uses.Count > 0)
                    {
                        if (use is not null || uses.Count > 1)
                        {
                            blocked = true;       // a second use of this definition
                            break;
                        }
                        use = uses[0];
                        useStatement = stmt;
                        if (rewritesTarget)
                            break;                // redefined in the same statement, after the use
                        continue;
                    }
                    if (rewritesTarget)
                    {
                        blocked = use is null;    // dead before use ⇒ leave it alone
                        break;
                    }
                    if (use is null
                        && (reads.Any(r => Writes(stmt, r.Kind, r.Index))
                            || WritesStaticDelegateTarget(stmt, value)
                            || (RequiresFirstEvaluation(value) && HasObservableEffect(stmt))))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked || use is null || useStatement is null)
                    continue;

                // A value that reads places must still evaluate first at the use
                // site; an effect-free value that reads nothing can land anywhere.
                if ((reads.Count > 0 || RequiresFirstEvaluation(value)) && !IsFirstEvaluatedLeaf(use, useStatement))
                    continue;

                var inlined = (IrExpression)block.Children[si].DetachChildren()[0];
                context.Stepper.StepOver(
                    $"inline {(targetKind == PlaceKind.Slot ? "stack slot" : "local")} {targetIndex} into its live-range use",
                    use);
                block.Children[si].Detach();
                use.ReplaceWith(inlined);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A value safe to recompute at a later point: it produces no observable
    /// effect, so reordering it is invisible as long as the places it reads are
    /// unchanged in between (the caller's interference scan). A non-capturing
    /// delegate creation qualifies — its target is the static <c>&lt;&gt;c.&lt;&gt;9</c>
    /// singleton, so it reads nothing the method can mutate.
    /// </summary>
    static bool TryMovableReads(IrExpression value, out List<(PlaceKind Kind, int Index)> reads)
    {
        reads = [];
        switch (value)
        {
            case Constant or SizeOf or LoadToken:
                return true;
            case LoadArgument argument:
                reads.Add((PlaceKind.Argument, argument.Index));
                return true;
            case LoadLocal load:
                reads.Add((PlaceKind.Local, load.Index));
                return true;
            case LoadStackSlot load:
                reads.Add((PlaceKind.Slot, load.Slot));
                return true;
            case DelegateCreation { Target: LoadField { Instance: null } }:
                return true;
            default:
                return false;
        }
    }

    // A read is stable to move only if no escaped address could let an
    // intervening call mutate it: arg whose address is never taken (and not the
    // possibly-byref `this` of an instance method), or a local never addressed.
    static bool ReadIsStable(
        (PlaceKind Kind, int Index) read, HashSet<int> argumentAddresses, HashSet<int> addressTakenLocals, IrFunction function)
        => read.Kind switch
        {
            PlaceKind.Argument => !argumentAddresses.Contains(read.Index)
                && !(function.Signature.HasThis && read.Index == 0),
            PlaceKind.Local => !addressTakenLocals.Contains(read.Index),
            _ => true,
        };

    // True when every load and store of the place sits inside one block, so its
    // whole live range is the straight-line statements the forward scan reads.
    static bool IsConfinedToBlock(IrFunction function, PlaceKind kind, int index, Block block)
    {
        foreach (var node in function.Descendants)
        {
            bool touches = kind switch
            {
                PlaceKind.Slot => node is LoadStackSlot ls && ls.Slot == index || node is StoreStackSlot ss && ss.Slot == index,
                PlaceKind.Local => node is LoadLocal ll && ll.Index == index || node is StoreLocal sl && sl.Index == index,
                _ => false,
            };
            if (touches && !ReferenceEquals(EnclosingBlock(node), block))
                return false;
        }
        return true;
    }

    static Block? EnclosingBlock(IrNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is Block block)
                return block;
        }
        return null;
    }

    static List<IrNode> LoadsOf(IrNode statement, PlaceKind kind, int index)
        => [.. statement.Descendants.Prepend(statement).Where(n => kind switch
        {
            PlaceKind.Slot => n is LoadStackSlot s && s.Slot == index,
            PlaceKind.Local => n is LoadLocal l && l.Index == index,
            _ => false,
        })];

    static bool Writes(IrNode statement, PlaceKind kind, int index)
        => statement.Descendants.Prepend(statement).Any(n => kind switch
        {
            PlaceKind.Slot => n is StoreStackSlot s && s.Slot == index,
            PlaceKind.Local => n is StoreLocal l && l.Index == index,
            PlaceKind.Argument => n is StoreArgument a && a.Index == index,
            _ => false,
        });

    static bool WritesStaticDelegateTarget(IrNode statement, IrExpression value)
        => value is DelegateCreation { Target: LoadField { Instance: null, Field: var field } }
            && statement.Descendants.Prepend(statement)
                .OfType<StoreField>()
                .Any(store => !store.HasInstance && store.Field.Equals(field));

    static bool ReadsStaticDelegateTarget(IrExpression value)
        => value is DelegateCreation { Target: LoadField { Instance: null } };

    static bool RequiresFirstEvaluation(IrExpression value)
        => value is DelegateCreation { Target: LoadField { Instance: null }, Method: var method }
            && !GeneratedCodeIdentity.IsNonCapturingLambdaMethod(method);

    static bool HasObservableEffect(IrNode statement)
        => statement.Descendants.Prepend(statement).Any(static node => node is
            Call or CallIndirect or NewObject or DelegateCreation or StoreField or StoreProperty or StoreElement or Throw);

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

    static bool IsInsideCatchFilter(IrNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is CatchClause clause)
                return clause.Filter is { } filter && ReferenceOwnership.IsInside(node, filter);
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
