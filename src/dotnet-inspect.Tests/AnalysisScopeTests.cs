using DotnetInspector.Output;
using DotnetInspector.Sections;

namespace DotnetInspector.Tests;

/// <summary>
/// Locks the section -> index-build-phase mapping (<see cref="ApiOutputFormatter.AnalysisScopeFor"/>):
/// only Allocation Facts and Performance Triage consume the two expensive whole-assembly phases, so
/// every other analysis section builds the index without escape-classified allocations or
/// optimization opportunities.
/// </summary>
public class AnalysisScopeTests
{
    [Fact]
    public void PerformanceTriage_NeedsOpportunitiesAndAllocations()
    {
        var (alloc, opp) = ApiOutputFormatter.AnalysisScopeFor([SectionNames.PerformanceTriage]);
        Assert.True(opp);
        Assert.True(alloc); // opportunities are synthesized from allocation hotspots
    }

    [Fact]
    public void AllocationFacts_NeedsAllocationsButNotOpportunities()
    {
        var (alloc, opp) = ApiOutputFormatter.AnalysisScopeFor([SectionNames.AllocationFacts]);
        Assert.True(alloc);
        Assert.False(opp);
    }

    [Theory]
    [InlineData(SectionNames.TopLeverage)]
    [InlineData(SectionNames.Calls)]
    [InlineData(SectionNames.Callers)]
    [InlineData(SectionNames.CallGraph)]
    [InlineData(SectionNames.CostFacts)]
    [InlineData(SectionNames.SafetyFacts)]
    [InlineData(SectionNames.UnsafeMembers)]
    [InlineData(SectionNames.UnsafeOperations)]
    [InlineData(SectionNames.CalledTypes)]
    public void OtherAnalysisSections_NeedNeitherExpensivePhase(string section)
    {
        var (alloc, opp) = ApiOutputFormatter.AnalysisScopeFor([section]);
        Assert.False(alloc);
        Assert.False(opp);
    }

    [Fact]
    public void UnionOfSections_TakesTheBroadestScope()
    {
        var (alloc, opp) = ApiOutputFormatter.AnalysisScopeFor(
            [SectionNames.TopLeverage, SectionNames.AllocationFacts]);
        Assert.True(alloc);
        Assert.False(opp);
    }

    [Fact]
    public void NullSections_DefaultsToFullBuild()
    {
        var (alloc, opp) = ApiOutputFormatter.AnalysisScopeFor(null);
        Assert.True(alloc);
        Assert.True(opp);
    }
}
