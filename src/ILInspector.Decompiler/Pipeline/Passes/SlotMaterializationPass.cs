namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Materializes decided synthetic stack slots as typed locals
/// (value-typed-emission.md, slice 5b-2; the #2209 trajectory commitment).
/// A slot whose loads testify to one type and whose stores are all at that
/// type — which coercion insertion guarantees for the wrappable population —
/// is a fully decided variable: it becomes a real local via
/// <see cref="IrFunction.AddLocal"/>, keeping its <c>S_{slot}</c> name so the
/// rendering is unchanged, and its <see cref="StoreStackSlot"/>/
/// <see cref="LoadStackSlot"/> nodes stop reaching the printer. What remains
/// on slots is the counted residual the printer's unifier still owns:
/// ambiguous testimony, cross-family (true disjoint ranges), and nested-body
/// scopes (this increment materializes function-scope slots only). The
/// terminus is C2: when the residual census reaches zero, the print-time
/// unifier deletes cleanly.
/// </summary>
public sealed class SlotMaterializationPass : IIrPass
{
    public string Name => "slot-materialization";

    public void Run(IrFunction function, PassContext context)
    {
        // Function-scope only this increment: nested Lambda/LocalFunction
        // bodies carry their own locals tables; their slots stay residual.
        var slotTypes = CoercionSinks.TestifiedSlotTypes(function.Body, function.Signature.ReturnType);
        if (slotTypes.Count == 0)
            return;

        var stores = new Dictionary<int, List<StoreStackSlot>>();
        var loads = new Dictionary<int, List<LoadStackSlot>>();
        foreach (var node in CoercionSinks.ScopeNodes(function.Body))
        {
            switch (node)
            {
                case StoreStackSlot store:
                    (stores.TryGetValue(store.Slot, out var ss) ? ss : stores[store.Slot] = []).Add(store);
                    break;
                case LoadStackSlot load:
                    (loads.TryGetValue(load.Slot, out var ls) ? ls : loads[load.Slot] = []).Add(load);
                    break;
            }
        }

        foreach (var (slot, slotType) in slotTypes)
        {
            // The lane's declared domain only (integer/bool/char/resolved
            // enum): ref, generic-param, and reference slots were never
            // instance 2's scope — the first corpus run materialized them and
            // bought 2,531 methods of prologue-declaration churn
            // (`= ref Unsafe.NullRef<…>()` initializers). They stay on the
            // unifier as residual.
            if (!CoercionDomain.InDomain(slotType, function.TypeShapes))
                continue;
            var slotStores = stores.GetValueOrDefault(slot);
            var slotLoads = loads.GetValueOrDefault(slot);
            if (slotStores is null || slotLoads is null)
                continue;
            // Every store must be at the decided type or renderably coercible
            // to it — the pass runs BEFORE coercion insertion (so the minted
            // LoadLocals get coerced at their sinks like any local; the
            // assertion diff caught the after-ordering leaving them bare), so
            // "will be wrapped" is the guard, not "was wrapped". A store
            // outside the renderable domain — the PrinterOwned residual —
            // keeps the whole slot on the unifier: materializing half a slot
            // recreates the severed-range class the 5b reviews taught.
            if (!slotStores.All(store => store.Value.ResultType?.Equals(slotType) == true
                    || CoercionRendering.CanSpellSlotCoercion(
                        store.Value.ResultType, slotType, function.TypeShapes, function.EnumUnderlyingTypes)))
                continue;
            // A slot whose store copies from another slot stays deferred:
            // materializing one end of a slot-to-slot copy defeats the
            // printer's copy folding (`switch ((uint)S_256)` gained an
            // intermediate `long S_0 = S_256;`). It materializes when its
            // source does — a later increment's coupled-component walk.
            if (slotStores.Any(store => store.Value is LoadStackSlot))
                continue;

            int index = function.AddLocal(slotType, $"S_{slot}");
            foreach (var store in slotStores)
            {
                context.Stepper.StepOver($"materialize slot {slot} store as local {index}", store);
                store.ReplaceWith(new StoreLocal(index, slotType, (IrExpression)store.Value.Clone()));
            }
            foreach (var load in slotLoads)
            {
                context.Stepper.StepOver($"materialize slot {slot} load as local {index}", load);
                load.ReplaceWith(new LoadLocal(index, slotType));
            }
        }
    }
}
