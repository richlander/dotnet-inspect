using ILInspector.Analysis;
using ILInspector.Evidence;

namespace ILInspector.Analysis.Tests;

public sealed class AnalysisEvidenceTests
{
    [Fact]
    public void DescribeAssembly_EmitsPresentRowsForAnalysisFacts()
    {
        var result = AnalysisEvidence.DescribeAssembly(typeof(OptimizationOpportunityFixtures).Assembly.Location);

        Assert.True(result.Succeeded, result.Failure);
        Assert.All(result.Rows, row => Assert.Equal(EvidenceRowKind.Present, row.Kind));
        Assert.Contains(result.Rows, row =>
            row.Descriptor.Id == "resource.leak"
            && row.Payload is LeakTriageFinding { Method.Name: nameof(ArrayPoolLeakFixtures.UseAfterReturn), Shape: "arraypool-use-after-return" });
        Assert.Contains(result.Rows, row =>
            row.Descriptor.Id == "alloc.in-loop"
            && row.Payload is AllocationOccurrence { Method.Name: nameof(OptimizationOpportunityFixtures.CapturingDelegateInLoop), InLoop: true });
        Assert.Contains(result.Rows, row =>
            row.Descriptor.Id == "signal.alloc-in-loop"
            && row.Payload is AnalysisSignalFact { Method.Name: nameof(OptimizationOpportunityFixtures.CapturingDelegateInLoop) });
        Assert.Contains(result.Rows, row =>
            row.Descriptor.Id.StartsWith("optimization.", StringComparison.Ordinal)
            && row.Payload is OptimizationOpportunity { Method.Name: nameof(OptimizationOpportunityFixtures.CapturingDelegateInLoop) });
        Assert.Contains(result.Rows, row =>
            row.Descriptor.Id == "semantic.cost"
            && row.Payload is CostFact { CostKind: "virtual dispatch" });
    }

    [Fact]
    public void DescribeMethod_FiltersFactsToOneMethod()
    {
        var index = LibraryBodyIndex.Open(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var method = Assert.Single(index.Methods.Where(method => method.Name == nameof(OptimizationOpportunityFixtures.CapturingDelegateInLoop)));

        var result = AnalysisEvidence.DescribeMethod(index, method.MetadataToken);

        Assert.True(result.Succeeded, result.Failure);
        Assert.NotEmpty(result.Rows);
        Assert.All(result.Rows, row => Assert.Equal(AnalysisEvidence.MethodKey(method), row.Subject.Key));
        Assert.Contains(result.Rows, row => row.Descriptor.Id == "alloc.in-loop");
    }

    [Fact]
    public void EvidenceDiffSummary_IsStreamAgnosticOverAnalysisRows()
    {
        var result = AnalysisEvidence.DescribeAssembly(typeof(OptimizationOpportunityFixtures).Assembly.Location);
        var analysisRows = result.Rows
            .Where(row => row.Descriptor.Id == "alloc.in-loop"
                && row.Payload is AllocationOccurrence { Method.Name: nameof(OptimizationOpportunityFixtures.CapturingDelegateInLoop) })
            .ToArray();

        var summary = EvidenceDiffSummary.Summarize(analysisRows);

        Assert.NotEmpty(analysisRows);
        Assert.Equal(EvidenceFinding.Identical, summary.Finding);
        Assert.Equal(analysisRows.Length, summary.Present);
    }
}
