using DotnetInspector.Queries;

namespace DotnetInspector.Ecosystems.Consumer.Tests;

public sealed class EcosystemWorkspaceConstructionConsumerTests
{
    [Theory]
    [InlineData(nameof(EcosystemPackCatalog.CreateWorkspace), false)]
    [InlineData(nameof(EcosystemPackCatalog.CreateWorkspaceAsynchronous), false)]
    [InlineData(nameof(EcosystemPackCatalog.CreatePlatformWorkspace), true)]
    [InlineData(nameof(EcosystemPackCatalog.CreatePlatformWorkspaceAsynchronous), true)]
    public async Task PublicFactoriesRetainExactDeclarationsInProductOrder(
        string factory, bool platformOnly)
    {
        await using InspectionWorkspace workspace = factory switch
        {
            nameof(EcosystemPackCatalog.CreateWorkspace) => EcosystemPackCatalog.CreateWorkspace(),
            nameof(EcosystemPackCatalog.CreateWorkspaceAsynchronous) => EcosystemPackCatalog.CreateWorkspaceAsynchronous(),
            nameof(EcosystemPackCatalog.CreatePlatformWorkspace) => EcosystemPackCatalog.CreatePlatformWorkspace(),
            nameof(EcosystemPackCatalog.CreatePlatformWorkspaceAsynchronous) =>
                EcosystemPackCatalog.CreatePlatformWorkspaceAsynchronous(),
            _ => throw new ArgumentOutOfRangeException(nameof(factory)),
        };
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

        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
        Assert.Same(close, workspace.CloseAsync());
        InspectionWorkspaceCloseReport report = await close;
        Assert.Empty(report.Groups);
        Assert.Empty(report.ArtifactSessionCleanupFailures);

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
        await using InspectionWorkspace first = Create(platformOnly);
        await using InspectionWorkspace second = Create(platformOnly);
        await using InspectionWorkspace raw = new InspectionWorkspace();
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

    private static InspectionWorkspace Create(bool platformOnly) =>
        platformOnly ? EcosystemPackCatalog.CreatePlatformWorkspace() : EcosystemPackCatalog.CreateWorkspace();

    private static WorkspaceRegistrationRevision Read(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(workspace.GetRegistrationSnapshot()).Revision;
}
