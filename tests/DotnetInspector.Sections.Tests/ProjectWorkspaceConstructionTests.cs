using System.Text;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections.Tests;

public sealed class ProjectWorkspaceConstructionTests
{
    [Fact]
    public async Task AuthoredPlanRetainsIncompleteDeclarationsWithoutRealizingPackages()
    {
        var inspection = ProjectInputInspection.Execute(
            ProjectInputKind.AuthoredProject,
            "DotnetInspect.Cli.csproj",
            Encoding.UTF8.GetBytes("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>$(DefaultTargetFramework)</TargetFramework></PropertyGroup>
                  <ItemGroup><PackageReference Include="Markout" /></ItemGroup>
                </Project>
                """));
        var original = Assert.IsType<AuthoredProjectDependencyFactsResult.Incomplete>(
            inspection.Content.AuthoredProject);
        var policy = new TraversalTargetFrameworkPolicy("net10.0");
        WorkspacePlan plan = Assert.IsType<ProjectWorkspacePlanOutcome.Available>(
            ProjectWorkspaceConstruction.CreatePlan(inspection.Content, policy)).Plan;

        var project = Assert.IsType<WorkspaceProjectInput.Authored>(plan.Project);
        Assert.Same(original, project.Result);
        Assert.Same(original.Value, project.Facts);
        Assert.NotEmpty(original.Limitations);
        Assert.Null(Assert.Single(project.Facts.PackageDeclarations).CanonicalVersionConstraint);
        Assert.Equal(inspection.Content.DeclaredName, project.DeclaredName);
        Assert.Same(policy, plan.TraversalTargetPolicy);
        Assert.Empty(plan.Contexts);
        Assert.Empty(plan.Registrations);
        Assert.IsType<InspectionShare.NonProjectable>(inspection.Share);

        await using var workspace = new InspectionWorkspace(plan);
        var scope = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync());
        Assert.NotNull(scope.Snapshot.Project);
        Assert.Same(project, scope.Snapshot.Project.Input);
        Assert.Empty(scope.Snapshot.Packages);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("partial")]
    [InlineData("unavailable")]
    [InlineData("failedGraph")]
    public void RestoredPlanRetainsTheExactProviderOutcome(string phase)
    {
        string assets = phase switch
        {
            "partial" => ProjectInputInspectionTests.Assets.Replace(
                """{ "type": "package" }""",
                """{ "type": "package", "dependencies": { "Missing.Package": "1.0.0" } }""",
                StringComparison.Ordinal),
            "failedGraph" => ProjectInputInspectionTests.Assets.Replace(
                "\"targets\": {",
                "\"targets\": { \"NET11.0\": {},",
                StringComparison.Ordinal),
            _ => ProjectInputInspectionTests.Assets,
        };
        ProjectInputDocument input = ProjectInputInspection.Execute(
            ProjectInputKind.RestoredProject,
            "project.assets.json",
            Encoding.UTF8.GetBytes(assets),
            phase == "unavailable" ? new("net12.0") : null).Content;
        WorkspacePlan plan = Assert.IsType<ProjectWorkspacePlanOutcome.Available>(
            ProjectWorkspaceConstruction.CreatePlan(
                input, TraversalTargetFrameworkPolicy.ProductDefault)).Plan;
        var project = Assert.IsType<WorkspaceProjectInput.Restored>(plan.Project);
        Assert.Same(input.RestoredTraversal, project.Result);
        Assert.Empty(plan.Contexts);
        Assert.Empty(plan.Registrations);
        switch (phase)
        {
            case "complete":
            case "partial":
                var available = Assert.IsType<RestoredProjectDependencyTraversalResult.Available>(
                    project.Result);
                Assert.Same(available.Value.Facts, project.Facts);
                Assert.Single(available.Value.PackageRelationships);
                Assert.Equal(
                    phase == "partial"
                        ? RestoredProjectTraversalCompletion.Partial
                        : RestoredProjectTraversalCompletion.Complete,
                    available.Value.Completion);
                break;
            case "unavailable":
                var unavailable = Assert.IsType<RestoredProjectDependencyTraversalResult.Unavailable>(
                    project.Result);
                Assert.Same(unavailable.Facts, project.Facts);
                Assert.Null(project.Facts.SelectedTarget);
                break;
            case "failedGraph":
                var failed = Assert.IsType<RestoredProjectDependencyTraversalResult.Failed>(
                    project.Result);
                var graph = Assert.IsType<RestoredProjectDependencyTraversalFailure.Graph>(
                    failed.Failure);
                Assert.Same(graph.Facts, project.Facts);
                Assert.Equal(
                    RestoredProjectGraphFailureReason.AmbiguousTargetIdentity,
                    graph.Failure.Reason);
                break;
        }
    }

    [Fact]
    public void TraversalPolicyDoesNotReselectTheRestoredFrameworkOrRuntime()
    {
        string assets = ProjectInputInspectionTests.Assets.Replace(
            "net11.0", "net10.0", StringComparison.Ordinal).Replace(
            "\"targets\": {",
            """
            "targets": {
              "net10.0/linux-x64": {
                "Newtonsoft.Json/13.0.4": { "type": "package" }
              },
            """,
            StringComparison.Ordinal);
        ProjectInputDocument input = ProjectInputInspection.Execute(
            ProjectInputKind.RestoredProject,
            "project.assets.json",
            Encoding.UTF8.GetBytes(assets),
            new("net10.0", "linux-x64")).Content;
        WorkspacePlan plan = Assert.IsType<ProjectWorkspacePlanOutcome.Available>(
            ProjectWorkspaceConstruction.CreatePlan(
                input, TraversalTargetFrameworkPolicy.ProductDefault)).Plan;
        var project = Assert.IsType<WorkspaceProjectInput.Restored>(plan.Project);

        Assert.Equal("net10.0", project.Facts.SelectedTarget?.FrameworkIdentity);
        Assert.Equal("linux-x64", project.Facts.SelectedTarget?.RuntimeIdentifierIdentity);
        var traversal = Assert.IsType<RestoredProjectDependencyTraversalResult.Available>(
            project.Result);
        Assert.Single(traversal.Value.PackageRelationships);
        Assert.Same(input.RestoredTraversal, traversal);
        Assert.Equal("net11.0", plan.TraversalTargetPolicy.TargetFramework);
    }

    [Theory]
    [InlineData(ProjectInputKind.AuthoredProject, "<Project")]
    [InlineData(ProjectInputKind.RestoredProject, "{")]
    [InlineData(ProjectInputKind.RestoredProject, """{"version":99}""")]
    public void WholeDocumentFailureCannotSeedAWorkspace(
        ProjectInputKind kind,
        string content)
    {
        ProjectInputDocument input = ProjectInputInspection.Execute(
            kind,
            "rejected input",
            Encoding.UTF8.GetBytes(content)).Content;
        var rejected = Assert.IsType<ProjectWorkspacePlanOutcome.Rejected>(
            ProjectWorkspaceConstruction.CreatePlan(
                input, TraversalTargetFrameworkPolicy.ProductDefault));

        Assert.Same(input, rejected.Input);
        Assert.NotEmpty(rejected.Input.Evidence.Failures);
        Assert.Equal(1, rejected.Input.Evidence.Summary.FailedRootCount);
    }
}
