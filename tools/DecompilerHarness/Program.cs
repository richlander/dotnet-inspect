using ILInspector.ControlFlow;
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
/// across whole assemblies, measures real-gap completeness (<c>--gaps</c>),
/// validates output (<c>--validity-check</c>, <c>--fidelity-check</c>), and dumps a
/// single method through every pipeline stage (<c>--dump</c> and friends).
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        List<string> inputs = [];
        int maxExamples = 5;

        string? dumpMethod = null;
        int dumpIndex = 0;
        bool listOverloads = false;
        bool byShape = false;
        bool validityCheck = false;
        int compileCap = 4000;
        string? emitValidityDefects = null;
        string? diffValidityDefects = null;
        bool fidelityCheck = false;
        bool gaps = false;
        bool annotationCheck = false;
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
        bool simulate = false;
        bool passImpact = false;
        string? passImpactPass = null;
        bool showDiff = false;
        bool structuringStops = false;
        bool postdomProbe = false;
        int postdomSample = 0;
        bool libraryReport = false;
        bool unsupportedNodes = false;
        bool typeCheck = false;
        bool bindCheck = false;
        bool classifyDec0009 = false;
        bool generatedFixtures = false;
        string? generatedFixtureSelector = null;
        bool keepGeneratedFixtures = false;
        string? emitCorpusSnapshot = null;
        string? diffCorpusBaseline = null;
        string? emitCorpusDelta = null;
        string? fidelityMethodDelta = null;
        bool qualityDiffCard = false;
        bool qualityCardRisky = false;
        var corpusFidelityCaps = new List<int>();
        int corpusMethodCap = int.MaxValue;
        bool json = false;
        int topPatterns = 10;
        int? topLibraries = null;
        int cap = int.MaxValue;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dump": dumpMethod = args[++i]; break;
                case "--index": dumpIndex = int.Parse(args[++i]); break;
                case "--list-overloads": listOverloads = true; break;
                case "--by-shape": byShape = true; break;
                case "--steps": steps = true; break;
                case "--facts": facts = true; break;
                case "--cfg": cfg = true; break;
                case "--mermaid": mermaid = true; break;
                case "--diff": diff = true; break;
                case "--remarks": remarks = true; break;
                case "--lowered": lowered = true; break;
                case "--simulate-new-rules": simulate = true; break;
                case "--step-limit": steps = true; stepLimit = int.Parse(args[++i]); break;
                case "--il": ilView = true; break;
                case "--skip-pdb": skipPdb = true; break;
                case "--max-examples": maxExamples = int.Parse(args[++i]); break;
                case "--validity-check": validityCheck = true; break;
                case "--compile-cap": compileCap = int.Parse(args[++i]); break;
                case "--emit-validity-defects": emitValidityDefects = args[++i]; break;
                case "--diff-validity-defects": diffValidityDefects = args[++i]; break;
                case "--fidelity-check": fidelityCheck = true; break;
                case "--gaps": gaps = true; break;
                case "--annotation-check": annotationCheck = true; break;
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
                case "--structuring-stops": structuringStops = true; break;
                case "--postdom-probe": postdomProbe = true; break;
                case "--postdom-sample": postdomProbe = true; postdomSample = int.Parse(args[++i]); break;
                case "--library-report": libraryReport = true; break;
                case "--unsupported-nodes": unsupportedNodes = true; break;
                case "--type-check": typeCheck = true; break;
                case "--bind-check": bindCheck = true; break;
                case "--classify-dec0009": classifyDec0009 = true; break;
                case "--dec0009-shapes": classifyDec0009 = true; break;
                case "--generated-fixtures":
                    generatedFixtures = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith('-')
                        && !File.Exists(args[i + 1]) && !Directory.Exists(args[i + 1])
                        && !LooksLikePath(args[i + 1]))
                        generatedFixtureSelector = args[++i];
                    break;
                case "--keep-generated-fixtures": keepGeneratedFixtures = true; break;
                case "--emit-corpus-baseline": emitCorpusSnapshot = args[++i]; break;
                case "--emit-corpus-snapshot": emitCorpusSnapshot = args[++i]; break;
                case "--diff-corpus-baseline": diffCorpusBaseline = args[++i]; break;
                case "--emit-corpus-delta": emitCorpusDelta = args[++i]; break;
                case "--fidelity-method-delta": fidelityMethodDelta = args[++i]; break;
                case "--quality-diff-card": qualityDiffCard = true; break;
                case "--quality-card-risky": qualityDiffCard = true; qualityCardRisky = true; break;
                case "--corpus-fidelity-cap":
                    foreach (var token in args[++i].Split(','))
                    {
                        if (token.Length == 0)
                            continue;
                        corpusFidelityCaps.Add(int.Parse(token));
                    }
                    break;
                case "--corpus-method-cap": corpusMethodCap = int.Parse(args[++i]); break;
                case "--json": json = true; break;
                case "--top-patterns": topPatterns = int.Parse(args[++i]); break;
                case "--top-libraries": topLibraries = int.Parse(args[++i]); break;
                case "--cap": cap = int.Parse(args[++i]); break;
                case "--help" or "-h": PrintUsage(); return 0;
                default: inputs.Add(args[i]); break;
            }
        }

        if (generatedFixtures)
        {
            if (inputs.Count > 0)
                return Fail("--generated-fixtures generates its own temporary input assembly; do not pass assembly paths.");
            return GeneratedFixtures(generatedFixtureSelector, keepGeneratedFixtures, json);
        }

        var assemblies = ResolveAssemblies(inputs);
        if (assemblies.Count == 0)
            return Fail("No managed assemblies found in the given inputs.");

        if (validityCheck || emitValidityDefects is not null || diffValidityDefects is not null)
            return ValidityCheck.Run(assemblies, compileCap, maxExamples, emitValidityDefects, diffValidityDefects, lowered);

        if (fidelityMethodDelta is not null)
        {
            if (!fidelityCheck)
                return Fail("--fidelity-method-delta requires --fidelity-check.");
            return FidelityCheck.RunMethodDelta(assemblies, fidelityMethodDelta, maxExamples, lowered);
        }

        if (fidelityCheck)
            return FidelityCheck.Run(assemblies, compileCap, maxExamples, lowered);

        if (typeCheck)
            return TypeSourceCheck.Run(assemblies, cap, maxExamples);

        if (bindCheck)
            return TypeBindCheck.Run(assemblies, cap, maxExamples);

        if (gaps)
            return CompletenessScan(assemblies, maxExamples, byShape);

        if (annotationCheck)
            return AnnotationCheck.Run(assemblies, maxExamples);

        if (classifyDec0009)
            return Dec0009Classifier.Run(assemblies, maxExamples, json);

        if (emitCorpusSnapshot is not null || diffCorpusBaseline is not null || emitCorpusDelta is not null || qualityDiffCard)
            return CorpusSensor.Run(assemblies, compileCap, corpusFidelityCaps, maxExamples, emitCorpusSnapshot, diffCorpusBaseline, emitCorpusDelta, qualityDiffCard, qualityCardRisky, corpusMethodCap);

        if (libraryReport)
            return LibraryReport.Run(assemblies, compileCap, maxExamples, json, topPatterns, topLibraries);

        if (unsupportedNodes)
            return UnsupportedNodeReport.Run(assemblies, maxExamples, json);

        // --dump is single-method inspection through the shipped product
        // pipeline (StageDump -> PrintRaised).
        if (dumpMethod is not null)
        {
            // Resolve overloads up front: --list-overloads prints the menu and
            // stops; otherwise a >1-overload name prints the menu to stderr (so
            // stdout stays pipe-clean) before dumping the selected --index, and an
            // out-of-range index fails with the menu instead of silently picking #0.
            if (DisambiguateDump(assemblies, dumpMethod, dumpIndex, listOverloads, skipPdb) is { } code)
                return code;

            if (facts)
                return DumpFacts(assemblies, dumpMethod, dumpIndex, skipPdb);
            if (cfg)
                return DumpCfg(assemblies, dumpMethod, dumpIndex, mermaid, skipPdb);
            if (diff)
                return DumpDiff(assemblies, dumpMethod, dumpIndex, skipPdb);
            if (remarks)
                return DumpRemarks(assemblies, dumpMethod, dumpIndex, skipPdb);
            if (lowered)
                return DumpLowered(assemblies, dumpMethod, dumpIndex, skipPdb, simulate);
            return steps
                ? DumpSteps(assemblies, dumpMethod, dumpIndex, stepLimit, skipPdb)
                : Dump(assemblies, dumpMethod, dumpIndex, ilView ? StageDumpView.Full : StageDumpView.IrTree, skipPdb, simulate);
        }

        if (passImpact)
            return PassImpact(assemblies, passImpactPass, showDiff, cap);

        if (structuringStops)
            return StructuringStops(assemblies, cap);

        if (postdomProbe)
            return PostDomProbe.Run(assemblies, cap, postdomSample);

        // Default: the pipeline's fidelity/stop-reason inventory.
        return Inventory(assemblies);
    }

    static int GeneratedFixtures(string? selector, bool keepArtifacts, bool json)
    {
        var fixtures = GeneratedFixtureCatalog.Select(selector);
        if (selector == "list")
        {
            if (json)
                Console.WriteLine(GeneratedFixtureRunner.FormatListJson(GeneratedFixtureCatalog.Catalog));
            else
                Console.Write(GeneratedFixtureRunner.FormatList(GeneratedFixtureCatalog.Catalog));
            return 0;
        }

        if (fixtures.Count == 0)
            return Fail($"No generated fixture IDs match '{selector}'. Use '--generated-fixtures list'.");

        var run = GeneratedFixtureRunner.Run(
            fixtures,
            new GeneratedFixtureRunOptions(KeepArtifacts: keepArtifacts));
        if (json)
        {
            Console.WriteLine(GeneratedFixtureRunner.FormatJson(run));
        }
        else
        {
            Console.Write(GeneratedFixtureRunner.FormatReport(run));
        }
        if (keepArtifacts && !json)
        {
            Console.WriteLine();
            Console.WriteLine($"Generated fixture project: {run.ProjectDirectory}");
            Console.WriteLine($"Generated fixture assembly: {run.AssemblyPath}");
        }
        return run.Passed ? 0 : 1;
    }

    static bool LooksLikePath(string value) =>
        value.Contains(Path.DirectorySeparatorChar)
        || value.Contains(Path.AltDirectorySeparatorChar)
        || value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The self-contained real-gap view. It inspects only the raised tree:
    /// a method is a gap if it still carries unstructured control flow (a
    /// surviving <c>goto</c>: a
    /// <see cref="Branch"/>/<see cref="ConditionalBranch"/>/<see cref="SwitchBranch"/>
    /// the structuring passes could not consume, or an EH <see cref="Leave"/>),
    /// or an <see cref="UnsupportedNode"/>. "Fully raised" is the metric to drive
    /// up; the residual-kind docket is the prioritized work. It measures
    /// completeness, not correctness — pair it with <c>--fidelity-check</c> for fidelity.
    /// </summary>
    static int CompletenessScan(List<string> assemblies, int maxExamples, bool byShape = false)
    {
        long total = 0, clean = 0, crashes = 0;
        var buckets = new Dictionary<string, (long Count, List<string> Examples)>();
        // When --by-shape is set, sub-classify the switch-branch bucket by the
        // structural shape of its residual switch (issue #682 item 2) and the
        // conditional-branch bucket by the shape around its residual guard (#921).
        var switchShapes = new Dictionary<string, (long Count, List<string> Examples)>();
        var conditionalShapes = new Dictionary<string, (long Count, List<string> Examples)>();
        var ehShapes = new Dictionary<string, (long Count, List<string> Examples)>();

        void Record(string bucket, string method)
        {
            if (!buckets.TryGetValue(bucket, out var b))
                b = (0, new List<string>());
            if (b.Examples.Count < maxExamples)
                b.Examples.Add(method);
            buckets[bucket] = (b.Count + 1, b.Examples);
        }

        void RecordShape(Dictionary<string, (long Count, List<string> Examples)> shapes, string shape, string method)
        {
            if (!shapes.TryGetValue(shape, out var b))
                b = (0, new List<string>());
            if (b.Examples.Count < maxExamples)
                b.Examples.Add(method);
            shapes[shape] = (b.Count + 1, b.Examples);
        }

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                total++;
                // The shape classifier reads the imported CFG (the switch is still a
                // block terminator there); the passes flatten it on a failed raise,
                // so keep a pre-pass copy when --by-shape is requested.
                IrFunction? prePass = byShape ? (IrFunction)function.Clone() : null;
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
                string? bucket = Completeness.Residual(function)
                    ?? (function.Fidelity != DecompilationFidelity.Full
                        ? $"fidelity: {BucketFor(function.Diagnostics.FirstOrDefault())}"
                        : null);

                if (bucket is null)
                    clean++;
                else
                {
                    Record(bucket, id);
                    if (byShape && bucket == "structuring: switch-branch")
                        RecordShape(switchShapes, SwitchShapeClassifier.Classify(prePass!), id);
                    // The residual conditional survives in the finished tree, so
                    // classify the post-pass function rather than the pre-pass clone.
                    if (byShape && bucket == "structuring: conditional-branch")
                    {
                        string shape = ConditionalBranchShapeClassifier.Classify(function);
                        RecordShape(conditionalShapes, shape, id);
                        // The eh-entangled bucket is itself a product of branch
                        // position x EH construct; sub-split it for the EH-aware
                        // structuring burndown (#1089).
                        if (shape == "eh-entangled")
                            RecordShape(ehShapes, EhShapeClassifier.Classify(function), id);
                    }
                }
            }
        }

        Console.WriteLine($"GAPS over {total} methods ({crashes} pass bugs):");
        Console.WriteLine($"  fully raised : {clean} ({Percent(clean, total)}) — no residual control flow, Full fidelity");
        long gapTotal = total - clean - crashes;
        Console.WriteLine($"  real gaps    : {gapTotal} ({Percent(gapTotal, total)}) — the self-contained completeness docket");
        Console.WriteLine("By residual kind (most-actionable bucket per method):");
        foreach (var b in buckets.OrderByDescending(b => b.Value.Count))
            Console.WriteLine($"  {b.Value.Count,8}  {b.Key,-34}  e.g. {string.Join(" | ", b.Value.Examples.Take(3))}");

        if (byShape && switchShapes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("switch-branch bucket by structural shape (--by-shape):");
            foreach (var s in switchShapes.OrderByDescending(s => s.Value.Count))
                Console.WriteLine($"  {s.Value.Count,8}  {s.Key,-38}  e.g. {string.Join(" | ", s.Value.Examples.Take(3))}");
        }
        if (byShape && conditionalShapes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("conditional-branch bucket by structural shape (--by-shape):");
            foreach (var s in conditionalShapes.OrderByDescending(s => s.Value.Count))
                Console.WriteLine($"  {s.Value.Count,8}  {s.Key,-38}  e.g. {string.Join(" | ", s.Value.Examples.Take(3))}");
        }
        if (byShape && ehShapes.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("eh-entangled bucket by EH subshape (--by-shape, hardest blocker per method):");
            foreach (var s in ehShapes.OrderByDescending(s => s.Value.Count))
                Console.WriteLine($"  {s.Value.Count,8}  {s.Key,-38}  e.g. {string.Join(" | ", s.Value.Examples.Take(3))}");
        }
        return crashes > 0 ? 1 : 0;
    }

    /// <summary>
    /// Inventory sweep of the pipeline: fidelity histogram plus
    /// the stop-reason buckets that ARE the prioritized slice roadmap. Fidelity
    /// is read from the FINISHED tree — passes run first, exactly as the product
    /// path does — so a gap a raising pass closes (delegate construction) leaves
    /// the roadmap, and only the genuine residue remains.
    /// </summary>
    static int Inventory(List<string> assemblies)
    {
        long total = 0, full = 0, crashes = 0;
        var stops = new Dictionary<string, long>();
        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
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
    /// bound the compiling checks use.
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

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
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
    /// reason per container it stops on and one tick per container it structures.
    /// The output is a per-container histogram — the reproducible measurement
    /// behind the "forward branch to a common exit past the region" docket that
    /// the index-range model cannot express. Behavior is unchanged: the sink only
    /// observes the stop decisions the pass already makes.
    /// </summary>
    static int StructuringStops(List<string> assemblies, int cap)
    {
        long total = 0, crashes = 0, structured = 0, stoppedContainers = 0, methodsWithStop = 0;
        var reasons = new Dictionary<string, (long Count, string Example)>(StringComparer.Ordinal);
        bool capped = false;

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
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
                if (diagnostics.Stops.Count > 0)
                    methodsWithStop++;
                foreach (var reason in diagnostics.Stops)
                {
                    stoppedContainers++;
                    var prior = reasons.GetValueOrDefault(reason);
                    reasons[reason] = (prior.Count + 1,
                        prior.Example ?? $"{typeName}::{methodName}");
                }
            }
            if (capped) break;
        }

        string scope = capped ? $"{total} methods (capped)" : $"{total} methods";
        Console.WriteLine();
        Console.WriteLine($"structuring stops over {scope} ({crashes} pass bugs):");
        Console.WriteLine($"  {structured} containers structured; " +
            $"{stoppedContainers} left flat across {methodsWithStop} methods");
        Console.WriteLine();
        foreach (var entry in reasons.OrderByDescending(e => e.Value.Count))
            Console.WriteLine($"  {entry.Value.Count,8}  {entry.Key,-30}  e.g. {entry.Value.Example}");
        return crashes > 0 ? 1 : 0;
    }

    /// <summary>
    /// Resolves <c>--dump</c> overloads before the dump runs. Returns a non-null
    /// exit code to short-circuit (the overload list was requested, or the index
    /// is out of range); returns null to let the dump proceed at the selected
    /// index. When a name has more than one overload it prints the menu to stderr
    /// so the user always sees what was selected, while stdout stays pipe-clean.
    /// </summary>
    static int? DisambiguateDump(List<string> assemblies, string dumpMethod, int index, bool listOnly, bool skipPdb)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        IReadOnlyList<OverloadInfo> overloads = [];
        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = OpenSource(assemblyPath, skipPdb, metadata);
            overloads = IrImporter.Overloads(source, typeName, methodName);
            if (overloads.Count > 0)
                break;
        }

        if (listOnly)
        {
            if (overloads.Count == 0)
                return Fail($"No overloads of '{dumpMethod}' found in the given assemblies.");
            Console.WriteLine($"{dumpMethod} — {overloads.Count} overload(s):");
            PrintOverloads(Console.Out, methodName, overloads, index);
            return 0;
        }

        // Unknown name: let the dump path emit its own not-found diagnostic.
        if (overloads.Count == 0)
            return null;

        if (index < 0 || index >= overloads.Count)
        {
            Console.Error.WriteLine($"--index {index} is out of range — '{dumpMethod}' has {overloads.Count} overload(s):");
            PrintOverloads(Console.Error, methodName, overloads, -1);
            return 1;
        }

        if (overloads.Count > 1)
        {
            Console.Error.WriteLine($"// '{dumpMethod}' has {overloads.Count} overloads; dumping --index {index} (use --index N to pick another):");
            PrintOverloads(Console.Error, methodName, overloads, index);
        }
        return null;
    }

    /// <summary>Renders the overload menu: one line per overload, the selected index marked.</summary>
    static void PrintOverloads(TextWriter writer, string methodName, IReadOnlyList<OverloadInfo> overloads, int selected)
    {
        foreach (var o in overloads)
        {
            string marker = o.Index == selected ? "->" : "  ";
            string body = o.HasBody ? "" : "  (no body)";
            string vis = o.IsPublic ? "public " : "";
            string self = o.HasThis ? "" : "static ";
            writer.WriteLine($"  {marker} [{o.Index}] {vis}{self}{o.ReturnType.ToDisplayString()} {methodName}{o.Describe()}{body}");
        }
    }

    /// <summary>Stage dump through the pipeline: the IR tree with diagnostics and fidelity (with <paramref name="view"/> = Full, the annotated-IL import views too).</summary>
    static int Dump(List<string> assemblies, string dumpMethod, int overloadIndex, StageDumpView view, bool skipPdb = false, bool simulate = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = OpenSource(assemblyPath, skipPdb, metadata);
            source.SimulateNewRules = simulate;
            // Probe this assembly first so a method in a later one is still found.
            if (IrImporter.Import(source, typeName, methodName, overloadIndex) is null)
                continue;
            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)} (pipeline: next)");
            var result = StageDump.DumpMethod(source, typeName, methodName, view, overloadIndex);
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
    static int DumpDiff(List<string> assemblies, string dumpMethod, int overloadIndex, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = OpenSource(assemblyPath, skipPdb, metadata);
            var function = IrImporter.Import(source, typeName, methodName, overloadIndex);
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
    static int DumpSteps(List<string> assemblies, string dumpMethod, int overloadIndex, int stepLimit, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = OpenSource(assemblyPath, skipPdb, metadata);
            var function = IrImporter.Import(source, typeName, methodName, overloadIndex);
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
    static int DumpFacts(List<string> assemblies, string dumpMethod, int overloadIndex, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = OpenSource(assemblyPath, skipPdb, metadata);
            var function = IrImporter.Import(source, typeName, methodName, overloadIndex);
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
    static int DumpRemarks(List<string> assemblies, string dumpMethod, int overloadIndex, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = OpenSource(assemblyPath, skipPdb, metadata);
            var function = IrImporter.Import(source, typeName, methodName, overloadIndex);
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
    static int DumpLowered(List<string> assemblies, string dumpMethod, int overloadIndex, bool skipPdb = false, bool simulate = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = OpenSource(assemblyPath, skipPdb, metadata);
            source.SimulateNewRules = simulate;
            var function = IrImporter.Import(source, typeName, methodName, overloadIndex);
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
    static int DumpCfg(List<string> assemblies, string dumpMethod, int overloadIndex, bool mermaid = false, bool skipPdb = false)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = OpenSource(assemblyPath, skipPdb, metadata);
            var function = IrImporter.Import(source, typeName, methodName, overloadIndex);
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

    static MetadataSource OpenSource(string assemblyPath, bool skipPdb, MetadataContext metadata)
        => skipPdb
            ? MetadataSource.OpenWithoutSymbols(assemblyPath, context: metadata)
            : MetadataSource.Open(assemblyPath, context: metadata);

    static string Succs(IReadOnlyList<Block> blocks, BlockEdges edges)
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
          --index <N>           with --dump: select overload N when the name
                                resolves to several methods (default 0). A
                                multi-overload name prints its menu to stderr so
                                stdout stays pipe-clean; an out-of-range N fails
                                with the menu.
          --list-overloads      with --dump: list every same-name overload (index,
                                signature, body/no-body) and stop, without dumping.
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
                                --validity-check: measure the lowered output's compile
                                rate instead of the shipped output's. With
                                --fidelity-check: roundtrip the lowered view through the
                                compiler and compare opcode streams.
          --step-limit <N>      with --dump: replay to step N and dump the IR
                                right before that rewrite.
          --il                  with --dump: prepend the annotated-IL import
                                views (raw/typed/structured).
          --skip-pdb            with --dump: ignore any portable PDB so locals
                                render as V_index — deterministic, symbol-
                                independent output regardless of nearby symbols.
          --simulate-new-rules  with --dump: optimistic ("simulate") rendering —
                                force the updated memory-safety rules on even for a
                                legacy module, so unsafe contexts are emitted where
                                the new rules would require them (recoverable cases
                                only: IL-visible ops, pointer-signature calls, and
                                cross-assembly RequiresUnsafe).
          --max-examples <n>    example methods per bucket (default 5)
          --validity-check       compile every decompiled body; report invalid C#
          --compile-cap <n>     cap semantically-bound methods (default 4000)
          --emit-validity-defects <f>    with --validity-check, write per-method defect codes to <f>
          --diff-validity-defects <f>    with --validity-check, diff per-method defects against baseline <f>
          --fidelity-check        decompile, recompile in-context, and compare IL opcodes (semantic fidelity)
          --type-check          whole-type source oracle — compose each public type
                                and compare its namespace, kind, modifiers, and member
                                surface against metadata (syntactic, never binds, so
                                method-body codegen is irrelevant). Issue #1112.
          --bind-check          whole-type binding oracle — compose each public type,
                                stub bodies, and bind against the platform refs; report
                                new CS0104 ambiguous-reference collisions (the
                                undetectable-without-namespace-enumeration class). Known
                                artifacts are allowlisted; nonzero exit on a new one.
                                Issue #1137.
          --gaps                self-contained real-gap view — methods whose
                                raised tree still holds unstructured control flow
                                (a surviving goto) or an unsupported node, bucketed
                                by residual kind. The completeness signal.
          --by-shape            with --gaps: sub-classify the switch-branch and
                                conditional-branch buckets by the structural shape
                                of their residual control flow, so a bucket count
                                becomes a per-shape slice docket. The eh-entangled
                                conditional shape is sub-split further by EH subshape
                                (the #1089 burndown slices).
          --annotation-check      hidden-fact annotation check — the analyzer analog
                                of --fidelity-check. Cross-checks each allocation/
                                unsafety/lifetime annotation against the raw IL
                                opcode at its offset (read independently with the
                                runtime-ported ILReader): PRECISION (every
                                annotation sits on a consistent opcode) and RECALL
                                (every unambiguous witness opcode — box/newarr/
                                localloc/calli — produced its annotation). Exits
                                non-zero on any precision violation.
          --library-report       per-assembly summary: Full %, fully-raised %,
                                validity defects, residual pattern buckets, and
                                examples. Use --json for machine-readable output.
          --unsupported-nodes    report every unsupported IL marker left in the
                                raised tree, grouped by opcode/reason. Use --json
                                for machine-readable output.
          --classify-dec0009     classify DEC0009 unrepresentable metadata-name
                                remarks by generated-name family. Use --json for
                                machine-readable output.
          --dec0009-shapes       alias for --classify-dec0009.
          --generated-fixtures [id|prefix|list]
                                generate selected fixture catalogue entries into a
                                temporary class library, run compile-back, and
                                report by fixture ID and target method. With no
                                selector, run all generated fixtures. Use
                                "list" to list fixture IDs. Add --json for
                                machine-readable list/results.
          --keep-generated-fixtures
                                with --generated-fixtures: keep the temporary
                                project and print its paths.
          --emit-corpus-baseline <f>     run the real-world corpus sensor and write
                                the current JSON baseline to <f>.
          --emit-corpus-snapshot <f>     alias for --emit-corpus-baseline; intended
                                for daily artifact snapshots.
          --diff-corpus-baseline <f>     run the real-world corpus sensor and fail
                                if current metrics regress beyond the tolerances
                                in baseline <f>.
                                Uses --compile-cap as a per-assembly semantic
                                validity cap.
          --emit-corpus-delta <f>        with --diff-corpus-baseline: write
                                changed per-method corpus rows as JSON for
                                reviewer drill-down and targeted fidelity runs.
          --fidelity-method-delta <f>    with --fidelity-check: compile back the
                                current changed methods from a corpus delta JSON.
          --quality-diff-card  with --diff-corpus-baseline: emit a Markdown
                                Decompiler quality diff card generated from the
                                baseline/current corpus snapshots.
          --quality-card-risky with --quality-diff-card: include thin-coverage
                                warnings and targeted-example guidance for risky
                                raise/structuring PRs.
          --corpus-fidelity-cap <n>      with corpus baseline modes: cap methods
                                        (repeat or use comma-separated values to compare multiple caps)
                                checked per assembly by the expensive compile-back
                                fidelity oracle (default 0, not run).
          --corpus-method-cap <n>        with corpus baseline modes: cap the
                                completeness/structuring scan to a deterministic
                                hash-ranked sample of n methods per assembly.
                                Intended for PR quick-corpus artifact runs.
          --top-patterns <n>     with --library-report: show top n patterns
                                overall and per library (default 10).
          --top-libraries <n>    with --library-report: show top n libraries by
                                unsupported-pattern load (default all).
          --pass-impact [pass]  blast-radius sweep — the inverse of --dump --diff.
                                With no pass: histogram of how many corpus methods
                                each pass changes. With a pass name (e.g.
                                'return-merge'): list every method that pass
                                changed. Add --show-diff for each method's hunk.
          --show-diff           with --pass-impact <pass>: print the per-pass diff
                                hunk under each changed method.
          --structuring-stops   tally why StructuringPass leaves containers flat:
                                a per-container histogram of stop reasons (the
                                common-exit merge docket). Honors --cap.
          --postdom-probe       step-4 prototype (#1175): classify each residual
                                conditional-branch method by its post-dominator
                                merge shape (single-merge / return-tail / multi-
                                merge / exit / loop). Read-only sizing of the
                                retained-label slice. Honors --cap.
          --postdom-sample <n>  with --postdom-probe: also print readability
                                sketches for the first n single-merge methods.
          --cap <n>             with --pass-impact/--structuring-stops: stop after
                                n methods (default: unlimited). Bounds a full-CoreLib
                                stage sweep.
        """);
}
