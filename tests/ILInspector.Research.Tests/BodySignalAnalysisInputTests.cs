using System.Collections.Immutable;
using System.Runtime.InteropServices;
using ILInspector.Analysis;

namespace ILInspector.Research.Tests;

public sealed class BodySignalAnalysisInputTests
{
    [Fact]
    public void ConstructorRejectsResultsFromDifferentExecutions()
    {
        LibraryBodyAnalysisExecution first = Analyze(
            LibraryBodyAnalysisFeatures.Default);
        LibraryBodyAnalysisExecution second = Analyze(
            LibraryBodyAnalysisFeatures.Default);

        var exception = Assert.Throws<ArgumentException>(() =>
            new BodySignalAnalysisInput(
                first.Allocations,
                second.Safety,
                first.CallGraph,
                first.Optimization));

        Assert.Contains("one execution receipt", exception.Message);
    }

    [Theory]
    [InlineData(
        LibraryBodyAnalysisFeatures.MethodEvidence,
        "allocation evidence")]
    [InlineData(
        LibraryBodyAnalysisFeatures.MethodEvidence
            | LibraryBodyAnalysisFeatures.Allocations,
        "optimization evidence")]
    public void ConstructorRequiresAllComparisonEvidence(
        LibraryBodyAnalysisFeatures features,
        string expectedMessage)
    {
        LibraryBodyAnalysisExecution execution = Analyze(features);

        var exception = Assert.Throws<ArgumentException>(() =>
            new BodySignalAnalysisInput(
                execution.Allocations,
                execution.Safety,
                execution.CallGraph,
                execution.Optimization));

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void ConstructorRequiresFullMethodEvidenceScope()
    {
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecuteImage(
                "scoped",
                Image(),
                LibraryBodyAnalysisRequest.Create(
                    LibraryBodyAnalysisFeatures.Default,
                    bodyScope: ImmutableHashSet<int>.Empty));

        var exception = Assert.Throws<ArgumentException>(() =>
            new BodySignalAnalysisInput(
                execution.Allocations,
                execution.Safety,
                execution.CallGraph,
                execution.Optimization));

        Assert.Contains(
            "full method-evidence scope",
            exception.Message);
    }

    static LibraryBodyAnalysisExecution Analyze(
        LibraryBodyAnalysisFeatures features)
        => LibraryBodyAnalysisService.ExecuteImage(
            "analysis",
            Image(),
            LibraryBodyAnalysisRequest.Create(features));

    static ImmutableArray<byte> Image()
        => ImmutableCollectionsMarshal.AsImmutableArray(
            File.ReadAllBytes(
                typeof(BodySignalAnalysisInputTests)
                    .Assembly.Location));
}
