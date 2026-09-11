using Inspector.Resources;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// Owns one context and root registration for sequential Package Source steps.
/// Each step must settle before the next step or synchronous release.
/// </summary>
[ResourceOwnership]
public sealed class PackageSourceOperationLease : IDisposable
{
    private readonly object _gate = new();
    private readonly PackageSourceSettlementGeneration _generation;
    private readonly NuGetOperationContext _context;
    private WorkLease? _work;
    private bool _disposed;

    internal PackageSourceOperationLease(
        PackageSourceSettlementGeneration generation,
        NuGetOperationContext context)
    {
        _generation = generation;
        _context = context;
    }

    /// <summary>Checks the shared caller cancellation and operation ceiling.</summary>
    public void ThrowIfExpired()
    {
        using WorkLease work = StartWork();
        work.Context.ThrowIfExpired();
    }

    public PackageAcquisitionCandidateResult ResolvePinnedCandidate(
        PackageSourceAuthorization authorization,
        PackageSourceCoordinate coordinate)
    {
        using WorkLease work = StartWork();
        work.Context.ThrowIfExpired();
        return work.Generation.ResolvePinnedCandidate(authorization, coordinate);
    }

    public ValueTask<PackageAcquisitionCandidateResult> ResolvePinnedCandidateAsync(
        IPackageSourceAuthorization sourceAuthorization,
        PackageSourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        ArgumentNullException.ThrowIfNull(coordinate);
        return ResolvePinnedCoreAsync(StartWork(), sourceAuthorization, coordinate);
    }

    public Task<PackageVersionDiscoveryResult> DiscoverDependencyVersionsAsync(
        string packageId,
        IPackageSourceAuthorization sourceAuthorization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        return DiscoverAuthorizedCoreAsync(StartWork(), packageId, sourceAuthorization);
    }

    public Task<PackageVersionDiscoveryResult> DiscoverDependencyVersionsAsync(
        string packageId,
        PackageSourceAuthorization authorization) =>
        DiscoverVersionsAsync(
            packageId, authorization,
            PackageVersionDiscoveryContract.DependencyRangeResolution);

    public Task<PackageVersionDiscoveryResult> DiscoverVersionsAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(contract);
        return DiscoverCoreAsync(StartWork(), packageId, authorization, contract);
    }

    public Task<ConfiguredPackageManifestResult> AcquireCandidateManifestAsync(
        PackageAcquisitionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return ManifestCoreAsync(StartWork(), candidate);
    }

    public Task<ConfiguredPackagePayloadResult> AcquireCandidatePayloadAsync(
        PackageAcquisitionCandidate candidate,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        Action<string>? log = null,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(createStore);
        return PayloadCoreAsync(StartWork(), candidate, createStore, log, limits, transferPolicy);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_work is not null)
                throw new InvalidOperationException(
                    "The current Package Source step must settle before releasing its operation.");
            _disposed = true;
            try
            {
                _context.Dispose();
            }
            finally
            {
                _generation.ReleaseOperation();
            }
        }
    }

    private WorkLease StartWork()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_work is not null)
                throw new InvalidOperationException(
                    "Only one Package Source step may be active per operation.");
            return _work = new(this, _generation, _context);
        }
    }

    private void ReleaseWork(WorkLease work)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_work, work))
                throw new InvalidOperationException("The work child is not owned by this operation.");
            _work = null;
        }
    }

    internal Task<TResult> RunAsync<TResult>(
        Func<PackageSourceSettlementGeneration, NuGetOperationContext, Task<TResult>> action) =>
        RunCoreAsync(StartWork(), action);

    private static async Task<TResult> RunCoreAsync<TResult>(
        WorkLease work,
        Func<PackageSourceSettlementGeneration, NuGetOperationContext, Task<TResult>> action)
    {
        using (work)
            return await action(work.Generation, work.Context).ConfigureAwait(false);
    }

    [ResourceOwnership]
    private sealed class WorkLease(
        PackageSourceOperationLease parent,
        PackageSourceSettlementGeneration generation,
        NuGetOperationContext context) : IDisposable
    {
        private PackageSourceOperationLease? _parent = parent;
        internal PackageSourceSettlementGeneration Generation { get; } = generation;
        internal NuGetOperationContext Context { get; } = context;

        public void Dispose() =>
            Interlocked.Exchange(ref _parent, null)?.ReleaseWork(this);
    }

    private static async ValueTask<PackageAcquisitionCandidateResult> ResolvePinnedCoreAsync(
        WorkLease work,
        IPackageSourceAuthorization authorization,
        PackageSourceCoordinate coordinate)
    {
        using (work)
            return await work.Generation.ResolvePinnedCandidateAsync(
                authorization, coordinate, operationContext: work.Context).ConfigureAwait(false);
    }

    private static async Task<PackageVersionDiscoveryResult> DiscoverAuthorizedCoreAsync(
        WorkLease work, string packageId, IPackageSourceAuthorization authorization)
    {
        using (work)
            return await work.Generation.DiscoverDependencyVersionsAsync(
                packageId, authorization, operationContext: work.Context).ConfigureAwait(false);
    }

    private static async Task<PackageVersionDiscoveryResult> DiscoverCoreAsync(
        WorkLease work, string packageId, PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract)
    {
        using (work)
            return await work.Generation.DiscoverVersionsAsync(
                packageId, authorization, contract, operationContext: work.Context).ConfigureAwait(false);
    }

    private static async Task<ConfiguredPackageManifestResult> ManifestCoreAsync(
        WorkLease work, PackageAcquisitionCandidate candidate)
    {
        using (work)
            return await work.Generation.AcquireCandidateManifestAsync(
                candidate, operationContext: work.Context).ConfigureAwait(false);
    }

    private static async Task<ConfiguredPackagePayloadResult> PayloadCoreAsync(
        WorkLease work, PackageAcquisitionCandidate candidate,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        Action<string>? log, PackagePayloadLimits? limits,
        IPackagePayloadTransferPolicy? transferPolicy)
    {
        using (work)
            return await work.Generation.AcquireCandidatePayloadAsync(
                candidate, createStore, work.Context, log, limits,
                transferPolicy).ConfigureAwait(false);
    }
}
