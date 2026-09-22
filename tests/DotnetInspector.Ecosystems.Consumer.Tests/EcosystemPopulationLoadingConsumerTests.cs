using System.Collections.Immutable;
using DotnetInspector.EcosystemLoading;
using DotnetInspector.Ecosystems;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;

namespace DotnetInspector.Ecosystems.Consumer.Tests;

public sealed class EcosystemPopulationLoadingConsumerTests
{
    [Fact]
    public async Task PublicCatalogSelectsLoaderForExactWorkspaceRegistration()
    {
        WorkspacePlan plan =
            EcosystemPackCatalog.CreateWorkspacePlan(
                [EcosystemPackIds.Runtime]);
        await using var workspace = new InspectionWorkspace(plan);
        WorkspaceRegistrationRevision revision =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                workspace.GetRegistrationSnapshot()).Revision;
        WorkspaceEcosystemRegistrationDeclaration registration =
            Assert.IsType<WorkspaceRegistration.Ecosystem>(
                Assert.Single(revision.Registrations)).Declaration;

        var known =
            Assert.IsAssignableFrom<EcosystemPopulationLoaderSelection.Known>(
                EcosystemPackCatalog.SelectPopulationLoader(
                    revision,
                    registration,
                    EcosystemPopulationDemand.WholePopulation.Instance));

        Assert.Equal("ecosystem-loader.runtime", known.Binding.Id.Value);
        Assert.Same(registration, known.Registration);
        Assert.Same(revision, known.Revision);
    }

    [Fact]
    public async Task PublicRuntimeLoaderReturnsUnavailableWithoutCapability()
    {
        (WorkspaceRegistrationRevision revision,
            WorkspaceEcosystemRegistrationDeclaration registration,
            InspectionWorkspace workspace) =
                await ProductWorkspaceAsync(EcosystemPackIds.Runtime);
        await using (workspace)
        {
            var known = Assert.IsType<
                EcosystemPopulationLoaderSelection.Known<
                    RuntimeEcosystemPopulationLoadInputs>>(
                    EcosystemPackCatalog.SelectPopulationLoader(
                        revision,
                        registration,
                        EcosystemPopulationDemand
                            .WholePopulation.Instance));
            var inputs = new RuntimeEcosystemPopulationLoadInputs(
                EcosystemPopulationOperationPolicyIdentity.Create(
                    "consumer-runtime-policy"),
                EcosystemPopulationCapabilityPlanIdentity.Create(
                    "consumer-runtime-capabilities"),
                EcosystemPopulationWorkIdentity.Create(
                    "consumer-runtime-work"),
                platformCapability: null);

            var outcome =
                Assert.IsType<EcosystemPopulationLoadOutcome.Unavailable>(
                    await EcosystemPopulationLoadOperation.InvokeAsync(
                        known.CreateRequest(
                            inputs,
                            TestContext.Current.CancellationToken)));

            Assert.Equal(
                PlatformFamily.DotNetRuntime,
                inputs.PlatformDeclaration.Family);
            Assert.Empty(outcome.Receipt.Children);
            Assert.Equal(
                "ecosystem-loader.platform-capability-unavailable",
                Assert.Single(outcome.Receipt.Diagnostics).Code);
        }
    }

    [Fact]
    public async Task PublicAspNetCoreLoaderReturnsUnavailableWithoutCapability()
    {
        (WorkspaceRegistrationRevision revision,
            WorkspaceEcosystemRegistrationDeclaration registration,
            InspectionWorkspace workspace) =
                await ProductWorkspaceAsync(EcosystemPackIds.AspNetCore);
        await using (workspace)
        {
            var known = Assert.IsType<
                EcosystemPopulationLoaderSelection.Known<
                    AspNetCoreEcosystemPopulationLoadInputs>>(
                    EcosystemPackCatalog.SelectPopulationLoader(
                        revision,
                        registration,
                        EcosystemPopulationDemand
                            .WholePopulation.Instance));
            var inputs = new AspNetCoreEcosystemPopulationLoadInputs(
                EcosystemPopulationOperationPolicyIdentity.Create(
                    "consumer-aspnetcore-policy"),
                EcosystemPopulationCapabilityPlanIdentity.Create(
                    "consumer-aspnetcore-capabilities"),
                EcosystemPopulationWorkIdentity.Create(
                    "consumer-aspnetcore-work"),
                platformCapability: null);

            var outcome =
                Assert.IsType<EcosystemPopulationLoadOutcome.Unavailable>(
                    await EcosystemPopulationLoadOperation.InvokeAsync(
                        known.CreateRequest(
                            inputs,
                            TestContext.Current.CancellationToken)));

            Assert.Equal(
                PlatformFamily.AspNetCore,
                inputs.PlatformDeclaration.Family);
            Assert.Empty(outcome.Receipt.Children);
            Assert.Equal(
                "ecosystem-loader.platform-capability-unavailable",
                Assert.Single(outcome.Receipt.Diagnostics).Code);
        }
    }

    [Fact]
    public async Task PublicSurfaceSelectsAndInvokesStaticLoader()
    {
        var declaration = new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create(
                "ecosystem.consumer"),
            ["Consumer"],
            [],
            []);
        await using var workspace = new InspectionWorkspace(
            new WorkspacePlan(
                ImmutableArray.Create<WorkspaceRegistration>(
                    new WorkspaceRegistration.Ecosystem(
                        declaration))));
        WorkspaceRegistrationRevision revision =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                workspace.GetRegistrationSnapshot()).Revision;
        EcosystemPopulationLoaderBinding<ConsumerInputs> binding =
            EcosystemPopulationLoaderBinding.Create<ConsumerInputs>(
                EcosystemPopulationLoaderId.Create(
                    "ecosystem-loader.consumer"),
                LoadAsync);
        var correspondence =
            new EcosystemPopulationLoaderCorrespondence<ConsumerInputs>(
                declaration,
                binding);
        var known =
            Assert.IsType<
                EcosystemPopulationLoaderSelection.Known<ConsumerInputs>>(
                    correspondence.Select(
                        revision,
                        declaration,
                        EcosystemPopulationDemand
                            .WholePopulation.Instance));
        var inputs = new ConsumerInputs();
        EcosystemPopulationLoadRequest<ConsumerInputs> request =
            known.CreateRequest(
                inputs,
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<ConsumerInputs> second =
            known.CreateRequest(
                new ConsumerInputs(),
                TestContext.Current.CancellationToken);
        EcosystemPopulationChildRequestIdentity childRequest =
            request.ChildRequest("consumer.child.request");
        EcosystemPopulationChildReceiptIdentity childReceipt =
            request.ChildReceipt(
                childRequest,
                "consumer.child.receipt");

        Assert.Throws<ArgumentException>(
            () => second.ChildIncomplete(
                childRequest,
                childReceipt));

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    request));

        Assert.Equal(1, inputs.InvocationCount);
        Assert.Same(declaration, outcome.Receipt.Request.Registration);
        Assert.Same(revision, outcome.Receipt.Request.RegistrationRevision);
        Assert.Same(binding.Id, outcome.Receipt.Request.Loader);
        Assert.Empty(outcome.Owners.Libraries);
        await outcome.Owners.DisposeAsync();
    }

    static ValueTask<EcosystemPopulationLoaderReply> LoadAsync(
        EcosystemPopulationLoadRequest<ConsumerInputs> request)
    {
        request.Inputs.InvocationCount++;
        return ValueTask.FromResult(
            request.Completed(
                request.Completion(
                    EcosystemPopulationCompletionIdentity.Create(
                        "consumer.no-members"),
                    EcosystemPopulationCompletionKind.NoMembers),
                []));
    }

    static Task<(
        WorkspaceRegistrationRevision Revision,
        WorkspaceEcosystemRegistrationDeclaration Registration,
        InspectionWorkspace Workspace)> ProductWorkspaceAsync(
            EcosystemPackId id)
    {
        var workspace = new InspectionWorkspace(
            EcosystemPackCatalog.CreateWorkspacePlan([id]));
        WorkspaceRegistrationRevision revision =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                workspace.GetRegistrationSnapshot()).Revision;
        WorkspaceEcosystemRegistrationDeclaration registration =
            Assert.IsType<WorkspaceRegistration.Ecosystem>(
                Assert.Single(revision.Registrations)).Declaration;
        return Task.FromResult((revision, registration, workspace));
    }

    sealed class ConsumerInputs : IEcosystemPopulationLoadInputs
    {
        public int InvocationCount { get; set; }

        public EcosystemPopulationLoadInputSnapshot Snapshot { get; } =
            new(
                EcosystemPopulationOperationPolicyIdentity.Create(
                    "consumer-policy"),
                EcosystemPopulationCapabilityPlanIdentity.Create(
                    "consumer-capabilities"),
                EcosystemPopulationWorkIdentity.Create(
                    "consumer-work"));
    }
}
