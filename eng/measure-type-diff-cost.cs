#:project ../src/DotnetInspector.ResearchQueries/DotnetInspector.ResearchQueries.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj
#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property IsPublishable=true
#:property PublishAot=true
#:property OptimizationPreference=Speed
#:property InvariantGlobalization=true
#:property EnablePreviewFeatures=true
#:property NoWarn=CA2252

// SPIKE (exp/type-diff-cost). Measures, per Type, the cost of each Type diff
// "gear" over one Library image pair whose images are opened once and kept
// warm:
//
//   g1  Exists: Fast Diff's own single-Type decision (FastDiff.CompareUnit)
//       over retained sides; also whole-Library FastDiff.Compare / Types.
//   g2  Type API full: AssemblyContextApiComparisonQuery.Execute with a
//       one-Type ApiTypeSelection (the `diff -t` and Inspect Web path).
//   g3  Type body full: IlBodyDiff.Compare full rows for every method pair of
//       the Type and its compiler-generated nested Types.
//   g4a Diff Findings from the g3 rows only (throw, allocation, branch, call).
//   g4b Body signals Type-scoped: BodySignalComparisonQuery with the Type
//       filter over whole-Library analyses run once per side and kept warm
//       (the analyses reject a partial method-evidence scope).
//   g5  Source: CSharpBodyDiff.CompareMembers (decompile both sides and line
//       diff) for each changed method pair.
//
// The Type population is Fast Diff's: every declared Type (top-level or
// nested), with compiler-generated nested Types attached to their nearest
// declared owner.
//
// Usage: measure-type-diff-cost <before.dll> <after.dll> <out.tsv>
//          [iterations=5] [g4bScope=all|changed|none] [g5Scope=all|changed|none]
//          [g5MaxTypes=0(unbounded)] [g4bMaxTypes=0(unbounded)]
// Requires the spike's InternalsVisibleTo on ILInspector.Metadata.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.ILDiff;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

// DIAGNOSTIC (exp/fastdiff-misses): --explain <before> <after> <type>...
// prints, per Type, Fast Diff's states and first differences, every method
// on one side only, and the canonical IL rows of every changed pair.
if (args is ["--explain", var explainBefore, var explainAfter, .. var explainTypes])
    return ExplainMisses(explainBefore, explainAfter, explainTypes);

if (args.Length is < 3 or > 8)
{
    Console.Error.WriteLine(
        "Usage: measure-type-diff-cost <before.dll> <after.dll> <out.tsv> [iterations] [g4bScope] [g5Scope] [g5MaxTypes] [g4bMaxTypes]");
    return 2;
}

string beforePath = Path.GetFullPath(args[0]);
string afterPath = Path.GetFullPath(args[1]);
string outPath = args[2];
int iterations = args.Length > 3 ? int.Parse(args[3]) : 5;
string g4bScope = args.Length > 4 ? args[4] : "all";
string g5Scope = args.Length > 5 ? args[5] : "all";
int g5MaxTypes = args.Length > 6 ? int.Parse(args[6]) : 0;
int g4bMaxTypes = args.Length > 7 ? int.Parse(args[7]) : 0;

const IlBodyDiffNormalization Normalization =
    IlBodyDiffNormalization.NormalizeCurrentAssemblyScope
    | IlBodyDiffNormalization.NormalizePlatformAssemblyScope;
var limits = new ApiSurfaceProjectionLimits(1, 1_000_000, 1_000_000, 1_000, 1_000_000, 10_000_000);
const LibraryBodyAnalysisFeatures SignalFeatures =
    LibraryBodyAnalysisFeatures.MethodEvidence
    | LibraryBodyAnalysisFeatures.Allocations
    | LibraryBodyAnalysisFeatures.OptimizationOpportunities;

static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

// One cold call, then n timed calls; returns (first, median).
static (double First, double Median) Time(int n, Action action)
{
    long t = Stopwatch.GetTimestamp();
    action();
    double first = Ms(t);
    if (n <= 0)
        return (first, first);
    var values = new double[n];
    for (int i = 0; i < n; i++)
    {
        t = Stopwatch.GetTimestamp();
        action();
        values[i] = Ms(t);
    }
    Array.Sort(values);
    return (first, values[n / 2]);
}

// ------------------------------------------------------------------ open once
long setupStart = Stopwatch.GetTimestamp();
byte[] beforeBytes = File.ReadAllBytes(beforePath);
byte[] afterBytes = File.ReadAllBytes(afterPath);
ImmutableArray<byte> beforeImage = [.. beforeBytes];
ImmutableArray<byte> afterImage = [.. afterBytes];
using var beforePe = new PEReader(beforeImage);
using var afterPe = new PEReader(afterImage);
MetadataReader beforeMd = beforePe.GetMetadataReader();
MetadataReader afterMd = afterPe.GetMetadataReader();

var sideA = new FastDiff.Side(beforePe);
var sideB = new FastDiff.Side(afterPe);
var unitsA = sideA.Units();
var unitsB = sideB.Units();
// A separate pair of sides for g3 pairing, so g1's warm key caches are not
// shared with it.
var pairA = new FastDiff.Side(beforePe);
var pairB = new FastDiff.Side(afterPe);

ResolvedAssemblyReference beforeAssembly = ResolvedAssemblyReference.CreateFromPath(
    beforePath, AssemblyResolutionProvenance.Local("type diff cost"));
ResolvedAssemblyReference afterAssembly = ResolvedAssemblyReference.CreateFromPath(
    afterPath, AssemblyResolutionProvenance.Local("type diff cost"));
AssemblyContextParticipant Participant(ResolvedAssemblyReference assembly, string path) =>
    new(assembly, new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(path)
    {
        PreferImplementationAssemblies = true,
        AllowPlatformAssemblyVersionRollForward = true,
    }));
AssemblyContextParticipant beforeParticipant = Participant(beforeAssembly, beforePath);
AssemblyContextParticipant afterParticipant = Participant(afterAssembly, afterPath);
await using var workspace = new InspectionWorkspace();
using AssemblyContextGroup beforeGroup = workspace.CreateAssemblyContextGroup([beforeParticipant]);
using AssemblyContextGroup afterGroup = workspace.CreateAssemblyContextGroup([afterParticipant]);

IAssemblyReferenceResolver beforeResolver = MetadataSource.DefaultAssemblyReferenceResolver(beforePath);
IAssemblyReferenceResolver afterResolver = MetadataSource.DefaultAssemblyReferenceResolver(afterPath);
using MetadataSource beforeSource = MetadataSource.OpenWithoutSymbols(beforePath);
using MetadataSource afterSource = MetadataSource.OpenWithoutSymbols(afterPath);
double setupMs = Ms(setupStart);

Console.WriteLine($"pair\t{beforePath}\n\t{afterPath}");
Console.WriteLine($"setup\tms={setupMs:F1}\tunitsBefore={unitsA.Count}\tunitsAfter={unitsB.Count}");

// --------------------------------------------------- whole-Library baselines
int wholeIters = Math.Max(1, Math.Min(iterations, 3));
FastDiffResult fast = null!;
var (fastFirst, fastMedian) = Time(iterations, () =>
{
    using var b = new PEReader(beforeImage);
    using var a = new PEReader(afterImage);
    fast = FastDiff.Compare(b, a);
});
Console.WriteLine(
    $"whole-fastdiff\tfirst={fastFirst:F2}\tmedian={fastMedian:F2}\ttypes={fast.Types.Length}"
    + $"\tperType={fastMedian / Math.Max(1, fast.Types.Length):F4}");

AssemblyContextApiComparisonResult wholeApi = null!;
var (wholeApiFirst, wholeApiMedian) = Time(wholeIters, () =>
    wholeApi = AssemblyContextApiComparisonQuery.Execute(
        beforeGroup, beforeParticipant, afterGroup, afterParticipant, ApiSurfaceScope.Public, limits));
Console.WriteLine(
    $"whole-api\tfirst={wholeApiFirst:F2}\tmedian={wholeApiMedian:F2}\tcomplete={wholeApi.IsComplete}"
    + $"\texact={wholeApi.IsExact}");

var (wholeCoreFirst, wholeCoreMedian) = Time(wholeIters, () => MetadataFindings.CompareApi(
    ApiSurfaceExtractor.ExtractDeclarations(beforePe, ApiSurfaceExtractionScope.Public),
    ApiSurfaceExtractor.ExtractDeclarations(afterPe, ApiSurfaceExtractionScope.Public),
    new FindingSubject("api", "API"), new ApiDiffOptions(ApiDiffScope.Signature)));
Console.WriteLine($"whole-api-core\tfirst={wholeCoreFirst:F2}\tmedian={wholeCoreMedian:F2}");

IlAssemblyDiffResult wholeIl = null!;
var (wholeIlFirst, wholeIlMedian) = Time(wholeIters, () =>
    wholeIl = IlAssemblyDiff.Compare(beforePe, beforeMd, afterPe, afterMd, maxExamples: int.MaxValue, Normalization));
Console.WriteLine(
    $"whole-il\tfirst={wholeIlFirst:F2}\tmedian={wholeIlMedian:F2}\tcompared={wholeIl.ComparedBodyCount}"
    + $"\tchanged={wholeIl.ChangedBodyCount}");

BodySignalComparisonResult? wholeSignals = null;
string wholeSignalsNote = "ok";
double wholeSignalsFirst = double.NaN, wholeSignalsMedian = double.NaN;
double analysisFirst = double.NaN, analysisMedian = double.NaN;
BodySignalComparisonBinding[] signalBefore = [], signalAfter = [];
if (g4bScope != "none")
{
    try
    {
        // Analysis requires the full method-evidence scope, so it runs once
        // per side over the whole Library and stays warm; only the comparison
        // can be Type-scoped (TypeFilters).
        (analysisFirst, analysisMedian) = Time(Math.Min(1, wholeIters), () =>
        {
            var execB = LibraryBodyAnalysisService.ExecuteImage(
                beforePath, beforeImage, LibraryBodyAnalysisRequest.Create(SignalFeatures), beforeResolver);
            var execA = LibraryBodyAnalysisService.ExecuteImage(
                afterPath, afterImage, LibraryBodyAnalysisRequest.Create(SignalFeatures), afterResolver);
            signalBefore = [new(beforeAssembly, beforeResolver, Signals(execB))];
            signalAfter = [new(afterAssembly, afterResolver, Signals(execA))];
        });
        (wholeSignalsFirst, wholeSignalsMedian) = Time(Math.Min(1, wholeIters), () =>
            wholeSignals = BodySignalComparisonQuery.Execute(new(signalBefore, signalAfter)));
    }
    catch (Exception ex)
    {
        wholeSignalsNote = ex.GetType().Name + ": " + ex.Message.ReplaceLineEndings(" ");
    }
}
Console.WriteLine($"whole-signal-analysis\tfirst={analysisFirst:F2}\tmedian={analysisMedian:F2}");
Console.WriteLine(
    $"whole-signals\tfirst={wholeSignalsFirst:F2}\tmedian={wholeSignalsMedian:F2}"
    + $"\tresult={wholeSignals?.GetType().Name}\tnote={wholeSignalsNote}");

// ------------------------------------------------------------- per Type
using var output = new StreamWriter(outPath);
output.WriteLine(string.Join('\t',
    "type", "apiMembers", "bodies", "memberCount", "fastApi", "fastBody", "apiChanged", "bodyChanged",
    "methodPairs", "changedMethods", "oneSidedMethods", "ilRows", "selectedTypes", "fullProjection",
    "g1First", "g1", "g2First", "g2", "g2coreFirst", "g2core", "coreChanged", "g3First", "g3", "g4aFirst", "g4a", "g4bFirst", "g4b", "g4bNote",
    "g5First", "g5", "g5Methods", "g5Rows", "findings"));

var fastByName = new Dictionary<string, FastDiffTypeState>(StringComparer.Ordinal);
foreach (FastDiffTypeState state in fast.Types)
    fastByName.TryAdd(state.FullName, state);

int index = 0;
int g5Types = 0;
int g4bErrors = 0;
int g4bTypes = 0;
int wholeAfterTypes = wholeApi.After.Surface?.Types.Count ?? int.MaxValue;
Console.WriteLine($"whole-api-types\tbefore={wholeApi.Before.Surface?.Types.Count}\tafter={wholeAfterTypes}");
int paired = unitsA.Count(pair => unitsB.ContainsKey(pair.Key));
long loopStart = Stopwatch.GetTimestamp();
foreach (var (key, unitA) in unitsA)
{
    if (!unitsB.TryGetValue(key, out var unitB) || unitA.Malformed || unitB.Malformed)
        continue;
    index++;
    string name = unitB.FullName;
    fastByName.TryGetValue(name, out FastDiffTypeState? fastState);

    // g1: single-Type Exists over retained sides.
    var work = new FastDiff.Work();
    var g1 = Time(iterations, () => FastDiff.CompareUnit(sideA, unitA, sideB, unitB, FastDiffAxes.ApiAndBody, work));

    // g2: Type API full.
    AssemblyContextApiComparisonResult api = null!;
    var selection = new ApiTypeSelection(candidate => candidate == name);
    var g2 = Time(iterations, () => api = AssemblyContextApiComparisonQuery.Execute(
        beforeGroup, beforeParticipant, afterGroup, afterParticipant, ApiSurfaceScope.Public, limits, selection));
    int selectedTypes = api.After.Surface?.Types.Count ?? -1;
    // The selection keeps either side's extension-declaring Types, and keeps
    // the whole projection when the selected Type is one of them.
    bool fullProjection = selectedTypes >= wholeAfterTypes;
    int apiMembers = api.After.Surface?.Types.Where(type => type.FullName == name).Sum(type => type.Members.Count) ?? 0;
    // The change bit is this Type's own, not the kept extension Types'.
    bool apiChanged = api.Comparison is null || TypeApiChanged(api.Comparison, name);

    // g2core: the same comparison without the query host: declarations of
    // only this Type on each warm reader, then the Metadata-owned comparison
    // at the Signature scope ApiComparisonQuery uses.
    TypeDefinitionHandle ownerA = unitA.Owner, ownerB = unitB.Owner;
    ApiFindingComparison core = null!;
    var g2core = Time(iterations, () => core = MetadataFindings.CompareApi(
        ApiSurfaceExtractor.ExtractDeclarations(beforePe, ApiSurfaceExtractionScope.Public, handle => handle == ownerA),
        ApiSurfaceExtractor.ExtractDeclarations(afterPe, ApiSurfaceExtractionScope.Public, handle => handle == ownerB),
        new FindingSubject("api", "API"), new ApiDiffOptions(ApiDiffScope.Signature)));
    bool coreChanged = TypeApiChanged(core, name);

    // g3: Type body full.
    TypeBodyDiff body = null!;
    var g3 = Time(iterations, () => body = DiffBodies(pairA, unitA, pairB, unitB, beforePe, beforeMd, afterPe, afterMd));

    // g4a: findings from the g3 rows.
    TypeFindings findings = null!;
    var g4a = Time(iterations, () => findings = Classify(body));

    // g4b: Type-scoped body signals.
    (double First, double Median) g4b = (double.NaN, double.NaN);
    string g4bNote = "skipped";
    bool changed = apiChanged || body.Changed;
    if ((g4bScope == "all" || (g4bScope == "changed" && body.Changed))
        && (g4bMaxTypes <= 0 || g4bTypes++ < g4bMaxTypes))
    {
        try
        {
            BodySignalComparisonResult signals = null!;
            var filter = new HashSet<string>(StringComparer.Ordinal) { name };
            g4b = Time(iterations, () => signals = BodySignalComparisonQuery.Execute(
                new(signalBefore, signalAfter, filter)));
            g4bNote = signals is BodySignalComparisonResult.Compared compared
                ? $"compared:{compared.Comparison.Changes.Length}"
                : signals.GetType().Name;
        }
        catch (Exception ex)
        {
            g4bNote = "ERR " + ex.GetType().Name;
            if (g4bErrors++ < 3)
                Console.Error.WriteLine($"g4b {name}: {ex}");
        }
    }

    // g5: decompiled C# for changed method pairs.
    (double First, double Median) g5 = (double.NaN, double.NaN);
    int g5Rows = 0;
    bool runG5 = (g5Scope == "all" || (g5Scope == "changed" && body.Changed))
        && (g5MaxTypes <= 0 || g5Types < g5MaxTypes);
    if (runG5)
    {
        if (body.ChangedPairs.Count > 0)
            g5Types++;
        try
        {
            g5 = Time(Math.Min(iterations, 3), () =>
            {
                int rows = 0;
                foreach (var (before, after) in body.ChangedPairs)
                    rows += CSharpBodyDiff.CompareMembers(beforeSource, before, afterSource, after).Rows.Length;
                g5Rows = rows;
            });
        }
        catch (Exception ex)
        {
            g5Rows = -1;
            Console.Error.WriteLine($"g5 {name}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    output.WriteLine(string.Join('\t',
        name, apiMembers, body.Bodies, apiMembers + body.Bodies,
        fastState?.Api.ToString() ?? "?", fastState?.Body.ToString() ?? "?",
        apiChanged, body.Changed, body.Pairs, body.ChangedPairs.Count, body.OneSided, body.Rows,
        selectedTypes, fullProjection,
        F(g1.First), F(g1.Median), F(g2.First), F(g2.Median), F(g2core.First), F(g2core.Median), coreChanged, F(g3.First), F(g3.Median),
        F(g4a.First), F(g4a.Median), F(g4b.First), F(g4b.Median), g4bNote,
        F(g5.First), F(g5.Median), runG5 ? body.ChangedPairs.Count : 0, g5Rows, findings.Summary));
    if (index % 200 == 0)
    {
        output.Flush();
        Console.Error.WriteLine($"{index}/{paired} types, {Ms(loopStart) / 1000:F0}s");
    }
}
Console.WriteLine($"types\tpaired={index}\tloopSeconds={Ms(loopStart) / 1000:F1}");
return 0;

static bool TypeApiChanged(ApiFindingComparison comparison, string name)
{
    if (comparison.ApiDiff.TypeDiffs.Any(diff => diff.TypeFullName == name))
        return true;
    if (comparison.Types.Value is FindingComparison<ApiTypeHandle>.Complete types
        && types.Pairs.Any(pair => pair.Kind != PairKind.Present && Payload(pair).TypeFullName == name))
        return true;
    if (comparison.Members.Value is FindingComparison<ApiMemberHandle>.Complete members
        && members.Pairs.Any(pair => pair.Kind != PairKind.Present && Payload(pair).TypeFullName == name))
        return true;
    return comparison.Types.Value is not FindingComparison<ApiTypeHandle>.Complete
        || comparison.Members.Value is not FindingComparison<ApiMemberHandle>.Complete;
}

static T Payload<T>(PairFinding<T> pair) where T : notnull
    => (((IPairFinding)pair).New as Finding<T> ?? (Finding<T>)((IPairFinding)pair).Old!).Payload;

static string F(double value) => double.IsNaN(value) ? "" : value.ToString("F6");

static BodySignalAnalysisInput Signals(LibraryBodyAnalysisExecution execution) =>
    new(execution.Allocations, execution.Safety, execution.CallGraph, execution.Optimization);

static Dictionary<SymbolKey, MethodDefinitionHandle> MethodsOf(FastDiff.Side side, FastDiff.Unit unit)
{
    var methods = new Dictionary<SymbolKey, MethodDefinitionHandle>();
    void Add(TypeDefinitionHandle type)
    {
        foreach (MethodDefinitionHandle method in side.Md.GetTypeDefinition(type).GetMethods())
            methods.TryAdd(side.Key(method), method);
    }
    Add(unit.Owner);
    foreach (TypeDefinitionHandle generated in unit.Generated)
        Add(generated);
    return methods;
}

static int ExplainMisses(string beforePath, string afterPath, string[] typeNames)
{
    using var peA = new PEReader(File.OpenRead(beforePath));
    using var peB = new PEReader(File.OpenRead(afterPath));
    MetadataReader mdA = peA.GetMetadataReader(), mdB = peB.GetMetadataReader();
    var a = new FastDiff.Side(peA);
    var b = new FastDiff.Side(peB);
    var unitsA = a.Units();
    var unitsB = b.Units();
    foreach (string name in typeNames)
    {
        var unitA = unitsA.Values.FirstOrDefault(unit => unit.FullName == name);
        var unitB = unitsB.Values.FirstOrDefault(unit => unit.FullName == name);
        if (unitA is null || unitB is null)
        {
            Console.WriteLine($"## {name}: not paired");
            continue;
        }
        FastDiffTypeState state = FastDiff.CompareUnit(a, unitA, b, unitB, FastDiffAxes.ApiAndBody, new FastDiff.Work());
        var (apiWhy, bodyWhy) = FastDiff.Explain(peA, peB, name);
        Console.WriteLine($"## {name}: fastApi={state.Api} fastBody={state.Body} generatedA={unitA.Generated.Count} generatedB={unitB.Generated.Count}");
        Console.WriteLine($"   apiWhy: {apiWhy?.Replace("\n", " | ")}");
        Console.WriteLine($"   bodyWhy: {bodyWhy?.Replace("\n", " | ")}");
        var methodsA = MethodsOf(a, unitA);
        var methodsB = MethodsOf(b, unitB);
        foreach (var (key, handle) in methodsA)
        {
            if (!methodsB.ContainsKey(key))
                Console.WriteLine($"   - before only: {Describe(mdA, handle, unitA.Owner)}  key={key}");
        }
        foreach (var (key, handle) in methodsB)
        {
            if (!methodsA.TryGetValue(key, out MethodDefinitionHandle handleA))
            {
                Console.WriteLine($"   + after only:  {Describe(mdB, handle, unitB.Owner)}  key={key}");
                continue;
            }
            MethodDefinition x = mdA.GetMethodDefinition(handleA), y = mdB.GetMethodDefinition(handle);
            if ((x.RelativeVirtualAddress == 0) != (y.RelativeVirtualAddress == 0))
            {
                Console.WriteLine($"   ~ body on one side: {Describe(mdA, handleA, unitA.Owner)} -> {Describe(mdB, handle, unitB.Owner)}");
                continue;
            }
            if (x.RelativeVirtualAddress == 0)
                continue;
            IlBodyDiffResult diff = IlBodyDiff.Compare(
                mdA, peA.GetMethodBody(x.RelativeVirtualAddress),
                mdB, peB.GetMethodBody(y.RelativeVirtualAddress), Normalization);
            if (diff.IsExact)
                continue;
            Console.WriteLine($"   * changed: {Describe(mdB, handle, unitB.Owner)} outcome={diff.Outcome} failure={diff.Failure}");
            foreach (IlDiffRow row in diff.Rows)
                Console.WriteLine($"       {row.Kind} {row.Operation.OpcodeFamily} {row.Operation.Operand?.Value} :: {row.Message}");
            MethodBodyBlock bodyA = peA.GetMethodBody(x.RelativeVirtualAddress), bodyB = peB.GetMethodBody(y.RelativeVirtualAddress);
            Console.WriteLine($"       size {bodyA.Size}/{bodyB.Size} maxstack {bodyA.MaxStack}/{bodyB.MaxStack} il {System.Convert.ToHexString(bodyA.GetILBytes()!)} / {System.Convert.ToHexString(bodyB.GetILBytes()!)}");
        }
    }
    return 0;

    static string Describe(MetadataReader md, MethodDefinitionHandle handle, TypeDefinitionHandle owner)
    {
        MethodDefinition method = md.GetMethodDefinition(handle);
        TypeDefinitionHandle declaring = method.GetDeclaringType();
        string where = declaring == owner ? "" : $"[in {md.GetString(md.GetTypeDefinition(declaring).Name)}] ";
        return $"{where}{md.GetString(method.Name)} access={method.Attributes & MethodAttributes.MemberAccessMask} "
            + $"attrs={method.Attributes} impl={method.ImplAttributes} rva={(method.RelativeVirtualAddress != 0 ? "body" : "none")}";
    }
}

static TypeBodyDiff DiffBodies(
    FastDiff.Side sideA, FastDiff.Unit unitA, FastDiff.Side sideB, FastDiff.Unit unitB,
    PEReader peA, MetadataReader mdA, PEReader peB, MetadataReader mdB)
{
    var methodsA = MethodsOf(sideA, unitA);
    var methodsB = MethodsOf(sideB, unitB);
    var result = new TypeBodyDiff
    {
        TokensBefore = methodsA.Values.Select(handle => MetadataTokens.GetToken(handle)).ToHashSet(),
        TokensAfter = methodsB.Values.Select(handle => MetadataTokens.GetToken(handle)).ToHashSet(),
    };
    foreach (var (key, handleB) in methodsB)
    {
        MethodDefinition methodB = mdB.GetMethodDefinition(handleB);
        if (methodB.RelativeVirtualAddress != 0)
            result.Bodies++;
        if (!methodsA.TryGetValue(key, out MethodDefinitionHandle handleA))
        {
            if (methodB.RelativeVirtualAddress != 0)
                result.OneSided++;
            continue;
        }
        MethodDefinition methodA = mdA.GetMethodDefinition(handleA);
        if (methodA.RelativeVirtualAddress == 0 && methodB.RelativeVirtualAddress == 0)
            continue;
        if (methodA.RelativeVirtualAddress == 0 || methodB.RelativeVirtualAddress == 0)
        {
            result.OneSided++;
            continue;
        }
        result.Pairs++;
        IlBodyDiffResult diff = IlBodyDiff.Compare(
            mdA, peA.GetMethodBody(methodA.RelativeVirtualAddress),
            mdB, peB.GetMethodBody(methodB.RelativeVirtualAddress),
            Normalization);
        if (diff.IsExact)
            continue;
        result.ChangedPairs.Add((handleA, handleB));
        result.Diffs.Add(diff);
        result.Rows += diff.Rows.Length;
    }
    foreach (var (key, handleA) in methodsA)
    {
        if (!methodsB.ContainsKey(key) && mdA.GetMethodDefinition(handleA).RelativeVirtualAddress != 0)
            result.OneSided++;
    }
    return result;
}

static TypeFindings Classify(TypeBodyDiff body)
{
    var findings = new TypeFindings();
    foreach (IlBodyDiffResult diff in body.Diffs)
    {
        if (!diff.IsAvailable)
        {
            findings.Unavailable++;
            continue;
        }
        int throws = 0, branches = 0;
        var allocations = new Dictionary<string, int>(StringComparer.Ordinal);
        var calls = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (IlDiffRow row in diff.Rows)
        {
            int sign = row.Kind switch { IlDiffKind.Add => 1, IlDiffKind.Remove => -1, _ => 0 };
            if (sign == 0)
                continue;
            string family = row.Operation.OpcodeFamily;
            string operand = row.Operation.Operand?.Value ?? "";
            switch (family)
            {
                case "throw" or "rethrow":
                    throws += sign;
                    break;
                case "newobj":
                    Bump(allocations, "newobj " + operand, sign);
                    Bump(calls, operand, sign);
                    break;
                case "newarr" or "box":
                    Bump(allocations, family + " " + operand, sign);
                    break;
                case "call" or "callvirt" or "calli":
                    Bump(calls, operand, sign);
                    break;
                case "switch" or "leave":
                    branches += sign;
                    break;
                default:
                    if (family is ['b', ..] && family != "box")
                        branches += sign;
                    break;
            }
        }
        if (throws > 0) findings.ThrowAdded++;
        if (throws < 0) findings.ThrowRemoved++;
        if (branches > 0) findings.BranchAdded++;
        if (branches < 0) findings.BranchRemoved++;
        findings.AllocationAdded += allocations.Count(pair => pair.Value > 0);
        findings.AllocationRemoved += allocations.Count(pair => pair.Value < 0);
        findings.CallAdded += calls.Count(pair => pair.Value > 0);
        findings.CallRemoved += calls.Count(pair => pair.Value < 0);
    }
    return findings;

    static void Bump(Dictionary<string, int> counts, string key, int sign)
        => counts[key] = counts.GetValueOrDefault(key) + sign;
}

sealed class TypeBodyDiff
{
    public int Bodies;
    public int Pairs;
    public int OneSided;
    public int Rows;
    public List<(MethodDefinitionHandle Before, MethodDefinitionHandle After)> ChangedPairs { get; } = [];
    public List<IlBodyDiffResult> Diffs { get; } = [];
    public required HashSet<int> TokensBefore { get; init; }
    public required HashSet<int> TokensAfter { get; init; }
    public bool Changed => ChangedPairs.Count > 0 || OneSided > 0;
}

sealed class TypeFindings
{
    public int ThrowAdded, ThrowRemoved, BranchAdded, BranchRemoved;
    public int AllocationAdded, AllocationRemoved, CallAdded, CallRemoved, Unavailable;

    public string Summary =>
        $"throw+{ThrowAdded}/-{ThrowRemoved} branch+{BranchAdded}/-{BranchRemoved} "
        + $"alloc+{AllocationAdded}/-{AllocationRemoved} call+{CallAdded}/-{CallRemoved} unavail={Unavailable}";
}

