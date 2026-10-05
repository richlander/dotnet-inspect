using System.Collections.Immutable;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Thin-writer burn-down measurement: after the full product pipeline, count
/// the locals <see cref="ResidualSlotBindingPass"/> issued for stack-slot webs
/// that materialization declined, grouped by binding kind and veto flags. The
/// population at the implementation slice's head is the recorded start of the
/// burn-down; each later materialization slice should move one group to zero.
/// Nested lambda and local-function bodies bind inside their own pipeline
/// tail and carry their provenance on the raised node, so the census reads
/// every scope: the function and each nested body it contains.
/// </summary>
static class ResidualBindingCensus
{
    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        var totals = new Totals();
        var groups = new Dictionary<(ResidualSlotBindingKind Kind, SlotMaterializationVeto Vetoes), (long Webs, long Locals, string Example)>();
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
                    bool methodHasBindings = false;
                    foreach (var (nestedScope, bindings) in Scopes(function))
                    {
                        if (bindings.Count == 0)
                            continue;
                        methodHasBindings = true;
                        if (nestedScope is not null)
                            totals.NestedLocals += bindings.Count;
                        totals.Locals += bindings.Count;
                        foreach (var web in bindings.Values.GroupBy(static binding => binding.Slot))
                        {
                            totals.Webs++;
                            if (nestedScope is not null)
                                totals.NestedWebs++;
                            var first = web.First();
                            var key = (first.Kind, first.Vetoes);
                            var prior = groups.GetValueOrDefault(key);
                            groups[key] = (
                                prior.Webs + 1,
                                prior.Locals + web.Count(),
                                prior.Example ?? $"{typeName}::{methodName}{nestedScope} S_{web.Key}");
                            if (first.Kind == ResidualSlotBindingKind.Split)
                                totals.SplitWebs++;
                            if (first.Vetoes == SlotMaterializationVeto.None)
                                totals.LateDecidableWebs++;
                        }
                    }
                    if (methodHasBindings)
                        totals.MethodsWithBindings++;
                }
                catch (Exception ex)
                {
                    totals.PassBugs++;
                    if (examples.Count < maxExamples)
                        examples.Add(
                            PassBugDiagnostic.Format(
                                ex, assemblyPath, typeName, methodName,
                                function.Signature, function.MetadataToken));
                }
            }
            if (capped)
                break;
        }

        string scope = capped ? $"{totals.Methods} methods (capped)" : $"{totals.Methods} methods";
        Console.WriteLine();
        Console.WriteLine($"RESIDUAL SLOT BINDING CENSUS over {scope} ({totals.PassBugs} pass bugs)");
        Console.WriteLine();
        Console.WriteLine("| Metric | Count |");
        Console.WriteLine("| --- | ---: |");
        Console.WriteLine($"| Residual-bound webs | {totals.Webs} |");
        Console.WriteLine($"| Residual-bound locals | {totals.Locals} |");
        Console.WriteLine($"| Methods with residual-bound webs | {totals.MethodsWithBindings} |");
        Console.WriteLine($"| Residual-bound webs in nested bodies | {totals.NestedWebs} |");
        Console.WriteLine($"| Residual-bound locals in nested bodies | {totals.NestedLocals} |");
        Console.WriteLine($"| Split webs | {totals.SplitWebs} |");
        Console.WriteLine($"| Late-decidable webs (no veto at the pass position) | {totals.LateDecidableWebs} |");
        Console.WriteLine();
        Console.WriteLine("Bound webs by binding kind and veto flags:");
        foreach (var (key, (webs, locals, example)) in groups.OrderByDescending(static entry => entry.Value.Webs).ThenBy(static entry => entry.Key.Kind))
        {
            string vetoes = key.Vetoes == SlotMaterializationVeto.None ? "late-decidable" : key.Vetoes.ToString();
            Console.WriteLine($"{webs,10}  {key.Kind,-8} {vetoes,-70}  locals={locals,-5} e.g. {example}");
        }

        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in examples)
                Console.WriteLine($"  {example}");
        }

        return totals.PassBugs > 0 ? 1 : 0;
    }

    /// <summary>
    /// The function's own bindings (scope label <c>null</c>), then each nested
    /// lambda or local function's bindings in pre-order, labelled for the
    /// example column.
    /// </summary>
    static IEnumerable<(string? Scope, ImmutableDictionary<int, ResidualSlotBinding> Bindings)> Scopes(IrFunction function)
    {
        yield return (null, function.ResidualSlotBindings);
        int lambdas = 0;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case Lambda lambda:
                    yield return ($" lambda#{lambdas++}", lambda.ResidualSlotBindings);
                    break;
                case LocalFunctionStatement localFunction:
                    yield return ($" {localFunction.Name}", localFunction.ResidualSlotBindings);
                    break;
            }
        }
    }

    sealed class Totals
    {
        public long Methods;
        public long PassBugs;
        public long Webs;
        public long Locals;
        public long NestedWebs;
        public long NestedLocals;
        public long SplitWebs;
        public long LateDecidableWebs;
        public long MethodsWithBindings;
    }
}
