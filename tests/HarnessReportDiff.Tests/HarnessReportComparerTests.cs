using DotnetInspector.HarnessReportDiff;

namespace HarnessReportDiff.Tests;

public class HarnessReportComparerTests
{
    [Fact]
    public void Compare_InterpretsMetricGoalsAndResidueEndpoint()
    {
        var before = Report(
            new ResidueEvidence(2),
            Metric("exact", MetricGoal.Higher, 10),
            Metric("fail", MetricGoal.Lower, 3));
        var after = Report(
            new ResidueEvidence(0),
            Metric("exact", MetricGoal.Higher, 12),
            Metric("fail", MetricGoal.Lower, 4));

        var comparison = HarnessReportComparer.Compare(before, after);

        Assert.Equal(MetricVerdict.Improved, comparison.Metrics.Single(row => row.Id == "exact").Verdict);
        Assert.Equal(MetricVerdict.Regressed, comparison.Metrics.Single(row => row.Id == "fail").Verdict);
        Assert.Equal("No — 2 residual method(s)", comparison.FullyRaised.Before);
        Assert.Equal("Yes — zero residue", comparison.FullyRaised.After);
        Assert.True(comparison.HasRegressions);
    }

    [Fact]
    public void Compare_MarksDifferentMetricPopulationsIncomparable()
    {
        var before = Report(new ResidueEvidence(0), Metric("exact", MetricGoal.Higher, 10, "sample-a"));
        var after = Report(new ResidueEvidence(0), Metric("exact", MetricGoal.Higher, 12, "sample-b"));

        var comparison = HarnessReportComparer.Compare(before, after);

        var metric = Assert.Single(comparison.Metrics);
        Assert.Equal(MetricVerdict.Incomparable, metric.Verdict);
        Assert.Equal("n/a", metric.Delta);
    }

    [Fact]
    public void Compare_DoesNotClaimFullyRaisedWhenResidueMeasurementIsIncomplete()
    {
        var report = Report(new ResidueEvidence(0, MeasurementComplete: false));

        var comparison = HarnessReportComparer.Compare(report, report);

        Assert.StartsWith("Not established", comparison.FullyRaised.After);
    }

    [Fact]
    public void Compare_RejectsDifferentReportKinds()
    {
        var before = Report(new ResidueEvidence(0));
        var after = before with { Kind = "other" };

        Assert.Throws<InvalidOperationException>(() => HarnessReportComparer.Compare(before, after));
    }

    static ComparableMetric Metric(
        string id,
        MetricGoal goal,
        long count,
        string population = "same")
        => new(id, id, goal, new MetricValue(count, 20), population);

    static StructuredHarnessReport Report(
        ResidueEvidence residue,
        params ComparableMetric[] metrics)
        => new(1, "test", "test report", "same", metrics, residue);
}
