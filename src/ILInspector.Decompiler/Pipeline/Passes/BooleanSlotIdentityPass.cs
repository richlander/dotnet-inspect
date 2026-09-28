namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recovers Boolean identity for a complete stack-slot live range whose I4
/// stores are exclusively Boolean values or the canonical zero/one encoding
/// and whose loads are exclusively consumed as Boolean values.
/// </summary>
public sealed class BooleanSlotIdentityPass : IIrPass
{
    static readonly TypeRef Boolean = TypeRef.CoreLib("System", "Boolean");

    public string Name => "boolean-slot-identity";

    public void Run(IrFunction function, PassContext context)
    {
        var nodes = CoercionSinks.ScopeNodes(function.Body).ToList();
        var loads = nodes.OfType<LoadStackSlot>().ToLookup(static load => load.Slot);
        foreach (var stores in nodes.OfType<StoreStackSlot>().GroupBy(static store => store.Slot))
        {
            var slotLoads = loads[stores.Key].ToList();
            if (slotLoads.Count == 0
                || !stores.All(static store => CoercionSinks.IsBooleanSlotStoreValue(store.Value))
                || !slotLoads.All(load => CoercionSinks.BooleanSlotLoadType(
                    load,
                    function.Signature.ReturnType,
                    function.TypeShapes) is not null))
            {
                continue;
            }

            foreach (var store in stores)
                TypedConstantsPass.Retype(store.Value, Boolean, function.TypeShapes, context.Stepper);
        }
    }
}
