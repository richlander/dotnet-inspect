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

        var incomplete =
            Assert.IsType<ResourceTriageResult.Incomplete>(result);
        Assert.NotEmpty(incomplete.Limitations);
        Assert.DoesNotContain(
            incomplete.Assessments,
            candidate =>
                candidate.Source.Payload.Method.Name
                    == NormalAndExceptionalExit);
        ResourceTriageAssessment assessment = Assert.Single(
            incomplete.Assessments,
            candidate =>
                candidate.Source.Payload.Method.Name
                    == ReadBeforeReturn);
        Assert.Contains(
            incomplete.Inspection.Findings,
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
    public void Execute_PreservesBoundaryClassificationAndIsolation()
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
        ResourceTriageResult result = ResourceTriageQuery.Execute(
            execution.ResourceLifecycle,
            new FindingSubject("query-tests", "query-tests"));
        var incomplete =
            Assert.IsType<ResourceTriageResult.Incomplete>(result);

        ResourceTriageAssessment lookalike = Assessment(
            incomplete,
            "RentLookalikeReadBeforeReturn");
        Assert.Equal(
            ResourceTriageActionability.Unknown,
            lookalike.Actionability);
        Assert.Equal(
            ResourceTriageBoundaryKind.Unknown,
            Assert.Single(lookalike.Boundaries).Kind);

        ResourceTriageAssessment framework = Assessment(
            incomplete,
            "RentTextReaderReadBeforeReturn");
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            framework.Actionability);
        Assert.Equal(
            ResourceTriageBoundaryKind.ExternalInput,
            Assert.Single(framework.Boundaries).Kind);

        ResourceTriageAssessment trusted = Assessment(
            incomplete,
            "RentEncodeThenUnrelatedReadAfterReturn");
        Assert.Equal(
            ResourceTriageActionability.TrustedLowActionability,
            trusted.Actionability);
        Assert.Equal(
            ResourceTriageReason.InMemoryBoundaryBeforeCleanup,
            trusted.Reason);
        ResourceTriageBoundaryAssessment trustedBoundary =
            Assert.Single(trusted.Boundaries);
        Assert.Equal(
            "GetBytes",
            trustedBoundary.Evidence.Operation.Name);
        Assert.Equal(
            ResourceTriageBoundaryKind.InMemoryTransform,
            trustedBoundary.Kind);
        Assert.DoesNotContain(
            trusted.Boundaries,
            boundary =>
                boundary.Evidence.Operation.Name == "ReadByte");
    }

    [Fact]
    public void Execute_PreservesWrapperControlFlowClassification()
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
        var incomplete = Assert.IsType<ResourceTriageResult.Incomplete>(
            ResourceTriageQuery.Execute(
                execution.ResourceLifecycle,
                new FindingSubject("query-tests", "query-tests")));

        AssertExternalRead(
            incomplete,
            "ExternalReadThroughReinitializedMemory");
        ResourceTriageAssessment loop = AssertExternalRead(
            incomplete,
            "ExternalReadThroughLoopReinitializedMemory");
        Assert.DoesNotContain(
            loop.Boundaries,
            boundary =>
                boundary.Evidence.Operation.Name == "Observe");
        AssertExternalRead(
            incomplete,
            "ExternalReadThroughConditionallyResetMemory");

        ResourceTriageAssessment disjoint = Assessment(
            incomplete,
            "DisjointMemoryUseDoesNotConsumeRent");
        Assert.Equal(
            ResourceTriageActionability.Unknown,
            disjoint.Actionability);
        Assert.Equal(
            ResourceTriageReason.UnclassifiedBoundaryBeforeCleanup,
            disjoint.Reason);
        ResourceTriageBoundaryAssessment disjointBoundary =
            Assert.Single(disjoint.Boundaries);
        Assert.Equal(
            ".ctor",
            disjointBoundary.Evidence.Operation.Name);
        Assert.Equal(
            ResourceTriageBoundaryKind.Unknown,
            disjointBoundary.Kind);
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

    static ResourceTriageAssessment Assessment(
        ResourceTriageResult.Incomplete result,
        string methodName) =>
        Assert.Single(
            result.Assessments,
            assessment =>
                assessment.Source.Payload.Method.Name == methodName);

    static ResourceTriageAssessment AssertExternalRead(
        ResourceTriageResult.Incomplete result,
        string methodName)
    {
        ResourceTriageAssessment assessment =
            Assessment(result, methodName);
        Assert.Equal(
            ResourceTriageActionability.UntrustedActionable,
            assessment.Actionability);
        Assert.Single(
            assessment.Boundaries.Where(boundary =>
                boundary.Kind
                    == ResourceTriageBoundaryKind.ExternalInput
                && boundary.Evidence.Operation.Name == "Read"));
        return assessment;
    }
}
