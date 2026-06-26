using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins reconstruction-closure (cluster) capture (#1412): instead of
/// reconstructing the whole module, the fidelity check reconstructs only the
/// types a target transitively needs (grown compile-driven from compiler
/// "type/member not found" diagnostics), falling back to the whole-module build
/// on bail. Two properties matter and are pinned here against
/// <see cref="FidelityCheck.ClusterMode.ForceAll"/> (the closure attempted for
/// every row, so the engine is exercised on the fixture rather than only on the
/// unrelated-sibling-poisoned rows that happen to exist in a given corpus):
///
/// 1. <b>It never changes a result.</b> For a method the whole-module build
///    already classifies, the closure must reconstruct the same body to the same
///    IL, so the opcode <i>status</i> is identical — the capture is an
///    optimisation that rescues poisoned rows, never a path that can flip a
///    verdict. A divergence here is a real closure bug.
/// 2. <b>The closure path actually runs.</b> At least one fixture row is captured
///    with <see cref="FidelityCheck.CaptureMode.Cluster"/> provenance and an
///    <c>Exact</c> status — proving a real result came from the closure and not
///    from the whole-module fallback (without this the suite would pass even if
///    the closure always bailed).
/// </summary>
[Trait("Speed", "Slow")]
public class ClusterCaptureTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.CfgSampleClass";

    [Fact]
    public void ClusterCapture_NeverChangesAResult_AndTheClosurePathRuns()
    {
        string assembly = typeof(CfgSampleClass).Assembly.Location;

        var whole = FidelityCheck.Evaluate(assembly, lowered: false, FidelityCheck.ClusterMode.Off)
            .Where(r => r.Type == FixtureType)
            .ToDictionary(r => (r.Method, r.Overload), r => r.Status);
        var forced = FidelityCheck.Evaluate(assembly, lowered: false, FidelityCheck.ClusterMode.ForceAll)
            .Where(r => r.Type == FixtureType)
            .ToList();

        Assert.NotEmpty(whole);
        Assert.NotEmpty(forced);

        // 1. The closure never changes a whole-module verdict.
        foreach (var row in forced)
            Assert.Equal(whole[(row.Method, row.Overload)], row.Status);

        // 2. The closure path genuinely produced a checkable result (not fallback).
        Assert.Contains(forced, r =>
            r.Capture == FidelityCheck.CaptureMode.Cluster &&
            r.Status == FidelityCheck.CompileBackStatus.Exact);
    }
}
