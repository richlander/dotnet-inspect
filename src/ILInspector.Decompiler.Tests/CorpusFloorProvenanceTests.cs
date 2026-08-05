using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Gates that a corpus row records whether the compile-back floor superseded
/// the RTS compile, and what verdict it discarded (#3814).
/// </summary>
/// <remarks>
/// <para>
/// The floor is load-bearing on the headline: on the 2026-07-28 EVIL run it
/// supplied the reported verdict for 199 of 1,576 <c>ValidMatch</c> rows and
/// 1,068 of 5,244 <c>ValidDifferent</c> rows. Those rows are the inventory of
/// where RTS cannot yet stand alone, which is the measurement the
/// compile-back-to-RTS replacement is steered by.
/// </para>
/// <para>
/// Before #3798 a stale <c>faultIsolation</c> on a floor-status row was an
/// accidental marker of that substitution — sound, because isolation has one
/// producer and it runs only on a <c>RecompileFail</c> compile, but incomplete,
/// because a floor row whose isolation came back null was never marked. #3798
/// correctly cleared the stale field; these fields replace it with a complete
/// one.
/// </para>
/// </remarks>
[Trait("Area", "RoundTrip")]
public class CorpusFloorProvenanceTests
{
    [Fact]
    public void AFloorRescuedResultCarriesTheFloorMarkerAndTheSupersededVerdict()
    {
        var rescued = ReturnToSender.WithCompileBackFloor(
            FailedResult(new ReturnToSender.FaultIsolationResult(
                ReturnToSender.FaultIsolationKind.ShellOrClosureDefect,
                "/src/Widget.cs",
                "CS0246: missing closure type")),
            Floor(FidelityCheck.CompileBackStatus.Exact));

        Assert.True(rescued.UsedCompileBackFloor);
        Assert.Null(rescued.FaultIsolation);
        Assert.Equal(
            ReturnToSender.FaultIsolationKind.ShellOrClosureDefect,
            rescued.SupersededFaultIsolation?.Kind);
    }

    /// <summary>
    /// The distinguishing case the whole issue is about: a floor-rescued row and a
    /// row RTS handled unaided can report the same status and the same (absent)
    /// live isolation, so only the floor marker separates them.
    /// </summary>
    [Fact]
    public void AnUnaidedResultIsDistinguishableFromAFloorRescuedOneAtTheSameStatus()
    {
        var unaided = FailedResult(faultIsolation: null) with
        {
            Status = FidelityCheck.CompileBackStatus.Exact,
        };
        var rescued = ReturnToSender.WithCompileBackFloor(
            FailedResult(new ReturnToSender.FaultIsolationResult(
                ReturnToSender.FaultIsolationKind.ShellOrClosureDefect,
                "/src/Widget.cs",
                null)),
            Floor(FidelityCheck.CompileBackStatus.Exact));

        Assert.Equal(unaided.Status, rescued.Status);
        Assert.Equal(unaided.FaultIsolation, rescued.FaultIsolation);

        Assert.False(unaided.UsedCompileBackFloor);
        Assert.True(rescued.UsedCompileBackFloor);
        Assert.Null(unaided.SupersededFaultIsolation);
        Assert.NotNull(rescued.SupersededFaultIsolation);
    }

    [Fact]
    public void TheSupersededVerdictKeepsTheMethodThatMeasuredIt()
    {
        var rescued = ReturnToSender.WithCompileBackFloor(
            FailedResult(new ReturnToSender.FaultIsolationResult(
                ReturnToSender.FaultIsolationKind.BodyDefect,
                "/src/Widget.cs",
                null)
            {
                Method = ReturnToSender.FaultIsolationMethod.SpanMeasured,
            }),
            Floor(FidelityCheck.CompileBackStatus.OpcodeDiff));

        Assert.Equal(
            ReturnToSender.FaultIsolationMethod.SpanMeasured,
            rescued.SupersededFaultIsolation?.Method);
        Assert.Equal(
            ReturnToSender.FaultIsolationKind.BodyDefect,
            rescued.SupersededFaultIsolation?.Kind);
    }

    /// <summary>
    /// A row RTS handled unaided must not claim a superseded verdict, so the
    /// field cannot be read as "isolation once existed" on every row.
    /// </summary>
    [Fact]
    public void ARowWithNoDiscardedCompileCarriesNoSupersededVerdict()
    {
        var rescued = ReturnToSender.WithCompileBackFloor(
            FailedResult(faultIsolation: null),
            Floor(FidelityCheck.CompileBackStatus.Exact));

        Assert.True(rescued.UsedCompileBackFloor);
        Assert.Null(rescued.SupersededFaultIsolation);
    }

    /// <summary>
    /// Gates the projection itself: the probe must carry the floor facts from the
    /// RTS <see cref="ReturnToSender.Result"/> onto the emitted row. The tests
    /// above gate the <c>Result</c>, which a row could still fail to reflect.
    /// </summary>
    [Fact]
    public void TheProbeProjectsFloorProvenanceOntoTheRow()
    {
        var rescued = ReturnToSender.WithCompileBackFloor(
            FailedResult(new ReturnToSender.FaultIsolationResult(
                ReturnToSender.FaultIsolationKind.ShellOrClosureDefect,
                "/src/Widget.cs",
                null)
            {
                Method = ReturnToSender.FaultIsolationMethod.SpanMeasured,
            }),
            Floor(FidelityCheck.CompileBackStatus.Exact));
        var rows = new List<ReturnToSenderSourceProbeResult>();

        ReturnToSenderSourceProbe.AddProbeResult(rows, rescued, BareRow());

        var row = Assert.Single(rows);
        Assert.True(row.UsedCompileBackFloor);
        Assert.Equal(
            ReturnToSender.FaultIsolationKind.ShellOrClosureDefect,
            row.SupersededFaultIsolationKind);
        Assert.Equal(
            ReturnToSender.FaultIsolationMethod.SpanMeasured,
            row.SupersededFaultIsolationMethod);
        Assert.Null(row.FaultIsolationKind);
    }

    /// <summary>
    /// The negative half of the projection: an unaided result must not stamp the
    /// marker, so <c>usedCompileBackFloor</c> partitions rows rather than being
    /// true everywhere.
    /// </summary>
    [Fact]
    public void TheProbeLeavesAnUnaidedRowUnmarked()
    {
        var rows = new List<ReturnToSenderSourceProbeResult>();

        ReturnToSenderSourceProbe.AddProbeResult(rows, FailedResult(faultIsolation: null), BareRow());

        var row = Assert.Single(rows);
        Assert.False(row.UsedCompileBackFloor);
        Assert.Null(row.SupersededFaultIsolationKind);
        Assert.Null(row.SupersededFaultIsolationMethod);
    }

    static ReturnToSenderSourceProbeResult BareRow()
        => new(
            new ReturnToSender.RequestedTarget("Widgets.Widget", "Spin", 0),
            ReturnToSenderSourceOutcome.ValidMatch,
            FidelityCheck.CompileBackStatus.Exact,
            "valid_match",
            Detail: null,
            SourcePath: "/src/Widget.cs",
            ExpectedBody: "",
            ActualBody: "");

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
