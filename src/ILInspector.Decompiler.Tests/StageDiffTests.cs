using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class StageDiffTests
{
    static PipelineStage Stage(string name, string projection) =>
        new(name, projection, DecompilationFidelity.Full);

    [Fact]
    public void FormatDiff_ShowsFirstStageInFull()
    {
        var diff = StageDump.FormatDiff([Stage("(import)", "a\nb\nc\n")]);

        Assert.Contains("==== IR (typed tree after import) ====", diff);
        Assert.Contains("a\nb\nc", diff);
    }

    [Fact]
    public void FormatDiff_CollapsesUnchangedStages()
    {
        var diff = StageDump.FormatDiff(
        [
            Stage("(import)", "a\nb\nc\n"),
            Stage("identity-convert", "a\nb\nc\n"),
        ]);

        Assert.Contains("==== IR (after identity-convert) (no change) ====", diff);
    }

    [Fact]
    public void FormatDiff_ShowsChangedLinesAsUnifiedHunk()
    {
        var diff = StageDump.FormatDiff(
        [
            Stage("(import)", "a\nb\nc\n"),
            Stage("property-sugar", "a\nB\nc\n"),
        ]);

        // The changed stage is not collapsed, and the delta shows the swapped line.
        Assert.Contains("==== IR (after property-sugar) ====", diff);
        Assert.DoesNotContain("(after property-sugar) (no change)", diff);
        Assert.Contains("- b", diff);
        Assert.Contains("+ B", diff);
        // Context line is retained; the far context is elided.
        Assert.Contains("  a", diff);
    }
}
