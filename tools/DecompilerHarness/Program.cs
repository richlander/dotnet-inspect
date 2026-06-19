using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Diagnostic harness for the decompiler pipeline — the asmdiffs analog from
/// docs/decompiler.md. It inventories health (fidelity, stop reasons)
/// across whole assemblies, scores the real-gap burn-down (<c>--gaps</c>),
/// validates output (<c>--compile-check</c>, <c>--compile-back</c>), and dumps a
/// single method through every pipeline stage (<c>--dump</c> and friends).
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        List<string> inputs = [];
        int maxExamples = 5;

        string? dumpMethod = null;
        bool compileCheck = false;
        int compileCap = 4000;
        string? emitDefects = null;
        string? diffDefects = null;
        bool compileBack = false;
        bool gaps = false;
        bool steps = false;
        int stepLimit = int.MaxValue;
        bool ilView = false;
        bool skipPdb = false;
        bool facts = false;
        bool cfg = false;
        bool mermaid = false;
        bool diff = false;
        bool remarks = false;
        bool lowered = false;
        bool passImpact = false;
        string? passImpactPass = null;
        bool showDiff = false;
        bool structuringBails = false;
        int cap = int.MaxValue;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dump": dumpMethod = args[++i]; break;
                case "--steps": steps = true; break;
                case "--facts": facts = true; break;
                case "--cfg": cfg = true; break;
                case "--mermaid": mermaid = true; break;
                case "--diff": diff = true; break;
                case "--remarks": remarks = true; break;
                case "--lowered": lowered = true; break;
                case "--step-limit": steps = true; stepLimit = int.Parse(args[++i]); break;
                case "--il": ilView = true; break;
                case "--skip-pdb": skipPdb = true; break;
                case "--max-examples": maxExamples = int.Parse(args[++i]); break;
                case "--compile-check": compileCheck = true; break;
                case "--compile-cap": compileCap = int.Parse(args[++i]); break;
                case "--emit-defects": emitDefects = args[++i]; break;
                case "--diff-defects": diffDefects = args[++i]; break;
                case "--compile-back": compileBack = true; break;
                case "--gaps": gaps = true; break;
                case "--pass-impact":
                    passImpact = true;
                    // Optional pass name: consume the next token only when it is
                    // clearly a pass name — not a flag, and not an input path
                    // (an assembly the sweep should run over). Absent → histogram.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-')
                        && !File.Exists(args[i + 1]) && !Directory.Exists(args[i + 1]))
                        passImpactPass = args[++i];
                    break;
                case "--show-diff": showDiff = true; break;
                case "--structuring-bails": structuringBails = true; break;
                case "--cap": cap = int.Parse(args[++i]); break;
                case "--help" or "-h": PrintUsage(); return 0;
                default: inputs.Add(args[i]); break;
            }
        }

        var assemblies = ResolveAssemblies(inputs);
        if (assemblies.Count == 0)
            return Fail("No managed assemblies found in the given inputs.");

        if (compileCheck || emitDefects is not null || diffDefects is not null)
            return CompileChecker.Run(assemblies, compileCap, maxExamples, emitDefects, diffDefects, lowered);

        if (compileBack)
            return CompileBack.Run(assemblies, compileCap, maxExamples, lowered);

        if (gaps)
            return GapScan(assemblies, maxExamples);

        // --dump is single-method inspection through the shipped product
        // pipeline (StageDump -> PrintRaised).
        if (dumpMethod is not null)
        {
            if (facts)
                return DumpFacts(assemblies, dumpMethod, skipPdb);
            if (cfg)
                return DumpCfg(assemblies, dumpMethod, mermaid, skipPdb);
            if (diff)
                return DumpDiff(assemblies, dumpMethod, skipPdb);
            if (remarks)
                return DumpRemarks(assemblies, dumpMethod, skipPdb);
            if (lowered)
                return DumpLowered(assemblies, dumpMethod, skipPdb);
            return steps
                ? DumpSteps(assemblies, dumpMethod, stepLimit, skipPdb)
                : DumpNext(assemblies, dumpMethod, ilView ? StageDumpView.Full : StageDumpView.IrTree, skipPdb);
        }

        if (passImpact)
            return PassImpact(assemblies, passImpactPass, showDiff, cap);

        if (structuringBails)
            return StructuringBails(assemblies, cap);

        // Default: the pipeline's fidelity/stop-reason inventory.
        return SweepNext(assemblies);
    }

    /// <summary>
    /// The self-contained real-gap scoreboard. It inspects only the raised tree:
    /// a method is a gap if it still carries unstructured control flow (a
    /// surviving <c>goto</c>: a
    /// <see cref="Branch"/>/<see cref="ConditionalBranch"/>/<see cref="SwitchBranch"/>
    /// the structuring passes could not consume, or an EH <see cref="Leave"/>),
    /// or an <see cref="UnsupportedNode"/>. "Fully raised" is the metric to drive
    /// up; the residual-kind docket is the prioritized work. It measures
    /// completeness, not correctness — pair it with <c>--compile-back</c> for fidelity.
    /// </summary>
    static int GapScan(List<string> assemblies, int maxExamples)
    {
        long total = 0, clean = 0, crashes = 0;
        var buckets = new Dictionary<string, (long Count, List<string> Examples)>();

        void Record(string bucket, string method)
        {
            if (!buckets.TryGetValue(bucket, out var b))
                b = (0, new List<string>());
            if (b.Examples.Count < maxExamples)
                b.Examples.Add(method);
            buckets[bucket] = (b.Count + 1, b.Examples);
        }

        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath);
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                total++;
                try { IrPasses.Run(function); }
                catch (Exception ex)
                {
                    crashes++;
                    Console.Error.WriteLine($"PASS BUG: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                // The residual control-flow a fully-raised method never keeps; a
                // Partial import with no residual node falls to its stop reason.
                string id = $"{typeName}::{methodName}";
                string? bucket = Gaps.Residual(function)
                    ?? (function.Fidelity != DecompilationFidelity.Full
                        ? $"fidelity: {BucketFor(function.Diagnostics.FirstOrDefault())}"
                        : null);

                if (bucket is null)
                    clean++;
                else
                    Record(bucket, id);
            }
        }

        Console.WriteLine($"GAPS over {total} methods ({crashes} pass bugs):");
        Console.WriteLine($"  fully raised : {clean} ({Percent(clean, total)}) — no residual control flow, Full fidelity");
        long gapTotal = total - clean - crashes;
        Console.WriteLine($"  real gaps    : {gapTotal} ({Percent(gapTotal, total)}) — the self-contained burn-down docket");
        Console.WriteLine("By residual kind (most-actionable bucket per method):");
        foreach (var b in buckets.OrderByDescending(b => b.Value.Count))
            Console.WriteLine($"  {b.Value.Count,8}  {b.Key,-34}  e.g. {string.Join(" | ", b.Value.Examples.Take(3))}");
        return crashes > 0 ? 1 : 0;
    }

    /// <summary>
    /// Inventory sweep of the pipeline: fidelity histogram plus
    /// the stop-reason buckets that ARE the prioritized slice roadmap. Fidelity
    /// is read from the FINISHED tree — passes run first, exactly as the product
    /// path does — so a gap a raising pass closes (delegate construction) leaves
    /// the roadmap, and only the genuine residue remains.
    /// </summary>
    static int SweepNext(List<string> assemblies)
    {
        long total = 0, full = 0, crashes = 0;
        var stops = new Dictionary<string, long>();
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath);
            foreach (var (_, _, function) in IrImporter.ImportAssembly(source))
            {
                total++;
                try
                {
                    IrPasses.Run(function);
                }
                catch (Exception ex)
                {
                    crashes++;
                    Console.Error.WriteLine($"PASS BUG: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }
                if (function.Fidelity == DecompilationFidelity.Full)
                {
                    full++;
                    continue;
                }
                var diagnostic = function.Diagnostics.FirstOrDefault();
                if (diagnostic.Id == DiagnosticIds.InternalError)
                {
                    crashes++;
                    Console.Error.WriteLine($"IMPORTER BUG: {diagnostic.Message}");
                    continue;
                }
                string bucket = BucketFor(diagnostic);
                stops[bucket] = stops.GetValueOrDefault(bucket) + 1;
            }
        }

        Console.WriteLine($"next: {full}/{total} Full ({Percent(full, total)}); importer bugs: {crashes}");
        Console.WriteLine("Top stop reasons (the slice roadmap):");
        foreach (var stop in stops.OrderByDescending(s => s.Value).Take(15))
            Console.WriteLine($"  {stop.Value,8}  {stop.Key}");
        return crashes > 0 ? 1 : 0;
    }

    /// <summary>
    /// The roadmap bucket for a stop. Type-level stops
    /// (<see cref="DiagnosticIds.UnsupportedType"/>) group by their reason —
    /// the function-pointer and custom-modifier signatures that used to sink
    /// fidelity silently into the "(typed)" catch-all; the parenthetical detail
    /// (the specific modifier type) is trimmed so they aggregate. Opcode-level
    /// stops keep their second message token (the IL opcode).
    /// </summary>
    static string BucketFor(DecompilerDiagnostic diagnostic)
    {
        if (diagnostic.Id == DiagnosticIds.UnsupportedType)
        {
            var message = diagnostic.Message ?? "(typed)";
            int detail = message.IndexOf('(');
            return detail < 0 ? message : message[..detail].TrimEnd();
        }
        return diagnostic.Message?.Split(' ').ElementAtOrDefault(1) ?? "(typed)";
    }

    /// <summary>
    /// Blast-radius sweep — the inverse of the per-method <c>--dump --diff</c>
    /// (issue #641). With no pass named, prints a histogram: for each pass, the
    /// number of corpus methods it changed (the "which passes carry the load"
    /// roadmap). With a pass named, lists every method that pass changed (its
    /// blast radius), optionally with the per-method diff hunk (<c>--show-diff</c>).
    /// <paramref name="cap"/> stops the sweep after that many methods —
    /// RunWithStages over whole CoreLib is not free, and a cap is the same
    /// bound the compile rails use.
    /// </summary>
    static int PassImpact(List<string> assemblies, string? passFilter, bool showDiff, int cap)
    {
        // Resolve the pass name to its canonical spelling (case-insensitively)
        // so the per-method comparison is a cheap ordinal match and a typo
        // fails loudly with the known list instead of silently matching nothing.
        string? canonicalPass = null;
        if (passFilter is not null)
        {
            canonicalPass = IrPasses.Default
                .Select(p => p.Name)
                .FirstOrDefault(n => n.Equals(passFilter, StringComparison.OrdinalIgnoreCase));
            if (canonicalPass is null)
                return Fail($"Unknown pass '{passFilter}'. Known: " +
                    string.Join(", ", IrPasses.Default.Select(p => p.Name).Distinct()));
        }

        long total = 0, crashes = 0, matched = 0;
        var changedBy = new Dictionary<string, long>(StringComparer.Ordinal);
        bool capped = false;

        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath);
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                if (total >= cap) { capped = true; break; }
                total++;

                IReadOnlyList<PipelineStage> stages;
                try
                {
                    stages = IrPasses.RunWithStages(function);
                }
                catch (Exception ex)
                {
                    crashes++;
                    Console.Error.WriteLine($"PASS BUG: {ex.GetType().Name}: {ex.Message} ({typeName}::{methodName})");
                    continue;
                }
                var changed = StageDump.PassesThatChanged(stages);

                if (canonicalPass is null)
                {
                    foreach (var name in changed)
                        changedBy[name] = changedBy.GetValueOrDefault(name) + 1;
                    continue;
                }

                if (!changed.Contains(canonicalPass))
                    continue;
                matched++;
                Console.WriteLine($"{typeName}::{methodName}");
                if (showDiff)
                {
                    Console.Write(StageDump.FormatPassDiff(stages, canonicalPass));
                    Console.WriteLine();
                }
            }
            if (capped) break;
        }

        string scope = capped ? $"{total} methods (capped)" : $"{total} methods";
        if (canonicalPass is null)
        {
            Console.WriteLine();
            Console.WriteLine($"pass impact over {scope} ({crashes} pass bugs):");
            // Order by impact, then pipeline order so ties read top-to-bottom.
            int Ordinal(string name) => IrPasses.Default.Select(p => p.Name).ToList().IndexOf(name);
            foreach (var entry in changedBy.OrderByDescending(e => e.Value).ThenBy(e => Ordinal(e.Key)))
                Console.WriteLine($"  {entry.Value,8}  {entry.Key} ({Percent(entry.Value, total)})");
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"{canonicalPass} changed {matched}/{scope} ({Percent(matched, total)}); {crashes} pass bugs");
        }
        return crashes > 0 ? 1 : 0;
    }

    /// <summary>
    /// Tally why <see cref="StructuringPass"/> leaves block containers flat
    /// (docs/design/control-flow-structuring.md, first PR of the structuring
    /// track). Each method runs the Default pipeline with a
    /// <see cref="StructuringDiagnostics"/> sink attached; the pass records one
    /// reason per container it bails on and one tick per container it structures.
    /// The output is a per-container histogram — the reproducible measurement
    /// behind the "forward branch to a common exit past the region" docket that
    /// the index-range model cannot express. Behavior is unchanged: the sink only
    /// observes the bail decisions the pass already makes.
    /// </summary>
    static int StructuringBails(List<string> assemblies, int cap)
    {
        long total = 0, crashes = 0, structured = 0, bailedContainers = 0, methodsWithBail = 0;
        var reasons = new Dictionary<string, (long Count, string Example)>(StringComparer.Ordinal);
        bool capped = false;

        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath);
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                if (total >= cap) { capped = true; break; }
                total++;

                var diagnostics = new StructuringDiagnostics();
                var context = new PassContext(new Stepper(enabled: false), diagnostics);
                try
                {
                    IrPasses.Run(function, IrPasses.Default, context);
                }
                catch (Exception ex)
                {
                    crashes++;
                    Console.Error.WriteLine($"PASS BUG: {ex.GetType().Name}: {ex.Message} ({typeName}::{methodName})");
                    continue;
                }

                structured += diagnostics.Structured;
                if (diagnostics.Bails.Count > 0)
                    methodsWithBail++;
                foreach (var reason in diagnostics.Bails)
                {
                    bailedContainers++;
                    var prior = reasons.GetValueOrDefault(reason);
                    reasons[reason] = (prior.Count + 1,
                        prior.Example ?? $"{typeName}::{methodName}");
                }
            }
            if (capped) break;
        }

        string scope = capped ? $"{total} methods (capped)" : $"{total} methods";
        Console.WriteLine();
        Console.WriteLine($"structuring bails over {scope} ({crashes} pass bugs):");
        Console.WriteLine($"  {structured} containers structured; " +
            $"{bailedContainers} bailed across {methodsWithBail} methods");
        Console.WriteLine();
        foreach (var entry in reasons.OrderByDescending(e => e.Value.Count))
            Console.WriteLine($"  {entry.Value.Count,8}  {entry.Key,-30}  e.g. {entry.Value.Example}");
        return crashes > 0 ? 1 : 0;
    }

    /// <summary>Stage dump through the pipeline: the IR tree with diagnostics and fidelity (with <paramref name="view"/> = Full, the annotated-IL import views too).</summary>
    static int DumpNext(List<string> assemblies, string dumpMethod, StageDumpView view, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        foreach (var assemblyPath in assemblies)
        {
            using var source = skipPdb ? MetadataSource.OpenWithoutSymbols(assemblyPath) : MetadataSource.Open(assemblyPath);
            // Probe this assembly first so a method in a later one is still found.
            if (IrImporter.Import(source, typeName, methodName) is null)
                continue;
            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)} (pipeline: next)");
            var result = StageDump.DumpMethod(source, typeName, methodName, view);
            Console.Write(result.Output ?? string.Join("\n", result.Diagnostics.Select(d => $"// {d}")) + "\n");
            return 0;
        }
        return Fail($"Method '{dumpMethod}' not found (or has no IL body) in the given assemblies.");
    }

    /// <summary>
    /// Per-pass diff of the staged pipeline: each pass's effect shown as a
    /// unified +/- hunk over the previous stage's IR tree, so "what did this
    /// pass change?" is a glance instead of a manual sed between two stage
    /// headers (issue #633 item 3). Same stages and boundaries as the plain
    /// stage dump — only the rendering condenses to deltas.
    /// </summary>
    static int DumpDiff(List<string> assemblies, string dumpMethod, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        foreach (var assemblyPath in assemblies)
        {
            using var source = skipPdb ? MetadataSource.OpenWithoutSymbols(assemblyPath) : MetadataSource.Open(assemblyPath);
            var function = IrImporter.Import(source, typeName, methodName);
            if (function is null)
                continue;

            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)} (pipeline: next, per-pass diff)");
            Console.Write(StageDump.FormatDiff(IrPasses.RunWithStages(function)));
            return 0;
        }
        return Fail($"Method '{dumpMethod}' not found (or has no IL body) in the given assemblies.");
    }
    /// step limit, replays to that ordinal and dumps the IR tree right before
    /// the rewrite — "show me the tree just before this went wrong."
    /// </summary>
    static int DumpSteps(List<string> assemblies, string dumpMethod, int stepLimit, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        foreach (var assemblyPath in assemblies)
        {
            using var source = skipPdb ? MetadataSource.OpenWithoutSymbols(assemblyPath) : MetadataSource.Open(assemblyPath);
            var function = IrImporter.Import(source, typeName, methodName);
            if (function is null)
                continue;

            string where = stepLimit == int.MaxValue ? "all steps" : $"replay to step {stepLimit}";
            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)} (pipeline: next, {where})");
            var stepper = IrPasses.RunWithSteps(function, stepLimit);

            Console.WriteLine();
            Console.WriteLine($"==== steps ({stepper.Count} recorded) ====");
            foreach (var step in stepper.Steps)
                PrintStep(step, 0);

            Console.WriteLine();
            Console.WriteLine(stepLimit == int.MaxValue
                ? "==== IR (after all passes) ===="
                : $"==== IR (right before step {stepLimit}) ====");
            Console.Write(IrPrinter.Dump(function));
            return 0;
        }
        return Fail($"Method '{dumpMethod}' not found (or has no IL body) in the given assemblies.");
    }

    static void PrintStep(Step step, int indent)
    {
        string position = step.Position is { } p ? $"  @ {p}" : "";
        Console.WriteLine($"{new string(' ', indent * 2)}[{step.Index}] {step.Description}{position}");
        foreach (var child in step.Children)
            PrintStep(child, indent + 1);
    }

    /// <summary>
    /// Surfaces the printer's definite-assignment dataflow facts — the per-block
    /// predecessors, gen, and the <c>in</c>/<c>out</c> sets of the CFG fixpoint
    /// that decides which locals keep <c>= default</c>. The same analysis that
    /// produces the shipped C# fills the sink, so what prints here is the real
    /// decision, not a re-derivation (issue #633 item 1). The function is raised
    /// through the canonical pipeline first so the facts match the output.
    /// </summary>
    static int DumpFacts(List<string> assemblies, string dumpMethod, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        foreach (var assemblyPath in assemblies)
        {
            using var source = skipPdb ? MetadataSource.OpenWithoutSymbols(assemblyPath) : MetadataSource.Open(assemblyPath);
            var function = IrImporter.Import(source, typeName, methodName);
            if (function is null)
                continue;

            IrPasses.Run(function);  // raise through the canonical pipeline, as the product does
            var facts = CSharpPrinter.CollectDataflowFacts(function);

            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)} (pipeline: next, definite-assignment facts)");
            Console.WriteLine();
            Console.WriteLine("==== definite-assignment dataflow ====");
            Console.WriteLine($"locals: {(facts.LocalNames.Count == 0 ? "(none)" : string.Join(", ", facts.LocalNames))}");

            if (facts.Bailed)
                Console.WriteLine("result: analysis bailed on an unmodeled shape — every local keeps `= default`");
            else
                Console.WriteLine($"result: reads-before-assign = {NameSet(facts.ReadBeforeAssign, facts.LocalNames)}  (these keep `= default`; the rest declare bare)");

            for (int c = 0; c < facts.Containers.Count; c++)
            {
                Console.WriteLine();
                Console.WriteLine($"container #{c} (CFG dataflow):");
                foreach (var block in facts.Containers[c].Blocks)
                {
                    string tag = block.Reachable ? "" : "  [unreachable]";
                    Console.WriteLine(
                        $"  IL_{block.Offset:X4}{tag}" +
                        $"  preds: {OffsetSet(block.Predecessors)}" +
                        $"  succs: {OffsetSet(block.Successors)}");
                    Console.WriteLine(
                        $"           gen: {NameSet(block.Gen, facts.LocalNames)}" +
                        $"  in: {NameSet(block.In, facts.LocalNames)}" +
                        $"  out: {NameSet(block.Out, facts.LocalNames)}");
                }
            }
            return 0;
        }
        return Fail($"Method '{dumpMethod}' not found (or has no IL body) in the given assemblies.");
    }

    static string NameSet(IReadOnlyList<int> indices, IReadOnlyList<string> names) =>
        indices.Count == 0 ? "{}" : "{" + string.Join(", ", indices.Select(i => i < names.Count ? names[i] : $"V_{i}")) + "}";

    /// <summary>
    /// Surfaces fidelity remarks — every IR node that caps the method below
    /// <c>Full</c>, paired with its stable <c>DEC####</c> code, block offset, and
    /// the reason (issue #637). The same predicate that computes
    /// <c>IrFunction.Fidelity</c> produces the list, so a remark exists for
    /// exactly the nodes that lower the score. The function is raised through the
    /// canonical pipeline first so the remarks match the shipped output.
    /// </summary>
    static int DumpRemarks(List<string> assemblies, string dumpMethod, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        foreach (var assemblyPath in assemblies)
        {
            using var source = skipPdb ? MetadataSource.OpenWithoutSymbols(assemblyPath) : MetadataSource.Open(assemblyPath);
            var function = IrImporter.Import(source, typeName, methodName);
            if (function is null)
                continue;

            IrPasses.Run(function);  // raise through the canonical pipeline, as the product does
            var remarks = FidelityRemarks.Collect(function);

            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)} (pipeline: next, fidelity remarks)");
            Console.WriteLine();
            Console.WriteLine($"fidelity: {function.Fidelity}");
            if (remarks.Count == 0)
            {
                Console.WriteLine("remarks: (none) — every construct raised; representable C#");
                return 0;
            }

            Console.WriteLine($"remarks: {remarks.Count} site{(remarks.Count == 1 ? "" : "s")} cap fidelity below Full");
            Console.WriteLine();
            foreach (var r in remarks)
            {
                string where = r.Offset >= 0 ? $"IL_{r.Offset:X4}" : "(signature)";
                Console.WriteLine($"  {r.Code}  {where,-12}  {r.Reason}");
                Console.WriteLine($"           at: {r.Node}");
            }
            return 0;
        }
        return Fail($"Method '{dumpMethod}' not found (or has no IL body) in the given assemblies.");
    }

    static string OffsetSet(IReadOnlyList<int> offsets) =>
        offsets.Count == 0 ? "-" : string.Join(", ", offsets.Select(o => $"IL_{o:X4}"));

    /// <summary>
    /// Renders the lowered-C# view (issue #636): the <see cref="IrPasses.Lowered"/>
    /// pipeline (the default minus the cosmetic statement-sugar passes), so
    /// <c>for</c>/<c>foreach</c> fall back to <c>while</c>, <c>lock</c> to an
    /// explicit <c>Monitor</c> <c>try…finally</c>, and the <c>++</c>/<c>--</c>
    /// idiom to its explicit temp — valid, recompilable C# at a lower altitude
    /// than the shipped output (a SharpLab "lowered C#" for the decompiler). The
    /// definite-assignment facts that survive the lowering annotate the locals
    /// the analysis kept <c>= default</c>.
    /// </summary>
    static int DumpLowered(List<string> assemblies, string dumpMethod, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        foreach (var assemblyPath in assemblies)
        {
            using var source = skipPdb ? MetadataSource.OpenWithoutSymbols(assemblyPath) : MetadataSource.Open(assemblyPath);
            var function = IrImporter.Import(source, typeName, methodName);
            if (function is null)
                continue;

            IrPasses.Run(function, IrPasses.Lowered);  // lower, but stop short of the cosmetic sugar
            var facts = CSharpPrinter.CollectDataflowFacts(function);
            var body = CSharpPrinter.Print(function).Output;

            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)} (pipeline: lowered, fidelity {function.Fidelity})");

            // Facts-sourced comments: name the locals the definite-assignment
            // analysis kept `= default` because they may be read before assignment.
            if (!facts.Bailed)
                foreach (int local in facts.ReadBeforeAssign)
                    Console.WriteLine($"// {(local < facts.LocalNames.Count ? facts.LocalNames[local] : $"V_{local}")} kept `= default`: may be read before assignment");

            Console.WriteLine();
            Console.WriteLine(body);
            return 0;
        }
        return Fail($"Method '{dumpMethod}' not found (or has no IL body) in the given assemblies.");
    }

    /// <summary>
    /// Renders the control-flow graph of each block container in the raised IR —
    /// the predecessor/successor edges that otherwise have to be reconstructed
    /// by eye from <c>Branch IL_xxxx</c> targets across many blocks (issue #633
    /// item 2). Edges come from the shared <see cref="Cfg.Build"/> the printer's
    /// definite-assignment dataflow also uses, so the view cannot drift from the
    /// analysis.
    /// </summary>
    static int DumpCfg(List<string> assemblies, string dumpMethod, bool mermaid = false, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        foreach (var assemblyPath in assemblies)
        {
            using var source = skipPdb ? MetadataSource.OpenWithoutSymbols(assemblyPath) : MetadataSource.Open(assemblyPath);
            var function = IrImporter.Import(source, typeName, methodName);
            if (function is null)
                continue;

            IrPasses.Run(function);  // raise through the canonical pipeline, as the product does

            string form = mermaid ? "mermaid flowchart" : "control-flow graph";
            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)} (pipeline: next, {form})");

            var containers = function.Descendants.Prepend(function).OfType<BlockContainer>().ToList();
            int index = 0;
            foreach (var container in containers)
            {
                var blocks = container.Blocks;
                if (blocks.Count == 0)
                    continue;

                if (mermaid)
                {
                    Console.WriteLine();
                    Console.WriteLine($"%% container #{index++} ({blocks.Count} block{(blocks.Count == 1 ? "" : "s")})");
                    Console.WriteLine("```mermaid");
                    Console.Write(CfgMermaid.Render(blocks));
                    Console.WriteLine("```");
                    continue;
                }

                var edges = Cfg.Build(blocks);

                var preds = new List<int>[blocks.Count];
                for (int i = 0; i < blocks.Count; i++)
                    preds[i] = [];
                for (int i = 0; i < blocks.Count; i++)
                    foreach (int s in edges[i].Successors)
                        preds[s].Add(i);

                Console.WriteLine();
                Console.WriteLine($"container #{index++} ({blocks.Count} block{(blocks.Count == 1 ? "" : "s")}):");
                for (int i = 0; i < blocks.Count; i++)
                {
                    var predOffsets = preds[i].Select(p => blocks[p].StartOffset).Order().ToList();
                    Console.WriteLine($"  IL_{blocks[i].StartOffset:X4}  preds: {OffsetSet(predOffsets),-28}  succs: {Succs(blocks, edges[i])}");
                }
            }
            return 0;
        }
        return Fail($"Method '{dumpMethod}' not found (or has no IL body) in the given assemblies.");
    }

    static string Succs(IReadOnlyList<Block> blocks, Cfg.BlockEdges edges)
    {
        var parts = new List<string>();
        foreach (int s in edges.Successors)
            parts.Add($"IL_{blocks[s].StartOffset:X4}");
        foreach (int t in edges.ExternalTargets)
            parts.Add($"IL_{t:X4} (external)");
        if (edges.ExitsMethod)
            parts.Add("(return)");
        if (edges.LeavesRegion)
            parts.Add("(leave region)");
        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    static List<string> ResolveAssemblies(List<string> inputs)
    {
        if (inputs.Count == 0)
            return [typeof(object).Assembly.Location];

        List<string> result = [];
        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
                result.AddRange(Directory.EnumerateFiles(input, "*.dll").Where(IsManaged).Order());
            else if (File.Exists(input))
                result.Add(input);
            else
                Console.Error.WriteLine($"Warning: '{input}' not found, skipping.");
        }
        return result;
    }

    static bool IsManaged(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            return pe.HasMetadata;
        }
        catch
        {
            return false;
        }
    }

    static string TypeDisplayName(MetadataReader reader, TypeDefinition td)
    {
        string ns = reader.GetString(td.Namespace);
        string name = reader.GetString(td.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static string Percent(long part, long whole) => whole == 0 ? "n/a" : $"{100.0 * part / whole:F2}%";

    static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ");

    static int Fail(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }

    static void PrintUsage() => Console.WriteLine("""
        usage: decompiler-harness [assembly-or-directory ...] [options]

        With no inputs, sweeps System.Private.CoreLib of the running runtime.

        With no mode flag, runs the fidelity/stop-reason inventory of the
        decompiler pipeline over the inputs.

        options:
          --dump <T::M>         print every stage projection for one method
                                (e.g. --dump 'System.String::IsNullOrEmpty') —
                                per-pass IR trees ending in the product C#.
          --steps               with --dump: print the per-pass step log
                                (fine-grained rewrites).
          --facts               with --dump: print the printer's definite-assignment
                                dataflow facts — per-block preds/gen and the in/out
                                sets that decide which locals keep `= default`.
          --cfg                 with --dump: print the control-flow graph (per-block
                                predecessor/successor edges) of each block container
                                in the raised IR.
          --mermaid             with --dump --cfg: render the control-flow graph as a
                                mermaid flowchart (GitHub renders it inline) instead
                                of the textual edge listing.
          --diff                with --dump: print each pass's effect as a unified
                                +/- diff over the previous stage's IR tree.
          --remarks             with --dump: list every IR site that caps the method
                                below Full fidelity, with its DEC#### code, block
                                offset, and reason.
          --lowered             with --dump: render the lowered-C# view — the default
                                pipeline minus the cosmetic statement-sugar passes
                                (for/foreach, lock, ++/--), so the output is valid,
                                recompilable C# at a lower altitude. With
                                --compile-check: measure the lowered output's compile
                                rate instead of the shipped output's. With
                                --compile-back: roundtrip the lowered view through the
                                compiler and compare opcode streams.
          --step-limit <N>      with --dump: replay to step N and dump the IR
                                right before that rewrite.
          --il                  with --dump: prepend the annotated-IL import
                                views (raw/typed/structured).
          --skip-pdb            with --dump: ignore any portable PDB so locals
                                render as V_index — deterministic, symbol-
                                independent output regardless of nearby symbols.
          --max-examples <n>    example methods per bucket (default 5)
          --compile-check       compile every decompiled body; report invalid C#
          --compile-cap <n>     cap semantically-bound methods (default 4000)
          --emit-defects <f>    with --compile-check, write per-method defect codes to <f>
          --diff-defects <f>    with --compile-check, diff per-method defects against baseline <f>
          --compile-back        decompile, recompile in-context, and compare IL opcodes (semantic fidelity)
          --gaps                self-contained real-gap scoreboard — methods whose
                                raised tree still holds unstructured control flow
                                (a surviving goto) or an unsupported node, bucketed
                                by residual kind. The completeness burn-down signal.
          --pass-impact [pass]  blast-radius sweep — the inverse of --dump --diff.
                                With no pass: histogram of how many corpus methods
                                each pass changes. With a pass name (e.g.
                                'return-merge'): list every method that pass
                                changed. Add --show-diff for each method's hunk.
          --show-diff           with --pass-impact <pass>: print the per-pass diff
                                hunk under each changed method.
          --structuring-bails   tally why StructuringPass leaves containers flat:
                                a per-container histogram of bail reasons (the
                                common-exit merge docket). Honors --cap.
          --cap <n>             with --pass-impact/--structuring-bails: stop after
                                n methods (default: unlimited). Bounds a full-CoreLib
                                stage sweep.
        """);
}
