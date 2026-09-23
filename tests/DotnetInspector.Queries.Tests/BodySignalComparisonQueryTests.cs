using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class BodySignalComparisonQueryTests
{
    [Fact]
    public void Execute_ReturnsResearchOwnedEvidenceFromFocusedAnalysis()
    {
        BodySignalAnalysisInput oldAnalysis = Analyze(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        BodySignalAnalysisInput newAnalysis = Analyze(
            FixtureCatalog.DiffPair.NewAssemblyPath());

        ResearchComparison comparison = BodySignalComparisonQuery.Execute(
            new BodySignalComparisonInput(
                [oldAnalysis],
                [newAnalysis]));

        ResearchChange regression = Assert.Single(
            comparison.Changes,
            change => change.Descriptor.Id == AnalysisFindings.AllocationDescriptor.Id
                && change.Subject.Display.Contains(
                    "RegressesAllocInLoop",
                    StringComparison.Ordinal));
        Assert.Equal("1", regression.OldValue);
        Assert.Equal("2", regression.NewValue);
        Assert.Equal("in-loop", regression.Shape);
        Assert.Equal(1, regression.DirectionScore);
    }

    [Fact]
    public void Definition_IsUnbounded()
        => Assert.Equal(
            InspectionCost.Unbounded,
            BodySignalComparisonQuery.Definition.Cost);

    [Fact]
    public void Execute_EmptyFocusedPopulations_ReturnsEmptyComparison()
    {
        ResearchComparison comparison = BodySignalComparisonQuery.Execute(
            new BodySignalComparisonInput([], []));

        Assert.Empty(comparison.Changes);
    }

    static BodySignalAnalysisInput Analyze(string path)
    {
        const LibraryBodyAnalysisFeatures features =
            LibraryBodyAnalysisFeatures.MethodEvidence
            | LibraryBodyAnalysisFeatures.Allocations
            | LibraryBodyAnalysisFeatures.OptimizationOpportunities;
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(features));
        return new(
            execution.Allocations,
            execution.Safety,
            execution.CallGraph,
            execution.Optimization);
    }
}
