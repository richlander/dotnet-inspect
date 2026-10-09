namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Retires identity-sensitive synthetic stack slots that are exact aliases of
/// an instance receiver.
///
/// <para>C# does not permit rebinding <c>this</c>. Replacing every use of a
/// proven alias with the same receiver binder therefore preserves identity
/// without introducing synthetic storage. For a value-type receiver, IL
/// represents <c>this</c> as a managed pointer, so the rewrite additionally
/// prevents a spill such as <c>S_0 = this</c> from later materializing as a
/// struct copy.</para>
///
/// <para>The rewrite is limited to the root function scope, rejects any receiver
/// write or address escape, and requires every store to the slot to carry that
/// exact receiver binder. Calls may mutate the receiver's target, but both the
/// slot alias and direct receiver still name that same target; only rebinding
/// argument zero would distinguish them, and that is the explicit decline
/// boundary. Admission is deliberately limited to value-type receivers and
/// generic declaring-type receivers whose open definition cannot be spelled as
/// reference storage; ordinary reference receivers remain with normal storage
/// planning.</para>
/// </summary>
public sealed class ReceiverAliasPass : IIrPass
{
    public string Name => "receiver-alias";

    public void Run(IrFunction function, PassContext context)
    {
        if (function.ReceiverParameter is not { } receiver
            || !IsIdentitySensitiveReceiver(function, receiver.Type)
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
                    || !type.Equals(receiver.Type)))
            {
                continue;
            }

            foreach (var load in slotLoads)
            {
                context.Stepper.StepOver(
                    $"replace receiver alias slot {slot} load with this",
                    load);
                load.ReplaceWith(new LoadArgument(0, receiver));
            }
            foreach (var store in slotStores)
            {
                context.Stepper.StepOver(
                    $"retire receiver alias slot {slot} store",
                    store);
                store.Detach();
            }
            return true;
        }
        return false;
    }

    static bool IsIdentitySensitiveReceiver(
        IrFunction function,
        TypeRef receiverType)
        => MemberIdentity.IsCoreLibraryType(function.BaseType, "System", "ValueType")
            || MemberIdentity.IsCoreLibraryType(function.BaseType, "System", "Enum")
            || !function.DeclaringTypeGenericParameterNames.IsEmpty
                && !CSharpSpellability.CanSpellNamedReferenceStorageType(
                    receiverType,
                    function);
}
