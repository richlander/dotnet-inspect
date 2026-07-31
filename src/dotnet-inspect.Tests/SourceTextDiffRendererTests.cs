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

    // The renderer emits LF on every platform, so expectations are built with LF rather than the
    // ambient newline. The CR-bearing literals above are inputs, not expectations.
    static string Join(params string[] lines) => string.Join("\n", lines);
}
