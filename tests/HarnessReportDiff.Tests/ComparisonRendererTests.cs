using DotnetInspector.HarnessReportDiff;
using DotnetInspector.HarnessReports;

namespace HarnessReportDiff.Tests;

public class ComparisonRendererTests
{
    [Fact]
    public void Markdown_RendersNativeArrowGlyphAndGoalLabel_ForComparableMetrics()
    {
        var comparison = HarnessReportComparer.Compare(
            Report(new ResidueEvidence(0), Metric("exact", MetricGoal.Higher, 10), Metric("fail", MetricGoal.Lower, 3)),
            Report(new ResidueEvidence(0), Metric("exact", MetricGoal.Higher, 12), Metric("fail", MetricGoal.Lower, 5)));

        string markdown = ComparisonRenderer.Render(comparison, "markdown");

        // Native change cell: arrow between the two rate displays, plus the improvement/regression glyph.
        Assert.Contains("10 (50.00%) → 12 (60.00%) ✓", markdown);
        Assert.Contains("3 (15.00%) → 5 (25.00%) ✗", markdown);
        // Goal label glyphs replace the old hand-mapped (+)/(−).
        Assert.Contains("exact ↑", markdown);
        Assert.Contains("fail ↓", markdown);
        // The hand-rolled goal symbols and verdict-word column are gone.
        Assert.DoesNotContain("(+)", markdown);
        Assert.DoesNotContain("Metric (goal)", markdown);
        Assert.DoesNotContain("Improved", markdown);
        Assert.DoesNotContain("Regressed", markdown);
    }

    [Fact]
    public void Markdown_OmitsPolarityGlyph_ForIncomparableMetric()
    {
        var comparison = HarnessReportComparer.Compare(
            Report(new ResidueEvidence(0), Metric("exact", MetricGoal.Higher, 10, "sample-a")),
            Report(new ResidueEvidence(0), Metric("exact", MetricGoal.Higher, 12, "sample-b")));

        string markdown = ComparisonRenderer.Render(comparison, "markdown");

        // Values still render with the goal-direction label, but no ✓/✗ verdict glyph and an n/a delta.
        Assert.Contains("exact ↑", markdown);
        Assert.Contains("10 (50.00%) → 12 (60.00%)", markdown);
        Assert.Contains("| n/a |", markdown);
        Assert.DoesNotContain("✓", markdown);
        Assert.DoesNotContain("✗", markdown);
    }

    [Fact]
    public void Tsv_EmitsTypedGoalColumnAndVerdictEnum()
    {
        var comparison = HarnessReportComparer.Compare(
            Report(new ResidueEvidence(0), Metric("exact", MetricGoal.Higher, 10)),
            Report(new ResidueEvidence(0), Metric("exact", MetricGoal.Higher, 12)));

        string tsv = ComparisonRenderer.Render(comparison, "tsv");

        Assert.Contains("section\titem\tgoal\tbefore\tafter\tdelta\tverdict\tdetail", tsv);
        Assert.Contains("Metric\texact\tHigher\t10 (50.00%)\t12 (60.00%)\t+2 / +10.00 pp\tImproved\t", tsv);
    }

    [Fact]
    public void Jsonl_CarriesGoalAndVerdictFields()
    {
        var comparison = HarnessReportComparer.Compare(
            Report(new ResidueEvidence(0), Metric("fail", MetricGoal.Lower, 3)),
            Report(new ResidueEvidence(0), Metric("fail", MetricGoal.Lower, 5)));

        string jsonl = ComparisonRenderer.Render(comparison, "jsonl");

        Assert.Contains("\"goal\":\"Lower\"", jsonl);
        Assert.Contains("\"verdict\":\"Regressed\"", jsonl);
    }

    static ComparableMetric Metric(string id, MetricGoal goal, long count, string population = "same")
        => new(id, id, goal, new MetricValue(count, 20), population);

    static StructuredHarnessReport Report(ResidueEvidence residue, params ComparableMetric[] metrics)
        => new(1, "test", "test report", "same", metrics, residue);
}
