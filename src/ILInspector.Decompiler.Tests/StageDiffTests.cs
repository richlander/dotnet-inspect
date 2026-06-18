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

    [Fact]
    public void PassesThatChanged_ReturnsOnlyPassesThatAlteredProjection()
    {
        var changed = StageDump.PassesThatChanged(
        [
            Stage("(import)", "a\nb\nc\n"),
            Stage("identity-convert", "a\nb\nc\n"),   // no change
            Stage("property-sugar", "a\nB\nc\n"),     // changed b -> B
            Stage("structuring", "a\nB\nc\n"),         // no change
        ]);

        Assert.Equal(["property-sugar"], changed);
    }

    [Fact]
    public void PassesThatChanged_IncludesRepeatedPassWhenAnyOccurrenceChanges()
    {
        var changed = StageDump.PassesThatChanged(
        [
            Stage("(import)", "a\n"),
            Stage("expression-inlining", "a\n"),       // first run: no change
            Stage("typed-constants", "a\n"),
            Stage("expression-inlining", "b\n"),       // second run: changed
        ]);

        Assert.Contains("expression-inlining", changed);
    }

    [Fact]
    public void FormatPassDiff_ShowsOnlyHunksForNamedPass()
    {
        var diff = StageDump.FormatPassDiff(
        [
            Stage("(import)", "a\nb\nc\n"),
            Stage("property-sugar", "a\nB\nc\n"),      // changed
            Stage("structuring", "a\nB\nX\n"),          // changed, but not requested
        ], "property-sugar");

        Assert.Contains("- b", diff);
        Assert.Contains("+ B", diff);
        // The structuring change must not leak into a property-sugar diff.
        Assert.DoesNotContain("X", diff);
    }

    [Fact]
    public void FormatPassDiff_EmitsHunkPerChangingOccurrenceOfRepeatedPass()
    {
        var diff = StageDump.FormatPassDiff(
        [
            Stage("(import)", "a\n"),
            Stage("expression-inlining", "b\n"),       // first run changed a -> b
            Stage("typed-constants", "b\n"),
            Stage("expression-inlining", "c\n"),        // second run changed b -> c
        ], "expression-inlining");

        Assert.Contains("+ b", diff);
        Assert.Contains("+ c", diff);
    }

    [Fact]
    public void FormatPassDiff_OmitsUnchangedOccurrences()
    {
        var diff = StageDump.FormatPassDiff(
        [
            Stage("(import)", "a\n"),
            Stage("property-sugar", "a\n"),            // no change
        ], "property-sugar");

        Assert.Equal("", diff);
    }
}
