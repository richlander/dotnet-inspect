using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Post-F2 (#2386) measurement: count the stack-slot webs still present at the
/// late slots-only ExpressionInliningPass boundary, before SlotMaterializationPass
/// turns eligible survivors into typed locals. This is C2/#2209 entry evidence,
/// not a product gate.
/// </summary>
static class SlotResidualCensus
{
    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        (int f2Index, int materializationIndex) = PassIndices();
        var totals = new Totals();
        var residuals = new Dictionary<DeferralClass, (long Count, string Example)>();
        var vetoes = new Dictionary<SlotMaterializationVeto, (long Count, string Example)>();
        var vetoCombinations = new Dictionary<SlotMaterializationVeto, (long Count, string Example)>();
        var examples = new List<string>();
        bool capped = false;

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                if (totals.Methods >= cap)
                {
                    capped = true;
                    break;
                }
                totals.Methods++;

                SlotSnapshot before;
                SlotSnapshot after;
                SlotSnapshot afterMaterialization;
                IReadOnlyList<SlotMaterializationDecision> decisions;
                try
                {
                    (before, after, afterMaterialization, decisions) = RunToMaterialization(
                        function,
                        IrPasses.Default,
                        f2Index,
                        materializationIndex,
                        method => IrImporter.Import(source, method));
                }
                catch (Exception ex)
                {
                    totals.PassBugs++;
                    if (examples.Count < maxExamples)
                        examples.Add(
                            PassBugDiagnostic.Format(
                                ex, assemblyPath, typeName, methodName,
                                function.Signature, function.MetadataToken));
                    continue;
                }

                totals.BeforeStores += before.StoreCount;
                totals.BeforeLoads += before.LoadCount;
                totals.BeforeSlots += before.Slots.Count;
                totals.AfterStores += after.StoreCount;
                totals.AfterLoads += after.LoadCount;
                totals.AfterSlots += after.Slots.Count;
                totals.AfterMaterializationStores += afterMaterialization.StoreCount;
                totals.AfterMaterializationLoads += afterMaterialization.LoadCount;
                totals.AfterMaterializationSlots += afterMaterialization.Slots.Count;
                if (after.Slots.Count > 0)
                    totals.MethodsWithResidual++;
                if (after.StoreCount < before.StoreCount || after.LoadCount < before.LoadCount)
                    totals.MethodsImproved++;
                if (afterMaterialization.Slots.Count < after.Slots.Count)
                    totals.MethodsMaterialized++;
                if (afterMaterialization.Slots.Count > 0)
                    totals.MethodsWithMaterializationResidual++;

                foreach (var slot in after.Slots.Values)
                {
                    var klass = Classify(slot);
                    var prior = residuals.GetValueOrDefault(klass);
                    string example = prior.Example
                        ?? $"{typeName}::{methodName} S_{slot.Slot} ({slot.Stores.Count} store, {slot.Loads.Count} load)";
                    residuals[klass] = (prior.Count + 1, example);
                }

                foreach (var decision in decisions)
                {
                    if (decision.WillMaterialize)
                    {
                        totals.MaterializedSlotWebs++;
                        continue;
                    }

                    totals.DeferredSlotWebs++;
                    var combination = vetoCombinations.GetValueOrDefault(decision.Vetoes);
                    vetoCombinations[decision.Vetoes] = (
                        combination.Count + 1,
                        combination.Example ?? $"{typeName}::{methodName} S_{decision.Slot}");
                    foreach (var veto in Enum.GetValues<SlotMaterializationVeto>())
                    {
                        if (veto == SlotMaterializationVeto.None || !decision.Vetoes.HasFlag(veto))
                            continue;
                        var prior = vetoes.GetValueOrDefault(veto);
                        string example = prior.Example ?? $"{typeName}::{methodName} S_{decision.Slot}";
                        vetoes[veto] = (prior.Count + 1, example);
                    }
                }
            }
            if (capped)
                break;
        }

        string scope = capped ? $"{totals.Methods} methods (capped)" : $"{totals.Methods} methods";
        Console.WriteLine();
        Console.WriteLine($"F2 SLOT RESIDUAL CENSUS over {scope} ({totals.PassBugs} pass bugs)");
        Console.WriteLine();
        Console.WriteLine("| Metric | Before late F2 | After late F2 | After materialization | F2 delta | Materialization delta |");
        Console.WriteLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        Row("StoreStackSlot nodes", totals.BeforeStores, totals.AfterStores, totals.AfterMaterializationStores);
        Row("LoadStackSlot nodes", totals.BeforeLoads, totals.AfterLoads, totals.AfterMaterializationLoads);
        Row("Distinct stack slots", totals.BeforeSlots, totals.AfterSlots, totals.AfterMaterializationSlots);
        Console.WriteLine();
        Console.WriteLine($"Methods with F2 removals: {totals.MethodsImproved}");
        Console.WriteLine($"Methods with post-F2 residual slots: {totals.MethodsWithResidual}");
        Console.WriteLine($"Methods with materialized slots: {totals.MethodsMaterialized}");
        Console.WriteLine($"Methods with post-materialization residual slots: {totals.MethodsWithMaterializationResidual}");
        Console.WriteLine();
        Console.WriteLine("Post-F2 residual deferral classes:");
        if (residuals.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var entry in residuals.OrderByDescending(e => e.Value.Count).ThenBy(e => e.Key.ToString(), StringComparer.Ordinal))
                Console.WriteLine($"  {entry.Value.Count,8}  {Label(entry.Key),-28}  e.g. {entry.Value.Example}");
        }

        Console.WriteLine();
        Console.WriteLine("Slot materialization decisions:");
        Console.WriteLine($"  {totals.MaterializedSlotWebs,8}  materialized");
        Console.WriteLine($"  {totals.DeferredSlotWebs,8}  deferred");
        Console.WriteLine();
        Console.WriteLine("Materialization veto attribution (overlapping; one slot web may have multiple vetoes):");
        if (vetoes.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var entry in vetoes.OrderByDescending(e => e.Value.Count).ThenBy(e => e.Key.ToString(), StringComparer.Ordinal))
                Console.WriteLine($"  {entry.Value.Count,8}  {VetoLabel(entry.Key),-32}  e.g. {entry.Value.Example}");
        }

        Console.WriteLine();
        Console.WriteLine("Materialization veto combinations (exact; one row per deferred slot web):");
        if (vetoCombinations.Count == 0)
        {
            Console.WriteLine("  (none)");
        }
        else
        {
            foreach (var entry in vetoCombinations
                .OrderByDescending(e => e.Value.Count)
                .ThenBy(e => VetoCombinationLabel(e.Key), StringComparer.Ordinal))
                Console.WriteLine($"  {entry.Value.Count,8}  {VetoCombinationLabel(entry.Key)}  e.g. {entry.Value.Example}");
        }

        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in examples)
                Console.WriteLine($"  {example}");
        }

        return totals.PassBugs > 0 ? 1 : 0;

        void Row(string label, long before, long afterF2, long afterMaterialization)
            => Console.WriteLine(
                $"| {label} | {before} | {afterF2} | {afterMaterialization} | "
                + $"{afterF2 - before:+#;-#;0} | {afterMaterialization - afterF2:+#;-#;0} |");
    }

    static (int F2, int Materialization) PassIndices()
    {
        var passes = IrPasses.Default;
        int materialization = -1;
        for (int i = 0; i < passes.Length; i++)
            if (passes[i] is SlotMaterializationPass)
            {
                materialization = i;
                break;
            }
        for (int i = materialization - 1; i >= 0; i--)
            if (passes[i] is ExpressionInliningPass)
                return (i, materialization);
        throw new InvalidOperationException("Could not find late F2 ExpressionInliningPass before SlotMaterializationPass.");
    }

    static (SlotSnapshot Before, SlotSnapshot AfterF2, SlotSnapshot AfterMaterialization, IReadOnlyList<SlotMaterializationDecision> Decisions) RunToMaterialization(
        IrFunction function,
        ImmutableArray<IIrPass> passes,
        int f2Index,
        int materializationIndex,
        Func<MethodRef, IrFunction?> importMethodBody)
    {
        var context = PassContext.ForImport(importMethodBody);
        SlotSnapshot? before = null;
        SlotSnapshot? afterF2 = null;
        for (int i = 0; i < passes.Length; i++)
        {
            if (i == f2Index)
            {
                before = SlotSnapshot.Capture(function);
                passes[i].Run(function, context);
                function.CheckInvariant();
                afterF2 = SlotSnapshot.Capture(function);
                continue;
            }
            if (i == materializationIndex)
            {
                if (before is null || afterF2 is null)
                    throw new InvalidOperationException("Slot materialization was reached before the late F2 measurement.");

                var decisions = SlotMaterializationPass.Analyze(function);
                var decisionWebs = new HashSet<SlotWebIdentity>(SlotWebIdentityComparer.Instance);
                foreach (var decision in decisions)
                {
                    if (!decisionWebs.Add(new(decision.Scope, decision.Slot)))
                        throw new InvalidOperationException(
                            $"Slot materialization issued duplicate decisions for scope {decision.Scope.GetType().Name} slot {decision.Slot}.");
                }
                if (!decisionWebs.SetEquals(afterF2.Slots.Keys))
                    throw new InvalidOperationException(
                        $"Slot materialization decisions do not identify the exact {afterF2.Slots.Count} post-F2 slot webs.");

                passes[i].Run(function, context);
                function.CheckInvariant();
                var afterMaterialization = SlotSnapshot.Capture(function);
                var deferredWebs = decisions
                    .Where(static decision => !decision.WillMaterialize)
                    .Select(static decision => new SlotWebIdentity(decision.Scope, decision.Slot))
                    .ToHashSet(SlotWebIdentityComparer.Instance);
                if (!deferredWebs.SetEquals(afterMaterialization.Slots.Keys))
                    throw new InvalidOperationException(
                        $"Slot materialization decisions do not identify the exact {afterMaterialization.Slots.Count} retained slot webs.");
                return (before, afterF2, afterMaterialization, decisions);
            }
            passes[i].Run(function, context);
            function.CheckInvariant();
        }
        throw new InvalidOperationException("Slot materialization pass was not reached.");
    }

    static DeferralClass Classify(SlotWeb slot)
    {
        if (slot.IsInsideNestedFunction)
            return DeferralClass.NestedScope;
        if (slot.Stores.Count == 0)
            return DeferralClass.LoadOnly;
        if (slot.Loads.Count == 0)
            return DeferralClass.StoreOnly;
        if (slot.Stores.Count > 1)
            return DeferralClass.MultiDefinitionOrMerged;
        if (slot.Loads.Count > 1)
            return DeferralClass.MultiUse;
        if (slot.Stores[0].Block is null || slot.Loads[0].Block is null || !ReferenceEquals(slot.Stores[0].Block, slot.Loads[0].Block))
            return DeferralClass.CrossBlock;
        if (slot.Loads[0].Node.Parent is IncrementDecrement)
            return DeferralClass.IncrementTarget;
        return DeferralClass.EffectOrOrderInterleaved;
    }

    static string Label(DeferralClass klass) => klass switch
    {
        DeferralClass.MultiDefinitionOrMerged => "multi-def/merged",
        DeferralClass.MultiUse => "multi-use",
        DeferralClass.CrossBlock => "cross-block",
        DeferralClass.EffectOrOrderInterleaved => "effect/order-interleaved",
        DeferralClass.NestedScope => "nested-scope",
        DeferralClass.StoreOnly => "store-only",
        DeferralClass.LoadOnly => "load-only",
        DeferralClass.IncrementTarget => "increment-target",
        _ => klass.ToString(),
    };

    static string VetoLabel(SlotMaterializationVeto veto) => veto switch
    {
        SlotMaterializationVeto.NestedScope => "nested body scope",
        SlotMaterializationVeto.MissingStore => "missing store",
        SlotMaterializationVeto.MissingLoad => "missing load",
        SlotMaterializationVeto.UnderivableTypeTestimony => "underivable type testimony",
        SlotMaterializationVeto.ConflictingTypeTestimony => "conflicting type testimony",
        SlotMaterializationVeto.NestedSlotNumberCollision => "nested slot-number collision",
        SlotMaterializationVeto.OutsideCoercionDomain => "outside coercion domain",
        SlotMaterializationVeto.UnrenderableStoreType => "cross-family/unrenderable store",
        SlotMaterializationVeto.MultiStoreSingleLoadFold => "multi-store/single-load fold",
        SlotMaterializationVeto.CrossBlockStoreFold => "cross-block store fold",
        SlotMaterializationVeto.BooleanSinkIdentityRecovery => "boolean sink identity recovery",
        SlotMaterializationVeto.ElementStoreIdentityRecovery => "element-store identity recovery",
        SlotMaterializationVeto.IncompleteCopyComponent => "incomplete direct-copy component",
        _ => veto.ToString(),
    };

    static string VetoCombinationLabel(SlotMaterializationVeto vetoes)
        => string.Join(
            " + ",
            Enum.GetValues<SlotMaterializationVeto>()
                .Where(veto => veto != SlotMaterializationVeto.None && vetoes.HasFlag(veto))
                .Select(VetoLabel));

    sealed class Totals
    {
        public long Methods;
        public long PassBugs;
        public long BeforeStores;
        public long BeforeLoads;
        public long BeforeSlots;
        public long AfterStores;
        public long AfterLoads;
        public long AfterSlots;
        public long AfterMaterializationStores;
        public long AfterMaterializationLoads;
        public long AfterMaterializationSlots;
        public long MethodsImproved;
        public long MethodsWithResidual;
        public long MethodsMaterialized;
        public long MethodsWithMaterializationResidual;
        public long MaterializedSlotWebs;
        public long DeferredSlotWebs;
    }

    enum DeferralClass
    {
        MultiDefinitionOrMerged,
        MultiUse,
        CrossBlock,
        EffectOrOrderInterleaved,
        NestedScope,
        StoreOnly,
        LoadOnly,
        IncrementTarget,
    }

    sealed record SlotSnapshot(IReadOnlyDictionary<SlotWebIdentity, SlotWeb> Slots)
    {
        public long StoreCount => Slots.Values.Sum(slot => slot.Stores.Count);
        public long LoadCount => Slots.Values.Sum(slot => slot.Loads.Count);

        public static SlotSnapshot Capture(IrFunction function)
        {
            var slots = new Dictionary<SlotWebIdentity, SlotWeb>(SlotWebIdentityComparer.Instance);
            SlotWeb Slot(IrNode node, int index)
            {
                var scope = SlotScope(function, node);
                var identity = new SlotWebIdentity(scope, index);
                if (!slots.TryGetValue(identity, out var slot))
                    slots[identity] = slot = new SlotWeb(index, scope != function);
                return slot;
            }

            foreach (var node in function.Descendants)
            {
                switch (node)
                {
                    case StoreStackSlot store:
                        Slot(store, store.Slot).Stores.Add(new SlotSite(store, EnclosingBlock(store)));
                        break;
                    case LoadStackSlot load:
                        Slot(load, load.Slot).Loads.Add(new SlotSite(load, EnclosingBlock(load)));
                        break;
                }
            }
            return new SlotSnapshot(slots);
        }
    }

    sealed class SlotWeb(int slot, bool isInsideNestedFunction)
    {
        public int Slot { get; } = slot;
        public List<SlotSite> Stores { get; } = [];
        public List<SlotSite> Loads { get; } = [];
        public bool IsInsideNestedFunction { get; } = isInsideNestedFunction;
    }

    readonly record struct SlotWebIdentity(IrNode Scope, int Slot);

    sealed class SlotWebIdentityComparer : IEqualityComparer<SlotWebIdentity>
    {
        public static SlotWebIdentityComparer Instance { get; } = new();

        public bool Equals(SlotWebIdentity x, SlotWebIdentity y)
            => ReferenceEquals(x.Scope, y.Scope) && x.Slot == y.Slot;

        public int GetHashCode(SlotWebIdentity obj)
            => HashCode.Combine(RuntimeHelpers.GetHashCode(obj.Scope), obj.Slot);
    }

    sealed record SlotSite(IrNode Node, Block? Block);

    static Block? EnclosingBlock(IrNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (current is Block block)
                return block;
        return null;
    }

    static IrNode SlotScope(IrFunction function, IrNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (current is Lambda or LocalFunctionStatement)
                return current;
        return function;
    }
}
