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
        bool FinalStageSurvivor = false,
        int LifetimeStages = 0,
        string DischargePass = "")
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
        new("AnonymousObject", [AssertionCoverageFixture]),
        new("ArrayLength", [AssertionCoverageFixture]),
        new("ArrayLiteral", [AssertionCoverageFixture]),
        new("AwaitExpression", [AssertionCoverageFixture]),
        new("Binary", [AssertionCoverageFixture]),
        new("Box", [AssertionCoverageFixture]),
        new("Call", [AssertionCoverageFixture]),
        new("CallIndirect", [AssertionCoverageFixture]),
        new("CastClass", [AssertionCoverageFixture]),
        new("CaughtException", [AssertionCoverageFixture]),
        new("Coalesce", [AssertionCoverageFixture]),
        new("Coerce", [AssertionCoverageFixture]),
        new("CollectionExpression", [AssertionCoverageFixture]),
        new("CollectionSpreadElement", [AssertionCoverageFixture]),
        new("Comparison", [AssertionCoverageFixture]),
        new("Conditional", [AssertionCoverageFixture]),
        new("Constant", [AssertionCoverageFixture]),
        new("Convert", [AssertionCoverageFixture]),
        new("DefaultValue", [AssertionCoverageFixture]),
        new("DelegateCreation", [AssertionCoverageFixture]),
        new("FixedBufferElementAddress", [AssertionCoverageFixture]),
        new("IncrementDecrement", [AssertionCoverageFixture]),
        new("IndexFromEnd", [AssertionCoverageFixture]),
        new("InitializerBlock", [AssertionCoverageFixture]),
        new("InlineArraySpanConversion", [AssertionCoverageFixture]),
        new("InterpolatedStringExpression", [AssertionCoverageFixture]),
        new("IsInstance", [AssertionCoverageFixture]),
        new("IsPattern", [AssertionCoverageFixture]),
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
        new("ObjectInitializerExpression", [AssertionCoverageFixture]),
        new("PositionalPattern", [AssertionCoverageFixture]),
        new("RangeExpression", [AssertionCoverageFixture]),
        new("RecursivePropertyDeclarationPattern", [AssertionCoverageFixture]),
        new("SingleElementListPattern", [AssertionCoverageFixture]),
        new("SizeOf", [AssertionCoverageFixture]),
        new("SliceExpression", [AssertionCoverageFixture]),
        new("SpanLiteral", [AssertionCoverageFixture]),
        new("StackAllocArray", [AssertionCoverageFixture]),
        new("StackAllocate", [AssertionCoverageFixture]),
        new("SwitchExpression", [AssertionCoverageFixture]),
        new("TupleBinaryExpression", [AssertionCoverageFixture]),
        new("TupleExpression", [AssertionCoverageFixture]),
        new("TypeOf", [AssertionCoverageFixture]),
        new("Unary", [AssertionCoverageFixture]),
        new("Unbox", [AssertionIlUnboxFixture]),
        new("UnboxAny", [AssertionCoverageFixture]),
        new("UnionSwitchExpression", [AssertionUnionSwitchFixture]),
        new("WithExpression", [AssertionCoverageFixture]),
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
        var declaredFixtureIds = AssertionFixtureGuarantees
            .SelectMany(g => g.FixtureIds)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Score each node against coverage from its OWN declared fixture(s). The C#
        // fixtures compile into one combined assembly, so scanning them together mixes
        // their coverage — a sibling fixture's incidental production would then mask an
        // inert declared producer (the #2289 IsPattern masking). Build and scan each
        // declared fixture in isolation so the declared producer must exercise the node.
        var perFixtureCoverage = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        var scans = new List<Result>();

        foreach (var fixtureId in declaredFixtureIds
            .Where(id => id != AssertionIlUnboxFixture)
            .Order(StringComparer.Ordinal))
        {
            var fixture = GeneratedFixtureCatalog.Catalog.FirstOrDefault(f => f.Id == fixtureId);
            if (fixture is null)
                continue;
            var fixtureScan = GeneratedFixtureRunner.RunWithMaterializedFixtures(
                [fixture],
                GeneratedFixtureRunOptions.Default,
                (_, assemblyPath) => EvaluateAssemblies([assemblyPath], null, workers, sequential, annotatedNodes, causeByNode));
            perFixtureCoverage[fixtureId] = fixtureScan.CoverageByNode;
            scans.Add(fixtureScan);
        }

        if (declaredFixtureIds.Contains(AssertionIlUnboxFixture))
        {
            var unboxFixture = BuildUnboxFixtureAssembly();
            try
            {
                var unboxScan = EvaluateAssemblies([unboxFixture.Path], null, workers, sequential, annotatedNodes, causeByNode);
                perFixtureCoverage[AssertionIlUnboxFixture] = unboxScan.CoverageByNode;
                scans.Add(unboxScan);
            }
            finally
            {
                TryDeleteDirectory(unboxFixture.Directory);
            }
        }

        var guarantee = BuildFixtureGuarantee(
            string.Join(", ", declaredFixtureIds.Order(StringComparer.Ordinal)),
            annotatedNodes,
            causeByNode,
            perFixtureCoverage);
        // scans is empty only in a broken dev state (every declared fixture id missing
        // from the catalog and no unbox fixture); report it as zero coverage — every
        // node regresses — rather than throwing an opaque Aggregate-on-empty exception.
        if (scans.Count == 0)
            return new Result([], new SortedDictionary<string, int>(StringComparer.Ordinal), annotatedNodes, causeByNode, guarantee);
        return scans.Aggregate(Merge) with { FixtureGuarantee = guarantee };
    }

    static AssertionFixtureGuaranteeResult BuildFixtureGuarantee(
        string fixtureIds,
        IReadOnlyList<string> annotatedNodes,
        IReadOnlyDictionary<string, InverseLedger.NodeCause> causeByNode,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> perFixtureCoverage)
    {
        var fixturesByNode = AssertionFixtureGuarantees
            .ToDictionary(g => g.Node, g => g.FixtureIds, StringComparer.Ordinal);
        var rows = annotatedNodes
            .Select(node =>
            {
                var declared = fixturesByNode.TryGetValue(node, out var fixtures) ? fixtures : [];
                return new AssertionFixtureNodeResult(
                    node,
                    causeByNode.TryGetValue(node, out var cause) ? cause : InverseLedger.CauseFor(node),
                    declared,
                    ObservedFromDeclaredFixtures(declared, perFixtureCoverage, node));
            })
            .OrderBy(row => row.Node, StringComparer.Ordinal)
            .ToArray();
        return new AssertionFixtureGuaranteeResult(fixtureIds, AggregateCoverage(perFixtureCoverage), rows);
    }

    /// <summary>
    /// A node's observed coverage counted only from the fixtures that DECLARE it. A
    /// sibling fixture's incidental production of the node does not count — the declared
    /// producer must itself exercise the node, so an inert declared fixture cannot pass
    /// by free-riding on another fixture's coverage (#2293).
    /// </summary>
    internal static int ObservedFromDeclaredFixtures(
        IReadOnlyList<string> declaredFixtureIds,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> perFixtureCoverage,
        string node)
        => declaredFixtureIds.Sum(id =>
            perFixtureCoverage.TryGetValue(id, out var coverage) ? coverage.GetValueOrDefault(node) : 0);

    static IReadOnlyDictionary<string, int> AggregateCoverage(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> perFixtureCoverage)
    {
        var coverage = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var fixtureCoverage in perFixtureCoverage.Values)
            foreach (var (node, count) in fixtureCoverage)
                coverage[node] = coverage.GetValueOrDefault(node) + count;
        return coverage;
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
        var stageNames = new List<string>();
        var firstStageByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);
        var lastStageByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);

        void Capture(string passName)
        {
            int stageIndex = stageNames.Count;
            stageNames.Add(passName);
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
                    // Track the accrual (first) and last-present stage index per
                    // identity — pass names in IrPasses.Default are not unique
                    // (e.g. typed-constants runs twice), so lifetime must key on
                    // capture-order index, not the pass name.
                    if (!firstStageByIdentity.ContainsKey(stableIdentity))
                        firstStageByIdentity[stableIdentity] = stageIndex;
                    lastStageByIdentity[stableIdentity] = stageIndex;
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
                Finalize(violations, finalStageIdentities, stageNames, firstStageByIdentity, lastStageByIdentity),
                covered,
                $"{ex.GetType().Name}: {ex.Message}");
        }

        return new MethodResult(assembly, assemblyPath, type, method, overload, signature, key,
            Finalize(violations, finalStageIdentities, stageNames, firstStageByIdentity, lastStageByIdentity),
            covered, PassBug: null);
    }

    /// <summary>
    /// Marks each first-appearance violation with its final-stage-survivor status
    /// and, for a discharged obligation, its lifetime — the number of pipeline
    /// stages it persisted from accrual to discharge (the corpus analog of the
    /// obligation-lifetime the effects model describes). A survivor has no
    /// discharge, so its lifetime is 0 and <c>DischargePass</c> is empty; a
    /// discharged obligation records the pass that cleared it. A short lifetime
    /// means a pass decided the type early; a long one means it retrofitted the
    /// claim late.
    /// </summary>
    internal static IReadOnlyList<ViolationSite> Finalize(
        IReadOnlyList<ViolationSite> violations,
        IReadOnlySet<string> finalStageIdentities,
        IReadOnlyList<string> stageNames,
        IReadOnlyDictionary<string, int> firstStageByIdentity,
        IReadOnlyDictionary<string, int> lastStageByIdentity)
        => violations
            .Select(v =>
            {
                bool survivor = finalStageIdentities.Contains(v.StageIdentity);
                if (survivor)
                    return v with { FinalStageSurvivor = true, LifetimeStages = 0, DischargePass = "" };

                int first = firstStageByIdentity[v.StageIdentity];
                int last = lastStageByIdentity[v.StageIdentity];
                // Discharged after `last`: the first absent stage is last+1.
                int dischargeIndex = last + 1;
                int lifetime = dischargeIndex - first;
                string dischargePass = dischargeIndex < stageNames.Count ? stageNames[dischargeIndex] : "";
                return v with { FinalStageSurvivor = false, LifetimeStages = lifetime, DischargePass = dischargePass };
            })
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

    /// <summary>
    /// The three mutually-exclusive violation states of a scan. Discharged
    /// obligations and final-stage survivors (UNSOUND) are drawn only from
    /// methods that completed the pipeline; a pass-bug method never reached a
    /// real final stage, so its violations are UNKNOWN — folding them into either
    /// bucket misreads a crash as a soundness or construction signal (#2285).
    /// </summary>
    internal readonly record struct ViolationStates(
        IReadOnlyList<ViolationSite> Survivors,
        int SurvivorMethods,
        IReadOnlyList<ViolationSite> Discharged,
        int UnknownViolations,
        int UnknownMethods);

    internal static ViolationStates ClassifyViolationStates(IReadOnlyList<MethodResult> methodsWithViolations)
    {
        var completed = methodsWithViolations.Where(m => m.PassBug is null).ToArray();
        var passBug = methodsWithViolations.Where(m => m.PassBug is not null).ToArray();
        return new ViolationStates(
            Survivors: completed.SelectMany(m => m.Violations).Where(v => v.FinalStageSurvivor).ToArray(),
            SurvivorMethods: completed.Count(m => m.Violations.Any(v => v.FinalStageSurvivor)),
            Discharged: completed.SelectMany(m => m.Violations).Where(v => !v.FinalStageSurvivor).ToArray(),
            UnknownViolations: passBug.Sum(m => m.Violations.Count),
            UnknownMethods: passBug.Length);
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
        // A pass-bug method never reached a real final stage, so its violations
        // are UNKNOWN — neither final-stage survivors (UNSOUND) nor discharged
        // obligations. Counting them either way misreads a pipeline crash as a
        // soundness or construction signal, so the survivor/discharge tallies
        // (and the histograms and lifetime stats below) restrict to methods that
        // completed the pipeline and the crash residue is a third, explicit
        // state (#2285; the --diff lane already excludes pass bugs).
        var states = ClassifyViolationStates(methodsWithViolations);
        var survivors = states.Survivors;
        var discharged = states.Discharged;
        Console.WriteLine($"  final-stage survivors (UNSOUND): {survivors.Count} across {states.SurvivorMethods} method(s)");
        Console.WriteLine($"  discharged obligations         : {discharged.Count}");
        if (states.UnknownViolations > 0)
            Console.WriteLine($"  unknown (pass bug, no final stage): {states.UnknownViolations} across {states.UnknownMethods} method(s)");
        Console.WriteLine();

        PrintHistogram("By sink type:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.SinkType));
        PrintHistogram("By first failing pass:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.Pass));
        PrintHistogram("By node:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.Node));
        PrintHistogram("By predicate:", methodsWithViolations.SelectMany(m => m.Violations).GroupBy(v => v.Predicate));

        if (survivors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Final-stage survivors (UNSOUND — the soundness signal):");
            PrintHistogram("  by node:", survivors.GroupBy(v => v.Node));
            PrintHistogram("  by sink type:", survivors.GroupBy(v => v.SinkType));
        }

        // Obligation lifetime — stages from accrual to discharge — is a
        // construction-quality signal: a short lifetime means a pass decided the
        // type early, a long one means it retrofitted the claim late. Only
        // discharged obligations have a lifetime (survivors never discharge, and
        // pass-bug residue is UNKNOWN, not discharged — both are excluded above).
        // See docs/design/assertion-lane-effects.md.
        if (discharged.Count > 0)
        {
            var lifetimes = discharged.Select(v => v.LifetimeStages).ToArray();
            Console.WriteLine();
            Console.WriteLine("Obligation lifetime (discharged; stages from accrual to discharge):");
            Console.WriteLine($"  mean: {lifetimes.Average():F1}  max: {lifetimes.Max()}  min: {lifetimes.Min()}");
            PrintHistogram("  by discharging pass:", discharged.Where(v => v.DischargePass.Length > 0).GroupBy(v => v.DischargePass));
            Console.WriteLine("  longest-lived (retrofit hotspots):");
            foreach (var v in discharged.OrderByDescending(v => v.LifetimeStages).Take(maxExamples))
                Console.WriteLine($"    {v.LifetimeStages} stages  {v.Node} -> {v.SinkType}  ({v.Pass} -> {v.DischargePass})");
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
        HarnessLog.Status($"Wrote assertion-violation map for {snapshot.Methods.Count} methods to {path}");
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
        // gained-but-discharged obligation. See #2269. The FinalStageSurvivor
        // flag arrived in snapshot schema v2, so a v1 baseline deserializes every
        // record with it false — comparing survivor sets against it would report
        // every current survivor as newly gained. Skip that section (and say why)
        // rather than emit a migration artifact as a regression (#2285).
        Console.WriteLine();
        Console.WriteLine("Final-stage survivors (UNSOUND) delta:");
        if (SurvivorDeltaComparable(baseline.SchemaVersion))
        {
            PrintDiffSide("  SURVIVOR REGRESSED (method gained a final-stage survivor)", survivorGained);
            PrintDiffSide("  SURVIVOR IMPROVED (method discharged a former survivor)", survivorLost);
        }
        else
        {
            Console.WriteLine($"  (skipped: baseline schema v{baseline.SchemaVersion} predates the survivor flag (v2).");
            Console.WriteLine("   Regenerate the baseline with the current tool; a v1-baseline survivor delta is a");
            Console.WriteLine("   migration artifact, not a regression.)");
        }
    }

    /// <summary>
    /// Whether a baseline's survivor set is comparable to the current run. The
    /// <c>FinalStageSurvivor</c> flag is a schema-v2 field; a v1 (or older)
    /// baseline records it as false everywhere, so a survivor delta against it is
    /// a migration artifact rather than a real regression (#2285).
    /// </summary>
    internal static bool SurvivorDeltaComparable(int baselineSchemaVersion) => baselineSchemaVersion >= 2;

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
