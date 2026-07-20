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
            payload);

        Assert.Equal(HarnessRunDisposition.Completed, report.Disposition);
        Assert.Equal(3, report.Payload.FailedFixtures);
        Assert.Empty(report.Blockers);
        Assert.IsAssignableFrom<IDecompilerHarnessReport>(report);
    }

    [Fact]
    public void CompletedReport_RejectsExecutionBlockers()
    {
        Assert.Throws<ArgumentException>(() => new DecompilerHarnessReport<DomainPayload>(
            new HarnessReportDescriptor("test.domain", 1),
            new DomainPayload(FailedFixtures: 0),
            blockers: [new HarnessBlocker("HARNESS001", "fixture acquisition failed")]));
    }

    [Fact]
    public void PartialReport_CarriesBlockersWithoutChangingPayloadVocabulary()
    {
        var report = new DecompilerHarnessReport<DomainPayload>(
            new HarnessReportDescriptor("test.domain", 1),
            new DomainPayload(FailedFixtures: 0),
            HarnessRunDisposition.Partial,
            [new HarnessBlocker("HARNESS001", "one assembly could not be opened")]);

        Assert.Equal(HarnessRunDisposition.Partial, report.Disposition);
        Assert.Equal("HARNESS001", Assert.Single(report.Blockers).Code);
    }

    sealed record DomainPayload(int FailedFixtures);
}
