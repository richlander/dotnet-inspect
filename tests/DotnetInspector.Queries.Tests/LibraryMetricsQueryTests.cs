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
    public void Execute_EvidenceCompleteInputsPublishStructuralSalience()
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
        LibraryStructuralNamespaceLeverageIndex namespaceIndex =
            LibraryStructuralReport.CreateNamespaceLeverageIndex(
                signatureAvailable.Result);
        LibraryStructuralTypeLeverageShard[] shards =
        [
            .. namespaceIndex.Rows.Select(row =>
            {
                var shardAvailable =
                    Assert.IsType<
                        MetadataLibrarySignatureUseOutcome.Available>(
                        session.LibrarySignatureUses(
                            new(
                                MetadataOperationPolicy.Unbounded,
                                row.Namespace),
                            TestContext.Current.CancellationToken));
                return LibraryStructuralReport.CreateTypeLeverageShard(
                    shardAvailable.Result);
            }),
        ];
        LibraryStructuralSalienceDocument structuralSalience =
            LibraryStructuralReport.CreateStructuralSalience(
                namespaceIndex,
                shards);
        LibraryMetricsResult result =
            LibraryMetricsQuery.Execute(
                analysis,
                structuralSalience);

        var available =
            Assert.IsType<LibraryMetricsResult.Available>(result);
        Assert.Same(
            analysis.Receipt,
            available.Document.AnalysisReceipt);
        Assert.Equal(
            LibraryStructuralReport.CurrentMethodologyVersion,
            available.Document.MethodologyVersion);
        LibraryStructuralSalienceDocument salience =
            Assert.IsType<LibraryStructuralSalienceDocument>(
                available.Document.StructuralSalience);
        Assert.NotEmpty(salience.NamespaceIndex.Rows);
        Assert.Equal(
            salience.NamespaceIndex.Rows.Length,
            salience.TypeLeverageShards.Length);
        Assert.Contains(
            salience.TypeLeverageShards,
            static shard => !shard.Rows.IsEmpty);
        Assert.Equal(
            analysis.Receipt.ModuleIdentity.ModuleVersionId,
            salience.NamespaceIndex.SignatureUse.Receipt.ModuleVersionId);
        Assert.Equal(
            LibraryStructuralEvidenceDisposition.Complete,
            salience.NamespaceIndex.Disposition);
    }
}
