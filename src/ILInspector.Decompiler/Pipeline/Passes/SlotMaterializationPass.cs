namespace ILInspector.Decompiler.Pipeline;

[Flags]
public enum SlotMaterializationVeto
{
    None = 0,
    NestedScope = 1 << 0,
    MissingStore = 1 << 1,
    MissingLoad = 1 << 2,
    UnderivableTypeTestimony = 1 << 3,
    ConflictingTypeTestimony = 1 << 4,
    OutsideCoercionDomain = 1 << 6,
    UnrenderableStoreType = 1 << 7,
    BooleanSinkIdentityRecovery = 1 << 10,
    ElementStoreIdentityRecovery = 1 << 11,
    IncompleteCopyComponent = 1 << 12,
    PendingStorageSwap = 1 << 13,
}

public readonly record struct SlotMaterializationDecision(
    IrNode Scope,
    int Slot,
    TypeRef? Type,
    SlotMaterializationVeto Vetoes)
{
    public bool WillMaterialize => Vetoes == SlotMaterializationVeto.None;
}

/// <summary>
/// Materializes decided synthetic stack slots as typed locals
/// (value-typed-emission.md, slice 5b-2; the #2209 trajectory commitment).
/// A slot whose loads testify to one type and whose stores are all at that
/// type — which coercion insertion guarantees for the wrappable population —
/// is a fully decided variable: it becomes a real local via
/// <see cref="IrFunction.AddLocal"/>, keeping its <c>S_{slot}</c> name so slot
/// references read the same — declaration order and form may still change
/// (materialized locals append to the locals table, and a single-store local
/// collapses to an initializer). Managed-reference locals retain their slot
/// provenance because their legacy declaration order and scope remain part
/// of the output contract. Its <see cref="StoreStackSlot"/>/
/// <see cref="LoadStackSlot"/> nodes stop reaching the printer. What remains
/// on slots is the counted residual the printer's unifier still owns:
/// ambiguous testimony, cross-family (true disjoint ranges), and nested-body
/// scopes (this increment materializes function-scope slots only). Direct
/// slot-copy webs retire as one connected component only when every member is
/// independently decided; an undecided member keeps the whole component on
/// slots. The terminus is C2: when the residual census reaches zero, the
/// print-time unifier deletes cleanly. The component boundary is gated by
/// <c>MaterializesCompleteDirectCopyComponent</c> and
/// <c>DefersWholeDirectCopyComponentWhenOneSlotIsUndecided</c>.
/// </summary>
public sealed class SlotMaterializationPass : IIrPass
{
    public string Name => "slot-materialization";

    public static IReadOnlyList<SlotMaterializationDecision> Analyze(IrFunction function)
        => BuildPlan(function).Decisions;

    public void Run(IrFunction function, PassContext context)
    {
        var plan = BuildPlan(function);
        var decided = plan.Candidates.Where(static candidate => candidate.Vetoes == SlotMaterializationVeto.None).ToList();
        var invariant = IrInvariants.Enabled && decided.Count > 0
            ? SlotMaterializationInvariant.Capture(function) : null;

        // Replace every load before moving store values so nested slot loads
        // have already become locals. Reparent each value instead of cloning
        // it: nested function objects are slot-web scope identities shared
        // with the analysis decision.
        var indices = new Dictionary<int, int>();
        foreach (var candidate in decided)
        {
            int index = function.AddSynthesizedLocal(
                candidate.Type!,
                $"S_{candidate.Slot}");
            indices[candidate.Slot] = index;
            if (candidate.Type!.Kind == TypeRefKind.ByRef)
            {
                function.MarkMaterializedStackSlotLocal(
                    index,
                    candidate.Slot,
                    producerOnly: candidate.Loads.Count == 0);
            }
        }
        foreach (var candidate in decided)
        {
            foreach (var load in candidate.Loads)
            {
                context.Stepper.StepOver($"materialize slot {candidate.Slot} load as local {indices[candidate.Slot]}", load);
                load.ReplaceWith(new LoadLocal(
                    indices[candidate.Slot],
                    candidate.Type!));
            }
        }
        foreach (var candidate in decided)
        {
            foreach (var store in candidate.Stores)
            {
                context.Stepper.StepOver($"materialize slot {candidate.Slot} store as local {indices[candidate.Slot]}", store);
                var value = (IrExpression)store.DetachChildren()[0];
                store.ReplaceWith(new StoreLocal(
                    indices[candidate.Slot],
                    candidate.Type!,
                    value));
            }
        }
        invariant?.Check();
    }

    static MaterializationPlan BuildPlan(IrFunction function)
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

        var nestedDecisions = new List<SlotMaterializationDecision>();
        // #2356 made nested generated names collision-free, but recursively
        // materializing each nested body's own locals table is a separate
        // behavior slice. Attribute those webs here instead of hiding them
        // behind the function-scope dictionaries.
        foreach (var nested in function.Descendants.Where(static node => node is Lambda or LocalFunctionStatement))
        {
            var slots = new HashSet<int>();
            foreach (var node in CoercionSinks.ScopeNodes(nested))
            {
                if (node is StoreStackSlot store)
                    slots.Add(store.Slot);
                else if (node is LoadStackSlot load)
                    slots.Add(load.Slot);
            }
            nestedDecisions.AddRange(slots
                .Order()
                .Select(slot => new SlotMaterializationDecision(
                    nested, slot, Type: null, SlotMaterializationVeto.NestedScope)));
        }

        var testimony = CoercionSinks.AnalyzeSlotTypeTestimony(
            function.Body,
            function.Signature.ReturnType,
            function.TypeShapes,
            recoverBooleanIdentity: true,
            recoverElementIdentity: true,
            enumUnderlyingTypes: function.EnumUnderlyingTypes);
        // Materializable slots retain the prior first-testimony order so
        // AddLocal preserves existing local indices and declaration order.
        var candidates = testimony.Keys
            .Concat(stores.Keys)
            .Concat(loads.Keys)
            .Distinct()
            .Select(slot => Candidate(
                function,
                slot,
                stores.GetValueOrDefault(slot) ?? [],
                loads.GetValueOrDefault(slot) ?? []))
            .ToList();

        foreach (var candidate in candidates)
        {
            if (candidate.Stores.Count == 0)
                candidate.Vetoes |= SlotMaterializationVeto.MissingStore;
            if (candidate.Loads.Count == 0)
                candidate.Vetoes |= SlotMaterializationVeto.MissingLoad;

            if (testimony.TryGetValue(candidate.Slot, out var slotTestimony))
            {
                candidate.Type = slotTestimony.Type;
                candidate.Vetoes |= slotTestimony.Status switch
                {
                    CoercionSinks.SlotTypeTestimonyStatus.Underivable
                        => SlotMaterializationVeto.UnderivableTypeTestimony,
                    CoercionSinks.SlotTypeTestimonyStatus.Conflicting
                        => SlotMaterializationVeto.ConflictingTypeTestimony,
                    _ => SlotMaterializationVeto.None,
                };
            }
            else if (candidate.Loads.Count > 0)
            {
                throw new InvalidOperationException($"Slot {candidate.Slot} loads had no testimony decision.");
            }
            else if (UnanimousManagedReferenceStoreType(candidate.Stores) is { } storeType)
            {
                candidate.Type = storeType;
                candidate.Vetoes &= ~SlotMaterializationVeto.MissingLoad;
            }

            if (candidate.Type is not { } slotType)
                continue;

            if (TypeFamilies.IsIntegerLike(slotType)
                && candidate.Loads.Any(load =>
                    CoercionSinks.SemanticLoadSinkTargetType(
                        load,
                        function.Signature.ReturnType,
                        function.TypeShapes) is { } sinkType
                    && TypeFamilies.IsBoolean(sinkType)))
            {
                candidate.Vetoes |= SlotMaterializationVeto.BooleanSinkIdentityRecovery;
            }

            if (!CoercionDomain.InDomain(slotType, function.TypeShapes))
            {
                bool exactStorage = candidate.Stores.All(store => CoercionDomain.IsAtTarget(store.Value, slotType))
                    && (slotType.Kind == TypeRefKind.Definition
                        && (MemberIdentity.IsCoreLibraryType(slotType, "System", "String")
                            || MemberIdentity.IsCoreLibraryType(slotType, "System", "Object"))
                        || CSharpSpellability.CanSpellArrayStorageType(slotType, function)
                        || CSharpSpellability.CanSpellPointerStorageType(slotType, function)
                        || HasCompleteManagedReferenceStorageType(slotType)
                        || CSharpSpellability.CanSpellNamedReferenceStorageType(slotType, function)
                        || CSharpSpellability.CanSpellNamedValueStorageType(slotType, function)
                        || CSharpSpellability.CanSpellGenericParameterStorageType(slotType, function));
                if (!exactStorage)
                    candidate.Vetoes |= SlotMaterializationVeto.OutsideCoercionDomain;
                else if (candidate.Stores.Any(store => SwapIdiomPass.IsPendingStackSwap(function, store)))
                    candidate.Vetoes |= SlotMaterializationVeto.PendingStorageSwap;
            }
            if (candidate.Stores.Any(store => store.Value.ResultType?.Equals(slotType) != true
                    && !CoercionRendering.CanSpellSlotCoercion(
                        store.Value.ResultType, slotType, function.TypeShapes, function.EnumUnderlyingTypes)))
                candidate.Vetoes |= SlotMaterializationVeto.UnrenderableStoreType;
            if (candidate.Loads.Any(load => load.Parent is StoreElement element
                    && ReferenceEquals(element.Value, load)
                    && CoercionSinks.StoreElementTarget(element, function.TypeShapes) is { } elementTarget
                    && !elementTarget.Equals(slotType)))
                candidate.Vetoes |= SlotMaterializationVeto.ElementStoreIdentityRecovery;
        }

        MarkIncompleteCopyComponents(stores, candidates);
        return new MaterializationPlan(
            candidates,
            [.. candidates.Select(static candidate => candidate.Decision), .. nestedDecisions]);

        static MaterializationCandidate Candidate(
            IrNode scope,
            int slot,
            List<StoreStackSlot> slotStores,
            List<LoadStackSlot> slotLoads)
            => new(scope, slot, slotStores, slotLoads);

        static bool HasCompleteManagedReferenceStorageType(TypeRef type)
            => type is
                {
                    Kind: TypeRefKind.ByRef,
                    ElementType:
                    {
                        Kind: not (TypeRefKind.ByRef
                            or TypeRefKind.Pinned
                            or TypeRefKind.Unsupported),
                    } element,
                }
                && !type.ContainsUnsupported
                && !MemberIdentity.IsCoreLibraryType(
                    element,
                    "System",
                    "Void");

        static TypeRef? UnanimousManagedReferenceStoreType(
            IReadOnlyList<StoreStackSlot> stores)
        {
            TypeRef? type = null;
            foreach (var store in stores)
            {
                if (store.Value.ResultType is not { } producerType
                    || !HasCompleteManagedReferenceStorageType(producerType)
                    || type is not null && !type.Equals(producerType))
                {
                    return null;
                }
                type = producerType;
            }
            return type;
        }

        static void MarkIncompleteCopyComponents(
            IReadOnlyDictionary<int, List<StoreStackSlot>> stores,
            IReadOnlyList<MaterializationCandidate> candidates)
        {
            var bySlot = candidates.ToDictionary(static candidate => candidate.Slot);
            var graph = new Dictionary<int, HashSet<int>>();

            HashSet<int> Neighbors(int slot)
                => graph.TryGetValue(slot, out var neighbors)
                    ? neighbors
                    : graph[slot] = [];

            foreach (var (destination, slotStores) in stores)
            {
                foreach (var source in slotStores
                    .Select(store => store.Value)
                    .OfType<LoadStackSlot>())
                {
                    Neighbors(destination).Add(source.Slot);
                    Neighbors(source.Slot).Add(destination);
                }
            }

            var visited = new HashSet<int>();
            foreach (int seed in graph.Keys)
            {
                if (!visited.Add(seed))
                    continue;

                var component = new HashSet<int> { seed };
                var pending = new Stack<int>();
                pending.Push(seed);
                while (pending.TryPop(out int slot))
                {
                    foreach (int neighbor in graph[slot])
                    {
                        if (!visited.Add(neighbor))
                            continue;
                        component.Add(neighbor);
                        pending.Push(neighbor);
                    }
                }

                if (component.Any(slot => bySlot[slot].Vetoes != SlotMaterializationVeto.None))
                {
                    foreach (int slot in component)
                        bySlot[slot].Vetoes |= SlotMaterializationVeto.IncompleteCopyComponent;
                }
            }
        }
    }

    sealed record MaterializationPlan(
        IReadOnlyList<MaterializationCandidate> Candidates,
        IReadOnlyList<SlotMaterializationDecision> Decisions);

    sealed class MaterializationCandidate(
        IrNode scope,
        int slot,
        List<StoreStackSlot> stores,
        List<LoadStackSlot> loads)
    {
        public IrNode Scope { get; } = scope;
        public int Slot { get; } = slot;
        public TypeRef? Type { get; set; }
        public List<StoreStackSlot> Stores { get; } = stores;
        public List<LoadStackSlot> Loads { get; } = loads;
        public SlotMaterializationVeto Vetoes { get; set; }
        public SlotMaterializationDecision Decision => new(Scope, Slot, Type, Vetoes);
    }
}
