using System.Text;
using InertText;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceScopeTests
{
    [Fact]
    public async Task ProjectPlanIssuesIndependentOccurrencesAndOutlivesClosedWorkspaces()
    {
        WorkspacePlan plan = ProjectBearingPlan();
        await using var first = new InspectionWorkspace(plan);
        await using var second = new InspectionWorkspace(plan);
        WorkspaceScopeSnapshot firstScope = await Current(first);
        WorkspaceScopeSnapshot secondScope = await Current(second);
        Assert.NotNull(firstScope.Project);
        Assert.NotNull(secondScope.Project);
        Assert.Same(plan.Project, firstScope.Project.Input);
        Assert.Same(plan.Project, secondScope.Project.Input);
        Assert.Same(first.Identity, firstScope.Project.Workspace);
        Assert.Same(second.Identity, secondScope.Project.Workspace);
        Assert.NotSame(firstScope.Project.Identity, secondScope.Project.Identity);

        await first.CloseAsync();
        var closed = Assert.IsType<WorkspaceScopeReadResult.Unavailable>(
            await first.GetScopeSnapshotAsync());
        Assert.Same(firstScope, closed.LastSnapshot);
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, closed.RuntimeFailure);
        Assert.Same(plan.Project, firstScope.Project.Input);
        await using var third = new InspectionWorkspace(plan);
        WorkspaceScopeSnapshot thirdScope = await Current(third);
        Assert.NotNull(thirdScope.Project);
        Assert.Same(plan.Project, thirdScope.Project.Input);
        Assert.NotSame(firstScope.Project.Identity, thirdScope.Project.Identity);
    }

    [Fact]
    public async Task PackageEditsAndRegistrationReplacementPreserveTheProjectBasis()
    {
        WorkspacePlan plan = ProjectBearingPlan();
        await using var workspace = new InspectionWorkspace(plan);
        WorkspaceScopeSnapshot initial = await Current(workspace);
        Assert.NotNull(initial.Project);
        Assert.Empty(initial.Packages);

        var registration = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(
                RegistrationCurrent(workspace),
                [new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."))]));
        Assert.Same(plan.Project, registration.Revision.Plan.Project);
        Assert.Same(initial, await Current(workspace));

        WorkspaceScopeSnapshot? preparing = null;
        WorkspaceScopeSnapshot added = Committed(await workspace.AddPackagesAsync(
            initial.Revision,
            [Binding("Selected.Package", onOpen: () =>
                preparing = Current(workspace).GetAwaiter().GetResult())],
            Deadline, TestContext.Current.CancellationToken)).Snapshot;
        Assert.NotNull(preparing);
        Assert.NotNull(preparing.Preparing);
        WorkspaceScopeSnapshot removed = Committed(await workspace.RemovePackageOccurrenceAsync(
            added.Revision, added.Packages[0].Occurrence.Identity,
            Deadline, TestContext.Current.CancellationToken)).Snapshot;
        WorkspaceScopeSnapshot replaced = await Replace(workspace, Binding("Other.Package"));
        WorkspaceScopeSnapshot cleared = Committed(await workspace.ClearScopeAsync(
            replaced.Revision, Deadline, TestContext.Current.CancellationToken)).Snapshot;

        Assert.Empty(removed.Packages);
        Assert.Empty(cleared.Packages);
        Assert.All([preparing, added, removed, replaced, cleared],
            snapshot => Assert.Same(initial.Project, snapshot.Project));
        Assert.Same(initial.Project, cleared.Revision.Project);
        Assert.Same(plan.Project, initial.Project.Input);
    }

    [Fact]
    public async Task ProjectDependencyEvidenceDoesNotBecomeOrTruncateToPackageMembership()
    {
        int count = WorkspaceScopeLimits.DefaultMaxPackages + 1;
        string[] packages = [.. Enumerable.Range(0, count)
            .Select(index => $"Project.Dependency.{index}")];
        string nodes = string.Join(",", packages.Select(id =>
            $"\"{id}/1.0.0\": {{\"type\":\"package\"}}"));
        string declarations = string.Join(",", packages.Select(id =>
            $"\"{id}\": {{\"target\":\"Package\",\"version\":\"[1.0.0, )\"}}"));
        string rootDependencies = string.Join(",", packages.Select(id =>
            $"\"{id} >= 1.0.0\""));
        var result = Assert.IsType<RestoredProjectDependencyTraversalResult.Available>(
            RestoredProjectDependencyTraversalQuery.Execute(
                Encoding.UTF8.GetBytes($$"""
                    {
                      "version": 3,
                      "targets": { "net11.0": { {{nodes}} } },
                      "projectFileDependencyGroups": {
                        "net11.0": [ {{rootDependencies}} ]
                      },
                      "project": {
                        "frameworks": {
                          "net11.0": { "dependencies": { {{declarations}} } }
                        }
                      }
                    }
                    """)));
        var input = new WorkspaceProjectInput.Restored(
            new InertString(TextPolicy.Field, "project.assets.json"), result);
        var plan = new WorkspacePlan(
            TraversalTargetFrameworkPolicy.ProductDefault, [], [], input);
        await using var workspace = new InspectionWorkspace(plan);
        WorkspaceScopeSnapshot scope = await Current(workspace);

        Assert.Equal(count, result.Value.PackageRelationships.Length);
        Assert.Equal(RestoredProjectTraversalCompletion.Complete, result.Value.Completion);
        Assert.NotNull(scope.Project);
        Assert.Same(input, scope.Project.Input);
        Assert.Empty(scope.Revision.Packages);
        Assert.Empty(scope.Packages);
        using var physical = ArtifactAvailable(
            await workspace.ReadArtifactRootCompositionAsync(workspace.Identity));
        Assert.Empty(physical.Roots);
    }

    static WorkspacePlan ProjectBearingPlan()
    {
        var input = new WorkspaceProjectInput.Authored(
            new InertString(TextPolicy.Field, "DotnetInspect.Cli.csproj"),
            AuthoredProjectDependencyFactsQuery.Execute(Encoding.UTF8.GetBytes("""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>$(DefaultTargetFramework)</TargetFramework></PropertyGroup>
                  <ItemGroup><PackageReference Include="Markout" /></ItemGroup>
                </Project>
                """)));
        return new WorkspacePlan(
            TraversalTargetFrameworkPolicy.ProductDefault, [], [], input);
    }
}
