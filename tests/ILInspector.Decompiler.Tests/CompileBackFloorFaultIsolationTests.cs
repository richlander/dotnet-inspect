using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates the invariant that a compile-back floor never carries fault-isolation
/// evidence forward from the RTS compile it supersedes (#3783).
/// </summary>
/// <remarks>
/// <see cref="ReturnToSender.Result.FaultIsolation"/> is produced only by
/// <see cref="ReturnToSender.TryIsolateRecompileFailure"/>, on a result whose
/// status is <see cref="FidelityCheck.CompileBackStatus.RecompileFail"/>. The
/// floor replaces that status with an independent compile-back verdict, so a
/// retained isolation would attribute a fault to a row that compiled.
/// </remarks>
[Trait("Area", "RoundTrip")]
public class CompileBackFloorFaultIsolationTests
{
    [Fact]
    public void WithCompileBackFloor_ClearsFaultIsolationFromTheSupersededCompile()
    {
        var failed = FailedResult(new ReturnToSender.FaultIsolationResult(
            ReturnToSender.FaultIsolationKind.ShellOrClosureDefect,
            "/src/Widget.cs",
            "CS0246: missing closure type"));

        var rescued = ReturnToSender.WithCompileBackFloor(failed, Floor(FidelityCheck.CompileBackStatus.Exact));

        Assert.Null(rescued.FaultIsolation);
        Assert.True(rescued.UsedCompileBackFloor);
        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, rescued.Status);
    }

    [Fact]
    public void WithCompileBackFloor_ClearsBodyDefectIsolationToo()
    {
        var failed = FailedResult(new ReturnToSender.FaultIsolationResult(
            ReturnToSender.FaultIsolationKind.BodyDefect,
            "/src/Widget.cs",
            "authored body compiled in the same RTS shell"));

        var rescued = ReturnToSender.WithCompileBackFloor(failed, Floor(FidelityCheck.CompileBackStatus.OpcodeDiff));

        Assert.Null(rescued.FaultIsolation);
    }

    [Fact]
    public void WithCompileBackFloor_RecordsTheSupersededVerdictAsProvenance()
    {
        var failed = FailedResult(new ReturnToSender.FaultIsolationResult(
            ReturnToSender.FaultIsolationKind.ShellOrClosureDefect,
            "/src/Widget.cs",
            "CS0246: missing closure type")
        {
            Method = ReturnToSender.FaultIsolationMethod.SpanMeasured,
        });

        var rescued = ReturnToSender.WithCompileBackFloor(failed, Floor(FidelityCheck.CompileBackStatus.Exact));

        Assert.Contains("superseded-fault-isolation: ShellOrClosureDefect (SpanMeasured)", rescued.Detail);
    }

    /// <summary>
    /// Negative case for the provenance marker only: it must be emitted conditionally,
    /// not unconditionally. This test deliberately does not assert that
    /// <c>FaultIsolation</c> is null — with a null input that would hold with or
    /// without the clearing, and the two tests above are what gate the clearing.
    /// </summary>
    [Fact]
    public void WithCompileBackFloor_LeavesDetailUnmarkedWhenNothingWasIsolated()
    {
        var failed = FailedResult(faultIsolation: null);

        var rescued = ReturnToSender.WithCompileBackFloor(failed, Floor(FidelityCheck.CompileBackStatus.Exact));

        Assert.DoesNotContain("superseded-fault-isolation", rescued.Detail);
    }

    static ReturnToSender.Result FailedResult(ReturnToSender.FaultIsolationResult? faultIsolation)
        => new(
            new CompileBackReconstructionPlan(
                "/tmp/Widget.dll",
                new CompileBackMethodIdentity("Widgets.Widget", "Spin", 0, "()"),
                new CompileBackModuleRequirement([], [], []),
                [],
                [],
                []),
            "class Widget { void Spin() { } }",
            FidelityCheck.CompileBackStatus.RecompileFail,
            OriginalOpcodes: "nop ret",
            RecompiledOpcodes: "",
            Detail: "CS0103: the name 'q' does not exist",
            FaultIsolation: faultIsolation);

    static FidelityCheck.CompileBackResult Floor(FidelityCheck.CompileBackStatus status)
        => new(
            "Widgets.Widget",
            "Spin",
            0,
            "()",
            status,
            OriginalOpcodes: "nop ret",
            RecompiledOpcodes: "nop ret",
            Detail: null);
}
