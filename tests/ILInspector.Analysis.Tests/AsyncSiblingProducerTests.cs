using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public class AsyncSiblingProducerTests
{
    static readonly string FixturePath =
        typeof(OptimizationOpportunityFixtures).Assembly.Location;

    static AssemblyAnalysisOperation<AsyncSiblingProducerResult> CreateOperation()
    {
        AsyncSiblingProducer producer = AsyncSiblingProducer.Instance;
        WorkDescription work = Assert.IsType<ProducerPlanResult.Accepted>(
            ProducerPlanner.Plan(
                [new ProducerRequest(producer, ProducerTerminal.Rows)]))
            .Description;
        return AssemblyAnalysisOperation<AsyncSiblingProducerResult>.Create(
            "async-sibling-test",
            MethodDefinitionSourceRequest<AsyncSiblingProducerResult>.Create(
                work,
                producer,
                MethodDefinitionSourceBreadth.AllDefinitions));
    }

    static AssemblyReferenceBindingAccess CreateBinding()
    {
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(FixturePath)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        return new AssemblyReferenceBindingAccess(
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local("async sibling test")),
            new AssemblyReferenceBindingPolicy(resolver));
    }

    [Fact]
    public void Service_RejectsMissingReferenceBinding()
    {
        var operation = CreateOperation();
        using var session = AssemblyInspectionSession.Open(FixturePath);

        var result = session.SnapshotOperation(
            operation,
            access => AssemblyAnalysisService.Instance.Execute(
                operation,
                access));

        var rejected = Assert.IsType<
            AssemblyAnalysisServiceResult<AsyncSiblingProducerResult>.Rejected>(
            result);
        Assert.Equal(
            AssemblyAnalysisRejectionKind.ReferenceBindingUnavailable,
            rejected.Kind);
    }

    [Fact]
    public void Producer_FindsSyncCallWithAsyncSibling()
    {
        var operation = CreateOperation();
        using var session = AssemblyInspectionSession.Open(FixturePath);

        AsyncSiblingProducerResult result = session.SnapshotOperation(
            operation,
            CreateBinding(),
            access =>
            {
                var completed = Assert.IsType<
                    AssemblyAnalysisServiceResult<AsyncSiblingProducerResult>
                        .Completed>(
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access));
                var produced = completed.Execution.ResultOf(
                    AsyncSiblingProducer.Instance);
                Assert.True(produced.HasValue, produced.Failure?.ToString());
                return produced.Value!;
            });

        AsyncSiblingRow row = Assert.Single(
            result.Rows,
            r => r.Caller.Name
                == nameof(OptimizationOpportunityAsyncSiblingFixtures
                    .CallsSyncSiblingFromAsync));
        Assert.Contains("Async", row.Alternative.Name, StringComparison.Ordinal);
        Assert.Equal(AsyncSiblingPairKind.Operation, row.PairKind);
        Assert.Empty(result.Diagnostics);
    }
}
