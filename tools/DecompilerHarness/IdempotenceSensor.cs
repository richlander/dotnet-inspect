using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Pipeline idempotence sensor (#2303). Runs the IR pass pipeline once to raise a
/// method, then runs it a second time and reports every method the second run
/// still rewrites. A second-run-only rewrite is either a missed raise the single
/// shipped pipeline should have caught (an ordering gap between an enabling pass
/// and its consumer, F1's class) or an unstable pass — both are findings, and zero
/// is the target. The run-2 rewrite is bucketed by the pass that fired, so the
/// report is a burn-down list of ordering gaps. This is a 2x-pipeline lane by
/// construction, so it belongs in scheduled/deep runs, not the per-PR loop.
/// </summary>
internal static class IdempotenceSensor
{
    public static int Run(
        IReadOnlyList<string> assemblies,
        int maxExamples,
        int methodCap,
        int? workers,
        bool sequential)
    {
        Console.WriteLine("Evaluating pipeline idempotence (second-run rewrites)...");
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = sequential ? 1 : (workers ?? Math.Max(1, Environment.ProcessorCount - 2)),
        };

        long total = 0;
        long unstable = 0;
        long crashes = 0;
        var byPass = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        var examples = new ConcurrentBag<Example>();
        int exampleBudget = Math.Max(0, maxExamples);

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            var portablePath = CorpusSensor.PortablePath(assemblyPath);
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));
            // The cross-method import seam must be identical across both runs, or a
            // cross-method pass (classic-async MoveNext pull-in) would no-op in one
            // run and fire in the other and register as a false idempotence break.
            Func<MethodRef, IrFunction?> seam = method => IrImporter.Import(source, method);
            var candidates = IrImporter.GetStableSampleCandidates(source, methodCap).ToList();

            Parallel.ForEach(candidates, options, item =>
            {
                Interlocked.Increment(ref total);
                try
                {
                    var function = item.Build(source);
                    var changed = SecondRunChangedPasses(function, seam);
                    if (changed.Count == 0)
                        return;

                    Interlocked.Increment(ref unstable);
                    foreach (var pass in changed)
                        byPass.AddOrUpdate(pass, 1, (_, count) => count + 1);
                    if (examples.Count < exampleBudget)
                    {
                        string key = $"{portablePath}!{item.TypeName}::{item.MethodName}";
                        examples.Add(new Example(key, changed.OrderBy(p => p, StringComparer.Ordinal).ToArray()));
                    }
                }
                catch
                {
                    // A crash only on the second run is itself an instability signal,
                    // but it is not a render difference; count it separately.
                    Interlocked.Increment(ref crashes);
                }
            });
        }

        return Report(total, unstable, crashes, byPass, examples, exampleBudget);
    }

    /// <summary>
    /// Raises <paramref name="rawFunction"/> once, then runs the pipeline a second
    /// time and returns the passes that still rewrote the tree — empty when the
    /// pipeline reached a fixed point on the first run. Both runs use the same
    /// import seam, so a cross-method pass cannot register as a false break by
    /// no-op'ing in one run and firing in the other.
    /// </summary>
    internal static IReadOnlyCollection<string> SecondRunChangedPasses(
        IrFunction rawFunction,
        Func<MethodRef, IrFunction?> seam)
    {
        IrPasses.Run(rawFunction, IrPasses.Default, PassContext.ForImport(seam));
        var secondRun = IrPasses.RunWithStages(rawFunction, seam);
        return StageDump.PassesThatChanged(secondRun);
    }

    static int Report(
        long total,
        long unstable,
        long crashes,
        IReadOnlyDictionary<string, long> byPass,
        IReadOnlyCollection<Example> examples,
        int exampleBudget)
    {
        Console.WriteLine($"Pipeline idempotence: {total} methods evaluated");
        Console.WriteLine($"Second-run rewrites: {unstable}");
        Console.WriteLine($"Second-run crashes:  {crashes}");

        if (byPass.Count > 0)
        {
            Console.WriteLine("\nBy discharging pass (second run):");
            foreach (var (pass, count) in byPass.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                Console.WriteLine($"  {count,6}  {pass}");
        }

        if (exampleBudget > 0 && examples.Count > 0)
        {
            Console.WriteLine("\nExamples (method — passes that fired on the second run):");
            foreach (var example in examples.OrderBy(e => e.Key, StringComparer.Ordinal).Take(exampleBudget))
                Console.WriteLine($"  {example.Key}  [{string.Join(", ", example.Passes)}]");
        }

        if (unstable == 0)
        {
            Console.WriteLine("\nPipeline is idempotent: the second run rewrote nothing.");
            return 0;
        }

        return 1;
    }

    readonly record struct Example(string Key, IReadOnlyList<string> Passes);
}
