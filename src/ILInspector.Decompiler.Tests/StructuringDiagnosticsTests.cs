using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StructuringDiagnosticsTests
{
    static StructuringDiagnostics RunWithDiagnostics(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        var diagnostics = new StructuringDiagnostics();
        IrPasses.Run(function, IrPasses.Default, new PassContext(new Stepper(enabled: false), diagnostics));
        return diagnostics;
    }

    [Fact]
    public void CommonExitMerge_RecordsForwardBranchStop()
    {
        // Two nested guards both `goto done` onto a shared exit past the region
        // whose merge is not a short return tail (it ends in a guard, so the
        // return-merge pass leaves it): the index-range model cannot express the
        // merge, so the container stays flat and the sink records exactly why.
        var diag = RunWithDiagnostics(nameof(CfgSampleClass.GotoCommonExitGuardedMerge));

        Assert.Equal(0, diag.Structured);
        Assert.Equal("forward-branch-not-region-exit", Assert.Single(diag.Stops));
    }

    [Theory]
    [InlineData(nameof(CfgSampleClass.TripleAnd))]
    [InlineData(nameof(CfgSampleClass.IfAnd))]
    [InlineData(nameof(CfgSampleClass.IfOr))]
    public void GuardChain_StructuresCleanlyWithNoStop(string methodName)
    {
        // &&/|| guard chains are in the slice today: the container structures and
        // the sink records the success, never a stop.
        var diag = RunWithDiagnostics(methodName);

        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Stops);
    }

    [Fact]
    public void NoSink_DefaultRunMatchesSinkRun()
    {
        // The sink is optional: a normal run (PassContext.None, null sink) records
        // nothing and behaves identically — the structuring decision is unchanged.
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var withSink = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.GotoCommonExit))!;
        var withoutSink = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.GotoCommonExit))!;

        IrPasses.Run(withSink, IrPasses.Default, new PassContext(new Stepper(enabled: false), new StructuringDiagnostics()));
        IrPasses.Run(withoutSink);  // PassContext.None — no sink

        Assert.Equal(IrPrinter.Dump(withoutSink), IrPrinter.Dump(withSink));
    }

    [Fact]
    public void EarlyBreakFromInnerTry_StructuresTryBody_NoTerminatorSurvivorStop()
    {
        // A nested foreach whose inner loop early-`break`s lowers to a non-tail
        // `leave` that exits the inner try (the enumerator-dispose finally) to the
        // `if (!matched)` continuation — the surviving-leave shape of
        // HashSetEqualityComparer::Equals. The structuring pass treats a leave
        // that exits its container as a clean path terminator, so the inner try
        // body raises into `while (...) { if (...) { ...; goto ...; } }` instead
        // of bailing flat with `eh-terminator-survivor`. The outer body keeps the
        // leave's target label, so it stays flat (leave-target-in-container) — the
        // remaining, deliberately conservative, stop.
        var diag = RunWithDiagnostics(nameof(CfgSampleClass.AllOuterMatchInner));

        Assert.DoesNotContain("eh-terminator-survivor", diag.Stops);
        Assert.True(diag.Structured > 0);
        Assert.Equal("leave-target-in-container", Assert.Single(diag.Stops));
    }

    [Fact]
    public void EarlyBreakFromInnerTry_RaisesWhileLoopOverFlatGotoSoup()
    {
        // The readability win: the inner try body is a structured `while` whose
        // early exit is the surviving `goto ...; // leave`, not a flat chain of
        // gotos. Fidelity stays Full (the leave still renders the same goto).
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.AllOuterMatchInner));
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal(DecompilationFidelity.Full, result.Fidelity);

        var output = result.Output!.ReplaceLineEndings("\n");
        Assert.Contains("while (", output);
        Assert.Contains("// leave", output);
    }
}
