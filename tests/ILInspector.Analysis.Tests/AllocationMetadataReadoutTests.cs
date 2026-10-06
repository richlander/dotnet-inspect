using DotnetInspector.Fixtures;
using ILInspector.AnalysisHarness;

namespace ILInspector.Analysis.Tests;

public class AllocationMetadataReadoutTests
{
    [Fact]
    public void Measure_ProjectsFocusedAnalysisResults()
    {
        string path = FixtureCatalog.AnalysisRender.AssemblyPath();
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.Allocations
                        | LibraryBodyAnalysisFeatures
                            .OptimizationOpportunities));

        AllocationReadout readout =
            AllocationMetadataReadout.Measure([path]);

        Assert.Equal(1, readout.Assemblies);
        Assert.Equal(1, readout.Opened);
        Assert.Equal(0, readout.Failed);
        Assert.Equal(
            execution.CallGraph.Methods.Length,
            readout.Methods);
        Assert.Equal(
            execution.Allocations.Occurrences.Values.Sum(
                static occurrences => occurrences.Length),
            readout.AllocationOccurrences);
        Assert.Equal(
            execution.Optimization.Opportunities.Length,
            readout.OptimizationOpportunities);
    }

    [Fact]
    public void Measure_CountsFailedAssemblyWithoutPublishingEvidence()
    {
        AllocationReadout readout =
            AllocationMetadataReadout.Measure(
                ["/not/a/real/assembly.dll"]);

        Assert.Equal(1, readout.Assemblies);
        Assert.Equal(0, readout.Opened);
        Assert.Equal(1, readout.Failed);
        Assert.Equal(0, readout.Methods);
        Assert.Equal(0, readout.AllocationOccurrences);
        Assert.Equal(0, readout.OptimizationOpportunities);
    }
}
