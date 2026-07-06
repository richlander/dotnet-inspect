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
        long liveHeapBase = Math.Max(liveHeapFirst, 16L * 1024 * 1024);

        // Steady-state ("retained") footprint at the END of the window, resistant to a
        // single final-sample blip (a GC dip or a transient spike) by taking the median
        // of the last few samples. Retention is about what the process is STILL holding
        // at the end relative to its baseline — not the global peak. Anchoring to global
        // peaks blinds the model to leaks smaller than a historical transient spike, and
        // to steady-state gaps that never peaked (a pure native leak on a flat heap).
        long liveHeapRetained = TailMedian(samples, static s => s.LiveHeap);
        long wsRetained = TailMedian(samples, static s => s.WorkingSet);

        bool gen2CollectionObserved = samples.Any(s => s.Gen2CollectionsInWindow > 0);

        // How much managed heap is still held above baseline at the end of the window.
        long managedRetained = liveHeapRetained - liveHeapBase;
        // How much RSS is still held above the live managed heap at the end: this memory
        // is, by construction, not live managed objects — native allocation or
        // GC-committed segments the runtime returns to the OS lazily.
        long nativeGap = wsRetained - liveHeapRetained;

        // The heap peaked well above where it SETTLED and settled near baseline: the
        // transient-churn signature (peaked high, came back down). Independent of whether
        // it had grown significantly first, so ChurnStorm still fires on collected churn.
        bool heapReturnedToBaseline = liveHeapPeak - liveHeapRetained >= reclaimThreshold
            && liveHeapRetained <= liveHeapBase * 3 / 2;
        bool heapReclaimedByGen2 = heapReturnedToBaseline;

        // Working set must actually GROW over the window for a native/committed growth
        // verdict: a stable high native baseline (large gap but flat) is a fixed
        // footprint, not a leak.
        bool workingSetGrew = wsPeak >= wsFirst + thresholds.RetainedGapBytes;

        // gapRatio compares the STEADY-STATE working set to the steady-state live heap
        // (not global peak to global peak): a committed-memory gap that persists after
        // the heap drops from a churn spike would be invisible to a peak/peak ratio.
        double gapRatio = liveHeapRetained > 0 ? (double)wsRetained / liveHeapRetained : 0;
        long retainedGap = nativeGap;
        long retainedFloorAfterGen2 = wsRetained;
        long liveHeapAtFloor = liveHeapRetained;
        long liveHeapFloorAfterPeak = liveHeapRetained;
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

        // Decision order:
        //  1. Native/committed growth (RSS held far above the live heap at steady state,
        //     and the working set grew) is checked FIRST: RSS above the live heap is
        //     unambiguously not-live-managed-objects, so when both a native gap and a
        //     heap rise coexist (e.g. an uncollected churn window with committed
        //     over-reservation) the native gap is the certain signal.
        //  2. Managed retention (the live heap is still held far above baseline at the
        //     end) — a genuine managed leak.
        //  3. Collectible churn storm (high GC pause, heap peaked and came back).
        //  4. Healthy.
        // All growth verdicts are evaluated at steady state (tail), not global peak.
        LeakWatchVerdict verdict;
        string headline;

        bool highChurn = duration > 0 && pauseFraction >= thresholds.PauseFraction;

        if (workingSetGrew && gapRatio >= thresholds.GapRatio && nativeGap >= thresholds.RetainedGapBytes)
        {
            verdict = LeakWatchVerdict.NativeOrCommittedGrowth;
            headline = $"Native/committed growth: working set grew to {Fmt(wsPeak)} and holds {Fmt(wsRetained)} ({gapRatio:F1}× the {Fmt(liveHeapRetained)} live heap) — staying {Fmt(nativeGap)} above the live heap{afterCollectionClause}. RSS is not following the heap down — native allocation or GC-committed regions returned to the OS lazily.";
            if (heapReclaimedByGen2)
                notes.Add($"The live heap fell to {Fmt(liveHeapFloorAfterPeak)} from its {Fmt(liveHeapPeak)} peak, but the working set held {Fmt(retainedFloorAfterGen2)} — the gap is off the managed heap.");
            else
                notes.Add("No collection was observed reclaiming the heap in this window; the gap is measured at the end of the sample. A longer sample (spanning a gen2 collection) or a gcdump confirms whether the managed heap is collectible.");
            if (highChurn)
                notes.Add("High allocation churn + GC pause alongside this suggests an allocation storm (e.g. heavy parallel work) driving both promotion and committed growth; reducing allocation rate / parallelism typically shrinks the footprint.");
        }
        else if (managedRetained >= thresholds.RetainedGapBytes)
        {
            verdict = LeakWatchVerdict.ManagedRetention;
            headline = $"Managed retention: the live GC heap is holding {Fmt(liveHeapRetained)} at steady state — {Fmt(managedRetained)} above its {Fmt(liveHeapBase)} baseline — and a collection did not return it, so it is a managed leak. Capture a gcdump and inspect the top retained types.";
            notes.Add("The growth is on the managed heap and survives collection: this is the class the static ArrayPool leak-triage targets, and a gcdump will name the retained roots.");
        }
        else if (highChurn && heapReclaimedByGen2)
        {
            verdict = LeakWatchVerdict.ChurnStorm;
            headline = $"Churn storm (collectible): {Fmt(totalAllocated)} allocated with {pauseFraction * 100:F0}% GC pause, but the heap peaked at {Fmt(liveHeapPeak)} and settled back to {Fmt(liveHeapRetained)} — high allocation rate, not a leak. Reduce allocation rate or parallelism.";
            notes.Add("An allocation-rate join would rank the hot allocators here, but the heap is collectible: this is churn, not retention.");
        }
        else
        {
            verdict = LeakWatchVerdict.Healthy;
            headline = workingSetGrew
                ? $"No leak signature: working set grew to {Fmt(wsPeak)} but the steady-state {Fmt(wsRetained)} tracks the {Fmt(liveHeapRetained)} live heap — no retention or native-growth gap."
                : $"Healthy: working set ({Fmt(wsPeak)} peak) is stable and tracks the live heap ({Fmt(liveHeapRetained)} retained); no growth signature.";
            if (!workingSetGrew && gapRatio >= thresholds.GapRatio)
                notes.Add($"The working set sits {gapRatio:F1}× above the live heap but is stable (not growing): a fixed native/committed baseline, not a leak.");
        }

        return new LeakWatchResult(
            verdict, headline, wsFirst, wsPeak, wsLast, liveHeapPeak,
            retainedFloorAfterGen2, liveHeapAtFloor, gapRatio, totalAllocated,
            duration, pauseFraction, gen2Collections, heapReclaimedByGen2,
            Math.Max(threadGrowth, 0), notes.ToImmutable());
    }

    // Steady-state estimator: the median of the last few samples, resistant to a single
    // final-sample blip (a transient GC dip or spike) while still reflecting the
    // end-of-window footprint rather than a historical peak.
    static long TailMedian(IReadOnlyList<LeakWatchSample> samples, Func<LeakWatchSample, long> selector)
    {
        int k = Math.Min(3, samples.Count);
        var tail = new long[k];
        for (int i = 0; i < k; i++)
            tail[i] = selector(samples[samples.Count - k + i]);
        Array.Sort(tail);
        return tail[tail.Length / 2];
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
