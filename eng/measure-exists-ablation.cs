#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// Experiment (branch exp/exists-gap-ablation): the whole Finding Diff Any
// comparison of one image pair through the walk and through lockstep over two
// IL cursors, under the ablation named by IL_ABLATE, so that each component of
// the cursor's per-advance work can be attributed by subtraction.
// Usage: measure-exists-ablation <before.dll> <after.dll> [iterations]

using System.Diagnostics;
using System.Reflection.PortableExecutable;
using ILInspector.ILDiff;
using ILInspector.Metadata;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: measure-exists-ablation <before.dll> <after.dll> [iterations]");
    return 2;
}

byte[] beforeBytes = File.ReadAllBytes(args[0]);
byte[] afterBytes = File.ReadAllBytes(args[1]);
int iterations = args.Length > 2 ? int.Parse(args[2]) : 10;
string config = Environment.GetEnvironmentVariable("IL_ABLATE") is { Length: > 0 } env ? env : "baseline";

FindingDiffAnyResult RunWalk()
{
    using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
    using var after = new PEReader(new MemoryStream(afterBytes, writable: false));
    return FindingDiffAny.Compare(before, after);
}

FindingDiffAnyResult RunLockstep()
{
    using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
    using var after = new PEReader(new MemoryStream(afterBytes, writable: false));
    IlCensusSide beforeSide = IlCensusSide.Create(before.GetMetadataReader(), IlBodyDiffPolicy.Body);
    IlCensusSide afterSide = IlCensusSide.Create(after.GetMetadataReader(), IlBodyDiffPolicy.Body);
    return FindingDiffAny.Compare(
        before,
        after,
        (beforePe, beforeMethod, afterPe, afterMethod) =>
            IlFindings.Exists(beforeSide, beforePe, beforeMethod, afterSide, afterPe, afterMethod).Decision);
}

(double MedianMs, long MedianBytes) Measure(Func<FindingDiffAnyResult> run)
{
    for (int i = 0; i < 3; i++)
        run();
    var ms = new List<double>(iterations);
    var bytes = new List<long>(iterations);
    for (int i = 0; i < iterations; i++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        run();
        ms.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        bytes.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
    }
    ms.Sort();
    bytes.Sort();
    return (ms[ms.Count / 2], bytes[bytes.Count / 2]);
}

FindingDiffAnyResult walk = RunWalk();
FindingDiffAnyResult lockstep = RunLockstep();
bool agree = walk.Types.SequenceEqual(lockstep.Types);
int changed = walk.Types.Count(t => t.Body == FindingDiffAnyState.Changed);
(double walkMs, long walkBytes) = Measure(RunWalk);
(double lockstepMs, long lockstepBytes) = Measure(RunLockstep);
Console.WriteLine(
    $"ablation\tconfig={config}\tpair={Path.GetFileName(args[0])}|{Path.GetFileName(args[1])}\ttypes={walk.Types.Length}\tbodyChanged={changed}"
        + $"\tbodies={walk.Receipt.BodiesCompared}\twalk-ms={walkMs:F2}\twalkBytes={walkBytes}"
        + $"\tlockstep-ms={lockstepMs:F2}\tlockstepBytes={lockstepBytes}\tagree={agree}");
return 0;
