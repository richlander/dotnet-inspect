using DotnetInspector.Fixtures;
using ILInspector.Analysis;

namespace DotnetInspector.Queries.Tests;

public sealed class OptimizationOpportunitiesQueryTests
{
    [Fact]
    public void Execute_RetainsCompletedOptimizationEvidence()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures
                        .OptimizationOpportunities));

        OptimizationOpportunitiesResult result =
            OptimizationOpportunitiesQuery.Execute(
                analysis.Optimization,
                includeAllocationFanout: true);

        var available =
            Assert.IsType<OptimizationOpportunitiesResult.Available>(
                result);
        Assert.NotEmpty(available.Opportunities);
        Assert.NotEmpty(
            available.AllocationFanoutOpportunities);
        Assert.Equal(
            analysis.Receipt.Diagnostics,
            available.Diagnostics);
    }

    [Fact]
    public void Execute_MissingOptimizationAcquisitionRemainsTypedFailure()
    {
        LibraryBodyAnalysisExecution analysis =
            LibraryBodyAnalysisService.ExecutePath(
                FixtureCatalog.AnalysisCallerLoop.AssemblyPath(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.MethodEvidence));

        OptimizationOpportunitiesResult result =
            OptimizationOpportunitiesQuery.Execute(
                analysis.Optimization,
                includeAllocationFanout: false);

        var failed =
            Assert.IsType<OptimizationOpportunitiesResult.Failed>(
                result);
        Assert.Contains(
            "were not requested",
            failed.Error.Message,
            StringComparison.Ordinal);
    }
}
