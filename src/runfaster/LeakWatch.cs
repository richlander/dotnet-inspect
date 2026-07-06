using System.Collections.Immutable;
using System.Globalization;

// Dynamic leak / memory-growth signal for a long-running process, consuming a
// dotnet-counters `System.Runtime` timeseries (CSV). This is the complement to the
// static ArrayPool leak-triage in ILInspector.Analysis: static analysis sees a
// managed-heap-visible IL shape; this sees the runtime accounting that separates
// three very different growth causes that look alike from the outside:
//
//   * managed retention   — the live GC heap grows and a gen2 collection does NOT
//                           reclaim it (a genuine managed leak);
//   * churn storm         — a high allocation rate promotes objects to gen2, but a
//                           gen2 collection DOES reclaim them (collectible; the cost
//                           is allocation rate / parallelism, not a leak);
//   * native / committed  — the working set stays far above the live heap even after
//     growth                a gen2 collection reclaims it (RSS does not follow the
//                           heap down: native allocation or GC-committed regions the
//                           runtime returns to the OS lazily).
//
// An allocation-rate join (runfaster correlate) ranks hot allocators but measures
// bytes *allocated*, not bytes *retained*, so it cannot tell a churn storm from a
// leak. This verb adds that retention axis.

enum LeakWatchVerdict
{
    Inconclusive,
    Healthy,
    ChurnStorm,
    ManagedRetention,
    NativeOrCommittedGrowth,
}

// One time sample of runtime memory counters. Rate counters (allocated, gen2
// collections, GC pause) are per-window increments as emitted by dotnet-counters.
readonly record struct LeakWatchSample(
    double SecondsFromStart,
    long WorkingSet,
    long LiveHeap,
    long Gen2,
    long Committed,
    long AllocatedInWindow,
    double Gen2CollectionsInWindow,
    double GcPauseSecondsInWindow,
    int Threads);

sealed record LeakWatchThresholds(
    double GapRatio = 2.0,
    double ManagedGrowthFactor = 2.0,
    double RetainedGapBytes = 512L * 1024 * 1024,
    double PauseFraction = 0.20)
{
    public static LeakWatchThresholds Default { get; } = new();
}

sealed record LeakWatchResult(
    LeakWatchVerdict Verdict,
    string Headline,
    long WorkingSetFirst,
    long WorkingSetPeak,
    long WorkingSetLast,
    long LiveHeapPeak,
    long RetainedFloorAfterGen2,
    long LiveHeapAtFloor,
    double GapRatio,
    long TotalAllocated,
    double DurationSeconds,
    double GcPauseFraction,
    int Gen2Collections,
    bool HeapReclaimedByGen2,
    int ThreadGrowth,
    ImmutableArray<string> Notes);

static class LeakWatchAnalyzer
{
    public static LeakWatchResult Analyze(IReadOnlyList<LeakWatchSample> samples, LeakWatchThresholds thresholds)
    {
        if (samples.Count < 2)
        {
            return new LeakWatchResult(
                LeakWatchVerdict.Inconclusive,
                "Not enough samples to establish a trajectory (need at least 2).",
                samples.Count > 0 ? samples[0].WorkingSet : 0,
                samples.Count > 0 ? samples[0].WorkingSet : 0,
                samples.Count > 0 ? samples[0].WorkingSet : 0,
                0, 0, 0, 0, 0, 0, 0, 0, false, 0, []);
        }

        long wsFirst = samples[0].WorkingSet;
        long wsLast = samples[^1].WorkingSet;
        long wsPeak = samples.Max(s => s.WorkingSet);
        long liveHeapPeak = samples.Max(s => s.LiveHeap);
        long liveHeapFirst = samples[0].LiveHeap;
        long liveHeapLast = samples[^1].LiveHeap;
        double duration = Math.Max(samples[^1].SecondsFromStart - samples[0].SecondsFromStart, 0);
        long totalAllocated = samples.Sum(s => s.AllocatedInWindow);
        double totalPause = samples.Sum(s => s.GcPauseSecondsInWindow);
        double pauseFraction = duration > 0 ? totalPause / duration : 0;
        int gen2Collections = (int)Math.Round(samples.Sum(s => s.Gen2CollectionsInWindow));
        int threadGrowth = samples.Max(s => s.Threads) - samples[0].Threads;

        long reclaimThreshold = (long)(thresholds.RetainedGapBytes / 4);

        // Index of the live-heap high-water mark. "Reclaimed" only counts if the TOTAL
        // live heap (not just gen2) falls substantially AFTER that peak — a collection
        // that fired before later monotonic growth must not mask a real leak.
        int liveHeapPeakIndex = 0;
        for (int i = 1; i < samples.Count; i++)
        {
            if (samples[i].LiveHeap > samples[liveHeapPeakIndex].LiveHeap)
                liveHeapPeakIndex = i;
        }

        bool heapReclaimedAfterPeak = false;
        int reclaimIndex = -1;
        long liveHeapFloorAfterPeak = liveHeapPeak;
        for (int i = liveHeapPeakIndex + 1; i < samples.Count; i++)
        {
            if (samples[i].LiveHeap < liveHeapPeak - reclaimThreshold)
            {
                heapReclaimedAfterPeak = true;
                if (samples[i].LiveHeap < liveHeapFloorAfterPeak)
                {
                    liveHeapFloorAfterPeak = samples[i].LiveHeap;
                    reclaimIndex = i;
                }
            }
        }
        bool gen2CollectionObserved = samples.Any(s => s.Gen2CollectionsInWindow > 0);
        bool heapReclaimedByGen2 = heapReclaimedAfterPeak;

        // The working-set floor after the live-heap was reclaimed: does RSS follow the
        // heap down? Only meaningful when a reclaim was actually observed; otherwise the
        // relevant gap is simply the end-of-window gap.
        long retainedFloorAfterGen2;
        long liveHeapAtFloor;
        if (reclaimIndex >= 0)
        {
            retainedFloorAfterGen2 = long.MaxValue;
            liveHeapAtFloor = liveHeapLast;
            for (int i = reclaimIndex; i < samples.Count; i++)
            {
                if (samples[i].WorkingSet < retainedFloorAfterGen2)
                {
                    retainedFloorAfterGen2 = samples[i].WorkingSet;
                    liveHeapAtFloor = samples[i].LiveHeap;
                }
            }
        }
        else
        {
            retainedFloorAfterGen2 = wsLast;
            liveHeapAtFloor = liveHeapLast;
        }

        // Working set must actually GROW over the window for a growth verdict; a stable
        // high native baseline (large gap but flat) is not growth.
        bool workingSetGrew = wsPeak >= wsFirst + thresholds.RetainedGapBytes;

        double gapRatio = liveHeapPeak > 0 ? (double)wsPeak / liveHeapPeak : 0;
        long retainedGap = retainedFloorAfterGen2 - liveHeapAtFloor;
        string afterCollectionClause = heapReclaimedByGen2 || gen2CollectionObserved
            ? " after a gen2 collection reclaimed the heap"
            : "";

        var notes = ImmutableArray.CreateBuilder<string>();
        notes.Add($"Working set: {Fmt(wsFirst)} → peak {Fmt(wsPeak)} → {Fmt(wsLast)} over {duration:F0}s.");
        notes.Add($"Live GC heap: {Fmt(liveHeapFirst)} → peak {Fmt(liveHeapPeak)} → {Fmt(liveHeapLast)}.");
        if (duration > 0)
            notes.Add($"Allocation churn: {Fmt(totalAllocated)} allocated ({Fmt((long)(totalAllocated / duration))}/s).");
        notes.Add($"GC pause: {pauseFraction * 100:F0}% of wall time ({gen2Collections} gen2 collection(s) observed).");
        if (threadGrowth > 0)
            notes.Add($"Thread count grew by {threadGrowth} over the window.");

        // Decision order: a genuine managed leak (heap grows and is NOT reclaimed) is
        // the most serious; then RSS-not-returned while the working set grows; then a
        // collectible churn storm; else healthy. All growth verdicts require the
        // working set (or live heap) to actually grow — a stable high baseline is not a
        // leak.
        LeakWatchVerdict verdict;
        string headline;

        // Growth relative to a small floor so a heap that starts near zero is not missed.
        long liveHeapBase = Math.Max(liveHeapFirst, 16L * 1024 * 1024);
        bool liveHeapGrew = liveHeapPeak > liveHeapBase * thresholds.ManagedGrowthFactor
            && liveHeapPeak - liveHeapFirst >= thresholds.RetainedGapBytes;
        bool highChurn = duration > 0 && pauseFraction >= thresholds.PauseFraction;

        if (liveHeapGrew && !heapReclaimedByGen2)
        {
            verdict = LeakWatchVerdict.ManagedRetention;
            headline = $"Managed retention: live GC heap grew to {Fmt(liveHeapPeak)} and no collection reclaimed it — a managed leak. Capture a gcdump and inspect the top retained types.";
            notes.Add("The growth is on the managed heap and survives collection: this is the class the static ArrayPool leak-triage targets, and a gcdump will name the retained roots.");
        }
        else if (workingSetGrew && gapRatio >= thresholds.GapRatio && retainedGap >= thresholds.RetainedGapBytes)
        {
            verdict = LeakWatchVerdict.NativeOrCommittedGrowth;
            headline = $"Native/committed growth: working set grew to {Fmt(wsPeak)} ({gapRatio:F1}× the {Fmt(liveHeapPeak)} live-heap peak) and stayed {Fmt(retainedGap)} above the live heap{afterCollectionClause}. RSS is not following the heap down — native allocation or GC-committed regions returned to the OS lazily.";
            if (heapReclaimedByGen2)
                notes.Add($"The live heap fell to {Fmt(liveHeapFloorAfterPeak)} after its peak, but the working set held {Fmt(retainedFloorAfterGen2)} — the gap is off the managed heap.");
            else
                notes.Add("No collection was observed reclaiming the heap in this window; the gap is measured at the end of the sample. A longer sample (spanning a gen2 collection) or a gcdump confirms whether the managed heap is collectible.");
            if (highChurn)
                notes.Add("High allocation churn + GC pause alongside this suggests an allocation storm (e.g. heavy parallel work) driving both promotion and committed growth; reducing allocation rate / parallelism typically shrinks the footprint.");
        }
        else if (highChurn && heapReclaimedByGen2)
        {
            verdict = LeakWatchVerdict.ChurnStorm;
            headline = $"Churn storm (collectible): {Fmt(totalAllocated)} allocated with {pauseFraction * 100:F0}% GC pause, but a collection reclaims the heap — high allocation rate, not a leak. Reduce allocation rate or parallelism.";
            notes.Add("An allocation-rate join would rank the hot allocators here, but the heap is collectible: this is churn, not retention.");
        }
        else
        {
            verdict = LeakWatchVerdict.Healthy;
            headline = workingSetGrew
                ? $"No leak signature: working set grew to {Fmt(wsPeak)} but tracks the live heap ({Fmt(liveHeapPeak)} peak) — no retention or native-growth gap."
                : $"Healthy: working set ({Fmt(wsPeak)} peak) is stable and tracks the live heap ({Fmt(liveHeapPeak)} peak); no growth signature.";
            if (!workingSetGrew && gapRatio >= thresholds.GapRatio)
                notes.Add($"The working set sits {gapRatio:F1}× above the live heap but is stable (not growing): a fixed native/committed baseline, not a leak.");
        }

        return new LeakWatchResult(
            verdict, headline, wsFirst, wsPeak, wsLast, liveHeapPeak,
            retainedFloorAfterGen2, liveHeapAtFloor, gapRatio, totalAllocated,
            duration, pauseFraction, gen2Collections, heapReclaimedByGen2,
            Math.Max(threadGrowth, 0), notes.ToImmutable());
    }

    static string Fmt(long bytes)
    {
        double b = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int u = 0;
        while (Math.Abs(b) >= 1024 && u < units.Length - 1)
        {
            b /= 1024;
            u++;
        }
        return $"{b:0.##} {units[u]}";
    }
}

// Parses a `dotnet-counters collect --format csv` file of System.Runtime counters
// into per-timestamp samples. Robust to counter ordering and missing rows.
static class LeakWatchCountersCsv
{
    const string WorkingSet = "dotnet.process.memory.working_set (By)";
    const string Committed = "dotnet.gc.last_collection.memory.committed_size (By)";
    const string ThreadCount = "dotnet.thread_pool.thread.count ({thread})";

    public static IReadOnlyList<LeakWatchSample> Parse(IEnumerable<string> lines)
    {
        // Group metric rows by timestamp, preserving first-seen order.
        var order = new List<string>();
        var byTimestamp = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);

        bool header = true;
        foreach (var line in lines)
        {
            if (header)
            {
                header = false;
                if (line.StartsWith("Timestamp", StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            var cols = SplitCsv(line);
            if (cols.Count < 5)
                continue;
            string timestamp = cols[0];
            string name = cols[2];
            if (!double.TryParse(cols[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                continue;
            if (!byTimestamp.TryGetValue(timestamp, out var map))
            {
                map = new Dictionary<string, double>(StringComparer.Ordinal);
                byTimestamp[timestamp] = map;
                order.Add(timestamp);
            }
            map[name] = value;
        }

        var samples = new List<LeakWatchSample>(order.Count);
        double? firstSeconds = null;
        DateTime? firstTime = null;
        for (int i = 0; i < order.Count; i++)
        {
            var map = byTimestamp[order[i]];
            // A sample must at least carry a working-set reading to be meaningful.
            if (!map.TryGetValue(WorkingSet, out double ws))
                continue;

            long liveHeap = (long)(
                Gen(map, "gen0") + Gen(map, "gen1") + Gen(map, "gen2") + Gen(map, "loh") + Gen(map, "poh"));

            double seconds;
            if (DateTime.TryParse(order[i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t))
            {
                firstTime ??= t;
                seconds = (t - firstTime.Value).TotalSeconds;
            }
            else
            {
                firstSeconds ??= i * 1.0;
                seconds = i - firstSeconds.Value;
            }

            samples.Add(new LeakWatchSample(
                SecondsFromStart: seconds,
                WorkingSet: (long)ws,
                LiveHeap: liveHeap,
                Gen2: (long)Gen(map, "gen2"),
                Committed: (long)Get(map, Committed),
                AllocatedInWindow: (long)GetPrefix(map, "dotnet.gc.heap.total_allocated"),
                Gen2CollectionsInWindow: GetGen2Collections(map),
                GcPauseSecondsInWindow: GetPrefix(map, "dotnet.gc.pause.time"),
                Threads: (int)Get(map, ThreadCount)));
        }

        return samples;
    }

    static double Gen(Dictionary<string, double> map, string gen)
        => Get(map, $"dotnet.gc.last_collection.heap.size (By)[gc.heap.generation={gen}]");

    static double Get(Dictionary<string, double> map, string name)
        => map.TryGetValue(name, out double v) ? v : 0;

    static double GetPrefix(Dictionary<string, double> map, string prefix)
    {
        foreach (var (k, v) in map)
        {
            if (k.StartsWith(prefix, StringComparison.Ordinal))
                return v;
        }
        return 0;
    }

    static double GetGen2Collections(Dictionary<string, double> map)
    {
        foreach (var (k, v) in map)
        {
            if (k.StartsWith("dotnet.gc.collections", StringComparison.Ordinal)
                && k.Contains("generation=gen2", StringComparison.Ordinal))
                return v;
        }
        return 0;
    }

    static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char c in line)
        {
            if (c == '"')
                quoted = !quoted;
            else if (c == ',' && !quoted)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
                sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }
}
