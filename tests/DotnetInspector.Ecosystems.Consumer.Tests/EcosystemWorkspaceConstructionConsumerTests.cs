using DotnetInspector.Queries;

namespace DotnetInspector.Ecosystems.Consumer.Tests;

public sealed class EcosystemWorkspaceConstructionConsumerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicPlanFactoriesRetainExactDeclarationsInProductOrder(bool platformOnly)
    {
        WorkspacePlan plan = Create(platformOnly);
        EcosystemPackId[] expected = platformOnly
            ? [EcosystemPackIds.Platform, EcosystemPackIds.AspNetCore, EcosystemPackIds.MicrosoftExtensions]
            : [EcosystemPackIds.Platform, EcosystemPackIds.AspNetCore,
                EcosystemPackIds.MicrosoftExtensions, EcosystemPackIds.Aspire,
                EcosystemPackIds.AI];

        WorkspaceEcosystemRegistrationDeclaration[] declarations =
            [.. plan.Registrations.Select(item => Assert.IsType<WorkspaceRegistration.Ecosystem>(item).Declaration)];
        Assert.Equal(expected.Select(id => id.Value), declarations.Select(item => item.Id.Value));
        for (int index = 0; index < expected.Length; index++)
        {
            var selected = Assert.IsType<EcosystemWorkspaceRegistrationSelectionResult.Known>(
                EcosystemPackCatalog.SelectWorkspaceRegistration(expected[index]));
            Assert.Same(selected.Declaration, declarations[index]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConstructionIsIndependentAndDoesNotAdmitPackages(bool platformOnly)
    {
        WorkspacePlan plan = Create(platformOnly);
        WorkspacePlan raw = new();
        await using InspectionWorkspace first = new(plan);
        await using InspectionWorkspace second = new(plan);
        WorkspaceRegistrationRevision initial = Read(first);
        WorkspaceRegistrationRevision other = Read(second);
        var scope = Assert.IsType<WorkspaceScopeReadResult.Available>(
            await first.GetScopeSnapshotAsync()).Snapshot;

        Assert.NotSame(initial.Workspace, other.Workspace);
        Assert.NotSame(initial.Identity, other.Identity);
        Assert.Same(plan, initial.Plan);
        Assert.Same(plan, other.Plan);
        Assert.Equal(initial.Registrations, other.Registrations);
        Assert.Empty(scope.Packages);
        Assert.Equal(64, scope.Revision.Limits.MaxPackages);
        Assert.Empty(raw.Registrations);
        Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            first.ReplaceRegistrations(initial, []));
        Assert.Same(other, Read(second));
        Assert.Same(scope, Assert.IsType<WorkspaceScopeReadResult.Available>(
            await first.GetScopeSnapshotAsync()).Snapshot);
        InspectionWorkspaceCloseReport report = await first.CloseAsync();
        Assert.Empty(report.Groups);
        Assert.Empty(report.ArtifactSessionCleanupFailures);
        Assert.Same(other, Read(second));
        Assert.Equal(other.Registrations, plan.Registrations);
        Assert.Equal(plan.Registrations, Create(platformOnly).Registrations);
        await using InspectionWorkspace reopened = new(plan);
        Assert.Same(plan, Read(reopened).Plan);
    }

    private static WorkspacePlan Create(bool platformOnly) =>
        platformOnly ? EcosystemPackCatalog.CreatePlatformWorkspacePlan() : EcosystemPackCatalog.CreateWorkspacePlan();

    private static WorkspaceRegistrationRevision Read(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(workspace.GetRegistrationSnapshot()).Revision;
}
