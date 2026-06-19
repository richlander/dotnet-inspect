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
    public void CommonExitMerge_RecordsForwardBranchBail()
    {
        // Two nested guards both `goto done` onto a shared exit past the region
        // whose merge is not a short return tail (it ends in a guard, so the
        // return-merge pass leaves it): the index-range model cannot express the
        // merge, so the container stays flat and the sink records exactly why.
        var diag = RunWithDiagnostics(nameof(CfgSampleClass.GotoCommonExitGuardedMerge));

        Assert.Equal(0, diag.Structured);
        Assert.Equal("forward-branch-not-region-exit", Assert.Single(diag.Bails));
    }

    [Theory]
    [InlineData(nameof(CfgSampleClass.TripleAnd))]
    [InlineData(nameof(CfgSampleClass.IfAnd))]
    [InlineData(nameof(CfgSampleClass.IfOr))]
    public void GuardChain_StructuresCleanlyWithNoBail(string methodName)
    {
        // &&/|| guard chains are in the slice today: the container structures and
        // the sink records the success, never a bail.
        var diag = RunWithDiagnostics(methodName);

        Assert.True(diag.Structured > 0);
        Assert.Empty(diag.Bails);
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
}
