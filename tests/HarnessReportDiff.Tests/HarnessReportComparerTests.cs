using DotnetInspector.HarnessReportDiff;
using DotnetInspector.HarnessReports;
using System.Text.Json;
using System.Text.Json.Nodes;

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
        Assert.NotNull(comparison.FullyRaised);
        var fullyRaised = comparison.FullyRaised!;
        Assert.Equal("No — 2 residual method(s)", fullyRaised.Before);
        Assert.Equal("Yes — zero residue", fullyRaised.After);
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

        Assert.NotNull(comparison.FullyRaised);
        Assert.StartsWith("Not established", comparison.FullyRaised!.After);
    }

    [Fact]
    public void Compare_RejectsDifferentReportKinds()
    {
        var before = Report(new ResidueEvidence(0));
        var after = before with { Kind = "other" };

        Assert.Throws<InvalidOperationException>(() => HarnessReportComparer.Compare(before, after));
    }

    [Fact]
    public void Compare_MarksResidueEndpointIncomparable_WhenPopulationChanges()
    {
        var before = Report(new ResidueEvidence(3)) with { PopulationKey = "population-a" };
        var after = Report(new ResidueEvidence(0)) with { PopulationKey = "population-b" };

        var comparison = HarnessReportComparer.Compare(before, after);

        Assert.NotNull(comparison.FullyRaised);
        var endpoint = comparison.FullyRaised!;
        Assert.Equal(MetricVerdict.Incomparable, endpoint.Verdict);
        Assert.Equal("Incomparable", endpoint.After);
    }

    [Fact]
    public void Compare_TreatsComparableResidueIncreaseAsRegression()
    {
        var before = Report(new ResidueEvidence(0));
        var after = Report(new ResidueEvidence(1));

        var comparison = HarnessReportComparer.Compare(before, after);

        Assert.NotNull(comparison.FullyRaised);
        Assert.Equal(MetricVerdict.Regressed, comparison.FullyRaised!.Verdict);
        Assert.True(comparison.HasRegressions);
    }

    [Fact]
    public void Compare_RejectsDuplicateMetricIds()
    {
        var report = Report(
            new ResidueEvidence(0),
            Metric("duplicate", MetricGoal.Higher, 1),
            Metric("duplicate", MetricGoal.Higher, 2));

        var error = Assert.Throws<InvalidOperationException>(
            () => HarnessReportComparer.Compare(report, report));

        Assert.Contains("duplicated", error.Message);
    }

    [Fact]
    public void Compare_RejectsDifferentMetricSetsWithinOneSchema()
    {
        var before = Report(new ResidueEvidence(0), Metric("exact", MetricGoal.Higher, 1));
        var after = Report(new ResidueEvidence(0));

        var error = Assert.Throws<InvalidOperationException>(
            () => HarnessReportComparer.Compare(before, after));

        Assert.Contains("Metric set differs", error.Message);
    }

    [Fact]
    public void Read_PreservesStoredEnvelopeIdentityAndMetrics()
    {
        var stored = new StoredHarnessReport(
            new HarnessReportDescriptor("test.stored", 3),
            HarnessRunDisposition.Completed,
            [],
            [],
            new HarnessComparisonProjection(
                "stored report",
                "population",
                [Metric("exact", MetricGoal.Higher, 10)]));

        var report = ReadJson(stored);

        Assert.Equal(3, report.SchemaVersion);
        Assert.Equal("test.stored", report.Kind);
        Assert.Equal("exact", Assert.Single(report.Metrics).Id);
    }

    [Fact]
    public void ReadCorpusSnapshot_PreservesSchemaVersion()
    {
        string baseline = Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "DecompilerHarness",
            "corpus",
            "pr-quick-baseline.json");
        var report = HarnessReportReader.Read(baseline);

        Assert.Equal(5, report.SchemaVersion);
    }

    [Fact]
    public void Compare_RejectsDifferentCorpusSnapshotSchemas()
    {
        string baseline = CorpusBaselinePath();
        var before = HarnessReportReader.Read(baseline);
        var after = before with { SchemaVersion = before.SchemaVersion + 1 };

        Assert.Throws<InvalidOperationException>(
            () => HarnessReportComparer.Compare(before, after));
    }

    [Fact]
    public void ReadCorpusSnapshot_DoesNotCompareUnknownSamplesWithMatchingAggregateCounts()
    {
        var beforeNode = JsonNode.Parse(File.ReadAllText(CorpusBaselinePath()))!.AsObject();
        beforeNode.Remove("methods");
        var afterNode = beforeNode.DeepClone().AsObject();

        string beforePath = WriteTemporaryJson(beforeNode);
        string afterPath = WriteTemporaryJson(afterNode);
        try
        {
            var comparison = HarnessReportComparer.Compare(
                HarnessReportReader.Read(beforePath),
                HarnessReportReader.Read(afterPath));

            Assert.Equal(
                MetricVerdict.Incomparable,
                comparison.Metrics.Single(metric => metric.Id == "fidelity-exact").Verdict);
            Assert.Equal(MetricVerdict.Incomparable, comparison.FullyRaised!.Verdict);
        }
        finally
        {
            File.Delete(beforePath);
            File.Delete(afterPath);
        }
    }

    [Fact]
    public void Compare_RejectsMissingMetricCollection()
    {
        var report = Report(new ResidueEvidence(0)) with { Metrics = null! };

        var error = Assert.Throws<InvalidOperationException>(
            () => HarnessReportComparer.Compare(report, report));

        Assert.Contains("no metric collection", error.Message);
    }

    [Fact]
    public void Read_RejectsStoredReportWithoutComparisonProjection()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                """{"descriptor":{"id":"test","schemaVersion":1},"disposition":"completed","blockers":[],"artifacts":[]}""");

            var error = Assert.Throws<InvalidOperationException>(() => HarnessReportReader.Read(path));

            Assert.Contains("no comparison projection", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Read_RejectsUnsupportedJsonShape()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """{"schemaVersion":1}""");
            var error = Assert.Throws<InvalidOperationException>(() => HarnessReportReader.Read(path));
            Assert.Contains("not a stored harness report", error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static StructuredHarnessReport ReadJson(StoredHarnessReport stored)
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(stored, HarnessReportStorage.JsonOptions(writeIndented: false)));
            return HarnessReportReader.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    static string WriteTemporaryJson(JsonNode value)
    {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, value.ToJsonString());
        return path;
    }

    static string CorpusBaselinePath()
        => Path.Combine(
            FindRepositoryRoot(),
            "tools",
            "DecompilerHarness",
            "corpus",
            "pr-quick-baseline.json");

    static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "dotnet-inspect.slnx")))
            directory = Directory.GetParent(directory)?.FullName;
        return directory ?? throw new InvalidOperationException("Could not find repository root.");
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
