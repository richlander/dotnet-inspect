namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Canonicalizes the receiver of a constructor-chain call (<c>this..ctor</c>
/// or <c>base..ctor</c>) back to <c>this</c>. When the base-call argument
/// carries control flow — the ubiquitous <c>base(message ?? SR.Default)</c>
/// exception shape — the importer spills the loaded <c>this</c> across the
/// branch into a slot, and the inliner leaves it there: <c>this</c> is held
/// impure because <see cref="TypeRef"/> cannot yet prove the receiver is a
/// class rather than a mutable byref struct.
///
/// But the receiver of a constructor's own base/this call is the object under
/// construction, an immutable reference for the whole body — naming it
/// <c>this</c> at the call site is always sound regardless of intervening
/// computation. This pass makes that one safe move the general inliner
/// declines, then drops the now-dead spill so the printer can render
/// <c>base(args);</c> / <c>this(args);</c> instead of <c>S_0..ctor(args);</c>.
/// </summary>
public sealed class ConstructorChainPass : IIrPass
{
    public string Name => "constructor-chain";

    public void Run(IrFunction function, PassContext context)
    {
        if (!function.Signature.HasThis)
            return;

        foreach (var call in function.Descendants.OfType<Call>().ToList())
        {
            if (call.Callee.Name != ".ctor" || !call.Callee.HasThis || call.Arguments.Count == 0)
                continue;
            var receiver = call.Arguments[0];
            if (receiver is LoadArgument { Index: 0 })
                continue;  // already canonical
            if (ResolveThisSpill(function, receiver) is not { } spill)
                continue;

            var thisArgument = new LoadArgument(0, "this", function.DeclaringType);
            context.Stepper.StepOver($"canonicalize {call.Callee.DeclaringType.Name}..ctor receiver to this", call);
            receiver.ReplaceWith(thisArgument);
            // The spill's sole load is gone; drop the store(s) so they do not
            // print as dead `T S_0 = this;` declarations.
            foreach (var store in spill.Stores)
                store.Detach();
        }
    }

    /// <summary>
    /// A receiver that is a slot or local with exactly one load (this call)
    /// and store(s) that all carry <c>this</c>; null when it is anything else.
    /// Branch-shaped constructor arguments can copy an earlier <c>this</c> spill
    /// into the chain receiver slot on each arm, so one level of copy is accepted
    /// when every source load is inside a receiver store that will be removed.
    /// </summary>
    static (IReadOnlyList<IrNode> Stores, IrExpression Load)? ResolveThisSpill(IrFunction function, IrExpression receiver)
    {
        var key = PlaceKey(receiver);
        if (key is not { } place)
            return null;

        var usage = CountUsage(function, place);
        if (usage.AddressTaken || usage.Loads.Count != 1 || !ReferenceEquals(usage.Loads[0], receiver)
            || usage.Stores.Count == 0)
            return null;

        if (usage.Stores.All(StoreIsThis))
            return (usage.Stores, receiver);

        if (TryResolveCopiedThis(function, usage.Stores, out var sourceStore))
            return (usage.Stores.Append(sourceStore).Distinct().ToList(), receiver);

        return null;
    }

    static bool TryResolveCopiedThis(IrFunction function, IReadOnlyList<IrNode> receiverStores, out IrNode sourceStore)
    {
        sourceStore = null!;
        (bool IsSlot, int Index)? sourceKey = null;
        foreach (var store in receiverStores)
        {
            var valueKey = PlaceKey(StoreValue(store));
            if (valueKey is null || sourceKey is { } existing && existing != valueKey)
                return false;
            sourceKey = valueKey;
        }
        if (sourceKey is not { } place)
            return false;

        var source = CountUsage(function, place);
        if (source.AddressTaken || source.Stores.Count != 1 || !StoreIsThis(source.Stores[0]))
            return false;

        foreach (var load in source.Loads)
            if (!receiverStores.Any(store => ReferenceOwnership.IsInside(load, store)))
                return false;

        sourceStore = source.Stores[0];
        return true;
    }

    static (bool IsSlot, int Index)? PlaceKey(IrExpression expression) => expression switch
    {
        LoadStackSlot slot => (true, slot.Slot),
        LoadLocal local => (false, local.Index),
        _ => null,
    };

    static IrExpression StoreValue(IrNode store) => store switch
    {
        StoreStackSlot slot => slot.Value,
        StoreLocal local => local.Value,
        _ => throw new InvalidOperationException("Expected a stack slot or local store."),
    };

    static bool StoreIsThis(IrNode store) => StoreValue(store) is LoadArgument { Index: 0 };
    static Place CountUsage(IrFunction function, (bool IsSlot, int Index) place)
    {
        var result = new Place();
        foreach (var node in function.Descendants)
        {
            if (place.IsSlot)
            {
                if (node is LoadStackSlot l && l.Slot == place.Index)
                    result.Loads.Add(l);
                else if (node is StoreStackSlot s && s.Slot == place.Index)
                    result.Stores.Add(s);
            }
            else
            {
                if (node is LoadLocal l && l.Index == place.Index)
                    result.Loads.Add(l);
                else if (node is LoadLocalAddress a && a.Index == place.Index)
                    result.AddressTaken = true;
                else if (node is StoreLocal s && s.Index == place.Index)
                    result.Stores.Add(s);
            }
        }
        return result;
    }

    sealed class Place
    {
        public List<IrExpression> Loads { get; } = [];
        public List<IrNode> Stores { get; } = [];
        public bool AddressTaken { get; set; }
    }
}
