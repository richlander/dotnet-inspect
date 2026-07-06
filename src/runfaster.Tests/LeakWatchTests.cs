using System.Collections.Generic;
using Xunit;

namespace runfaster.Tests;

public class LeakWatchTests
{
    const long MB = 1024L * 1024;
    const long GB = 1024L * MB;

    static LeakWatchSample Sample(
        double seconds,
        long workingSet,
        long liveHeap,
        long gen2,
        long allocated = 0,
        double gen2Collections = 0,
        double gcPause = 0,
        int threads = 8)
        => new(seconds, workingSet, liveHeap, gen2, workingSet, allocated, gen2Collections, gcPause, threads);

    [Fact]
    public void Analyze_HealthyTrajectory_WorkingSetTracksHeap()
    {
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 400 * MB, 300 * MB, 200 * MB),
            Sample(2, 420 * MB, 310 * MB, 210 * MB),
            Sample(4, 410 * MB, 305 * MB, 205 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.Healthy, result.Verdict);
    }

    [Fact]
    public void Analyze_NativeGrowth_WorkingSetStaysAboveHeapAfterCollection()
    {
        // Live heap climbs then a gen2 collection reclaims it, but the working set
        // never comes back down — the RSS-doesn't-follow-heap signature.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 3 * GB, 3 * GB, 3 * GB, allocated: 1 * GB, gcPause: 0.5),
            Sample(2, 7 * GB, 4 * GB, 4 * GB, allocated: 2 * GB, gcPause: 0.6),
            Sample(4, 10 * GB, 4500 * MB, 4500 * MB, allocated: 2 * GB, gcPause: 0.7),
            // gen2 collection: gen2/live drop sharply, working set holds.
            Sample(6, 10 * GB, 600 * MB, 400 * MB, allocated: 2 * GB, gen2Collections: 1, gcPause: 0.6),
            Sample(8, 10 * GB, 700 * MB, 500 * MB, allocated: 2 * GB, gcPause: 0.5),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.NativeOrCommittedGrowth, result.Verdict);
        Assert.True(result.HeapReclaimedByGen2);
        // The retained floor is the post-collection working set, far above the live heap.
        Assert.True(result.RetainedFloorAfterGen2 - result.LiveHeapAtFloor > 5 * GB);
    }

    [Fact]
    public void Analyze_ManagedRetention_HeapGrowsAndSurvivesCollection()
    {
        // The live heap grows monotonically and no gen2 collection reclaims it: a
        // genuine managed leak. Working set tracks the heap (no native gap).
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 500 * MB, 400 * MB, 300 * MB, allocated: 200 * MB),
            Sample(2, 900 * MB, 800 * MB, 700 * MB, allocated: 200 * MB),
            Sample(4, 1400 * MB, 1300 * MB, 1200 * MB, allocated: 200 * MB),
            Sample(6, 1900 * MB, 1800 * MB, 1700 * MB, allocated: 200 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ManagedRetention, result.Verdict);
        Assert.False(result.HeapReclaimedByGen2);
    }

    [Fact]
    public void Analyze_ChurnStorm_HighAllocReclaimedWithoutNativeGap()
    {
        // High allocation churn with heavy GC pause, but a gen2 collection reclaims
        // the heap and the working set stays close to the (small) live heap.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 500 * MB, 350 * MB, 250 * MB, allocated: 3 * GB, gcPause: 0.6),
            Sample(2, 700 * MB, 600 * MB, 500 * MB, allocated: 3 * GB, gcPause: 0.7),
            Sample(4, 650 * MB, 200 * MB, 120 * MB, allocated: 3 * GB, gen2Collections: 1, gcPause: 0.6),
            Sample(6, 620 * MB, 250 * MB, 150 * MB, allocated: 3 * GB, gcPause: 0.6),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ChurnStorm, result.Verdict);
        Assert.True(result.HeapReclaimedByGen2);
    }

    [Fact]
    public void Analyze_TooFewSamples_IsInconclusive()
    {
        var result = LeakWatchAnalyzer.Analyze([Sample(0, 1 * GB, 500 * MB, 400 * MB)], LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.Inconclusive, result.Verdict);
    }

    [Fact]
    public void Analyze_CollectionBeforeLaterGrowth_IsManagedRetention_NotHealthy()
    {
        // A gen2 collection early, then the live heap grows monotonically past its old
        // peak: the early collection must NOT count as reclaiming the later growth.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 600 * MB, 400 * MB, 300 * MB, gen2Collections: 1),
            Sample(2, 900 * MB, 700 * MB, 600 * MB),
            Sample(4, 1400 * MB, 1200 * MB, 1100 * MB),
            Sample(6, 1900 * MB, 1700 * MB, 1600 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ManagedRetention, result.Verdict);
        Assert.False(result.HeapReclaimedByGen2);
    }

    [Fact]
    public void Analyze_StableHighNativeBaseline_IsHealthy_NotGrowth()
    {
        // Large fixed gap between working set and live heap, but flat: a native/committed
        // baseline, not growth. Must not be flagged NativeOrCommittedGrowth.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 1 * GB, 100 * MB, 80 * MB),
            Sample(2, 1 * GB, 100 * MB, 80 * MB),
            Sample(4, 1 * GB, 100 * MB, 80 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.Healthy, result.Verdict);
    }

    [Fact]
    public void Analyze_LeakFromNearZeroHeap_IsManagedRetention()
    {
        // A leak that starts from a near-empty heap must still be caught (no
        // multiply-by-zero blind spot in the growth test).
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 60 * MB, 4 * MB, 2 * MB),
            Sample(2, 700 * MB, 640 * MB, 600 * MB),
            Sample(4, 1400 * MB, 1300 * MB, 1200 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ManagedRetention, result.Verdict);
    }

    [Fact]
    public void Analyze_LeakAlongsideCollectibleChurn_IsManagedRetention()
    {
        // A real leak (100 MB → ~1 GB retained) plus transient churn on top. A gen2
        // collection reclaims the churn but the leak stays resident: the partial reclaim
        // must not mask the retained growth.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 300 * MB, 100 * MB, 80 * MB),
            Sample(2, 1600 * MB, 1200 * MB, 1100 * MB, allocated: 400 * MB, gen2Collections: 1),
            // churn (200 MB) reclaimed, but ~1 GB leak stays.
            Sample(4, 1500 * MB, 1000 * MB, 950 * MB, allocated: 400 * MB),
            Sample(6, 1550 * MB, 1050 * MB, 1000 * MB, allocated: 400 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ManagedRetention, result.Verdict);
        Assert.False(result.HeapReclaimedByGen2);
    }

    [Fact]
    public void Analyze_LargeHeapRetainedBelowDoubling_IsManagedRetention()
    {
        // GPT: a large existing heap can retain multiple GB without doubling. Growth of
        // 10 GB → 15 GB (+5 GB) with the working set tracking it is managed retention,
        // not Healthy — the ×2 factor must not gate out large absolute retention.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 10500 * MB, 10000 * MB, 9500 * MB),
            Sample(2, 13000 * MB, 12500 * MB, 12000 * MB),
            Sample(4, 15500 * MB, 15000 * MB, 14500 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ManagedRetention, result.Verdict);
    }

    [Fact]
    public void Analyze_PureNativeLeakOnFlatHeap_IsNativeOrCommittedGrowth()
    {
        // Gemini: the managed heap stays flat at 1 GB while the working set leaks from
        // 1 GB to 10 GB. A peak-anchored floor search saw a zero gap; steady-state must
        // see the 9 GB native gap.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 1024 * MB, 1000 * MB, 900 * MB),
            Sample(2, 4096 * MB, 1000 * MB, 900 * MB),
            Sample(4, 7168 * MB, 1000 * MB, 900 * MB),
            Sample(6, 10240 * MB, 1000 * MB, 900 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.NativeOrCommittedGrowth, result.Verdict);
    }

    [Fact]
    public void Analyze_SteadyLeakAfterEarlyChurnSpike_IsManagedRetention()
    {
        // Gemini: an early churn spike (100 MB → 10 GB → reclaimed to 100 MB) followed
        // by a steady uncollected 5 GB leak at the end. A peak-anchored model finds the
        // post-spike trough and declares reclaim; steady-state must see the end leak.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 400 * MB, 100 * MB, 80 * MB),
            Sample(2, 10240 * MB, 10000 * MB, 9500 * MB, allocated: 8000L * MB, gen2Collections: 1),
            Sample(4, 500 * MB, 100 * MB, 80 * MB, gen2Collections: 1),
            Sample(6, 5200 * MB, 5000 * MB, 4800 * MB),
            Sample(8, 5300 * MB, 5100 * MB, 4900 * MB),
            Sample(10, 5250 * MB, 5050 * MB, 4850 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ManagedRetention, result.Verdict);
    }

    [Fact]
    public void Analyze_CommittedGapAfterChurnSpike_IsNativeOrCommittedGrowth()
    {
        // Gemini: heap and WS both spike to 10 GB (churn); the heap correctly drops to
        // 1 GB but the working set stays at 5 GB — a 4 GB committed-memory gap. A
        // peak/peak gapRatio is 1.0 and misses it; steady-state gapRatio is 5.0.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 1024 * MB, 900 * MB, 800 * MB),
            Sample(2, 10240 * MB, 10000 * MB, 9500 * MB, allocated: 8000L * MB, gen2Collections: 1),
            Sample(4, 5120 * MB, 1000 * MB, 900 * MB, gen2Collections: 1),
            Sample(6, 5120 * MB, 1000 * MB, 900 * MB),
            Sample(8, 5120 * MB, 1000 * MB, 900 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.NativeOrCommittedGrowth, result.Verdict);
    }

    [Fact]
    public void Analyze_MonotonicTailAboveThreshold_IsManagedRetention()
    {
        // GPT: a monotonic tail (10 GB → 10.4 GB → 10.8 GB) crosses the retention
        // threshold at the FINAL sample. A middle-of-tail estimator would fall below it
        // and report Healthy (and spuriously imply reclaim); the last sample must win.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 10500 * MB, 10000 * MB, 9500 * MB),
            Sample(2, 10900 * MB, 10400 * MB, 9900 * MB),
            Sample(4, 11300 * MB, 10800 * MB, 10300 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ManagedRetention, result.Verdict);
        Assert.False(result.HeapReclaimedByGen2);
    }

    [Fact]
    public void Analyze_TerminalCollectionEndsTrace_IsNotManagedRetention()
    {
        // Gemini: a high-churn window whose FINAL sample is a forced collection (the GC
        // a gcdump capture triggers) that reclaims the heap to baseline. The last sample
        // holds the true reclaimed footprint; a middle-of-tail estimator would keep the
        // pre-collection peak and falsely report a managed leak.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 500 * MB, 100 * MB, 80 * MB),
            Sample(2, 10240 * MB, 10000 * MB, 9500 * MB, allocated: 8000L * MB, gcPause: 1.4),
            Sample(4, 10240 * MB, 10000 * MB, 9500 * MB, allocated: 8000L * MB, gcPause: 1.4),
            Sample(6, 500 * MB, 100 * MB, 80 * MB, gen2Collections: 1),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.NotEqual(LeakWatchVerdict.ManagedRetention, result.Verdict);
        Assert.NotEqual(LeakWatchVerdict.NativeOrCommittedGrowth, result.Verdict);
    }

    [Fact]
    public void Analyze_SlowLinearLeak_IsManagedRetention()
    {
        // Gemini: a slow linear leak climbing 100 MB → 600 MB → 1100 MB across the tail.
        // A middle-of-tail estimator evaluates 600 MB (below threshold) and misses the
        // 1 GB leak; the last sample captures its full size.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 200 * MB, 100 * MB, 80 * MB),
            Sample(2, 700 * MB, 600 * MB, 550 * MB),
            Sample(4, 1200 * MB, 1100 * MB, 1050 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ManagedRetention, result.Verdict);
    }

    [Fact]
    public void Analyze_ManagedGrowthUnderStableNativeBaseline_IsManagedRetention()
    {
        // GPT: a fixed native gap (constant ~3 GB) with the managed heap growing on top
        // (512 MB → 2 GB). The only growth is managed retention; a large-but-static
        // native gap must not steal the verdict.
        var samples = new List<LeakWatchSample>
        {
            Sample(0, 3584 * MB, 512 * MB, 400 * MB),
            Sample(2, 4096 * MB, 1024 * MB, 900 * MB),
            Sample(4, 5120 * MB, 2048 * MB, 1900 * MB),
        };

        var result = LeakWatchAnalyzer.Analyze(samples, LeakWatchThresholds.Default);

        Assert.Equal(LeakWatchVerdict.ManagedRetention, result.Verdict);
    }

    [Fact]
    public void CountersCsv_DropsTruncatedFinalBlockWithoutHeapRows()
    {
        // GPT + Gemini: a Ctrl+C capture leaves the final timestamp block with a
        // working-set row but no heap-generation rows. That block must be dropped, not
        // emitted with a live heap of 0 (which would invert the steady-state verdict).
        var csv = new[]
        {
            "Timestamp,Provider,Counter Name,Counter Type,Mean/Increment",
            "2026-01-01T00:00:00Z,System.Runtime,dotnet.process.memory.working_set (By),Metric,1000000000",
            "2026-01-01T00:00:00Z,System.Runtime,dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=gen2],Metric,900000000",
            "2026-01-01T00:00:02Z,System.Runtime,dotnet.process.memory.working_set (By),Metric,1100000000",
            "2026-01-01T00:00:02Z,System.Runtime,dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=gen2],Metric,1000000000",
            // Truncated final block: working set only, no heap rows.
            "2026-01-01T00:00:04Z,System.Runtime,dotnet.process.memory.working_set (By),Metric,1200000000",
        };

        var samples = LeakWatchCountersCsv.Parse(csv);

        Assert.Equal(2, samples.Count);
        Assert.All(samples, s => Assert.True(s.LiveHeap > 0));
    }

    [Fact]
    public void CountersCsv_ParsesGroupedTimestampsAndComputesLiveHeap()
    {
        var lines = new[]
        {
            "Timestamp,Provider,Counter Name,Counter Type,Mean/Increment",
            "07/06/2026 12:45:38,System.Runtime,dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=gen2],Metric,3000000000",
            "07/06/2026 12:45:38,System.Runtime,dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=loh],Metric,500000000",
            "07/06/2026 12:45:38,System.Runtime,dotnet.process.memory.working_set (By),Metric,8000000000",
            "07/06/2026 12:45:38,System.Runtime,dotnet.gc.heap.total_allocated (By / 2 sec),Metric,1000000000",
            "07/06/2026 12:45:40,System.Runtime,dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=gen2],Metric,3200000000",
            "07/06/2026 12:45:40,System.Runtime,dotnet.process.memory.working_set (By),Metric,9000000000",
        };

        var samples = LeakWatchCountersCsv.Parse(lines);

        Assert.Equal(2, samples.Count);
        Assert.Equal(8_000_000_000, samples[0].WorkingSet);
        Assert.Equal(3_500_000_000, samples[0].LiveHeap); // gen2 + loh
        Assert.Equal(1_000_000_000, samples[0].AllocatedInWindow);
        Assert.Equal(2.0, samples[1].SecondsFromStart, 3);
    }

    [Fact]
    public void CountersCsv_RowsWithoutWorkingSet_AreSkipped()
    {
        var lines = new[]
        {
            "Timestamp,Provider,Counter Name,Counter Type,Mean/Increment",
            "t1,System.Runtime,dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=gen2],Metric,100",
            "t2,System.Runtime,dotnet.process.memory.working_set (By),Metric,200",
            "t2,System.Runtime,dotnet.gc.last_collection.heap.size (By)[gc.heap.generation=gen2],Metric,150",
        };

        var samples = LeakWatchCountersCsv.Parse(lines);

        // t1 carries no working-set reading and is skipped; only the complete t2 block
        // (working set + heap) yields a sample.
        Assert.Single(samples);
        Assert.Equal(200, samples[0].WorkingSet);
    }
}
