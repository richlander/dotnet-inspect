using System.Collections.Immutable;
using DotnetInspector.EcosystemLoading;
using DotnetInspector.Queries;

namespace DotnetInspector.Ecosystems.Consumer.Tests;

public sealed class EcosystemPopulationLoadingConsumerTests
{
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

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    known.CreateRequest(
                        inputs,
                        TestContext.Current.CancellationToken)));

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
