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
        IReadOnlyList<int> fidelityCompileCaps,
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

        var (current, fidelityReports) = Capture(assemblies, validityCompileCap, fidelityCompileCaps, maxExamples, methodCap);
        if (!qualityDiffCard)
            PrintSummary(current, fidelityReports);

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
        var regressions = Compare(baseline, current, fidelityReports, gateAggregateRates: !qualityDiffCard || qualityCardRisky);
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

    static (CorpusSensorSnapshot Snapshot, ImmutableArray<FidelityCapReport> Reports) Capture(
        IReadOnlyList<string> assemblies,
        int validityCompileCap,
        IReadOnlyList<int> fidelityCompileCaps,
        int maxExamples,
        int methodCap)
    {
        var completeness = AnalyzeCompleteness(assemblies, maxExamples, methodCap);
        var methods = completeness.Methods.ToDictionary(MethodKey, StringComparer.Ordinal);
        var validity = AnalyzeValidity(assemblies, validityCompileCap, methods);
        var structuring = AnalyzeStructuring(assemblies, methodCap);
        var totalMethods = completeness.Assemblies.Sum(assembly => assembly.TotalMethods);
        var fullyRaisedMethods = completeness.FullyRaisedMethods;
        var conditionalBranchMethods = completeness.ResidualBuckets.GetValueOrDefault(ConditionalBranchBucket);
        var forwardMergeContainers = ForwardMergeStopReasons.Sum(reason => structuring.StopReasons.GetValueOrDefault(reason));
        var requestedCaps = fidelityCompileCaps.Where(cap => cap > 0).Distinct().ToArray();
        var primaryFidelityCap = requestedCaps.FirstOrDefault();
        var fidelityReports = AnalyzeFidelity(assemblies, fidelityCompileCaps, methods, primaryFidelityCap);
        var selectedFidelity = fidelityReports.FirstOrDefault(report => report.Cap == primaryFidelityCap)?.Metrics
            ?? fidelityReports.LastOrDefault()?.Metrics
            ?? new FidelitySensorMetrics(0, 0, 0, 0, 0, 0);

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
            selectedFidelity);

        var snapshot = new CorpusSensorSnapshot(
            SchemaVersion: 1,
            Description: "#1166 real-world decompiler corpus sensor: #1150 pinned NuGet assemblies plus dotnet-inspect managed assemblies.",
            GeneratedUtc: DateTimeOffset.UtcNow,
            ValidityCompileCap: validityCompileCap,
            FidelityCompileCap: primaryFidelityCap,
            MethodCap: methodCap == int.MaxValue ? null : methodCap,
            Tolerances: CorpusSensorTolerances.Default,
            Assemblies: completeness.Assemblies,
            Methods: methods.Values.OrderBy(MethodKey, StringComparer.Ordinal).ToImmutableArray(),
            Metrics: metrics);

        return (snapshot, fidelityReports);
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
                if (passBug is null && residual is null)
                {
                    fullyRaised++;
                }
                else if (residual is not null)
                {
                    residualBuckets[residual] = residualBuckets.GetValueOrDefault(residual) + 1;
                }
                methodReports.Add(new CorpusMethodSnapshot(
                    source.AssemblyName,
                    portablePath,
                    typeName,
                    methodName,
                    overload,
                    CorpusMethodIdentity.SignatureText(function.Signature),
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

    static ImmutableArray<FidelityCapReport> AnalyzeFidelity(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<int> caps,
        Dictionary<string, CorpusMethodSnapshot> methods,
        int primaryCap)
    {
        var reports = ImmutableArray.CreateBuilder<FidelityCapReport>();
        foreach (var cap in caps.Where(cap => cap > 0).Distinct().OrderBy(cap => cap))
        {
            var usefulResults = new List<FidelityCheck.CompileBackResult>();
            var allResults = new List<FidelityCheck.CompileBackResult>();
            foreach (var assembly in assemblies)
            {
                var portablePath = PortablePath(assembly);
                var assemblyUsefulResults = FidelityCheck.Evaluate([assembly], cap, lowered: false, includeAllResults: false);
                usefulResults.AddRange(assemblyUsefulResults);
                if (cap == primaryCap)
                {
                    foreach (var result in assemblyUsefulResults)
                    {
                        string key = MethodKey(portablePath, result.Type, result.Method, result.Signature);
                        if (methods.TryGetValue(key, out var methodSnapshot))
                            methods[key] = methodSnapshot with { FidelityCheck = result.Status.ToString() };
                    }
                }
                allResults.AddRange(FidelityCheck.Evaluate([assembly], cap, lowered: false, includeAllResults: true));
            }
            var metrics = new FidelitySensorMetrics(
                usefulResults.Count,
                usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.Exact),
                usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.OpcodeDiff),
                usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.RecompileFail),
                usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.ContextFail),
                usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.NotFull));
            var contextBuckets = FidelityCheck.SummarizeFailures(allResults, FidelityCheck.CompileBackStatus.ContextFail);
            var recompileBuckets = FidelityCheck.SummarizeFailures(allResults, FidelityCheck.CompileBackStatus.RecompileFail);
            reports.Add(new FidelityCapReport(cap, metrics, contextBuckets, recompileBuckets));
        }

        if (reports.Count == 0)
            reports.Add(new FidelityCapReport(0, new FidelitySensorMetrics(0, 0, 0, 0, 0, 0), ImmutableDictionary<string, FidelityCheck.FailureBucketSummary>.Empty, ImmutableDictionary<string, FidelityCheck.FailureBucketSummary>.Empty));
        return reports.ToImmutable();
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

    internal static ImmutableArray<string> Compare(
        CorpusSensorSnapshot baseline,
        CorpusSensorSnapshot current,
        ImmutableArray<FidelityCapReport> fidelityReports,
        bool gateAggregateRates = true)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        var tolerance = baseline.Tolerances ?? CorpusSensorTolerances.Default;
        var baselinePinned = PinnedCounts(baseline);
        var currentPinned = PinnedCounts(current);
        var matchedFidelityReport = fidelityReports.FirstOrDefault(report => report.Cap == baseline.FidelityCompileCap)
            ?? fidelityReports.FirstOrDefault(report => report.Cap == current.FidelityCompileCap)
            ?? fidelityReports.LastOrDefault();
        var currentFidelityMetrics = matchedFidelityReport?.Metrics ?? current.Metrics.Fidelity;
        var currentFidelityCap = matchedFidelityReport?.Cap ?? current.FidelityCompileCap;

        if (current.ValidityCompileCap < baseline.ValidityCompileCap)
            failures.Add($"validity cap lower than baseline (baseline {baseline.ValidityCompileCap}, current {current.ValidityCompileCap})");
        if (currentFidelityCap < baseline.FidelityCompileCap)
            failures.Add($"fidelity cap lower than baseline (baseline {baseline.FidelityCompileCap}, current {currentFidelityCap})");
        if (current.MethodCap != baseline.MethodCap)
            failures.Add($"method cap differs from baseline (baseline {CapText(baseline.MethodCap)}, current {CapText(current.MethodCap)})");
        if (current.Metrics.SemanticCheckedMethods < baseline.Metrics.SemanticCheckedMethods)
            failures.Add($"semantic checked methods lower than baseline (baseline {baseline.Metrics.SemanticCheckedMethods}, current {current.Metrics.SemanticCheckedMethods})");
        if (currentFidelityMetrics.CheckedMethods < baseline.Metrics.Fidelity.CheckedMethods)
            failures.Add($"fidelity checked methods lower than baseline (baseline {baseline.Metrics.Fidelity.CheckedMethods}, current {currentFidelityMetrics.CheckedMethods})");

        if (gateAggregateRates || baselinePinned is null || currentPinned is null)
        {
            AddRateRegression(failures, "fully-raised rate", baseline.Metrics.FullyRaisedBasisPoints, current.Metrics.FullyRaisedBasisPoints, tolerance.FullyRaisedDropBasisPoints, lowerIsRegression: true);
            AddRateRegression(failures, "conditional-branch residual rate", baseline.Metrics.ConditionalBranchBasisPoints, current.Metrics.ConditionalBranchBasisPoints, tolerance.ConditionalBranchIncreaseBasisPoints, lowerIsRegression: false);
            AddRateRegression(failures, "forward-merge stop rate", baseline.Metrics.ForwardMergeBasisPoints, current.Metrics.ForwardMergeBasisPoints, tolerance.ForwardMergeIncreaseBasisPoints, lowerIsRegression: false);
        }
        else
        {
            AddRateRegression(failures, "fully-raised rate (pinned)", baselinePinned.FullyRaisedBasisPoints, currentPinned.FullyRaisedBasisPoints, tolerance.FullyRaisedDropBasisPoints, lowerIsRegression: true);
            AddRateRegression(failures, "conditional-branch residual rate (pinned)", baselinePinned.ConditionalBranchBasisPoints, currentPinned.ConditionalBranchBasisPoints, tolerance.ConditionalBranchIncreaseBasisPoints, lowerIsRegression: false);
            // Forward-merge stops are structuring-container counts, not per-method snapshot
            // rows, so the PR quick gate leaves aggregate movement advisory unless the
            // caller explicitly opts into aggregate rate gating (for risky decompiler work).
        }

        // Count-based regressions (Full malformed, semantic defects, fidelity buckets)
        // have a zero tolerance, which is incompatible with a corpus that mixes the
        // pinned NuGet assemblies with the repo's own assemblies: the latter grow every
        // PR, and newly-added repo code that the decompiler malforms drives phantom count
        // regressions on changes that touched nothing. Gate Full-malformed and
        // semantic-defect counts on the *pinned* subset — a fixed method set whose counts
        // move only when decompiler behavior does — computed from the per-method snapshots
        // both snapshots carry. Pass bugs (crashes) stay on the full aggregate: a crash
        // anywhere is worth blocking. Fall back to the aggregate when a snapshot lacks
        // per-method detail.
        if (baselinePinned is { } basePinned && currentPinned is { } curPinned)
        {
            AddCountRegression(failures, "Full malformed methods (pinned)", basePinned.FullMalformed, curPinned.FullMalformed, tolerance.FullMalformedIncrease);
            AddCountRegression(failures, "semantic defect methods (pinned)", basePinned.SemanticDefect, curPinned.SemanticDefect, tolerance.SemanticDefectIncrease);

            // Fidelity is sampled far more thinly than validity, and that small sample
            // currently lands almost entirely on repo assemblies, so the pinned subset
            // often has zero fidelity-checked methods. Gate fidelity counts on the pinned
            // subset only when it actually holds a fidelity sample and the caps match
            // (per-method FidelityCheck is recorded at the primary cap); otherwise the
            // aggregate fidelity count is drift-contaminated, so leave it ungated and rely
            // on changed-method fidelity (the authoritative fidelity proof) instead of
            // re-introducing a repo-growth false positive.
            if (basePinned.FidelityChecked > 0 && curPinned.FidelityChecked > 0
                && current.FidelityCompileCap == baseline.FidelityCompileCap)
            {
                AddCountRegression(failures, "fidelity opcode diffs (pinned)", basePinned.OpcodeDiff, curPinned.OpcodeDiff, tolerance.FidelityOpcodeDiffIncrease);
                AddCountRegression(failures, "fidelity recompile failures (pinned)", basePinned.RecompileFail, curPinned.RecompileFail, tolerance.FidelityRecompileFailIncrease);
                AddCountRegression(failures, "fidelity context failures (pinned)", basePinned.ContextFail, curPinned.ContextFail, tolerance.FidelityContextFailIncrease);
            }
        }
        else
        {
            AddCountRegression(failures, "Full malformed methods", baseline.Metrics.FullMalformedMethods, current.Metrics.FullMalformedMethods, tolerance.FullMalformedIncrease);
            AddCountRegression(failures, "semantic defect methods", baseline.Metrics.SemanticDefectMethods, current.Metrics.SemanticDefectMethods, tolerance.SemanticDefectIncrease);
            if (baseline.Metrics.Fidelity.CheckedMethods > 0)
            {
                AddCountRegression(failures, "fidelity opcode diffs", baseline.Metrics.Fidelity.OpcodeDiffMethods, currentFidelityMetrics.OpcodeDiffMethods, tolerance.FidelityOpcodeDiffIncrease);
                AddCountRegression(failures, "fidelity recompile failures", baseline.Metrics.Fidelity.RecompileFailMethods, currentFidelityMetrics.RecompileFailMethods, tolerance.FidelityRecompileFailIncrease);
                AddCountRegression(failures, "fidelity context failures", baseline.Metrics.Fidelity.ContextFailMethods, currentFidelityMetrics.ContextFailMethods, tolerance.FidelityContextFailIncrease);
            }
        }

        AddCountRegression(failures, "pass bugs", baseline.Metrics.PassBugs, current.Metrics.PassBugs, tolerance.PassBugIncrease);

        return failures.ToImmutable();
    }

    /// <summary>
    /// Count-based correctness metrics restricted to the pinned NuGet assemblies — the
    /// deterministic, fixed-version slice of the corpus whose method set never changes,
    /// so a count delta there reflects a real decompiler behavior change rather than
    /// repo-assembly growth. Computed from the per-method snapshots (consistent with the
    /// aggregates, which equal pinned + repo). <see cref="PinnedCountMetrics.FidelityChecked"/>
    /// lets the caller skip fidelity gating when the pinned subset holds no fidelity
    /// sample. Returns null when per-method detail is absent, so the caller falls back to
    /// the aggregate counts.
    /// </summary>
    static PinnedCountMetrics? PinnedCounts(CorpusSensorSnapshot snapshot)
    {
        if (snapshot.Methods is not { Count: > 0 } methods)
            return null;

        int total = 0, fullyRaised = 0, conditional = 0;
        int malformed = 0, semantic = 0, opcode = 0, recompile = 0, context = 0, fidelityChecked = 0;
        foreach (var method in methods)
        {
            if (!IsPinnedAssembly(method.AssemblyPath))
                continue;
            total++;
            if (method.FullyRaised)
                fullyRaised++;
            if (method.Residual == ConditionalBranchBucket)
                conditional++;
            if (method.Validity.StartsWith("full-malformed:", StringComparison.Ordinal))
                malformed++;
            else if (method.Validity.StartsWith("semantic-defect:", StringComparison.Ordinal))
                semantic++;
            if (method.FidelityCheck != "not-sampled")
                fidelityChecked++;
            switch (method.FidelityCheck)
            {
                case "OpcodeDiff": opcode++; break;
                case "RecompileFail": recompile++; break;
                case "ContextFail": context++; break;
            }
        }
        if (total == 0)
            return null;

        return new PinnedCountMetrics(
            total,
            fullyRaised,
            RateBasisPoints(fullyRaised, total),
            conditional,
            RateBasisPoints(conditional, total),
            malformed,
            semantic,
            opcode,
            recompile,
            context,
            fidelityChecked);
    }

    static bool IsPinnedAssembly(string assemblyPath)
        => assemblyPath.StartsWith("nuget:", StringComparison.Ordinal);

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

    static void AddRateRegression(ImmutableArray<string>.Builder failures, string name, int baseline, int current, int tolerance, bool lowerIsRegression)
    {
        int change = lowerIsRegression ? baseline - current : current - baseline;
        if (change > tolerance)
        {
            string direction = lowerIsRegression ? "dropped" : "increased";
            failures.Add($"{name} {direction} {FormatPercentagePoints(change)} (baseline {FormatBps(baseline)}, current {FormatBps(current)}, tolerance {FormatPercentagePoints(tolerance)})");
        }
    }

    static void PrintSummary(CorpusSensorSnapshot snapshot, ImmutableArray<FidelityCapReport> fidelityReports)
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
        else if (fidelityReports.Length == 1)
        {
            var report = fidelityReports[0];
            Console.WriteLine($"Fidelity (cap {report.Cap}): {report.Metrics.ExactMethods} exact, {report.Metrics.OpcodeDiffMethods} opcode diffs, {report.Metrics.RecompileFailMethods} recompile failures, {report.Metrics.ContextFailMethods} context failures over {report.Metrics.CheckedMethods} checked");
            PrintFailureBuckets(report.ContextFailureBuckets, report.RecompileFailureBuckets, "  ");
        }
        else
        {
            Console.WriteLine("Fidelity coverage by cap:");
            foreach (var report in fidelityReports)
            {
                Console.WriteLine($"  cap {report.Cap}: {report.Metrics.ExactMethods} exact, {report.Metrics.OpcodeDiffMethods} opcode diffs, {report.Metrics.RecompileFailMethods} recompile failures, {report.Metrics.ContextFailMethods} context failures over {report.Metrics.CheckedMethods} checked");
                PrintFailureBuckets(report.ContextFailureBuckets, report.RecompileFailureBuckets, "    ");
            }
        }
    }

    static void PrintFailureBuckets(
        IReadOnlyDictionary<string, FidelityCheck.FailureBucketSummary> contextBuckets,
        IReadOnlyDictionary<string, FidelityCheck.FailureBucketSummary> recompileBuckets,
        string indent)
    {
        if (contextBuckets.Count > 0)
        {
            Console.WriteLine($"{indent}context-failure buckets:");
            foreach (var bucket in contextBuckets.OrderByDescending(pair => pair.Value.Count))
                Console.WriteLine($"{indent}  {bucket.Key}: {bucket.Value.Count} (e.g. {string.Join(", ", bucket.Value.Examples)})");
        }

        if (recompileBuckets.Count > 0)
        {
            Console.WriteLine($"{indent}recompile-failure buckets:");
            foreach (var bucket in recompileBuckets.OrderByDescending(pair => pair.Value.Count))
                Console.WriteLine($"{indent}  {bucket.Key}: {bucket.Value.Count} (e.g. {string.Join(", ", bucket.Value.Examples)})");
        }
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
        PrintBaselineStaleness(baseline, current);
        Console.WriteLine();
        Console.WriteLine("| Metric (desired direction) | Baseline | PR | Count delta |");
        Console.WriteLine("| --- | ---: | ---: | ---: |");
        PrintMetric(
            "Fully raised (+)",
            CountPercent(baseline.Metrics.FullyRaisedMethods, baseline.Metrics.FullyRaisedBasisPoints),
            CountPercent(current.Metrics.FullyRaisedMethods, current.Metrics.FullyRaisedBasisPoints),
            Delta(current.Metrics.FullyRaisedMethods - baseline.Metrics.FullyRaisedMethods));
        PrintMetric(
            "Conditional-branch residual (-)",
            CountPercent(baseline.Metrics.ConditionalBranchMethods, baseline.Metrics.ConditionalBranchBasisPoints),
            CountPercent(current.Metrics.ConditionalBranchMethods, current.Metrics.ConditionalBranchBasisPoints),
            Delta(current.Metrics.ConditionalBranchMethods - baseline.Metrics.ConditionalBranchMethods));
        PrintMetric(
            "Forward-merge stops (-)",
            CountPercent(baseline.Metrics.ForwardMergeStoppedContainers, baseline.Metrics.ForwardMergeBasisPoints),
            CountPercent(current.Metrics.ForwardMergeStoppedContainers, current.Metrics.ForwardMergeBasisPoints),
            Delta(current.Metrics.ForwardMergeStoppedContainers - baseline.Metrics.ForwardMergeStoppedContainers));
        if (current.ValidityCompileCap > 0)
        {
            PrintMetric(
                "Full malformed (-)",
                Number(baseline.Metrics.FullMalformedMethods),
                Number(current.Metrics.FullMalformedMethods),
                Delta(current.Metrics.FullMalformedMethods - baseline.Metrics.FullMalformedMethods));
            PrintMetric(
                "Semantic defects (-)",
                FractionWithCoverage(baseline.Metrics.SemanticDefectMethods, baseline.Metrics.SemanticCheckedMethods, baseline.Metrics.TotalMethods),
                FractionWithCoverage(current.Metrics.SemanticDefectMethods, current.Metrics.SemanticCheckedMethods, current.Metrics.TotalMethods),
                Delta(current.Metrics.SemanticDefectMethods - baseline.Metrics.SemanticDefectMethods));
        }
        if (current.FidelityCompileCap > 0)
        {
            PrintMetric(
                "Fidelity diffs (-)",
                FidelityWithCoverage(baseline.Metrics.Fidelity, baseline.Metrics.TotalMethods),
                FidelityWithCoverage(current.Metrics.Fidelity, current.Metrics.TotalMethods),
                Delta(current.Metrics.Fidelity.OpcodeDiffMethods - baseline.Metrics.Fidelity.OpcodeDiffMethods));
        }
        PrintMetric(
            "Pass bugs (-)",
            Number(baseline.Metrics.PassBugs),
            Number(current.Metrics.PassBugs),
            Delta(current.Metrics.PassBugs - baseline.Metrics.PassBugs));
        PrintPinnedGate(baseline, current);
        if (!risky)
            PrintAdvisoryRateMovements(baseline, current);
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
            if (IsBaselineStale(baseline, current))
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Caveat: the corpus drifted from the baseline (see baseline staleness above). "
                    + "The aggregate rows above mix the PR with that drift, but rate/count "
                    + "regressions are gated on the pinned-NuGet subset where available (a fixed method set), so any "
                    + "`(pinned)` regression listed here is a real decompiler delta, not drift.");
            }
        }
    }

    /// <summary>
    /// Shows the pinned-NuGet-subset metrics that drive the PR quick verdict, so
    /// reviewers can see the stable gate alongside the drifting aggregate rows.
    /// Silent when per-method detail is unavailable (the verdict then falls back
    /// to aggregate counts/rates).
    /// </summary>
    static void PrintPinnedGate(CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
    {
        if (PinnedCounts(baseline) is not { } basePinned || PinnedCounts(current) is not { } curPinned)
            return;
        var fidelity = basePinned.FidelityChecked > 0 && curPinned.FidelityChecked > 0
            && current.FidelityCompileCap == baseline.FidelityCompileCap
            ? $"opcode diffs {Number(basePinned.OpcodeDiff)} -> {Number(curPinned.OpcodeDiff)} "
                + $"({Delta(curPinned.OpcodeDiff - basePinned.OpcodeDiff)})"
            : "fidelity ungated (no pinned fidelity sample; rely on changed-method fidelity)";
        var fullMalformed = current.ValidityCompileCap > 0
            ? $"Full malformed {Number(basePinned.FullMalformed)} -> {Number(curPinned.FullMalformed)} "
                + $"({Delta(curPinned.FullMalformed - basePinned.FullMalformed)}); "
            : "";
        Console.WriteLine();
        Console.WriteLine(
            "Pinned-subset gate (PR quick rate/count regressions evaluated here): "
            + $"Fully raised {FormatBps(basePinned.FullyRaisedBasisPoints)} -> {FormatBps(curPinned.FullyRaisedBasisPoints)} "
            + $"({DeltaPercentagePoints(curPinned.FullyRaisedBasisPoints - basePinned.FullyRaisedBasisPoints)}); "
            + $"conditional residual {FormatBps(basePinned.ConditionalBranchBasisPoints)} -> {FormatBps(curPinned.ConditionalBranchBasisPoints)} "
            + $"({DeltaPercentagePoints(curPinned.ConditionalBranchBasisPoints - basePinned.ConditionalBranchBasisPoints)}); "
            + fullMalformed
            + fidelity + ".");
    }

    static void PrintAdvisoryRateMovements(CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
    {
        var tolerance = baseline.Tolerances ?? CorpusSensorTolerances.Default;
        var advisories = new List<string>();
        AddAdvisory("fully-raised", baseline.Metrics.FullyRaisedBasisPoints, current.Metrics.FullyRaisedBasisPoints, tolerance.FullyRaisedDropBasisPoints, lowerIsRegression: true);
        AddAdvisory("conditional-branch residual", baseline.Metrics.ConditionalBranchBasisPoints, current.Metrics.ConditionalBranchBasisPoints, tolerance.ConditionalBranchIncreaseBasisPoints, lowerIsRegression: false);
        AddAdvisory("forward-merge stop", baseline.Metrics.ForwardMergeBasisPoints, current.Metrics.ForwardMergeBasisPoints, tolerance.ForwardMergeIncreaseBasisPoints, lowerIsRegression: false);
        if (advisories.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("Advisory aggregate rate movement (not a PR quick hard gate; review for decompiler-risky changes):");
        foreach (var advisory in advisories)
            Console.WriteLine($"- {advisory}");

        void AddAdvisory(string name, int baselineRate, int currentRate, int toleranceBps, bool lowerIsRegression)
        {
            int change = lowerIsRegression ? baselineRate - currentRate : currentRate - baselineRate;
            if (change > toleranceBps)
            {
                string direction = lowerIsRegression ? "dropped" : "increased";
                advisories.Add($"{name} {direction} {FormatPercentagePoints(change)} (baseline {FormatBps(baselineRate)}, PR {FormatBps(currentRate)}, tolerance {FormatPercentagePoints(toleranceBps)})");
            }
        }
    }

    /// <summary>
    /// The real-world corpus includes the repo's own assemblies, which grow as
    /// unrelated code lands, so a baseline captured earlier can disagree with the
    /// current run on method population even when the PR changed nothing. When that
    /// happens the aggregate count deltas (fully raised, Full malformed, fidelity
    /// diffs) mix the PR's effect with corpus drift and stop being a clean signal.
    /// Surface the drift explicitly so reviewers rebaseline instead of chasing a
    /// phantom regression.
    /// </summary>
    static void PrintBaselineStaleness(CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
    {
        var drift = DescribeBaselineDrift(baseline, current);
        if (drift.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine(
            $"Baseline staleness: corpus drifted from the pinned baseline "
            + $"(generated {baseline.GeneratedUtc:yyyy-MM-dd}). Aggregate count deltas below include "
            + "this corpus change and are not a clean PR signal; rebaseline against current main "
            + "(--emit-corpus-baseline) before trusting count-based regressions.");
        foreach (var line in drift)
            Console.WriteLine($"- {line}");
    }

    static bool IsBaselineStale(CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
        => DescribeBaselineDrift(baseline, current).Count > 0;

    static List<string> DescribeBaselineDrift(CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
    {
        var lines = new List<string>();
        if (baseline.Metrics.TotalMethods != current.Metrics.TotalMethods)
        {
            lines.Add(
                $"total methods {Number(baseline.Metrics.TotalMethods)} -> {Number(current.Metrics.TotalMethods)} "
                + $"({Delta(current.Metrics.TotalMethods - baseline.Metrics.TotalMethods)})");
        }

        var baselineByName = baseline.Assemblies
            .GroupBy(assembly => assembly.Assembly)
            .ToDictionary(group => group.Key, group => group.First().TotalMethods);
        var currentByName = current.Assemblies
            .GroupBy(assembly => assembly.Assembly)
            .ToDictionary(group => group.Key, group => group.First().TotalMethods);

        foreach (var name in baselineByName.Keys.Concat(currentByName.Keys).Distinct().OrderBy(name => name))
        {
            bool inBaseline = baselineByName.TryGetValue(name, out int baselineMethods);
            bool inCurrent = currentByName.TryGetValue(name, out int currentMethods);
            if (!inCurrent)
                lines.Add($"{name}: removed from corpus (was {Number(baselineMethods)} methods)");
            else if (!inBaseline)
                lines.Add($"{name}: added to corpus ({Number(currentMethods)} methods)");
            else if (baselineMethods != currentMethods)
                lines.Add(
                    $"{name}: {Number(baselineMethods)} -> {Number(currentMethods)} methods "
                    + $"({Delta(currentMethods - baselineMethods)})");
        }
        return lines;
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

    static string DeltaPercentagePoints(int basisPoints)
        => basisPoints == 0 ? "0 pp" : $"{(basisPoints > 0 ? "+" : "")}{FormatPercentagePoints(basisPoints)}";

    static string FormatPercentagePoints(int basisPoints)
        => $"{basisPoints / 100.0:F2} pp";

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

internal sealed record PinnedCountMetrics(
    int TotalMethods,
    int FullyRaisedMethods,
    int FullyRaisedBasisPoints,
    int ConditionalBranchMethods,
    int ConditionalBranchBasisPoints,
    int FullMalformed,
    int SemanticDefect,
    int OpcodeDiff,
    int RecompileFail,
    int ContextFail,
    int FidelityChecked);

internal sealed record CompletenessSensorMetrics(
    IReadOnlyList<CorpusAssemblySnapshot> Assemblies,
    int FullyRaisedMethods,
    int PassBugs,
    IReadOnlyDictionary<string, int> ResidualBuckets,
    IReadOnlyList<CorpusMethodSnapshot> Methods);

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

internal sealed record FidelityCapReport(
    int Cap,
    FidelitySensorMetrics Metrics,
    IReadOnlyDictionary<string, FidelityCheck.FailureBucketSummary> ContextFailureBuckets,
    IReadOnlyDictionary<string, FidelityCheck.FailureBucketSummary> RecompileFailureBuckets);

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
