using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed class ResourceTriageQueryTests
{
    const string ReadBeforeReturn = "RentReadBeforeReturn";

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

        var legacyInspection =
            Assert.IsType<
                FindingInspection<ResourceLifecycleOccurrence>.Complete>(
                ResourceLifecycleAnalysis.InspectAssembly(
                    path,
                    new FindingSubject(
                        "query-tests",
                        "query-tests")).Value);
        ResourceTriageAssessment legacy = Assert.Single(
            ResourceTriageAnalysis.Assess(legacyInspection),
            candidate =>
                candidate.Source.Payload.Method.Name
                    == ReadBeforeReturn);
        Assert.Equal(legacy.Source.Key, assessment.Source.Key);
        Assert.Equal(legacy.Source.Payload, assessment.Source.Payload);
        Assert.Equal(
            legacy.Source.Descriptor,
            assessment.Source.Descriptor);
        Assert.Equal(legacy.Source.Detail, assessment.Source.Detail);
        Assert.Equal(legacy.CandidateId, assessment.CandidateId);
    }

    [Fact]
    public void Definition_IsUnbounded()
        => Assert.Equal(
            InspectionCost.Unbounded,
            ResourceTriageQuery.Definition.Cost);
}
