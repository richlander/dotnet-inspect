using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;
using Markout;
using Markout.Formatting;

namespace ILInspector.DecompilerHarness;

internal enum CorpusFidelityOracle
{
    [JsonStringEnumMemberName("compile-back")]
    CompileBack,

    [JsonStringEnumMemberName("rts-parity")]
    ReturnToSender,
}

internal enum CorpusProfile
{
    [JsonStringEnumMemberName("real-world")]
    RealWorld,

    [JsonStringEnumMemberName("opt-in-net11")]
    OptInNet11,

    [JsonStringEnumMemberName("classic-state-machines")]
    ClassicStateMachines,
}

internal static class CorpusSensor
{
    internal const int CurrentSchemaVersion = 5;
    internal const int CurrentFidelityContractVersion = FidelityCheck.CurrentContractVersion;
    const string ConditionalBranchBucket = "structuring: conditional-branch";
    const int RiskyValidityCoverageFloorBasisPoints = 100; // 1.00%
    const int RiskyFidelityCoverageFloorBasisPoints = 10;  // 0.10%
    static readonly string[] ForwardMergeStopReasons =
    [
        "cond-target-past-region",
        "forward-branch-not-region-exit",
    ];
    static readonly ImmutableSortedDictionary<string, int> RequiredOptInNet11Features =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["await-recovery-methods"] = 1,
            ["cross-assembly-requires-unsafe-methods"] = 1,
            ["legacy-memory-safety-control-methods"] = 1,
            ["runtime-async-awaiter-methods"] = 1,
            ["runtime-async-await-using-methods"] = 1,
            ["runtime-async-exception-methods"] = 1,
            ["runtime-async-loop-methods"] = 1,
            ["runtime-async-methods"] = 1,
            ["union-declarations"] = 1,
            ["union-switch-methods"] = 1,
            ["union-types"] = 1,
            ["updated-memory-safety-methods"] = 1,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    static readonly ImmutableSortedDictionary<string, int> RequiredClassicStateMachinesFeatures =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["classic-async-methods"] = 1,
            ["classic-iterator-methods"] = 1,
            ["classic-async-iterator-methods"] = 1,
            ["switch-methods"] = 1,
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);

    public static int Run(
        IReadOnlyList<string> assemblies,
        int validityCompileCap,
        IReadOnlyList<int> fidelityCompileCaps,
        int maxExamples,
        string? emitBaseline,
        string? diffBaseline,
        string? diffBaselineRef,
        string? emitDelta,
        bool qualityDiffCard = false,
        bool qualityCardRisky = false,
        int methodCap = int.MaxValue,
        int? workers = null,
        bool sequential = false,
        CorpusFidelityOracle fidelityOracle = CorpusFidelityOracle.CompileBack,
        CorpusProfile profile = CorpusProfile.RealWorld,
        string? rtsParityBurndown = null,
        string? emitRtsParityBurndown = null)
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
        if (diffBaselineRef is not null && diffBaseline is null)
        {
            Console.Error.WriteLine("--diff-corpus-baseline-ref requires --diff-corpus-baseline <file>.");
            return 1;
        }
        if (emitDelta is not null && diffBaseline is null)
        {
            Console.Error.WriteLine("--emit-corpus-delta requires --diff-corpus-baseline <file>.");
            return 1;
        }

        if (rtsParityBurndown is not null || emitRtsParityBurndown is not null)
        {
            var burndownError = ValidateRtsParityBurndownFlags(
                fidelityOracle, fidelityCompileCaps, rtsParityBurndown, emitRtsParityBurndown);
            if (burndownError is not null)
            {
                Console.Error.WriteLine(burndownError);
                return 1;
            }
        }

        var (current, fidelityReports) = Capture(
            assemblies,
            validityCompileCap,
            fidelityCompileCaps,
            maxExamples,
            methodCap,
            workers,
            sequential,
            fidelityOracle,
            profile);
        if (!qualityDiffCard)
            PrintSummary(current, fidelityReports);

        bool rtsParityRegressed = false;
        if (fidelityOracle == CorpusFidelityOracle.ReturnToSender)
            rtsParityRegressed = EnforceRtsParityBurndown(current, rtsParityBurndown, emitRtsParityBurndown, qualityDiffCard);

        if (emitBaseline is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(emitBaseline)) ?? ".");
            File.WriteAllText(emitBaseline, JsonSerializer.Serialize(current, JsonOptions()));
            if (!qualityDiffCard)
            {
                Console.WriteLine();
                HarnessLog.Status($"Wrote corpus baseline: {emitBaseline}");
            }
        }

        if (diffBaseline is null)
            return current.Metrics.PassBugs > 0
                || FeatureCoverageFailures(current).Length > 0
                || ClassicStateMachineCoverageFailures(current, current).Length > 0
                || rtsParityRegressed
                ? 1
                : 0;

        var baseline = JsonSerializer.Deserialize<CorpusSensorSnapshot>(
            ReadBaselineText(diffBaseline, diffBaselineRef),
            JsonOptions())
            ?? throw new InvalidOperationException($"Could not read corpus baseline '{diffBaseline}'.");
        var regressions = Compare(
            baseline,
            current,
            fidelityReports,
            gateAggregateRates: ShouldGateAggregateRates(profile, qualityDiffCard, qualityCardRisky));
        if (emitDelta is not null)
        {
            EmitMethodDelta(emitDelta, baseline, current);
            if (!qualityDiffCard)
            {
                Console.WriteLine();
                HarnessLog.Status($"Wrote per-method corpus delta: {emitDelta}");
            }
        }
        if (qualityDiffCard)
        {
            PrintQualityDiffCard(
                baseline,
                current,
                regressions,
                qualityCardRisky,
                diffBaselineRef);
            if (emitDelta is not null)
            {
                Console.WriteLine();
                Console.WriteLine($"Per-method delta artifact: `{emitDelta}`");
            }
            return regressions.Length == 0 && !rtsParityRegressed ? 0 : 1;
        }

        if (regressions.Length == 0 && !rtsParityRegressed)
        {
            Console.WriteLine();
            Console.WriteLine(
                diffBaselineRef is null
                    ? $"Corpus sensor matched baseline: {diffBaseline}"
                    : $"Corpus sensor matched baseline: {diffBaseline} at {diffBaselineRef}");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Corpus sensor regressions:");
        foreach (var regression in regressions)
            Console.WriteLine($"- {regression}");
        return 1;
    }

    static string ReadBaselineText(string path, string? gitRef)
    {
        if (gitRef is null)
            return File.ReadAllText(path);

        string fullPath = Path.GetFullPath(path);
        string startDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;
        string repositoryRoot = RunGit(startDirectory, "rev-parse", "--show-toplevel").Trim();
        string relativePath = Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');
        if (relativePath == ".."
            || relativePath.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidOperationException(
                $"Baseline '{path}' is outside git repository '{repositoryRoot}'.");
        }

        string commit = RunGit(
            repositoryRoot,
            "rev-parse",
            "--verify",
            "--end-of-options",
            $"{gitRef}^{{commit}}").Trim();
        return RunGit(repositoryRoot, "show", $"{commit}:{relativePath}");
    }

    static string RunGit(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start git.");
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {error.Trim()}");
        }

        return output;
    }

    internal static string ReadBaselineTextForTesting(string path, string? gitRef)
        => ReadBaselineText(path, gitRef);

    internal static CorpusSensorSnapshot ReadBaselineForTesting(string path)
        => JsonSerializer.Deserialize<CorpusSensorSnapshot>(File.ReadAllText(path), JsonOptions())
            ?? throw new InvalidOperationException($"Could not read corpus baseline '{path}'.");

    static (CorpusSensorSnapshot Snapshot, ImmutableArray<FidelityCapReport> Reports) Capture(
        IReadOnlyList<string> assemblies,
        int validityCompileCap,
        IReadOnlyList<int> fidelityCompileCaps,
        int maxExamples,
        int methodCap,
        int? workers,
        bool sequential,
        CorpusFidelityOracle fidelityOracle,
        CorpusProfile profile)
    {
        var completeness = AnalyzeCompleteness(assemblies, maxExamples, methodCap, workers, sequential, profile);
        var methods = completeness.Methods.ToDictionary(MethodKey, StringComparer.Ordinal);
        var validity = AnalyzeValidity(assemblies, validityCompileCap, methods, workers, sequential);
        var structuring = AnalyzeStructuring(assemblies, methodCap, workers, sequential);
        var totalMethods = completeness.Assemblies.Sum(assembly => assembly.TotalMethods);
        var fullyRaisedMethods = completeness.FullyRaisedMethods;
        var conditionalBranchMethods = completeness.ResidualBuckets.GetValueOrDefault(ConditionalBranchBucket);
        var forwardMergeContainers = ForwardMergeStopReasons.Sum(reason => structuring.StopReasons.GetValueOrDefault(reason));
        var requestedCaps = fidelityCompileCaps.Where(cap => cap > 0).Distinct().ToArray();
        var primaryFidelityCap = requestedCaps.FirstOrDefault();
        var fidelityReports = AnalyzeFidelity(
            assemblies,
            fidelityCompileCaps,
            methods,
            primaryFidelityCap,
            workers,
            sequential,
            fidelityOracle);
        var selectedFidelity = fidelityReports.FirstOrDefault(report => report.Cap == primaryFidelityCap)?.Metrics
            ?? fidelityReports.LastOrDefault()?.Metrics
            ?? FidelitySensorMetrics.Empty;

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
            SchemaVersion: CurrentSchemaVersion,
            Description: DescriptionForProfile(profile),
            GeneratedUtc: DateTimeOffset.UtcNow,
            ValidityCompileCap: validityCompileCap,
            FidelityCompileCap: primaryFidelityCap,
            MethodCap: methodCap == int.MaxValue ? null : methodCap,
            Tolerances: CorpusSensorTolerances.Default,
            Assemblies: completeness.Assemblies,
            Methods: methods.Values.OrderBy(MethodKey, StringComparer.Ordinal).ToImmutableArray(),
            Metrics: metrics,
            FidelityOracle: fidelityOracle,
            Profile: profile,
            FeatureCoverage: completeness.FeatureCoverage,
            ClassicStateMachineCoverage: completeness.ClassicStateMachineCoverage);

        return (snapshot, fidelityReports);
    }

    internal static string DescriptionForProfile(CorpusProfile profile)
        => profile switch
        {
            CorpusProfile.RealWorld
                => "#1166 real-world decompiler corpus sensor: #1150 pinned NuGet assemblies plus dotnet-inspect managed assemblies.",
            CorpusProfile.OptInNet11
                => "#2766 net11 opt-in compiler-feature corpus: pinned runtime-async, union, and memory-safety fixtures.",
            CorpusProfile.ClassicStateMachines
                => "#2818 classic async/iterator state-machine corpus: pinned classic async, iterator, async-iterator, and switch fixtures, raised with the cross-method import seam wired.",
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };

    internal static bool ShouldGateAggregateRates(
        CorpusProfile profile,
        bool qualityDiffCard,
        bool qualityCardRisky)
        => profile != CorpusProfile.RealWorld || !qualityDiffCard || qualityCardRisky;

    static CompletenessSensorMetrics AnalyzeCompleteness(
        IReadOnlyList<string> assemblies,
        int maxExamples,
        int methodCap,
        int? workers,
        bool sequential,
        CorpusProfile profile)
    {
        var residualBuckets = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var featureCoverage = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var assemblyReports = new ConcurrentBag<CorpusAssemblySnapshot>();
        var methodReports = new ConcurrentBag<CorpusMethodSnapshot>();
        int fullyRaised = 0, passBugs = 0;

        using var metadata = CorpusMetadata.Create(assemblies);
        var options = new ParallelOptions { MaxDegreeOfParallelism = sequential ? 1 : (workers ?? Math.Max(1, Environment.ProcessorCount - 2)) };

        foreach (var assemblyPath in assemblies)
        {
            if (profile == CorpusProfile.OptInNet11)
                RecordUnionCoverage(assemblyPath, featureCoverage);

            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            // Pre-warm the type maps before fan-out to avoid lock contention
            _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));

            int methods = 0;
            string portablePath = PortablePath(assemblyPath);
            var stableSample = IrImporter.GetStableSampleCandidates(source, methodCap).ToList();

            Parallel.ForEach(stableSample, options, item =>
            {
                var typeName = item.TypeName;
                var methodName = item.MethodName;
                var overload = item.Overload;
                var function = item.Build(source);
                
                Interlocked.Increment(ref methods);
                string? residual = null;
                string? passBug = null;
                FidelityCauseBuckets.Census? fidelityCensus = null;
                try
                {
                    if (profile == CorpusProfile.OptInNet11)
                    {
                        var stages = IrPasses.RunWithStages(function);
                        RecordMethodFeatureCoverage(
                            function,
                            source.AssemblyName,
                            StageDump.PassesThatChanged(stages),
                            featureCoverage);
                    }
                    else if (profile == CorpusProfile.ClassicStateMachines)
                    {
                        // Wires the cross-method import seam (same helper the
                        // product and --dump/--library-report paths use) so
                        // cross-method passes like ClassicAsyncReconstructionPass
                        // can pull in the sibling MoveNext body, exactly as they
                        // do outside the corpus sensor (#2818).
                        IrPasses.Run(function, IrPasses.Default, PassContext.ForImport(method => IrImporter.Import(source, method)));
                        RecordClassicStateMachineFeatureCoverage(typeName, methodName, featureCoverage);
                    }
                    else
                    {
                        IrPasses.Run(function);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref passBugs);
                    passBug = ex.GetType().Name;
                }

                if (passBug is null)
                {
                    string id = $"{typeName}::{methodName}{overload}";
                    fidelityCensus = FidelityCauseBuckets.Inspect(function, id);
                    if (!fidelityCensus.Value.Succeeded)
                    {
                        Interlocked.Increment(ref passBugs);
                        passBug = fidelityCensus.Value.ErrorCode;
                    }
                    else
                    {
                        residual = Completeness.Residual(function)
                            ?? (!fidelityCensus.Value.Causes.IsEmpty
                            ? $"fidelity: {FidelityCauseBuckets.PrimaryBucket(fidelityCensus.Value)}"
                            : null);
                    }
                }
                if (passBug is null && residual is null)
                {
                    Interlocked.Increment(ref fullyRaised);
                }
                else if (residual is not null)
                {
                    residualBuckets.AddOrUpdate(residual, 1, (_, v) => v + 1);
                }
                methodReports.Add(new CorpusMethodSnapshot(
                    source.AssemblyName,
                    portablePath,
                    typeName,
                    methodName,
                    overload,
                    CorpusMethodIdentity.SignatureText(function.Signature),
                    fidelityCensus.HasValue && fidelityCensus.Value.Succeeded
                        ? (fidelityCensus.Value.Causes.IsEmpty ? "Full" : "Partial")
                        : "Unavailable",
                    residual is null && passBug is null,
                    residual,
                    passBug,
                    Validity: "not-sampled",
                    FidelityCheck: "not-sampled",
                    FidelityCauses: fidelityCensus is { Succeeded: true, Causes.IsEmpty: false }
                        ? fidelityCensus.Value.Causes
                            .GroupBy(static cause => (cause.Code, cause.Discriminator))
                            .Select(static group =>
                            {
                                int sites = group.Count();
                                return new CorpusFidelityCauseSnapshot(
                                    group.Key.Code,
                                    group.Key.Discriminator,
                                    sites > 1 ? sites : null);
                            })
                            .OrderBy(static cause => cause.Code, StringComparer.Ordinal)
                            .ThenBy(static cause => cause.Discriminator, StringComparer.Ordinal)
                            .ToImmutableArray()
                        : null));
            });
            assemblyReports.Add(new CorpusAssemblySnapshot(source.AssemblyName, PortablePath(assemblyPath), methods));
        }

        return new CompletenessSensorMetrics(
            assemblyReports.OrderBy(r => r.Path, StringComparer.Ordinal).ToImmutableArray(),
            fullyRaised,
            passBugs,
            residualBuckets.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal),
            methodReports.OrderBy(m => MethodKey(m), StringComparer.Ordinal).ToImmutableArray(),
            featureCoverage.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToImmutableSortedDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            profile == CorpusProfile.ClassicStateMachines
                ? BuildClassicStateMachineCoverage(methodReports)
                : null);
    }

    internal static ImmutableSortedDictionary<string, ClassicStateMachineFeatureMetrics>
        BuildClassicStateMachineCoverage(IEnumerable<CorpusMethodSnapshot> methods)
    {
        var coverage = new Dictionary<string, ClassicStateMachineFeatureMetrics>(StringComparer.Ordinal);
        foreach (var method in methods)
        {
            // Count source kickoffs, not every generated interface/support member.
            // The kickoff's post-pass fidelity is the product-visible result of
            // reconstructing its sibling MoveNext through the import seam.
            if (method.Type.Contains('<', StringComparison.Ordinal))
                continue;

            string? feature = method.Method switch
            {
                var name when name.StartsWith("AsyncIterator_", StringComparison.Ordinal)
                    => "classic-async-iterator",
                var name when name.StartsWith("Iterator_", StringComparison.Ordinal)
                    => "classic-iterator",
                var name when name.StartsWith("Async_", StringComparison.Ordinal)
                    => "classic-async",
                _ => null,
            };
            if (feature is null)
                continue;

            coverage.TryGetValue(feature, out var current);
            current ??= new ClassicStateMachineFeatureMetrics();
            coverage[feature] = current with
            {
                Population = current.Population + 1,
                FullyRaised = current.FullyRaised + (method.FullyRaised ? 1 : 0),
                Residual = current.Residual + (method.FullyRaised ? 0 : 1),
            };
        }

        return coverage.ToImmutableSortedDictionary(StringComparer.Ordinal);
    }

    static void RecordUnionCoverage(
        string assemblyPath,
        ConcurrentDictionary<string, int> featureCoverage)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return;

        var unions = UnionTypeScanner.Scan(pe)
            .Where(union => union.ImplementsIUnion && union.CaseTypes.Count > 0)
            .ToArray();
        if (unions.Length == 0)
            return;

        AddFeature(featureCoverage, "union-types", unions.Length);
        var unionNames = unions.Select(union => union.TypeName).ToHashSet(StringComparer.Ordinal);
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        foreach (var type in surface.Types.Where(type => unionNames.Contains(type.FullName)))
        {
            var source = MemberBodyProducer.Project(type, assemblyPath, pdbPath: null).Output;
            if (source is not null && source.Split('\n').Any(line =>
                line.StartsWith("public union ", StringComparison.Ordinal)
                || line.StartsWith("public readonly union ", StringComparison.Ordinal)))
            {
                AddFeature(featureCoverage, "union-declarations");
            }
        }
    }

    static void RecordMethodFeatureCoverage(
        IrFunction function,
        string assemblyName,
        IReadOnlyCollection<string> changedPasses,
        ConcurrentDictionary<string, int> featureCoverage)
    {
        if (function.IsRuntimeAsync == MetadataFactState.Yes)
        {
            AddFeature(featureCoverage, "runtime-async-methods");
            bool hasAwaitForeach = function.Descendants
                .OfType<ForeachStatement>()
                .Any(statement => statement.IsAwait);
            bool hasAwaitUsing = function.Descendants
                .OfType<UsingStatement>()
                .Any(statement => statement.IsAwait);
            if (hasAwaitUsing)
                AddFeature(featureCoverage, "runtime-async-await-using-methods");
            if (hasAwaitForeach
                || function.Descendants.Any(node => node is ForLoop or WhileLoop))
            {
                AddFeature(featureCoverage, "runtime-async-loop-methods");
            }
            if (hasAwaitForeach
                || hasAwaitUsing
                || function.Descendants.Any(node => node is TryCatch or TryFinally))
            {
                AddFeature(featureCoverage, "runtime-async-exception-methods");
            }
        }

        if (changedPasses.Contains("await-recovery"))
            AddFeature(featureCoverage, "await-recovery-methods");
        if (changedPasses.Contains("runtime-async-awaiter"))
            AddFeature(featureCoverage, "runtime-async-awaiter-methods");
        if (changedPasses.Contains("union-switch-expression"))
            AddFeature(featureCoverage, "union-switch-methods");

        if (assemblyName == "ILInspector.Decompiler.Fixtures.NewUnsafe"
            && function.UsesUpdatedMemorySafetyRules)
        {
            AddFeature(featureCoverage, "updated-memory-safety-methods");
        }
        else if (assemblyName == "ILInspector.Decompiler.Fixtures.LegacyUnsafe"
                 && !function.UsesUpdatedMemorySafetyRules)
        {
            AddFeature(featureCoverage, "legacy-memory-safety-control-methods");
        }

        if (function.UsesUpdatedMemorySafetyRules
            && function.Descendants.OfType<Call>().Any(call =>
                call.Callee.RequiresUnsafe
                && call.Callee.DeclaringType.Assembly != function.DeclaringType.Assembly))
        {
            AddFeature(featureCoverage, "cross-assembly-requires-unsafe-methods");
        }
    }

    internal static IReadOnlyDictionary<string, int>
        RecordMethodFeatureCoverageForTesting(IrFunction function)
    {
        var coverage = new ConcurrentDictionary<string, int>(
            StringComparer.Ordinal);
        RecordMethodFeatureCoverage(function, "", [], coverage);
        return coverage;
    }

    /// <summary>
    /// Tags classic-state-machine fixture methods by the name-prefix contract
    /// documented on <c>ClassicStateMachineFixtures</c> (#2818): a top-level
    /// kickoff method and its compiler-generated state machine type both embed
    /// the original method name, so either <paramref name="typeName"/> (e.g.
    /// <c>&lt;Async_AwaitValue&gt;d__2</c>) or <paramref name="methodName"/>
    /// carries the prefix. Check the more specific "AsyncIterator_"/"Iterator_"
    /// prefixes before the plain "Async_" prefix, since "AsyncIterator_"
    /// textually contains "Iterator_".
    /// </summary>
    static void RecordClassicStateMachineFeatureCoverage(
        string typeName,
        string methodName,
        ConcurrentDictionary<string, int> featureCoverage)
    {
        string haystack = typeName + "::" + methodName;
        if (haystack.Contains("AsyncIterator_", StringComparison.Ordinal))
            AddFeature(featureCoverage, "classic-async-iterator-methods");
        else if (haystack.Contains("Iterator_", StringComparison.Ordinal))
            AddFeature(featureCoverage, "classic-iterator-methods");
        else if (haystack.Contains("Async_", StringComparison.Ordinal))
            AddFeature(featureCoverage, "classic-async-methods");

        if (haystack.Contains("Switch_", StringComparison.Ordinal))
            AddFeature(featureCoverage, "switch-methods");
    }

    static void AddFeature(
        ConcurrentDictionary<string, int> featureCoverage,
        string feature,
        int count = 1)
        => featureCoverage.AddOrUpdate(feature, count, (_, current) => current + count);

    static ValiditySensorMetrics AnalyzeValidity(
        IReadOnlyList<string> assemblies,
        int cap,
        Dictionary<string, CorpusMethodSnapshot> methods,
        int? workers,
        bool sequential)
    {
        if (cap <= 0)
            return new ValiditySensorMetrics(0, 0, 0);

        var results = new List<ValidityCheck.MethodResult>();
        foreach (var assembly in assemblies)
        {
            var portablePath = PortablePath(assembly);
            foreach (var result in ValidityCheck.Evaluate(assembly, cap, lowered: false, importSiblingBodies: false, workers, sequential))
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

    static StructuringSensorMetrics AnalyzeStructuring(IReadOnlyList<string> assemblies, int methodCap, int? workers, bool sequential)
    {
        long total = 0, crashes = 0, structured = 0, stoppedContainers = 0, methodsWithStop = 0;
        var reasons = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        using var metadata = CorpusMetadata.Create(assemblies);
        var options = new ParallelOptions { MaxDegreeOfParallelism = sequential ? 1 : (workers ?? Math.Max(1, Environment.ProcessorCount - 2)) };

        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            // Pre-warm type maps
            _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));
            int assemblyMethods = 0;
            
            var stableSample = IrImporter.GetStableSampleCandidates(source, methodCap).ToList();

            Parallel.ForEach(stableSample, options, item =>
            {
                var function = item.Build(source);
                Interlocked.Increment(ref assemblyMethods);
                Interlocked.Increment(ref total);
                var diagnostics = new StructuringDiagnostics();
                var context = new PassContext(new Stepper(enabled: false), diagnostics);
                try
                {
                    IrPasses.Run(function, IrPasses.Default, context);
                }
                catch
                {
                    Interlocked.Increment(ref crashes);
                    return; // equivalent to continue
                }

                Interlocked.Add(ref structured, diagnostics.Structured);
                if (diagnostics.Stops.Count > 0)
                    Interlocked.Increment(ref methodsWithStop);
                foreach (var reason in diagnostics.Stops)
                {
                    Interlocked.Increment(ref stoppedContainers);
                    reasons.AddOrUpdate(reason, 1, (_, v) => v + 1);
                }
            });
        }

        return new StructuringSensorMetrics(total, structured, stoppedContainers, methodsWithStop, crashes, reasons.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal));
    }

    static ImmutableArray<FidelityCapReport> AnalyzeFidelity(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<int> caps,
        Dictionary<string, CorpusMethodSnapshot> methods,
        int primaryCap,
        int? workers,
        bool sequential,
        CorpusFidelityOracle fidelityOracle)
    {
        var reports = ImmutableArray.CreateBuilder<FidelityCapReport>();
        foreach (var cap in caps.Where(cap => cap > 0).Distinct().OrderBy(cap => cap))
        {
            var usefulResults = new List<FidelityCheck.CompileBackResult>();
            var allResults = new List<FidelityCheck.CompileBackResult>();
            int parityRescued = 0, paritySame = 0, parityWorse = 0;
            foreach (var assembly in assemblies)
            {
                var portablePath = PortablePath(assembly);
                var targetAttempts = DeterministicCompileBackTargetAttempts(methods.Values, assembly, cap);
                FidelityOracleEvaluation evaluation;
                try
                {
                    evaluation = fidelityOracle == CorpusFidelityOracle.CompileBack
                        ? EvaluateCompileBackTargets([assembly], targetAttempts, cap)
                        : EvaluateReturnToSender(assembly, targetAttempts, cap);
                }
                catch (Exception ex) when (
                    fidelityOracle == CorpusFidelityOracle.ReturnToSender
                    && ex is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
                {
                    HarnessLog.Status($"RTS parity skipped {portablePath}: {ex.Message}");
                    continue;
                }
                var assemblyUsefulResults = evaluation.Results;
                usefulResults.AddRange(assemblyUsefulResults);
                if (evaluation.Parity is { } parity)
                {
                    parityRescued += parity.RescuedMethods;
                    paritySame += parity.SameMethods;
                    parityWorse += parity.WorseMethods;
                }
                if (cap == primaryCap)
                {
                    foreach (var result in assemblyUsefulResults)
                    {
                        string key = MethodKey(portablePath, result.Type, result.Method, result.Signature);
                        if (methods.TryGetValue(key, out var methodSnapshot))
                        {
                            methods[key] = methodSnapshot with
                            {
                                FidelityCheck = result.Status.ToString(),
                                FidelityCapture = fidelityOracle == CorpusFidelityOracle.ReturnToSender
                                    ? result.CaptureDetail ?? result.Capture.ToString()
                                    : methodSnapshot.FidelityCapture,
                                FidelityReference = ReferenceStatus(evaluation, result),
                            };
                        }
                    }
                }
                allResults.AddRange(evaluation.AllResults);
            }
            var metrics = new FidelitySensorMetrics(
                ContractVersion: CurrentFidelityContractVersion,
                CheckedMethods: usefulResults.Count,
                ExactMethods: usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.Exact),
                OpcodeDiffMethods: usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.OpcodeDiff),
                OperandDiffMethods: usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.OperandDiff),
                FidelityUnavailableMethods: usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.FidelityUnavailable),
                RecompileFailMethods: usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.RecompileFail),
                ContextFailMethods: usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.ContextFail),
                NotFullMethods: usefulResults.Count(result => result.Status == FidelityCheck.CompileBackStatus.NotFull),
                ReturnToSenderParity: fidelityOracle == CorpusFidelityOracle.ReturnToSender
                    ? new ReturnToSenderParityMetrics(parityRescued, paritySame, parityWorse)
                    : null);
            var contextBuckets = FidelityCheck.SummarizeFailures(allResults, FidelityCheck.CompileBackStatus.ContextFail);
            var recompileBuckets = FidelityCheck.SummarizeFailures(allResults, FidelityCheck.CompileBackStatus.RecompileFail);
            reports.Add(new FidelityCapReport(cap, metrics, contextBuckets, recompileBuckets));
        }

        if (reports.Count == 0)
            reports.Add(new FidelityCapReport(
                0,
                FidelitySensorMetrics.Empty,
                ImmutableDictionary<string, FidelityCheck.FailureBucketSummary>.Empty,
                ImmutableDictionary<string, FidelityCheck.FailureBucketSummary>.Empty));
        return reports.ToImmutable();
    }

    internal static IReadOnlyList<FidelityCheck.CompileBackResult> EvaluateReturnToSenderForTesting(
        string assemblyPath,
        int cap)
        => EvaluateReturnToSender(assemblyPath, cap, workers: 1, sequential: true).Results;

    internal static ReturnToSenderParityMetrics SummarizeReturnToSenderParityForTesting(
        IReadOnlyList<FidelityCheck.CompileBackResult> referenceResults,
        IReadOnlyList<FidelityCheck.CompileBackResult> currentResults)
        => SummarizeReturnToSenderParity(referenceResults, currentResults);

    static FidelityOracleEvaluation EvaluateReturnToSender(
        string assemblyPath,
        int cap,
        int? workers,
        bool sequential)
        => EvaluateReturnToSender(
            assemblyPath,
            DeterministicCompileBackTargetAttemptsForAssembly(assemblyPath, cap),
            cap);

    static FidelityOracleEvaluation EvaluateCompileBackTargets(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<FidelityCheck.CompileBackTarget> targetAttempts,
        int cap)
    {
        var evaluatedResults = EvaluateTargetsInAttemptOrderUntilUseful(assemblies, targetAttempts, cap);
        var usefulResults = evaluatedResults
            .Where(FidelityCheck.IsUsefulCorpusSample)
            .Take(cap)
            .ToArray();
        return new FidelityOracleEvaluation(usefulResults, AllResults: evaluatedResults);
    }

    static FidelityOracleEvaluation EvaluateReturnToSender(
        string assemblyPath,
        IReadOnlyList<FidelityCheck.CompileBackTarget> targetAttempts,
        int cap)
    {
        var evaluatedResults = EvaluateTargetsInAttemptOrderUntilUseful([assemblyPath], targetAttempts, cap);
        var targetSample = evaluatedResults
            .Where(FidelityCheck.IsUsefulCorpusSample)
            .Take(cap)
            .ToArray();
        if (targetSample.Length == 0)
            return new FidelityOracleEvaluation([], AllResults: targetSample);

        var requestedTargets = targetSample
            .Select(result => new ReturnToSender.RequestedTarget(
                result.Type,
                result.Method,
                result.Overload,
                result.Signature))
            .ToArray();
        var returnToSenderResults = ReturnToSender.CompileBackTargets(
                assemblyPath,
                requestedTargets,
                applyCompileBackFloor: false)
            .ToArray();
        var alignedResults = AlignReturnToSenderResults(targetSample, returnToSenderResults);
        return new FidelityOracleEvaluation(
            alignedResults,
            AllResults: alignedResults,
            targetSample.ToDictionary(
                FidelityResultKey,
                result => result.Status,
                StringComparer.Ordinal),
            SummarizeReturnToSenderParity(targetSample, alignedResults));
    }

    static IReadOnlyList<FidelityCheck.CompileBackResult> EvaluateTargetsInAttemptOrderUntilUseful(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<FidelityCheck.CompileBackTarget> targetAttempts,
        int cap)
    {
        if (cap <= 0 || targetAttempts.Count == 0)
            return [];

        int batchSize = cap == int.MaxValue
            ? targetAttempts.Count
            : Math.Min(targetAttempts.Count, Math.Max(cap, 100));
        var results = new List<FidelityCheck.CompileBackResult>();
        int useful = 0;
        for (int offset = 0; offset < targetAttempts.Count && useful < cap; offset += batchSize)
        {
            var batch = targetAttempts.Skip(offset).Take(batchSize).ToArray();
            foreach (var result in EvaluateTargetsInAttemptOrder(assemblies, batch))
            {
                results.Add(result);
                if (FidelityCheck.IsUsefulCorpusSample(result) && ++useful >= cap)
                    break;
            }
        }

        return results;
    }

    static IReadOnlyList<FidelityCheck.CompileBackResult> EvaluateTargetsInAttemptOrder(
        IReadOnlyList<string> assemblies,
        IReadOnlyList<FidelityCheck.CompileBackTarget> targetAttempts)
    {
        var resultsByTarget = FidelityCheck.EvaluateTargets(assemblies, targetAttempts, lowered: false)
            .GroupBy(FidelityResultKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return targetAttempts
            .Select(CompileBackTargetKey)
            .Where(resultsByTarget.ContainsKey)
            .Select(key => resultsByTarget[key])
            .ToArray();
    }

    internal static IReadOnlyList<FidelityCheck.CompileBackResult> AlignReturnToSenderResultsForTesting(
        IReadOnlyList<FidelityCheck.CompileBackResult> targetSample,
        IReadOnlyList<ReturnToSender.Result> returnToSenderResults)
        => AlignReturnToSenderResults(targetSample, returnToSenderResults);

    static IReadOnlyList<FidelityCheck.CompileBackResult> AlignReturnToSenderResults(
        IReadOnlyList<FidelityCheck.CompileBackResult> targetSample,
        IReadOnlyList<ReturnToSender.Result> returnToSenderResults)
    {
        var resultsByTarget = returnToSenderResults
            .GroupBy(ReturnToSenderKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var results = new FidelityCheck.CompileBackResult[targetSample.Count];
        for (int i = 0; i < targetSample.Count; i++)
        {
            var target = targetSample[i];
            if (!resultsByTarget.TryGetValue(FidelityResultKey(target), out var result))
            {
                results[i] = target with
                {
                    Status = FidelityCheck.CompileBackStatus.ContextFail,
                    OriginalOpcodes = "",
                    RecompiledOpcodes = "",
                    Detail = "return-to-sender-target-unavailable",
                    Capture = FidelityCheck.CaptureMode.WholeModule,
                    CaptureDetail = "return-to-sender",
                };
                continue;
            }

            results[i] = new FidelityCheck.CompileBackResult(
                result.Plan.TargetMethod.Type,
                result.Plan.TargetMethod.Method,
                result.Plan.TargetMethod.Overload,
                result.Plan.TargetMethod.Signature,
                result.Status,
                result.OriginalOpcodes,
                result.RecompiledOpcodes,
                result.Detail,
                result.CompileBackFloor?.Capture ?? FidelityCheck.CaptureMode.WholeModule,
                result.UsedCompileBackFloor
                    ? "return-to-sender; compile-back-floor"
                    : "return-to-sender",
                result.FidelityDiff);
        }
        return results;
    }

    static ReturnToSenderParityMetrics SummarizeReturnToSenderParity(
        IReadOnlyList<FidelityCheck.CompileBackResult> referenceResults,
        IReadOnlyList<FidelityCheck.CompileBackResult> currentResults)
    {
        var currentByTarget = currentResults.ToDictionary(FidelityResultKey, StringComparer.Ordinal);
        int rescued = 0, same = 0, worse = 0;
        foreach (var reference in referenceResults)
        {
            if (!currentByTarget.TryGetValue(FidelityResultKey(reference), out var current))
            {
                worse++;
                continue;
            }

            if (reference.Status == current.Status)
                same++;
            else if (reference.Status != FidelityCheck.CompileBackStatus.Exact
                     && current.Status == FidelityCheck.CompileBackStatus.Exact)
                rescued++;
            else
                worse++;
        }
        return new ReturnToSenderParityMetrics(rescued, same, worse);
    }

    static string? ReferenceStatus(
        FidelityOracleEvaluation evaluation,
        FidelityCheck.CompileBackResult result)
        => evaluation.ReferenceStatuses is { } references
           && references.TryGetValue(FidelityResultKey(result), out var status)
            ? status.ToString()
            : null;

    static string FidelityResultKey(FidelityCheck.CompileBackResult result)
        => $"{result.Type}::{result.Method}::{result.Overload}::{result.Signature}";

    // The RTS-parity burn-down set: methods the product oracle recompiled Exact but
    // ReturnToSender could not (RecompileFail/ContextFail). These are the parity gaps
    // the RTS-orchestrator migration must close; the gate fails only when a NEW row
    // appears versus the committed burn-down manifest, so the known set stays a visible,
    // shrinking checklist rather than a silent tolerance.
    internal static ImmutableArray<CorpusMethodSnapshot> ExactReferenceRecompileRegressions(
        CorpusSensorSnapshot snapshot)
    {
        if (snapshot.FidelityOracle != CorpusFidelityOracle.ReturnToSender
            || snapshot.Methods is not { } methods)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<CorpusMethodSnapshot>();
        foreach (var method in methods)
        {
            if (string.Equals(method.FidelityReference, "Exact", StringComparison.Ordinal)
                && (string.Equals(method.FidelityCheck, "RecompileFail", StringComparison.Ordinal)
                    || string.Equals(method.FidelityCheck, "ContextFail", StringComparison.Ordinal)))
            {
                builder.Add(method);
            }
        }

        return builder.ToImmutable();
    }

    // Guards that make the RTS-parity burn-down flags refuse to run toothless: they
    // require the rts-parity oracle (else EnforceRtsParityBurndown never fires) AND a
    // positive fidelity cap (else the parity population is empty and every manifest row
    // is spuriously "resolved" while the run exits 0). Enforcing against a just-emitted
    // manifest (same path) would self-certify new regressions, so that is rejected too.
    // Returns an error message, or null when the flags are usable.
    internal static string? ValidateRtsParityBurndownFlags(
        CorpusFidelityOracle fidelityOracle,
        IReadOnlyList<int> fidelityCompileCaps,
        string? rtsParityBurndown,
        string? emitRtsParityBurndown)
    {
        if (rtsParityBurndown is null && emitRtsParityBurndown is null)
            return null;
        if (fidelityOracle != CorpusFidelityOracle.ReturnToSender)
            return "--rts-parity-burndown and --emit-rts-parity-burndown require --corpus-fidelity-oracle rts-parity.";
        if (!fidelityCompileCaps.Any(cap => cap > 0))
            return "--rts-parity-burndown and --emit-rts-parity-burndown require a positive --corpus-fidelity-cap so the parity population is actually evaluated.";
        if (rtsParityBurndown is not null
            && emitRtsParityBurndown is not null
            && string.Equals(
                Path.GetFullPath(rtsParityBurndown),
                Path.GetFullPath(emitRtsParityBurndown),
                StringComparison.Ordinal))
        {
            return "--rts-parity-burndown and --emit-rts-parity-burndown must not point at the same file; enforcing against a just-emitted manifest would self-certify new regressions.";
        }
        return null;
    }

    internal sealed record RtsParityBurndownRow(string Method, string Status);

    internal sealed record RtsParityBurndownManifest(
        string Description,
        string Command,
        ImmutableArray<RtsParityBurndownRow> Rows);

    internal sealed record RtsParityBurndownEvaluation(
        ImmutableArray<CorpusMethodSnapshot> NewRegressions,
        ImmutableArray<CorpusMethodSnapshot> KnownGaps,
        ImmutableArray<string> ResolvedRows);

    // Pure gate: compare the current Exact->recompile-failure set against the committed
    // burn-down. A NEW row (not in the manifest) is a hard regression; a row present in
    // the manifest but no longer failing is "resolved" and should be dropped when the
    // manifest is regenerated. Manifest rows are keyed by DisplayMethod so a change to a
    // method's parity is traceable to a named target.
    internal static RtsParityBurndownEvaluation EvaluateRtsParityBurndown(
        CorpusSensorSnapshot current,
        IReadOnlyCollection<string> knownRows)
    {
        var currentGaps = ExactReferenceRecompileRegressions(current);
        var known = new HashSet<string>(knownRows, StringComparer.Ordinal);
        var currentKeys = new HashSet<string>(currentGaps.Select(m => m.DisplayMethod), StringComparer.Ordinal);

        var newRegressions = currentGaps
            .Where(m => !known.Contains(m.DisplayMethod))
            .ToImmutableArray();
        var resolved = knownRows
            .Where(row => !currentKeys.Contains(row))
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToImmutableArray();

        return new RtsParityBurndownEvaluation(newRegressions, currentGaps, resolved);
    }

    static RtsParityBurndownManifest BuildRtsParityBurndown(CorpusSensorSnapshot current)
    {
        var rows = ExactReferenceRecompileRegressions(current)
            .OrderBy(m => m.DisplayMethod, StringComparer.Ordinal)
            .Select(m => new RtsParityBurndownRow(m.DisplayMethod, m.FidelityCheck))
            .ToImmutableArray();
        return new RtsParityBurndownManifest(
            Description: "RTS-parity burn-down: methods the product compile-back oracle recompiled Exact "
                + "but ReturnToSender did not. Regenerate mechanically with --emit-rts-parity-burndown; "
                + "the RTS-orchestrator work drains this list.",
            Command: "decompiler-harness [corpus] --compile-cap 0 --corpus-fidelity-cap 3 "
                + "--corpus-fidelity-oracle rts-parity --emit-rts-parity-burndown "
                + "tools/DecompilerHarness/corpus/rts-parity-burndown.json",
            Rows: rows);
    }

    // Enforces the RTS-parity burn-down gate and optionally regenerates the manifest.
    // Returns true when a NEW Exact->recompile-failure regression is present.
    static bool EnforceRtsParityBurndown(
        CorpusSensorSnapshot current,
        string? burndownPath,
        string? emitBurndownPath,
        bool quiet)
    {
        if (emitBurndownPath is not null)
        {
            var manifest = BuildRtsParityBurndown(current);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(emitBurndownPath)) ?? ".");
            File.WriteAllText(emitBurndownPath, JsonSerializer.Serialize(manifest, JsonOptions()));
            if (!quiet)
            {
                Console.WriteLine();
                HarnessLog.Status($"Wrote RTS-parity burn-down ({manifest.Rows.Length} row(s)): {emitBurndownPath}");
            }
        }

        IReadOnlyCollection<string> knownRows = burndownPath is not null
            ? ReadRtsParityBurndown(burndownPath).Rows.Select(r => r.Method).ToImmutableArray()
            : [];
        var evaluation = EvaluateRtsParityBurndown(current, knownRows);

        if (!quiet)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"RTS parity burn-down: {evaluation.KnownGaps.Length} method(s) recompiled Exact under the "
                + "product oracle but not under ReturnToSender.");
            foreach (var gap in evaluation.KnownGaps.OrderBy(m => m.DisplayMethod, StringComparer.Ordinal))
            {
                bool isNew = evaluation.NewRegressions.Any(m => m.DisplayMethod == gap.DisplayMethod);
                Console.WriteLine($"- {(isNew ? "NEW " : "")}{gap.DisplayMethod} [{gap.FidelityCheck}]");
            }
            foreach (var resolved in evaluation.ResolvedRows)
                Console.WriteLine($"- resolved (regenerate manifest to drop): {resolved}");
        }

        if (!evaluation.NewRegressions.IsEmpty)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"RTS parity lost on {evaluation.NewRegressions.Length} new method(s) not in the burn-down manifest:");
            foreach (var offender in evaluation.NewRegressions.OrderBy(m => m.DisplayMethod, StringComparer.Ordinal))
                Console.WriteLine($"- {offender.DisplayMethod} recompiled Exact under the product oracle but {offender.FidelityCheck} under ReturnToSender");
        }

        return !evaluation.NewRegressions.IsEmpty;
    }

    internal static RtsParityBurndownManifest ReadRtsParityBurndown(string path)
    {
        var manifest = JsonSerializer.Deserialize<RtsParityBurndownManifest>(File.ReadAllText(path), JsonOptions())
            ?? throw new InvalidOperationException($"Could not read RTS-parity burn-down '{path}'.");
        return manifest.Rows.IsDefault ? manifest with { Rows = [] } : manifest;
    }

    static string CompileBackTargetKey(FidelityCheck.CompileBackTarget target)
        => $"{target.Type}::{target.Method}::{target.Overload}::{target.Signature}";

    static string ReturnToSenderKey(ReturnToSender.Result result)
        => $"{result.Plan.TargetMethod.Type}::{result.Plan.TargetMethod.Method}::{result.Plan.TargetMethod.Overload}::{result.Plan.TargetMethod.Signature}";

    sealed record FidelityOracleEvaluation(
        IReadOnlyList<FidelityCheck.CompileBackResult> Results,
        IReadOnlyList<FidelityCheck.CompileBackResult> AllResults,
        IReadOnlyDictionary<string, FidelityCheck.CompileBackStatus>? ReferenceStatuses = null,
        ReturnToSenderParityMetrics? Parity = null);

    internal static IReadOnlyList<FidelityCheck.CompileBackTarget> DeterministicCompileBackTargetAttemptsForTesting(
        IReadOnlyList<CorpusMethodSnapshot> methods,
        string assemblyPath,
        int cap)
        => DeterministicCompileBackTargetAttempts(methods, assemblyPath, cap);

    static IReadOnlyList<FidelityCheck.CompileBackTarget> DeterministicCompileBackTargetAttemptsForAssembly(
        string assemblyPath,
        int cap)
    {
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);
        string portablePath = PortablePath(assemblyPath);
        var methods = IrImporter.GetStableSampleCandidates(source, DeterministicAttemptCap(cap))
            .Select(candidate =>
            {
                var function = candidate.Build(source);
                return new CorpusMethodSnapshot(
                    source.AssemblyName,
                    portablePath,
                    candidate.TypeName,
                    candidate.MethodName,
                    candidate.Overload,
                    CorpusMethodIdentity.SignatureText(function.Signature),
                    function.Fidelity.ToString(),
                    FullyRaised: function.Fidelity == DecompilationFidelity.Full,
                    Residual: null,
                    PassBug: null,
                    Validity: "not-sampled",
                    FidelityCheck: "not-sampled");
            })
            .ToArray();
        return DeterministicCompileBackTargetAttempts(methods, assemblyPath, cap);
    }

    static IReadOnlyList<FidelityCheck.CompileBackTarget> DeterministicCompileBackTargetAttempts(
        IEnumerable<CorpusMethodSnapshot> methods,
        string assemblyPath,
        int cap)
    {
        if (cap <= 0)
            return [];

        string portablePath = PortablePath(assemblyPath);
        return methods
            .Where(method => string.Equals(method.AssemblyPath, portablePath, StringComparison.Ordinal)
                && !FidelityCheck.IsSynthesizedMember(method.Type, method.Method))
            .OrderBy(StableMethodHash)
            .ThenBy(MethodKey, StringComparer.Ordinal)
            .Take(DeterministicAttemptCap(cap))
            .Select(method => new FidelityCheck.CompileBackTarget(
                assemblyPath,
                method.Type,
                method.Method,
                method.Overload,
                method.Signature))
            .ToArray();
    }

    static int DeterministicAttemptCap(int cap)
        => cap > int.MaxValue / 10
            ? int.MaxValue
            : Math.Max(cap * 10, 100);

    static ulong StableMethodHash(CorpusMethodSnapshot method)
        => StableHash(MethodKey(method));

    static ulong StableHash(string text)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char ch in text)
        {
            hash ^= (byte)ch;
            hash *= prime;
            hash ^= (byte)(ch >> 8);
            hash *= prime;
        }
        return hash;
    }

    static string MethodKey(CorpusMethodSnapshot method)
        => method.StableKey;

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

    internal static string PortablePath(string path)
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
        bool sameFidelityOracle = baseline.FidelityOracle == current.FidelityOracle;
        bool sameFidelityContract =
            baseline.Metrics.Fidelity.ContractVersion == currentFidelityMetrics.ContractVersion;

        if (baseline.Profile != current.Profile)
        {
            failures.Add(
                $"corpus profile differs (baseline {CorpusProfileName(baseline.Profile)}, "
                + $"current {CorpusProfileName(current.Profile)})");
        }
        failures.AddRange(FeatureCoverageFailures(current));
        failures.AddRange(ClassicStateMachineCoverageFailures(baseline, current));
        if (baseline.FeatureCoverage is not null && current.FeatureCoverage is not null)
        {
            foreach (var (feature, baselineCount) in baseline.FeatureCoverage)
            {
                int currentCount = current.FeatureCoverage.GetValueOrDefault(feature);
                if (currentCount < baselineCount)
                {
                    failures.Add(
                        $"feature evidence '{feature}' dropped "
                        + $"(baseline {baselineCount}, current {currentCount})");
                }
            }
        }
        if (current.ValidityCompileCap < baseline.ValidityCompileCap)
            failures.Add($"validity cap lower than baseline (baseline {baseline.ValidityCompileCap}, current {current.ValidityCompileCap})");
        if (currentFidelityCap < baseline.FidelityCompileCap)
            failures.Add($"fidelity cap lower than baseline (baseline {baseline.FidelityCompileCap}, current {currentFidelityCap})");
        if (!sameFidelityOracle && (baseline.FidelityCompileCap > 0 || current.FidelityCompileCap > 0))
        {
            failures.Add(
                $"fidelity oracle differs (baseline {FidelityOracleName(baseline.FidelityOracle)}, "
                + $"current {FidelityOracleName(current.FidelityOracle)})");
        }
        if (!sameFidelityContract && (baseline.FidelityCompileCap > 0 || current.FidelityCompileCap > 0))
        {
            failures.Add(
                $"fidelity contract differs (baseline v{baseline.Metrics.Fidelity.ContractVersion}, "
                + $"current v{currentFidelityMetrics.ContractVersion})");
        }
        if (current.MethodCap != baseline.MethodCap)
            failures.Add($"method cap differs from baseline (baseline {CapText(baseline.MethodCap)}, current {CapText(current.MethodCap)})");
        if (current.Metrics.SemanticCheckedMethods < baseline.Metrics.SemanticCheckedMethods)
            failures.Add($"semantic checked methods lower than baseline (baseline {baseline.Metrics.SemanticCheckedMethods}, current {current.Metrics.SemanticCheckedMethods})");
        if (sameFidelityOracle && sameFidelityContract
            && currentFidelityMetrics.CheckedMethods < baseline.Metrics.Fidelity.CheckedMethods)
            failures.Add($"fidelity checked methods lower than baseline (baseline {baseline.Metrics.Fidelity.CheckedMethods}, current {currentFidelityMetrics.CheckedMethods})");

        if (gateAggregateRates || baselinePinned is null || currentPinned is null)
        {
            AddRateRegression(
                failures,
                "detected lowering residue rate",
                RateBasisPoints(baseline.Metrics.TotalMethods - baseline.Metrics.FullyRaisedMethods, baseline.Metrics.TotalMethods),
                RateBasisPoints(current.Metrics.TotalMethods - current.Metrics.FullyRaisedMethods, current.Metrics.TotalMethods),
                tolerance.FullyRaisedDropBasisPoints,
                lowerIsRegression: false);
            AddRateRegression(failures, "conditional-branch residue rate", baseline.Metrics.ConditionalBranchBasisPoints, current.Metrics.ConditionalBranchBasisPoints, tolerance.ConditionalBranchIncreaseBasisPoints, lowerIsRegression: false);
            AddRateRegression(failures, "forward-merge stop rate", baseline.Metrics.ForwardMergeBasisPoints, current.Metrics.ForwardMergeBasisPoints, tolerance.ForwardMergeIncreaseBasisPoints, lowerIsRegression: false);
        }
        else
        {
            AddRateRegression(
                failures,
                "detected lowering residue rate (pinned)",
                RateBasisPoints(baselinePinned.TotalMethods - baselinePinned.FullyRaisedMethods, baselinePinned.TotalMethods),
                RateBasisPoints(currentPinned.TotalMethods - currentPinned.FullyRaisedMethods, currentPinned.TotalMethods),
                tolerance.FullyRaisedDropBasisPoints,
                lowerIsRegression: false);
            AddRateRegression(failures, "conditional-branch residue rate (pinned)", baselinePinned.ConditionalBranchBasisPoints, currentPinned.ConditionalBranchBasisPoints, tolerance.ConditionalBranchIncreaseBasisPoints, lowerIsRegression: false);
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
            if (HaveSameMethodSample(
                baseline.Methods,
                current.Methods,
                static method => IsPinnedAssembly(method.AssemblyPath)
                    && method.Validity != "not-sampled"))
            {
                AddCountRegression(failures, "Full malformed methods (pinned)", basePinned.FullMalformed, curPinned.FullMalformed, tolerance.FullMalformedIncrease);
            }
            if (HaveSameMethodSample(
                baseline.Methods,
                current.Methods,
                static method => IsPinnedAssembly(method.AssemblyPath)
                    && (method.Validity == "valid"
                        || method.Validity.StartsWith("semantic-defect:", StringComparison.Ordinal))))
            {
                AddCountRegression(failures, "semantic defect methods (pinned)", basePinned.SemanticDefect, curPinned.SemanticDefect, tolerance.SemanticDefectIncrease);
            }

            // Fidelity is sampled far more thinly than validity, and that small sample
            // currently lands almost entirely on repo assemblies, so the pinned subset
            // often has zero fidelity-checked methods. Equal caps do not guarantee equal
            // samples when the corpus changes, so gate only when the exact checked-method
            // populations match. Otherwise rely on changed-method fidelity.
            if (basePinned.FidelityChecked > 0 && curPinned.FidelityChecked > 0
                && sameFidelityOracle
                && sameFidelityContract
                && current.FidelityCompileCap == baseline.FidelityCompileCap
                && HaveSameMethodSample(
                    baseline.Methods,
                    current.Methods,
                    static method => IsPinnedAssembly(method.AssemblyPath)
                        && method.FidelityCheck != "not-sampled"))
            {
                AddCountRegression(failures, "fidelity opcode diffs (pinned)", basePinned.OpcodeDiff, curPinned.OpcodeDiff, tolerance.FidelityOpcodeDiffIncrease);
                AddCountRegression(failures, "fidelity operand diffs (pinned)", basePinned.OperandDiff, curPinned.OperandDiff, tolerance.FidelityOperandDiffIncrease);
                AddCountRegression(failures, "fidelity unavailable comparisons (pinned)", basePinned.FidelityUnavailable, curPinned.FidelityUnavailable, tolerance.FidelityUnavailableIncrease);
                AddCountRegression(failures, "fidelity recompile failures (pinned)", basePinned.RecompileFail, curPinned.RecompileFail, tolerance.FidelityRecompileFailIncrease);
                AddCountRegression(failures, "fidelity context failures (pinned)", basePinned.ContextFail, curPinned.ContextFail, tolerance.FidelityContextFailIncrease);
            }
        }
        else
        {
            AddCountRegression(failures, "Full malformed methods", baseline.Metrics.FullMalformedMethods, current.Metrics.FullMalformedMethods, tolerance.FullMalformedIncrease);
            AddCountRegression(failures, "semantic defect methods", baseline.Metrics.SemanticDefectMethods, current.Metrics.SemanticDefectMethods, tolerance.SemanticDefectIncrease);
            if (sameFidelityOracle && sameFidelityContract
                && baseline.Metrics.Fidelity.CheckedMethods > 0)
            {
                AddCountRegression(failures, "fidelity opcode diffs", baseline.Metrics.Fidelity.OpcodeDiffMethods, currentFidelityMetrics.OpcodeDiffMethods, tolerance.FidelityOpcodeDiffIncrease);
                AddCountRegression(failures, "fidelity operand diffs", baseline.Metrics.Fidelity.OperandDiffMethods, currentFidelityMetrics.OperandDiffMethods, tolerance.FidelityOperandDiffIncrease);
                AddCountRegression(failures, "fidelity unavailable comparisons", baseline.Metrics.Fidelity.FidelityUnavailableMethods, currentFidelityMetrics.FidelityUnavailableMethods, tolerance.FidelityUnavailableIncrease);
                AddCountRegression(failures, "fidelity recompile failures", baseline.Metrics.Fidelity.RecompileFailMethods, currentFidelityMetrics.RecompileFailMethods, tolerance.FidelityRecompileFailIncrease);
                AddCountRegression(failures, "fidelity context failures", baseline.Metrics.Fidelity.ContextFailMethods, currentFidelityMetrics.ContextFailMethods, tolerance.FidelityContextFailIncrease);
            }
        }

        if (sameFidelityOracle
            && sameFidelityContract
            && baseline.Metrics.Fidelity.ReturnToSenderParity is { } baselineParity
            && currentFidelityMetrics.ReturnToSenderParity is { } currentParity
            && HaveSameMethodSample(
                baseline.Methods,
                current.Methods,
                static method => method.FidelityCheck != "not-sampled"))
        {
            AddCountRegression(
                failures,
                "RTS parity worse methods",
                baselineParity.WorseMethods,
                currentParity.WorseMethods,
                tolerance: 0);
        }

        AddCountRegression(failures, "pass bugs", baseline.Metrics.PassBugs, current.Metrics.PassBugs, tolerance.PassBugIncrease);

        return failures.ToImmutable();
    }

    internal static ImmutableArray<string> ClassicStateMachineCoverageFailures(
        CorpusSensorSnapshot baseline,
        CorpusSensorSnapshot current)
    {
        if (current.Profile != CorpusProfile.ClassicStateMachines)
            return [];

        var failures = ImmutableArray.CreateBuilder<string>();
        foreach (string feature in new[] { "classic-async", "classic-iterator", "classic-async-iterator" })
        {
            var currentMetrics = current.ClassicStateMachineCoverage?.GetValueOrDefault(feature);
            if (currentMetrics is null || currentMetrics.Population == 0)
            {
                failures.Add($"classic state-machine evidence '{feature}' has no kickoff specimens");
                continue;
            }

            var baselineMetrics = baseline.ClassicStateMachineCoverage?.GetValueOrDefault(feature);
            if (baselineMetrics is null)
                continue;
            if (currentMetrics.Population < baselineMetrics.Population)
            {
                failures.Add(
                    $"classic state-machine population '{feature}' dropped "
                    + $"(baseline {baselineMetrics.Population}, current {currentMetrics.Population})");
            }
            if (currentMetrics.FullyRaised < baselineMetrics.FullyRaised)
            {
                failures.Add(
                    $"classic state-machine fully raised '{feature}' dropped "
                    + $"(baseline {baselineMetrics.FullyRaised}, current {currentMetrics.FullyRaised})");
            }
            if (currentMetrics.Population == baselineMetrics.Population
                && currentMetrics.Residual > baselineMetrics.Residual)
            {
                failures.Add(
                    $"classic state-machine residual '{feature}' increased "
                    + $"(baseline {baselineMetrics.Residual}, current {currentMetrics.Residual})");
            }
        }
        return failures.ToImmutable();
    }

    internal static ImmutableArray<string> FeatureCoverageFailures(CorpusSensorSnapshot snapshot)
    {
        var required = snapshot.Profile switch
        {
            CorpusProfile.OptInNet11 => RequiredOptInNet11Features,
            CorpusProfile.ClassicStateMachines => RequiredClassicStateMachinesFeatures,
            _ => (ImmutableSortedDictionary<string, int>?)null,
        };
        if (required is null)
            return [];

        var failures = ImmutableArray.CreateBuilder<string>();
        foreach (var (feature, minimum) in required)
        {
            int actual = snapshot.FeatureCoverage?.GetValueOrDefault(feature) ?? 0;
            if (actual < minimum)
                failures.Add($"feature evidence '{feature}' is {actual}; expected at least {minimum}");
        }
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
        int malformed = 0, semantic = 0, opcode = 0, operand = 0;
        int unavailable = 0, recompile = 0, context = 0, fidelityChecked = 0;
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
                case "OperandDiff": operand++; break;
                case "FidelityUnavailable": unavailable++; break;
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
            operand,
            unavailable,
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
            SchemaVersion: 2,
            GeneratedUtc: DateTimeOffset.UtcNow,
            BaselineGeneratedUtc: baseline.GeneratedUtc,
            CurrentGeneratedUtc: current.GeneratedUtc,
            BaselineFidelityContractVersion: baseline.Metrics.Fidelity.ContractVersion,
            CurrentFidelityContractVersion: current.Metrics.Fidelity.ContractVersion,
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
        Add("fidelityCapture", before.FidelityCapture, after.FidelityCapture);
        Add("fidelityReference", before.FidelityReference, after.FidelityReference);
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
        string fidelityOracle = FidelityOracleName(snapshot.FidelityOracle);
        Console.WriteLine("# Decompiler corpus sensor");
        Console.WriteLine();
        Console.WriteLine($"Assemblies: {snapshot.Assemblies.Count}");
        Console.WriteLine($"Methods: {metrics.TotalMethods}");
        if (snapshot.MethodCap is { } cap)
            Console.WriteLine($"Sample: hash-stable {Number(cap)} methods per assembly");
        var verified = VerifiedFullyRaised(snapshot);
        int loweringResidue = metrics.TotalMethods - metrics.FullyRaisedMethods;
        Console.WriteLine(
            $"Detected lowering residue: {loweringResidue} "
            + $"({FormatBps(RateBasisPoints(loweringResidue, metrics.TotalMethods))})");
        Console.WriteLine($"Conditional-branch residue: {metrics.ConditionalBranchMethods} ({FormatBps(metrics.ConditionalBranchBasisPoints)})");
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
        PrintFeatureCoverage(snapshot);
        if (snapshot.FidelityCompileCap <= 0)
            Console.WriteLine("Fidelity: not run");
        else if (fidelityReports.Length == 1)
        {
            var report = fidelityReports[0];
            Console.WriteLine(
                $"Fidelity contract v{report.Metrics.ContractVersion} ({fidelityOracle}, cap {report.Cap}; EH-blind): "
                + $"{report.Metrics.ExactMethods} exact, {report.Metrics.OpcodeDiffMethods} opcode diffs, "
                + $"{report.Metrics.OperandDiffMethods} operand diffs, "
                + $"{report.Metrics.FidelityUnavailableMethods} unavailable, "
                + $"{report.Metrics.RecompileFailMethods} recompile failures, "
                + $"{report.Metrics.ContextFailMethods} context failures over {report.Metrics.CheckedMethods} checked");
            PrintReturnToSenderParity(report.Metrics, "");
            PrintFailureBuckets(report.ContextFailureBuckets, report.RecompileFailureBuckets, "  ");
        }
        else
        {
            Console.WriteLine($"Fidelity coverage by cap ({fidelityOracle}):");
            foreach (var report in fidelityReports)
            {
                Console.WriteLine(
                    $"  cap {report.Cap}, contract v{report.Metrics.ContractVersion}: "
                    + $"{report.Metrics.ExactMethods} exact, {report.Metrics.OpcodeDiffMethods} opcode diffs, "
                    + $"{report.Metrics.OperandDiffMethods} operand diffs, "
                    + $"{report.Metrics.FidelityUnavailableMethods} unavailable, "
                    + $"{report.Metrics.RecompileFailMethods} recompile failures, "
                    + $"{report.Metrics.ContextFailMethods} context failures over {report.Metrics.CheckedMethods} checked");
                PrintReturnToSenderParity(report.Metrics, "  ");
                PrintFailureBuckets(report.ContextFailureBuckets, report.RecompileFailureBuckets, "    ");
            }
        }
        if (verified is { } fullyRaised)
        {
            Console.WriteLine(
                $"Fully raised: {fullyRaised.RaisedMethods}/{fullyRaised.CheckedMethods} "
                + $"({FormatBps(RateBasisPoints(fullyRaised.RaisedMethods, fullyRaised.CheckedMethods))} "
                + "of completed validity outcomes)");
        }
        PrintFidelityResidualPortfolio(snapshot);
    }

    static void PrintFidelityResidualPortfolio(CorpusSensorSnapshot snapshot)
    {
        if (snapshot.Methods is not { } methods
            || !methods.Any(static method => method.FidelityCauses is not null))
        {
            return;
        }

        var portfolio = FidelityResidualPortfolioBuilder.Build(
            methods,
            snapshot.Metrics.TotalMethods,
            snapshot.Metrics.FullyRaisedMethods);

        Console.WriteLine();
        Console.WriteLine($"## Fidelity residual portfolio (policy v{portfolio.PolicyVersion})");
        Console.WriteLine();
        Console.WriteLine(
            $"Population: {Number(portfolio.FidelityPrimaryMethods)} fidelity-primary methods, "
            + $"{Number(portfolio.FidelityCauseSites)} cause sites.");
        Console.WriteLine(
            "Method rollup: "
            + $"{Number(portfolio.RecoverableMethods)} all causes recoverable; "
            + $"{Number(portfolio.PolicyFloorMethods)} any cause at the policy floor; "
            + $"{Number(portfolio.UnclassifiedMethods)} otherwise unclassified.");
        if (portfolio.MissingCauseMethods > 0)
        {
            Console.WriteLine(
                $"Missing cause snapshots: {Number(portfolio.MissingCauseMethods)} "
                + "(counted as unclassified).");
        }
        Console.WriteLine(
            "Current roadmap target range: "
            + $"{Number(portfolio.RoadmapTargetLowerMethods)}–{Number(portfolio.RoadmapTargetUpperMethods)}"
            + $"/{Number(portfolio.TotalMethods)} projected fully raised "
            + $"({FormatBps(RateBasisPoints(portfolio.RoadmapTargetLowerMethods, portfolio.TotalMethods))}"
            + $"–{FormatBps(RateBasisPoints(portfolio.RoadmapTargetUpperMethods, portfolio.TotalMethods))}).");
        Console.WriteLine(
            "The range excludes the current policy floor; its upper endpoint includes "
            + "unclassified work, so it is a policy target, not a mathematical ceiling.");
        Console.WriteLine(
            $"Earlier structural-primary co-occurrence (excluded): "
            + $"{Number(portfolio.StructuralPrimaryMethodsWithFidelityCauses)} methods, "
            + $"{Number(portfolio.StructuralPrimaryFidelityCauseSites)} fidelity cause sites.");

        PrintFidelityFacetSection(
            "Recoverable roadmap",
            portfolio.Facets,
            FidelityResidualDisposition.RecoverableRoadmap);
        PrintFidelityFacetSection(
            "Policy floor",
            portfolio.Facets,
            FidelityResidualDisposition.PolicyFloor);
        PrintFidelityFacetSection(
            "Unclassified",
            portfolio.Facets,
            FidelityResidualDisposition.Unclassified);
    }

    static void PrintFidelityFacetSection(
        string heading,
        ImmutableArray<FidelityResidualFacetSummary> facets,
        FidelityResidualDisposition disposition)
    {
        var selected = facets
            .Where(facet => facet.Disposition == disposition)
            .ToArray();
        Console.WriteLine();
        Console.WriteLine($"{heading} facets:");
        if (selected.Length == 0)
        {
            Console.WriteLine("  none");
            return;
        }

        foreach (var facet in selected)
        {
            string label = facet.Discriminator is null
                ? facet.Code
                : $"{facet.Code}/{facet.Discriminator}";
            string example = facet.Examples.Length == 0
                ? ""
                : $"; e.g. {facet.Examples[0]}";
            Console.WriteLine(
                $"  {label}: {Number(facet.Methods)} methods, "
                + $"{Number(facet.CauseSites)} sites{example}");
        }
    }

    static void PrintReturnToSenderParity(FidelitySensorMetrics metrics, string indent)
    {
        if (metrics.ReturnToSenderParity is not { } parity)
            return;

        Console.WriteLine(
            $"{indent}RTS parity: {parity.RescuedMethods} rescued, {parity.SameMethods} same, "
            + $"{parity.WorseMethods} worse over {parity.ComparedMethods} compile-back targets");
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
        bool risky,
        string? baselineRef)
    {
        Console.WriteLine(QualityCardHeadingForProfile(current.Profile));
        Console.WriteLine();
        Console.WriteLine($"Corpus: {current.Description} {AssemblyCount(current.Assemblies.Count)}, {Number(current.Metrics.TotalMethods)} methods");
        if (baselineRef is not null)
            Console.WriteLine($"Baseline ref: `{baselineRef}`");
        if (current.MethodCap is { } cap)
            Console.WriteLine($"Sample: hash-stable {Number(cap)} methods per assembly");
        Console.WriteLine($"Correctness coverage: {CoverageSummary(current)}");
        if (risky)
            PrintRiskyCoverageGuidance(current);
        PrintBaselineStaleness(baseline, current);
        Console.WriteLine();
        PrintQualityMetricChanges(baseline, current);
        PrintFeatureCoverage(current);
        if (current.Profile == CorpusProfile.RealWorld)
            PrintPinnedGate(baseline, current);
        if (!risky && current.Profile == CorpusProfile.RealWorld)
            PrintAdvisoryRateMovements(baseline, current);
        Console.WriteLine();
        Console.WriteLine($"Current measured debt: {CurrentMeasuredDebt(current)}");
        Console.WriteLine(RegressionVerdict(regressions.Count));
        if (regressions.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Regressions:");
            foreach (var regression in regressions)
                Console.WriteLine($"- {regression}");
            if (IsBaselineStale(baseline, current) && current.Profile == CorpusProfile.RealWorld)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Caveat: the corpus drifted from the baseline (see baseline staleness above). "
                    + "The aggregate rows above mix the PR with that drift, but rate/count "
                    + "regressions are gated on the pinned-NuGet subset where available (a fixed method set), so any "
                    + "`(pinned)` regression listed here is a real decompiler delta, not drift.");
            }

            else if (IsBaselineStale(baseline, current))
            {
                Console.WriteLine();
                Console.WriteLine(
                    "Caveat: the opt-in corpus population differs from its baseline. "
                    + "Advance fixture sources or the pinned SDK and regenerate the baseline "
                    + "together; otherwise treat this as unexpected corpus drift.");
            }

        }
    }

    static string CurrentMeasuredDebt(CorpusSensorSnapshot snapshot)
    {
        var debt = new List<string>();
        int withLoweringResidue =
            Math.Max(0, snapshot.Metrics.TotalMethods - snapshot.Metrics.FullyRaisedMethods);
        if (withLoweringResidue > 0)
        {
            debt.Add(
                Counted(
                    withLoweringResidue,
                    "method with detected lowering residue",
                    "methods with detected lowering residue"));
        }

        if (snapshot.ValidityCompileCap > 0)
        {
            if (snapshot.Metrics.FullMalformedMethods > 0)
                debt.Add(Counted(snapshot.Metrics.FullMalformedMethods, "malformed Full method", "malformed Full methods"));
            if (snapshot.Metrics.SemanticDefectMethods > 0)
            {
                debt.Add(
                    $"{Counted(snapshot.Metrics.SemanticDefectMethods, "semantic defect", "semantic defects")} "
                    + $"among {Number(snapshot.Metrics.SemanticCheckedMethods)} checked");
            }
        }

        if (snapshot.FidelityCompileCap > 0)
        {
            var fidelity = snapshot.Metrics.Fidelity;
            if (fidelity.OpcodeDiffMethods > 0)
            {
                debt.Add(
                    $"{Counted(fidelity.OpcodeDiffMethods, "fidelity opcode diff", "fidelity opcode diffs")} "
                    + $"among {Number(fidelity.CheckedMethods)} checked");
            }
            if (fidelity.OperandDiffMethods > 0)
            {
                debt.Add(
                    $"{Counted(fidelity.OperandDiffMethods, "fidelity operand diff", "fidelity operand diffs")} "
                    + $"among {Number(fidelity.CheckedMethods)} checked");
            }
            if (fidelity.FidelityUnavailableMethods > 0)
            {
                debt.Add(
                    $"{Counted(fidelity.FidelityUnavailableMethods, "unavailable fidelity comparison", "unavailable fidelity comparisons")} "
                    + $"among {Number(fidelity.CheckedMethods)} checked");
            }
            if (fidelity.RecompileFailMethods > 0)
            {
                debt.Add(
                    $"{Counted(fidelity.RecompileFailMethods, "fidelity recompile failure", "fidelity recompile failures")} "
                    + $"among {Number(fidelity.CheckedMethods)} checked");
            }
            if (fidelity.ContextFailMethods > 0)
            {
                debt.Add(
                    $"{Counted(fidelity.ContextFailMethods, "fidelity context failure", "fidelity context failures")} "
                    + $"among {Number(fidelity.CheckedMethods)} checked");
            }
        }

        if (snapshot.Metrics.PassBugs > 0)
            debt.Add(Counted(snapshot.Metrics.PassBugs, "pass bug", "pass bugs"));

        return debt.Count == 0
            ? "none in enabled checks."
            : string.Join("; ", debt) + ".";
    }

    static string RegressionVerdict(int regressionCount)
        => regressionCount == 0
            ? "Regression verdict: PASS — corpus sensor matched baseline tolerances."
            : "Regression verdict: FAIL — corpus sensor reported regressions; review before merging.";

    internal static string CurrentMeasuredDebtForTesting(CorpusSensorSnapshot snapshot)
        => CurrentMeasuredDebt(snapshot);

    internal static string RegressionVerdictForTesting(int regressionCount)
        => RegressionVerdict(regressionCount);

    static void PrintFeatureCoverage(CorpusSensorSnapshot snapshot)
    {
        if (snapshot.FeatureCoverage is { Count: > 0 } coverage)
        {
            Console.WriteLine();
            Console.WriteLine("Feature evidence:");
            foreach (var (feature, count) in coverage.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                Console.WriteLine($"- `{feature}`: {count}");
        }

        if (snapshot.ClassicStateMachineCoverage is { Count: > 0 } stateMachines)
        {
            Console.WriteLine();
            Console.WriteLine("Classic state-machine kickoff evidence:");
            foreach (var (feature, metrics) in stateMachines.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                Console.WriteLine(
                    $"- `{feature}`: population {metrics.Population}, "
                    + $"fully raised {metrics.FullyRaised}, residual {metrics.Residual}");
            }
        }
    }

    internal static string QualityCardHeadingForProfile(CorpusProfile profile)
        => profile switch
        {
            CorpusProfile.RealWorld => "### Decompiler quality diff",
            CorpusProfile.OptInNet11 => "### Decompiler net11 opt-in feature diff",
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };

    /// <summary>
    /// Shows the pinned-NuGet-subset metrics that drive the PR quick verdict, so
    /// reviewers can see the stable gate alongside the drifting aggregate rows.
    /// Silent when per-method detail is unavailable (the verdict then falls back
    /// to aggregate counts/rates).
    /// </summary>
    static void PrintPinnedGate(CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
    {
        string? summary = PinnedGateSummary(baseline, current);
        if (summary is null)
            return;

        Console.WriteLine();
        Console.WriteLine(summary);
    }

    internal static string? PinnedGateSummaryForTesting(
        CorpusSensorSnapshot baseline,
        CorpusSensorSnapshot current)
        => PinnedGateSummary(baseline, current);

    static string? PinnedGateSummary(CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
    {
        if (PinnedCounts(baseline) is not { } basePinned || PinnedCounts(current) is not { } curPinned)
            return null;

        bool comparableMalformedSamples = HaveSameMethodSample(
            baseline.Methods,
            current.Methods,
            static method => IsPinnedAssembly(method.AssemblyPath)
                && method.Validity != "not-sampled");
        bool comparableSemanticSamples = HaveSameMethodSample(
            baseline.Methods,
            current.Methods,
            static method => IsPinnedAssembly(method.AssemblyPath)
                && (method.Validity == "valid"
                    || method.Validity.StartsWith("semantic-defect:", StringComparison.Ordinal)));
        bool comparableFidelitySamples = baseline.FidelityOracle == current.FidelityOracle
            && basePinned.FidelityChecked > 0 && curPinned.FidelityChecked > 0
            && current.FidelityCompileCap == baseline.FidelityCompileCap
            && HaveSameMethodSample(
                baseline.Methods,
                current.Methods,
                static method => IsPinnedAssembly(method.AssemblyPath)
                    && method.FidelityCheck != "not-sampled");
        var fidelity = comparableFidelitySamples
            ? $"opcode diffs {Number(basePinned.OpcodeDiff)} -> {Number(curPinned.OpcodeDiff)} "
                + $"({Delta(curPinned.OpcodeDiff - basePinned.OpcodeDiff)})"
            : baseline.FidelityOracle != current.FidelityOracle
                ? "fidelity ungated (oracle differs)"
                : "fidelity ungated (sampling differs; rely on changed-method fidelity)";
        var fullMalformed = current.ValidityCompileCap > 0
            ? comparableMalformedSamples
                ? $"Full malformed {Number(basePinned.FullMalformed)} -> {Number(curPinned.FullMalformed)} "
                    + $"({Delta(curPinned.FullMalformed - basePinned.FullMalformed)}); "
                : "Full malformed ungated (sampling differs); "
            : "";
        var semanticDefects = current.ValidityCompileCap > 0
            ? comparableSemanticSamples
                ? $"semantic defects {Number(basePinned.SemanticDefect)} -> {Number(curPinned.SemanticDefect)} "
                    + $"({Delta(curPinned.SemanticDefect - basePinned.SemanticDefect)}); "
                : "semantic defects ungated (sampling differs); "
            : "";
        return "Pinned-subset gate (PR quick rate/count regressions evaluated here): "
            + $"detected lowering residue {FormatBps(10_000 - basePinned.FullyRaisedBasisPoints)} -> {FormatBps(10_000 - curPinned.FullyRaisedBasisPoints)} "
            + $"({DeltaPercentagePoints(basePinned.FullyRaisedBasisPoints - curPinned.FullyRaisedBasisPoints)}); "
            + $"conditional-branch residue {FormatBps(basePinned.ConditionalBranchBasisPoints)} -> {FormatBps(curPinned.ConditionalBranchBasisPoints)} "
            + $"({DeltaPercentagePoints(curPinned.ConditionalBranchBasisPoints - basePinned.ConditionalBranchBasisPoints)}); "
            + fullMalformed
            + semanticDefects
            + fidelity + ".";
    }

    static void PrintAdvisoryRateMovements(CorpusSensorSnapshot baseline, CorpusSensorSnapshot current)
    {
        var tolerance = baseline.Tolerances ?? CorpusSensorTolerances.Default;
        var advisories = new List<string>();
        AddAdvisory(
            "detected lowering residue",
            RateBasisPoints(baseline.Metrics.TotalMethods - baseline.Metrics.FullyRaisedMethods, baseline.Metrics.TotalMethods),
            RateBasisPoints(current.Metrics.TotalMethods - current.Metrics.FullyRaisedMethods, current.Metrics.TotalMethods),
            tolerance.FullyRaisedDropBasisPoints,
            lowerIsRegression: false);
        AddAdvisory("conditional-branch residue", baseline.Metrics.ConditionalBranchBasisPoints, current.Metrics.ConditionalBranchBasisPoints, tolerance.ConditionalBranchIncreaseBasisPoints, lowerIsRegression: false);
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
    /// happens the aggregate count deltas (lowering residue, Full malformed, fidelity
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

    static void PrintQualityMetricChanges(
        CorpusSensorSnapshot baseline,
        CorpusSensorSnapshot current,
        TextWriter? output = null)
    {
        bool comparableStructuralPopulation = HaveSameMethodSample(
            baseline.Methods,
            current.Methods,
            static _ => true);
        Goal structuralLower = comparableStructuralPopulation ? Goal.Lower : Goal.Context;
        string structuralPopulationSuffix = comparableStructuralPopulation
            ? ""
            : " (population differs)";
        var rows = new List<MultiSourceRow>();
        MultiSourceRow? fullyRaisedRow = null;
        MultiSourceRow? fidelityCoverageRow = null;
        MultiSourceRow? fidelityExactRow = null;

        if (current.ValidityCompileCap > 0)
        {
            var baselineFullyRaised = VerifiedFullyRaised(baseline);
            var currentFullyRaised = VerifiedFullyRaised(current);
            if (baselineFullyRaised is not null && currentFullyRaised is not null)
            {
                bool comparableFullyRaisedSamples = HaveSameMethodSample(
                    baseline.Methods,
                    current.Methods,
                    static method => HasValidityOutcome(method.Validity));
                fullyRaisedRow = ShareChangeRow(
                    comparableFullyRaisedSamples
                        ? "Fully raised"
                        : "Fully raised (sampling differs)",
                    baselineFullyRaised.Value.RaisedMethods,
                    baselineFullyRaised.Value.CheckedMethods,
                    currentFullyRaised.Value.RaisedMethods,
                    currentFullyRaised.Value.CheckedMethods,
                    comparableFullyRaisedSamples ? Goal.Higher : Goal.Context,
                    countDeltaKnown: comparableFullyRaisedSamples);
            }
        }

        rows.AddRange(
        [
            ShareChangeRow(
                "Detected lowering residue" + structuralPopulationSuffix,
                baseline.Metrics.TotalMethods - baseline.Metrics.FullyRaisedMethods,
                baseline.Metrics.TotalMethods,
                current.Metrics.TotalMethods - current.Metrics.FullyRaisedMethods,
                current.Metrics.TotalMethods,
                structuralLower,
                countDeltaKnown: comparableStructuralPopulation),
            ShareChangeRow(
                "Conditional-branch residue" + structuralPopulationSuffix,
                baseline.Metrics.ConditionalBranchMethods,
                baseline.Metrics.TotalMethods,
                current.Metrics.ConditionalBranchMethods,
                current.Metrics.TotalMethods,
                structuralLower,
                countDeltaKnown: comparableStructuralPopulation),
            ShareChangeRow(
                "Forward-merge stops" + structuralPopulationSuffix,
                baseline.Metrics.ForwardMergeStoppedContainers,
                baseline.Metrics.TotalMethods,
                current.Metrics.ForwardMergeStoppedContainers,
                current.Metrics.TotalMethods,
                structuralLower,
                countDeltaKnown: comparableStructuralPopulation),
        ]);

        if (current.ValidityCompileCap > 0)
        {
            bool comparableMalformedSamples = HaveSameMethodSample(
                baseline.Methods,
                current.Methods,
                static method => method.Validity != "not-sampled");
            bool comparableSemanticSamples = HaveSameMethodSample(
                baseline.Methods,
                current.Methods,
                static method => method.Validity == "valid"
                    || method.Validity.StartsWith("semantic-defect:", StringComparison.Ordinal));
            rows.Add(CountChangeRow(
                comparableMalformedSamples
                    ? "Full malformed"
                    : "Full malformed (sampling differs)",
                baseline.Metrics.FullMalformedMethods,
                current.Metrics.FullMalformedMethods,
                comparableMalformedSamples ? Goal.Lower : Goal.Context,
                countDeltaKnown: comparableMalformedSamples));
            rows.Add(ShareChangeRow(
                comparableSemanticSamples
                    ? "Semantic defects"
                    : "Semantic defects (sampling differs)",
                baseline.Metrics.SemanticDefectMethods,
                baseline.Metrics.SemanticCheckedMethods,
                current.Metrics.SemanticDefectMethods,
                current.Metrics.SemanticCheckedMethods,
                comparableSemanticSamples ? Goal.Lower : Goal.Context,
                countDeltaKnown: comparableSemanticSamples));
        }

        if (current.FidelityCompileCap > 0)
        {
            bool sameFidelityOracle = baseline.FidelityOracle == current.FidelityOracle;
            bool sameFidelityContract =
                baseline.Metrics.Fidelity.ContractVersion == current.Metrics.Fidelity.ContractVersion;
            bool comparableFidelitySamples = sameFidelityOracle && sameFidelityContract && HaveSameMethodSample(
                baseline.Methods,
                current.Methods,
                static method => method.FidelityCheck != "not-sampled");
            string fidelityDifference = !sameFidelityOracle
                ? "oracle differs"
                : !sameFidelityContract
                    ? "contract differs"
                    : "sampling differs";
            rows.Add(ShareChangeRow(
                comparableFidelitySamples
                    ? "Fidelity opcode diffs"
                    : $"Fidelity opcode diffs ({fidelityDifference})",
                baseline.Metrics.Fidelity.OpcodeDiffMethods,
                baseline.Metrics.Fidelity.CheckedMethods,
                current.Metrics.Fidelity.OpcodeDiffMethods,
                current.Metrics.Fidelity.CheckedMethods,
                comparableFidelitySamples ? Goal.Lower : Goal.Context,
                countDeltaKnown: comparableFidelitySamples));
            fidelityExactRow = ShareChangeRow(
                comparableFidelitySamples
                    ? $"Fidelity exact (contract v{current.Metrics.Fidelity.ContractVersion}; EH-blind)"
                    : $"Fidelity exact ({fidelityDifference})",
                baseline.Metrics.Fidelity.ExactMethods,
                baseline.Metrics.Fidelity.CheckedMethods,
                current.Metrics.Fidelity.ExactMethods,
                current.Metrics.Fidelity.CheckedMethods,
                comparableFidelitySamples ? Goal.Higher : Goal.Context,
                countDeltaKnown: comparableFidelitySamples);
            rows.Add(ShareChangeRow(
                comparableFidelitySamples
                    ? "Fidelity operand diffs"
                    : $"Fidelity operand diffs ({fidelityDifference})",
                baseline.Metrics.Fidelity.OperandDiffMethods,
                baseline.Metrics.Fidelity.CheckedMethods,
                current.Metrics.Fidelity.OperandDiffMethods,
                current.Metrics.Fidelity.CheckedMethods,
                comparableFidelitySamples ? Goal.Lower : Goal.Context,
                countDeltaKnown: comparableFidelitySamples));
            rows.Add(ShareChangeRow(
                comparableFidelitySamples
                    ? "Fidelity unavailable comparisons"
                    : $"Fidelity unavailable comparisons ({fidelityDifference})",
                baseline.Metrics.Fidelity.FidelityUnavailableMethods,
                baseline.Metrics.Fidelity.CheckedMethods,
                current.Metrics.Fidelity.FidelityUnavailableMethods,
                current.Metrics.Fidelity.CheckedMethods,
                comparableFidelitySamples ? Goal.Lower : Goal.Context,
                countDeltaKnown: comparableFidelitySamples));
            rows.Add(ShareChangeRow(
                comparableFidelitySamples
                    ? "Fidelity recompile failures"
                    : $"Fidelity recompile failures ({fidelityDifference})",
                baseline.Metrics.Fidelity.RecompileFailMethods,
                baseline.Metrics.Fidelity.CheckedMethods,
                current.Metrics.Fidelity.RecompileFailMethods,
                current.Metrics.Fidelity.CheckedMethods,
                comparableFidelitySamples ? Goal.Lower : Goal.Context,
                countDeltaKnown: comparableFidelitySamples));
            rows.Add(ShareChangeRow(
                comparableFidelitySamples
                    ? "Fidelity context failures"
                    : $"Fidelity context failures ({fidelityDifference})",
                baseline.Metrics.Fidelity.ContextFailMethods,
                baseline.Metrics.Fidelity.CheckedMethods,
                current.Metrics.Fidelity.ContextFailMethods,
                current.Metrics.Fidelity.CheckedMethods,
                comparableFidelitySamples ? Goal.Lower : Goal.Context,
                countDeltaKnown: comparableFidelitySamples));
            if (current.Metrics.Fidelity.ReturnToSenderParity is { } currentParity)
            {
                var baselineParity = baseline.Metrics.Fidelity.ReturnToSenderParity;
                bool comparableParity = comparableFidelitySamples && baselineParity is not null;
                string parityDifference = baselineParity is null && comparableFidelitySamples
                    ? "baseline unavailable"
                    : fidelityDifference;
                rows.Add(CountChangeRow(
                    comparableParity
                        ? "RTS parity worse"
                        : $"RTS parity worse ({parityDifference})",
                    baselineParity?.WorseMethods ?? 0,
                    currentParity.WorseMethods,
                    comparableParity ? Goal.Lower : Goal.Context,
                    countDeltaKnown: comparableParity));
            }
            fidelityCoverageRow = ShareChangeRow(
                "Fidelity check coverage",
                baseline.Metrics.Fidelity.CheckedMethods,
                baseline.Metrics.TotalMethods,
                current.Metrics.Fidelity.CheckedMethods,
                current.Metrics.TotalMethods,
                Goal.Higher);
        }

        rows.Add(CountChangeRow("Pass bugs", baseline.Metrics.PassBugs, current.Metrics.PassBugs, Goal.Lower));
        if (fidelityCoverageRow is { } coverageRow)
            rows.Add(coverageRow);
        if (fidelityExactRow is { } exactRow)
            rows.Add(exactRow);
        if (fullyRaisedRow is { } raisedRow)
            rows.Add(raisedRow);

        var writer = new MarkoutWriter(output ?? Console.Out, new MarkdownFormatter());
        writer.WriteMultiSourceTable("Metric", rows);
    }

    static VerifiedFullyRaisedMetrics? VerifiedFullyRaised(
        CorpusSensorSnapshot snapshot)
    {
        if (snapshot.ValidityCompileCap <= 0 || snapshot.Methods is not { } methods)
            return null;

        int checkedMethods = 0;
        int raisedMethods = 0;
        foreach (var method in methods)
        {
            if (!HasValidityOutcome(method.Validity))
                continue;

            checkedMethods++;
            if (method.FullyRaised && method.Validity == "valid")
                raisedMethods++;
        }

        return checkedMethods == 0
            ? null
            : new VerifiedFullyRaisedMetrics(raisedMethods, checkedMethods);
    }

    static bool HasValidityOutcome(string validity)
        => validity == "valid"
            || validity.StartsWith("semantic-defect:", StringComparison.Ordinal)
            || validity.StartsWith("full-malformed:", StringComparison.Ordinal)
            || validity.StartsWith("partial-malformed:", StringComparison.Ordinal);

    internal static (int RaisedMethods, int CheckedMethods)?
        VerifiedFullyRaisedForTesting(CorpusSensorSnapshot snapshot)
        => VerifiedFullyRaised(snapshot) is { } metrics
            ? (metrics.RaisedMethods, metrics.CheckedMethods)
            : null;

    internal static string QualityMetricChangesForTesting(
        CorpusSensorSnapshot baseline,
        CorpusSensorSnapshot current)
    {
        using var writer = new StringWriter();
        PrintQualityMetricChanges(baseline, current, writer);
        return writer.ToString();
    }

    static MultiSourceRow CountChangeRow(
        string metric,
        int baseline,
        int current,
        Goal goal,
        bool countDeltaKnown = true)
        => new(
            MetricLabel(metric, goal),
            new Source(
                "Change",
                new Change<int>(baseline, current),
                new MarkoutCellFormat { Goal = goal, Delta = countDeltaKnown ? Markout.Delta.Absolute : Markout.Delta.None, NumberFormat = "N0" }));

    static MultiSourceRow ShareChangeRow(
        string metric,
        int baseline,
        int baselineTotal,
        int current,
        int currentTotal,
        Goal goal,
        bool countDeltaKnown = true)
        => new(
            MetricLabel(metric, goal),
            new Source(
                "Change",
                new Change<QualityRate>(new QualityRate(baseline, baselineTotal), new QualityRate(current, currentTotal)),
                new MarkoutCellFormat { Goal = goal, DeltaNoun = countDeltaKnown ? "methods" : null, NumberFormat = "N0" }));

    static bool HaveSameMethodSample(
        IReadOnlyList<CorpusMethodSnapshot>? baselineMethods,
        IReadOnlyList<CorpusMethodSnapshot>? currentMethods,
        Func<CorpusMethodSnapshot, bool> isChecked)
    {
        if (baselineMethods is null || currentMethods is null)
            return false;

        return baselineMethods
            .Where(isChecked)
            .Select(MethodKey)
            .ToHashSet(StringComparer.Ordinal)
            .SetEquals(currentMethods.Where(isChecked).Select(MethodKey));
    }

    static string MetricLabel(string metric, Goal goal)
        => goal switch
        {
            Goal.Higher => metric + " (+)",
            Goal.Lower => metric + " (-)",
            _ => metric,
        };

    static string CoverageSummary(CorpusSensorSnapshot snapshot)
    {
        var validity = snapshot.ValidityCompileCap <= 0
            ? "validity not run"
            : $"validity compiled {Number(snapshot.Metrics.SemanticCheckedMethods)} methods"
                + ValidityCompileCapNote(snapshot.ValidityCompileCap);
        var fidelity = snapshot.FidelityCompileCap <= 0
            ? "fidelity not run"
            : $"fidelity ({FidelityOracleName(snapshot.FidelityOracle)}) sampled "
                + Coverage(snapshot.Metrics.Fidelity.CheckedMethods, snapshot.Metrics.TotalMethods);
        if (snapshot.Metrics.Fidelity.ReturnToSenderParity is { } parity)
        {
            fidelity += $", RTS parity {Number(parity.WorseMethods)} worse / "
                + $"{Number(parity.ComparedMethods)} compared";
        }
        return $"{validity}; {fidelity}";
    }

    static string FidelityOracleName(CorpusFidelityOracle oracle)
        => oracle switch
        {
            CorpusFidelityOracle.CompileBack => "compile-back",
            CorpusFidelityOracle.ReturnToSender => "rts-parity",
            _ => throw new ArgumentOutOfRangeException(nameof(oracle)),
        };

    static string CorpusProfileName(CorpusProfile profile)
        => profile switch
        {
            CorpusProfile.RealWorld => "real-world",
            CorpusProfile.OptInNet11 => "opt-in-net11",
            _ => profile.ToString(),
        };

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

    static string Coverage(int checkedMethods, int totalMethods)
        => $"{Number(checkedMethods)} / {Number(totalMethods)} ({FormatBps(RateBasisPoints(checkedMethods, totalMethods))})";

    static string AssemblyCount(int count)
        => $"{Number(count)} assembl{(count == 1 ? "y" : "ies")}";

    static string Counted(int count, string singular, string plural)
        => $"{Number(count)} {(count == 1 ? singular : plural)}";

    static string CapText(int? cap)
        => cap is { } value ? Number(value) : "uncapped";

    static string CompileCapText(int cap)
        => cap == int.MaxValue ? "all" : Number(cap);

    static string ValidityCompileCapNote(int cap)
        => cap == int.MaxValue
            ? " (compile-cap all; exhaustive binding sweep)"
            : $" (compile-cap {CompileCapText(cap)}; per-sample, not corpus-wide)";

    static string Delta(int value)
        => value > 0 ? $"+{Number(value)}" : Number(value);

    readonly record struct QualityRate(int Count, int Total) : IMarkoutCell, IGoalMagnitude, IDeltaCountable
    {
        double IGoalMagnitude.GoalMagnitude => Total <= 0 ? double.NaN : RateBasisPoints(Count, Total) / 10_000.0;

        double IDeltaCountable.DeltaCount => Count;

        public void FormatInline(TextWriter writer, in MarkoutCellFormat format)
            => writer.Write(Total <= 0
                ? $"{Number(Count)} (—)"
                : $"{Number(Count)} ({FormatBps(RateBasisPoints(Count, Total))})");

        public void Decompose(ICollection<MarkoutField> fields, string? side, in MarkoutCellFormat format)
        {
            fields.Add(new MarkoutField(SideKey(side, "count"), Count.ToString(CultureInfo.InvariantCulture)));
            fields.Add(new MarkoutField(SideKey(side, "total"), Total.ToString(CultureInfo.InvariantCulture)));
            if (Total > 0)
                fields.Add(new MarkoutField(SideKey(side, "basisPoints"), RateBasisPoints(Count, Total).ToString(CultureInfo.InvariantCulture)));
        }

        static string SideKey(string? side, string key)
            => side is null ? key : side + "_" + key;
    }

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
    CorpusSensorMetrics Metrics,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CorpusFidelityOracle>))]
    CorpusFidelityOracle FidelityOracle = CorpusFidelityOracle.CompileBack,
    [property: JsonConverter(typeof(JsonStringEnumConverter<CorpusProfile>))]
    CorpusProfile Profile = CorpusProfile.RealWorld,
    IReadOnlyDictionary<string, int>? FeatureCoverage = null,
    IReadOnlyDictionary<string, ClassicStateMachineFeatureMetrics>? ClassicStateMachineCoverage = null);

internal sealed record ClassicStateMachineFeatureMetrics(
    int Population = 0,
    int FullyRaised = 0,
    int Residual = 0);

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
    int OperandDiff,
    int FidelityUnavailable,
    int RecompileFail,
    int ContextFail,
    int FidelityChecked);

internal sealed record CompletenessSensorMetrics(
    IReadOnlyList<CorpusAssemblySnapshot> Assemblies,
    int FullyRaisedMethods,
    int PassBugs,
    IReadOnlyDictionary<string, int> ResidualBuckets,
    IReadOnlyList<CorpusMethodSnapshot> Methods,
    IReadOnlyDictionary<string, int> FeatureCoverage,
    IReadOnlyDictionary<string, ClassicStateMachineFeatureMetrics>? ClassicStateMachineCoverage);

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
    int ContractVersion,
    int CheckedMethods,
    int ExactMethods,
    int OpcodeDiffMethods,
    int OperandDiffMethods,
    int FidelityUnavailableMethods,
    int RecompileFailMethods,
    int ContextFailMethods,
    int NotFullMethods,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ReturnToSenderParityMetrics? ReturnToSenderParity = null)
{
    public static FidelitySensorMetrics Empty { get; } = new(
        CorpusSensor.CurrentFidelityContractVersion,
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        0);
}

readonly record struct VerifiedFullyRaisedMetrics(
    int RaisedMethods,
    int CheckedMethods);

internal sealed record ReturnToSenderParityMetrics(
    int RescuedMethods,
    int SameMethods,
    int WorseMethods)
{
    public int ComparedMethods => RescuedMethods + SameMethods + WorseMethods;
}

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
    int FidelityOperandDiffIncrease,
    int FidelityUnavailableIncrease,
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
        FidelityOperandDiffIncrease: 0,
        FidelityUnavailableIncrease: 0,
        FidelityRecompileFailIncrease: 0,
        FidelityContextFailIncrease: 0);
}
