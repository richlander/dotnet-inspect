namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Retires function-scope stack-slot components with no surviving observer.
/// Bare constants and loads disappear; every other producer stays at the same
/// statement position as an explicitly target-typed evaluated discard.
/// Managed-reference producers, unspellable target types, nested bodies, and
/// any component with an external observer remain for storage planning.
/// </summary>
public sealed class ProducerOnlySlotRetirementPass : IIrPass
{
    public string Name => "producer-only-slot-retirement";

    public void Run(IrFunction function, PassContext context)
    {
        var stores = new Dictionary<int, List<StoreStackSlot>>();
        var loads = new Dictionary<int, List<LoadStackSlot>>();
        foreach (IrNode node in CoercionSinks.ScopeNodes(function.Body))
        {
            if (node is StoreStackSlot store)
                (stores.TryGetValue(store.Slot, out var ss) ? ss : stores[store.Slot] = []).Add(store);
            else if (node is LoadStackSlot load)
                (loads.TryGetValue(load.Slot, out var ls) ? ls : loads[load.Slot] = []).Add(load);
        }

        var retiring = stores
            .Where(pair => pair.Value.All(store => CanRetireStore(store, function)))
            .Select(static pair => pair.Key)
            .ToHashSet();

        bool changed;
        do
        {
            changed = false;
            foreach (int slot in retiring.ToArray())
            {
                if ((loads.GetValueOrDefault(slot) ?? [])
                    .Any(load => !IsCopyIntoRetiringSlot(load, retiring)))
                {
                    retiring.Remove(slot);
                    changed = true;
                }
            }
        }
        while (changed);

        if (retiring.Count == 0)
            return;

        var retiredStores = CoercionSinks.ScopeNodes(function.Body)
            .OfType<StoreStackSlot>()
            .Where(store => retiring.Contains(store.Slot))
            .ToArray();
        foreach (StoreStackSlot store in retiredStores)
        {
            if (CanDropWithoutEvaluation(store.Value))
            {
                context.Stepper.StepOver(
                    $"retire producer-only slot {store.Slot} pure store",
                    store);
                store.Detach();
                continue;
            }

            TypeRef target = store.Value.AssignmentType
                ?? throw new InvalidOperationException(
                    $"Producer-only slot {store.Slot} lost its discard target testimony.");
            var value = (IrExpression)store.DetachChildren()[0];
            var discard = new ExpressionStatement(value, target);
            discard.InheritSourceOffset(store);
            context.Stepper.StepOver(
                $"retire producer-only slot {store.Slot} store as typed discard",
                store);
            store.ReplaceWith(discard);
        }

        if (IrInvariants.Enabled)
        {
            IrNode? residual = CoercionSinks.ScopeNodes(function.Body)
                .FirstOrDefault(node => node switch
                {
                    StoreStackSlot store => retiring.Contains(store.Slot),
                    LoadStackSlot load => retiring.Contains(load.Slot),
                    _ => false,
                });
            if (residual is not null)
            {
                throw new InvalidOperationException(
                    $"Producer-only slot retirement left {residual.Describe()} in a retired component.");
            }
        }
    }

    static bool CanRetireStore(
        StoreStackSlot store,
        IrFunction function)
    {
        if (store.Parent is not Block)
            return false;
        if (store.Value.ResultType?.Kind == TypeRefKind.ByRef
            || store.Value.AssignmentType?.Kind == TypeRefKind.ByRef)
            return false;
        if (CanDropWithoutEvaluation(store.Value))
            return true;
        return store.Value.AssignmentType is { } type
            && type is not { Namespace: "System", Name: "Void" }
            && CSharpSpellability.CanSpellExplicitParameterType(
                type,
                function,
                ArgumentRefKind.Value);
    }

    static bool CanDropWithoutEvaluation(IrExpression expression)
        => expression is Constant
            or LoadArgument
            or LoadLocal
            or LoadStackSlot;

    static bool IsCopyIntoRetiringSlot(
        LoadStackSlot load,
        HashSet<int> retiring)
        => load.Parent is StoreStackSlot store
            && ReferenceEquals(store.Value, load)
            && retiring.Contains(store.Slot);
}
