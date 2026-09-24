using System.Collections.Immutable;

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

    /// <summary>Gets the per-request deadline carried by this operation.</summary>
    public TimeSpan RequestTimeout => _context.RequestTimeout;

    /// <summary>Gets the ceiling for this complete Package Source operation.</summary>
    public TimeSpan OperationTimeout => _context.OperationTimeout;

    /// <summary>Gets the caller cancellation carried by this operation.</summary>
    public CancellationToken CancellationToken => _context.CancellationToken;

    /// <summary>
    /// Gets the token that observes both caller cancellation and the operation
    /// ceiling.
    /// </summary>
    public CancellationToken OperationCancellationToken =>
        _context.OperationToken;

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

    internal bool OwnsCandidate(PackageAcquisitionCandidate candidate) =>
        _generation.OwnsCandidate(candidate);

    /// <summary>
    /// Validates that every candidate belongs to this operation's Package
    /// Source root generation.
    /// </summary>
    public void ValidateCandidateOwnership(
        ImmutableArray<PackageAcquisitionCandidate> candidates)
    {
        if (candidates.IsDefault
            || candidates.Any(static candidate => candidate is null))
        {
            throw new ArgumentException(
                "A candidate ownership check requires a non-default collection without null candidates.",
                nameof(candidates));
        }
        using ActiveWorkRegistration work = StartWork();
        if (candidates.Any(
                candidate => !work.Generation.OwnsCandidate(candidate)))
        {
            throw new InvalidOperationException(
                "One or more package acquisition candidates belong to another Package Source root generation.");
        }
    }

    /// <summary>
    /// Validates that every candidate in a frozen population belongs to this
    /// operation's package-source generation.
    /// </summary>
    public void ValidatePopulationOwnership(
        PackageAcquisitionPopulation population)
    {
        ArgumentNullException.ThrowIfNull(population);
        ValidateCandidateOwnership(population.Candidates);
    }

    public ValueTask<PackageAcquisitionCandidateResult> ResolvePinnedCandidateAsync(
        IPackageSourceAuthorization sourceAuthorization,
        PackageSourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        ArgumentNullException.ThrowIfNull(coordinate);
        return ResolvePinnedCoreAsync(StartWork(), sourceAuthorization, coordinate);
    }

    /// <summary>
    /// Freezes one to five caller-pinned coordinates as authority-bearing
    /// candidates in caller order.
    /// </summary>
    public Task<PackageAcquisitionPopulation> ResolvePinnedPopulationAsync(
        IPackageSourceAuthorization sourceAuthorization,
        IReadOnlyList<PackageSourceCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        ArgumentNullException.ThrowIfNull(coordinates);
        if (coordinates.Count is < 1
            or > PackageAcquisitionPopulation.MaximumCandidates)
        {
            throw new ArgumentException(
                $"A package population requires between 1 and {PackageAcquisitionPopulation.MaximumCandidates} exact coordinates.",
                nameof(coordinates));
        }

        PackageSourceCoordinate[] snapshot = [.. coordinates];
        if (snapshot.Any(static coordinate => coordinate is null))
        {
            throw new ArgumentException(
                "A package population cannot contain a null coordinate.",
                nameof(coordinates));
        }
        if (snapshot.Distinct().Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "A package population cannot contain duplicate coordinates.",
                nameof(coordinates));
        }

        return ResolvePinnedPopulationCoreAsync(
            StartWork(),
            sourceAuthorization,
            snapshot);
    }

    internal Task<PackageAcquisitionPopulation>
        ResolveGalleryExactPopulationAsync(
        string packageId,
        bool includePrerelease,
        PackageSourceAuthorization authorization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        if (authorization.Authorities.Count != 1
            || !authorization.Authorities[0].Key.IsNuGetOrg
            || authorization.Authorities[0].Source.Credential is not null)
        {
            throw new ArgumentException(
                "Exact package population selection requires the credential-free NuGet Gallery authority.",
                nameof(authorization));
        }

        return ResolveGalleryExactPopulationCoreAsync(
            StartWork(),
            packageId,
            includePrerelease,
            authorization);
    }

    internal Task<PackageAcquisitionPopulation>
        ResolveGalleryPrefixPopulationAsync(
        string prefix,
        int maximumCandidates,
        bool includePrerelease,
        PackageSourceAuthorization authorization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (maximumCandidates is < 1
            or > PackageAcquisitionPopulation.MaximumCandidates)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCandidates));
        }
        ArgumentNullException.ThrowIfNull(authorization);
        if (authorization.Authorities.Count != 1
            || !authorization.Authorities[0].Key.IsNuGetOrg
            || authorization.Authorities[0].Source.Credential is not null)
        {
            throw new ArgumentException(
                "Package-prefix population selection requires the credential-free NuGet Gallery authority.",
                nameof(authorization));
        }

        return ResolveGalleryPrefixPopulationCoreAsync(
            StartWork(),
            prefix,
            maximumCandidates,
            includePrerelease,
            authorization);
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
        PackageVersionDiscoveryContract contract,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(contract);
        return DiscoverCoreAsync(StartWork(), packageId, authorization, contract, log);
    }

    /// <summary>
    /// Discovers versions under a deadline no longer than
    /// <paramref name="bound"/>: the step's request and operation ceilings
    /// are each clipped to the bound while the caller's cancellation still
    /// applies. A refresh the Package Version Service can abandon for a
    /// retained prior uses this form; an expired bound surfaces as the
    /// ordinary timeout evidence, never as a longer wait.
    /// </summary>
    public Task<PackageVersionDiscoveryResult> DiscoverVersionsAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        TimeSpan bound,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bound, TimeSpan.Zero);
        return DiscoverBoundedCoreAsync(StartWork(), packageId, authorization, contract, bound, log);
    }

    public Task<ConfiguredPackageManifestResult> AcquireCandidateManifestAsync(
        PackageAcquisitionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return ManifestCoreAsync(StartWork(), candidate);
    }

    /// <param name="rangedRead">
    /// When supplied, a cache miss on an authority whose client can serve
    /// byte ranges is answered by reading the archive directory and only the
    /// entries this selector chooses, retained as
    /// <see cref="RangedPackageContent"/> with origin
    /// <see cref="PackagePayloadOrigin.Ranged"/>; a refusal falls back to the
    /// complete fetch on that authority.
    /// </param>
    public Task<ConfiguredPackagePayloadResult> AcquireCandidatePayloadAsync(
        PackageAcquisitionCandidate candidate,
        Func<ConfiguredPackageAuthority, PackageProducerIdentity, IPackageStore> createStore,
        Action<string>? log = null,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        PackageRangedRead? rangedRead = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(createStore);
        return PayloadCoreAsync(
            StartWork(), candidate, createStore, log, limits, transferPolicy, rangedRead);
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

    private static async Task<PackageAcquisitionPopulation>
        ResolvePinnedPopulationCoreAsync(
        ActiveWorkRegistration work,
        IPackageSourceAuthorization authorization,
        IReadOnlyList<PackageSourceCoordinate> coordinates)
    {
        using (work)
        {
            return await work.Generation.ResolvePinnedPopulationAsync(
                authorization,
                coordinates,
                work.Context).ConfigureAwait(false);
        }
    }

    private static async Task<PackageAcquisitionPopulation>
        ResolveGalleryExactPopulationCoreAsync(
        ActiveWorkRegistration work,
        string packageId,
        bool includePrerelease,
        PackageSourceAuthorization authorization)
    {
        using (work)
        {
            return await work.Generation.ResolveGalleryExactPopulationAsync(
                packageId,
                includePrerelease,
                authorization,
                work.Context).ConfigureAwait(false);
        }
    }

    private static async Task<PackageAcquisitionPopulation>
        ResolveGalleryPrefixPopulationCoreAsync(
        ActiveWorkRegistration work,
        string prefix,
        int maximumCandidates,
        bool includePrerelease,
        PackageSourceAuthorization authorization)
    {
        using (work)
        {
            return await work.Generation.ResolveGalleryPrefixPopulationAsync(
                prefix,
                maximumCandidates,
                includePrerelease,
                authorization,
                work.Context).ConfigureAwait(false);
        }
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
        PackageVersionDiscoveryContract contract,
        Action<string>? log)
    {
        using (work)
            return await work.Generation.DiscoverVersionsAsync(
                packageId, authorization, contract, operationContext: work.Context, log).ConfigureAwait(false);
    }

    private static async Task<PackageVersionDiscoveryResult> DiscoverBoundedCoreAsync(
        ActiveWorkRegistration work, string packageId, PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        TimeSpan bound,
        Action<string>? log)
    {
        using (work)
        {
            work.Context.ThrowIfExpired();
            // The bounded context shares the caller token (so caller
            // cancellation keeps its attribution) and clips both ceilings to
            // the bound. It does not observe the parent operation's remaining
            // time: a refresh may run up to the bound past it, and the House
            // re-checks the parent deadline after settlement so no served
            // prior outlives it.
            using var bounded = new NuGetOperationContext(
                Clip(work.Context.RequestTimeout, bound),
                Clip(work.Context.OperationTimeout, bound),
                work.Context.CancellationToken);
            return await work.Generation.DiscoverVersionsAsync(
                packageId, authorization, contract, operationContext: bounded, log).ConfigureAwait(false);
        }

        static TimeSpan Clip(TimeSpan ceiling, TimeSpan bound) =>
            ceiling < bound ? ceiling : bound;
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
        IPackagePayloadTransferPolicy? transferPolicy,
        PackageRangedRead? rangedRead)
    {
        using (work)
            return await work.Generation.AcquireCandidatePayloadAsync(
                candidate, createStore, work.Context, log, limits,
                transferPolicy, rangedRead).ConfigureAwait(false);
    }
}
