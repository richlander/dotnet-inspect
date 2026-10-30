using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class LibraryMetricsQueryTests
{
    [Fact]
    public void
        AssemblyContextRequest_SelectsFocusedStructuralEvidence()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                AssemblyContextLibraryMetricsQuery
                    .CreateAnalysisRequest());

        Assert.True(analysis.StructuralMetrics.WasRequested);
        Assert.False(
            analysis.ImplementationProfiles.WasRequested);
        Assert.IsType<LibraryMetricsResult.Available>(
            LibraryMetricsQuery.Execute(analysis));
    }

    [Fact]
    public void Execute_PublishesResearchDocumentFromImplementationProfiles()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest
                    .CreateCompleteImplementationProfile());

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
                        LibraryBodyAnalysisFeatures.MethodEvidence)
                    .WithCompleteImplementationProfile());

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
    public void Execute_SharedMethodEvidencePreservesStructuralPopulation()
    {
        string path =
            FixtureCatalog.AnalysisCallerLoop.AssemblyPath();
        LibraryBodyAnalysisExecution focusedAnalysis =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest
                    .CreateLibraryStructuralReport());
        LibraryBodyAnalysisExecution sharedAnalysis =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                        LibraryBodyAnalysisFeatures
                            .MethodEvidence)
                    .WithLibraryStructuralReport());
        LibraryMetricsResult.Available focused =
            Assert.IsType<LibraryMetricsResult.Available>(
                LibraryMetricsQuery.Execute(
                    focusedAnalysis));
        LibraryMetricsResult.Available shared =
            Assert.IsType<LibraryMetricsResult.Available>(
                LibraryMetricsQuery.Execute(
                    sharedAnalysis));

        Assert.True(
            focused.Document.Population.CompleteProfileCount > 0);
        Assert.Equal(
            focused.Document.Population.CompleteProfileCount,
            shared.Document.Population.CompleteProfileCount);
        Assert.Equal(
            focused.Document.Population.IncompleteProfileCount,
            shared.Document.Population.IncompleteProfileCount);
        LibraryStructuralMetricDistribution focusedComplexity =
            Assert.Single(
                focused.Document.Distributions,
                static distribution =>
                    distribution.Metric
                        == LibraryStructuralMetric
                            .NormalFlowCyclomaticComplexity);
        LibraryStructuralMetricDistribution sharedComplexity =
            Assert.Single(
                shared.Document.Distributions,
                static distribution =>
                    distribution.Metric
                        == LibraryStructuralMetric
                            .NormalFlowCyclomaticComplexity);
        Assert.Equal(
            focusedComplexity.CompleteBodyCount,
            sharedComplexity.CompleteBodyCount);
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
                        LibraryBodyAnalysisFeatures.MethodEvidence)
                    .WithCompleteImplementationProfile());
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
