using DotnetInspector.Output;

namespace DotnetInspector.Tests;

public class SourceTextDiffTests
{
    [Fact]
    public void AddedAndRemovedLines_PreserveUnifiedDiffShape()
    {
        string actual = SourceTextDiff.CreateUnifiedDiff(
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
        string actual = SourceTextDiff.CreateUnifiedDiff(
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
        string actual = SourceTextDiff.CreateUnifiedDiff(
            "one\r\ntwo\rthree\r",
            "one\ntwo\nthree\n",
            "Before",
            "After");

        Assert.Equal("# Before and After are identical.", actual);
    }

    [Fact]
    public void EmptyAndWhitespaceOnlyText_AreValidInputs()
    {
        Assert.Equal(
            "# Before and After are identical.",
            SourceTextDiff.CreateUnifiedDiff("", "", "Before", "After"));

        string whitespace = SourceTextDiff.CreateUnifiedDiff(" ", "\t", "Before", "After");
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
            SourceTextDiff.CreateUnifiedDiff(null, "text", "Before", "After"));
        Assert.Equal(
            "# After unavailable; source diff requires both Before and After.",
            SourceTextDiff.CreateUnifiedDiff("text", null, "Before", "After"));
    }

    static string Join(params string[] lines) => string.Join(Environment.NewLine, lines);
}
