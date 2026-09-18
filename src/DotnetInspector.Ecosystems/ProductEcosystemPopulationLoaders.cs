using DotnetInspector.EcosystemLoading;

namespace DotnetInspector.Ecosystems;

internal static class ProductEcosystemPopulationLoaders
{
    internal static EcosystemPopulationLoaderBinding Runtime { get; } =
        EcosystemPopulationLoaderBinding.Create<RuntimeInputs>(
            EcosystemPopulationLoaderId.Create("ecosystem-loader.runtime"),
            LoadRuntimeAsync);

    internal static EcosystemPopulationLoaderBinding AspNetCore { get; } =
        EcosystemPopulationLoaderBinding.Create<AspNetCoreInputs>(
            EcosystemPopulationLoaderId.Create("ecosystem-loader.aspnetcore"),
            LoadAspNetCoreAsync);

    private static ValueTask<EcosystemPopulationLoaderReply> LoadRuntimeAsync(
        EcosystemPopulationLoadRequest<RuntimeInputs> request) =>
        ValueTask.FromResult<EcosystemPopulationLoaderReply>(
            request.Failed(
                [],
                [
                    new(
                        "ecosystem-loader.adapter-not-composed",
                        "The .NET Runtime Ecosystem population loader is not yet composed with PlatformHouse."),
                ]));

    private static ValueTask<EcosystemPopulationLoaderReply> LoadAspNetCoreAsync(
        EcosystemPopulationLoadRequest<AspNetCoreInputs> request) =>
        ValueTask.FromResult<EcosystemPopulationLoaderReply>(
            request.Failed(
                [],
                [
                    new(
                        "ecosystem-loader.adapter-not-composed",
                        "The ASP.NET Core Ecosystem population loader is not yet composed with PlatformHouse."),
                ]));

    private sealed class RuntimeInputs : IEcosystemPopulationLoadInputs
    {
        public EcosystemPopulationLoadInputSnapshot Snapshot { get; } =
            PendingSnapshot("runtime");
    }

    private sealed class AspNetCoreInputs : IEcosystemPopulationLoadInputs
    {
        public EcosystemPopulationLoadInputSnapshot Snapshot { get; } =
            PendingSnapshot("aspnetcore");
    }

    private static EcosystemPopulationLoadInputSnapshot PendingSnapshot(
        string family) =>
        new(
            EcosystemPopulationOperationPolicyIdentity.Create(
                $"pending-{family}-operation-policy"),
            EcosystemPopulationCapabilityPlanIdentity.Create(
                $"pending-{family}-capability-plan"),
            EcosystemPopulationWorkIdentity.Create(
                $"pending-{family}-work"));
}
