using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.RegularExpressions;

using ILInspector.Decompiler;
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
        bool Sequential,
        bool IncludeFixtureGuarantee);

    public sealed record ViolationSite(
        string Method,
        string Pass,
        string Predicate,
        string Node,
        string SinkType,
        string Message,
        int Ordinal,
        bool FinalStageSurvivor = false)
    {
        public string Identity => $"{Pass}|{Predicate}|{Node}|{SinkType}|{Message}#{Ordinal}";

        /// <summary>
        /// Pass-independent identity — the assertion is the same claim wherever it
        /// is observed. Used to match a first-appearance site against the set of
        /// violations still present at the final stage (the survivor set).
        /// </summary>
        public string StageIdentity => $"{Predicate}|{Node}|{SinkType}|{Message}#{Ordinal}";
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
        IReadOnlyList<string> AnnotatedNodes,
        IReadOnlyDictionary<string, InverseLedger.NodeCause>? CauseByNode = null,
        AssertionFixtureGuaranteeResult? FixtureGuarantee = null);

    public sealed record AssertionFixtureGuaranteeResult(
        string FixtureIds,
        IReadOnlyDictionary<string, int> CoverageByNode,
        IReadOnlyList<AssertionFixtureNodeResult> Nodes)
    {
        public IReadOnlyList<AssertionFixtureNodeResult> MissingFixtures
            => Nodes.Where(n => n.Status == AssertionFixtureNodeStatus.MissingFixture).ToArray();

        public IReadOnlyList<AssertionFixtureNodeResult> Regressions
            => Nodes.Where(n => n.Status == AssertionFixtureNodeStatus.Regression).ToArray();
    }

    public sealed record AssertionFixtureNodeResult(
        string Node,
        InverseLedger.NodeCause Cause,
        IReadOnlyList<string> FixtureIds,
        int ObservedCount)
    {
        public AssertionFixtureNodeStatus Status
            => FixtureIds.Count == 0
                ? AssertionFixtureNodeStatus.MissingFixture
                : ObservedCount == 0
                    ? AssertionFixtureNodeStatus.Regression
                    : AssertionFixtureNodeStatus.Pass;
    }

    public enum AssertionFixtureNodeStatus
    {
        Pass,
        MissingFixture,
        Regression,
    }

    sealed record MethodInput(
        string Assembly,
        string AssemblyPath,
        string Type,
        string Method,
        int Overload,
        IrFunction Function);

    sealed record AssertionFixtureGuarantee(string Node, IReadOnlyList<string> FixtureIds);

    const string AssertionCoverageFixture = "assertion.inverse-node-coverage";
    const string AssertionIlUnboxFixture = "assertion.il-unbox";
    const string AssertionUnionSwitchFixture = "assertion.union-switch";

    static readonly IReadOnlyList<AssertionFixtureGuarantee> AssertionFixtureGuarantees =
    [
        new("AddressOfMethod", [AssertionCoverageFixture]),
        new("ArrayLength", [AssertionCoverageFixture]),
        new("AwaitExpression", [AssertionCoverageFixture]),
        new("Binary", [AssertionCoverageFixture]),
        new("Box", [AssertionCoverageFixture]),
        new("Call", [AssertionCoverageFixture]),
        new("CallIndirect", [AssertionCoverageFixture]),
        new("CastClass", [AssertionCoverageFixture]),
        new("CaughtException", [AssertionCoverageFixture]),
        new("Coalesce", [AssertionCoverageFixture]),
        new("Coerce", [AssertionCoverageFixture]),
        new("Comparison", [AssertionCoverageFixture]),
        new("Conditional", [AssertionCoverageFixture]),
        new("Constant", [AssertionCoverageFixture]),
        new("Convert", [AssertionCoverageFixture]),
        new("DelegateCreation", [AssertionCoverageFixture]),
        new("IncrementDecrement", [AssertionCoverageFixture]),
        new("IndexFromEnd", [AssertionCoverageFixture]),
        new("InlineArraySpanConversion", [AssertionCoverageFixture]),
        new("IsInstance", [AssertionCoverageFixture]),
        new("Lambda", [AssertionCoverageFixture]),
        new("LoadArgument", [AssertionCoverageFixture]),
        new("LoadArgumentAddress", [AssertionCoverageFixture]),
        new("LoadElement", [AssertionCoverageFixture]),
        new("LoadElementAddress", [AssertionCoverageFixture]),
        new("LoadField", [AssertionCoverageFixture]),
        new("LoadFieldAddress", [AssertionCoverageFixture]),
        new("LoadFunctionPointer", [AssertionCoverageFixture]),
        new("LoadIndirect", [AssertionCoverageFixture]),
        new("LoadLocal", [AssertionCoverageFixture]),
        new("LoadLocalAddress", [AssertionCoverageFixture]),
        new("LoadProperty", [AssertionCoverageFixture]),
        new("LoadStackSlot", [AssertionCoverageFixture]),
        new("LoadToken", [AssertionCoverageFixture]),
        new("LocalFunctionInvocation", [AssertionCoverageFixture]),
        new("LogicalBinary", [AssertionCoverageFixture]),
        new("LogicalNot", [AssertionCoverageFixture]),
        new("NewArray", [AssertionCoverageFixture]),
        new("NewObject", [AssertionCoverageFixture]),
        new("NullConditional", [AssertionCoverageFixture]),
        new("RangeExpression", [AssertionCoverageFixture]),
        new("SizeOf", [AssertionCoverageFixture]),
        new("SliceExpression", [AssertionCoverageFixture]),
        new("SpanLiteral", [AssertionCoverageFixture]),
        new("StackAllocArray", [AssertionCoverageFixture]),
        new("StackAllocate", [AssertionCoverageFixture]),
        new("SwitchExpression", [AssertionCoverageFixture]),
        new("TypeOf", [AssertionCoverageFixture]),
        new("Unary", [AssertionCoverageFixture]),
        new("Unbox", [AssertionIlUnboxFixture]),
        new("UnboxAny", [AssertionCoverageFixture]),
        new("UnionSwitchExpression", [AssertionUnionSwitchFixture]),
    ];

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

        var result = Evaluate(
            assemblies,
            options.SampleSize,
            options.Workers,
            options.Sequential,
            options.IncludeFixtureGuarantee);
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
        bool sequential = false,
        bool includeFixtureGuarantee = false)
    {
        var annotatedRows = InverseLedger.Rows(typeof(IrFunction).Assembly)
            .OrderBy(row => row.Node, StringComparer.Ordinal)
            .ToArray();
        var annotatedNodes = annotatedRows.Select(row => row.Node).ToArray();
        var causeByNode = annotatedRows.ToDictionary(row => row.Node, row => row.Cause, StringComparer.Ordinal);
        var result = EvaluateAssemblies(assemblies, sampleSize, workers, sequential, annotatedNodes, causeByNode);
        if (!includeFixtureGuarantee)
            return result;

        var fixtureResult = EvaluateFixtureGuarantee(annotatedNodes, causeByNode, workers, sequential);
        return Merge(result, fixtureResult);
    }

    static Result EvaluateAssemblies(
        IReadOnlyList<string> assemblies,
        int? sampleSize,
        int? workers,
        bool sequential,
        IReadOnlyList<string> annotatedNodes,
        IReadOnlyDictionary<string, InverseLedger.NodeCause> causeByNode)
    {
        var methods = new ConcurrentBag<MethodResult>();
        var coverage = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
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
                        function,
                        method => IrImporter.Import(source, method));
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
                    input.Function,
                    method => IrImporter.Import(source, method));
                methods.Add(result);
                AddCoverage(coverage, result.CoveredNodes);
            });
        }

        return new Result(
            methods.OrderBy(m => m.Key, StringComparer.Ordinal).ToArray(),
            coverage.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal),
            annotatedNodes,
            causeByNode);
    }

    static Result Merge(Result corpus, Result fixtures)
    {
        var coverage = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (node, count) in corpus.CoverageByNode)
            coverage[node] = count;
        foreach (var (node, count) in fixtures.CoverageByNode)
            coverage[node] = coverage.GetValueOrDefault(node) + count;

        return corpus with
        {
            Methods = corpus.Methods.Concat(fixtures.Methods).OrderBy(m => m.Key, StringComparer.Ordinal).ToArray(),
            CoverageByNode = coverage,
            FixtureGuarantee = fixtures.FixtureGuarantee,
        };
    }

    static Result EvaluateFixtureGuarantee(
        IReadOnlyList<string> annotatedNodes,
        IReadOnlyDictionary<string, InverseLedger.NodeCause> causeByNode,
        int? workers,
        bool sequential)
    {
        var fixtureIds = AssertionFixtureGuarantees
            .SelectMany(pair => pair.FixtureIds)
            .Where(id => id != AssertionIlUnboxFixture)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var fixtures = GeneratedFixtureCatalog.Catalog
            .Where(fixture => fixtureIds.Contains(fixture.Id))
            .ToArray();
        var csharpResult = GeneratedFixtureRunner.RunWithMaterializedFixtures(
            fixtures,
            GeneratedFixtureRunOptions.Default,
            (_, assemblyPath) =>
            {
                return EvaluateAssemblies([assemblyPath], null, workers, sequential, annotatedNodes, causeByNode);
            });

        var unboxFixture = BuildUnboxFixtureAssembly();
        try
        {
            var unboxResult = EvaluateAssemblies([unboxFixture.Path], null, workers, sequential, annotatedNodes, causeByNode);
            var scan = Merge(csharpResult, unboxResult);
            var guarantee = BuildFixtureGuarantee(
                string.Join(", ", AssertionFixtureGuarantees
                    .SelectMany(g => g.FixtureIds)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)),
                annotatedNodes,
                causeByNode,
                scan.CoverageByNode);
            return scan with { FixtureGuarantee = guarantee };
        }
        finally
        {
            TryDeleteDirectory(unboxFixture.Directory);
        }
    }

    static AssertionFixtureGuaranteeResult BuildFixtureGuarantee(
        string assemblyPath,
        IReadOnlyList<string> annotatedNodes,
        IReadOnlyDictionary<string, InverseLedger.NodeCause> causeByNode,
        IReadOnlyDictionary<string, int> coverageByNode)
    {
        var fixturesByNode = AssertionFixtureGuarantees
            .ToDictionary(g => g.Node, g => g.FixtureIds, StringComparer.Ordinal);
        var rows = annotatedNodes
            .Select(node => new AssertionFixtureNodeResult(
                node,
                causeByNode.TryGetValue(node, out var cause) ? cause : InverseLedger.CauseFor(node),
                fixturesByNode.TryGetValue(node, out var fixtures) ? fixtures : [],
                coverageByNode.GetValueOrDefault(node)))
            .OrderBy(row => row.Node, StringComparer.Ordinal)
            .ToArray();
        return new AssertionFixtureGuaranteeResult(assemblyPath, coverageByNode, rows);
    }

    internal static IReadOnlyList<string> NodesWithoutFixtureGuarantee(IReadOnlyList<string> annotatedNodes)
    {
        var guaranteed = AssertionFixtureGuarantees
            .Select(g => g.Node)
            .ToHashSet(StringComparer.Ordinal);
        return annotatedNodes
            .Where(node => !guaranteed.Contains(node))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<string> InvalidFixtureGuaranteeIds()
    {
        var fixtureIds = GeneratedFixtureCatalog.Catalog
            .Select(fixture => fixture.Id)
            .ToHashSet(StringComparer.Ordinal);
        fixtureIds.Add(AssertionIlUnboxFixture);
        return AssertionFixtureGuarantees
            .SelectMany(g => g.FixtureIds)
            .Where(id => !fixtureIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    static (string Path, string Directory) BuildUnboxFixtureAssembly()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dotnet-inspect-assertion-il-unbox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "GeneratedAssertionIlUnbox.dll");

        var assemblyName = new AssemblyName("GeneratedAssertionIlUnbox");
        var assembly = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(assemblyName.Name!);
        var mutableBuilder = module.DefineType(
            "GeneratedFixtures.AssertionIlUnbox.Mutable",
            TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed,
            typeof(ValueType));
        mutableBuilder.DefineField("Value", typeof(int), FieldAttributes.Public);
        var mutable = mutableBuilder.CreateType();

        var holder = module.DefineType(
            "GeneratedFixtures.AssertionIlUnbox.Class1",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = holder.DefineMethod(
            "Unbox",
            MethodAttributes.Public | MethodAttributes.Static,
            mutable.MakeByRefType(),
            [typeof(object)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Unbox, mutable);
        il.Emit(OpCodes.Ret);

        holder.CreateType();
        assembly.Save(path);
        return (path, directory);
    }

    static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static MethodResult EvaluateFunction(
        string assembly,
        string assemblyPath,
        string type,
        string method,
        int overload,
        IrFunction function,
        Func<MethodRef, IrFunction?>? importMethodBody = null)
    {
        string signature = CorpusMethodIdentity.SignatureText(function.Signature);
        string key = $"{assemblyPath}!{type}::{method}{signature}";
        var violations = new List<ViolationSite>();
        var seenViolations = new HashSet<string>(StringComparer.Ordinal);
        var covered = new SortedSet<string>(StringComparer.Ordinal);
        var occurrenceOrdinals = new Dictionary<string, Dictionary<IrNode, int>>(StringComparer.Ordinal);
        var importCrashes = function.Diagnostics
            .Where(diagnostic => diagnostic.Id == DiagnosticIds.InternalError)
            .ToArray();
        if (importCrashes.Length > 0)
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
                importCrashes[0].ToString());
        }

        var finalStageIdentities = new HashSet<string>(StringComparer.Ordinal);

        void Capture(string passName)
        {
            AddCoveredNodes(function, covered);
            var stageIdentities = new HashSet<string>(StringComparer.Ordinal);
            var stageViolations = new List<(string Identity, ViolationSite Site)>();
            foreach (var predicate in AssertionEvaluator.EvaluateAssumptions(function))
            {
                foreach (var violation in predicate.Violations)
                {
                    string node = violation.Node.GetType().Name;
                    string sinkType = SinkType(violation.Message);
                    string identity = $"{predicate.Name}|{node}|{sinkType}|{violation.Message}";
                    int ordinal = OccurrenceOrdinal(occurrenceOrdinals, identity, violation.Node);
                    string stableIdentity = $"{identity}#{ordinal}";
                    stageIdentities.Add(stableIdentity);
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

            // The most recent Capture is the final stage once the loop ends: keep
            // this stage's identities so survivors (still present at the final
            // stage — the corpus analog of #2271's UNSOUND) can be marked after.
            finalStageIdentities.Clear();
            finalStageIdentities.UnionWith(stageIdentities);
        }

        try
        {
            var context = importMethodBody is null
                ? PassContext.None
                : new PassContext(new Stepper(enabled: false), importMethodBody: importMethodBody);
            Capture(IrPasses.ImportStageName);
            foreach (var pass in IrPasses.Default)
            {
                pass.Run(function, context);
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
                MarkSurvivors(violations, finalStageIdentities),
                covered,
                $"{ex.GetType().Name}: {ex.Message}");
        }

        return new MethodResult(assembly, assemblyPath, type, method, overload, signature, key,
            MarkSurvivors(violations, finalStageIdentities), covered, PassBug: null);
    }

    /// <summary>
    /// Flags each first-appearance violation whose claim is still present at the
    /// final stage — a discharged obligation (wrapped by a later pass, e.g.
    /// coercion insertion) is cleared, a survivor is the real soundness signal.
    /// </summary>
    internal static IReadOnlyList<ViolationSite> MarkSurvivors(
        IReadOnlyList<ViolationSite> violations, IReadOnlySet<string> finalStageIdentities)
        => violations
            .Select(v => v with { FinalStageSurvivor = finalStageIdentities.Contains(v.StageIdentity) })
            .ToArray();

    static int OccurrenceOrdinal(Dictionary<string, Dictionary<IrNode, int>> ordinals, string identity, IrNode node)
    {
        if (!ordinals.TryGetValue(identity, out var byNode))
            ordinals[identity] = byNode = new Dictionary<IrNode, int>(ReferenceEqualityComparer.Instance);
        if (byNode.TryGetValue(node, out int ordinal))
            return ordinal;
        ordinal = byNode.Count;
        byNode.Add(node, ordinal);
        return ordinal;
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

    internal static string SinkType(string message)
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

        // Final-stage survivors are the corpus analog of #2271's UNSOUND marker:
        // an assertion still failing after the last pass, where nothing downstream
        // remains to discharge it. Every other violation site is a discharged
        // OBLIGATION (a later pass, e.g. coercion insertion, wrapped the sink) —
        // the pipeline working as designed. The survivor count is the real
        // soundness number; "final stage is zero" for the wrappable population is
        // the target (known PrinterOwned residuals excluded). See #2269.
        var survivors = methodsWithViolations.SelectMany(m => m.Violations).Where(v => v.FinalStageSurvivor).ToArray();
        int survivorMethods = methodsWithViolations.Count(m => m.Violations.Any(v => v.FinalStageSurvivor));
        Console.WriteLine($"  final-stage survivors (UNSOUND): {survivors.Length} across {survivorMethods} method(s)");
        Console.WriteLine($"  discharged obligations         : {violationCount - survivors.Length}");
        Console.WriteLine();

        PrintHistogram("By sink type:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.SinkType));
        PrintHistogram("By first failing pass:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.Pass));
        PrintHistogram("By node:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.Node));
        PrintHistogram("By predicate:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.Predicate));

        if (survivors.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Final-stage survivors (UNSOUND — the soundness signal):");
            PrintHistogram("  by node:", survivors.GroupBy(v => v.Node));
            PrintHistogram("  by sink type:", survivors.GroupBy(v => v.SinkType));
        }

        Console.WriteLine();
        Console.WriteLine("Annotation coverage:");
        Console.WriteLine($"  distinct [InverseOf] nodes exercised: {result.CoverageByNode.Count}/{result.AnnotatedNodes.Count}");
        if (result.CoverageByNode.Count > 0)
            Console.WriteLine($"  covered: {string.Join(", ", result.CoverageByNode.Keys.Order(StringComparer.Ordinal))}");
        var missing = result.AnnotatedNodes.Except(result.CoverageByNode.Keys, StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
        {
            PrintMissingByCause("  importer-emitted not exercised", missing, result.CauseByNode, InverseLedger.NodeCause.ImporterEmitted);
            PrintMissingByCause("  pass-raised not exercised (raise-pass or fixture gap; investigate)", missing, result.CauseByNode, InverseLedger.NodeCause.PassRaised);
        }

        if (result.FixtureGuarantee is { } fixtureGuarantee)
        {
            Console.WriteLine();
            Console.WriteLine("Generated fixture guarantee:");
            Console.WriteLine($"  fixtures: {fixtureGuarantee.FixtureIds}");
            Console.WriteLine($"  guaranteed nodes: {fixtureGuarantee.Nodes.Count(n => n.Status == AssertionFixtureNodeStatus.Pass)}/{fixtureGuarantee.Nodes.Count}");
            if (fixtureGuarantee.MissingFixtures.Count > 0)
                Console.WriteLine($"  nodes without fixture mapping: {string.Join(", ", fixtureGuarantee.MissingFixtures.Select(n => n.Node))}");
            if (fixtureGuarantee.Regressions.Count > 0)
            {
                Console.WriteLine("  fixture regression alarms (report-only):");
                foreach (var row in fixtureGuarantee.Regressions.OrderBy(r => r.Cause).ThenBy(r => r.Node, StringComparer.Ordinal))
                    Console.WriteLine($"    {row.Cause}: {row.Node} expected from {string.Join(", ", row.FixtureIds)}");
            }
            else if (fixtureGuarantee.MissingFixtures.Count == 0)
            {
                Console.WriteLine("  fixture regression alarms (report-only): (none)");
            }
        }

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

    static void PrintMissingByCause(
        string label,
        IReadOnlyList<string> missing,
        IReadOnlyDictionary<string, InverseLedger.NodeCause>? causeByNode,
        InverseLedger.NodeCause cause)
    {
        var nodes = missing
            .Where(node => (causeByNode is not null && causeByNode.TryGetValue(node, out var classified)
                ? classified
                : InverseLedger.CauseFor(node)) == cause)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (nodes.Length > 0)
            Console.WriteLine($"{label}: {string.Join(", ", nodes)}");
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
        var survivorGained = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var survivorLost = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var method in comparable)
        {
            var now = currentByMethod[method].ViolationIdentities();
            var was = baselineByMethod[method].ViolationIdentities();
            foreach (var violation in now.Except(was, StringComparer.Ordinal))
                (gained.TryGetValue(violation, out var methods) ? methods : gained[violation] = []).Add(method);
            foreach (var violation in was.Except(now, StringComparer.Ordinal))
                (lost.TryGetValue(violation, out var methods) ? methods : lost[violation] = []).Add(method);

            var nowSurvivors = currentByMethod[method].SurvivorIdentities();
            var wasSurvivors = baselineByMethod[method].SurvivorIdentities();
            foreach (var violation in nowSurvivors.Except(wasSurvivors, StringComparer.Ordinal))
                (survivorGained.TryGetValue(violation, out var methods) ? methods : survivorGained[violation] = []).Add(method);
            foreach (var violation in wasSurvivors.Except(nowSurvivors, StringComparer.Ordinal))
                (survivorLost.TryGetValue(violation, out var methods) ? methods : survivorLost[violation] = []).Add(method);
        }

        Console.WriteLine();
        Console.WriteLine($"ASSERTION VIOLATION DIFF vs {baselinePath} ({comparable.Length} methods checked in both; {onlyCurrent} only-current, {onlyBaseline} only-baseline, {passBugExcluded} pass-bug excluded)");
        PrintDiffSide("REGRESSED (method gained the violation)", gained);
        PrintDiffSide("IMPROVED (method lost the violation)", lost);

        // The survivor deltas are the load-bearing soundness signal: a newly
        // surviving assertion (UNSOUND) is a real regression, distinct from a
        // gained-but-discharged obligation. See #2269.
        Console.WriteLine();
        Console.WriteLine("Final-stage survivors (UNSOUND) delta:");
        PrintDiffSide("  SURVIVOR REGRESSED (method gained a final-stage survivor)", survivorGained);
        PrintDiffSide("  SURVIVOR IMPROVED (method discharged a former survivor)", survivorLost);
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
                SchemaVersion: 2,
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
                            .Select(v => new AssertionViolationRecord(v.Pass, v.Predicate, v.Node, v.SinkType, v.Message, v.Ordinal, v.FinalStageSurvivor))
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

        /// <summary>Identities of violations still failing at the final stage — the survivor (UNSOUND) set.</summary>
        public SortedSet<string> SurvivorIdentities()
            => new(Violations.Where(v => v.FinalStageSurvivor).Select(v => v.Identity), StringComparer.Ordinal);
    }

    public sealed record AssertionViolationRecord(
        string Pass,
        string Predicate,
        string Node,
        string SinkType,
        string Message,
        int Ordinal,
        bool FinalStageSurvivor = false)
    {
        public string Identity => $"{Predicate}|{Node}|{SinkType}|{Message}#{Ordinal}";
    }
}
