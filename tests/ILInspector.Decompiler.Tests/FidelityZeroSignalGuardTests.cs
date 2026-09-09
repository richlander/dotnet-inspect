using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class FidelityZeroSignalGuardTests
{
    [Fact]
    public void Observe_StopsWhenProbeHasNoUsefulRowsAndDominantFailure()
    {
        var guard = new FidelityCheck.ZeroSignalGuard(probeCount: 100, requestedCap: 4000);

        Assert.Equal(100, guard.EffectiveCap(total: 0));

        guard.Observe(
            total: 100,
            exact: 0,
            diffCount: 0,
            recompileFail: 100,
            contextFail: 0,
            recompileFailCodes: new Dictionary<string, int> { ["CS0234"] = 94, ["CS1525"] = 6 });

        Assert.True(guard.Stopped);
        Assert.Equal(100, guard.StopCount);
        Assert.Equal("CS0234", guard.DominantBucket);
        Assert.Equal(94, guard.DominantCount);
    }

    [Fact]
    public void Observe_ContinuesWhenProbeFindsUsefulRows()
    {
        var guard = new FidelityCheck.ZeroSignalGuard(probeCount: 100, requestedCap: 4000);

        guard.Observe(
            total: 100,
            exact: 1,
            diffCount: 0,
            recompileFail: 99,
            contextFail: 0,
            recompileFailCodes: new Dictionary<string, int> { ["CS0234"] = 99 });

        Assert.False(guard.Stopped);
        Assert.True(guard.ProbeCompleted);
        Assert.Equal(4000, guard.EffectiveCap(total: 100));
    }

    [Fact]
    public void Observe_ContinuesWhenFailureBucketDoesNotDominate()
    {
        var guard = new FidelityCheck.ZeroSignalGuard(probeCount: 100, requestedCap: 4000);

        guard.Observe(
            total: 100,
            exact: 0,
            diffCount: 0,
            recompileFail: 100,
            contextFail: 0,
            recompileFailCodes: new Dictionary<string, int> { ["CS0234"] = 89, ["CS1525"] = 11 });

        Assert.False(guard.Stopped);
        Assert.True(guard.ProbeCompleted);
        Assert.Equal(4000, guard.EffectiveCap(total: 100));
    }

    [Fact]
    public void Observe_StopsWhenAllRowsAreContextFail()
    {
        var guard = new FidelityCheck.ZeroSignalGuard(probeCount: 100, requestedCap: 4000);

        guard.Observe(
            total: 100,
            exact: 0,
            diffCount: 0,
            recompileFail: 0,
            contextFail: 100,
            recompileFailCodes: new Dictionary<string, int>());

        Assert.True(guard.Stopped);
        Assert.Equal("context-fail", guard.DominantBucket);
        Assert.Equal(100, guard.DominantCount);
    }

    [Fact]
    public void Observe_StopsWhenContextFailDominatesMixedFailures()
    {
        var guard = new FidelityCheck.ZeroSignalGuard(probeCount: 100, requestedCap: 4000);

        guard.Observe(
            total: 100,
            exact: 0,
            diffCount: 0,
            recompileFail: 1,
            contextFail: 99,
            recompileFailCodes: new Dictionary<string, int> { ["CS0103"] = 1 });

        Assert.True(guard.Stopped);
        Assert.Equal("context-fail", guard.DominantBucket);
        Assert.Equal(99, guard.DominantCount);
    }

    [Fact]
    public void Observe_RerunsWithoutGuardWhenProbeDoesNotStop()
    {
        var guard = new FidelityCheck.ZeroSignalGuard(probeCount: 100, requestedCap: 4000);

        guard.Observe(
            total: 100,
            exact: 1,
            diffCount: 0,
            recompileFail: 99,
            contextFail: 0,
            recompileFailCodes: new Dictionary<string, int> { ["CS0234"] = 99 });

        Assert.True(guard.ShouldRerunWithoutGuard);
    }
}
