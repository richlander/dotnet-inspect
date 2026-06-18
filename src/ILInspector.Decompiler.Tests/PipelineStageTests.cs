using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class PipelineStageTests
{
    static IrFunction ImportFixture(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        return function;
    }

    [Fact]
    public void RunWithStages_CapturesImportPlusEveryPass()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));

        var stages = IrPasses.RunWithStages(function);

        // One snapshot for the importer output, then one per pass — the count is
        // the pass list length plus the import boundary.
        Assert.Equal(IrPasses.Default.Length + 1, stages.Count);
        Assert.Equal(IrPasses.ImportStageName, stages[0].PassName);
        Assert.Equal(
            IrPasses.Default.Select(p => p.Name),
            stages.Skip(1).Select(s => s.PassName));
        Assert.All(stages, s => Assert.False(string.IsNullOrWhiteSpace(s.Projection)));
    }

    [Fact]
    public void RunWithStages_AppliesTheSamePassesAsRun()
    {
        // The staged run must leave the function in the same final state as the
        // plain run — staging is observation, not a different pipeline.
        var staged = ImportFixture(nameof(CfgSampleClass.Add));
        var plain = ImportFixture(nameof(CfgSampleClass.Add));

        IrPasses.RunWithStages(staged);
        IrPasses.Run(plain);

        Assert.Equal(IrPrinter.Dump(plain), IrPrinter.Dump(staged));
    }

    [Fact]
    public void RunWithStages_CapturesFidelityPerStage()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));

        var stages = IrPasses.RunWithStages(function);

        // The final captured fidelity matches the function's own computation.
        Assert.Equal(function.Fidelity, stages[^1].Fidelity);
    }

    [Fact]
    public void StageDump_Format_FramesEveryStageWithAHeader()
    {
        var function = ImportFixture(nameof(CfgSampleClass.Add));
        var stages = IrPasses.RunWithStages(function);

        string text = StageDump.Format(stages);

        Assert.Contains("==== IR (typed tree after import) ====", text);
        foreach (var pass in IrPasses.Default)
            Assert.Contains($"==== IR (after {pass.Name}) ====", text);
    }

    [Fact]
    public void StageDump_Title_NamesTheImportBoundary()
    {
        Assert.Equal("IR (typed tree after import)", StageDump.Title(IrPasses.ImportStageName));
        Assert.Equal("IR (after my-pass)", StageDump.Title("my-pass"));
    }

    [Fact]
    public void DumpMethod_IrTreeView_FramesImportPassesAndCSharp()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);

        var result = StageDump.DumpMethod(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Add), StageDumpView.IrTree);

        Assert.True(result.Succeeded);
        Assert.Contains("==== IR (typed tree after import) ====", result.Output);
        Assert.Contains("==== C# (lowered; structure not yet raised) ====", result.Output);
        // The Ir-tree view does not include the annotated-IL import sections.
        Assert.DoesNotContain("==== IL (raw) ====", result.Output);
    }

    [Fact]
    public void DumpMethod_FullView_PrependsAnnotatedIlImportViews()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);

        var result = StageDump.DumpMethod(
            source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Add), StageDumpView.Full);

        Assert.True(result.Succeeded);
        Assert.Contains("==== IL (raw) ====", result.Output);
        Assert.Contains("==== IL (typed: per-instruction stack states) ====", result.Output);
        Assert.Contains("==== IL (structured: blocks + exception regions) ====", result.Output);
        Assert.Contains("==== IR (typed tree after import) ====", result.Output);
        // IL import views come before the IR-tree stages.
        Assert.True(result.Output!.IndexOf("==== IL (raw) ====", StringComparison.Ordinal)
            < result.Output.IndexOf("==== IR (typed tree after import) ====", StringComparison.Ordinal));
    }

    [Fact]
    public void DumpMethod_UnknownMethod_FailsWithDiagnostic()
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);

        var result = StageDump.DumpMethod(source, typeof(CfgSampleClass).FullName!, "NoSuchMethod");

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Diagnostics);
    }
}
