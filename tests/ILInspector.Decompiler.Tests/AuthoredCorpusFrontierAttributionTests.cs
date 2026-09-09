using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Corpus")]
public sealed class AuthoredCorpusFrontierAttributionTests
{
    [Fact]
    public void MethodologyV3_IsTheFrontierAttributionVersion()
    {
        Assert.Equal(3, AuthoredCorpusMethodology.Version);
        Assert.Equal(AuthoredCorpusMethodology.Version, AuthoredCorpusBenchmark.MethodologyVersion);
    }

    [Fact]
    public void FrontierIlDiffBreakdown_PartitionsProductHarnessFloorAndUnclassified()
    {
        ReturnToSenderSourceProbeResult[] rows =
        [
            Frontier(
                ReturnToSender.FaultIsolationKind.BodyDefect,
                ReturnToSender.FaultIsolationMethod.FidelityControl),
            Frontier(
                ReturnToSender.FaultIsolationKind.ShellOrClosureDefect,
                ReturnToSender.FaultIsolationMethod.FidelityControl),
            Frontier(
                ReturnToSender.FaultIsolationKind.BodyDefect,
                ReturnToSender.FaultIsolationMethod.FidelityControl,
                usedCompileBackFloor: true),
            Frontier(
                ReturnToSender.FaultIsolationKind.BodyDefect,
                ReturnToSender.FaultIsolationMethod.SubstitutionControl),
            Frontier(faultKind: null, faultMethod: null),
            Exact(),
        ];

        var breakdown = AuthoredCorpusBenchmark.FrontierIlDiffBreakdown(rows);

        Assert.Equal(5, breakdown.Total);
        Assert.Equal(1, breakdown.ProductBodyDefect);
        Assert.Equal(1, breakdown.HarnessShellReconstruction);
        Assert.Equal(1, breakdown.CompileBackFloor);
        Assert.Equal(2, breakdown.Unclassified);
        Assert.True(breakdown.PartitionClosed);
    }

    /// <summary>
    /// A floor-backed row may carry a superseded RTS verdict, but its headline
    /// ValidDifferent status came from CompileBack. It stays in the explicit floor
    /// bucket rather than becoming product evidence for the successful RTS control.
    /// </summary>
    [Fact]
    public void FrontierIlDiffBreakdown_DoesNotMakeCompileBackFloorLoadBearing()
    {
        var row = Frontier(
            ReturnToSender.FaultIsolationKind.BodyDefect,
            ReturnToSender.FaultIsolationMethod.FidelityControl,
            usedCompileBackFloor: true) with
        {
            SupersededFaultIsolationKind = ReturnToSender.FaultIsolationKind.BodyDefect,
            SupersededFaultIsolationMethod = ReturnToSender.FaultIsolationMethod.SubstitutionControl,
        };

        var breakdown = AuthoredCorpusBenchmark.FrontierIlDiffBreakdown([row]);

        Assert.Equal(0, breakdown.ProductBodyDefect);
        Assert.Equal(1, breakdown.CompileBackFloor);
    }

    static ReturnToSenderSourceProbeResult Frontier(
        ReturnToSender.FaultIsolationKind? faultKind,
        ReturnToSender.FaultIsolationMethod? faultMethod,
        bool usedCompileBackFloor = false)
        => new(
            new ReturnToSender.RequestedTarget("C", "M", 0),
            ReturnToSenderSourceOutcome.ValidDifferent,
            FidelityCheck.CompileBackStatus.OpcodeDiff,
            "valid_different.semantic_opcode_diff.syntax",
            Detail: null,
            SourcePath: "Fixture.cs",
            ExpectedBody: "return 1;",
            ActualBody: "return 2;",
            FaultIsolationKind: faultKind,
            FaultIsolationMethod: faultMethod,
            UsedCompileBackFloor: usedCompileBackFloor);

    static ReturnToSenderSourceProbeResult Exact()
        => new(
            new ReturnToSender.RequestedTarget("C", "M", 0),
            ReturnToSenderSourceOutcome.ValidDifferent,
            FidelityCheck.CompileBackStatus.Exact,
            "valid_different.source_shape_frontier.syntax.exact",
            Detail: null,
            SourcePath: "Fixture.cs",
            ExpectedBody: "return 1;",
            ActualBody: "return (1);");
}
