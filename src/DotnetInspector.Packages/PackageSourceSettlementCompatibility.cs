using Inspector.Resources;
using NuGetFetch;

namespace DotnetInspector.Packages;

// Calls without an external context own a fresh operation lease. The legacy
// context branch is not PackageHouse ownership adoption (#6622).
internal static class PackageSourceSettlementCompatibility
{
    internal static TResult Run<TResult>(
        PackageSourceSettlementAuthorization authorization,
        Func<PackageSourceSettlementGeneration, TResult> action)
    {
        using var work = new Work(authorization);
        return action(work.Generation);
    }

    internal static Task<TResult> RunAsync<TResult>(
        PackageSourceSettlementAuthorization authorization,
        CancellationToken cancellationToken,
        NuGetOperationContext? context,
        Func<PackageSourceSettlementGeneration, NuGetOperationContext, Task<TResult>> action,
        TimeSpan? requestTimeout = null,
        TimeSpan? operationTimeout = null) =>
        context is null
            ? RunOwnedAsync(
                PackageSourceSettlementService.IssueOperationLease(
                    authorization, cancellationToken, requestTimeout, operationTimeout),
                action)
            : RunCoreAsync(new Work(authorization), cancellationToken, context, action);

    private static async Task<TResult> RunOwnedAsync<TResult>(
        PackageSourceOperationLease operation,
        Func<PackageSourceSettlementGeneration, NuGetOperationContext, Task<TResult>> action)
    {
        using (operation)
            return await operation.RunAsync(action).ConfigureAwait(false);
    }

    private static async Task<TResult> RunCoreAsync<TResult>(
        Work work,
        CancellationToken cancellationToken,
        NuGetOperationContext context,
        Func<PackageSourceSettlementGeneration, NuGetOperationContext, Task<TResult>> action)
    {
        using (work)
        {
            _ = context.ResolveInvocationToken(cancellationToken);
            return await action(work.Generation, context).ConfigureAwait(false);
        }
    }

    [ResourceOwnership]
    private sealed class Work : IDisposable
    {
        private int _disposed;
        internal PackageSourceSettlementGeneration Generation { get; }

        internal Work(PackageSourceSettlementAuthorization authorization)
        {
            Generation = authorization.Generation;
            Generation.RegisterOperation();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Generation.ReleaseOperation();
        }
    }
}

public sealed partial class PackageSourceSettlementLease
{
    internal PackageAcquisitionCandidateResult ResolvePinnedCandidate(
        PackageSourceAuthorization authorization,
        PackageSourceCoordinate coordinate) =>
        PackageSourceSettlementCompatibility.Run(
            CreateAuthorization(),
            generation => generation.ResolvePinnedCandidate(authorization, coordinate));

    internal Task<PackageVersionDiscoveryResult> DiscoverVersionsAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        PackageSourceSettlementCompatibility.RunAsync(
            CreateAuthorization(), cancellationToken, operationContext,
            (generation, context) => generation.DiscoverVersionsAsync(
                packageId, authorization, contract, operationContext: context));

    internal Task<ConfiguredPackagePayloadResult> AcquireCandidatePayloadAsync(
        PackageAcquisitionCandidate candidate,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        Action<string>? log = null,
        PackagePayloadLimits? limits = null,
        CancellationToken cancellationToken = default,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        NuGetOperationContext? operationContext = null) =>
        PackageSourceSettlementCompatibility.RunAsync(
            CreateAuthorization(), cancellationToken, operationContext,
            (generation, context) => generation.AcquireCandidatePayloadAsync(
                candidate, createStore, context, log, limits,
                transferPolicy));
}
