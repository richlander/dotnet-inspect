using DotnetInspector.Output;
using DotnetInspector.Views;
using ILInspector.Findings;

namespace DotnetInspector.Tests;

public class SourceTextDiffRendererTests
{
    [Fact]
    public void NormalVerbosity_ReportsFactualLineCounts()
    {
        SourceDiffOutput output = SourceTextDiffRenderer.CreateOutput(
            "one\nremoved\nlast",
            "one\nadded\nlast",
            "Before",
            "After");

        AssertField(output, "Added lines", "0");
        AssertField(output, "Removed lines", "0");
        AssertField(output, "Changed lines", "1 Before -> 1 After");
        AssertField(output, "Moved lines", "0 Before -> 0 After");
        Assert.DoesNotContain("--- Before", output.Content);
    }

    [Fact]
    public void NormalVerbosity_PreservesUnequalReplacementCardinalities()
    {
        SourceDiffOutput output = SourceTextDiffRenderer.CreateOutput(
            "before one\nbefore two\n",
            "after one\nafter two\nafter three\n",
            "Before",
            "After");

        AssertField(output, "Changed lines", "2 Before -> 3 After");
        AssertField(output, "Added lines", "0");
        AssertField(output, "Removed lines", "0");
    }

    [Theory]
    [InlineData("stable\n", "stable\nadded\n", "Added lines", "1")]
    [InlineData("stable\nremoved\n", "stable\n", "Removed lines", "1")]
    public void NormalVerbosity_PreservesOneSidedLineCounts(
        string before,
        string after,
        string key,
        string value)
    {
        SourceDiffOutput output = SourceTextDiffRenderer.CreateOutput(
            before,
            after,
            "Before",
            "After");

        AssertField(output, key, value);
        AssertField(output, "Changed lines", "0 Before -> 0 After");
    }

    [Fact]
    public void DetailedVerbosity_RendersCompleteMappedDiff()
    {
        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            "one\nremoved\nlast",
            "one\nadded\nlast",
            "Before",
            "After",
            detailed: true);

        Assert.Equal(
            Join(
                "--- Before",
                "+++ After",
                "@@ -1,3 +1,3 @@",
                " one",
                "-removed",
                "+added",
                " last",
                "\\ No newline at end of file"),
            actual);
    }

    [Fact]
    public void MovedBlock_IsCountedAndRendersAtItsTypedPositions()
    {
        const string before = "A\nB\nC\nmoved-one\nmoved-two\nD\nE";
        const string after = "moved-one\nmoved-two\nA\nB\nC\nD\nE";

        SourceDiffOutput summary = SourceTextDiffRenderer.CreateOutput(
            before,
            after,
            "Before",
            "After");
        string detailed = SourceTextDiffRenderer.CreateUnifiedDiff(
            before,
            after,
            "Before",
            "After",
            detailed: true);

        AssertField(summary, "Moved lines", "2 Before -> 2 After");
        Assert.Contains("+moved-one", detailed);
        Assert.Contains("+moved-two", detailed);
        Assert.Contains("-moved-one", detailed);
        Assert.Contains("-moved-two", detailed);
    }

    [Fact]
    public void ChangedAndMovedRemainOverlappingFacets()
    {
        SourceDiffOutput output = SourceTextDiffRenderer.CreateOutput(
            "A\nB\nC\nmoved-one\nmoved-two",
            "moved-one\nmoved-two\nA\nB\nC\n",
            "Before",
            "After");

        AnalysisDiffRelation.Correspondence relation = Assert.Single(
            output.Analysis!.Relations.OfType<AnalysisDiffRelation.Correspondence>(),
            relation =>
                relation.Content == AnalysisDiffContentKind.Changed
                && relation.Placement == AnalysisDiffPlacementKind.Moved);
        AssertField(output, "Changed lines", "1 Before -> 1 After");
        AssertField(output, "Moved lines", "2 Before -> 2 After");
    }

    [Fact]
    public void CrLfCrAndLf_AreEquivalent()
    {
        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            "one\r\ntwo\rthree\r",
            "one\ntwo\nthree\n",
            "Before",
            "After");

        Assert.Equal("Status  Before and After are identical.", actual);
    }

    [Fact]
    public void NonNullEmptyAndWhitespaceOnlyText_AreValidInputs()
    {
        Assert.Equal(
            "Status  Before and After are identical.",
            SourceTextDiffRenderer.CreateUnifiedDiff("", "", "Before", "After"));

        SourceDiffOutput whitespace = SourceTextDiffRenderer.CreateOutput(
            " ",
            "\t",
            "Before",
            "After");
        AssertField(whitespace, "Changed lines", "1 Before -> 1 After");
    }

    [Fact]
    public void NullInput_RemainsCallerLevelUnavailability()
    {
        Assert.Equal(
            "Status  Before unavailable; source diff requires both Before and After.",
            SourceTextDiffRenderer.CreateUnifiedDiff(null, "text", "Before", "After"));
        Assert.Equal(
            "Status  After unavailable; source diff requires both Before and After.",
            SourceTextDiffRenderer.CreateUnifiedDiff("text", null, "Before", "After"));
    }

    [Fact]
    public void FinalNewlineDifference_IsAChangedFinalLine()
    {
        SourceDiffOutput output = SourceTextDiffRenderer.CreateOutput(
            "value",
            "value\n",
            "Before",
            "After");
        string detailed = SourceTextDiffRenderer.CreateUnifiedDiff(
            "value",
            "value\n",
            "Before",
            "After",
            detailed: true);

        AssertField(output, "Changed lines", "1 Before -> 1 After");
        Assert.Contains("-value", detailed);
        Assert.Contains("+value", detailed);
        Assert.Contains("\\ No newline at end of file", detailed);
    }

    [Theory]
    [InlineData("", "added", "@@ -0,0 +1 @@")]
    [InlineData("removed", "", "@@ -1 +0,0 @@")]
    public void DetailedDiff_AnchorsEmptyRangesAtThePrecedingLine(
        string before,
        string after,
        string expectedHeader)
    {
        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            before,
            after,
            "Before",
            "After",
            detailed: true);

        Assert.Contains(expectedHeader, actual);
    }

    [Fact]
    public void DetailedDiff_RetainsTheWholeDocument()
    {
        string[] before = Enumerable.Range(1, 100).Select(index => $"before-{index}").ToArray();
        string[] after = [.. before];
        after[49] = "changed-50";

        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            Join(before),
            Join(after),
            "Before",
            "After",
            detailed: true);

        Assert.Contains(" before-1", actual);
        Assert.Contains("-before-50", actual);
        Assert.Contains("+changed-50", actual);
        Assert.Contains(" before-100", actual);
        Assert.DoesNotContain("omitted", actual);
    }

    static string Join(params string[] lines) => string.Join("\n", lines);

    static void AssertField(SourceDiffOutput output, string key, string value)
    {
        Markout.MarkoutField field = Assert.Single(output.Fields, field => field.Key == key);
        Assert.Equal(value, field.Value);
    }
}
