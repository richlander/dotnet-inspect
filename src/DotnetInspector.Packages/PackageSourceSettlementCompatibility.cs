using NuGetFetch;

namespace DotnetInspector.Packages;

// Calls without an external context own a fresh operation lease. The legacy
// context branch is not PackageHouse ownership adoption (#6622).
internal static class PackageSourceSettlementCompatibility
{
    internal static Task<TResult> RunAsync<TResult>(
        PackageSourceSettlementLease root,
        CancellationToken cancellationToken,
        NuGetOperationContext? context,
        Func<PackageSourceSettlementGeneration, NuGetOperationContext, Task<TResult>> action,
        TimeSpan? requestTimeout = null,
        TimeSpan? operationTimeout = null) =>
        context is null
            ? RunOwnedAsync(
                root.IssueOperationLease(
                    cancellationToken,
                    requestTimeout,
                    operationTimeout),
                action)
            : RunCoreAsync(
                new OperationRegistration(root),
                cancellationToken,
                context,
                action);

    private static async Task<TResult> RunOwnedAsync<TResult>(
        PackageSourceOperationLease operation,
        Func<PackageSourceSettlementGeneration, NuGetOperationContext, Task<TResult>> action)
    {
        using (operation)
            return await operation.RunAsync(action).ConfigureAwait(false);
    }

    private static async Task<TResult> RunCoreAsync<TResult>(
        OperationRegistration work,
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

    private sealed class OperationRegistration : IDisposable
    {
        private int _disposed;
        internal PackageSourceSettlementGeneration Generation { get; }

        internal OperationRegistration(PackageSourceSettlementLease root)
        {
            Generation = root.RegisterCompatibilityOperation();
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
    internal Task<PackageVersionDiscoveryResult> DiscoverVersionsAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null) =>
        PackageSourceSettlementCompatibility.RunAsync(
            this, cancellationToken, operationContext,
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
            this, cancellationToken, operationContext,
            (generation, context) => generation.AcquireCandidatePayloadAsync(
                candidate, createStore, context, log, limits,
                transferPolicy));
}
