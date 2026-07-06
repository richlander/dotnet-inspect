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
        };

        var samples = LeakWatchCountersCsv.Parse(lines);

        // Only the timestamp carrying a working-set reading yields a sample.
        Assert.Single(samples);
        Assert.Equal(200, samples[0].WorkingSet);
    }
}
