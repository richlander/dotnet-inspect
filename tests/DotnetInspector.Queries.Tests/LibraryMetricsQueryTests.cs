using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;

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

    [Fact]
    public void Execute_SharedExecutionPublishesRelationshipEvidence()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures.ImplementationProfiles));

        LibraryMetricsResult result =
            LibraryMetricsQuery.Execute(analysis);

        var available =
            Assert.IsType<LibraryMetricsResult.Available>(result);
        Assert.NotEmpty(available.Document.EntangledRelationships);
        Assert.Same(
            analysis.Receipt,
            available.Document.AnalysisReceipt);
    }

    [Fact]
    public void Execute_EvidenceCompleteInputsPublishTypeLeverage()
    {
        string path =
            FixtureCatalog.AnalysisCallerGraphTarget.AssemblyPath();
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence
                    | LibraryBodyAnalysisFeatures.ImplementationProfiles));
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(path);
        var signatureAvailable =
            Assert.IsType<MetadataLibrarySignatureUseOutcome.Available>(
                session.LibrarySignatureUses(
                    new(MetadataOperationPolicy.Unbounded),
                    TestContext.Current.CancellationToken));
        var bodyAvailable =
            Assert.IsType<AnalysisLibraryBodyUseOutcome.Available>(
                AnalysisLibraryBodyUseService.ExecutePath(
                    path,
                    new(),
                    TestContext.Current.CancellationToken));

        LibraryMetricsResult result =
            LibraryMetricsQuery.Execute(
                analysis,
                signatureAvailable.Result,
                bodyAvailable.Result);

        var available =
            Assert.IsType<LibraryMetricsResult.Available>(result);
        Assert.Same(
            analysis.Receipt,
            available.Document.AnalysisReceipt);
        Assert.Equal(
            LibraryStructuralReport.CurrentMethodologyVersion,
            available.Document.MethodologyVersion);
        LibraryStructuralTypeLeverageDocument leverage =
            Assert.IsType<LibraryStructuralTypeLeverageDocument>(
                available.Document.TypeLeverage);
        Assert.NotEmpty(leverage.Rows);
        Assert.NotEmpty(leverage.SeaLevel.Types);
        Assert.NotEmpty(leverage.MountainPeak.Types);
        Assert.Equal(
            analysis.Receipt.ModuleIdentity.ModuleVersionId,
            leverage.SignatureUse.Receipt.ModuleVersionId);
        Assert.Equal(
            analysis.Receipt.ModuleIdentity.ModuleVersionId,
            leverage.BodyUse.Receipt.ModuleVersionId);
    }
}
