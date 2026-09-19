using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis;
using Inspector.Findings;

namespace DotnetInspector.Queries.Tests;

public sealed class ResourceTriageQueryTests
{
    [Fact]
    public void Execute_ReturnsLifecycleFindingsAndTypedAssessments()
    {
        string path =
            FixtureCatalog.AnalysisResourceLifecycle.AssemblyPath();
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path));
        LibraryBodyAnalysisExecution execution =
            LibraryBodyAnalysisService.ExecutePath(
                path,
                LibraryBodyAnalysisRequest.CreateResourceLifecycle(
                    ArrayPoolResourceEffectModel.Create()),
                resolver);

        ResourceTriageResult result = ResourceTriageQuery.Execute(
            execution.ResourceLifecycle,
            new FindingSubject("query-tests", "query-tests"));

        var available =
            Assert.IsType<ResourceTriageResult.Available>(result);
        ResourceTriageAssessment assessment = Assert.Single(
            available.Assessments,
            candidate =>
                candidate.Source.Payload.Method.Name
                == "ReadBeforeReturn");
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

}
