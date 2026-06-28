using System.Collections.Generic;

using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class AnalysisCorpusSensorTests
{
    static AssemblyMetrics Asm(string name, bool opened = true, bool timedOut = false, int methods = 10, int diagnostics = 0, int allocations = 5, params (string Shape, int Count)[] shapes)
        => new(name, opened, timedOut, methods, diagnostics, allocations, 0,
            shapes.ToDictionary(s => s.Shape, s => s.Count));

    [Fact]
    public void Diff_IdenticalSnapshots_NoRegressionNoDrift()
    {
        var snap = new CorpusSnapshot([Asm("A.dll"), Asm("B.dll", shapes: ("box-value-type", 3))]);
        var diff = AnalysisCorpusSensor.Diff(snap, snap);
        Assert.False(diff.HasRegression);
        Assert.Empty(diff.Drift);
    }

    [Fact]
    public void Diff_AssemblyStopsOpening_IsRegression()
    {
        var baseline = new CorpusSnapshot([Asm("A.dll")]);
        var current = new CorpusSnapshot([Asm("A.dll", opened: false)]);
        Assert.True(AnalysisCorpusSensor.Diff(baseline, current).HasRegression);
    }

    [Fact]
    public void Diff_DiagnosticsIncrease_IsRegression()
    {
        var baseline = new CorpusSnapshot([Asm("A.dll", diagnostics: 0)]);
        var current = new CorpusSnapshot([Asm("A.dll", diagnostics: 2)]);
        Assert.True(AnalysisCorpusSensor.Diff(baseline, current).HasRegression);
    }

    [Fact]
    public void Diff_TimeoutThenMeasured_IsNotRegression()
    {
        // A previously timed-out assembly that now measures is an improvement, not a regression.
        var baseline = new CorpusSnapshot([Asm("Big.dll", opened: false, timedOut: true, methods: 0)]);
        var current = new CorpusSnapshot([Asm("Big.dll", methods: 9000)]);
        var diff = AnalysisCorpusSensor.Diff(baseline, current);
        Assert.False(diff.HasRegression);
    }

    [Fact]
    public void Diff_SignalCountMovement_IsDriftNotRegression()
    {
        var baseline = new CorpusSnapshot([Asm("A.dll", allocations: 5, shapes: ("box-value-type", 3))]);
        var current = new CorpusSnapshot([Asm("A.dll", allocations: 4, shapes: ("box-value-type", 5))]);
        var diff = AnalysisCorpusSensor.Diff(baseline, current);
        Assert.False(diff.HasRegression);
        Assert.NotEmpty(diff.Drift);
    }

    [Fact]
    public void Diff_AddedOrRemoved_IsNotRegression()
    {
        var baseline = new CorpusSnapshot([Asm("A.dll")]);
        var current = new CorpusSnapshot([Asm("A.dll"), Asm("New.dll")]);
        var diff = AnalysisCorpusSensor.Diff(baseline, current);
        Assert.False(diff.HasRegression);
        Assert.Single(diff.AddedOrRemoved);
    }

    [Fact]
    public void Diff_TimeoutThenMeasuredWithDiagnostics_IsNotRegression()
    {
        // A previously timed-out assembly that now measures, even WITH diagnostics, is an
        // improvement: diagnostics are only compared when both baseline and current opened.
        var baseline = new CorpusSnapshot([Asm("Big.dll", opened: false, timedOut: true, methods: 0, diagnostics: 0)]);
        var current = new CorpusSnapshot([Asm("Big.dll", methods: 9000, diagnostics: 4)]);
        Assert.False(AnalysisCorpusSensor.Diff(baseline, current).HasRegression);
    }

    [Fact]
    public void JsonRoundTrip_PreservesMetrics()
    {
        var snap = new CorpusSnapshot([Asm("A.dll", methods: 42, allocations: 7, shapes: ("small-array", 2))]);
        var back = AnalysisCorpusSensor.FromJson(AnalysisCorpusSensor.ToJson(snap));
        Assert.Equal(42, back.Assemblies[0].Methods);
        Assert.Equal(2, back.Assemblies[0].Shapes["small-array"]);
    }
}
