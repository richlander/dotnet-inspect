using System.Collections.Immutable;
using DotnetInspector.Platforms;
using DotnetInspector.QueriesConsumer;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspacePlanTests
{
    [Fact]
    public async Task EmptyPlanIsReusableWithoutSharingLiveIdentity()
    {
        WorkspacePlan plan = new();
        Assert.Empty(plan.Registrations);
        Assert.Empty(WorkspacePlan.Empty.Registrations);
        await using InspectionWorkspace first = WorkspaceRegistrationConsumer.Create(plan);
        await using InspectionWorkspace second = WorkspaceRegistrationConsumer.Create(plan);

        WorkspaceRegistrationRevision firstRevision = Current(first);
        WorkspaceRegistrationRevision secondRevision = Current(second);
        Assert.Same(plan, firstRevision.Plan);
        Assert.Same(plan, secondRevision.Plan);
        Assert.NotSame(firstRevision.Workspace, secondRevision.Workspace);
        Assert.NotSame(firstRevision.Identity, secondRevision.Identity);
    }

    [Fact]
    public async Task PublicConsumerRetainsExactOrderedOwnerValues()
    {
        var library = new WorkspaceRegistration.ExactLibrary(
            WorkspaceRegistrationTestData.RealPackageSystemTextJson());
        var prefix = new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."));
        var ecosystem = new WorkspaceRegistration.Ecosystem(Platform());
        ImmutableArray<WorkspaceRegistration> registrations = [library, prefix, ecosystem];
        WorkspacePlan plan = WorkspaceRegistrationConsumer.CreatePlan(registrations);

        Assert.Equal(registrations, plan.Registrations);
        await using InspectionWorkspace workspace = WorkspaceRegistrationConsumer.Create(plan);
        WorkspaceRegistrationObservation observed = WorkspaceRegistrationConsumer.Observe(workspace);
        Assert.Same(plan, observed.Revision.Plan);
        Assert.Equal(plan.Registrations, observed.Revision.Registrations);
        Assert.Same(library.Coordinate, Assert.Single(observed.ExactLibraries));
        Assert.Same(prefix.Prefix, Assert.Single(observed.PackagePrefixes));
        Assert.Same(ecosystem.Declaration, Assert.Single(observed.Ecosystems));
        Assert.Equal("System.Text.Json", library.Coordinate.LibraryIdentity.Identity.Name);

        var scope = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync());
        Assert.Empty(scope.Snapshot.Packages);
    }

    [Fact]
    public void PlanConstructionRejectsTheWholeInvalidSet()
    {
        var library = new WorkspaceRegistration.ExactLibrary(
            WorkspaceRegistrationTestData.RealPackageSystemTextJson());
        var prefix = new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."));
        ImmutableArray<WorkspaceRegistration>[] invalid =
        [
            default,
            [null!],
            [prefix, library, new WorkspaceRegistration.ExactLibrary(
                WorkspaceRegistrationTestData.RealPackageSystemTextJson())],
            [prefix, library, new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."))],
            [prefix, new WorkspaceRegistration.Ecosystem(Platform()),
                new WorkspaceRegistration.Ecosystem(Platform())],
        ];
        foreach (ImmutableArray<WorkspaceRegistration> registrations in invalid)
        {
            ArgumentException error = Assert.Throws<ArgumentException>(
                () => WorkspaceRegistrationConsumer.CreatePlan(registrations));
            Assert.Equal("registrations", error.ParamName);
        }
        Assert.Throws<ArgumentNullException>(
            () => WorkspaceRegistrationConsumer.Create((WorkspacePlan)null!));
    }

    [Fact]
    public async Task ReplacementChangesOneLivePlanWithoutMutatingSharedData()
    {
        var original = new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."));
        var replacement = new WorkspaceRegistration.PackagePrefix(new("Aspire."));
        WorkspacePlan plan = new([original]);
        await using var first = new InspectionWorkspace(plan);
        await using var second = new InspectionWorkspace(plan);
        WorkspaceRegistrationRevision initial = Current(first);
        var unchanged = Assert.IsType<WorkspaceRegistrationOperationResult.NoEffect>(
            first.ReplaceRegistrations(initial,
                [new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."))]));
        Assert.Same(initial, unchanged.Revision);
        Assert.Same(plan, unchanged.Revision.Plan);

        var changed = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            first.ReplaceRegistrations(initial, [replacement]));
        Assert.NotSame(plan, changed.Revision.Plan);
        Assert.Equal([replacement], changed.Revision.Plan.Registrations);
        Assert.Equal([original], plan.Registrations);
        Assert.Same(plan, initial.Plan);
        Assert.Same(plan, Current(second).Plan);
        Assert.Same(changed.Revision, Current(first));
    }

    [Fact]
    public async Task HistoricalPlanCanCreateAnotherOwnerButCannotTransferRevisionAuthority()
    {
        WorkspacePlan plan = new([new WorkspaceRegistration.Ecosystem(Platform())]);
        var first = new InspectionWorkspace(plan);
        WorkspaceRegistrationRevision initial = Current(first);
        await first.DisposeAsync();
        var historical = Assert.IsType<WorkspaceRegistrationReadResult.Unavailable>(
            first.GetRegistrationSnapshot());
        Assert.Same(plan, historical.LastRevision.Plan);

        await using var second = new InspectionWorkspace(historical.LastRevision.Plan);
        WorkspaceRegistrationRevision current = Current(second);
        Assert.Same(plan, current.Plan);
        Assert.NotSame(initial.Workspace, current.Workspace);
        var rejected = Assert.IsType<WorkspaceRegistrationOperationResult.Rejected>(
            second.ReplaceRegistrations(initial, plan.Registrations));
        Assert.Equal(WorkspaceRegistrationRejection.ForeignWorkspace, rejected.Reason);
        Assert.Same(current, rejected.Revision);
    }

    [Fact]
    public async Task NewEcosystemCorrespondencePublishesANewPlan()
    {
        WorkspaceEcosystemRegistrationDeclaration first = Platform();
        WorkspaceEcosystemRegistrationDeclaration replacement = Platform();
        WorkspacePlan plan = new([new WorkspaceRegistration.Ecosystem(first)]);
        await using var workspace = new InspectionWorkspace(plan);
        var changed = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(Current(workspace),
                [new WorkspaceRegistration.Ecosystem(replacement)]));

        Assert.Same(first, Assert.IsType<WorkspaceRegistration.Ecosystem>(
            Assert.Single(plan.Registrations)).Declaration);
        Assert.Same(replacement, Assert.IsType<WorkspaceRegistration.Ecosystem>(
            Assert.Single(changed.Revision.Plan.Registrations)).Declaration);
        Assert.NotSame(plan, changed.Revision.Plan);
    }

    [Fact]
    public async Task ConvenienceConstructionUsesTheSamePlanBoundary()
    {
        var registration = new WorkspaceRegistration.PackagePrefix(new("Aspire."));
        await using var empty = new InspectionWorkspace();
        await using var explicitWorkspace = new InspectionWorkspace([registration]);

        Assert.Same(WorkspacePlan.Empty, Current(empty).Plan);
        Assert.Equal([registration], Current(explicitWorkspace).Plan.Registrations);
    }

    static WorkspaceRegistrationRevision Current(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;

    static WorkspaceEcosystemRegistrationDeclaration Platform() =>
        new(WorkspaceEcosystemRegistrationId.Create("ecosystem.platform"),
            ["System"], [],
            [new WorkspaceEcosystemPopulationDeclaration.Platform(
                new(PlatformFamily.DotNetRuntime))]);
}
