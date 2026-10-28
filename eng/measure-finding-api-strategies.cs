#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property AssemblyName=ILInspector.Metadata.Tests

// Experiment only: same paired-Type population, admission, and complete All
// comparison for each strategy. Different/equal hashes are evidence, never rows.
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;
using Inspector.Findings;

if (args.Length < 2) return 2;
byte[] oldBytes = File.ReadAllBytes(args[0]), newBytes = File.ReadAllBytes(args[1]);
int rounds = args.Length > 2 ? int.Parse(args[2]) : 9;
using var before = new PEReader(new MemoryStream(oldBytes));
using var after = new PEReader(new MemoryStream(newBytes));
var oracle = MetadataFindings.CompareApi(
    ApiSurfaceExtractor.ExtractDeclarations(before, ApiSurfaceExtractionScope.ApiAxis),
    ApiSurfaceExtractor.ExtractDeclarations(after, ApiSurfaceExtractionScope.ApiAxis),
    new FindingSubject("api", "API"), new ApiDiffOptions(ApiDiffScope.All));
var changed = new HashSet<string>(oracle.ApiDiff.TypeDiffs.Select(t => t.TypeFullName), StringComparer.Ordinal);
if (oracle.Types.Value is FindingComparison<ApiTypeHandle>.Complete types)
    foreach (var row in types.Pairs.Where(p => p.Kind != PairKind.Present)) changed.Add(Payload(row).TypeFullName);
if (oracle.Members.Value is FindingComparison<ApiMemberHandle>.Complete members)
    foreach (var row in members.Pairs.Where(p => p.Kind != PairKind.Present)) changed.Add(Payload(row).TypeFullName);

var strategies = Enum.GetValues<Strategy>();
foreach (var strategy in strategies)
{
    var check = Run(strategy);
    var misses = check.States.Where(p => changed.Contains(p.Key) != (p.Value == FindingDiffAnyState.Changed)).ToArray();
    Console.WriteLine($"check\t{strategy}\ttypes={check.States.Count}\tmismatch={misses.Length}\tfallbackPairs={check.Fallbacks}\tcensuses={check.Censuses}\thashes={check.Hashes}");
    foreach (var miss in misses.Take(12)) Console.WriteLine($"  mismatch\t{strategy}\t{miss.Key}\tstate={miss.Value}\toracleChanged={changed.Contains(miss.Key)}");
}
var samples = strategies.ToDictionary(s => s, _ => new List<double>());
var allocations = strategies.ToDictionary(s => s, _ => new List<long>());
for (int pass = -2; pass < rounds; pass++)
{
    foreach (var strategy in strategies.Skip((pass + 2) % strategies.Length).Concat(strategies.Take((pass + 2) % strategies.Length)))
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        var result = Run(strategy);
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        GC.KeepAlive(result);
        if (pass >= 0) { samples[strategy].Add(ms); allocations[strategy].Add(bytes); }
    }
}
foreach (var strategy in strategies)
{
    samples[strategy].Sort(); allocations[strategy].Sort();
    Console.WriteLine($"time\t{strategy}\tmedian-ms={samples[strategy][rounds / 2]:F2}\tmin={samples[strategy][0]:F2}\tmax={samples[strategy][^1]:F2}\tbytes={allocations[strategy][rounds / 2]}");
}
return 0;

Result Run(Strategy strategy)
{
    using var imageA = new PEReader(new MemoryStream(oldBytes));
    using var imageB = new PEReader(new MemoryStream(newBytes));
    var interner = new SymbolKeyInterner<FindingDiffAnyPart>();
    var a = new FindingDiffAny.Side(imageA, interner);
    var b = new FindingDiffAny.Side(imageB, interner);
    var unitsA = a.Units(); var unitsB = b.Units();
    var work = new FindingDiffAny.Work();
    var hashA = new ApiTypeClosureHash(imageA.GetMetadataReader());
    var hashB = new ApiTypeClosureHash(imageB.GetMetadataReader());
    bool contextEqual = strategy != Strategy.Census && hashA.Context() is { } contextA && hashB.Context() is { } contextB && contextA == contextB;
    var result = new Result();
    foreach (var (key, oldType) in unitsA.Entries)
    {
        if (!unitsB.TryGetValue(key, out var newType)) continue;
        FindingDiffAnyState state;
        try
        {
            state = Decide(oldType, newType);
        }
        finally { a.EndUnit(); b.EndUnit(); }
        result.States[oldType.FullName] = state;
    }
    result.Fallbacks += work.ApiSurfacesBuilt / 2;
    return result;

    FindingDiffAnyState Decide(FindingDiffAny.Unit ua, FindingDiffAny.Unit ub)
    {
        if (ua.Malformed || ub.Malformed) return FindingDiffAnyState.Indeterminate;
        if (!a.IsVisible(ua.Owner) && !b.IsVisible(ub.Owner)) return FindingDiffAnyState.Unchanged;
        if (strategy == Strategy.Census)
        {
            result.Censuses += 2;
            return FindingDiffAny.ApiState(a, ua, a.ApiCensus(ua), b, ub, b.ApiCensus(ub), work);
        }
        if (strategy is Strategy.HashFallback or Strategy.HashThenCensus && EqualHash(ua, ub)) return FindingDiffAnyState.Unchanged;
        if (strategy != Strategy.HashFallback)
        {
            result.Censuses += 2;
            var ca = a.ApiCensus(ua); var cb = b.ApiCensus(ub);
            bool equal = FindingDiffAny.CensusesEqual(a, ca, b, cb);
            if (!equal && FindingDiffAny.HasPresentedApiDifference(a, ca, b, cb)) return FindingDiffAnyState.Changed;
            if (strategy == Strategy.CensusThenHash && EqualHash(ua, ub)) return FindingDiffAnyState.Unchanged;
        }
        result.Fallbacks++;
        var complete = MetadataFindings.CompareApi(
            ApiSurfaceExtractor.ExtractDeclarations(imageA, ApiSurfaceExtractionScope.ApiAxis, h => h == ua.Owner),
            ApiSurfaceExtractor.ExtractDeclarations(imageB, ApiSurfaceExtractionScope.ApiAxis, h => h == ub.Owner),
            new FindingSubject("api", "API"), new ApiDiffOptions(ApiDiffScope.All));
        return complete.ApiDiff.IsEmpty ? FindingDiffAnyState.Unchanged : FindingDiffAnyState.Changed;
    }

    bool EqualHash(FindingDiffAny.Unit ua, FindingDiffAny.Unit ub)
    {
        if (!contextEqual) return false;
        result.Hashes += 2;
        return hashA.Type(ua.Owner, ApiSurfaceExtractionScope.ApiAxis) is { } x
            && hashB.Type(ub.Owner, ApiSurfaceExtractionScope.ApiAxis) is { } y && x == y;
    }
}

static T Payload<T>(PairFinding<T> row) where T : notnull
    => (((IPairFinding)row).New as Finding<T> ?? (Finding<T>)((IPairFinding)row).Old!).Payload;

enum Strategy { Census, HashFallback, HashThenCensus, CensusThenHash }
sealed class Result
{
    public Dictionary<string, FindingDiffAnyState> States { get; } = new(StringComparer.Ordinal);
    public int Fallbacks, Censuses, Hashes;
}
