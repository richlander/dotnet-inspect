namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Retires synthetic stack slots that are exact aliases of a value-type
/// instance receiver.
///
/// <para>IL represents value-type <c>this</c> as a managed pointer. The IR
/// receiver binder intentionally carries the declared type used by C# emission,
/// so a spill such as <c>S_0 = this</c> otherwise looks like value storage and
/// can later materialize as a struct copy. Replacing every use of a proven alias
/// with the same receiver binder preserves the managed-reference identity
/// without introducing a synthetic <c>ref</c> local.</para>
///
/// <para>The rewrite is limited to the root function scope, requires metadata
/// proof that the declaring type is a value type, rejects any receiver binding
/// write or address escape, and requires every store to the slot to carry that
/// exact receiver binder. Calls may mutate the receiver's target, but both the
/// slot alias and direct receiver still name that same target; only rebinding
/// argument zero would distinguish them, and that is the explicit decline
/// boundary.</para>
/// </summary>
public sealed class ValueTypeReceiverAliasPass : IIrPass
{
    public string Name => "value-type-receiver-alias";

    public void Run(IrFunction function, PassContext context)
    {
        if (function.ReceiverParameter is not { } receiver
            || !IsKnownValueTypeReceiver(function)
            || SpilledReceiverFold.OrderSensitiveArgumentsInScope(function.Body).Contains(0))
        {
            return;
        }

        while (TryRetireOne(function, receiver, context))
        {
        }
    }

    static bool TryRetireOne(
        IrFunction function,
        Parameter receiver,
        PassContext context)
    {
        var stores = new Dictionary<int, List<StoreStackSlot>>();
        var loads = new Dictionary<int, List<LoadStackSlot>>();
        foreach (var node in CoercionSinks.ScopeNodes(function.Body))
        {
            if (node is StoreStackSlot store)
                (stores.TryGetValue(store.Slot, out var ss) ? ss : stores[store.Slot] = []).Add(store);
            else if (node is LoadStackSlot load)
                (loads.TryGetValue(load.Slot, out var ls) ? ls : loads[load.Slot] = []).Add(load);
        }

        foreach (var (slot, slotStores) in stores)
        {
            if (slotStores.Count == 0
                || !loads.TryGetValue(slot, out var slotLoads)
                || slotLoads.Count == 0
                || slotStores.Any(store => store.Parent is not Block
                    || store.Value is not LoadArgument
                    {
                        Index: 0,
                        Parameter: { } parameter,
                    }
                    || !ReferenceEquals(parameter, receiver))
                || slotLoads.Any(load => load.ResultType is not { } type
                    || !type.Equals(function.DeclaringType)))
            {
                continue;
            }

            foreach (var load in slotLoads)
            {
                context.Stepper.StepOver(
                    $"replace value-type receiver alias slot {slot} load with this",
                    load);
                load.ReplaceWith(new LoadArgument(0, receiver));
            }
            foreach (var store in slotStores)
            {
                context.Stepper.StepOver(
                    $"retire value-type receiver alias slot {slot} store",
                    store);
                store.Detach();
            }
            return true;
        }
        return false;
    }

    static bool IsKnownValueTypeReceiver(IrFunction function)
        => MemberIdentity.IsCoreLibraryType(function.BaseType, "System", "ValueType")
            || MemberIdentity.IsCoreLibraryType(function.BaseType, "System", "Enum");
}
