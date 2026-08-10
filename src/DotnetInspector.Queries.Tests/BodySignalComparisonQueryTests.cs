using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Research;

namespace DotnetInspector.Queries.Tests;

public sealed class BodySignalComparisonQueryTests
{
    [Fact]
    public void Execute_ReturnsResearchOwnedAnalysisEvidenceFromSuppliedIndexes()
    {
        var oldIndex = LibraryBodyIndex.Open(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        var newIndex = LibraryBodyIndex.Open(
            FixtureCatalog.DiffPair.NewAssemblyPath());

        ResearchComparison comparison = BodySignalComparisonQuery.Execute(
            new BodySignalComparisonInput([oldIndex], [newIndex]));

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
}
