using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json.Serialization;

using DotnetInspector.Services;
using DotnetInspector.RoundTripCompilation;
using ILInspector.CSharp;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Research;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Prototype for the ReturnToSender compile-back architecture: build typed shell
/// records, print them as source, compile with Roslyn, and compare opcodes.
/// </summary>
static class ReturnToSender
{
    public enum FaultIsolationKind
    {
        BodyDefect,
        ShellOrClosureDefect,
    }

    public enum FaultIsolationMethod
    {
        /// <summary>Authored body compiled in the failing shell (positive control).</summary>
        SubstitutionControl,

        /// <summary>
        /// Shell was also broken, but the authored body was error-free within its
        /// own body span while the decompiled body carried an in-body error —
        /// attributable to the decompiler by span comparison.
        /// </summary>
        SpanMeasured,
    }

    public sealed record FaultIsolationResult(
        FaultIsolationKind Kind,
        string SourcePath,
        string? Detail)
    {
        public FaultIsolationMethod Method { get; init; } = FaultIsolationMethod.SubstitutionControl;
    }

    /// <summary>
    /// Independent compile-back evidence for an event accessor's sibling
    /// (the <c>add</c>/<c>remove</c> not directly requested), captured when
    /// both accessors have real bodies raised into a single reconstruction
    /// (see <see cref="CompileBackEventAccessor"/>). This mirrors
    /// <see cref="Result.Status"/>/<c>OriginalOpcodes</c>/<c>RecompiledOpcodes</c>
    /// but for the sibling method, so each accessor keeps its own exact
    /// compile-back verdict rather than one accessor borrowing the other's.
    /// </summary>
    public sealed record SiblingAccessorEvidence(
        string MethodName,
        FidelityCheck.CompileBackStatus Status,
        string OriginalOpcodes,
        string RecompiledOpcodes);

    public sealed record Result(
        CompileBackReconstructionPlan Plan,
        string Source,
        FidelityCheck.CompileBackStatus Status,
        string OriginalOpcodes,
        string RecompiledOpcodes,
        string? Detail,
        string TargetBody = "",
        IlDiffDisplayResult? IlDiffDiagnostic = null,
        IlMemberDiffResult? IlDiff = null,
        MemberAnchor? MemberAnchor = null,
        IReadOnlyList<DecompilerDecision>? Decisions = null,
        FidelityCheck.CompileBackResult? CompileBackFloor = null,
        FaultIsolationResult? FaultIsolation = null,
        SiblingAccessorEvidence? SiblingAccessor = null,
        IlBodyDiffResult? FidelityDiff = null)
    {
        public bool UsedCompileBackFloor => CompileBackFloor is not null;
        public RoundTripScope Scope { get; init; } = RoundTripScope.Cluster;
        public IReadOnlyList<string> UnsupportedDeclarations { get; init; } = [];
        public bool DeclarationComplete
            => Scope != RoundTripScope.All || UnsupportedDeclarations.Count == 0;
        public RoundTripBodyPolicy BodyPolicy { get; init; } = RoundTripBodyPolicy.Selected;
        public IReadOnlyList<FullBodyProduction> FullBodies { get; init; } = [];
        public bool BodyComplete
            => BodyPolicy != RoundTripBodyPolicy.Full
               || (!UsedCompileBackFloor
                   && FullBodies.All(body => body.Status == MemberBodyProductionStatus.Complete));
        public RoundTripComparisonResult? Comparison { get; init; }
        public RoundTripCompilationProvenance? Compilation { get; init; }
        [JsonIgnore]
        public byte[]? DonorPe { get; init; }
        internal ArtifactRequest? FinalRequest { get; init; }
    }

    public sealed record RequestedTarget(string Type, string Method, int Overload, string? Signature = null);

    public sealed record ScopePairResult(
        Result Cluster,
        Result All,
        RoundTripScopeComparisonResult Comparison);

    sealed class NoSupportedReturnToSenderTargetsException(string message) : InvalidOperationException(message);

    enum ComparisonDelta
    {
        Rescued,
        Same,
        Worse,
        Changed,
        CurrentMissing,
    }

    sealed record ComparisonResult(
        Result ReturnToSender,
        FidelityCheck.CompileBackResult? Current,
        ComparisonDelta Delta);

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        int total = 0, exact = 0, opcodeDiff = 0, operandDiff = 0;
        int fidelityUnavailable = 0, recompileFail = 0, contextFail = 0;
        int closureRoots = 0, closureMembers = 0, compileBackFloor = 0;
        var planningDiagnostics = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var examples = new List<string>();

        foreach (var assemblyPath in assemblies)
        {
            if (total >= cap)
                break;

            IReadOnlyList<Result> results;
            try
            {
                results = CompileBackPropertyGetters(assemblyPath, cap - total);
            }
            catch (NoSupportedReturnToSenderTargetsException)
            {
                continue;
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
            {
                total++;
                contextFail++;
                if (examples.Count < maxExamples)
                    examples.Add($"{Path.GetFileName(assemblyPath)}\n    ContextFail: {ex.Message}");
                continue;
            }

            foreach (var result in results)
            {
                if (total >= cap)
                    break;

                total++;
                closureRoots += Math.Max(0, result.Plan.Types.Count - 1);
                closureMembers += result.Plan.Types
                    .Skip(1)
                    .Sum(type => type.Members.Count);
                if (result.UsedCompileBackFloor)
                    compileBackFloor++;
                foreach (var diagnostic in result.Plan.Diagnostics)
                {
                    string key = $"{diagnostic.Layer}/{diagnostic.Reason}";
                    planningDiagnostics[key] = planningDiagnostics.GetValueOrDefault(key) + 1;
                }

                switch (result.Status)
                {
                    case FidelityCheck.CompileBackStatus.Exact:
                        exact++;
                        break;
                    case FidelityCheck.CompileBackStatus.OpcodeDiff:
                        opcodeDiff++;
                        break;
                    case FidelityCheck.CompileBackStatus.OperandDiff:
                        operandDiff++;
                        break;
                    case FidelityCheck.CompileBackStatus.FidelityUnavailable:
                        fidelityUnavailable++;
                        break;
                    case FidelityCheck.CompileBackStatus.RecompileFail:
                        recompileFail++;
                        break;
                    default:
                        contextFail++;
                        break;
                }

                if (examples.Count < maxExamples)
                {
                    var (layer, detail) = ExampleLayerAndDetail(result);
                    examples.Add($"""
                        {result.Plan.TargetMethod.Type}::{result.Plan.TargetMethod.Method}
                            status: {result.Status}
                            layer : {layer}
                            detail: {detail}
                        """);
                }
            }
        }

        Console.WriteLine($"RETURNTOSENDER over {total} property getters");
        Console.WriteLine();
        Console.WriteLine($"  Exact (V{FidelityCheck.CurrentContractVersion})    : {exact}");
        Console.WriteLine($"  OpcodeDiff    : {opcodeDiff}");
        Console.WriteLine($"  OperandDiff   : {operandDiff}");
        Console.WriteLine($"  FidelityUnavailable: {fidelityUnavailable}");
        Console.WriteLine($"  RecompileFail : {recompileFail}");
        Console.WriteLine($"  ContextFail   : {contextFail}");
        Console.WriteLine();
        Console.WriteLine("Plan layers:");
        Console.WriteLine($"  closure roots   : {closureRoots}");
        Console.WriteLine($"  closure members : {closureMembers}");
        Console.WriteLine($"  compile-back floor: {compileBackFloor}");
        if (planningDiagnostics.Count == 0)
        {
            Console.WriteLine("  diagnostics     : 0");
        }
        else
        {
            Console.WriteLine("  diagnostics:");
            foreach (var (key, count) in planningDiagnostics)
                Console.WriteLine($"    {key}: {count}");
        }

        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in examples)
                Console.WriteLine($"  {example}");
        }

        return recompileFail + contextFail == 0 ? 0 : 1;
    }

    public static int RunComparison(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        int total = 0, rescued = 0, same = 0, worse = 0, changed = 0, currentMissing = 0;
        var examples = new List<ComparisonResult>();

        foreach (var assemblyPath in assemblies)
        {
            if (total >= cap)
                break;

            IReadOnlyList<Result> rtsResults;
            try
            {
                rtsResults = CompileBackPropertyGetters(assemblyPath, cap - total, applyCompileBackFloor: false);
            }
            catch (NoSupportedReturnToSenderTargetsException)
            {
                continue;
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
            {
                if (examples.Count < maxExamples)
                    examples.Add(new ComparisonResult(ContextFailResult(assemblyPath, ex.Message), null, ComparisonDelta.CurrentMissing));
                currentMissing++;
                total++;
                continue;
            }

            IReadOnlyDictionary<string, FidelityCheck.CompileBackResult> current;
            try
            {
                var currentTargets = rtsResults
                    .Select(result => new FidelityCheck.CompileBackTarget(
                        assemblyPath,
                        result.Plan.TargetMethod.Type,
                        result.Plan.TargetMethod.Method,
                        result.Plan.TargetMethod.Overload,
                        result.Plan.TargetMethod.Signature))
                    .Distinct()
                    .ToArray();
                current = FidelityCheck.EvaluateTargets([assemblyPath], currentTargets)
                    .GroupBy(CurrentKey, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
                rtsResults = ApplyCompileBackFloor(assemblyPath, rtsResults, current.Values);
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
            {
                if (examples.Count < maxExamples)
                    examples.Add(new ComparisonResult(ContextFailResult(assemblyPath, ex.Message), null, ComparisonDelta.CurrentMissing));
                currentMissing++;
                total++;
                continue;
            }

            foreach (var rts in rtsResults)
            {
                if (total >= cap)
                    break;

                total++;
                current.TryGetValue(ReturnToSenderKey(rts), out var currentResult);
                var delta = ClassifyDelta(currentResult, rts);
                switch (delta)
                {
                    case ComparisonDelta.Rescued:
                        rescued++;
                        break;
                    case ComparisonDelta.Same:
                        same++;
                        break;
                    case ComparisonDelta.Worse:
                        worse++;
                        break;
                    case ComparisonDelta.Changed:
                        changed++;
                        break;
                    case ComparisonDelta.CurrentMissing:
                        currentMissing++;
                        break;
                }

                if (examples.Count < maxExamples
                    && delta is ComparisonDelta.Rescued or ComparisonDelta.Worse or ComparisonDelta.Changed or ComparisonDelta.CurrentMissing)
                {
                    examples.Add(new ComparisonResult(rts, currentResult, delta));
                }
            }
        }

        Console.WriteLine($"RETURNTOSENDER A/B over {total} property getters");
        Console.WriteLine();
        Console.WriteLine($"  Rescued       : {rescued}");
        Console.WriteLine($"  Same          : {same}");
        Console.WriteLine($"  Changed       : {changed}");
        Console.WriteLine($"  Worse         : {worse}");
        Console.WriteLine($"  CurrentMissing: {currentMissing}");
        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in examples)
            {
                var rts = example.ReturnToSender;
                Console.WriteLine($"  {rts.Plan.TargetMethod.Type}::{rts.Plan.TargetMethod.Method}");
                Console.WriteLine($"    delta  : {example.Delta}");
                Console.WriteLine($"    current: {example.Current?.Status.ToString() ?? "missing"} {example.Current?.Detail ?? ""}".TrimEnd());
                Console.WriteLine($"    rts    : {rts.Status} {rts.Detail ?? ""}".TrimEnd());
            }
        }

        return 0;
    }

    static string CurrentKey(FidelityCheck.CompileBackResult result)
        => $"{result.Type}::{result.Method}::{result.Overload}";

    static string ReturnToSenderKey(Result result)
        => $"{result.Plan.TargetMethod.Type}::{result.Plan.TargetMethod.Method}::{result.Plan.TargetMethod.Overload}";

    static string FloorKey(FidelityCheck.CompileBackResult result)
        => $"{result.Type}::{result.Method}::{result.Overload}::{result.Signature}";

    static string FloorKey(Result result)
        => $"{result.Plan.TargetMethod.Type}::{result.Plan.TargetMethod.Method}::{result.Plan.TargetMethod.Overload}::{result.Plan.TargetMethod.Signature}";

    static ComparisonDelta ClassifyDelta(FidelityCheck.CompileBackResult? current, Result rts)
    {
        if (current is null)
            return ComparisonDelta.CurrentMissing;

        if (current.Status == FidelityCheck.CompileBackStatus.Exact
            && rts.Status != FidelityCheck.CompileBackStatus.Exact)
        {
            return ComparisonDelta.Worse;
        }

        if (current.Status != FidelityCheck.CompileBackStatus.Exact
            && rts.Status == FidelityCheck.CompileBackStatus.Exact)
        {
            return ComparisonDelta.Rescued;
        }

        bool currentChecked = current.Status is FidelityCheck.CompileBackStatus.Exact
            or FidelityCheck.CompileBackStatus.OpcodeDiff
            or FidelityCheck.CompileBackStatus.OperandDiff;
        bool rtsChecked = rts.Status is FidelityCheck.CompileBackStatus.Exact
            or FidelityCheck.CompileBackStatus.OpcodeDiff
            or FidelityCheck.CompileBackStatus.OperandDiff;
        if (!currentChecked && rtsChecked)
            return ComparisonDelta.Rescued;
        if (currentChecked && !rtsChecked)
            return ComparisonDelta.Worse;
        return current.Status == rts.Status ? ComparisonDelta.Same : ComparisonDelta.Changed;
    }

    static Result ContextFailResult(string assemblyPath, string detail)
    {
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(Path.GetFileNameWithoutExtension(assemblyPath), "<assembly>", 0, ""),
            new CompileBackModuleRequirement(["System"], [], []),
            [],
            [],
            []);
        return new Result(plan, "", FidelityCheck.CompileBackStatus.ContextFail, "", "", detail);
    }

    static (string Layer, string Detail) ExampleLayerAndDetail(Result result)
    {
        if (result.UsedCompileBackFloor)
            return ("compile-back floor", result.Detail ?? result.CompileBackFloor!.Status.ToString());
        if (result.Plan.Diagnostics.FirstOrDefault() is { } diagnostic)
            return (diagnostic.Layer, $"{diagnostic.Reason}: {diagnostic.Detail}");
        if (!string.IsNullOrWhiteSpace(result.Detail))
            return (result.Status == FidelityCheck.CompileBackStatus.RecompileFail ? "compile" : "context", result.Detail);
        if (result.Plan.Types.Count > 1)
            return ("closure membership + member surface", "resolved");
        return ("identity transform + target type shell", "resolved");
    }

    public static Result CompileBackFirstPropertyGetter(string assemblyPath)
        => CompileBackPropertyGetters(assemblyPath, maxTargets: 1).First();

    public static IReadOnlyList<Result> CompileBackPropertyGetters(string assemblyPath, int maxTargets = int.MaxValue)
        => CompileBackPropertyGetters(assemblyPath, maxTargets, applyCompileBackFloor: true);

    static IReadOnlyList<Result> CompileBackPropertyGetters(string assemblyPath, int maxTargets, bool applyCompileBackFloor)
    {
        if (maxTargets <= 0)
            return [];

        var results = new List<Result>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            throw new InvalidOperationException("Assembly has no metadata.");

        var reader = pe.GetMetadataReader();
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);
        var sourceIndex = ReturnToSenderSourceIndex.TryCreate(assemblyPath);
        var memberAnchors = MemberAnchorsByMethodToken(pe);

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            string typeName = reader.GetString(typeDef.Name);
            if (!typeDef.GetDeclaringType().IsNil
                || typeName == "<Module>"
                || typeName.Contains('<', StringComparison.Ordinal)
                || typeName.Contains('`', StringComparison.Ordinal)
                || !IsSupportedClass(source, typeHandle))
            {
                continue;
            }

            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                if (reader.GetString(property.Name).Contains('<', StringComparison.Ordinal))
                    continue;

                MethodSignature<string> propertySignature;
                try
                {
                    propertySignature = property.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, typeDef));
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    continue;
                }

                if (propertySignature.ParameterTypes.Length != 0)
                    continue;

                var accessors = property.GetAccessors();
                if (accessors.Getter.IsNil)
                    continue;

                var getter = reader.GetMethodDefinition(accessors.Getter);
                if (getter.RelativeVirtualAddress == 0)
                    continue;

                results.Add(CompileBackPropertyGetterOrContextFail(
                    assemblyPath,
                    pe,
                    reader,
                    source,
                    typeHandle,
                    propertyHandle,
                    accessors.Getter,
                    MemberAnchorFor(memberAnchors, accessors.Getter),
                    sourceIndex));
                if (results.Count >= maxTargets)
                    return applyCompileBackFloor ? ApplyCompileBackFloor(assemblyPath, results) : results;
            }
        }

        if (results.Count == 0)
            throw new NoSupportedReturnToSenderTargetsException("No supported property getter with a method body was found.");
        return applyCompileBackFloor ? ApplyCompileBackFloor(assemblyPath, results) : results;
    }

    public static IReadOnlyList<Result> CompileBackTargets(string assemblyPath, IReadOnlyList<RequestedTarget> targets)
        => CompileBackTargets(
            assemblyPath,
            targets,
            ReturnToSenderSourceIndex.TryCreate(assemblyPath),
            applyCompileBackFloor: true,
            RoundTripScope.Cluster,
            RoundTripBodyPolicy.Selected);

    public static IReadOnlyList<Result> CompileBackTargets(
        string assemblyPath,
        IReadOnlyList<RequestedTarget> targets,
        RoundTripScope scope)
        => CompileBackTargets(
            assemblyPath,
            targets,
            ReturnToSenderSourceIndex.TryCreate(assemblyPath),
            applyCompileBackFloor: true,
            scope,
            RoundTripBodyPolicy.Selected);

    public static IReadOnlyList<Result> CompileBackTargets(
        string assemblyPath,
        IReadOnlyList<RequestedTarget> targets,
        RoundTripScope scope,
        RoundTripBodyPolicy bodyPolicy)
        => CompileBackTargets(
            assemblyPath,
            targets,
            ReturnToSenderSourceIndex.TryCreate(assemblyPath),
            applyCompileBackFloor: true,
            scope,
            bodyPolicy);

    public static ScopePairResult CompileBackScopes(
        string assemblyPath,
        RequestedTarget target)
    {
        var sourceIndex = ReturnToSenderSourceIndex.TryCreate(assemblyPath);
        var cluster = AssertSingleScope(CompileBackTargets(
            assemblyPath,
            [target],
            sourceIndex,
            applyCompileBackFloor: false,
            RoundTripScope.Cluster,
            RoundTripBodyPolicy.Selected));
        var all = AssertSingleScope(CompileBackTargets(
            assemblyPath,
            [target],
            sourceIndex,
            applyCompileBackFloor: false,
            RoundTripScope.All,
            RoundTripBodyPolicy.Selected));
        var clusterRequest = CreateRoundTripRequest(assemblyPath, cluster, RoundTripScope.Cluster);
        var allRequest = CreateRoundTripRequest(assemblyPath, all, RoundTripScope.All);
        var comparison = cluster.Compilation is not null && cluster.DonorPe is not null
            && all.Compilation is not null && all.DonorPe is not null
                ? RoundTripScopeComparison.Compare(
                    clusterRequest,
                    cluster.Compilation,
                    cluster.DonorPe,
                    allRequest,
                    all.Compilation,
                    all.DonorPe)
                : new RoundTripScopeComparisonResult(
                    RoundTripScopeComparisonStatus.Unavailable,
                    Cluster: null,
                    All: null,
                    Members: [],
                    Failure: cluster.Detail ?? all.Detail ?? "cluster or all donor compilation unavailable");
        return new ScopePairResult(cluster, all, comparison);

        static Result AssertSingleScope(IReadOnlyList<Result> results)
            => results.Count == 1
                ? results[0]
                : throw new InvalidOperationException($"Expected one scope result, found {results.Count}.");
    }

    static RoundTripRequest CreateRoundTripRequest(
        string assemblyPath,
        Result result,
        RoundTripScope scope)
    {
        if (result.FinalRequest is not { } finalRequest)
            throw new InvalidOperationException("Scope result has no final artifact request.");
        if (result.MemberAnchor is not { } anchor)
            throw new InvalidOperationException("Scope result has no typed member anchor.");

        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var module = reader.GetModuleDefinition();
        var address = MetadataMethodAddress.Create(reader, finalRequest.TargetMethod);
        var targets = new List<RoundTripTarget> { new(address, anchor) };
        if (result.BodyPolicy == RoundTripBodyPolicy.Full)
        {
            var anchors = MemberAnchorsByMethodToken(pe);
            var included = new HashSet<MetadataMethodAddress> { address };
            foreach (var body in result.FullBodies)
            {
                if (!included.Add(body.Method))
                    continue;
                var method = reader.GetMethodDefinition(body.Method.Handle);
                var bodyAnchor = MemberAnchorFor(anchors, body.Method.Handle)
                    ?? ApiMemberIdentity.CreateMethodAnchor(reader, method.GetDeclaringType(), method);
                targets.Add(new RoundTripTarget(body.Method, bodyAnchor));
            }
        }
        var initializer = finalRequest.TargetBody.ConstructorChain is { } chain
            ? CSharpFormatter.ParseConstructorInitializer(chain)
            : null;
        var replacement = RoundTripMethodReplacement.Create(
            address,
            anchor,
            new CSharpBlockBody(finalRequest.TargetBody.Source, initializer)
            {
                RequiresAsyncModifier = finalRequest.TargetBody.RequiresAsyncModifier,
                RequiresUnsafeModifier = finalRequest.TargetBody.RequiresUnsafeModifier,
            });
        return RoundTripRequest.Create(
            RoundTripArtifactIdentity.FromFile(assemblyPath, "return-to-sender"),
            new RoundTripModuleIdentity(reader.GetString(module.Name), address.ModuleVersionId),
            targets,
            scope,
            result.BodyPolicy,
            [replacement]);
    }

    static Result AttachRoundTripComparison(string assemblyPath, Result result)
    {
        if (result.BodyPolicy != RoundTripBodyPolicy.Full
            || result.Compilation is null
            || result.DonorPe is null
            || result.FinalRequest is null
            || result.MemberAnchor is null)
            return result;
        var request = CreateRoundTripRequest(assemblyPath, result, result.Scope);
        var comparison = RoundTripComparison.Compare(request, result.DonorPe, result.Compilation);
        var productionFailures = result.FullBodies
            .Where(body => body.Status != MemberBodyProductionStatus.Complete)
            .GroupBy(body => body.Method)
            .ToDictionary(group => group.Key, group => group.First());
        if (comparison.Status == RoundTripComparisonStatus.Completed && productionFailures.Count != 0)
        {
            comparison = comparison with
            {
                Members = comparison.Members.Select(member =>
                {
                    if (!productionFailures.TryGetValue(member.Target.Method, out var production))
                        return member;
                    string failure = production.Failure ?? $"body production was {production.Status}";
                    return member with
                    {
                        CSharpStatus = RoundTripEvidenceStatus.Unavailable,
                        IlStatus = IlBodyDiffOutcome.Unavailable,
                        CSharpDiff = null,
                        IlDiff = null,
                        Evidence = null,
                        CSharpFailure = failure,
                        IlFailure = failure,
                    };
                }).ToImmutableArray(),
            };
        }
        return result with
        {
            Comparison = comparison,
        };
    }

    public static IReadOnlyList<Result> CompileBackTargets(
        string assemblyPath,
        IReadOnlyList<RequestedTarget> targets,
        IReadOnlyList<string> sourcePaths)
        => CompileBackTargets(
            assemblyPath,
            targets,
            ReturnToSenderSourceIndex.TryCreate(sourcePaths),
            applyCompileBackFloor: true,
            RoundTripScope.Cluster,
            RoundTripBodyPolicy.Selected);

    internal static IReadOnlyList<Result> CompileBackTargets(
        string assemblyPath,
        IReadOnlyList<RequestedTarget> targets,
        ReturnToSenderSourceIndex? sourceIndex)
        => CompileBackTargets(
            assemblyPath,
            targets,
            sourceIndex,
            applyCompileBackFloor: true,
            RoundTripScope.Cluster,
            RoundTripBodyPolicy.Selected);

    public static IReadOnlyList<Result> CompileBackTargets(
        string assemblyPath,
        IReadOnlyList<RequestedTarget> targets,
        bool applyCompileBackFloor)
        => CompileBackTargets(
            assemblyPath,
            targets,
            ReturnToSenderSourceIndex.TryCreate(assemblyPath),
            applyCompileBackFloor,
            RoundTripScope.Cluster,
            RoundTripBodyPolicy.Selected);

    static IReadOnlyList<Result> CompileBackTargets(
        string assemblyPath,
        IReadOnlyList<RequestedTarget> targets,
        ReturnToSenderSourceIndex? sourceIndex,
        bool applyCompileBackFloor,
        RoundTripScope scope,
        RoundTripBodyPolicy bodyPolicy)
    {
        if (targets.Count == 0)
            return [];
        if (bodyPolicy == RoundTripBodyPolicy.Full && targets.Count != 1)
        {
            throw new NotSupportedException(
                "Full body policy currently requires exactly one primary target; coherent target-set reconstruction is not yet supported.");
        }

        var results = new List<Result>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            throw new InvalidOperationException("Assembly has no metadata.");

        var reader = pe.GetMetadataReader();
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);
        var memberAnchors = MemberAnchorsByMethodToken(pe);
        var typeHandles = reader.TypeDefinitions
            .Select(handle => (Handle: handle, Definition: reader.GetTypeDefinition(handle)))
            .Where(item => reader.GetFullTypeName(item.Definition) is { } fullName
                && fullName.Length != 0)
            .ToDictionary(item => reader.GetFullTypeName(item.Definition), item => item.Handle, StringComparer.Ordinal);

        foreach (var target in targets)
        {
            if (!typeHandles.TryGetValue(target.Type, out var typeHandle))
                continue;

            var typeDef = reader.GetTypeDefinition(typeHandle);
            string typeName = reader.GetString(typeDef.Name);
            if (typeName == "<Module>"
                || typeName.Contains('<', StringComparison.Ordinal)
                || !IsSupportedTargetType(source, typeHandle))
            {
                continue;
            }

            if (TryFindPropertyGetter(reader, typeDef, target) is { } propertyTarget)
            {
                results.Add(CompileBackPropertyGetterOrContextFail(
                    assemblyPath,
                    pe,
                    reader,
                    source,
                    typeHandle,
                    propertyTarget.Property,
                    propertyTarget.Getter,
                    MemberAnchorFor(memberAnchors, propertyTarget.Getter),
                    sourceIndex,
                    scope,
                    bodyPolicy));
                continue;
            }

            if (TryFindPropertySetter(reader, typeDef, target) is { } setterTarget)
            {
                results.Add(CompileBackPropertySetterOrContextFail(
                    assemblyPath,
                    pe,
                    reader,
                    source,
                    typeHandle,
                    setterTarget.Property,
                    setterTarget.Setter,
                    MemberAnchorFor(memberAnchors, setterTarget.Setter),
                    sourceIndex,
                    scope,
                    bodyPolicy));
                continue;
            }

            if (TryFindEventAccessor(reader, typeDef, target, bodyPolicy == RoundTripBodyPolicy.Full) is { } eventTarget)
            {
                results.Add(CompileBackEventAccessorOrContextFail(
                    assemblyPath,
                    pe,
                    reader,
                    source,
                    typeHandle,
                    eventTarget.Event,
                    eventTarget.Accessor,
                    MemberAnchorFor(memberAnchors, eventTarget.Accessor),
                    sourceIndex,
                    scope,
                    bodyPolicy));
                continue;
            }

            if (TryFindMethod(reader, typeDef, target) is { } methodHandle)
            {
                results.Add(CompileBackMethodOrContextFail(
                    assemblyPath,
                    pe,
                    reader,
                    source,
                    typeHandle,
                    methodHandle,
                    MemberAnchorFor(memberAnchors, methodHandle),
                    sourceIndex,
                    scope,
                    bodyPolicy));
            }
        }

        var unsupportedDeclarations = scope == RoundTripScope.All
            ? reader.TypeDefinitions
                .Where(handle => reader.GetTypeDefinition(handle).GetDeclaringType().IsNil)
                .Where(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) != "<Module>")
                .Where(handle => !IsSupportedClosureRoot(reader, reader.GetTypeDefinition(handle)))
                .Select(handle => reader.GetFullTypeName(reader.GetTypeDefinition(handle)))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        var scopedResults = results
            .Select(result => result with
            {
                Scope = scope,
                BodyPolicy = bodyPolicy,
                UnsupportedDeclarations = unsupportedDeclarations,
            })
            .Select(result => AttachRoundTripComparison(assemblyPath, result))
            .ToArray();
        return applyCompileBackFloor ? ApplyCompileBackFloor(assemblyPath, scopedResults) : scopedResults;
    }

    static IReadOnlyList<Result> ApplyCompileBackFloor(
        string assemblyPath,
        IReadOnlyList<Result> results,
        IEnumerable<FidelityCheck.CompileBackResult>? compileBackResults = null)
    {
        if (results.Count == 0 || !results.Any(NeedsCompileBackFloor))
            return results;

        var floorRows = compileBackResults?.ToArray()
            ?? FidelityCheck.EvaluateTargets(
                [assemblyPath],
                results
                    .Where(NeedsCompileBackFloor)
                    .Select(result => new FidelityCheck.CompileBackTarget(
                        assemblyPath,
                        result.Plan.TargetMethod.Type,
                        result.Plan.TargetMethod.Method,
                        result.Plan.TargetMethod.Overload,
                        result.Plan.TargetMethod.Signature))
                    .Distinct()
                    .ToArray());
        var floorByTarget = floorRows
            .Where(row => row.Status is FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff)
            .GroupBy(FloorKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        if (floorByTarget.Count == 0)
            return results;

        var adjusted = new Result[results.Count];
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            adjusted[i] = NeedsCompileBackFloor(result)
                && floorByTarget.TryGetValue(FloorKey(result), out var floor)
                    ? WithCompileBackFloor(result, floor)
                    : result;
        }

        return adjusted;
    }

    static bool NeedsCompileBackFloor(Result result)
        => result.Status is FidelityCheck.CompileBackStatus.RecompileFail or FidelityCheck.CompileBackStatus.ContextFail
           && HasUsableFloorPayload(result);

    static bool HasUsableFloorPayload(Result result)
        => !string.IsNullOrWhiteSpace(result.Source)
           && !string.IsNullOrWhiteSpace(result.TargetBody);

    static Result WithCompileBackFloor(Result result, FidelityCheck.CompileBackResult floor)
    {
        string originalFailure = string.IsNullOrWhiteSpace(result.Detail)
            ? result.Status.ToString()
            : $"{result.Status}: {result.Detail}";
        string floorDetail = string.IsNullOrWhiteSpace(floor.Detail)
            ? $"compile-back-floor: {originalFailure}"
            : $"compile-back-floor: {floor.Detail}; rts: {originalFailure}";
        return result with
        {
            Status = floor.Status,
            OriginalOpcodes = floor.OriginalOpcodes,
            RecompiledOpcodes = floor.RecompiledOpcodes,
            Detail = floorDetail,
            CompileBackFloor = floor,
            FidelityDiff = floor.FidelityDiff,
        };
    }

    static (PropertyDefinitionHandle Property, MethodDefinitionHandle Getter)? TryFindPropertyGetter(
        MetadataReader reader,
        TypeDefinition typeDef,
        RequestedTarget target)
    {
        var getterToProperty = new Dictionary<MethodDefinitionHandle, PropertyDefinitionHandle>();
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var accessors = property.GetAccessors();
            if (!accessors.Getter.IsNil)
                getterToProperty[accessors.Getter] = propertyHandle;
        }

        if (TryFindMethod(reader, typeDef, target) is { } getterHandle
            && getterToProperty.TryGetValue(getterHandle, out var foundPropertyHandle))
        {
            return (foundPropertyHandle, getterHandle);
        }

        return null;
    }

    static (PropertyDefinitionHandle Property, MethodDefinitionHandle Setter)? TryFindPropertySetter(
        MetadataReader reader,
        TypeDefinition typeDef,
        RequestedTarget target)
    {
        var setterToProperty = new Dictionary<MethodDefinitionHandle, PropertyDefinitionHandle>();
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var accessors = property.GetAccessors();
            if (!accessors.Setter.IsNil)
                setterToProperty[accessors.Setter] = propertyHandle;
        }

        if (TryFindMethod(reader, typeDef, target) is { } setterHandle
            && setterToProperty.TryGetValue(setterHandle, out var foundPropertyHandle))
        {
            return (foundPropertyHandle, setterHandle);
        }

        return null;
    }

    static (EventDefinitionHandle Event, MethodDefinitionHandle Accessor)? TryFindEventAccessor(
        MetadataReader reader,
        TypeDefinition typeDef,
        RequestedTarget target,
        bool includePlainEventAccessors)
    {
        var accessorToEvent = new Dictionary<MethodDefinitionHandle, EventDefinitionHandle>();
        foreach (var eventHandle in typeDef.GetEvents())
        {
            var accessors = reader.GetEventDefinition(eventHandle).GetAccessors();
            if (!accessors.Adder.IsNil)
                accessorToEvent[accessors.Adder] = eventHandle;
            if (!accessors.Remover.IsNil)
                accessorToEvent[accessors.Remover] = eventHandle;
        }

        // Explicit-interface event accessors always route through ComposeEventAccessor.
        // A plain (non-explicit-interface) event accessor is method-routed by default so the
        // Selected A/B path keeps its single-accessor shape (and the corpus baseline is stable);
        // it only routes through ComposeEventAccessor under Full, where a coherent
        // `event { add remove }` type surface is required to avoid a CS0082 collision between the
        // standalone accessor method and the re-declared event's synthesized accessor (#3007).
        //
        // Field-like events (`event Action Changed;`) are excluded from the Full broadening: the
        // compiler-generated backing field shares the event's exact name, and the full member
        // surface would emit it as a separate `Action Changed;` field alongside the reconstructed
        // event (CS0102/CS0229). Coherent field-like reconstruction is out of #3007's scope, so
        // these stay method-routed exactly as before (an honest compile-back floor, not a
        // double-declaration false success).
        if (TryFindMethod(reader, typeDef, target) is { } accessorHandle
            && accessorToEvent.TryGetValue(accessorHandle, out var foundEventHandle)
            && (IsExplicitInterfaceEventAccessor(reader, typeDef, accessorHandle)
                || (includePlainEventAccessors && !IsFieldLikeEvent(reader, typeDef, foundEventHandle))))
        {
            return (foundEventHandle, accessorHandle);
        }

        return null;
    }

    // A field-like event (`event Action Changed;`) has a compiler-generated backing field whose
    // name is identical to the event. A custom event with explicit accessors uses a
    // distinctly-named user field, so a same-named field uniquely identifies the field-like form
    // (a type cannot declare both a field and an event with the same name).
    static bool IsFieldLikeEvent(
        MetadataReader reader,
        TypeDefinition typeDef,
        EventDefinitionHandle eventHandle)
    {
        string eventName = reader.GetString(reader.GetEventDefinition(eventHandle).Name);
        foreach (var fieldHandle in typeDef.GetFields())
        {
            if (reader.GetString(reader.GetFieldDefinition(fieldHandle).Name) == eventName)
                return true;
        }

        return false;
    }

    static bool IsExplicitInterfaceEventAccessor(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinitionHandle accessorHandle)
    {
        foreach (var implementationHandle in typeDef.GetMethodImplementations())
        {
            var implementation = reader.GetMethodImplementation(implementationHandle);
            if (implementation.MethodBody != accessorHandle
                || implementation.MethodDeclaration.Kind != HandleKind.MethodDefinition)
            {
                continue;
            }

            var declarationHandle = (MethodDefinitionHandle)implementation.MethodDeclaration;
            var declaration = reader.GetMethodDefinition(declarationHandle);
            var interfaceDef = reader.GetTypeDefinition(declaration.GetDeclaringType());
            if (!interfaceDef.Attributes.HasFlag(TypeAttributes.Interface))
                continue;
            foreach (var eventHandle in interfaceDef.GetEvents())
            {
                var accessors = reader.GetEventDefinition(eventHandle).GetAccessors();
                if (accessors.Adder == declarationHandle || accessors.Remover == declarationHandle)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves the target metadata method by normalized signature when the target
    /// carries one (ordinal-independent), falling back to the count-all overload
    /// ordinal when no signature is supplied or the signature is missing/ambiguous.
    /// </summary>
    static MethodDefinitionHandle? TryFindMethod(
        MetadataReader reader,
        TypeDefinition typeDef,
        RequestedTarget target)
    {
        if (target.Signature is { } signature
            && TryFindMethodBySignature(reader, typeDef, target.Method, signature) is { } bySignature)
        {
            return bySignature;
        }

        return TryFindMethod(reader, typeDef, target.Method, target.Overload);
    }

    static MethodDefinitionHandle? TryFindMethodBySignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        string methodName,
        string signature)
    {
        MethodDefinitionHandle? found = null;
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != methodName
                || method.RelativeVirtualAddress == 0
                || SignatureIdentity.ForMetadataMethod(reader, typeDef, methodHandle) != signature)
            {
                continue;
            }

            if (found is not null)
                return null;
            found = methodHandle;
        }

        return found;
    }

    /// <summary>
    /// True when <paramref name="signature"/> resolves to exactly <paramref name="expected"/>
    /// among body-bearing same-name methods. Target discovery uses this to only attach a
    /// signature that unambiguously round-trips, so a lossy/ambiguous normalized signature
    /// is dropped and both metadata and source correlation fall back to the ordinal.
    /// </summary>
    internal static bool ResolvesUniquelyBySignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        string methodName,
        string signature,
        MethodDefinitionHandle expected)
        => TryFindMethodBySignature(reader, typeDef, methodName, signature) == expected;

    static MethodDefinitionHandle? TryFindMethod(
        MetadataReader reader,
        TypeDefinition typeDef,
        string methodName,
        int overload)
    {
        int seen = 0;
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != methodName)
            {
                continue;
            }

            if (seen++ == overload
                && method.RelativeVirtualAddress != 0)
            {
                return methodHandle;
            }
        }

        return null;
    }

    static Result CompileBackPropertyGetterOrContextFail(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle getterHandle,
        MemberAnchor? memberAnchor,
        ReturnToSenderSourceIndex? sourceIndex,
        RoundTripScope scope = RoundTripScope.Cluster,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected)
    {
        try
        {
            return CompileBackPropertyGetter(assemblyPath, pe, reader, source, typeHandle, propertyHandle, getterHandle, memberAnchor, sourceIndex, scope, bodyPolicy);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return ContextFailResult(assemblyPath, reader, typeHandle, propertyHandle, getterHandle, $"{ex.GetType().Name}: {ex.Message}", memberAnchor);
        }
    }

    static Result CompileBackMethodOrContextFail(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MemberAnchor? memberAnchor,
        ReturnToSenderSourceIndex? sourceIndex,
        RoundTripScope scope = RoundTripScope.Cluster,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected)
    {
        try
        {
            return CompileBackMethod(assemblyPath, pe, reader, source, typeHandle, methodHandle, memberAnchor, sourceIndex, scope, bodyPolicy);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return ContextFailResult(assemblyPath, reader, typeHandle, methodHandle, $"{ex.GetType().Name}: {ex.Message}", memberAnchor);
        }
    }

    static Result CompileBackEventAccessorOrContextFail(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        EventDefinitionHandle eventHandle,
        MethodDefinitionHandle accessorHandle,
        MemberAnchor? memberAnchor,
        ReturnToSenderSourceIndex? sourceIndex,
        RoundTripScope scope = RoundTripScope.Cluster,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected)
    {
        try
        {
            return CompileBackEventAccessor(
                assemblyPath,
                pe,
                reader,
                source,
                typeHandle,
                eventHandle,
                accessorHandle,
                memberAnchor,
                sourceIndex,
                scope,
                bodyPolicy);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return ContextFailResult(
                assemblyPath,
                reader,
                typeHandle,
                accessorHandle,
                $"{ex.GetType().Name}: {ex.Message}",
                memberAnchor);
        }
    }

    static Result CompileBackPropertySetterOrContextFail(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle setterHandle,
        MemberAnchor? memberAnchor,
        ReturnToSenderSourceIndex? sourceIndex,
        RoundTripScope scope = RoundTripScope.Cluster,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected)
    {
        try
        {
            return CompileBackPropertySetter(assemblyPath, pe, reader, source, typeHandle, propertyHandle, setterHandle, memberAnchor, sourceIndex, scope, bodyPolicy);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return ContextFailResult(assemblyPath, reader, typeHandle, propertyHandle, setterHandle, $"{ex.GetType().Name}: {ex.Message}", memberAnchor);
        }
    }

    static Result CompileBackPropertyGetter(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle getterHandle,
        MemberAnchor? memberAnchor,
        ReturnToSenderSourceIndex? sourceIndex,
        RoundTripScope scope,
        RoundTripBodyPolicy bodyPolicy)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var getter = reader.GetMethodDefinition(getterHandle);
        string fullType = reader.GetFullTypeName(typeDef);
        string methodName = reader.GetString(getter.Name);
        int overload = OverloadIndex(reader, typeDef, getterHandle, methodName);

        var targetBody = CompileBackSourceComposer.CreateTargetBody(source, getterHandle, fullType, methodName, out var function);

        var original = MetadataInstructionProducer.Disassemble(pe, reader, getter)
            ?? throw new InvalidOperationException($"Could not disassemble {fullType}::{methodName}.");
        var originalOps = original.Select(instruction => CanonicalOpcode(instruction.OpCodeName)).ToArray();

        return CompileBackTarget(
            assemblyPath,
            pe,
            reader,
            typeHandle,
            getterHandle,
            function,
            targetBody,
            fullType,
            methodName,
            overload,
            originalOps,
            memberAnchor,
            sourceIndex,
            (closureRoots, closureFacts) => new PropertyGetterArtifactRequest(
                AssemblyPath: assemblyPath,
                Reader: reader,
                Function: function,
                TargetType: typeHandle,
                TargetMethod: getterHandle,
                TargetProperty: propertyHandle,
                TargetBody: targetBody,
                FullType: fullType,
                MethodName: methodName,
                Overload: overload,
                SignatureText: CorpusMethodIdentity.SignatureText(function.Signature),
                ClosureRoots: closureRoots,
                ClosureFacts: closureFacts)
            {
                BodyPolicy = bodyPolicy,
                BodySource = source,
            },
            scope: scope,
            bodyPolicy: bodyPolicy);
    }

    static Result CompileBackMethod(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MemberAnchor? memberAnchor,
        ReturnToSenderSourceIndex? sourceIndex,
        RoundTripScope scope,
        RoundTripBodyPolicy bodyPolicy)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var method = reader.GetMethodDefinition(methodHandle);
        string fullType = reader.GetFullTypeName(typeDef);
        string methodName = reader.GetString(method.Name);
        int overload = OverloadIndex(reader, typeDef, methodHandle, methodName);

        var targetBody = CompileBackSourceComposer.CreateTargetBody(source, methodHandle, fullType, methodName, out var function);

        var original = MetadataInstructionProducer.Disassemble(pe, reader, method)
            ?? throw new InvalidOperationException($"Could not disassemble {fullType}::{methodName}.");
        var originalOps = original.Select(instruction => CanonicalOpcode(instruction.OpCodeName)).ToArray();

        return CompileBackTarget(
            assemblyPath,
            pe,
            reader,
            typeHandle,
            methodHandle,
            function,
            targetBody,
            fullType,
            methodName,
            overload,
            originalOps,
            memberAnchor,
            sourceIndex,
            (closureRoots, closureFacts) => new MethodArtifactRequest(
                AssemblyPath: assemblyPath,
                Reader: reader,
                Function: function,
                TargetType: typeHandle,
                TargetMethod: methodHandle,
                TargetBody: targetBody,
                FullType: fullType,
                MethodName: methodName,
                Overload: overload,
                SignatureText: CorpusMethodIdentity.SignatureText(function.Signature),
                ClosureRoots: closureRoots,
                ClosureFacts: closureFacts)
            {
                BodyPolicy = bodyPolicy,
                BodySource = source,
            },
            scope: scope,
            bodyPolicy: bodyPolicy);
    }

    static Result CompileBackEventAccessor(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        EventDefinitionHandle eventHandle,
        MethodDefinitionHandle accessorHandle,
        MemberAnchor? memberAnchor,
        ReturnToSenderSourceIndex? sourceIndex,
        RoundTripScope scope,
        RoundTripBodyPolicy bodyPolicy)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var accessor = reader.GetMethodDefinition(accessorHandle);
        string fullType = reader.GetFullTypeName(typeDef);
        string methodName = reader.GetString(accessor.Name);
        int overload = OverloadIndex(reader, typeDef, accessorHandle, methodName);

        var targetBody = CompileBackSourceComposer.CreateTargetBody(source, accessorHandle, fullType, methodName, out var function);

        var original = MetadataInstructionProducer.Disassemble(pe, reader, accessor)
            ?? throw new InvalidOperationException($"Could not disassemble {fullType}::{methodName}.");
        var originalOps = original.Select(instruction => CanonicalOpcode(instruction.OpCodeName)).ToArray();

        // Raise the sibling accessor's real body too (not an honest throw
        // stub) whenever it has one, so a single reconstruction produces the
        // fully raised event shape. Each accessor still gets its own
        // independent compile-back verdict below rather than one borrowing
        // the other's -- see SiblingAccessorEvidence.
        var eventDefinition = reader.GetEventDefinition(eventHandle);
        var eventAccessors = eventDefinition.GetAccessors();
        var siblingHandle = accessorHandle == eventAccessors.Adder ? eventAccessors.Remover : eventAccessors.Adder;
        ProductTargetBody? siblingBody = null;
        string? siblingMethodName = null;
        string[]? siblingOriginalOps = null;
        if (!siblingHandle.IsNil)
        {
            var siblingMethod = reader.GetMethodDefinition(siblingHandle);
            if (siblingMethod.RelativeVirtualAddress != 0)
            {
                siblingMethodName = reader.GetString(siblingMethod.Name);
                var siblingOriginal = MetadataInstructionProducer.Disassemble(pe, reader, siblingMethod);
                if (siblingOriginal is not null)
                {
                    siblingBody = CompileBackSourceComposer.CreateTargetBody(
                        source,
                        siblingHandle,
                        fullType,
                        siblingMethodName,
                        out _);
                    siblingOriginalOps = siblingOriginal.Select(instruction => CanonicalOpcode(instruction.OpCodeName)).ToArray();
                }
            }
        }

        return CompileBackTarget(
            assemblyPath,
            pe,
            reader,
            typeHandle,
            accessorHandle,
            function,
            targetBody,
            fullType,
            methodName,
            overload,
            originalOps,
            memberAnchor,
            sourceIndex,
            (closureRoots, closureFacts) => new EventAccessorArtifactRequest(
                AssemblyPath: assemblyPath,
                Reader: reader,
                Function: function,
                TargetType: typeHandle,
                TargetMethod: accessorHandle,
                TargetEvent: eventHandle,
                TargetBody: targetBody,
                FullType: fullType,
                MethodName: methodName,
                Overload: overload,
                SignatureText: CorpusMethodIdentity.SignatureText(function.Signature),
                ClosureRoots: closureRoots,
                ClosureFacts: closureFacts,
                SiblingAccessorBody: siblingBody)
            {
                BodyPolicy = bodyPolicy,
                BodySource = source,
            },
            siblingMethodName is not null && siblingOriginalOps is not null
                ? (siblingMethodName, siblingOriginalOps)
                : null,
            scope,
            bodyPolicy);
    }

    static Result CompileBackPropertySetter(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle setterHandle,
        MemberAnchor? memberAnchor,
        ReturnToSenderSourceIndex? sourceIndex,
        RoundTripScope scope,
        RoundTripBodyPolicy bodyPolicy)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var setter = reader.GetMethodDefinition(setterHandle);
        string fullType = reader.GetFullTypeName(typeDef);
        string methodName = reader.GetString(setter.Name);
        int overload = OverloadIndex(reader, typeDef, setterHandle, methodName);

        var targetBody = CompileBackSourceComposer.CreateTargetBody(source, setterHandle, fullType, methodName, out var function);

        var original = MetadataInstructionProducer.Disassemble(pe, reader, setter)
            ?? throw new InvalidOperationException($"Could not disassemble {fullType}::{methodName}.");
        var originalOps = original.Select(instruction => CanonicalOpcode(instruction.OpCodeName)).ToArray();

        return CompileBackTarget(
            assemblyPath,
            pe,
            reader,
            typeHandle,
            setterHandle,
            function,
            targetBody,
            fullType,
            methodName,
            overload,
            originalOps,
            memberAnchor,
            sourceIndex,
            (closureRoots, closureFacts) => new PropertySetterArtifactRequest(
                AssemblyPath: assemblyPath,
                Reader: reader,
                Function: function,
                TargetType: typeHandle,
                TargetMethod: setterHandle,
                TargetProperty: propertyHandle,
                TargetBody: targetBody,
                FullType: fullType,
                MethodName: methodName,
                Overload: overload,
                SignatureText: CorpusMethodIdentity.SignatureText(function.Signature),
                ClosureRoots: closureRoots,
                ClosureFacts: closureFacts)
            {
                BodyPolicy = bodyPolicy,
                BodySource = source,
            },
            scope: scope,
            bodyPolicy: bodyPolicy);
    }

    static Result CompileBackTarget(
        string assemblyPath,
        PEReader originalPe,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        IrFunction function,
        ProductTargetBody targetBody,
        string fullType,
        string methodName,
        int overload,
        string[] originalOps,
        MemberAnchor? memberAnchor,
        ReturnToSenderSourceIndex? sourceIndex,
        Func<IReadOnlySet<TypeDefinitionHandle>, IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>>, ArtifactRequest> createRequest,
        (string MethodName, string[] OriginalOpcodes)? sibling = null,
        RoundTripScope scope = RoundTripScope.Cluster,
        RoundTripBodyPolicy bodyPolicy = RoundTripBodyPolicy.Selected)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable,
            allowUnsafe: true);
        var references = CompilationReferences(assemblyPath).ToArray();
        var indexes = ClosureIndexes(reader);
        var targetRoot = TopLevelRootOf(reader, typeHandle);
        var closureRoots = scope == RoundTripScope.All
            ? reader.TypeDefinitions
                .Where(handle => reader.GetTypeDefinition(handle).GetDeclaringType().IsNil)
                .Where(handle => IsSupportedClosureRoot(reader, reader.GetTypeDefinition(handle)))
                .ToHashSet()
            : [targetRoot];
        closureRoots.Add(targetRoot);
        var closureFacts = new Dictionary<TypeDefinitionHandle, List<CompileBackFact>>();
        const int maxRoots = 200;
        string originalOpcodes = string.Join(" ", originalOps);

        ProductArtifact Compose()
        {
            if (bodyPolicy == RoundTripBodyPolicy.Full)
            {
                foreach (var root in closureRoots)
                {
                    if (!closureFacts.TryGetValue(root, out var facts))
                        closureFacts[root] = facts = [];
                    var fact = new CompileBackFact("round-trip", "closure-member", "full body policy");
                    if (!facts.Contains(fact))
                        facts.Add(fact);
                }
            }
            return CompileBackSourceComposer.Compose(createRequest(closureRoots, closureFacts));
        }

        var firstArtifact = Compose();
        if (firstArtifact.Plan.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Layer == "type identity") is { } initialIdentityDiagnostic)
        {
            return new Result(
                firstArtifact.Plan,
                firstArtifact.Source,
                FidelityCheck.CompileBackStatus.ContextFail,
                originalOpcodes,
                "",
                $"{initialIdentityDiagnostic.Reason}: {initialIdentityDiagnostic.Detail}",
                TargetBody: targetBody.Source,
                MemberAnchor: memberAnchor,
                Decisions: targetBody.Decisions)
            {
                FinalRequest = firstArtifact.Request,
                BodyPolicy = bodyPolicy,
                FullBodies = firstArtifact.FullBodies,
            };
        }

        bool firstArtifactPending = true;
        CompileBackPlanningDiagnostic? identityFailure = null;
        var compilationResult = RoundTripCompilationEngine.Compile(
            compose: () =>
            {
                if (firstArtifactPending)
                {
                    firstArtifactPending = false;
                    return firstArtifact;
                }
                return Compose();
            },
            source: artifact => artifact.Source,
            references,
            parseOptions,
            compileOptions,
            grow: (artifact, errors, semanticModel) =>
            {
                if (artifact.Plan.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Layer == "type identity") is { } identity)
                {
                    identityFailure = identity;
                    return RoundTripGrowthResult.Stop("type-identity");
                }

                var growth = AddClosureRoots(
                    errors,
                    semanticModel,
                    indexes,
                    reader.GetString(typeDef.Namespace),
                    TopLevelRootOf(reader, typeHandle),
                    closureRoots,
                    closureFacts);
                int effectiveRootCount = EffectiveClosureRootCount(artifact, closureRoots);
                string? reason = effectiveRootCount > maxRoots
                    ? "closure-root-budget"
                    : !growth.Grew
                        ? ClosureDiagnosticEvidence.FailureReason(
                        "closure-stalled",
                            growth.UnextractedDiagnosticIds)
                        : null;
                return reason is null
                    ? RoundTripGrowthResult.Continue
                    : RoundTripGrowthResult.Stop(reason);
            },
            new RoundTripCompilationOptions
            {
                AssemblyName = "return-to-sender",
                MaxIterations = 80,
            });

        var sourceResult = compilationResult.Artifact;
        var plan = sourceResult.Plan;
        string unit = sourceResult.Source;
        if (!compilationResult.Succeeded || compilationResult.PeImage is null)
        {
            if (identityFailure is { } identityDiagnostic)
            {
                return new Result(
                    plan,
                    unit,
                    FidelityCheck.CompileBackStatus.ContextFail,
                    originalOpcodes,
                    "",
                    $"{identityDiagnostic.Reason}: {identityDiagnostic.Detail}",
                    TargetBody: targetBody.Source,
                    MemberAnchor: memberAnchor,
                    Decisions: targetBody.Decisions)
                {
                    FinalRequest = sourceResult.Request,
                    Compilation = compilationResult.Provenance,
                    BodyPolicy = bodyPolicy,
                    FullBodies = sourceResult.FullBodies,
                };
            }

            var error = compilationResult.Status == RoundTripCompilationStatus.IterationBudget
                ? compilationResult.FirstError
                : compilationResult.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    ?? compilationResult.FirstError;
            var faultIsolation = TryIsolateRecompileFailure(
                sourceResult.Request,
                unit,
                compilationResult.Diagnostics,
                spanAttributionEligible: compilationResult.Status != RoundTripCompilationStatus.IterationBudget,
                sourceIndex,
                parseOptions,
                compileOptions,
                references);
            return new Result(
                plan,
                unit,
                FidelityCheck.CompileBackStatus.RecompileFail,
                originalOpcodes,
                "",
                $"{compilationResult.StopReason}: {FormatDiagnostic(error)}",
                TargetBody: targetBody.Source,
                MemberAnchor: memberAnchor,
                Decisions: targetBody.Decisions,
                FaultIsolation: faultIsolation)
            {
                FinalRequest = sourceResult.Request,
                Compilation = compilationResult.Provenance,
                BodyPolicy = bodyPolicy,
                FullBodies = sourceResult.FullBodies,
            };
        }

        var recompiledBytes = compilationResult.PeImage;
        using var recompiledStream = new MemoryStream(recompiledBytes, writable: false);
        using var recompiled = new PEReader(recompiledStream);
        var recompiledOps = FindAndDisassemble(recompiled, fullType, methodName, overload: 0)
            ?.Select(instruction => CanonicalOpcode(instruction.OpCodeName))
            .ToArray();
        var implementationDiff = BuildImplementationDiff(assemblyPath, reader, methodHandle, recompiledBytes, fullType, methodName, overload: 0);
        var ilDiff = implementationDiff?.IlDiff;
        var ilDiffDiagnostic = ToDisplayDiagnostic(ilDiff);
        var fidelityDiff = BuildIlDiff(
            originalPe,
            reader,
            methodHandle,
            recompiled,
            fullType,
            methodName,
            overload: 0,
            FidelityCheck.ContractV1BodyDiffNormalization)?.Diff;

        if (recompiledOps is null)
        {
            return new Result(
                plan,
                unit,
                FidelityCheck.CompileBackStatus.ContextFail,
                originalOpcodes,
                "",
                "method-not-found",
                TargetBody: targetBody.Source,
                MemberAnchor: memberAnchor,
                Decisions: targetBody.Decisions)
            {
                FinalRequest = sourceResult.Request,
                Compilation = compilationResult.Provenance,
                DonorPe = recompiledBytes,
                BodyPolicy = bodyPolicy,
                FullBodies = sourceResult.FullBodies,
            };
        }

        var siblingAccessor = sibling is { } siblingRequest
            ? ComputeSiblingAccessorEvidence(recompiled, fullType, siblingRequest.MethodName, siblingRequest.OriginalOpcodes)
            : null;

        var status = FidelityCheck.ClassifyStatus(
            isFull: true,
            opcodesExact: originalOps.SequenceEqual(recompiledOps),
            fidelityDiff: fidelityDiff);
        string? detail = status == FidelityCheck.CompileBackStatus.FidelityUnavailable
            ? fidelityDiff?.Failure ?? "compile-back fidelity body comparison unavailable"
            : fidelityDiff?.Failure;

        return new Result(
            plan,
            unit,
            status,
            originalOpcodes,
            string.Join(" ", recompiledOps),
            detail,
            TargetBody: targetBody.Source,
            IlDiffDiagnostic: ilDiffDiagnostic,
            IlDiff: ilDiff,
            MemberAnchor: memberAnchor,
            Decisions: targetBody.Decisions,
            SiblingAccessor: siblingAccessor,
            FidelityDiff: fidelityDiff)
        {
            FinalRequest = sourceResult.Request,
            Compilation = compilationResult.Provenance,
            DonorPe = recompiledBytes,
            BodyPolicy = bodyPolicy,
            FullBodies = sourceResult.FullBodies,
        };
    }

    /// <summary>
    /// Independently verifies the sibling event accessor raised alongside the
    /// requested target (see <see cref="CompileBackEventAccessor"/>): finds
    /// its own method in the already-recompiled module and compares its
    /// opcodes against its own original disassembly, so the sibling gets its
    /// own exact/opcode-diff verdict rather than inheriting the target's.
    /// </summary>
    static SiblingAccessorEvidence? ComputeSiblingAccessorEvidence(
        PEReader recompiled,
        string fullType,
        string siblingMethodName,
        string[] siblingOriginalOps)
    {
        var recompiledOps = FindAndDisassemble(recompiled, fullType, siblingMethodName, overload: 0)
            ?.Select(instruction => CanonicalOpcode(instruction.OpCodeName))
            .ToArray();
        string originalOpcodes = string.Join(" ", siblingOriginalOps);
        if (recompiledOps is null)
            return new SiblingAccessorEvidence(siblingMethodName, FidelityCheck.CompileBackStatus.ContextFail, originalOpcodes, "");

        return new SiblingAccessorEvidence(
            siblingMethodName,
            siblingOriginalOps.SequenceEqual(recompiledOps)
                ? FidelityCheck.CompileBackStatus.Exact
                : FidelityCheck.CompileBackStatus.OpcodeDiff,
            originalOpcodes,
            string.Join(" ", recompiledOps));
    }

    static Result ContextFailResult(
        string assemblyPath,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle getterHandle,
        string detail,
        MemberAnchor? memberAnchor)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var getter = reader.GetMethodDefinition(getterHandle);
        string fullType = reader.GetFullTypeName(typeDef);
        string methodName = reader.GetString(getter.Name);
        int overload = OverloadIndex(reader, typeDef, getterHandle, methodName);
        string ns = reader.GetString(typeDef.Namespace);
        string typeName = reader.GetString(typeDef.Name);
        string propertyName = reader.GetString(reader.GetPropertyDefinition(propertyHandle).Name);
        var property = new ApiMember
        {
            Name = propertyName,
            Kind = "property",
            SignatureModel = new ApiSignature
            {
                ReturnType = "object",
                MemberName = propertyName,
                Accessors = [new ApiAccessor { Kind = "get" }],
            },
        };
        var type = new ApiType
        {
            Namespace = ns,
            Name = typeName,
            MetadataName = typeName,
            Kind = "class",
            Members = [property],
        };

        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, ""),
            new CompileBackModuleRequirement(["System"], [], []),
            [],
            [
                new CSharpTypePrintRequest(type)
            ],
            []);

        return new Result(
            plan,
            "",
            FidelityCheck.CompileBackStatus.ContextFail,
            "",
            "",
            detail,
            MemberAnchor: memberAnchor);
    }

    static Result ContextFailResult(
        string assemblyPath,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        string detail,
        MemberAnchor? memberAnchor)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var method = reader.GetMethodDefinition(methodHandle);
        string fullType = reader.GetFullTypeName(typeDef);
        string methodName = reader.GetString(method.Name);
        int overload = OverloadIndex(reader, typeDef, methodHandle, methodName);
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, ""),
            new CompileBackModuleRequirement(["System"], [], []),
            [],
            [],
            []);
        return new Result(plan, "", FidelityCheck.CompileBackStatus.ContextFail, "", "", detail, MemberAnchor: memberAnchor);
    }

    static IReadOnlyDictionary<int, MemberAnchor> MemberAnchorsByMethodToken(PEReader pe)
    {
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: true);
        var result = new Dictionary<int, MemberAnchor>();
        foreach (var type in surface.Types)
        {
            foreach (var member in type.Members)
            {
                var anchor = ApiMemberIdentity.GetMemberAnchor(type, member);
                if (member.MetadataToken is { } token)
                    result[token] = anchor;
                if (member.GetterToken is { } getter)
                    result[getter] = anchor;
                if (member.SetterToken is { } setter)
                    result[setter] = anchor;
                if (member.AdderToken is { } adder)
                    result[adder] = anchor;
                if (member.RemoverToken is { } remover)
                    result[remover] = anchor;
            }
        }

        return result;
    }

    static MemberAnchor? MemberAnchorFor(IReadOnlyDictionary<int, MemberAnchor> anchors, MethodDefinitionHandle methodHandle)
        => anchors.TryGetValue(MetadataTokens.GetToken(methodHandle), out var anchor) ? anchor : null;

    static int OverloadIndex(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle target, string methodName)
    {
        int overload = 0;
        foreach (var handle in typeDef.GetMethods())
        {
            if (handle == target)
                return overload;
            if (reader.GetString(reader.GetMethodDefinition(handle).Name) == methodName)
                overload++;
        }
        return overload;
    }

    static bool IsSupportedClass(MetadataSource source, TypeDefinitionHandle handle)
        => source.ClassifyType((EntityHandle)handle) == TypeShapeKind.Class;

    static bool IsSupportedTargetType(MetadataSource source, TypeDefinitionHandle handle)
        => source.ClassifyType((EntityHandle)handle) is TypeShapeKind.Class or TypeShapeKind.Struct;

    static bool IsSupportedClosureRoot(MetadataReader reader, TypeDefinition typeDef)
    {
        string name = reader.GetString(typeDef.Name);
        return name is not "<Module>"
            && !name.Contains('<', StringComparison.Ordinal)
            && !IsDelegate(reader, typeDef);
    }

    static bool IsDelegate(MetadataReader reader, TypeDefinition typeDef)
    {
        if (typeDef.BaseType.IsNil)
            return false;

        string baseName = typeDef.BaseType.Kind switch
        {
            HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType)),
            HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType)),
            _ => "",
        };

        return baseName is "System.MulticastDelegate" or "System.Delegate";
    }

    static string FullName(MetadataReader reader, TypeReference type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static string FullName(MetadataReader reader, TypeDefinition type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static List<ILInstructionText>? FindAndDisassemble(
        PEReader pe,
        string fullType,
        string methodName,
        int overload)
        => FindMethodDefinition(pe, fullType, methodName, overload) is { } found
            ? MetadataInstructionProducer.Disassemble(pe, found.Reader, found.Method)
            : null;

    static (MetadataReader Reader, MethodDefinitionHandle Handle, MethodDefinition Method)? FindMethodDefinition(
        PEReader pe,
        string fullType,
        string methodName,
        int overload)
    {
        if (!pe.HasMetadata)
            return null;

        var reader = pe.GetMetadataReader();
        return FindMethodDefinition(reader, fullType, methodName, overload);
    }

    static (MetadataReader Reader, MethodDefinitionHandle Handle, MethodDefinition Method)? FindMethodDefinition(
        MetadataReader reader,
        string fullType,
        string methodName,
        int overload)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (reader.GetFullTypeName(typeDef) != fullType)
                continue;

            int seen = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (seen++ != overload)
                    continue;
                return (reader, methodHandle, method);
            }
        }

        return null;
    }

    internal static IlDiffDisplayResult? BuildIlDiffDiagnostic(
        PEReader originalPe,
        MetadataReader originalReader,
        MethodDefinitionHandle originalMethod,
        PEReader recompiledPe,
        string fullType,
        string methodName,
        int overload)
        => ToDisplayDiagnostic(BuildIlDiff(originalPe, originalReader, originalMethod, recompiledPe, fullType, methodName, overload));

    internal static IlMemberDiffResult? BuildIlDiff(
        PEReader originalPe,
        MetadataReader originalReader,
        MethodDefinitionHandle originalMethod,
        PEReader recompiledPe,
        string fullType,
        string methodName,
        int overload,
        IlBodyDiffNormalization normalization = IlBodyDiffNormalization.None)
    {
        if (originalReader.GetMethodDefinition(originalMethod).RelativeVirtualAddress == 0)
            return null;
        if (FindMethodDefinition(recompiledPe, fullType, methodName, overload) is not { } recompiled)
            return null;
        if (recompiled.Method.RelativeVirtualAddress == 0)
            return null;

        return IlAssemblyDiff.CompareMembers(
            originalPe,
            originalReader,
            originalMethod,
            recompiledPe,
            recompiled.Reader,
            recompiled.Handle,
            oldLabel: $"{fullType}::{methodName}",
            newLabel: $"{fullType}::{methodName}",
            normalization: normalization);
    }

    internal static ImplementationMemberDiffResult? BuildImplementationDiff(
        string assemblyPath,
        MetadataReader originalReader,
        MethodDefinitionHandle originalMethod,
        byte[] recompiledAssembly,
        string fullType,
        string methodName,
        int overload,
        ImplementationDiffMechanism mechanisms = ImplementationDiffMechanism.IlBody)
    {
        if (originalReader.GetMethodDefinition(originalMethod).RelativeVirtualAddress == 0)
            return null;

        var recompiledReference = new ResolvedAssemblyReference(
            new AssemblyReferenceIdentity("return-to-sender", Version: null, Culture: null, PublicKeyToken: null),
            Path: null,
            OpenRead: () => new MemoryStream(recompiledAssembly, writable: false),
            Provenance: "ReturnToSender");
        var resolver = MetadataSource.DefaultAssemblyReferenceResolver(assemblyPath);
        using var originalSource = MetadataSource.OpenWithoutSymbols(assemblyPath);
        using var recompiledSource = MetadataSource.OpenWithoutSymbols(recompiledReference, resolver);
        if (FindMethodDefinition(recompiledSource.Reader, fullType, methodName, overload) is not { } recompiled)
            return null;
        if (recompiled.Method.RelativeVirtualAddress == 0)
            return null;

        return ImplementationDiff.CompareMembers(
            originalSource,
            originalMethod,
            recompiledSource,
            recompiled.Handle,
            mechanisms);
    }

    static IlDiffDisplayResult? ToDisplayDiagnostic(IlMemberDiffResult? diff)
    {
        if (diff is null)
            return null;
        var display = IlDiffPrinter.ToDisplayResult(diff.Diff);
        return display.IsEmpty ? null : display;
    }

    internal static IEnumerable<MetadataReference> CompilationReferences(string targetPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
        {
            ExcludeTargetAssembly = true,
        });

        foreach (var dependency in resolver.ResolveAll())
        {
            if (!ManagedReferenceFilter.IsManagedAssembly(dependency.Path))
                continue;

            string simpleName = Path.GetFileNameWithoutExtension(dependency.Path);
            if (!seen.Add(simpleName))
                continue;

            MetadataReference reference;
            try
            {
                reference = MetadataReference.CreateFromFile(dependency.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException)
            {
                continue;
            }

            yield return reference;
        }
    }

    static string? FormatDiagnostic(Diagnostic? diagnostic)
    {
        if (diagnostic is null)
            return null;

        string message = diagnostic.GetMessage();
        return string.IsNullOrWhiteSpace(message)
            ? diagnostic.Id
            : $"{diagnostic.Id}: {message}";
    }

    internal static FaultIsolationResult? TryIsolateRecompileFailure(
        ArtifactRequest request,
        string decompiledSource,
        ImmutableArray<Diagnostic> decompiledDiagnostics,
        bool spanAttributionEligible,
        ReturnToSenderSourceIndex? sourceIndex,
        CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions,
        IReadOnlyList<MetadataReference> references)
    {
        if (sourceIndex is null)
            return null;

        string? signature = string.IsNullOrWhiteSpace(request.SignatureText)
            ? null
            : request.SignatureText;
        if (!sourceIndex.TryFind(
                new RequestedTarget(request.FullType, request.MethodName, request.Overload, signature),
                out var sourceMember)
            || sourceMember.Body is not { } authoredBody)
        {
            return null;
        }

        var authoredTargetBody = new ProductTargetBody(authoredBody, []);
        try
        {
            var authoredArtifact = CompileBackSourceComposer.Compose(WithTargetBody(request, authoredTargetBody));
            var tree = CSharpSyntaxTree.ParseText(authoredArtifact.Source, parseOptions);
            var compilation = CSharpCompilation.Create("return-to-sender-source-oracle", [tree], references, compileOptions);
            using var ms = new MemoryStream();
            var emit = compilation.Emit(ms);
            if (emit.Success)
            {
                return new FaultIsolationResult(
                    FaultIsolationKind.BodyDefect,
                    sourceMember.SourcePath,
                    "authored body compiled in the same RTS shell");
            }

            // Shell is broken: the substitution control is blind. Recover the
            // additional signal by span attribution — but only credit a body
            // defect for a provably shell-independent in-body error (syntax or
            // body-intrinsic semantic), and only when the diagnostics match the
            // decompiled source (not the IterationBudget path, where the returned
            // source and diagnostics come from different compose iterations).
            if (spanAttributionEligible
                && BuildTargetIdentity(request) is { } identity
                && SpanAttribution.IsolatingBodyError(
                    decompiledSource,
                    decompiledDiagnostics,
                    authoredArtifact.Source,
                    emit.Diagnostics,
                    identity) is { } isolatingError)
            {
                return new FaultIsolationResult(
                    FaultIsolationKind.BodyDefect,
                    sourceMember.SourcePath,
                    $"span-measured: shell-independent decompiled body error absent from authored body ({FormatDiagnostic(isolatingError)})")
                {
                    Method = FaultIsolationMethod.SpanMeasured,
                };
            }

            var error = emit.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            return new FaultIsolationResult(
                FaultIsolationKind.ShellOrClosureDefect,
                sourceMember.SourcePath,
                FormatDiagnostic(error));
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    static SpanAttribution.TargetIdentity? BuildTargetIdentity(ArtifactRequest request)
    {
        int parameterCount = request.Function.Signature.Parameters.Length;
        var kind = request switch
        {
            PropertyGetterArtifactRequest => SpanAttribution.TargetMemberKind.PropertyGet,
            PropertySetterArtifactRequest => SpanAttribution.TargetMemberKind.PropertySet,
            EventAccessorArtifactRequest when request.MethodName.StartsWith("add_", StringComparison.Ordinal)
                => SpanAttribution.TargetMemberKind.EventAdd,
            EventAccessorArtifactRequest when request.MethodName.StartsWith("remove_", StringComparison.Ordinal)
                => SpanAttribution.TargetMemberKind.EventRemove,
            EventAccessorArtifactRequest => (SpanAttribution.TargetMemberKind?)null,
            MethodArtifactRequest when request.MethodName is ".ctor" or ".cctor"
                => SpanAttribution.TargetMemberKind.Constructor,
            MethodArtifactRequest => SpanAttribution.TargetMemberKind.Method,
            _ => null,
        };

        return kind is { } memberKind
            ? new SpanAttribution.TargetIdentity(request.FullType, request.MethodName, parameterCount, memberKind)
            : null;
    }

    internal static ArtifactRequest WithTargetBody(ArtifactRequest request, ProductTargetBody targetBody)
        => request switch
        {
            MethodArtifactRequest method => method with { TargetBody = targetBody },
            PropertyGetterArtifactRequest getter => getter with { TargetBody = targetBody },
            PropertySetterArtifactRequest setter => setter with { TargetBody = targetBody },
            EventAccessorArtifactRequest eventAccessor => eventAccessor with { TargetBody = targetBody },
            _ => throw new ArgumentException($"Unknown artifact request type '{request.GetType().FullName}'.", nameof(request)),
        };

    internal static ArtifactRequest WithReader(ArtifactRequest request, MetadataReader reader)
        => request switch
        {
            MethodArtifactRequest method => method with { Reader = reader },
            PropertyGetterArtifactRequest getter => getter with { Reader = reader },
            PropertySetterArtifactRequest setter => setter with { Reader = reader },
            EventAccessorArtifactRequest eventAccessor => eventAccessor with { Reader = reader },
            _ => throw new ArgumentException($"Unknown artifact request type '{request.GetType().FullName}'.", nameof(request)),
        };

    static int EffectiveClosureRootCount(ProductArtifact artifact, IReadOnlySet<TypeDefinitionHandle> oracleClosureRoots)
    {
        int count = artifact.ClosureRoots.Count;
        foreach (var root in oracleClosureRoots)
        {
            if (!artifact.ClosureRoots.Contains(root))
                count++;
        }

        return count;
    }

    static string CanonicalOpcode(string op)
        => HarnessOpcode.Canonicalize(op);

    sealed record ClosureIndex(
        Dictionary<string, List<TypeDefinitionHandle>> Types,
        Dictionary<string, List<TypeDefinitionHandle>> FullTypes,
        Dictionary<string, List<TypeDefinitionHandle>> Methods,
        Dictionary<string, List<TypeDefinitionHandle>> Properties,
        Dictionary<string, List<TypeDefinitionHandle>> Fields,
        Dictionary<string, List<TypeDefinitionHandle>> Namespaces,
        Dictionary<TypeDefinitionHandle, string> RootNamespaces);

    static void AddClosureFact(
        Dictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        TypeDefinitionHandle root,
        CompileBackFact fact)
    {
        if (!closureFacts.TryGetValue(root, out var facts))
            closureFacts[root] = facts = [];
        if (!facts.Contains(fact))
            facts.Add(fact);
    }

    static ClosureIndex ClosureIndexes(MetadataReader reader)
    {
        var types = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var fullTypes = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var methods = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var properties = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var fields = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var namespaces = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var rootNamespaces = new Dictionary<TypeDefinitionHandle, string>();

        static void Add(Dictionary<string, List<TypeDefinitionHandle>> index, string key, TypeDefinitionHandle root)
        {
            if (key.Length == 0)
                return;
            if (!index.TryGetValue(key, out var list))
                index[key] = list = [];
            if (!list.Contains(root))
                list.Add(root);
        }

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (!IsSupportedClosureRoot(reader, typeDef))
                continue;

            var root = TopLevelRootOf(reader, handle);
            var identity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            rootNamespaces.TryAdd(root, identity.Namespace);
            Add(types, NormalizeTypeName(reader.GetString(typeDef.Name)), root);
            Add(fullTypes, identity.FullName, root);
            if (typeDef.GetDeclaringType().IsNil)
                Add(namespaces, reader.GetString(typeDef.Namespace), handle);
            foreach (var methodHandle in typeDef.GetMethods())
                Add(methods, reader.GetString(reader.GetMethodDefinition(methodHandle).Name), root);
            foreach (var propertyHandle in typeDef.GetProperties())
                Add(properties, reader.GetString(reader.GetPropertyDefinition(propertyHandle).Name), root);
            foreach (var fieldHandle in typeDef.GetFields())
                Add(fields, reader.GetString(reader.GetFieldDefinition(fieldHandle).Name), root);
        }

        return new ClosureIndex(types, fullTypes, methods, properties, fields, namespaces, rootNamespaces);
    }

    readonly record struct ClosureGrowth(bool Grew, IReadOnlyList<string> UnextractedDiagnosticIds);

    static ClosureGrowth AddClosureRoots(
        IReadOnlyList<Diagnostic> diagnostics,
        SemanticModel semanticModel,
        ClosureIndex indexes,
        string targetNamespace,
        TypeDefinitionHandle preferredRoot,
        HashSet<TypeDefinitionHandle> closureRoots,
        Dictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts)
    {
        bool grew = false;
        var unextractedDiagnosticIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var diagnostic in diagnostics)
        {
            var reference = ClosureDiagnosticEvidence.Extract(diagnostic, semanticModel);
            if (reference is null)
            {
                if (ClosureDiagnosticEvidence.Supports(diagnostic.Id))
                    unextractedDiagnosticIds.Add(diagnostic.Id);
                continue;
            }

            if (diagnostic.Id is "CS0246" or "CS0234" or "CS0103" or "CS0122")
            {
                if (reference.ContainingType is { } containingType)
                {
                    grew |= AddTypeRoots(diagnostic, containingType, $"{containingType}.{reference.Name}", addMemberSurfaceFact: true);
                }
                else
                {
                    grew |= AddTypeRoots(diagnostic, reference.Name, reference.Name, addMemberSurfaceFact: false);
                }

                if (diagnostic.Id is "CS0103")
                {
                    grew |= AddRoots(indexes, indexes.Methods, NormalizeTypeName(reference.Name), diagnostic, reference.Name, targetNamespace, preferredRoot, addMemberSurfaceFact: true, closureRoots, closureFacts);
                    grew |= AddRoots(indexes, indexes.Properties, NormalizeTypeName(reference.Name), diagnostic, reference.Name, targetNamespace, preferredRoot, addMemberSurfaceFact: true, closureRoots, closureFacts);
                    grew |= AddRoots(indexes, indexes.Fields, reference.Name, diagnostic, reference.Name, targetNamespace, preferredRoot, addMemberSurfaceFact: true, closureRoots, closureFacts);
                }

                if (diagnostic.Id is "CS0234"
                    && reference.ContainingNamespace is { Length: > 0 } containingNamespace)
                {
                    string metadataFullName = $"{containingNamespace}.{reference.Name}";
                    string displayNamespace = CSharpFormatter.EscapeNamespace(containingNamespace);
                    string displayName = CSharpIdentifier.Sanitize(reference.Name);
                    string displayFullName = $"{displayNamespace}.{displayName}";
                    grew |= AddRoots(indexes, indexes.FullTypes, displayFullName, diagnostic, metadataFullName, targetNamespace, preferredRoot, addMemberSurfaceFact: false, closureRoots, closureFacts);
                    grew |= AddRoots(indexes, indexes.Namespaces, metadataFullName, diagnostic, metadataFullName, targetNamespace, preferredRoot, addMemberSurfaceFact: false, closureRoots, closureFacts);
                }
            }
            else if ((diagnostic.Id is "CS1061" or "CS0117")
                     && reference.ContainingType is { } containingType)
            {
                grew |= AddTypeRoots(diagnostic, containingType, $"{containingType}.{reference.Name}", addMemberSurfaceFact: true);
            }
        }

        return new ClosureGrowth(
            grew,
            unextractedDiagnosticIds.Order(StringComparer.Ordinal).ToArray());

        bool AddTypeRoots(Diagnostic diagnostic, string typeName, string detail, bool addMemberSurfaceFact)
        {
            bool changed = false;
            bool resolved = false;
            if (typeName.Contains('.', StringComparison.Ordinal))
            {
                changed |= AddRoots(
                    indexes,
                    indexes.FullTypes,
                    typeName,
                    diagnostic,
                    detail,
                    targetNamespace,
                    preferredRoot,
                    addMemberSurfaceFact,
                    closureRoots,
                    closureFacts,
                    out resolved);
            }

            if (!resolved)
            {
                changed |= AddRoots(
                    indexes,
                    indexes.Types,
                    NormalizeTypeName(typeName),
                    diagnostic,
                    detail,
                    targetNamespace,
                    preferredRoot,
                    addMemberSurfaceFact,
                    closureRoots,
                    closureFacts);
            }

            return changed;
        }
    }

    static bool AddRoots(
        ClosureIndex indexes,
        IReadOnlyDictionary<string, List<TypeDefinitionHandle>> index,
        string key,
        Diagnostic diagnostic,
        string detail,
        string targetNamespace,
        TypeDefinitionHandle preferredRoot,
        bool addMemberSurfaceFact,
        HashSet<TypeDefinitionHandle> closureRoots,
        Dictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts)
        => AddRoots(
            indexes,
            index,
            key,
            diagnostic,
            detail,
            targetNamespace,
            preferredRoot,
            addMemberSurfaceFact,
            closureRoots,
            closureFacts,
            out _);

    static bool AddRoots(
        ClosureIndex indexes,
        IReadOnlyDictionary<string, List<TypeDefinitionHandle>> index,
        string key,
        Diagnostic diagnostic,
        string detail,
        string targetNamespace,
        TypeDefinitionHandle preferredRoot,
        bool addMemberSurfaceFact,
        HashSet<TypeDefinitionHandle> closureRoots,
        Dictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts,
        out bool resolved)
    {
        resolved = false;
        if (!index.TryGetValue(key, out var roots))
            return false;

        if (!key.Contains('.', StringComparison.Ordinal) && roots.Count > 1)
        {
            if (roots.Contains(preferredRoot))
            {
                roots = [preferredRoot];
            }
            else
            {
                var sameNamespace = roots
                .Where(root => indexes.RootNamespaces.TryGetValue(root, out var ns) && ns == targetNamespace)
                .ToList();
                if (sameNamespace.Count == 1)
                    roots = sameNamespace;
                else
                    return false;
            }
        }

        resolved = true;
        bool changed = false;
        foreach (var root in roots)
        {
            bool addedRoot = closureRoots.Add(root);
            changed |= addedRoot;

            if (!closureFacts.TryGetValue(root, out var facts))
                closureFacts[root] = facts = [];
            var rootFact = new CompileBackFact("roslyn", "closure-root", $"{diagnostic.Id}: {detail}");
            if (addedRoot && !facts.Contains(rootFact))
            {
                facts.Add(rootFact);
                changed = true;
            }
            if (addMemberSurfaceFact)
            {
                var memberFact = new CompileBackFact("roslyn", "closure-member", $"{diagnostic.Id}: {detail}");
                if (!facts.Contains(memberFact))
                {
                    facts.Add(memberFact);
                    changed = true;
                }
            }
        }

        return changed;
    }

    static TypeDefinitionHandle TopLevelRootOf(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var declaring = reader.GetTypeDefinition(handle).GetDeclaringType();
        return declaring.IsNil ? handle : TopLevelRootOf(reader, declaring);
    }

    static string NormalizeTypeName(string name)
        => ClosureDiagnosticEvidence.NormalizeTypeName(name);

}
