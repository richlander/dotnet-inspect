#:project ../src/DotnetInspector.Queries/DotnetInspector.Queries.csproj
#:project ../src/DotnetInspector.Presentation/DotnetInspector.Presentation.csproj

// Measures the exact Type load of the selected-Library API comparison
// (docs/design/library-api-diff-presentation.md#type-scoped-comparison) over
// one Library image pair, at the Browser's API axis scope.
// Usage: measure-type-api-exact <before.dll> <after.dll> [iterations] [predicateSample]
//
// complete    the whole-Library comparison and its presentation, what a Type
//             request costs when no Library comparison is retained;
// exact       ApiTypeSelection.ForFullNames([type]) for every Type on either
//             side, checked against the complete comparison's subject for
//             that Type (agreement must be total);
// predicate   the shipped predicate selection (pairs with every
//             extension-declaring Type), on every Nth Type.
// A Type whose exact selection keeps the projection complete (it declares an
// extension) is counted as "complete-pairing".

using System.Diagnostics;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using ILInspector.Metadata;

if (args.Length is < 2 or > 4)
{
    Console.Error.WriteLine("Usage: measure-type-api-exact <before.dll> <after.dll> [iterations] [predicateSample]");
    return 2;
}

string beforePath = args[0];
string afterPath = args[1];
int iterations = args.Length > 2 ? int.Parse(args[2]) : 5;
int predicateSample = args.Length > 3 ? int.Parse(args[3]) : 10;
const ApiSurfaceScope Scope = ApiSurfaceScope.ApiAxis;
var limits = new ApiSurfaceProjectionLimits(64, 1_000_000, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

await using var workspace = new InspectionWorkspace();
var beforeParticipant = Participant(beforePath, "Before");
var afterParticipant = Participant(afterPath, "After");
using AssemblyContextGroup beforeGroup = workspace.CreateAssemblyContextGroup([beforeParticipant]);
using AssemblyContextGroup afterGroup = workspace.CreateAssemblyContextGroup([afterParticipant]);

AssemblyContextApiComparisonResult Compare(ApiTypeSelection? selection)
    => AssemblyContextApiComparisonQuery.Execute(
        beforeGroup, beforeParticipant, afterGroup, afterParticipant, Scope, limits, selection);

LibraryApiDiffOutcome.Available Present(AssemblyContextApiComparisonResult result)
    => LibraryApiDiffPresentationAdapter.Create(result) as LibraryApiDiffOutcome.Available
        ?? throw new InvalidOperationException("The comparison has no available presentation.");

static double Median(List<double> values)
{
    values.Sort();
    return values.Count == 0 ? double.NaN : values[values.Count / 2];
}

static double Percentile(List<double> sorted, double p)
    => sorted.Count == 0 ? double.NaN : sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];

// Complete comparison: one warm-up, then the median of `iterations`.
AssemblyContextApiComparisonResult completeResult = Compare(null);
LibraryApiDiffOutcome.Available complete = Present(completeResult);
var completeSamples = new List<double>();
for (int i = 0; i < iterations; i++)
{
    long start = Stopwatch.GetTimestamp();
    Present(Compare(null));
    completeSamples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
}

var subjects = complete.Document.Comparison.Subjects
    .GroupBy(subject => subject.Display, StringComparer.Ordinal)
    .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
string[] names =
[
    .. completeResult.Before.Surface!.Types
        .Concat(completeResult.After.Surface!.Types)
        .Select(type => type.FullName)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal),
];

var exact = new List<double>();
var exactCompletePairing = new List<double>();
var predicate = new List<double>();
int disagreements = 0;
int changed = 0;
string slowest = "";
double slowestMs = 0;
for (int index = 0; index < names.Length; index++)
{
    string name = names[index];
    ApiTypeSelection selection = ApiTypeSelection.ForFullNames([name]);
    AssemblyContextApiComparisonResult result = Compare(selection);
    var samples = new List<double>();
    for (int i = 0; i < iterations; i++)
    {
        long start = Stopwatch.GetTimestamp();
        result = Compare(selection);
        Present(result);
        samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    double ms = Median(samples);
    bool completePairing = result.Before.Surface!.Types.Count == completeResult.Before.Surface!.Types.Count
        && result.After.Surface!.Types.Count == completeResult.After.Surface!.Types.Count
        && completeResult.Before.Surface!.Types.Count > 1;
    (completePairing ? exactCompletePairing : exact).Add(ms);
    if (ms > slowestMs)
    {
        slowestMs = ms;
        slowest = name;
    }

    var expected = subjects.GetValueOrDefault(name) ?? [];
    var actual = Present(result).Document.Comparison.Subjects
        .Where(subject => subject.Display == name)
        .ToArray();
    if (expected.Length > 0)
        changed++;
    if (!expected.SequenceEqual(actual))
    {
        disagreements++;
        Console.Error.WriteLine($"disagrees: {name} expected={expected.Length} actual={actual.Length}");
    }

    if (predicateSample > 0 && index % predicateSample == 0)
    {
        var shipped = new ApiTypeSelection(candidate => candidate == name);
        Compare(shipped);
        long start = Stopwatch.GetTimestamp();
        Present(Compare(shipped));
        predicate.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}

exact.Sort();
exactCompletePairing.Sort();
predicate.Sort();
Console.WriteLine(
    $"types={names.Length}\tchanged={changed}\tdisagreements={disagreements}"
    + $"\tcomplete={Median(completeSamples):F2}"
    + $"\texactMedian={Median([.. exact]):F3}\texactP95={Percentile(exact, 0.95):F3}\texactMax={(exact.Count == 0 ? double.NaN : exact[^1]):F3}"
    + $"\tcompletePairing={exactCompletePairing.Count}\tcompletePairingMedian={Median([.. exactCompletePairing]):F2}"
    + $"\tpredicateMedian={Median([.. predicate]):F2}\tpredicateSampled={predicate.Count}"
    + $"\tslowest={slowest}:{slowestMs:F2}");
return disagreements == 0 ? 0 : 1;

static AssemblyContextParticipant Participant(string path, string label)
    => new(
        ResolvedAssemblyReference.CreateFromPath(path, AssemblyResolutionProvenance.Local(label)),
        NoResolverAssemblyBindingPolicy.Instance);
