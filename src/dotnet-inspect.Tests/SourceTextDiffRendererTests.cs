using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class SourceTextDiffRendererTests
{
    [Fact]
    public void AddedAndRemovedLines_PreserveUnifiedDiffShape()
    {
        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            "one\nremoved\nlast",
            "one\nadded\nlast",
            "Before",
            "After");

        Assert.Equal(
            Join(
                "--- Before",
                "+++ After",
                "@@ -1,3 +1,3 @@",
                " one",
                "-removed",
                "+added",
                " last"),
            actual);
    }

    [Fact]
    public void MovedBlock_RendersAsInsertionAndRemovalAtItsTypedPositions()
    {
        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            "A\nB\nC\nmoved-one\nmoved-two\nD\nE",
            "moved-one\nmoved-two\nA\nB\nC\nD\nE",
            "Before",
            "After");

        Assert.Equal(
            Join(
                "--- Before",
                "+++ After",
                "@@ -1,7 +1,7 @@",
                "+moved-one",
                "+moved-two",
                " A",
                " B",
                " C",
                "-moved-one",
                "-moved-two",
                " D",
                " E"),
            actual);
    }

    [Fact]
    public void CrLfCrAndLf_AreEquivalent()
    {
        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            "one\r\ntwo\rthree\r",
            "one\ntwo\nthree\n",
            "Before",
            "After");

        Assert.Equal("# Before and After are identical.", actual);
    }

    [Fact]
    public void NonNullEmptyAndWhitespaceOnlyText_AreValidInputs()
    {
        Assert.Equal(
            "# Before and After are identical.",
            SourceTextDiffRenderer.CreateUnifiedDiff("", "", "Before", "After"));

        string whitespace = SourceTextDiffRenderer.CreateUnifiedDiff(" ", "\t", "Before", "After");
        Assert.Equal(
            Join(
                "--- Before",
                "+++ After",
                "@@ -1,1 +1,1 @@",
                "- ",
                "+\t"),
            whitespace);
    }

    [Fact]
    public void NullInput_RemainsCallerLevelUnavailability()
    {
        Assert.Equal(
            "# Before unavailable; source diff requires both Before and After.",
            SourceTextDiffRenderer.CreateUnifiedDiff(null, "text", "Before", "After"));
        Assert.Equal(
            "# After unavailable; source diff requires both Before and After.",
            SourceTextDiffRenderer.CreateUnifiedDiff("text", null, "Before", "After"));
    }

    [Fact]
    public void ReviewerSizedDiff_OmitsDistantUnchangedLinesButRetainsEveryChange()
    {
        string[] before = Enumerable.Range(1, 30).Select(index => $"line-{index}").ToArray();
        string[] after = [.. before];
        after[1] = "changed-2";
        after[27] = "changed-28";

        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            Join(before),
            Join(after),
            "Original",
            "After",
            reviewerSized: true);

        Assert.Contains("-line-2", actual);
        Assert.Contains("+changed-2", actual);
        Assert.Contains("-line-28", actual);
        Assert.Contains("+changed-28", actual);
        Assert.DoesNotContain(" line-15", actual);
        Assert.Equal(2, actual.Split('\n').Count(line => line.StartsWith("@@ ", StringComparison.Ordinal)));
        Assert.DoesNotContain("Source diff status: Partial", actual);
    }

    [Fact]
    public void ReviewerSizedDiff_BoundsHunksAndLargeHunksWithVisibleDisclosure()
    {
        string[] before = Enumerable.Range(1, 160).Select(index => $"before-{index}").ToArray();
        string[] after = Enumerable.Range(1, 160).Select(index => $"after-{index}").ToArray();

        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            Join(before),
            Join(after),
            "Original",
            "After",
            reviewerSized: true);

        Assert.StartsWith("# Source diff status: Partial - ", actual);
        Assert.Contains("diff lines omitted", actual);
        Assert.Contains("use -v:d for complete line evidence", actual);
        Assert.Contains("-before-1", actual);
        Assert.Contains("+after-160", actual);
        Assert.DoesNotContain("-before-80", actual);
        Assert.Contains("@@ -1,40 +0,0 @@", actual);
        Assert.Contains("@@ -160,0 +121,40 @@", actual);
    }

    [Theory]
    [InlineData("", "added", "@@ -0,0 +1,1 @@")]
    [InlineData("removed", "", "@@ -1,1 +0,0 @@")]
    public void CompleteDiff_AnchorsEmptyRangesAtThePrecedingLine(
        string before,
        string after,
        string expectedHeader)
    {
        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            before,
            after,
            "Original",
            "After");

        Assert.Contains(expectedHeader, actual);
    }

    [Fact]
    public void ReviewerSizedDiff_BoundsTheNumberOfHunkExamples()
    {
        string[] before = Enumerable.Range(1, 100).Select(index => $"line-{index}").ToArray();
        string[] after = [.. before];
        foreach (int index in new[] { 2, 14, 26, 38, 50, 62, 74 })
            after[index] = $"changed-{index + 1}";

        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            Join(before),
            Join(after),
            "Original",
            "After",
            reviewerSized: true);

        Assert.StartsWith("# Source diff status: Partial - 2 additional hunks (", actual);
        Assert.Contains(" lines) omitted; use -v:d for complete line evidence", actual);
        Assert.Equal(5, actual.Split('\n').Count(line => line.StartsWith("@@ ", StringComparison.Ordinal)));
        Assert.Contains("+changed-51", actual);
        Assert.DoesNotContain("+changed-63", actual);
        Assert.DoesNotContain("+changed-75", actual);
    }

    [Fact]
    public void ReviewerSizedDiff_BoundsEmittedFragmentsFromOversizedHunks()
    {
        string[] before = Enumerable.Range(1, 210).Select(index => $"line-{index}").ToArray();
        string[] after = [.. before];
        foreach (int index in Enumerable.Range(10, 50)
            .Concat(Enumerable.Range(80, 50))
            .Concat(Enumerable.Range(150, 50)))
        {
            after[index] = $"changed-{index + 1}";
        }

        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            Join(before),
            Join(after),
            "Original",
            "After",
            reviewerSized: true);

        Assert.Equal(5, actual.Split('\n').Count(line => line.StartsWith("@@ ", StringComparison.Ordinal)));
        Assert.Contains("# ... 66 diff lines omitted from this hunk ...", actual);
        Assert.Contains("118 lines within shown hunks", actual);
    }

    [Fact]
    public void CompleteDiff_RetainsTheWholeDocument()
    {
        string[] before = Enumerable.Range(1, 100).Select(index => $"before-{index}").ToArray();
        string[] after = [.. before];
        after[49] = "changed-50";

        string actual = SourceTextDiffRenderer.CreateUnifiedDiff(
            Join(before),
            Join(after),
            "Original",
            "After");

        Assert.Contains(" before-1", actual);
        Assert.Contains("-before-50", actual);
        Assert.Contains("+changed-50", actual);
        Assert.Contains(" before-100", actual);
        Assert.DoesNotContain("omitted", actual);
    }

    // The renderer emits LF on every platform, so expectations are built with LF rather than the
    // ambient newline. The CR-bearing literals above are inputs, not expectations.
    static string Join(params string[] lines) => string.Join("\n", lines);
}
