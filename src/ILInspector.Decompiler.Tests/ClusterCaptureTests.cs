using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins reconstruction-closure (cluster) capture (#1412): instead of
/// reconstructing the whole module, the fidelity check reconstructs only the
/// types the target transitively needs (grown compile-driven from compiler
/// "type/member not found" diagnostics) and falls back to the whole-module build
/// on bail. For a method that already compiles whole-module the closure must
/// reconstruct the same body to the same IL, so the opcode outcome is identical —
/// the capture is an optimisation that greens unrelated-sibling-poisoned rows
/// without ever changing a result it already had.
/// </summary>
public class ClusterCaptureTests
{
    const string FixtureType = "ILInspector.Decompiler.Tests.CfgSampleClass";

    [Fact]
    public void ClusterCapture_MatchesWholeModuleForFixtures()
    {
        string assembly = typeof(CfgSampleClass).Assembly.Location;

        var whole = FidelityCheck.Evaluate(assembly, lowered: false, cluster: false)
            .Where(r => r.Type == FixtureType)
            .ToDictionary(r => (r.Method, r.Overload), r => r.Status);
        var cluster = FidelityCheck.Evaluate(assembly, lowered: false, cluster: true)
            .Where(r => r.Type == FixtureType)
            .ToDictionary(r => (r.Method, r.Overload), r => r.Status);

        Assert.NotEmpty(cluster);
        Assert.Contains(cluster.Values, status => status == FidelityCheck.CompileBackStatus.Exact);
        foreach (var (key, wholeStatus) in whole)
            Assert.Equal(wholeStatus, cluster[key]);
    }
}
