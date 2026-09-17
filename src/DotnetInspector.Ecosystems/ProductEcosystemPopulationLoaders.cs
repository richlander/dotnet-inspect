using DotnetInspector.EcosystemLoading;

namespace DotnetInspector.Ecosystems;

internal static class ProductEcosystemPopulationLoaders
{
    internal static EcosystemPopulationLoaderBinding DotNet { get; } =
        EcosystemPopulationLoaderBinding.Create<DotNetInputs>(
            EcosystemPopulationLoaderId.Create("ecosystem-loader.dotnet"),
            LoadDotNetAsync);

    internal static EcosystemPopulationLoaderBinding AspNetCore { get; } =
        EcosystemPopulationLoaderBinding.Create<AspNetCoreInputs>(
            EcosystemPopulationLoaderId.Create("ecosystem-loader.aspnetcore"),
            LoadAspNetCoreAsync);

    private static ValueTask<EcosystemPopulationLoaderReply> LoadDotNetAsync(
        EcosystemPopulationLoadRequest<DotNetInputs> request) =>
        ValueTask.FromResult<EcosystemPopulationLoaderReply>(
            request.Failed(
                [],
                [
                    new(
                        "ecosystem-loader.adapter-not-composed",
                        "The .NET Ecosystem population loader is not yet composed with PlatformHouse."),
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

    private sealed class DotNetInputs : IEcosystemPopulationLoadInputs
    {
        public EcosystemPopulationLoadInputSnapshot Snapshot { get; } =
            PendingSnapshot("dotnet");
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
