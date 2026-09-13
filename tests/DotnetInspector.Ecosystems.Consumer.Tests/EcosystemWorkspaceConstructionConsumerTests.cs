using DotnetInspector.Queries;

namespace DotnetInspector.Ecosystems.Consumer.Tests;

public sealed class EcosystemWorkspaceConstructionConsumerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PublicFactoriesRetainExactDeclarationsInProductOrder(
        bool platformOnly, bool asynchronous)
    {
        await using InspectionWorkspace workspace = Create(platformOnly, asynchronous);
        WorkspaceRegistrationRevision revision = Read(workspace);
        EcosystemPackId[] expected = platformOnly
            ? [EcosystemPackIds.Platform, EcosystemPackIds.AspNetCore, EcosystemPackIds.MicrosoftExtensions]
            : [EcosystemPackIds.Platform, EcosystemPackIds.AspNetCore,
                EcosystemPackIds.MicrosoftExtensions, EcosystemPackIds.Aspire];

        Assert.Same(workspace.Identity, revision.Workspace);
        WorkspaceEcosystemRegistrationDeclaration[] declarations =
            [.. revision.Registrations.Select(item => Assert.IsType<WorkspaceRegistration.Ecosystem>(item).Declaration)];
        Assert.Equal(expected.Select(id => id.Value), declarations.Select(item => item.Id.Value));
        for (int index = 0; index < expected.Length; index++)
        {
            var selected = Assert.IsType<EcosystemWorkspaceRegistrationSelectionResult.Known>(
                EcosystemPackCatalog.SelectWorkspaceRegistration(expected[index]));
            Assert.Same(selected.Declaration, declarations[index]);
        }

        if (asynchronous)
        {
            Assert.Throws<InvalidOperationException>(() => workspace.Dispose());
            Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
            Assert.Same(close, workspace.CloseAsync());
            await close;
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => { _ = workspace.CloseAsync(); });
            workspace.Dispose();
        }

        var closed = Assert.IsType<WorkspaceRegistrationReadResult.Unavailable>(
            workspace.GetRegistrationSnapshot());
        Assert.Same(revision, closed.LastRevision);
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, closed.RuntimeFailure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConstructionIsIndependentAndDoesNotAdmitPackages(bool platformOnly)
    {
        await using InspectionWorkspace first = Create(platformOnly, true);
        await using InspectionWorkspace second = Create(platformOnly, true);
        await using InspectionWorkspace raw = InspectionWorkspace.CreateAsynchronous();
        WorkspaceRegistrationRevision initial = Read(first);
        WorkspaceRegistrationRevision other = Read(second);
        var scope = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await first.GetScopeSnapshotAsync()).Snapshot;

        Assert.NotSame(initial.Workspace, other.Workspace);
        Assert.NotSame(initial.Identity, other.Identity);
        Assert.Equal(initial.Registrations, other.Registrations);
        Assert.Empty(scope.Packages);
        Assert.Equal(64, scope.Revision.Limits.MaxPackages);
        Assert.Empty(Read(raw).Registrations);
        Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            first.ReplaceRegistrations(initial, []));
        Assert.Same(other, Read(second));
        Assert.Same(scope, Assert.IsType<WorkspaceScopeReadResult.Available>(
            await first.GetScopeSnapshotAsync()).Snapshot);
        await first.CloseAsync();
        Assert.Same(other, Read(second));
    }

    private static InspectionWorkspace Create(bool platformOnly, bool asynchronous) =>
        (platformOnly, asynchronous) switch
        {
            (true, false) => EcosystemPackCatalog.CreatePlatformWorkspace(),
            (true, true) => EcosystemPackCatalog.CreatePlatformWorkspaceAsynchronous(),
            (false, false) => EcosystemPackCatalog.CreateWorkspace(),
            (false, true) => EcosystemPackCatalog.CreateWorkspaceAsynchronous(),
        };

    private static WorkspaceRegistrationRevision Read(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(workspace.GetRegistrationSnapshot()).Revision;
}
