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
    private ActiveWorkRegistration? _work;
    private bool _disposed;

    internal PackageSourceOperationLease(
        PackageSourceSettlementGeneration generation,
        NuGetOperationContext context)
    {
        _generation = generation;
        _context = context;
    }

    internal TimeSpan RequestTimeout => _context.RequestTimeout;

    internal TimeSpan OperationTimeout => _context.OperationTimeout;

    /// <summary>Checks the shared caller cancellation and operation ceiling.</summary>
    public void ThrowIfExpired()
    {
        using ActiveWorkRegistration work = StartWork();
        work.Context.ThrowIfExpired();
    }

    public PackageAcquisitionCandidateResult ResolvePinnedCandidate(
        PackageSourceAuthorization authorization,
        PackageSourceCoordinate coordinate)
    {
        using ActiveWorkRegistration work = StartWork();
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
        return DiscoverAuthorizedCoreAsync(
            StartWork(),
            packageId,
            sourceAuthorization,
            PackageVersionDiscoveryContract.DependencyRangeResolution);
    }

    public Task<PackageVersionDiscoveryResult> DiscoverVersionsAsync(
        string packageId,
        IPackageSourceAuthorization sourceAuthorization,
        PackageVersionDiscoveryContract contract)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        ArgumentNullException.ThrowIfNull(contract);
        return DiscoverAuthorizedCoreAsync(
            StartWork(),
            packageId,
            sourceAuthorization,
            contract);
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

    private ActiveWorkRegistration StartWork()
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

    private void ReleaseWork(ActiveWorkRegistration work)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_work, work))
                throw new InvalidOperationException(
                    "The active work registration is not owned by this operation.");
            _work = null;
        }
    }

    internal Task<TResult> RunAsync<TResult>(
        Func<PackageSourceSettlementGeneration, NuGetOperationContext, Task<TResult>> action) =>
        RunCoreAsync(StartWork(), action);

    private static async Task<TResult> RunCoreAsync<TResult>(
        ActiveWorkRegistration work,
        Func<PackageSourceSettlementGeneration, NuGetOperationContext, Task<TResult>> action)
    {
        using (work)
            return await action(work.Generation, work.Context).ConfigureAwait(false);
    }

    private sealed class ActiveWorkRegistration(
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
        ActiveWorkRegistration work,
        IPackageSourceAuthorization authorization,
        PackageSourceCoordinate coordinate)
    {
        using (work)
            return await work.Generation.ResolvePinnedCandidateAsync(
                authorization, coordinate, operationContext: work.Context).ConfigureAwait(false);
    }

    private static async Task<PackageVersionDiscoveryResult> DiscoverAuthorizedCoreAsync(
        ActiveWorkRegistration work,
        string packageId,
        IPackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract)
    {
        using (work)
            return await work.Generation.DiscoverVersionsAsync(
                packageId,
                authorization,
                contract,
                operationContext: work.Context).ConfigureAwait(false);
    }

    private static async Task<PackageVersionDiscoveryResult> DiscoverCoreAsync(
        ActiveWorkRegistration work, string packageId, PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract)
    {
        using (work)
            return await work.Generation.DiscoverVersionsAsync(
                packageId, authorization, contract, operationContext: work.Context).ConfigureAwait(false);
    }

    private static async Task<ConfiguredPackageManifestResult> ManifestCoreAsync(
        ActiveWorkRegistration work, PackageAcquisitionCandidate candidate)
    {
        using (work)
            return await work.Generation.AcquireCandidateManifestAsync(
                candidate, operationContext: work.Context).ConfigureAwait(false);
    }

    private static async Task<ConfiguredPackagePayloadResult> PayloadCoreAsync(
        ActiveWorkRegistration work, PackageAcquisitionCandidate candidate,
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
