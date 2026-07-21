using DotnetInspector.HarnessReports;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

public class DecompilerHarnessReportTests
{
    [Fact]
    public void CompletedReport_PreservesTypedPayloadAndDomainOutcome()
    {
        var payload = new DomainPayload(FailedFixtures: 3);
        var report = new DecompilerHarnessReport<DomainPayload>(
            new HarnessReportDescriptor("test.domain", 1),
            payload,
            Projection());

        Assert.Equal(HarnessRunDisposition.Completed, report.Disposition);
        Assert.Equal(3, report.Payload.FailedFixtures);
        Assert.Empty(report.Blockers);
        Assert.IsAssignableFrom<IDecompilerHarnessReport>(report);
        Assert.Equal("test-population", report.ToStoredReport().Comparison.PopulationKey);
    }

    [Fact]
    public void CompletedReport_RejectsExecutionBlockers()
    {
        Assert.Throws<ArgumentException>(() => new DecompilerHarnessReport<DomainPayload>(
            new HarnessReportDescriptor("test.domain", 1),
            new DomainPayload(FailedFixtures: 0),
            Projection(),
            blockers: [new HarnessBlocker("HARNESS001", "fixture acquisition failed")]));
    }

    [Fact]
    public void PartialReport_CarriesBlockersWithoutChangingPayloadVocabulary()
    {
        var report = new DecompilerHarnessReport<DomainPayload>(
            new HarnessReportDescriptor("test.domain", 1),
            new DomainPayload(FailedFixtures: 0),
            Projection(),
            HarnessRunDisposition.Partial,
            [new HarnessBlocker("HARNESS001", "one assembly could not be opened")]);

        Assert.Equal(HarnessRunDisposition.Partial, report.Disposition);
        Assert.Equal("HARNESS001", Assert.Single(report.Blockers).Code);
    }

    [Fact]
    public void SourceCorrespondenceReport_PreservesIndependentOutcomeMetrics()
    {
        var target = new ReturnToSender.RequestedTarget("Synthetic.Type", "get_Value", 0);
        var rows = new[]
        {
            new ReturnToSenderSourceProbeResult(
                target,
                ReturnToSenderSourceOutcome.ValidMatch,
                FidelityCheck.CompileBackStatus.Exact,
                "valid_match",
                null,
                "Source.cs",
                "return 1;",
                "return 1;"),
            new ReturnToSenderSourceProbeResult(
                target with { Method = "get_Other" },
                ReturnToSenderSourceOutcome.SourceUnavailable,
                FidelityCheck.CompileBackStatus.Exact,
                "source_unavailable",
                null,
                null,
                null,
                "return 2;"),
        };

        var report = ReturnToSenderSourceProbe.BuildReport(rows).ToStoredReport();

        Assert.Equal("return-to-sender.source-correspondence", report.Descriptor.Id);
        Assert.Equal(
            MetricGoal.Higher,
            report.Comparison.Metrics.Single(metric => metric.Id == "valid-match").Goal);
        Assert.Equal(
            MetricGoal.Context,
            report.Comparison.Metrics.Single(metric => metric.Id == "source-unavailable").Goal);
    }

    sealed record DomainPayload(int FailedFixtures);

    static HarnessComparisonProjection Projection()
        => new("test report", "test-population", []);
}
