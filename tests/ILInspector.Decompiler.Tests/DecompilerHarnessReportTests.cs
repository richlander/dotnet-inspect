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
                "return 1;",
                SourceAcquisition: SourceAcquisitionOutcome.Complete),
            new ReturnToSenderSourceProbeResult(
                target with { Method = "get_Other" },
                ReturnToSenderSourceOutcome.SourceUnavailable,
                FidelityCheck.CompileBackStatus.Exact,
                "source_unavailable",
                null,
                null,
                null,
                "return 2;",
                SourceAcquisition: SourceAcquisitionOutcome.Absent),
        };

        var report = ReturnToSenderSourceProbe.BuildReport(rows).ToStoredReport();

        Assert.Equal("return-to-sender.source-correspondence", report.Descriptor.Id);
        Assert.Equal(2, report.Descriptor.SchemaVersion);
        Assert.Equal(
            MetricGoal.Higher,
            report.Comparison.Metrics.Single(metric => metric.Id == "valid-match").Goal);
        Assert.Equal(
            MetricGoal.Context,
            report.Comparison.Metrics.Single(metric => metric.Id == "source-unavailable").Goal);
        Assert.Equal(
            1,
            report.Comparison.Metrics.Single(metric => metric.Id == "source-acquisition-complete").Value.Count);
        Assert.Equal(
            1,
            report.Comparison.Metrics.Single(metric => metric.Id == "source-acquisition-absent").Value.Count);
        Assert.Equal(
            1,
            report.Comparison.Metrics.Single(metric => metric.Id == "valid-match").Value.Count);
    }

    [Fact]
    public void SourceAcquisitionFailure_FailsOnlyTheLiveCensusLane()
    {
        var result = new ReturnToSenderSourceProbeResult(
            new ReturnToSender.RequestedTarget("Synthetic.Type", "M", 0),
            ReturnToSenderSourceOutcome.SourceUnavailable,
            CompileBackStatus: null,
            "source_failed",
            "fetch failed",
            SourcePath: null,
            ExpectedBody: null,
            ActualBody: null,
            SourceAcquisition: SourceAcquisitionOutcome.Failed);

        Assert.False(result.Failed);
        Assert.True(result.Skipped);
        Assert.True(ReturnToSenderSourceProbe.HasCommandFailure(
            [result],
            failOnInvalid: false));

        var invalid = result with
        {
            Outcome = ReturnToSenderSourceOutcome.Invalid,
            SourceAcquisition = SourceAcquisitionOutcome.Complete,
        };
        Assert.False(ReturnToSenderSourceProbe.HasCommandFailure(
            [invalid],
            failOnInvalid: false));
        Assert.True(ReturnToSenderSourceProbe.HasCommandFailure(
            [invalid],
            failOnInvalid: true));
    }

    [Fact]
    public void CompleteSourceAcquisition_DoesNotReplaceCorrespondenceDetail()
    {
        var result = new ReturnToSenderSourceProbeResult(
            new ReturnToSender.RequestedTarget("Synthetic.Type", "M", 0),
            ReturnToSenderSourceOutcome.SourceUnavailable,
            FidelityCheck.CompileBackStatus.FidelityUnavailable,
            "fidelity-unavailable",
            "RTS diagnostic",
            SourcePath: null,
            ExpectedBody: null,
            ActualBody: null);
        var acquisition = new ReturnToSenderSourceProbe.SourceAcquisitionAttempt(
            SourceAcquisitionOutcome.Complete,
            "Exact",
            "Source.cs",
            Member: null);

        ReturnToSenderSourceProbeResult stamped =
            ReturnToSenderSourceProbe.AddSourceAcquisition(result, acquisition);

        Assert.Equal("fidelity-unavailable", stamped.Reason);
        Assert.Equal("RTS diagnostic", stamped.Detail);
        Assert.Equal("Exact", stamped.SourceAcquisitionDetail);

        ReturnToSenderSourceProbeResult absent =
            ReturnToSenderSourceProbe.AddSourceAcquisition(
                result,
                acquisition with
                {
                    Outcome = SourceAcquisitionOutcome.Absent,
                    Detail = "No source mapping",
                });
        Assert.Equal("fidelity-unavailable", absent.Reason);
        Assert.Equal("RTS diagnostic", absent.Detail);
    }

    [Fact]
    public void SourceCorrespondenceReport_TracksBodylessRowsAsContext()
    {
        var row = new ReturnToSenderSourceProbeResult(
            new ReturnToSender.RequestedTarget("Synthetic.Type", "get_Value", 0),
            ReturnToSenderSourceOutcome.ValidMatch,
            FidelityCheck.CompileBackStatus.Exact,
            "valid_match.source_bodyless",
            Detail: null,
            "Source.cs",
            ExpectedBody: null,
            ActualBody: "return _value;");

        var report = ReturnToSenderSourceProbe.BuildReport([row]);

        Assert.Equal(
            0,
            report.Comparison.Metrics.Single(metric => metric.Id == "valid-match").Value.Count);
        Assert.Equal(
            1,
            report.Comparison.Metrics.Single(metric => metric.Id == "source-bodyless").Value.Count);
    }

    [Fact]
    public void SourceCorrespondencePopulation_SeparatesProvidedAndPdbSource()
    {
        var row = new ReturnToSenderSourceProbeResult(
            new ReturnToSender.RequestedTarget("Synthetic.Type", "M", 0),
            ReturnToSenderSourceOutcome.ValidMatch,
            FidelityCheck.CompileBackStatus.Exact,
            "valid_match",
            Detail: null,
            "Source.cs",
            "return 1;",
            "return 1;");
        var provided = ReturnToSenderSourceProbe.BuildReport([row]);
        var acquired = ReturnToSenderSourceProbe.BuildReport(
            [row with { SourceAcquisition = SourceAcquisitionOutcome.Complete }]);

        Assert.NotEqual(
            provided.Comparison.PopulationKey,
            acquired.Comparison.PopulationKey);
    }

    sealed record DomainPayload(int FailedFixtures);

    static HarnessComparisonProjection Projection()
        => new("test report", "test-population", []);
}
