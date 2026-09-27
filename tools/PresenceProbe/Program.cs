using System.Collections.Immutable;
using System.Diagnostics;

// args: <budget-ms> <dll>...   or   alloc <calls> <dll>
if (args[0] == "alloc")
{
    int calls = int.Parse(args[1]);
    var allocImage = ImmutableArray.Create(File.ReadAllBytes(args[2]));
    for (int i = 0; i < 20; i++)
        Probe(args[2], allocImage);
    long before = GC.GetAllocatedBytesForCurrentThread();
    using (var profile = new AllocProfile())
    {
        for (int i = 0; i < calls; i++)
            Probe(args[2], allocImage);
        Console.WriteLine($"hand-rolled {Path.GetFileName(args[2])}: exact {(GC.GetAllocatedBytesForCurrentThread() - before) / calls / 1024.0:F1} KB/call");
        profile.Report(calls, 20);
    }
    return;
}

int budgetMs = int.Parse(args[0]);
foreach (string path in args.Skip(1))
{
    var image = ImmutableArray.Create(File.ReadAllBytes(path));
    string answer = Probe(path, image);
    for (int i = 0; i < 5; i++)
        Probe(path, image);

    var samples = new List<double>();
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    var total = Stopwatch.StartNew();
    while ((total.ElapsedMilliseconds < budgetMs || samples.Count < 20) && samples.Count < 2000)
    {
        long start = Stopwatch.GetTimestamp();
        Probe(path, image);
        samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    long allocatedPerCall =
        (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / samples.Count;
    samples.Sort();
    double Q(double q) => samples[(int)Math.Clamp(Math.Round(q * (samples.Count - 1)), 0, samples.Count - 1)];
    Console.WriteLine(
        $"{Path.GetFileName(path)}\t{answer}\t{samples.Count}\t{Q(0.5):F3}\t{Q(0.1):F3}\t{Q(0.9):F3}\t{allocatedPerCall}");
}

static string Probe(string path, ImmutableArray<byte> image)
{
    try
    {
#if HEAD
        return ILInspector.Analysis.Planning.UnsafeEvidencePresence.HasEvidence(path, image) ? "true" : "false";
#else
        return ILInspector.Analysis.LibraryBodyIndex.HasUnsafeEvidence(path, image) ? "true" : "false";
#endif
    }
    catch (InvalidDataException)
    {
        return "incomplete";
    }
}
