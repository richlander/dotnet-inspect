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
//   g2exact Type API over the exact-Type request path (#9466): per side, the
//       exact TypeDef binding by structured name
//       (MetadataExactTypeDefinitionInspection.Resolve) and the same-image
//       extension Exists (MetadataRelationInspection.ExecuteExtensionPresence);
//       then only that TypeDef's declarations
//       (ApiSurfaceExtractor.ExtractDeclarations, one-handle predicate) compared
//       by ApiComparisonQuery.Execute, the comparison g2 runs. When either side
//       reports a same-image extension for the Type, the receiver needs its
//       projected extension members and extension/instance pairing, so that
//       side also extracts the Types that declare an extension for it (from the
//       same-image extension relation population) with local extension
//       projection (mode "ext"). g2exact keeps one ApiSurfaceImageCache per
//       image warm for the run (the memory-safety index and attribute decode
//       state); g2exactCold builds fresh caches per request.
//       g2exactAgrees: this Type's API change bit equals g2's;
//       g2exactFindings: this Type's findings (classifier changes, Type and
//       Member pairs with kind, identity, and detail) equal g2's filtered to it.
//
// The Type population is Fast Diff's: every declared Type (top-level or
// nested), with compiler-generated nested Types attached to their nearest
// declared owner.
//
// Usage: measure-type-diff-cost <before.dll> <after.dll> <out.tsv>
//          [iterations=5] [g4bScope=all|changed|none] [g5Scope=all|changed|none]
//          [g5MaxTypes=0(unbounded)] [g4bMaxTypes=0(unbounded)]
//          [g2Sample=1(every Type)]
// g2Sample=N runs g2 (and so g2exactAgrees) on every Nth paired Type only;
// g2core and g2exact run on every Type. TYPECOST_PROFILE=Name1,Name2 prints a
// g2core/g2exact component split for those Types; TYPECOST_PROFILE_ONLY=1
// measures only those Types.
// Requires the spike's InternalsVisibleTo on ILInspector.Metadata and its
// spike-only ApiSurfaceImageCache extraction overload.

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

if (args.Length is < 3 or > 9)
{
    Console.Error.WriteLine(
        "Usage: measure-type-diff-cost <before.dll> <after.dll> <out.tsv> [iterations] [g4bScope] [g5Scope] [g5MaxTypes] [g4bMaxTypes] [g2Sample]");
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
int g2Sample = args.Length > 8 ? Math.Max(1, int.Parse(args[8])) : 1;
HashSet<string> profileTypes = new(
    (Environment.GetEnvironmentVariable("TYPECOST_PROFILE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries),
    StringComparer.Ordinal);
bool profileOnly = Environment.GetEnvironmentVariable("TYPECOST_PROFILE_ONLY") == "1";

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

// ------------------------------------------- fixed (whole-Library) costs
// Each line is one side's cost of a step that does not depend on the selected
// Type: what a one-Type g2core or g2exact pays regardless of the Type's size.
AssemblyReferenceIdentity beforeIdentity = AssemblyReferenceIdentity.FromAssemblyDefinition(beforeMd);
AssemblyReferenceIdentity afterIdentity = AssemblyReferenceIdentity.FromAssemblyDefinition(afterMd);
var beforeCache = new ApiSurfaceImageCache(beforePe);
var afterCache = new ApiSurfaceImageCache(afterPe);
var exactBounds = new ApiSurfaceExtractionBounds(1_000_000, 10_000_000, 1_000_000, 1_000_000, int.MaxValue);
foreach (var (label, pe, md) in new[] { ("before", beforePe, beforeMd), ("after", afterPe, afterMd) })
{
    int fixedIters = Math.Max(3, iterations);
    var none = Time(fixedIters, () => ApiSurfaceExtractor.ExtractDeclarations(pe, ApiSurfaceExtractionScope.Public, _ => false));
    var safety = Time(fixedIters, () => MemorySafetyMetadataIndex.Create(md));
    var register = Time(fixedIters, () => RegisterScan(md));
    var byName = Time(fixedIters, () => TypeNameIndex(md));
    var extNames = Time(fixedIters, () => ApiTypeSelection.ExtensionDeclaringTypeNames(md));
    var bind = Time(fixedIters, () => new ApiTypeSelection(_ => false).Bind(md));
    var rows = Time(fixedIters, () => MetadataOperationContext.CountMetadataRows(md));
    Console.WriteLine(
        $"fixed-{label}\textractNoType={none.Median:F3}\tmemorySafetyIndex={safety.Median:F3}"
        + $"\tregisterScan={register.Median:F3}\ttypeDefByNameIndex={byName.Median:F3}"
        + $"\textensionDeclaringTypeNames={extNames.Median:F3}\tselectionBind={bind.Median:F3}"
        + $"\tcountRows={rows.Median:F4}\ttypeDefs={md.TypeDefinitions.Count}\tmethodDefs={md.MethodDefinitions.Count}"
        + $"\tcustomAttributes={md.CustomAttributes.Count}"
        + $"\tmemorySafetyWork={MemorySafetyMetadataIndex.CreateReceipted(md).ConstructionWork}");
}

// ------------------------------------------------------------- per Type
using var output = new StreamWriter(outPath);
output.WriteLine(string.Join('\t',
    "type", "apiMembers", "bodies", "memberCount", "fastApi", "fastBody", "apiChanged", "bodyChanged",
    "methodPairs", "changedMethods", "oneSidedMethods", "ilRows", "selectedTypes", "fullProjection",
    "g1First", "g1", "g2First", "g2", "g2coreFirst", "g2core", "coreChanged",
    "g2coreExtract", "g2coreCompare", "g2exactFirst", "g2exact", "g2exactCold", "g2exactMode", "g2exactResolve", "g2exactExists",
    "g2exactExtract", "g2exactCompare", "g2exactChanged", "g2exactAgrees", "g2exactFindings", "g3First", "g3", "g4aFirst", "g4a", "g4bFirst", "g4b", "g4bNote",
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
    if (profileOnly && !profileTypes.Contains(unitB.FullName))
        continue;
    index++;
    string name = unitB.FullName;
    fastByName.TryGetValue(name, out FastDiffTypeState? fastState);

    // g1: single-Type Exists over retained sides.
    var work = new FastDiff.Work();
    var g1 = Time(iterations, () => FastDiff.CompareUnit(sideA, unitA, sideB, unitB, FastDiffAxes.ApiAndBody, work));

    // g2: Type API full (every g2Sample-th Type).
    bool runG2 = (index - 1) % g2Sample == 0;
    AssemblyContextApiComparisonResult? api = null;
    var selection = new ApiTypeSelection(candidate => candidate == name);
    (double First, double Median) g2 = (double.NaN, double.NaN);
    if (runG2)
    {
        g2 = Time(iterations, () => api = AssemblyContextApiComparisonQuery.Execute(
            beforeGroup, beforeParticipant, afterGroup, afterParticipant, ApiSurfaceScope.Public, limits, selection));
    }
    int selectedTypes = api?.After.Surface?.Types.Count ?? -1;
    // The selection keeps either side's extension-declaring Types, and keeps
    // the whole projection when the selected Type is one of them.
    bool fullProjection = selectedTypes >= wholeAfterTypes;
    // The change bit is this Type's own, not the kept extension Types'.
    bool? g2Changed = api is null ? null : api.Comparison is null || TypeApiChanged(api.Comparison, name);

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
    // g2core split: both extractions, then the comparison alone.
    ApiSurface coreA = null!, coreB = null!;
    var g2coreExtract = Time(iterations, () =>
    {
        coreA = ApiSurfaceExtractor.ExtractDeclarations(beforePe, ApiSurfaceExtractionScope.Public, handle => handle == ownerA);
        coreB = ApiSurfaceExtractor.ExtractDeclarations(afterPe, ApiSurfaceExtractionScope.Public, handle => handle == ownerB);
    });
    var g2coreCompare = Time(iterations, () => MetadataFindings.CompareApi(
        coreA, coreB, new FindingSubject("api", "API"), new ApiDiffOptions(ApiDiffScope.Signature)));

    // g2exact: the exact-Type request path. The structured name stands in for
    // the parsed request and is read once, untimed.
    MetadataTypeDefinitionName exactName =
        ((MetadataTypeDefinitionNameReadResult.Read)MetadataTypeDefinitionName.Read(afterMd, ownerB)).Name;
    ExactTypeApi exact = null!;
    // Warm: one extraction cache per image for the whole run, as a session
    // that keeps the images open keeps them.
    var g2exact = Time(iterations, () => exact = ExactTypeApi.Run(
        beforePe, beforeMd, beforeIdentity, beforeCache, afterPe, afterMd, afterIdentity, afterCache, exactName, exactBounds));
    // Cold: a fresh extraction cache per request, as one cold request pays.
    var g2exactCold = Time(iterations, () => ExactTypeApi.Run(
        beforePe, beforeMd, beforeIdentity, new ApiSurfaceImageCache(beforePe),
        afterPe, afterMd, afterIdentity, new ApiSurfaceImageCache(afterPe), exactName, exactBounds));
    // g2exact split (warm): resolve both, Exists both, extract both, compare.
    TypeDefinitionHandle exactA = default, exactB = default;
    var g2exactResolve = Time(iterations, () =>
    {
        exactA = ExactTypeApi.Resolve(beforeMd, exactName, exactBounds);
        exactB = ExactTypeApi.Resolve(afterMd, exactName, exactBounds);
    });
    var g2exactExists = Time(iterations, () =>
    {
        ExactTypeApi.ExtensionExists(beforePe, beforeMd, beforeIdentity, exactName, exactA);
        ExactTypeApi.ExtensionExists(afterPe, afterMd, afterIdentity, exactName, exactB);
    });
    ApiSurface exactSurfaceA = null!, exactSurfaceB = null!;
    var g2exactExtract = Time(iterations, () =>
    {
        exactSurfaceA = ExactTypeApi.Extract(beforePe, beforeMd, beforeIdentity, beforeCache, exactName, exactA, exact.ExtensionMode);
        exactSurfaceB = ExactTypeApi.Extract(afterPe, afterMd, afterIdentity, afterCache, exactName, exactB, exact.ExtensionMode);
    });
    var g2exactCompare = Time(iterations, () => ApiComparisonQuery.Execute(exactSurfaceA, exactSurfaceB));
    bool exactChanged = TypeApiChanged(exact.Comparison, name);
    string g2exactAgrees = g2Changed is null ? "" : (g2Changed == exactChanged).ToString();
    string g2exactFindings = api?.Comparison is null
        ? ""
        : FindingsOf(api.Comparison, name).SequenceEqual(FindingsOf(exact.Comparison, name)).ToString();
    if (g2exactFindings == "False" || g2exactAgrees == "False")
    {
        Console.Error.WriteLine($"g2exact disagrees {name}: mode={exact.Mode} g2Changed={g2Changed} exactChanged={exactChanged}");
        foreach (string line in FindingsOf(api!.Comparison!, name).Except(FindingsOf(exact.Comparison, name)))
            Console.Error.WriteLine("  g2 only:    " + line);
        foreach (string line in FindingsOf(exact.Comparison, name).Except(FindingsOf(api.Comparison!, name)))
            Console.Error.WriteLine("  exact only: " + line);
    }
    int apiMembers = exact.After.Types.Where(type => type.FullName == name).Sum(type => type.Members.Count);
    bool apiChanged = g2Changed ?? exactChanged;
    if (profileTypes.Contains(name))
    {
        Console.WriteLine(
            $"profile\t{name}\tapiMembers={apiMembers}\tg2={F(g2.Median)}\tg2core={g2core.Median:F3}"
            + $"\tg2coreExtract={g2coreExtract.Median:F3}\tg2coreCompare={g2coreCompare.Median:F3}"
            + $"\tg2exact={g2exact.Median:F3}\tg2exactCold={g2exactCold.Median:F3}\tresolve={g2exactResolve.Median:F3}\texists={g2exactExists.Median:F3}"
            + $"\textract={g2exactExtract.Median:F3}\tcompare={g2exactCompare.Median:F3}\tmode={exact.Mode}");
    }

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
        F(g1.First), F(g1.Median), F(g2.First), F(g2.Median), F(g2core.First), F(g2core.Median), coreChanged,
        F(g2coreExtract.Median), F(g2coreCompare.Median), F(g2exact.First), F(g2exact.Median), F(g2exactCold.Median), exact.Mode,
        F(g2exactResolve.Median), F(g2exactExists.Median), F(g2exactExtract.Median), F(g2exactCompare.Median),
        exactChanged, g2exactAgrees, g2exactFindings, F(g3.First), F(g3.Median),
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

// The ApiSurfaceExtractor registration pre-pass (ApiSurfaceExtractor.cs, the
// MethodDefinitions walk for __Register_): one whole MethodDef table walk.
static int RegisterScan(MetadataReader md)
{
    int count = 0;
    foreach (MethodDefinitionHandle handle in md.MethodDefinitions)
    {
        MethodDefinition method = md.GetMethodDefinition(handle);
        if ((method.Attributes & (MethodAttributes.MemberAccessMask | MethodAttributes.Static))
                != (MethodAttributes.Private | MethodAttributes.Static)
            || !md.StringComparer.Equals(method.Name, "__Register_"))
        {
            continue;
        }
        count++;
    }
    return count;
}

// Approximates the attribute decoder's lazy TypeDef-by-full-name index
// (CustomAttributeValueDecoder.BuildTypeDefinitionIndex), built the first time
// an extraction decodes an enum-typed attribute argument.
static int TypeNameIndex(MetadataReader md)
{
    var names = new Dictionary<string, TypeDefinitionHandle>(md.TypeDefinitions.Count, StringComparer.Ordinal);
    foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
    {
        if (md.ResolveFullTypeName(handle) is RelationshipTraversalResult<string>.Completed { Value: var name })
            names.TryAdd(name, handle);
    }
    return names.Count;
}

// This Type's findings: the classifier's Type changes, then every Type and
// Member pair (kind, identity, detail) whose payload belongs to the Type.
static List<string> FindingsOf(ApiFindingComparison comparison, string name)
{
    var lines = new List<string>();
    foreach (TypeDiff diff in comparison.ApiDiff.TypeDiffs.Where(diff => diff.TypeFullName == name))
    {
        foreach (ApiChange change in diff.Changes)
            lines.Add($"D|{change.Kind}|{change.Classification}|{change.Message}");
    }
    if (comparison.Types.Value is FindingComparison<ApiTypeHandle>.Complete types)
    {
        foreach (PairFinding<ApiTypeHandle> pair in types.Pairs.Where(pair => Payload(pair).TypeFullName == name))
            lines.Add($"T|{pair.Kind}|{pair.Detail}");
    }
    else
    {
        lines.Add("T|incomplete");
    }
    if (comparison.Members.Value is FindingComparison<ApiMemberHandle>.Complete members)
    {
        foreach (PairFinding<ApiMemberHandle> pair in members.Pairs.Where(pair => Payload(pair).TypeFullName == name))
            lines.Add($"M|{pair.Kind}|{Payload(pair).CanonicalSignature}|{pair.Detail}");
    }
    else
    {
        lines.Add("M|incomplete");
    }
    lines.Sort(StringComparer.Ordinal);
    return lines;
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

// g2exact: the exact-Type request path (#9466) feeding the Metadata-owned API
// comparison g2 runs.
sealed class ExactTypeApi
{
    public required ApiFindingComparison Comparison { get; init; }
    public required ApiSurface After { get; init; }
    public required bool ExtensionMode { get; init; }
    public string Mode => ExtensionMode ? "ext" : "decl";

    public static ExactTypeApi Run(
        PEReader beforePe, MetadataReader beforeMd, AssemblyReferenceIdentity beforeIdentity, ApiSurfaceImageCache beforeCache,
        PEReader afterPe, MetadataReader afterMd, AssemblyReferenceIdentity afterIdentity, ApiSurfaceImageCache afterCache,
        MetadataTypeDefinitionName type, ApiSurfaceExtractionBounds bounds)
    {
        TypeDefinitionHandle before = Resolve(beforeMd, type, bounds);
        TypeDefinitionHandle after = Resolve(afterMd, type, bounds);
        // A same-image extension for this receiver on either side means the
        // receiver carries projected extension members, and its instance
        // members can pair with extensions; an unanswered Exists is treated
        // the same way.
        bool extensionMode =
            ExtensionExists(beforePe, beforeMd, beforeIdentity, type, before) != false
            || ExtensionExists(afterPe, afterMd, afterIdentity, type, after) != false;
        ApiSurface beforeSurface = Extract(beforePe, beforeMd, beforeIdentity, beforeCache, type, before, extensionMode);
        ApiSurface afterSurface = Extract(afterPe, afterMd, afterIdentity, afterCache, type, after, extensionMode);
        return new()
        {
            Comparison = ApiComparisonQuery.Execute(beforeSurface, afterSurface),
            After = afterSurface,
            ExtensionMode = extensionMode,
        };
    }

    public static TypeDefinitionHandle Resolve(
        MetadataReader md, MetadataTypeDefinitionName type, ApiSurfaceExtractionBounds bounds)
        => MetadataExactTypeDefinitionInspection.Resolve(md, type, bounds, CancellationToken.None)
            is MetadataExactTypeDefinitionResolution.Resolved resolved
                ? resolved.Handle
                : default;

    static MetadataExtensionReceiverSelection Receiver(
        MetadataReader md, AssemblyReferenceIdentity identity, MetadataTypeDefinitionName type, TypeDefinitionHandle handle)
        => new(identity, type, handle.IsNil ? null : MetadataTypeDefinitionAddress.FromHandle(md, handle));

    public static bool? ExtensionExists(
        PEReader pe, MetadataReader md, AssemblyReferenceIdentity identity,
        MetadataTypeDefinitionName type, TypeDefinitionHandle handle)
        => MetadataRelationInspection.ExecuteExtensionPresence(
                pe,
                md,
                new MetadataExtensionRelationPresenceRequest(
                    Receiver(md, identity, type, handle),
                    new MetadataOperationPolicy(int.MaxValue),
                    includeNonPublic: false,
                    sourceAssembly: identity),
                CancellationToken.None)
            is MetadataExtensionRelationPresenceOutcome.Available available
                ? available.Exists
                : null;

    // The TypeDefs that declare an extension for this receiver in this image,
    // from the same-image extension relation population; null when it cannot
    // be read completely.
    public static HashSet<TypeDefinitionHandle>? ExtensionDeclaringTypes(
        PEReader pe, MetadataReader md, AssemblyReferenceIdentity identity,
        MetadataTypeDefinitionName type, TypeDefinitionHandle handle)
    {
        MetadataExtensionRelationPopulationOutcome outcome = MetadataRelationInspection.ExecuteExtensionPopulation(
            pe,
            md,
            new MetadataExtensionRelationPopulationRequest(
                Receiver(md, identity, type, handle),
                new MetadataOperationPolicy(int.MaxValue),
                rows: new MetadataExtensionRelationPopulationRowsRequest(0, int.MaxValue)),
            CancellationToken.None);
        if (outcome is not MetadataExtensionRelationPopulationOutcome.Available
            {
                Result.Rows: MetadataExtensionRelationPopulationRowsOutcome.Read { NextOrdinal: null } read,
            })
        {
            return null;
        }
        var declaring = new HashSet<TypeDefinitionHandle>();
        foreach (MetadataExtensionRelationPopulationRow row in read.Items)
        {
            foreach (MetadataExtensionRelationEvidence occurrence in row.Occurrences)
            {
                if (!occurrence.DeclaringType.TryResolve(md, out TypeDefinitionHandle declaringType))
                    return null;
                declaring.Add(declaringType);
            }
        }
        return declaring;
    }

    public static ApiSurface Extract(
        PEReader pe, MetadataReader md, AssemblyReferenceIdentity identity, ApiSurfaceImageCache cache,
        MetadataTypeDefinitionName type, TypeDefinitionHandle handle, bool extensionMode)
    {
        if (!extensionMode)
        {
            return ApiSurfaceExtractor.ExtractDeclarations(
                pe, ApiSurfaceExtractionScope.Public, candidate => candidate == handle, cache);
        }
        // The receiver plus only the Types that declare an extension for it on
        // this side (and their nested Types), with local extension projection:
        // the receiver's projected extension members and extension/instance
        // pairing for it are exactly g2's. A population that cannot be read
        // falls back to g2's complete projection.
        HashSet<TypeDefinitionHandle>? declaring = ExtensionDeclaringTypes(pe, md, identity, type, handle);
        if (declaring is null)
            return ApiSurfaceExtractor.Extract(pe, ApiSurfaceExtractionScope.Public);
        return ApiSurfaceExtractor.ExtractDeclarations(
            pe,
            ApiSurfaceExtractionScope.Public,
            candidate =>
            {
                if (candidate == handle)
                    return true;
                for (TypeDefinitionHandle current = candidate; !current.IsNil; current = md.GetTypeDefinition(current).GetDeclaringType())
                {
                    if (declaring.Contains(current))
                        return true;
                }
                return false;
            },
            cache,
            includeLocalExtensionProjections: true);
    }
}
