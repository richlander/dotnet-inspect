using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed class ResourceTriageQueryTests
{
    const string ReadBeforeReturn = "RentReadBeforeReturn";
    const string NormalAndExceptionalExit =
        "RentAcrossNormalAndExceptionalExit";

    [Fact]
    public void Execute_ReturnsLifecycleFindingsAndTypedAssessments()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                PreferImplementationAssemblies = true,
            });
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                    ArrayPoolResourceEffectModel.Create()),
                resolver);
        LibraryResourceLifecycleAnalysisResult lifecycle =
            execution.ResourceLifecycle;
        ResourceLifecycleMethodResult lifecycleMethod = Assert.Single(
            lifecycle.Methods,
            method => method.Method.Name == ReadBeforeReturn);
        ResourceLifecycleRootResult lifecycleRoot =
            Assert.Single(lifecycleMethod.Roots);
        Assert.True(
            lifecycleRoot.IsComplete,
            string.Join(
                "; ",
                lifecycleRoot.Limitations.Select(limitation =>
                    $"{limitation.Kind}: {limitation.Detail} "
                    + $"call={limitation.OccurrenceLimitation?.Call?.Callee} "
                    + $"gap={limitation.OccurrenceLimitation?.EffectResolutionGap}")));
        Assert.Contains(
            lifecycleRoot.Outcomes,
            outcome =>
                outcome.Kind
                == ResourceLifecycleOutcomeKind
                    .ExceptionalCleanupMissing);

        ResourceTriageResult result = ResourceTriageQuery.Execute(
            lifecycle,
            new FindingSubject("query-tests", "query-tests"));

        var available =
            Assert.IsType<ResourceTriageResult.Available>(result);
        Assert.DoesNotContain(
            available.Assessments,
            candidate =>
                candidate.Source.Payload.Method.Name
                    == NormalAndExceptionalExit);
        ResourceTriageAssessment assessment = Assert.Single(
            available.Assessments,
            candidate =>
                candidate.Source.Payload.Method.Name
                    == ReadBeforeReturn);
        Assert.Contains(
            available.Inspection.Findings,
            finding => finding == assessment.Source);
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            assessment.Actionability);
        Assert.Contains(
            assessment.Boundaries,
            boundary =>
                boundary.Kind
                    == ResourceTriageBoundaryKind.ExternalInput);

    }

    [Fact]
    public void Definition_IsUnbounded()
        => Assert.Equal(
            InspectionCost.Unbounded,
            ResourceTriageQuery.Definition.Cost);

    [Fact]
    public void Execute_RejectsPartialMethodScope()
    {
        string path =
            FixtureCatalog.AnalysisOwnershipFlow.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                PreferImplementationAssemblies = true,
            });
        LibraryBodyAnalysisExecution full =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                    ArrayPoolResourceEffectModel.Create()),
                resolver);
        int cleanMethod = Assert.Single(
            full.ResourceLifecycle.Methods,
            method => method.Method.Name == "RentAndReturnDirectly")
            .Method.MetadataToken;
        LibraryResourceLifecycleAnalysisResult scoped =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                    ArrayPoolResourceEffectModel.Create(),
                    bodyScope: new HashSet<int> { cleanMethod }),
                resolver)
            .ResourceLifecycle;

        Assert.False(scoped.Receipt.HasFullMethodEvidenceScope);
        Assert.IsType<ResourceTriageResult.Failed>(
            ResourceTriageQuery.Execute(
                scoped,
                new FindingSubject("query-tests", "query-tests")));
    }

}
