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
        var slotTypes = CoercionSinks.TestifiedSlotTypes(function.Body, function.Signature.ReturnType, function.TypeShapes);
        if (slotTypes.Count == 0)
            return;

        var stores = new Dictionary<int, List<StoreStackSlot>>();
        var loads = new Dictionary<int, List<LoadStackSlot>>();
        var nestedSlots = new HashSet<int>();
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
                case Lambda or LocalFunctionStatement:
                    foreach (var inner in node.Descendants)
                    {
                        if (inner is StoreStackSlot innerStore)
                            nestedSlots.Add(innerStore.Slot);
                        else if (inner is LoadStackSlot innerLoad)
                            nestedSlots.Add(innerLoad.Slot);
                    }
                    break;
            }
        }

        var decided = new List<(int Slot, TypeRef Type, List<StoreStackSlot> Stores, List<LoadStackSlot> Loads)>();
        foreach (var (slot, slotType) in slotTypes)
        {
            // Slot numbering restarts at 0 inside each nested body, and a
            // raised lambda prints through a fresh inner printer with no view
            // of the outer locals table — a residual inner `S_0` next to a
            // materialized outer `S_0` is CS0136 (review: HIGH). A slot whose
            // number also appears in any nested body stays on the unifier
            // until nested scopes materialize too.
            if (nestedSlots.Contains(slot))
                continue;
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
            // A slot loaded into an array-element store whose semantic element
            // type disagrees with the testified type stays deferred: the
            // printer RE-TYPES such slots (the #1751 char identity recovery —
            // `chars[i] = intSlot` becomes `char S_2 = '+' : '-'`, because
            // char's storage width is never its semantic type), and a
            // materialized int local forecloses that. Coerce is the wrong tool
            // here — the recovery changes the slot's type, not the value's
            // spelling (round-2 review: CharElementStorePrinterTests).
            if (slotLoads.Any(load => load.Parent is StoreElement element
                    && ReferenceEquals(element.Value, load)
                    && CoercionSinks.StoreElementTarget(element, function.TypeShapes) is { } elementTarget
                    && !elementTarget.Equals(slotType)))
                continue;
            // A slot whose store copies from another slot stays deferred:
            // materializing one end of a slot-to-slot copy defeats the
            // printer's copy folding (`switch ((uint)S_256)` gained an
            // intermediate `long S_0 = S_256;`). It materializes when its
            // source does — a later increment's coupled-component walk.
            if (slotStores.Any(store => store.Value is LoadStackSlot))
                continue;

            decided.Add((slot, slotType, slotStores, slotLoads));
        }

        // Two-phase rewrite (review: CRITICAL clone-orphaning): replace ALL
        // loads across ALL decided slots first — in-place subtree mutation
        // keeps the collected store references valid — THEN rewrite stores.
        // A store's Clone() thereby copies already-materialized LoadLocals;
        // cloning one slot's store before another slot's nested load was
        // processed injected a fresh LoadStackSlot into the live tree while
        // the pass replaced the original inside the discarded subtree,
        // orphaning the live one forever.
        var indices = new Dictionary<int, int>();
        foreach (var (slot, slotType, _, _) in decided)
            indices[slot] = function.AddLocal(slotType, $"S_{slot}");
        foreach (var (slot, slotType, _, slotLoads) in decided)
        {
            foreach (var load in slotLoads)
            {
                context.Stepper.StepOver($"materialize slot {slot} load as local {indices[slot]}", load);
                load.ReplaceWith(new LoadLocal(indices[slot], slotType));
            }
        }
        foreach (var (slot, slotType, slotStores, _) in decided)
        {
            foreach (var store in slotStores)
            {
                context.Stepper.StepOver($"materialize slot {slot} store as local {indices[slot]}", store);
                store.ReplaceWith(new StoreLocal(indices[slot], slotType, (IrExpression)store.Value.Clone()));
            }
        }
    }
}
