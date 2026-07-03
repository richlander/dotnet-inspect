using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Decompiler.Pipeline.InverseArchitecture;
using ILInspector.Decompiler.Tests.InverseArchitecture;

namespace ILInspector.DecompilerHarness;

internal static class AssertionScan
{
    public sealed record Options(
        int? SampleSize,
        int MaxExamples,
        string? EmitViolationsPath,
        string? DiffViolationsPath,
        int? Workers,
        bool Sequential);

    public sealed record ViolationSite(
        string Method,
        string Pass,
        string Predicate,
        string Node,
        string SinkType,
        string Message,
        int Ordinal)
    {
        public string Identity => $"{Pass}|{Predicate}|{Node}|{SinkType}|{Message}#{Ordinal}";
    }

    public sealed record MethodResult(
        string Assembly,
        string AssemblyPath,
        string Type,
        string Method,
        int Overload,
        string Signature,
        string Key,
        IReadOnlyList<ViolationSite> Violations,
        IReadOnlyCollection<string> CoveredNodes,
        string? PassBug);

    public sealed record Result(
        IReadOnlyList<MethodResult> Methods,
        IReadOnlyDictionary<string, int> CoverageByNode,
        IReadOnlyList<string> AnnotatedNodes);

    sealed record MethodInput(
        string Assembly,
        string AssemblyPath,
        string Type,
        string Method,
        int Overload,
        IrFunction Function);

    public static int Run(IReadOnlyList<string> assemblies, Options options)
    {
        if (options.SampleSize is <= 0)
        {
            Console.Error.WriteLine("--sample must be greater than zero.");
            return 1;
        }

        if (options.DiffViolationsPath is not null && !File.Exists(options.DiffViolationsPath))
        {
            Console.Error.WriteLine($"Assertion-violation baseline not found: {options.DiffViolationsPath}");
            return 1;
        }

        var result = Evaluate(assemblies, options.SampleSize, options.Workers, options.Sequential);
        Report(result, options.MaxExamples, options.SampleSize);

        if (options.EmitViolationsPath is not null)
            EmitViolations(options.EmitViolationsPath, result);
        if (options.DiffViolationsPath is not null)
            DiffViolations(options.DiffViolationsPath, result);

        return result.Methods.Any(m => m.PassBug is not null) ? 1 : 0;
    }

    public static Result Evaluate(
        IReadOnlyList<string> assemblies,
        int? sampleSize = null,
        int? workers = null,
        bool sequential = false)
    {
        var methods = new ConcurrentBag<MethodResult>();
        var coverage = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var annotatedNodes = InverseLedger.Rows(typeof(IrFunction).Assembly)
            .Select(row => row.Node)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = sequential ? 1 : (workers ?? Math.Max(1, Environment.ProcessorCount - 2)),
        };

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath, context: metadata);
            _ = source.ResolveShape(TypeRef.CoreLib("System", "Int32"));
            string portablePath = CorpusSensor.PortablePath(assemblyPath);

            if (sampleSize is { } sample)
            {
                var candidates = IrImporter.GetStableSampleCandidates(source, sample).ToList();
                Parallel.ForEach(candidates, parallel, candidate =>
                {
                    var function = candidate.Build(source);
                    var result = EvaluateFunction(
                        source.AssemblyName,
                        portablePath,
                        candidate.TypeName,
                        candidate.MethodName,
                        candidate.Overload,
                        function);
                    methods.Add(result);
                    AddCoverage(coverage, result.CoveredNodes);
                });
                continue;
            }

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            var inputs = new List<MethodInput>();
            foreach (var (typeName, methodName, function) in IrImporter.ImportAssembly(source))
            {
                string overloadKey = $"{typeName}::{methodName}";
                int overload = seen.GetValueOrDefault(overloadKey);
                seen[overloadKey] = overload + 1;
                inputs.Add(new MethodInput(source.AssemblyName, portablePath, typeName, methodName, overload, function));
            }

            Parallel.ForEach(inputs, parallel, input =>
            {
                var result = EvaluateFunction(
                    input.Assembly,
                    input.AssemblyPath,
                    input.Type,
                    input.Method,
                    input.Overload,
                    input.Function);
                methods.Add(result);
                AddCoverage(coverage, result.CoveredNodes);
            });
        }

        return new Result(
            methods.OrderBy(m => m.Key, StringComparer.Ordinal).ToArray(),
            coverage.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal),
            annotatedNodes);
    }

    internal static MethodResult EvaluateFunction(
        string assembly,
        string assemblyPath,
        string type,
        string method,
        int overload,
        IrFunction function)
    {
        string signature = CorpusMethodIdentity.SignatureText(function.Signature);
        string key = $"{assemblyPath}!{type}::{method}{signature}";
        var violations = new List<ViolationSite>();
        var seenViolations = new HashSet<string>(StringComparer.Ordinal);
        var covered = new SortedSet<string>(StringComparer.Ordinal);

        void Capture(string passName)
        {
            AddCoveredNodes(function, covered);
            var stageViolations = new List<(string Identity, ViolationSite Site)>();
            foreach (var predicate in AssertionEvaluator.EvaluateAssumptions(function))
            {
                var perIdentityOrdinal = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var violation in predicate.Violations)
                {
                    string node = violation.Node.GetType().Name;
                    string sinkType = SinkType(violation.Message);
                    string identity = $"{predicate.Name}|{node}|{sinkType}|{violation.Message}";
                    int ordinal = perIdentityOrdinal.GetValueOrDefault(identity);
                    perIdentityOrdinal[identity] = ordinal + 1;
                    string stableIdentity = $"{identity}#{ordinal}";
                    stageViolations.Add((
                        stableIdentity,
                        new ViolationSite(key, passName, predicate.Name, node, sinkType, violation.Message, ordinal)));
                }
            }

            foreach (var (identity, site) in stageViolations)
            {
                if (seenViolations.Add(identity))
                    violations.Add(site);
            }
        }

        try
        {
            Capture(IrPasses.ImportStageName);
            foreach (var pass in IrPasses.Default)
            {
                pass.Run(function, PassContext.None);
                function.CheckInvariant();
                Capture(pass.Name);
            }
        }
        catch (Exception ex)
        {
            return new MethodResult(
                assembly,
                assemblyPath,
                type,
                method,
                overload,
                signature,
                key,
                violations,
                covered,
                $"{ex.GetType().Name}: {ex.Message}");
        }

        return new MethodResult(assembly, assemblyPath, type, method, overload, signature, key, violations, covered, PassBug: null);
    }

    static void AddCoveredNodes(IrFunction function, ISet<string> covered)
    {
        foreach (var node in function.Descendants)
        {
            if (node.GetType().GetCustomAttributes(typeof(InverseOfAttribute), inherit: false).Length > 0)
                covered.Add(node.GetType().Name);
        }
    }

    static void AddCoverage(ConcurrentDictionary<string, int> coverage, IReadOnlyCollection<string> nodes)
    {
        foreach (var node in nodes)
            coverage.AddOrUpdate(node, 1, (_, count) => count + 1);
    }

    static string SinkType(string message)
    {
        var match = Regex.Match(message, @" occupies a (?<sink>.+?) sink without a Coerce$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["sink"].Value : "(unknown)";
    }

    static void Report(Result result, int maxExamples, int? sampleSize)
    {
        int total = result.Methods.Count;
        int passBugs = result.Methods.Count(m => m.PassBug is not null);
        var methodsWithViolations = result.Methods.Where(m => m.Violations.Count > 0).ToArray();
        int violationCount = methodsWithViolations.Sum(m => m.Violations.Count);

        string scope = sampleSize is null ? $"{total} methods" : $"{total} methods (--sample {sampleSize} per assembly)";
        Console.WriteLine($"ASSERTION SCAN over {scope} ({passBugs} pass bugs)");
        Console.WriteLine($"  methods with >=1 violation: {methodsWithViolations.Length} ({Percent(methodsWithViolations.Length, total)})");
        Console.WriteLine($"  first violation sites      : {violationCount}");
        Console.WriteLine();

        PrintHistogram("By sink type:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.SinkType));
        PrintHistogram("By first failing pass:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.Pass));
        PrintHistogram("By node:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.Node));
        PrintHistogram("By predicate:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.Predicate));

        Console.WriteLine();
        Console.WriteLine("Annotation coverage:");
        Console.WriteLine($"  distinct [InverseOf] nodes exercised: {result.CoverageByNode.Count}/{result.AnnotatedNodes.Count}");
        if (result.CoverageByNode.Count > 0)
            Console.WriteLine($"  covered: {string.Join(", ", result.CoverageByNode.Keys.Order(StringComparer.Ordinal))}");
        var missing = result.AnnotatedNodes.Except(result.CoverageByNode.Keys, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            Console.WriteLine($"  not exercised: {string.Join(", ", missing)}");

        if (methodsWithViolations.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var method in methodsWithViolations.Take(maxExamples))
            {
                Console.WriteLine($"  {method.Key}");
                foreach (var violation in method.Violations.Take(maxExamples))
                    Console.WriteLine($"    {violation.Pass}: {violation.Predicate} {violation.Node} -> {violation.SinkType}: {violation.Message}");
            }
        }

        var passBugExamples = result.Methods.Where(m => m.PassBug is not null).Take(maxExamples).ToArray();
        if (passBugExamples.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Pass-bug examples:");
            foreach (var method in passBugExamples)
                Console.WriteLine($"  {method.Key}: {method.PassBug}");
        }
    }

    static void PrintHistogram(string title, IEnumerable<IGrouping<string, ViolationSite>> groups)
    {
        Console.WriteLine(title);
        var ordered = groups
            .Select(g => (Name: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Name, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }
        foreach (var item in ordered)
            Console.WriteLine($"  {item.Count,8}  {item.Name}");
    }

    static void EmitViolations(string path, Result result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var snapshot = AssertionViolationSnapshot.FromResult(result);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions()));
        Console.WriteLine();
        Console.WriteLine($"Wrote assertion-violation map for {snapshot.Methods.Count} methods to {path}");
    }

    static void DiffViolations(string baselinePath, Result result)
    {
        var baseline = JsonSerializer.Deserialize<AssertionViolationSnapshot>(File.ReadAllText(baselinePath), JsonOptions())
            ?? throw new InvalidOperationException($"Could not read assertion-violation baseline '{baselinePath}'.");
        var current = AssertionViolationSnapshot.FromResult(result);

        var baselineByMethod = baseline.Methods.ToDictionary(m => m.Key, StringComparer.Ordinal);
        var currentByMethod = current.Methods.ToDictionary(m => m.Key, StringComparer.Ordinal);
        var shared = currentByMethod.Keys.Intersect(baselineByMethod.Keys, StringComparer.Ordinal).ToArray();
        var comparable = shared
            .Where(method => currentByMethod[method].PassBug is null && baselineByMethod[method].PassBug is null)
            .Order(StringComparer.Ordinal)
            .ToArray();
        int onlyCurrent = currentByMethod.Count - shared.Length;
        int onlyBaseline = baselineByMethod.Count - shared.Length;
        int passBugExcluded = shared.Length - comparable.Length;

        var gained = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var lost = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var method in comparable)
        {
            var now = currentByMethod[method].ViolationIdentities();
            var was = baselineByMethod[method].ViolationIdentities();
            foreach (var violation in now.Except(was, StringComparer.Ordinal))
                (gained.TryGetValue(violation, out var methods) ? methods : gained[violation] = []).Add(method);
            foreach (var violation in was.Except(now, StringComparer.Ordinal))
                (lost.TryGetValue(violation, out var methods) ? methods : lost[violation] = []).Add(method);
        }

        Console.WriteLine();
        Console.WriteLine($"ASSERTION VIOLATION DIFF vs {baselinePath} ({comparable.Length} methods checked in both; {onlyCurrent} only-current, {onlyBaseline} only-baseline, {passBugExcluded} pass-bug excluded)");
        PrintDiffSide("REGRESSED (method gained the violation)", gained);
        PrintDiffSide("IMPROVED (method lost the violation)", lost);
    }

    static void PrintDiffSide(string title, SortedDictionary<string, List<string>> byViolation)
    {
        Console.WriteLine();
        Console.WriteLine(title + ":");
        if (byViolation.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        foreach (var (violation, methods) in byViolation.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {violation}: {methods.Count}");
            foreach (var method in methods.OrderBy(m => m, StringComparer.Ordinal))
                Console.WriteLine($"      {method}");
        }
    }

    static JsonSerializerOptions JsonOptions()
        => new() { WriteIndented = true };

    static string Percent(long part, long whole) => whole == 0 ? "n/a" : $"{100.0 * part / whole:F2}%";

    public sealed record AssertionViolationSnapshot(
        int SchemaVersion,
        DateTimeOffset GeneratedUtc,
        IReadOnlyList<AssertionViolationMethod> Methods)
    {
        public static AssertionViolationSnapshot FromResult(Result result)
            => new(
                SchemaVersion: 1,
                GeneratedUtc: DateTimeOffset.UtcNow,
                Methods: result.Methods
                    .Select(m => new AssertionViolationMethod(
                        m.Assembly,
                        m.AssemblyPath,
                        m.Type,
                        m.Method,
                        m.Overload,
                        m.Signature,
                        m.Key,
                        m.PassBug,
                        m.Violations
                            .Select(v => new AssertionViolationRecord(v.Pass, v.Predicate, v.Node, v.SinkType, v.Message, v.Ordinal))
                            .OrderBy(v => v.Identity, StringComparer.Ordinal)
                            .ToArray()))
                    .OrderBy(m => m.Key, StringComparer.Ordinal)
                    .ToArray());
    }

    public sealed record AssertionViolationMethod(
        string Assembly,
        string AssemblyPath,
        string Type,
        string Method,
        int Overload,
        string Signature,
        string Key,
        string? PassBug,
        IReadOnlyList<AssertionViolationRecord> Violations)
    {
        public SortedSet<string> ViolationIdentities()
            => new(Violations.Select(v => v.Identity), StringComparer.Ordinal);
    }

    public sealed record AssertionViolationRecord(
        string Pass,
        string Predicate,
        string Node,
        string SinkType,
        string Message,
        int Ordinal)
    {
        public string Identity => $"{Pass}|{Predicate}|{Node}|{SinkType}|{Message}#{Ordinal}";
    }
}
