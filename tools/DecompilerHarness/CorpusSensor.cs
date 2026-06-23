using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

internal static class CorpusSensor
{
    const string ConditionalBranchBucket = "structuring: conditional-branch";
    const int RiskyValidityCoverageFloorBasisPoints = 100; // 1.00%
    const int RiskyFidelityCoverageFloorBasisPoints = 10;  // 0.10%
    static readonly string[] ForwardMergeStopReasons =
    [
        "cond-target-past-region",
        "forward-branch-not-region-exit",
    ];

    public static int Run(
        IReadOnlyList<string> assemblies,
        int validityCompileCap,
        int fidelityCompileCap,
        int maxExamples,
        string? emitBaseline,
        string? diffBaseline,
        string? emitDelta,
        bool qualityDiffCard = false,
        bool qualityCardRisky = false,
        int methodCap = int.MaxValue)
    {
        if (assemblies.Count == 0)
        {
            Console.Error.WriteLine("No assemblies supplied for the corpus sensor.");
            return 1;
        }

        if (methodCap <= 0)
        {
            Console.Error.WriteLine("--corpus-method-cap must be greater than zero.");
            return 1;
        }

        if (qualityDiffCard && diffBaseline is null)
        {
            Console.Error.WriteLine("--quality-diff-card requires --diff-corpus-baseline <file>.");
            return 1;
        }
        if (emitDelta is not null && diffBaseline is null)
        {
            Console.Error.WriteLine("--emit-corpus-delta requires --diff-corpus-baseline <file>.");
            return 1;
        }

        var current = Capture(assemblies, validityCompileCap, fidelityCompileCap, maxExamples, methodCap);
        if (!qualityDiffCard)
            PrintSummary(current);

        if (emitBaseline is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(emitBaseline)) ?? ".");
            File.WriteAllText(emitBaseline, JsonSerializer.Serialize(current, JsonOptions()));
            if (!qualityDiffCard)
            {
                Console.WriteLine();
                Console.WriteLine($"Wrote corpus baseline: {emitBaseline}");
            }
        }

        if (diffBaseline is null)
            return current.Metrics.PassBugs > 0 ? 1 : 0;

        var baseline = JsonSerializer.Deserialize<CorpusSensorSnapshot>(File.ReadAllText(diffBaseline), JsonOptions())
            ?? throw new InvalidOperationException($"Could not read corpus baseline '{diffBaseline}'.");
        var regressions = Compare(baseline, current);
        if (emitDelta is not null)
        {
            EmitMethodDelta(emitDelta, baseline, current);
            if (!qualityDiffCard)
            {
                Console.WriteLine();
                Console.WriteLine($"Wrote per-method corpus delta: {emitDelta}");
            }
        }
        if (qualityDiffCard)
        {
            PrintQualityDiffCard(baseline, current, regressions, qualityCardRisky);
            if (emitDelta is not null)
            {
                Console.WriteLine();
                Console.WriteLine($"Per-method delta artifact: `{emitDelta}`");
            }
            return regressions.Length == 0 ? 0 : 1;
        }

        if (regressions.Length == 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Corpus sensor matched baseline: {diffBaseline}");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Corpus sensor regressions:");
        foreach (var regression in regressions)
            Console.WriteLine($"- {regression}");
        return 1;
    }

    static CorpusSensorSnapshot Capture(
        IReadOnlyList<string> assemblies,
        int validityCompileCap,
        int fidelityCompileCap,
        int maxExamples,
        int methodCap)
    {
        var completeness = AnalyzeCompleteness(assemblies, maxExamples, methodCap);
        var methods = completeness.Methods.ToDictionary(MethodKey, StringComparer.Ordinal);
        var validity = AnalyzeValidity(assemblies, validityCompileCap, methods);
        var structuring = AnalyzeStructuring(assemblies, methodCap);
        var fidelity = AnalyzeFidelity(assemblies, fidelityCompileCap, methods);
        var totalMethods = completeness.Assemblies.Sum(assembly => assembly.TotalMethods);
        var fullyRaisedMethods = completeness.FullyRaisedMethods;
        var conditionalBranchMethods = completeness.ResidualBuckets.GetValueOrDefault(ConditionalBranchBucket);
        var forwardMergeContainers = ForwardMergeStopReasons.Sum(reason => structuring.StopReasons.GetValueOrDefault(reason));

        var metrics = new CorpusSensorMetrics(
            totalMethods,
            fullyRaisedMethods,
            RateBasisPoints(fullyRaisedMethods, totalMethods),
            conditionalBranchMethods,
            RateBasisPoints(conditionalBranchMethods, totalMethods),
            forwardMergeContainers,
            RateBasisPoints(forwardMergeContainers, totalMethods),
            validity.FullMalformedMethods,
            validity.SemanticCheckedMethods,
            validity.SemanticDefectMethods,
            completeness.PassBugs + (int)structuring.PassBugs,
            completeness.ResidualBuckets,
            structuring,
            fidelity);

        return new CorpusSensorSnapshot(
            SchemaVersion: 1,
            Description: "#1166 real-world decompiler corpus sensor: #1150 pinned NuGet assemblies plus dotnet-inspect managed assemblies.",
            GeneratedUtc: DateTimeOffset.UtcNow,
            ValidityCompileCap: validityCompileCap,
            FidelityCompileCap: fidelityCompileCap,
            MethodCap: methodCap == int.MaxValue ? null : methodCap,
            Tolerances: CorpusSensorTolerances.Default,
            Assemblies: completeness.Assemblies,
            Methods: methods.Values.OrderBy(MethodKey, StringComparer.Ordinal).ToImmutableArray(),
            Metrics: metrics);
    }

    static CompletenessSensorMetrics AnalyzeCompleteness(IReadOnlyList<string> assemblies, int maxExamples, int methodCap)
    {
        var residualBuckets = new Dictionary<string, int>(StringComparer.Ordinal);
        var assemblyReports = ImmutableArray.CreateBuilder<CorpusAssemblySnapshot>();
        var methodReports = ImmutableArray.CreateBuilder<CorpusMethodSnapshot>();
        int fullyRaised = 0, passBugs = 0;

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            int methods = 0;
            string portablePath = PortablePath(assemblyPath);
            var overloads = new Dictionary<(string Type, string Method), int>();
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssemblyStableSample(source, methodCap))
            {
                int overload = overloads.GetValueOrDefault((typeName, methodName));
                overloads[(typeName, methodName)] = overload + 1;
                methods++;
                string? residual = null;
                string? passBug = null;
                try
                {
                    IrPasses.Run(function);
                }
                catch (Exception ex)
                {
                    passBugs++;
                    passBug = ex.GetType().Name;
                }

                if (passBug is null)
                {
                    residual = Completeness.Residual(function)
                        ?? (function.Fidelity != DecompilationFidelity.Full
                            ? $"fidelity: {BucketFor(function.Diagnostics.FirstOrDefault())}"
                            : null);
                }
                if (residual is null)
                    fullyRaised++;
                else
                    residualBuckets[residual] = residualBuckets.GetValueOrDefault(residual) + 1;
                methodReports.Add(new CorpusMethodSnapshot(
                    source.AssemblyName,
                    portablePath,
                    typeName,
                    methodName,
                    overload,
                    SignatureText(function),
                    function.Fidelity.ToString(),
                    residual is null && passBug is null,
                    residual,
                    passBug,
                    Validity: "not-sampled",
                    FidelityCheck: "not-sampled"));
            }
            assemblyReports.Add(new CorpusAssemblySnapshot(source.AssemblyName, PortablePath(assemblyPath), methods));
        }

        return new CompletenessSensorMetrics(
            assemblyReports.ToImmutable(),
            fullyRaised,
            passBugs,
            residualBuckets,
            methodReports.ToImmutable());
    }

    static ValiditySensorMetrics AnalyzeValidity(
        IReadOnlyList<string> assemblies,
        int cap,
        Dictionary<string, CorpusMethodSnapshot> methods)
    {
        if (cap <= 0)
            return new ValiditySensorMetrics(0, 0, 0);

        var results = new List<ValidityCheck.MethodResult>();
        foreach (var assembly in assemblies)
        {
            var portablePath = PortablePath(assembly);
            foreach (var result in ValidityCheck.Evaluate(assembly, cap))
            {
                results.Add(result);
                string key = MethodKey(portablePath, result.TypeName, result.MethodName, result.Signature);
                if (methods.TryGetValue(key, out var method))
                    methods[key] = method with { Validity = ValidityStatus(result) };
            }
        }
        return new ValiditySensorMetrics(
            results.Count(result => result.IsFull && result.IsMalformed),
            results.Count(result => result.SemanticChecked),
            results.Count(result => result.HasSemanticDefect));
    }

    static StructuringSensorMetrics AnalyzeStructuring(IReadOnlyList<string> assemblies, int methodCap)
    {
        long total = 0, crashes = 0, structured = 0, stoppedContainers = 0, methodsWithStop = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            int assemblyMethods = 0;
            foreach (var (_, _, function) in IrImporter.ImportAssemblyStableSample(source, methodCap))
            {
                assemblyMethods++;
                total++;
                var diagnostics = new StructuringDiagnostics();
                var context = new PassContext(new Stepper(enabled: false), diagnostics);
                try
                {
                    IrPasses.Run(function, IrPasses.Default, context);
                }
                catch
                {
                    crashes++;
                    continue;
                }

                structured += diagnostics.Structured;
                if (diagnostics.Stops.Count > 0)
                    methodsWithStop++;
                foreach (var reason in diagnostics.Stops)
                {
                    stoppedContainers++;
                    reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
                }
            }
        }

        return new StructuringSensorMetrics(total, structured, stoppedContainers, methodsWithStop, crashes, reasons);
    }

    static FidelitySensorMetrics AnalyzeFidelity(
        IReadOnlyList<string> assemblies,
        int cap,
        Dictionary<string, CorpusMethodSnapshot> methods)
    {
        var results = new List<FidelityCheck.CompileBackResult>();
        foreach (var assembly in assemblies)
        {
            var portablePath = PortablePath(assembly);
            foreach (var result in FidelityCheck.Evaluate([assembly], cap, lowered: false))
            {
                results.Add(result);
                string key = MethodKey(portablePath, result.Type, result.Method, result.Signature);
                if (methods.TryGetValue(key, out var method))
                    methods[key] = method with { FidelityCheck = result.Status.ToString() };
            }
        }
        return new FidelitySensorMetrics(
            results.Count,
            results.Count(result => result.Status == FidelityCheck.CompileBackStatus.Exact),
            results.Count(result => result.Status == FidelityCheck.CompileBackStatus.OpcodeDiff),
            results.Count(result => result.Status == FidelityCheck.CompileBackStatus.RecompileFail),
            results.Count(result => result.Status == FidelityCheck.CompileBackStatus.ContextFail),
            results.Count(result => result.Status == FidelityCheck.CompileBackStatus.NotFull));
    }

    static string BucketFor(DecompilerDiagnostic diagnostic)
    {
        if (diagnostic.Id is null)
            return "(typed)";
        if (diagnostic.Id == DiagnosticIds.UnsupportedType)
        {
            var message = diagnostic.Message ?? "(typed)";
            int detail = message.IndexOf('(');
            return detail < 0 ? message : message[..detail].TrimEnd();
        }
        return diagnostic.Message?.Split(' ').ElementAtOrDefault(1) ?? "(typed)";
    }

    static string SignatureText(IrFunction function)
        => $"({string.Join(", ", function.Signature.Parameters.Select(p => p.Type.ToDisplayString()))}) -> {function.Signature.ReturnType.ToDisplayString()}";

    static string MethodKey(CorpusMethodSnapshot method)
        => MethodKey(method.AssemblyPath, method.Type, method.Method, method.Signature);

    static string MethodKey(string assemblyPath, string type, string method, string signature)
        => $"{assemblyPath}!{type}::{method}{signature}";

    static string ValidityStatus(ValidityCheck.MethodResult result)
    {
        if (result.IsMalformed)
            return $"{(result.IsFull ? "full" : "partial")}-malformed:{Codes(result.MalformedDiagnostics)}";
        if (result.SemanticChecked)
            return result.HasSemanticDefect
                ? $"semantic-defect:{Codes(result.SemanticDiagnostics)}"
                : "valid";
        return "syntax-valid";
    }

    static string Codes(IEnumerable<ValidityCheck.ValidityDiagnostic> diagnostics)
        => string.Join(",", diagnostics.Select(d => d.Id).Distinct().Order(StringComparer.Ordinal));

    static string PortablePath(string path)
    {
        var full = Path.GetFullPath(path).Replace('\\', '/');
        const string nugetMarker = "/.nuget/packages/";
        int nuget = full.IndexOf(nugetMarker, StringComparison.OrdinalIgnoreCase);
        if (nuget >= 0)
            return $"nuget:{full[(nuget + nugetMarker.Length)..]}";

        var cwd = Path.GetFullPath(Environment.CurrentDirectory).Replace('\\', '/').TrimEnd('/');
        if (full.StartsWith(cwd + "/", StringComparison.Ordinal))
            return full[(cwd.Length + 1)..];
        return Path.GetFileName(path);
    }

    static ImmutableArray<string> Compare(CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        var tolerance = baseline.Tolerances ?? CorpusSensorTolerances.Default;

        if (current.ValidityCompileCap < baseline.ValidityCompileCap)
            failures.Add($"validity cap lower than baseline (baseline {baseline.ValidityCompileCap}, current {current.ValidityCompileCap})");
        if (current.FidelityCompileCap < baseline.FidelityCompileCap)
            failures.Add($"fidelity cap lower than baseline (baseline {baseline.FidelityCompileCap}, current {current.FidelityCompileCap})");
        if (current.MethodCap != baseline.MethodCap)
            failures.Add($"method cap differs from baseline (baseline {CapText(baseline.MethodCap)}, current {CapText(current.MethodCap)})");
        if (current.Metrics.SemanticCheckedMethods < baseline.Metrics.SemanticCheckedMethods)
            failures.Add($"semantic checked methods lower than baseline (baseline {baseline.Metrics.SemanticCheckedMethods}, current {current.Metrics.SemanticCheckedMethods})");
        if (current.Metrics.Fidelity.CheckedMethods < baseline.Metrics.Fidelity.CheckedMethods)
            failures.Add($"fidelity checked methods lower than baseline (baseline {baseline.Metrics.Fidelity.CheckedMethods}, current {current.Metrics.Fidelity.CheckedMethods})");

        int fullyRaisedDrop = baseline.Metrics.FullyRaisedBasisPoints - current.Metrics.FullyRaisedBasisPoints;
        if (fullyRaisedDrop > tolerance.FullyRaisedDropBasisPoints)
            failures.Add($"fully-raised rate dropped {fullyRaisedDrop} bps (baseline {FormatBps(baseline.Metrics.FullyRaisedBasisPoints)}, current {FormatBps(current.Metrics.FullyRaisedBasisPoints)}, tolerance {tolerance.FullyRaisedDropBasisPoints} bps)");

        int conditionalIncrease = current.Metrics.ConditionalBranchBasisPoints - baseline.Metrics.ConditionalBranchBasisPoints;
        if (conditionalIncrease > tolerance.ConditionalBranchIncreaseBasisPoints)
            failures.Add($"conditional-branch residual rate increased {conditionalIncrease} bps (baseline {FormatBps(baseline.Metrics.ConditionalBranchBasisPoints)}, current {FormatBps(current.Metrics.ConditionalBranchBasisPoints)}, tolerance {tolerance.ConditionalBranchIncreaseBasisPoints} bps)");

        int forwardIncrease = current.Metrics.ForwardMergeBasisPoints - baseline.Metrics.ForwardMergeBasisPoints;
        if (forwardIncrease > tolerance.ForwardMergeIncreaseBasisPoints)
            failures.Add($"forward-merge stop rate increased {forwardIncrease} bps (baseline {FormatBps(baseline.Metrics.ForwardMergeBasisPoints)}, current {FormatBps(current.Metrics.ForwardMergeBasisPoints)}, tolerance {tolerance.ForwardMergeIncreaseBasisPoints} bps)");

        AddCountRegression(failures, "Full malformed methods", baseline.Metrics.FullMalformedMethods, current.Metrics.FullMalformedMethods, tolerance.FullMalformedIncrease);
        AddCountRegression(failures, "semantic defect methods", baseline.Metrics.SemanticDefectMethods, current.Metrics.SemanticDefectMethods, tolerance.SemanticDefectIncrease);
        AddCountRegression(failures, "pass bugs", baseline.Metrics.PassBugs, current.Metrics.PassBugs, tolerance.PassBugIncrease);

        if (baseline.Metrics.Fidelity.CheckedMethods > 0)
        {
            AddCountRegression(failures, "fidelity opcode diffs", baseline.Metrics.Fidelity.OpcodeDiffMethods, current.Metrics.Fidelity.OpcodeDiffMethods, tolerance.FidelityOpcodeDiffIncrease);
            AddCountRegression(failures, "fidelity recompile failures", baseline.Metrics.Fidelity.RecompileFailMethods, current.Metrics.Fidelity.RecompileFailMethods, tolerance.FidelityRecompileFailIncrease);
            AddCountRegression(failures, "fidelity context failures", baseline.Metrics.Fidelity.ContextFailMethods, current.Metrics.Fidelity.ContextFailMethods, tolerance.FidelityContextFailIncrease);
        }

        return failures.ToImmutable();
    }

    static void EmitMethodDelta(string path, CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var baselineMethods = (baseline.Methods ?? []).ToDictionary(MethodKey, StringComparer.Ordinal);
        var currentMethods = (current.Methods ?? []).ToDictionary(MethodKey, StringComparer.Ordinal);
        var rows = ImmutableArray.CreateBuilder<CorpusMethodDeltaRow>();
        foreach (var key in baselineMethods.Keys.Union(currentMethods.Keys).Order(StringComparer.Ordinal))
        {
            baselineMethods.TryGetValue(key, out var before);
            currentMethods.TryGetValue(key, out var after);
            var deltas = MethodDeltas(before, after);
            if (deltas.Length == 0)
                continue;
            rows.Add(new CorpusMethodDeltaRow(
                Method: after?.DisplayMethod ?? before?.DisplayMethod ?? key,
                Assembly: after?.Assembly ?? before?.Assembly ?? "",
                AssemblyPath: after?.AssemblyPath ?? before?.AssemblyPath ?? "",
                Type: after?.Type ?? before?.Type ?? "",
                MethodName: after?.Method ?? before?.Method ?? "",
                Overload: after?.Overload ?? before?.Overload ?? 0,
                Signature: after?.Signature ?? before?.Signature ?? "",
                Baseline: before,
                Current: after,
                Deltas: deltas));
        }
        var artifact = new CorpusMethodDeltaArtifact(
            SchemaVersion: 1,
            GeneratedUtc: DateTimeOffset.UtcNow,
            BaselineGeneratedUtc: baseline.GeneratedUtc,
            CurrentGeneratedUtc: current.GeneratedUtc,
            BaselineHasMethodDetails: baseline.Methods is not null,
            CurrentHasMethodDetails: current.Methods is not null,
            ChangedMethods: rows.ToImmutable());
        File.WriteAllText(path, JsonSerializer.Serialize(artifact, JsonOptions()));
    }

    static ImmutableArray<string> MethodDeltas(CorpusMethodSnapshot? before, CorpusMethodSnapshot? after)
    {
        if (before is null && after is null)
            return [];
        var deltas = ImmutableArray.CreateBuilder<string>();
        if (before is null)
        {
            deltas.Add("added");
            return deltas.ToImmutable();
        }
        if (after is null)
        {
            deltas.Add("removed");
            return deltas.ToImmutable();
        }
        Add("fidelity", before.Fidelity, after.Fidelity);
        Add("fullyRaised", before.FullyRaised, after.FullyRaised);
        Add("residual", before.Residual, after.Residual);
        Add("validity", before.Validity, after.Validity);
        Add("fidelityCheck", before.FidelityCheck, after.FidelityCheck);
        Add("passBug", before.PassBug, after.PassBug);
        return deltas.ToImmutable();

        void Add<T>(string name, T? oldValue, T? newValue)
        {
            if (!EqualityComparer<T?>.Default.Equals(oldValue, newValue))
                deltas.Add(name);
        }
    }

    static void AddCountRegression(ImmutableArray<string>.Builder failures, string name, int baseline, int current, int tolerance)
    {
        int increase = current - baseline;
        if (increase > tolerance)
            failures.Add($"{name} increased by {increase} (baseline {baseline}, current {current}, tolerance {tolerance})");
    }

    static void PrintSummary(CorpusSensorSnapshot snapshot)
    {
        var metrics = snapshot.Metrics;
        Console.WriteLine("# Decompiler corpus sensor");
        Console.WriteLine();
        Console.WriteLine($"Assemblies: {snapshot.Assemblies.Count}");
        Console.WriteLine($"Methods: {metrics.TotalMethods}");
        if (snapshot.MethodCap is { } cap)
            Console.WriteLine($"Sample: hash-stable {Number(cap)} methods per assembly");
        Console.WriteLine($"Fully raised: {metrics.FullyRaisedMethods} ({FormatBps(metrics.FullyRaisedBasisPoints)})");
        Console.WriteLine($"Conditional-branch residual: {metrics.ConditionalBranchMethods} ({FormatBps(metrics.ConditionalBranchBasisPoints)})");
        Console.WriteLine($"Forward-merge stops: {metrics.ForwardMergeStoppedContainers} ({FormatBps(metrics.ForwardMergeBasisPoints)} of methods)");
        if (snapshot.ValidityCompileCap <= 0)
        {
            Console.WriteLine("Full malformed: not run");
            Console.WriteLine("Semantic defects: not run");
        }
        else
        {
            Console.WriteLine($"Full malformed: {metrics.FullMalformedMethods}");
            Console.WriteLine($"Semantic defects: {metrics.SemanticDefectMethods}/{metrics.SemanticCheckedMethods}");
        }
        Console.WriteLine($"Pass bugs: {metrics.PassBugs}");
        if (snapshot.FidelityCompileCap <= 0)
            Console.WriteLine("Fidelity: not run");
        else
            Console.WriteLine($"Fidelity: {metrics.Fidelity.ExactMethods} exact, {metrics.Fidelity.OpcodeDiffMethods} opcode diffs, {metrics.Fidelity.RecompileFailMethods} recompile failures over {metrics.Fidelity.CheckedMethods} checked");
    }

    static void PrintQualityDiffCard(
        CorpusSensorSnapshot baseline,
        CorpusSensorSnapshot current,
        IReadOnlyList<string> regressions,
        bool risky)
    {
        Console.WriteLine("### Decompiler quality diff");
        Console.WriteLine();
        Console.WriteLine($"Corpus: {current.Description} {AssemblyCount(current.Assemblies.Count)}, {Number(current.Metrics.TotalMethods)} methods");
        if (current.MethodCap is { } cap)
            Console.WriteLine($"Sample: hash-stable {Number(cap)} methods per assembly");
        Console.WriteLine($"Correctness coverage: {CoverageSummary(current)}");
        if (risky)
            PrintRiskyCoverageGuidance(current);
        Console.WriteLine();
        Console.WriteLine("| Metric | Baseline | PR | Delta |");
        Console.WriteLine("| --- | ---: | ---: | ---: |");
        PrintMetric(
            "Fully raised",
            CountPercent(baseline.Metrics.FullyRaisedMethods, baseline.Metrics.FullyRaisedBasisPoints),
            CountPercent(current.Metrics.FullyRaisedMethods, current.Metrics.FullyRaisedBasisPoints),
            Delta(current.Metrics.FullyRaisedMethods - baseline.Metrics.FullyRaisedMethods));
        PrintMetric(
            "Conditional-branch residual",
            CountPercent(baseline.Metrics.ConditionalBranchMethods, baseline.Metrics.ConditionalBranchBasisPoints),
            CountPercent(current.Metrics.ConditionalBranchMethods, current.Metrics.ConditionalBranchBasisPoints),
            Delta(current.Metrics.ConditionalBranchMethods - baseline.Metrics.ConditionalBranchMethods));
        PrintMetric(
            "Forward-merge stops",
            CountPercent(baseline.Metrics.ForwardMergeStoppedContainers, baseline.Metrics.ForwardMergeBasisPoints),
            CountPercent(current.Metrics.ForwardMergeStoppedContainers, current.Metrics.ForwardMergeBasisPoints),
            Delta(current.Metrics.ForwardMergeStoppedContainers - baseline.Metrics.ForwardMergeStoppedContainers));
        if (current.ValidityCompileCap <= 0)
        {
            PrintMetric("Full malformed", "not run", "not run", "-");
            PrintMetric("Semantic defects", "not run", "not run", "-");
        }
        else
        {
            PrintMetric(
                "Full malformed",
                Number(baseline.Metrics.FullMalformedMethods),
                Number(current.Metrics.FullMalformedMethods),
                Delta(current.Metrics.FullMalformedMethods - baseline.Metrics.FullMalformedMethods));
            PrintMetric(
                "Semantic defects",
                FractionWithCoverage(baseline.Metrics.SemanticDefectMethods, baseline.Metrics.SemanticCheckedMethods, baseline.Metrics.TotalMethods),
                FractionWithCoverage(current.Metrics.SemanticDefectMethods, current.Metrics.SemanticCheckedMethods, current.Metrics.TotalMethods),
                Delta(current.Metrics.SemanticDefectMethods - baseline.Metrics.SemanticDefectMethods));
        }
        if (current.FidelityCompileCap <= 0)
        {
            PrintMetric("Fidelity diffs", "not run", "not run", "-");
        }
        else
        {
            PrintMetric(
                "Fidelity diffs",
                FidelityWithCoverage(baseline.Metrics.Fidelity, baseline.Metrics.TotalMethods),
                FidelityWithCoverage(current.Metrics.Fidelity, current.Metrics.TotalMethods),
                Delta(current.Metrics.Fidelity.OpcodeDiffMethods - baseline.Metrics.Fidelity.OpcodeDiffMethods));
        }
        PrintMetric(
            "Pass bugs",
            Number(baseline.Metrics.PassBugs),
            Number(current.Metrics.PassBugs),
            Delta(current.Metrics.PassBugs - baseline.Metrics.PassBugs));
        Console.WriteLine();
        Console.WriteLine(regressions.Count == 0
            ? "Verdict: corpus sensor matched baseline tolerances."
            : "Verdict: corpus sensor reported regressions; review before merging.");
        if (regressions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Regressions:");
            foreach (var regression in regressions)
                Console.WriteLine($"- {regression}");
        }
    }

    static void PrintMetric(string metric, string baseline, string current, string delta)
        => Console.WriteLine($"| {metric} | {baseline} | {current} | {delta} |");

    static string CoverageSummary(CorpusSensorSnapshot snapshot)
    {
        var validity = snapshot.ValidityCompileCap <= 0
            ? "validity not run"
            : $"validity sampled {Coverage(snapshot.Metrics.SemanticCheckedMethods, snapshot.Metrics.TotalMethods)}";
        var fidelity = snapshot.FidelityCompileCap <= 0
            ? "fidelity not run"
            : $"fidelity sampled {Coverage(snapshot.Metrics.Fidelity.CheckedMethods, snapshot.Metrics.TotalMethods)}";
        return $"{validity}; {fidelity}";
    }

    static void PrintRiskyCoverageGuidance(CorpusSensorSnapshot snapshot)
    {
        List<string> warnings = [];
        if (snapshot.ValidityCompileCap <= 0)
            warnings.Add("validity not run");
        else if (RateBasisPoints(snapshot.Metrics.SemanticCheckedMethods, snapshot.Metrics.TotalMethods) < RiskyValidityCoverageFloorBasisPoints)
            warnings.Add($"validity below {FormatBps(RiskyValidityCoverageFloorBasisPoints)} floor");

        if (snapshot.FidelityCompileCap <= 0)
            warnings.Add("fidelity not run");
        else if (RateBasisPoints(snapshot.Metrics.Fidelity.CheckedMethods, snapshot.Metrics.TotalMethods) < RiskyFidelityCoverageFloorBasisPoints)
            warnings.Add($"fidelity below {FormatBps(RiskyFidelityCoverageFloorBasisPoints)} floor");

        if (warnings.Count > 0)
        {
            Console.WriteLine($"Risk warning: thin correctness coverage ({string.Join("; ", warnings)}). Add targeted improved examples and still-flat near misses; aggregate numbers are not sufficient alone.");
            return;
        }

        Console.WriteLine("Risk guidance: add targeted improved examples and still-flat near misses; aggregate numbers are not sufficient alone.");
    }

    static string CountPercent(int count, int basisPoints)
        => $"{Number(count)} ({FormatBps(basisPoints)})";

    static string Fraction(int numerator, int denominator)
        => $"{Number(numerator)}/{Number(denominator)}";

    static string FractionWithCoverage(int numerator, int denominator, int total)
        => $"{Fraction(numerator, denominator)} — sampled {Coverage(denominator, total)}";

    static string FidelityWithCoverage(FidelitySensorMetrics metrics, int total)
        => $"opcode-diff {Fraction(metrics.OpcodeDiffMethods, metrics.CheckedMethods)}, exact {Number(metrics.ExactMethods)}, recompile-failed {Number(metrics.RecompileFailMethods)}, context-failed {Number(metrics.ContextFailMethods)}; sampled {Coverage(metrics.CheckedMethods, total)}";

    static string Coverage(int checkedMethods, int totalMethods)
        => $"{Number(checkedMethods)} / {Number(totalMethods)} ({FormatBps(RateBasisPoints(checkedMethods, totalMethods))})";

    static string AssemblyCount(int count)
        => $"{Number(count)} assembl{(count == 1 ? "y" : "ies")}";

    static string CapText(int? cap)
        => cap is { } value ? Number(value) : "uncapped";

    static string Delta(int value)
        => value > 0 ? $"+{Number(value)}" : Number(value);

    static string Number(int value)
        => value.ToString("N0", CultureInfo.InvariantCulture);

    static int RateBasisPoints(int part, int whole)
        => whole <= 0 ? 0 : (int)Math.Round(10_000.0 * part / whole, MidpointRounding.AwayFromZero);

    static string FormatBps(int basisPoints)
        => $"{basisPoints / 100.0:F2}%";

    static JsonSerializerOptions JsonOptions()
        => new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

internal sealed record CorpusSensorSnapshot(
    int SchemaVersion,
    string Description,
    DateTimeOffset GeneratedUtc,
    int ValidityCompileCap,
    int FidelityCompileCap,
    int? MethodCap,
    CorpusSensorTolerances? Tolerances,
    IReadOnlyList<CorpusAssemblySnapshot> Assemblies,
    IReadOnlyList<CorpusMethodSnapshot>? Methods,
    CorpusSensorMetrics Metrics);

internal sealed record CorpusAssemblySnapshot(string Assembly, string Path, int TotalMethods);

internal sealed record CompletenessSensorMetrics(
    IReadOnlyList<CorpusAssemblySnapshot> Assemblies,
    int FullyRaisedMethods,
    int PassBugs,
    IReadOnlyDictionary<string, int> ResidualBuckets,
    IReadOnlyList<CorpusMethodSnapshot> Methods);

internal sealed record CorpusMethodSnapshot(
    string Assembly,
    string AssemblyPath,
    string Type,
    string Method,
    int Overload,
    string Signature,
    string Fidelity,
    bool FullyRaised,
    string? Residual,
    string? PassBug,
    string Validity,
    string FidelityCheck)
{
    public string DisplayMethod => $"{Assembly}!{Type}::{Method}#{Overload}";
}

internal sealed record CorpusMethodDeltaArtifact(
    int SchemaVersion,
    DateTimeOffset GeneratedUtc,
    DateTimeOffset BaselineGeneratedUtc,
    DateTimeOffset CurrentGeneratedUtc,
    bool BaselineHasMethodDetails,
    bool CurrentHasMethodDetails,
    IReadOnlyList<CorpusMethodDeltaRow> ChangedMethods);

internal sealed record CorpusMethodDeltaRow(
    string Method,
    string Assembly,
    string AssemblyPath,
    string Type,
    string MethodName,
    int Overload,
    string Signature,
    CorpusMethodSnapshot? Baseline,
    CorpusMethodSnapshot? Current,
    IReadOnlyList<string> Deltas);

internal sealed record ValiditySensorMetrics(
    int FullMalformedMethods,
    int SemanticCheckedMethods,
    int SemanticDefectMethods);

internal sealed record CorpusSensorMetrics(
    int TotalMethods,
    int FullyRaisedMethods,
    int FullyRaisedBasisPoints,
    int ConditionalBranchMethods,
    int ConditionalBranchBasisPoints,
    int ForwardMergeStoppedContainers,
    int ForwardMergeBasisPoints,
    int FullMalformedMethods,
    int SemanticCheckedMethods,
    int SemanticDefectMethods,
    int PassBugs,
    IReadOnlyDictionary<string, int> ResidualBuckets,
    StructuringSensorMetrics Structuring,
    FidelitySensorMetrics Fidelity);

internal sealed record StructuringSensorMetrics(
    long TotalMethods,
    long StructuredContainers,
    long StoppedContainers,
    long MethodsWithStops,
    long PassBugs,
    IReadOnlyDictionary<string, int> StopReasons);

internal sealed record FidelitySensorMetrics(
    int CheckedMethods,
    int ExactMethods,
    int OpcodeDiffMethods,
    int RecompileFailMethods,
    int ContextFailMethods,
    int NotFullMethods);

internal sealed record CorpusSensorTolerances(
    int FullyRaisedDropBasisPoints,
    int ConditionalBranchIncreaseBasisPoints,
    int ForwardMergeIncreaseBasisPoints,
    int FullMalformedIncrease,
    int SemanticDefectIncrease,
    int PassBugIncrease,
    int FidelityOpcodeDiffIncrease,
    int FidelityRecompileFailIncrease,
    int FidelityContextFailIncrease)
{
    public static CorpusSensorTolerances Default { get; } = new(
        FullyRaisedDropBasisPoints: 10,
        ConditionalBranchIncreaseBasisPoints: 10,
        ForwardMergeIncreaseBasisPoints: 10,
        FullMalformedIncrease: 0,
        SemanticDefectIncrease: 0,
        PassBugIncrease: 0,
        FidelityOpcodeDiffIncrease: 0,
        FidelityRecompileFailIncrease: 0,
        FidelityContextFailIncrease: 0);
}
