using DotnetInspector.Output;
using DotnetInspector.Presentation;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Tests;

public class SourceTextDiffRendererTests
{
    [Fact]
    public void NormalVerbosity_ReportsSharedStatisticsWithoutTheDiff()
    {
        SourceDiffOutput output = SourceTextDiffRenderer.CreateOutput(
            Presentation(
                """
                public void M()
                {
                    one();
                    removed();
                    last();
                }
                """,
                """
                    public void M()
                    {
                        one();
                        added();
                        last();
                    }
                """));

        AssertField(output, "Added lines", "0");
        AssertField(output, "Removed lines", "0");
        AssertField(
            output,
            "Changed lines",
            "1 PDB comparison -> 1 Decompiled comparison");
        AssertField(
            output,
            "Moved lines",
            "0 PDB comparison -> 0 Decompiled comparison");
        Assert.DoesNotContain("--- PDB comparison", output.Content);
    }

    [Fact]
    public void DetailedVerbosity_RendersTheCompleteSharedMappedDiff()
    {
        SourceDiffOutput output = SourceTextDiffRenderer.CreateOutput(
            Presentation(
                """
                public void M()
                {
                    one();
                    removed();
                    last();
                }
                """,
                """
                    public void M()
                    {
                        one();
                        added();
                        last();
                    }
                """),
            detailed: true);

        Assert.Contains("--- PDB comparison", output.Content);
        Assert.Contains("+++ Decompiled comparison", output.Content);
        Assert.Contains("-    removed();", output.Content);
        Assert.Contains("+    added();", output.Content);
        Assert.Contains("     last();", output.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IdenticalComparison_RetainsStatusAndZeroStatistics(bool detailed)
    {
        MemberSourceDiffPresentation presentation = Presentation(
            "public void M() { }",
            "    public void M() { }");
        SourceDiffOutput output =
            SourceTextDiffRenderer.CreateOutput(
                presentation,
                detailed);

        AssertField(
            output,
            "Status",
            "PDB comparison and Decompiled comparison are identical.");
        AssertField(output, "Added lines", "0");
        AssertField(output, "Removed lines", "0");
        AssertField(output, "Changed lines", "0 PDB comparison -> 0 Decompiled comparison");
        AssertField(output, "Moved lines", "0 PDB comparison -> 0 Decompiled comparison");
        Assert.Same(presentation.Analysis, output.Analysis);
        Assert.Same(presentation.Diff, output.Diff);
        Assert.False(output.ShowDiff);
        Assert.DoesNotContain("--- PDB comparison", output.Content);
    }

    [Fact]
    public void UnavailableComparison_DoesNotInventZeroStatistics()
    {
        var output = new SourceDiffOutput("Comparison unavailable.");

        Assert.Null(output.Analysis);
        Assert.Null(output.Diff);
        Assert.Equal("Status", Assert.Single(output.Fields).Key);
    }

    static MemberSourceDiffPresentation Presentation(
        string before,
        string after)
        => MemberSourceComparisonTestData.CreatePresentation(
            before,
            after);

    static void AssertField(
        SourceDiffOutput output,
        string key,
        string value)
    {
        MarkoutField field =
            Assert.Single(output.Fields, field => field.Key == key);
        Assert.Equal(value, field.Value);
    }
}
