#:project ../src/DotnetInspector.Sections/DotnetInspector.Sections.csproj
#:project ../src/DotnetInspector.Sections.Local/DotnetInspector.Sections.Local.csproj
#:property IsPublishable=true
#:property PublishAot=true
#:property OptimizationPreference=Speed
#:property InvariantGlobalization=true

// Measures the complete host-neutral Type Method Leverage operation used by
// the Browser/Wasm package and platform routes. The assembly context remains
// open across samples, matching one active workspace; each sample performs
// Type selection, whole-assembly call analysis, ranking, and attribution.
//
//   measure-type-method-leverage <assembly> <type-definition-id>
//     [--samples N] [--warmup N] [--serial]

using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Sections.Local;
using ILInspector.Metadata;

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "Usage: measure-type-method-leverage <assembly> "
            + "<type-definition-id> [--samples N] [--warmup N] "
            + "[--serial]");
    return 2;
}

string assemblyPath = Path.GetFullPath(args[0]);
string typeDefinitionId = args[1];
int samples = 20;
int warmup = 3;
bool serial = false;
for (int index = 2; index < args.Length; index++)
{
    switch (args[index])
    {
        case "--samples" when index + 1 < args.Length
            && int.TryParse(
                args[++index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsedSamples)
            && parsedSamples > 0:
            samples = parsedSamples;
            break;
        case "--warmup" when index + 1 < args.Length
            && int.TryParse(
                args[++index],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsedWarmup)
            && parsedWarmup >= 0:
            warmup = parsedWarmup;
            break;
        case "--serial":
            serial = true;
            break;
        default:
            Console.Error.WriteLine(
                "Usage: measure-type-method-leverage <assembly> "
                    + "<type-definition-id> [--samples N] [--warmup N] "
                    + "[--serial]");
            return 2;
    }
}

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"Assembly not found: {assemblyPath}");
    return 2;
}

ResolvedAssemblyReference assembly =
    ResolvedAssemblyReference.CreateFromPath(
        assemblyPath,
        AssemblyResolutionProvenance.Local(
            "Type Method Leverage measurement"));
var participant = new AssemblyContextParticipant(
    assembly,
    NoResolverAssemblyBindingPolicy.Instance);
await using var workspace = new InspectionWorkspace();
using AssemblyContextGroup group =
    workspace.CreateAssemblyContextGroup([participant]);

string expected =
    Fingerprint(Execute(group, participant, typeDefinitionId, serial));
Console.Error.WriteLine(
    string.Join(
        '\t',
        "identity",
        serial ? "serial" : "bounded-parallel",
        RuntimeFeature.IsDynamicCodeSupported
            ? "dynamic-code"
            : "static-code",
        expected));

for (int iteration = 0; iteration < warmup; iteration++)
{
    RequireExpected(
        expected,
        Execute(group, participant, typeDefinitionId, serial));
}

var timings = new List<double>(samples);
var allocations = new List<long>(samples);
for (int sample = 0; sample < samples; sample++)
{
    long before = GC.GetTotalAllocatedBytes(precise: true);
    long start = Stopwatch.GetTimestamp();
    AssemblyTypeMethodLeverageInspection result =
        Execute(group, participant, typeDefinitionId, serial);
    double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    long allocated =
        GC.GetTotalAllocatedBytes(precise: true) - before;
    RequireExpected(expected, result);
    timings.Add(elapsed);
    allocations.Add(allocated);
    Console.WriteLine(
        string.Join(
            '\t',
            sample + 1,
            elapsed.ToString("F3", CultureInfo.InvariantCulture),
            allocated));
}

timings.Sort();
allocations.Sort();
Console.Error.WriteLine(
    string.Join(
        '\t',
        "summary",
        samples,
        Percentile(timings, 0.50)
            .ToString("F3", CultureInfo.InvariantCulture),
        Percentile(timings, 0.95)
            .ToString("F3", CultureInfo.InvariantCulture),
        Percentile(allocations, 0.50),
        Percentile(allocations, 0.95)));
return 0;

static AssemblyTypeMethodLeverageInspection Execute(
    AssemblyContextGroup group,
    AssemblyContextParticipant participant,
    string typeDefinitionId,
    bool serial)
{
    InspectionEnvelope<
        AssemblyContextEntry<AssemblyTypeMethodLeverageInspection>> envelope =
            serial
                ? TypeMethodLeverageInspectionOperation.Execute(
                    group,
                    participant,
                    typeDefinitionId)
                : LocalTypeMethodLeverageInspectionOperation.Execute(
                    group,
                    participant,
                    typeDefinitionId);
    return envelope.Content switch
    {
        AssemblyContextEntry<
            AssemblyTypeMethodLeverageInspection>.Available available =>
                available.Value,
        AssemblyContextEntry<
            AssemblyTypeMethodLeverageInspection>.Rejected rejected =>
                throw new InvalidOperationException(
                    $"Type Method Leverage was rejected: "
                        + $"{rejected.Failure.Kind}."),
        AssemblyContextEntry<
            AssemblyTypeMethodLeverageInspection>.Failed failed =>
                throw new InvalidOperationException(
                    "Type Method Leverage failed.",
                    failed.Error),
        _ => throw new InvalidOperationException(
            "Unknown Type Method Leverage outcome."),
    };
}

static void RequireExpected(
    string expected,
    AssemblyTypeMethodLeverageInspection actual)
{
    string fingerprint = Fingerprint(actual);
    if (!StringComparer.Ordinal.Equals(expected, fingerprint))
    {
        throw new InvalidOperationException(
            $"Type Method Leverage changed between samples: "
                + $"{expected} != {fingerprint}");
    }
}

static string Fingerprint(
    AssemblyTypeMethodLeverageInspection inspection)
{
    TypeMethodLeverageRank? rank = inspection.WinningRank;
    string winningRank = rank is null
        ? "none"
        : string.Join(
            ',',
            rank.DirectCallerCount,
            rank.RootReach,
            rank.Fanout,
            rank.LoopCallCount,
            rank.MaxDepth);
    string anchors = string.Join(
        ';',
        inspection.AnchoredWinners.Select(winner =>
            winner.StableSelector
                + ":"
                + string.Join(',', winner.MethodTokens)));
    return string.Join(
        '|',
        inspection.TypeDefinitionId,
        inspection.MethodCount,
        inspection.WinnerCount,
        winningRank,
        anchors);
}

static T Percentile<T>(
    IReadOnlyList<T> sorted,
    double percentile) =>
    sorted[
        Math.Max(
            0,
            Math.Min(
                sorted.Count - 1,
                (int)Math.Ceiling(sorted.Count * percentile) - 1))];
