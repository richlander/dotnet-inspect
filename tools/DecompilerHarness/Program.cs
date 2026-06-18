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
/// Differential harness for the decompiler pipelines — the asmdiffs analog
/// from docs/decompiler-pipeline.md. With one pipeline it inventories health
/// (render rate, exception buckets) across whole assemblies; with a baseline
/// and a candidate it reports agreement and categorized diffs. The parity
/// gate for the pipeline replacement is measured here.
/// </summary>
static class Program
{
    // The replacement pipeline registers here under "next" when it exists.
    static readonly Dictionary<string, Func<MethodBodyContext, string>> Pipelines = new(StringComparer.OrdinalIgnoreCase)
    {
        ["current"] = CSharpEmitter.Emit,
    };

    /// <summary>Methods slower than this are listed in the report. True hangs stall the sweep visibly (CI applies job-level timeouts); a nested-task timeout here would misreport pool starvation as a product hang.</summary>
    const double SlowThresholdSeconds = 2.0;

    static int Main(string[] args)
    {
        List<string> inputs = [];
        string baselineName = "current";
        string? candidateName = null;
        string? reportPath = null;
        string? jsonPath = null;
        int maxExamples = 5;

        string? dumpMethod = null;
        string pipelineName = "current";
        bool gradeSource = false;
        int gradeCap = 1500;
        bool compileCheck = false;
        int compileCap = 4000;
        string? emitDefects = null;
        string? diffDefects = null;
        bool compileBack = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--dump": dumpMethod = args[++i]; break;
                case "--pipeline": pipelineName = args[++i]; break;
                case "--baseline": baselineName = args[++i]; break;
                case "--candidate": candidateName = args[++i]; break;
                case "--report": reportPath = args[++i]; break;
                case "--json": jsonPath = args[++i]; break;
                case "--max-examples": maxExamples = int.Parse(args[++i]); break;
                case "--grade-source": gradeSource = true; break;
                case "--grade-cap": gradeCap = int.Parse(args[++i]); break;
                case "--compile-check": compileCheck = true; break;
                case "--compile-cap": compileCap = int.Parse(args[++i]); break;
                case "--emit-defects": emitDefects = args[++i]; break;
                case "--diff-defects": diffDefects = args[++i]; break;
                case "--compile-back": compileBack = true; break;
                case "--help" or "-h": PrintUsage(); return 0;
                default: inputs.Add(args[i]); break;
            }
        }

        var assemblies = ResolveAssemblies(inputs);
        if (assemblies.Count == 0)
            return Fail("No managed assemblies found in the given inputs.");

        if (gradeSource)
            return SourceGrader.Run(assemblies, gradeCap, maxExamples);

        if (compileCheck || emitDefects is not null || diffDefects is not null)
            return CompileChecker.Run(assemblies, compileCap, maxExamples, emitDefects, diffDefects);

        if (compileBack)
            return CompileBack.Run(assemblies, compileCap, maxExamples);

        if (candidateName?.Equals("next", StringComparison.OrdinalIgnoreCase) == true)
            return DiffNext(assemblies, maxExamples, reportPath);

        if (pipelineName.Equals("next", StringComparison.OrdinalIgnoreCase))
            return dumpMethod is not null ? DumpNext(assemblies, dumpMethod) : SweepNext(assemblies);

        if (!Pipelines.TryGetValue(baselineName, out var baseline))
            return Fail($"Unknown baseline pipeline '{baselineName}'. Known: {string.Join(", ", Pipelines.Keys)}");
        Func<MethodBodyContext, string>? candidate = null;
        if (candidateName is not null && !Pipelines.TryGetValue(candidateName, out candidate))
            return Fail($"Unknown candidate pipeline '{candidateName}'. Known: {string.Join(", ", Pipelines.Keys)}");

        if (dumpMethod is not null)
            return DumpStages(assemblies, dumpMethod);

        var report = new StringBuilder();
        report.AppendLine("# Decompiler Harness Report");
        report.AppendLine();
        report.AppendLine(candidate is null
            ? $"Mode: inventory (pipeline: {baselineName})"
            : $"Mode: diff (baseline: {baselineName}, candidate: {candidateName})");
        report.AppendLine();

        List<FailureRecord> failures = [];
        long grandTotal = 0, grandRendered = 0, grandAgree = 0, grandCompared = 0;

        foreach (var assemblyPath in assemblies)
        {
            var sweep = Sweep(assemblyPath, baseline, candidate);
            grandTotal += sweep.Total;
            grandRendered += sweep.Rendered;
            grandAgree += sweep.Agree;
            grandCompared += sweep.Compared;
            failures.AddRange(sweep.Failures);
            AppendAssemblyReport(report, sweep, candidate is not null, maxExamples);
            Console.WriteLine(SummaryLine(sweep, candidate is not null));
        }

        report.AppendLine("## Totals");
        report.AppendLine();
        report.AppendLine($"- Methods: {grandTotal}");
        report.AppendLine($"- Rendered: {grandRendered} ({Percent(grandRendered, grandTotal)})");
        if (candidate is not null)
            report.AppendLine($"- Agreement: {grandAgree}/{grandCompared} ({Percent(grandAgree, grandCompared)})");

        Console.WriteLine();
        Console.WriteLine(candidate is null
            ? $"TOTAL: {grandRendered}/{grandTotal} rendered ({Percent(grandRendered, grandTotal)})"
            : $"TOTAL: {grandAgree}/{grandCompared} agree ({Percent(grandAgree, grandCompared)}); {grandRendered}/{grandTotal} rendered");

        if (reportPath is not null)
        {
            File.WriteAllText(reportPath, report.ToString());
            Console.WriteLine($"Report: {reportPath}");
        }
        if (jsonPath is not null)
        {
            using var stream = File.Create(jsonPath);
            JsonSerializer.Serialize(stream, failures, FailureJson.Default.ListFailureRecord);
            Console.WriteLine($"JSON: {jsonPath}");
        }
        return 0;
    }

    /// <summary>
    /// Inventory sweep of the replacement pipeline: fidelity histogram plus
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
    /// The parity scoreboard: every method through both pipelines, compared
    /// as trimmed C# text. Both sides enumerate metadata in the same order,
    /// so the zip is positional. Candidate output only counts when the IR
    /// imported at Full fidelity — Partial methods are not comparable yet.
    /// </summary>
    static int DiffNext(List<string> assemblies, int maxExamples, string? reportPath)
    {
        long agree = 0, differ = 0, baselineFailed = 0, candidatePartial = 0, total = 0;
        long candidateWorse = 0, baselineWorse = 0, likelyCosmetic = 0, uncertain = 0;
        var diffBuckets = new Dictionary<string, (int Count, string Example)>();

        foreach (var assemblyPath in assemblies)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var reader = peReader.GetMetadataReader();
            using var source = MetadataSource.Open(assemblyPath);
            using var candidates = IrImporter.ImportAssembly(source).GetEnumerator();

            foreach (var typeDefHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if (method.RelativeVirtualAddress == 0)
                        continue;
                    if (!candidates.MoveNext())
                        return Fail("pipeline enumerations diverged — importer and harness disagree about method order");
                    var (typeName, methodName, function) = candidates.Current;
                    total++;

                    string? baseline = null;
                    try
                    {
                        var context = MethodBodyContext.Create(peReader, reader, method);
                        if (context is not null)
                            baseline = CSharpEmitter.Emit(context);
                    }
                    catch
                    {
                        baselineFailed++;
                        continue;
                    }
                    if (baseline is null)
                        continue;

                    if (function.Fidelity != DecompilationFidelity.Full)
                    {
                        candidatePartial++;
                        continue;
                    }
                    var candidate = CSharpPrinter.PrintRaised(function);
                    if (candidate.Output is null)
                    {
                        candidatePartial++;
                        continue;
                    }

                    if (Normalize(baseline) == Normalize(candidate.Output))
                    {
                        agree++;
                    }
                    else
                    {
                        differ++;
                        string nb = Normalize(baseline), nc = Normalize(candidate.Output);
                        switch (Classify(nb, nc))
                        {
                            case DiffClass.CandidateWorse: candidateWorse++; break;
                            case DiffClass.BaselineWorse: baselineWorse++; break;
                            case DiffClass.LikelyCosmetic: likelyCosmetic++; break;
                            default: uncertain++; break;
                        }
                        string bucket = FirstDiffLine(nb, nc);
                        if (!diffBuckets.TryGetValue(bucket, out var entry))
                            entry = (0, $"{typeName}::{methodName}");
                        diffBuckets[bucket] = (entry.Count + 1, entry.Example);
                    }
                }
            }
        }

        long compared = agree + differ;
        Console.WriteLine($"PARITY: {agree}/{compared} agree ({Percent(agree, compared)}) of comparable methods");
        Console.WriteLine($"  total {total}; candidate Partial {candidatePartial}; baseline failed {baselineFailed}");
        // The disagreements are not all gaps. These signals split them so the
        // real burndown — what the new pipeline still does WORSE — is visible
        // separately from where it already wins or merely differs cosmetically.
        Console.WriteLine($"  of {differ} disagreements:");
        Console.WriteLine($"    candidate-worse (goto/unraised/comment, baseline cleaner): {candidateWorse}");
        Console.WriteLine($"    baseline-worse  (:: / S_in_ / comment, candidate cleaner): {baselineWorse}");
        Console.WriteLine($"    likely cosmetic (equal after namespace-qualifier strip):  {likelyCosmetic}");
        Console.WriteLine($"    uncertain       (both clean, needs source):               {uncertain}");
        long worstCaseGap = candidatePartial + candidateWorse + uncertain;
        Console.WriteLine($"  REAL-GAP burndown: {candidatePartial + candidateWorse} known + up to {uncertain} uncertain "
            + $"= {candidatePartial + candidateWorse}..{worstCaseGap} of {total} ({Percent(candidatePartial + candidateWorse, total)}..{Percent(worstCaseGap, total)})");
        Console.WriteLine("Top differences:");
        foreach (var bucket in diffBuckets.OrderByDescending(b => b.Value.Count).Take(maxExamples * 3))
            Console.WriteLine($"  {bucket.Value.Count,7}  {bucket.Key}  e.g. {bucket.Value.Example}");

        if (reportPath is not null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Parity Report (current vs next)").AppendLine();
            sb.AppendLine($"- Agree: {agree}/{compared} ({Percent(agree, compared)})");
            sb.AppendLine($"- Candidate Partial (not comparable): {candidatePartial}");
            sb.AppendLine();
            sb.AppendLine("| Count | First difference | Example |");
            sb.AppendLine("| --- | --- | --- |");
            foreach (var bucket in diffBuckets.OrderByDescending(b => b.Value.Count))
                sb.AppendLine($"| {bucket.Value.Count} | {Escape(bucket.Key)} | {Escape(bucket.Value.Example)} |");
            File.WriteAllText(reportPath, sb.ToString());
            Console.WriteLine($"Report: {reportPath}");
        }
        return 0;
    }

    /// <summary>
    /// Canonicalizes settled taste deltas before comparison so parity
    /// measures structure: the taste doc fixes null tests as is-forms, and
    /// the frozen baseline still spells ==/!= — both sides canonicalize to
    /// the is-form.
    /// </summary>
    static string Normalize(string text) => text
        .ReplaceLineEndings("\n")
        .Replace(" != null", " is not null")
        .Replace(" == null", " is null")
        .TrimEnd();

    enum DiffClass { CandidateWorse, BaselineWorse, LikelyCosmetic, Uncertain }

    /// <summary>
    /// A coarse, deliberately conservative split of a disagreement. Strong
    /// structural tells win first (an unraised goto or a baseline <c>::</c> are
    /// unambiguous); only tell-free diffs fall to the cosmetic/uncertain check,
    /// so a real gap is never hidden behind a cosmetic verdict. "Uncertain" is
    /// the honest bucket that still needs the source corpus to grade.
    /// </summary>
    static DiffClass Classify(string baseline, string candidate)
    {
        bool candidateGap = CandidateGap(candidate);
        bool baselineGap = BaselineGap(baseline);
        if (candidateGap && !baselineGap)
            return DiffClass.CandidateWorse;
        if (baselineGap && !candidateGap)
            return DiffClass.BaselineWorse;
        if (StripQualifiers(baseline) == StripQualifiers(candidate))
            return DiffClass.LikelyCosmetic;
        return DiffClass.Uncertain;
    }

    /// <summary>The new pipeline left something unraised or unrepresentable.</summary>
    static bool CandidateGap(string text)
        => text.Contains("goto IL_", StringComparison.Ordinal)
            || text.Contains("Monitor.Enter(", StringComparison.Ordinal)
            || text.Contains("/* ", StringComparison.Ordinal)
            || text.Contains("// leave", StringComparison.Ordinal)
            || text.Contains("// endf", StringComparison.Ordinal);

    /// <summary>The old emitter emitted an IL artifact or gave up with a comment.</summary>
    static bool BaselineGap(string text)
        => text.Contains("::", StringComparison.Ordinal)
            || text.Contains("S_in_", StringComparison.Ordinal)
            || text.Contains("/* ", StringComparison.Ordinal);

    /// <summary>Collapses runs of <c>Namespace.</c> qualifiers symmetrically; a no-tell diff equal afterwards is namespace verbosity, not a gap.</summary>
    static string StripQualifiers(string text) => Regex.Replace(text, @"(?<![\w.])(?:[A-Z][A-Za-z0-9_]*\.)+", "");

    /// <summary>Stage dump through the replacement pipeline: the IR tree with diagnostics and fidelity.</summary>
    static int DumpNext(List<string> assemblies, string dumpMethod)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath);
            var function = IrImporter.Import(source, typeName, methodName);
            if (function is null)
                continue;
            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)} (pipeline: next)");
            Console.WriteLine();
            Console.WriteLine("==== IR (typed tree after import) ====");
            Console.Write(IrPrinter.Dump(function));
            foreach (var pass in IrPasses.Default)
            {
                IrPasses.Run(function, [pass]);
                Console.WriteLine();
                Console.WriteLine($"==== IR (after {pass.Name}) ====");
                Console.Write(IrPrinter.Dump(function));
            }
            Console.WriteLine();
            Console.WriteLine("==== C# (lowered; structure not yet raised) ====");
            var printed = CSharpPrinter.Print(function);
            Console.WriteLine(printed.Output ?? string.Join("\n", printed.Diagnostics.Select(d => $"// {d}")));
            return 0;
        }
        return Fail($"Method '{dumpMethod}' not found (or has no IL body) in the given assemblies.");
    }

    /// <summary>
    /// JitDump for the decompiler: every stage of one method's analysis as a
    /// projection of the shared MethodAnalysis — raw IL, typed IL, structured
    /// IL, then C#. The IL projections come from pre-transform stages, so
    /// what this prints is exactly what each pipeline layer saw.
    /// </summary>
    static int DumpStages(List<string> assemblies, string dumpMethod)
    {
        int separator = dumpMethod.IndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
            return Fail("--dump expects Namespace.Type::Method (metadata type name, e.g. System.Collections.Generic.Stack`1::Push)");
        string typeName = dumpMethod[..separator];
        string methodName = dumpMethod[(separator + 2)..];

        foreach (var assemblyPath in assemblies)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var context = MethodBodyContext.Create(peReader, typeName, methodName);
            if (context is null)
                continue;

            var analysis = MethodAnalysis.Create(context);
            Console.WriteLine($"// {dumpMethod} in {Path.GetFileName(assemblyPath)}");
            foreach (var (title, render) in new (string, Func<DecompilerResult>)[]
            {
                ("IL (raw)", () => AnnotatedILEmitter.Decompile(analysis, ILAnnotationDepth.Raw)),
                ("IL (typed: per-instruction stack states)", () => AnnotatedILEmitter.Decompile(analysis, ILAnnotationDepth.Typed)),
                ("IL (structured: blocks + exception regions)", () => AnnotatedILEmitter.Decompile(analysis, ILAnnotationDepth.Structured)),
                ("C# (after raising transforms + structuring)", () => CSharpEmitter.Decompile(analysis)),
            })
            {
                Console.WriteLine();
                Console.WriteLine($"==== {title} ====");
                var result = render();
                Console.WriteLine(result.Output ?? string.Join("\n", result.Diagnostics.Select(d => $"// {d}")));
            }
            return 0;
        }
        return Fail($"Method '{dumpMethod}' not found (or has no IL body) in the given assemblies.");
    }

    static SweepResult Sweep(string assemblyPath, Func<MethodBodyContext, string> baseline, Func<MethodBodyContext, string>? candidate)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var outcomes = new ConcurrentBag<MethodResult>();
        var slow = new ConcurrentBag<(string Method, double Seconds)>();
        Parallel.ForEach(reader.TypeDefinitions, tdh =>
        {
            var td = reader.GetTypeDefinition(tdh);
            string typeName = TypeDisplayName(reader, td);
            foreach (var mh in td.GetMethods())
            {
                var method = reader.GetMethodDefinition(mh);
                if (method.RelativeVirtualAddress == 0)
                    continue;
                string id = $"{typeName}::{reader.GetString(method.Name)}";

                MethodBodyContext? context;
                try
                {
                    context = MethodBodyContext.Create(peReader, reader, method);
                }
                catch (Exception ex)
                {
                    outcomes.Add(new MethodResult(id, Kind.ContextFailed, Bucket(ex)));
                    continue;
                }
                if (context is null)
                {
                    outcomes.Add(new MethodResult(id, Kind.ContextFailed, "null context"));
                    continue;
                }

                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                var (baseText, baseBucket) = Run(baseline, context);
                if (candidate is null)
                {
                    RecordIfSlow(slow, id, started);
                    outcomes.Add(baseText is not null
                        ? new MethodResult(id, Kind.Rendered, null)
                        : new MethodResult(id, Kind.Threw, baseBucket));
                    continue;
                }

                var (candText, candBucket) = Run(candidate, context);
                RecordIfSlow(slow, id, started);
                outcomes.Add((baseText, candText) switch
                {
                    (not null, not null) when baseText == candText => new MethodResult(id, Kind.Agree, null),
                    (not null, not null) => new MethodResult(id, Kind.Differ, FirstDiffLine(baseText, candText)),
                    (not null, null) => new MethodResult(id, Kind.CandidateThrew, candBucket),
                    (null, not null) => new MethodResult(id, Kind.BaselineThrew, baseBucket),
                    _ => new MethodResult(id, Kind.BothThrew, $"baseline: {baseBucket}; candidate: {candBucket}"),
                });
            }
        });

        return SweepResult.From(Path.GetFileName(assemblyPath), outcomes, slow);
    }

    static (string? Text, string? Bucket) Run(Func<MethodBodyContext, string> pipeline, MethodBodyContext context)
    {
        try
        {
            return (pipeline(context), null);
        }
        catch (Exception ex)
        {
            return (null, Bucket(ex));
        }
    }

    static void RecordIfSlow(ConcurrentBag<(string Method, double Seconds)> slow, string id, long started)
    {
        double seconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
        if (seconds >= SlowThresholdSeconds)
            slow.Add((id, seconds));
    }

    /// <summary>Buckets an exception by type and the topmost decompiler frame, so one bug is one bucket.</summary>
    static string Bucket(Exception ex)
    {
        string frame = "?";
        foreach (var line in (ex.StackTrace ?? "").Split('\n'))
        {
            int at = line.IndexOf("at DotnetInspector.", StringComparison.Ordinal);
            if (at < 0)
                continue;
            string site = line[(at + 3)..].Trim();
            int paren = site.IndexOf('(');
            frame = paren > 0 ? site[..paren] : site;
            break;
        }
        return $"{ex.GetType().Name} @ {frame}";
    }

    static string FirstDiffLine(string baseline, string candidate)
    {
        var a = baseline.Split('\n');
        var b = candidate.Split('\n');
        for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            string left = i < a.Length ? a[i].Trim() : "<end>";
            string right = i < b.Length ? b[i].Trim() : "<end>";
            if (left != right)
                return $"`{Truncate(left, 60)}` vs `{Truncate(right, 60)}`";
        }
        return "<whitespace only>";
    }

    static void AppendAssemblyReport(StringBuilder report, SweepResult sweep, bool diffMode, int maxExamples)
    {
        report.AppendLine($"## {sweep.Assembly}");
        report.AppendLine();
        report.AppendLine($"- Methods with bodies: {sweep.Total}");
        report.AppendLine($"- Rendered: {sweep.Rendered} ({Percent(sweep.Rendered, sweep.Total)})");
        if (diffMode)
            report.AppendLine($"- Agreement: {sweep.Agree}/{sweep.Compared} ({Percent(sweep.Agree, sweep.Compared)})");
        foreach (var (method, seconds) in sweep.Slow.OrderByDescending(x => x.Seconds))
            report.AppendLine($"- Slow: {Escape(method)} ({seconds:F1}s)");
        if (sweep.Failures.Count == 0)
        {
            report.AppendLine();
            return;
        }

        report.AppendLine();
        report.AppendLine("| Count | Kind | Bucket | Example |");
        report.AppendLine("| --- | --- | --- | --- |");
        foreach (var group in sweep.Failures
            .GroupBy(f => (f.Kind, f.Bucket))
            .OrderByDescending(g => g.Count()))
        {
            var examples = group.Take(maxExamples).Select(f => f.Method);
            report.AppendLine($"| {group.Count()} | {group.Key.Kind} | {Escape(group.Key.Bucket ?? "")} | {Escape(string.Join("; ", examples))} |");
        }
        report.AppendLine();
    }

    static string SummaryLine(SweepResult sweep, bool diffMode) => diffMode
        ? $"{sweep.Assembly}: {sweep.Agree}/{sweep.Compared} agree ({Percent(sweep.Agree, sweep.Compared)}), {sweep.Failures.Count} failures"
        : $"{sweep.Assembly}: {sweep.Rendered}/{sweep.Total} rendered ({Percent(sweep.Rendered, sweep.Total)}), {sweep.Failures.Count} failures";

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

        options:
          --dump <T::M>         print every stage projection for one method
                                (e.g. --dump 'System.String::IsNullOrEmpty')
          --pipeline <name>     'current' (default) or 'next' (replacement
                                pipeline: fidelity inventory and IR dumps)
          --baseline <name>     pipeline to run (default: current)
          --candidate <name>    second pipeline; enables diff mode
          --report <path>       write a markdown report
          --json <path>         write failure/diff records as JSON
          --max-examples <n>    example methods per bucket in the report (default 5)
          --grade-source        grade candidate vs original source via SourceLink
          --grade-cap <n>       cap graded methods (default 1500)
          --compile-check       compile every decompiled body; report invalid C#
          --compile-cap <n>     cap semantically-bound methods (default 4000)
          --emit-defects <f>    with --compile-check, write per-method defect codes to <f>
          --diff-defects <f>    with --compile-check, diff per-method defects against baseline <f>
          --compile-back        decompile, recompile in-context, and compare IL opcodes (semantic fidelity)
        """);
}

enum Kind { Rendered, Threw, ContextFailed, Agree, Differ, CandidateThrew, BaselineThrew, BothThrew }

record MethodResult(string Id, Kind Kind, string? Bucket);

record FailureRecord(string Assembly, string Method, string Kind, string? Bucket);

record SweepResult(string Assembly, long Total, long Rendered, long Agree, long Compared, List<FailureRecord> Failures, List<(string Method, double Seconds)> Slow)
{
    public static SweepResult From(string assembly, IEnumerable<MethodResult> outcomes, IEnumerable<(string Method, double Seconds)> slow)
    {
        long total = 0, rendered = 0, agree = 0, compared = 0;
        List<FailureRecord> failures = [];
        foreach (var outcome in outcomes)
        {
            total++;
            switch (outcome.Kind)
            {
                case Kind.Rendered:
                    rendered++;
                    break;
                case Kind.Agree:
                    rendered++; agree++; compared++;
                    break;
                case Kind.Differ:
                    rendered++; compared++;
                    failures.Add(new FailureRecord(assembly, outcome.Id, outcome.Kind.ToString(), outcome.Bucket));
                    break;
                default:
                    failures.Add(new FailureRecord(assembly, outcome.Id, outcome.Kind.ToString(), outcome.Bucket));
                    break;
            }
        }
        return new SweepResult(assembly, total, rendered, agree, compared, failures, [.. slow]);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(List<FailureRecord>))]
[System.Text.Json.Serialization.JsonSourceGenerationOptions(WriteIndented = true)]
partial class FailureJson : System.Text.Json.Serialization.JsonSerializerContext;
