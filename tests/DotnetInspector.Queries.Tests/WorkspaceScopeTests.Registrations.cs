using System.Collections.Immutable;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceScopeTests
{
    [Fact]
    public async Task RegistrationReplacementPreservesPreparingPackagePublication()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Replace(workspace, Binding("Prior.Package"));
        WorkspaceRegistrationRevision registrations = RegistrationCurrent(workspace);
        WorkspaceScopeSnapshot? before = null;
        WorkspaceScopeSnapshot? after = null;
        WorkspaceRegistrationOperationResult? registrationResult = null;
        PackageRootBinding next = Binding("Next.Package", onOpen: () =>
        {
            before = Current(workspace).GetAwaiter().GetResult();
            registrationResult = workspace.ReplaceRegistrations(registrations,
                [new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."))]);
            after = Current(workspace).GetAwaiter().GetResult();
        });
        WorkspaceScopeSnapshot replaced = Committed(await workspace.ReplaceScopeAsync(
            prior.Revision, [next], Deadline, TestContext.Current.CancellationToken)).Snapshot;

        Assert.NotNull(before);
        Assert.NotNull(before.Preparing);
        Assert.Same(before, after);
        Assert.Same(prior.Revision, before.Revision);
        Assert.Same(prior.PhysicalComposition, before.PhysicalComposition);
        var committed = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(registrationResult);
        Assert.Same(committed.Revision, RegistrationCurrent(workspace));
        Assert.Equal(["Next.Package"], Names(replaced));
        Assert.Null(replaced.Preparing);
        WorkspaceScopeSnapshot cleared = Committed(await workspace.ClearScopeAsync(
            replaced.Revision, Deadline, TestContext.Current.CancellationToken)).Snapshot;
        Assert.Empty(cleared.Packages);
        Assert.Same(committed.Revision, RegistrationCurrent(workspace));
    }

    [Fact]
    public async Task RegistrationChangesKeepAdmittedContentAndObserveClose()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot packages = await Replace(workspace, Binding("Retained.Package"));
        WorkspaceRegistrationRevision initial = RegistrationCurrent(workspace);
        using InspectionWorkspace.ArtifactRootQueryLease query = ArtifactAvailable(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity,
                packages.Packages[0].Occurrence.Correspondence, Ready(packages.Packages[0]),
                cancellationToken: TestContext.Current.CancellationToken));
        var changed = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(initial,
                [new WorkspaceRegistration.PackagePrefix(new("Microsoft.Extensions."))]));
        var cleared = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(changed.Revision, []));
        Assert.Same(packages, await Current(workspace));

        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
        try
        {
            Assert.False(close.IsCompleted);
            var read = Assert.IsType<WorkspaceRegistrationReadResult.Unavailable>(
                workspace.GetRegistrationSnapshot());
            var mutation = Assert.IsType<WorkspaceRegistrationOperationResult.Unavailable>(
                workspace.ReplaceRegistrations(null!, default));
            Assert.Equal(ArtifactRootFailure.WorkspaceClosing, read.RuntimeFailure);
            Assert.Equal(read.RuntimeFailure, mutation.RuntimeFailure);
            Assert.Same(cleared.Revision, read.LastRevision);
            Assert.Same(cleared.Revision, mutation.LastRevision);
        }
        finally { query.Dispose(); }
        await close;
    }

    [Fact]
    public async Task RegistrationCountIsIndependentOfPackageCapacity()
    {
        ImmutableArray<WorkspaceRegistration> registrations =
            [.. Enumerable.Range(0, 65).Select(index =>
                (WorkspaceRegistration)new WorkspaceRegistration.PackagePrefix(new($"Example.P{index}.")))];
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous(registrations);
        WorkspaceScopeSnapshot packages = await Current(workspace);
        Assert.Equal(65, RegistrationCurrent(workspace).Registrations.Length);
        Assert.Equal(64, packages.Revision.Limits.MaxPackages);
        Assert.Empty(packages.Packages);
        Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.ReplaceRegistrations(RegistrationCurrent(workspace), []));
        Assert.Same(packages, await Current(workspace));
    }

    static WorkspaceRegistrationRevision RegistrationCurrent(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;
}
