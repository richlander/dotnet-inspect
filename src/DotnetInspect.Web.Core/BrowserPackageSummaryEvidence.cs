using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Sections;
using DotnetInspector.Services;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserPackageSummaryEvidence
{
    internal static EvidenceInspectionEnvelope<
        PackageVersionSettlementOutcome,
        PackageAcquisitionEvidence> Capture(BrowserPackageRealizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        InspectionEnvelope<PackageVersionSettlementOutcome> inspection = result switch
        {
            BrowserPackageRealizationResult.Realized realized =>
                realized.Realization.VersionSettlement,
            BrowserPackageRealizationResult.NotSettled notSettled =>
                notSettled.VersionSettlement,
            _ => throw new InvalidOperationException("Unknown Browser package realization outcome."),
        };
        var acquisitions = ImmutableArray.CreateBuilder<PackageAcquisitionEvidenceEntry>();
        for (BrowserPackageRealizationResult? step = result; step is not null; step = step.Continuation)
        {
            if (step.Acquisition is { } acquisition)
                acquisitions.Add(PackageAcquisitionEvidenceEntry.From(acquisition));
        }
        return new(inspection, new PackageAcquisitionEvidence(acquisitions.ToImmutable()));
    }
}
