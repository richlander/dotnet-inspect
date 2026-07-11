using System.Collections.Immutable;

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
        int f2Index = F2PassIndex();
        var totals = new Totals();
        var residuals = new Dictionary<DeferralClass, (long Count, string Example)>();
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
                try
                {
                    (before, after) = RunToF2(function, IrPasses.Default, f2Index, method => IrImporter.Import(source, method));
                }
                catch (Exception ex)
                {
                    totals.PassBugs++;
                    if (examples.Count < maxExamples)
                        examples.Add($"PASS BUG {ex.GetType().Name}: {ex.Message} ({typeName}::{methodName})");
                    continue;
                }

                totals.BeforeStores += before.StoreCount;
                totals.BeforeLoads += before.LoadCount;
                totals.BeforeSlots += before.Slots.Count;
                totals.AfterStores += after.StoreCount;
                totals.AfterLoads += after.LoadCount;
                totals.AfterSlots += after.Slots.Count;
                if (after.Slots.Count > 0)
                    totals.MethodsWithResidual++;
                if (after.StoreCount < before.StoreCount || after.LoadCount < before.LoadCount)
                    totals.MethodsImproved++;

                foreach (var slot in after.Slots.Values)
                {
                    var klass = Classify(slot);
                    var prior = residuals.GetValueOrDefault(klass);
                    string example = prior.Example
                        ?? $"{typeName}::{methodName} S_{slot.Slot} ({slot.Stores.Count} store, {slot.Loads.Count} load)";
                    residuals[klass] = (prior.Count + 1, example);
                }
            }
            if (capped)
                break;
        }

        string scope = capped ? $"{totals.Methods} methods (capped)" : $"{totals.Methods} methods";
        Console.WriteLine();
        Console.WriteLine($"F2 SLOT RESIDUAL CENSUS over {scope} ({totals.PassBugs} pass bugs)");
        Console.WriteLine();
        Console.WriteLine("| Metric | Before late F2 | After late F2 | Delta |");
        Console.WriteLine("| --- | ---: | ---: | ---: |");
        Row("StoreStackSlot nodes", totals.BeforeStores, totals.AfterStores);
        Row("LoadStackSlot nodes", totals.BeforeLoads, totals.AfterLoads);
        Row("Distinct stack slots", totals.BeforeSlots, totals.AfterSlots);
        Console.WriteLine();
        Console.WriteLine($"Methods with F2 removals: {totals.MethodsImproved}");
        Console.WriteLine($"Methods with post-F2 residual slots: {totals.MethodsWithResidual}");
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

        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in examples)
                Console.WriteLine($"  {example}");
        }

        return totals.PassBugs > 0 ? 1 : 0;

        void Row(string label, long before, long after)
            => Console.WriteLine($"| {label} | {before} | {after} | {after - before:+#;-#;0} |");
    }

    static int F2PassIndex()
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
                return i;
        throw new InvalidOperationException("Could not find late F2 ExpressionInliningPass before SlotMaterializationPass.");
    }

    static (SlotSnapshot Before, SlotSnapshot After) RunToF2(
        IrFunction function,
        ImmutableArray<IIrPass> passes,
        int f2Index,
        Func<MethodRef, IrFunction?> importMethodBody)
    {
        var context = PassContext.ForImport(importMethodBody);
        for (int i = 0; i < passes.Length; i++)
        {
            if (i == f2Index)
            {
                var before = SlotSnapshot.Capture(function);
                passes[i].Run(function, context);
                function.CheckInvariant();
                return (before, SlotSnapshot.Capture(function));
            }
            passes[i].Run(function, context);
            function.CheckInvariant();
        }
        throw new InvalidOperationException("F2 pass was not reached.");
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
        public long MethodsImproved;
        public long MethodsWithResidual;
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

    sealed record SlotSnapshot(IReadOnlyDictionary<int, SlotWeb> Slots)
    {
        public long StoreCount => Slots.Values.Sum(slot => slot.Stores.Count);
        public long LoadCount => Slots.Values.Sum(slot => slot.Loads.Count);

        public static SlotSnapshot Capture(IrFunction function)
        {
            var slots = new Dictionary<(IrNode Scope, int Slot), SlotWeb>();
            SlotWeb Slot(IrNode node, int index)
            {
                var scope = SlotScope(function, node);
                if (!slots.TryGetValue((scope, index), out var slot))
                    slots[(scope, index)] = slot = new SlotWeb(index, scope != function);
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
            return new SlotSnapshot(slots.Values.ToDictionary(slot => slot.Ordinal, slot => slot));
        }
    }

    sealed class SlotWeb(int slot, bool isInsideNestedFunction)
    {
        public int Slot { get; } = slot;
        public int Ordinal { get; } = s_nextOrdinal++;
        public List<SlotSite> Stores { get; } = [];
        public List<SlotSite> Loads { get; } = [];
        public bool IsInsideNestedFunction { get; } = isInsideNestedFunction;

        static int s_nextOrdinal;
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
