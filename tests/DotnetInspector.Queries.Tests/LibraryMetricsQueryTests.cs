using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class LibraryMetricsQueryTests
{
    [Fact]
    public void Execute_PublishesResearchDocumentFromImplementationProfiles()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.ImplementationProfiles));

        LibraryMetricsResult result =
            LibraryMetricsQuery.Execute(
                analysis.ImplementationProfiles);

        var available =
            Assert.IsType<LibraryMetricsResult.Available>(
                result);
        Assert.Equal(
            analysis.Receipt,
            available.Document.AnalysisReceipt);
        Assert.Equal(
            analysis.ImplementationProfiles.Coverage,
            available.Document.Population.Coverage);
        Assert.Contains(
            available.Document.Distributions,
            distribution =>
                distribution.Metric
                    == ILInspector.Research.LibraryStructuralMetric
                        .NormalFlowCyclomaticComplexity);
        Assert.Equal(
            InspectionCost.Unbounded,
            LibraryMetricsQuery.Definition.Cost);
    }

    [Fact]
    public void Execute_MissingProfileAcquisitionRemainsTypedUnavailable()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));

        LibraryMetricsResult result =
            LibraryMetricsQuery.Execute(
                analysis.ImplementationProfiles);

        var unavailable =
            Assert.IsType<LibraryMetricsResult.Unavailable>(
                result);
        Assert.Equal(
            ILInspector.Research.LibraryStructuralReportUnavailableReason
                .ImplementationProfilesNotRequested,
            unavailable.Outcome.Reason);
        Assert.Equal(
            analysis.ImplementationProfiles.Coverage,
            unavailable.Outcome.Coverage);
    }
}
