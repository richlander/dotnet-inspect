using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// C2/#2209 measurement: ask the actual printer stack-slot unifier how many
/// residual slots it sees after the full product pipeline. Unlike a target-type
/// proxy, this is owned by the printer's private unifier path.
/// </summary>
static class SlotUnifierCensus
{
    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        var totals = new Totals();
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

                try
                {
                    IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(method => IrImporter.Import(source, method)));
                    var telemetry = CSharpPrinter.CollectStackSlotUnifierTelemetry(function);
                    totals.Add(telemetry);
                    if (telemetry.DistinctSlots > 0)
                        totals.MethodsWithSlots++;
                    if (telemetry.UnunifiedSplitSlots > 0 && examples.Count < maxExamples)
                        examples.Add($"{typeName}::{methodName} ({telemetry.UnunifiedSplitSlots} un-unified split slot{(telemetry.UnunifiedSplitSlots == 1 ? "" : "s")})");
                }
                catch (Exception ex)
                {
                    totals.PassBugs++;
                    if (examples.Count < maxExamples)
                        examples.Add(
                            PassBugDiagnostic.Format(ex, assemblyPath, typeName, methodName, function.Signature));
                }
            }
            if (capped)
                break;
        }

        string scope = capped ? $"{totals.Methods} methods (capped)" : $"{totals.Methods} methods";
        Console.WriteLine();
        Console.WriteLine($"STACK-SLOT UNIFIER CENSUS over {scope} ({totals.PassBugs} pass bugs)");
        Console.WriteLine();
        Console.WriteLine("| Metric | Count |");
        Console.WriteLine("| --- | ---: |");
        Console.WriteLine($"| StoreStackSlot nodes reaching printer | {totals.StoreNodes} |");
        Console.WriteLine($"| LoadStackSlot nodes reaching printer | {totals.LoadNodes} |");
        Console.WriteLine($"| Distinct slots considered by printer unifier | {totals.DistinctSlots} |");
        Console.WriteLine($"| Methods with residual stack slots | {totals.MethodsWithSlots} |");
        Console.WriteLine($"| Single-candidate slots | {totals.SingleCandidateSlots} |");
        Console.WriteLine($"| Multi-candidate slots unified by printer | {totals.MultiCandidateUnifiedSlots} |");
        Console.WriteLine($"| Un-unified split slots | {totals.UnunifiedSplitSlots} |");
        Console.WriteLine($"| Stack-slot declarations emitted | {totals.EmittedDeclarationNames} |");

        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in examples)
                Console.WriteLine($"  {example}");
        }

        return totals.PassBugs > 0 ? 1 : 0;
    }

    sealed class Totals
    {
        public long Methods;
        public long PassBugs;
        public long StoreNodes;
        public long LoadNodes;
        public long DistinctSlots;
        public long CandidateSlots;
        public long SingleCandidateSlots;
        public long MultiCandidateUnifiedSlots;
        public long UnunifiedSplitSlots;
        public long EmittedDeclarationNames;
        public long MethodsWithSlots;

        public void Add(CSharpPrinter.StackSlotUnifierTelemetry telemetry)
        {
            StoreNodes += telemetry.StoreNodes;
            LoadNodes += telemetry.LoadNodes;
            DistinctSlots += telemetry.DistinctSlots;
            CandidateSlots += telemetry.CandidateSlots;
            SingleCandidateSlots += telemetry.SingleCandidateSlots;
            MultiCandidateUnifiedSlots += telemetry.MultiCandidateUnifiedSlots;
            UnunifiedSplitSlots += telemetry.UnunifiedSplitSlots;
            EmittedDeclarationNames += telemetry.EmittedDeclarationNames;
        }
    }
}
