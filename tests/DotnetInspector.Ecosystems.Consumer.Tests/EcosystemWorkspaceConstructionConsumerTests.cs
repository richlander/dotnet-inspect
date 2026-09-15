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
                EcosystemPackIds.AI, EcosystemPackIds.Azure,
                EcosystemPackIds.Blazor];

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

    [Fact]
    public void PublicSelectedPlanCanRegisterOnlyPlatform()
    {
        EcosystemPackId[] expected = [EcosystemPackIds.Platform];
        WorkspacePlan plan =
            EcosystemPackCatalog.CreateWorkspacePlan(expected);

        WorkspaceEcosystemRegistrationDeclaration[] declarations =
        [
            .. plan.Registrations.Select(item =>
                Assert.IsType<WorkspaceRegistration.Ecosystem>(item)
                    .Declaration),
        ];

        Assert.Equal(
            expected.Select(id => id.Value),
            declarations.Select(declaration => declaration.Id.Value));
        Assert.Single(declarations);
    }

    [Fact]
    public void AllKnownAzureRegistrationSeparatesConcreteRootsFromDiscoveryPrefixes()
    {
        WorkspaceEcosystemRegistrationDeclaration azure =
            EcosystemPackCatalog.CreateWorkspacePlan().Registrations
                .Select(item => Assert.IsType<WorkspaceRegistration.Ecosystem>(item).Declaration)
                .Single(declaration => declaration.Id.Value == EcosystemPackIds.Azure.Value);

        Assert.Equal(
            [
                "Microsoft.Extensions.Azure",
                "Azure.AI.OpenAI",
                "Microsoft.Azure.SignalR",
                "Aspire.Azure.AI.OpenAI",
                "Aspire.Hosting.Azure.SignalR",
                "Azure.Identity",
                "Azure.Security.KeyVault.Secrets",
                "Azure.Storage.Blobs",
                "Azure.Messaging.ServiceBus",
            ],
            azure.CorePackages.Select(package => package.PackageId));
        Assert.Equal(
            [
                "Azure.",
                "Microsoft.Azure.",
                "Microsoft.Extensions.Azure",
                "Aspire.Azure.",
                "Aspire.Hosting.Azure.",
            ],
            azure.Populations.Select(population =>
                Assert.IsType<
                    WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(
                        population).Prefix.Prefix));
    }

    [Fact]
    public void AllKnownBlazorRegistrationSeparatesConcreteRootsFromDiscoveryPrefixes()
    {
        WorkspaceEcosystemRegistrationDeclaration blazor =
            EcosystemPackCatalog.CreateWorkspacePlan().Registrations
                .Select(item => Assert.IsType<WorkspaceRegistration.Ecosystem>(item).Declaration)
                .Single(declaration => declaration.Id.Value == EcosystemPackIds.Blazor.Value);

        Assert.Equal(
            [
                "Microsoft.AspNetCore.Components.WebAssembly",
                "Microsoft.AspNetCore.Components.WebView.Maui",
                "Microsoft.AspNetCore.Components.QuickGrid.EntityFrameworkAdapter",
                "Microsoft.Authentication.WebAssembly.Msal",
            ],
            blazor.CorePackages.Select(package => package.PackageId));
        Assert.Equal(
            [
                "Microsoft.AspNetCore.Components",
                "Microsoft.Authentication.WebAssembly",
            ],
            blazor.Populations.Select(population =>
                Assert.IsType<
                    WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(
                        population).Prefix.Prefix));
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
