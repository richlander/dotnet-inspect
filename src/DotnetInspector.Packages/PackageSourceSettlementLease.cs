using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// The package-source-owned service that issues bounded settlement leases.
/// </summary>
public static class PackageSourceSettlementService
{
    /// <summary>
    /// Issues one source-settlement lease over caller-owned source clients and
    /// an optional lower-owner operation-context factory.
    /// </summary>
    public static PackageSourceSettlementLease IssueLease(
        Func<ConfiguredPackageAuthority, IPackageSourceClient> getClient,
        Func<CancellationToken, NuGetOperationContext>?
            createOperationContext = null)
    {
        ArgumentNullException.ThrowIfNull(getClient);
        return new PackageSourceSettlementLease(
            getClient,
            createOperationContext
                ?? (cancellationToken =>
                    new NuGetOperationContext(cancellationToken)));
    }
}

/// <summary>
/// An owner-issued package-source settlement capability over caller-owned
/// source clients.
/// </summary>
/// <remarks>
/// Retiring the lease rejects new settlement but does not dispose source
/// clients, operation contexts, payload streams, stores, or Workspace
/// participants owned by adjacent layers.
/// </remarks>
public sealed class PackageSourceSettlementLease : IDisposable
{
    private readonly Func<
        ConfiguredPackageAuthority,
        IPackageSourceClient> _getClient;
    private readonly Func<
        CancellationToken,
        NuGetOperationContext> _createOperationContext;
    private readonly PackageAcquisitionCandidateIssuer _candidateIssuer =
        new();
    private readonly PackageAcquisitionCandidateManifestAcquirer
        _manifestAcquirer;
    private int _retired;

    internal PackageSourceSettlementLease(
        Func<ConfiguredPackageAuthority, IPackageSourceClient> getClient,
        Func<CancellationToken, NuGetOperationContext>
            createOperationContext)
    {
        _getClient = getClient;
        _createOperationContext = createOperationContext;
        _manifestAcquirer = new(
            _candidateIssuer,
            GetClient);
    }

    internal object CandidateIssuerIdentity =>
        _candidateIssuer.Identity;

    internal PackageAcquisitionCandidate CreatePinnedCandidate(
        PackageSourceCoordinate coordinate,
        IReadOnlyList<ConfiguredPackageAuthority> authorities)
    {
        ThrowIfRetired();
        return PackageAcquisitionCandidate.CreatePinned(
            CandidateIssuerIdentity,
            coordinate,
            authorities);
    }

    internal bool OwnsCandidate(
        PackageAcquisitionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return _candidateIssuer.OwnsCandidate(candidate);
    }

    /// <summary>
    /// Settles one caller-pinned coordinate against an already-authorized
    /// source set.
    /// </summary>
    public PackageAcquisitionCandidateResult ResolvePinnedCandidate(
        PackageSourceAuthorization authorization,
        PackageSourceCoordinate coordinate)
    {
        ThrowIfRetired();
        return _candidateIssuer.ResolvePinnedCandidate(
            authorization,
            coordinate);
    }

    /// <summary>
    /// Authorizes and settles one caller-pinned coordinate within one shared
    /// package-source operation.
    /// </summary>
    public ValueTask<PackageAcquisitionCandidateResult>
        ResolvePinnedCandidateAsync(
        IPackageSourceAuthorization sourceAuthorization,
        PackageSourceCoordinate coordinate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ThrowIfRetired();
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        ArgumentNullException.ThrowIfNull(coordinate);
        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? CreateOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = operation.ResolveInvocationToken(
            cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation.ThrowIfExpired();
            PackageSourceAuthorization authorization =
                sourceAuthorization.AuthorizeSourcesFor(
                    coordinate.PackageId);
            cancellationToken.ThrowIfCancellationRequested();
            operation.ThrowIfExpired();
            PackageAcquisitionCandidateResult result =
                ResolvePinnedCandidate(
                    authorization,
                    coordinate);
            cancellationToken.ThrowIfCancellationRequested();
            operation.ThrowIfExpired();
            return ValueTask.FromResult(result);
        }
        catch (NuGetOperationTimeoutException)
        {
            return ValueTask.FromResult(
                _candidateIssuer.CreateIncompletePinnedCandidate(
                    [
                        new PackageAuthorityFailure(
                            InertString.Empty,
                            PackageAuthorityFailureKind.Timeout,
                            "The package candidate operation deadline expired before authorization completed.")
                        {
                            Timeout = new(
                                PackageSourceTimeoutKind.Operation,
                                operation.OperationTimeout),
                        },
                    ]));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    /// <summary>
    /// Authorizes and settles complete dependency-version discovery within one
    /// shared package-source operation.
    /// </summary>
    public async Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsAsync(
        string packageId,
        IPackageSourceAuthorization sourceAuthorization,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ThrowIfRetired();
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? CreateOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = operation.ResolveInvocationToken(
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        PackageSourceAuthorization authorization =
            sourceAuthorization.AuthorizeSourcesFor(packageId);
        return await DiscoverDependencyVersionsCoreAsync(
            packageId,
            authorization,
            cancellationToken,
            operation).ConfigureAwait(false);
    }

    /// <summary>
    /// Settles complete dependency-version discovery across every authorized
    /// authority.
    /// </summary>
    public async Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ThrowIfRetired();
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? CreateOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = operation.ResolveInvocationToken(
            cancellationToken);
        return await DiscoverDependencyVersionsCoreAsync(
            packageId,
            authorization,
            cancellationToken,
            operation).ConfigureAwait(false);
    }

    private async Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsCoreAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        CancellationToken cancellationToken,
        NuGetOperationContext operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outcomes = new List<
            PackageSourceOperationResult<PackageVersionResult>>(
                authorization.Authorities.Count);
        for (int index = 0;
             index < authorization.Authorities.Count;
             index++)
        {
            ConfiguredPackageAuthority authority =
                authorization.Authorities[index];
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.ThrowIfExpired();
            }
            catch (NuGetOperationTimeoutException)
            {
                return _candidateIssuer
                    .CreateIncompleteDependencyVersionDiscovery(
                        packageId,
                        authorization,
                        outcomes,
                        OperationTimeoutFailures(
                            authorization,
                            index,
                            operation));
            }

            IPackageSourceClient client = GetClient(authority);
            RequireAuthority(client.Source, authority);

            PackageSourceOperationResult<PackageVersionResult> outcome;
            try
            {
                outcome = await client.GetVersionsAsync(
                    packageId,
                    cancellationToken,
                    operation).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                operation.ThrowIfExpired();
            }
            catch (NuGetOperationTimeoutException)
            {
                return _candidateIssuer
                    .CreateIncompleteDependencyVersionDiscovery(
                        packageId,
                        authorization,
                        outcomes,
                        OperationTimeoutFailures(
                            authorization,
                            index,
                            operation));
            }
            PackageSourceResultIdentity resultSource = outcome.Failure?.Source
                ?? outcome.Value?.Source
                ?? throw new InvalidOperationException(
                    "The package source version operation returned neither a value nor a failure.");
            RequireAuthority(
                resultSource,
                authority,
                client.Source);
            outcomes.Add(outcome);
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            operation.ThrowIfExpired();
        }
        catch (NuGetOperationTimeoutException)
        {
            return _candidateIssuer.CreateIncompleteDependencyVersionDiscovery(
                packageId,
                authorization,
                outcomes,
                [
                    OperationTimeoutFailure(
                        authority: null,
                        operation),
                ]);
        }

        PackageVersionDiscoveryResult discovery =
            _candidateIssuer.CreateDependencyVersionDiscovery(
                packageId,
                authorization,
                outcomes);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            operation.ThrowIfExpired();
        }
        catch (NuGetOperationTimeoutException)
        {
            return _candidateIssuer.CreateIncompleteDependencyVersionDiscovery(
                discovery,
                [
                    OperationTimeoutFailure(
                        authority: null,
                        operation),
                ]);
        }

        return discovery;
    }

    /// <summary>
    /// Acquires one exact manifest through a candidate issued by this lease.
    /// </summary>
    public async Task<ConfiguredPackageManifestResult>
        AcquireCandidateManifestAsync(
        PackageAcquisitionCandidate candidate,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ThrowIfRetired();
        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? CreateOperationContext(cancellationToken)
                : null;
        return await _manifestAcquirer.AcquireAsync(
            candidate,
            cancellationToken,
            operationContext ?? ownedOperation).ConfigureAwait(false);
    }

    public void Dispose() =>
        Interlocked.Exchange(ref _retired, 1);

    private IPackageSourceClient GetClient(
        ConfiguredPackageAuthority authority)
    {
        ThrowIfRetired();
        IPackageSourceClient client = _getClient(authority)
            ?? throw new InvalidOperationException(
                "The package source client capability returned null.");
        return client;
    }

    private NuGetOperationContext CreateOperationContext(
        CancellationToken cancellationToken) =>
        _createOperationContext(cancellationToken)
            ?? throw new InvalidOperationException(
                "The package operation-context capability returned null.");

    private void ThrowIfRetired() =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _retired) != 0,
            this);

    private static void RequireAuthority(
        PackageSourceResultIdentity result,
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity? expectedSource = null)
    {
        if (!ReferenceEquals(
                result.Association,
                authority.Association)
            || (expectedSource is not null
                && !ReferenceEquals(result, expectedSource)))
        {
            throw new InvalidOperationException(
                "The package source result belongs to another configured authority or client.");
        }
    }

    private static IReadOnlyList<PackageAuthorityFailure>
        OperationTimeoutFailures(
            PackageSourceAuthorization authorization,
            int firstUnsettledAuthority,
            NuGetOperationContext operation)
    {
        var failures = new List<PackageAuthorityFailure>(
            authorization.Authorities.Count - firstUnsettledAuthority);
        for (int index = firstUnsettledAuthority;
             index < authorization.Authorities.Count;
             index++)
        {
            failures.Add(OperationTimeoutFailure(
                authorization.Authorities[index],
                operation));
        }

        return failures;
    }

    private static PackageAuthorityFailure OperationTimeoutFailure(
        ConfiguredPackageAuthority? authority,
        NuGetOperationContext operation) =>
        new(
            authority is null
                ? InertString.Empty
                : PackageSourceDisplay.ForDiagnostics(authority.Source),
            PackageAuthorityFailureKind.Timeout,
            authority is null
                ? "The package candidate operation deadline expired before discovery could be published."
                : "An authorized package source was not settled before the package candidate operation deadline.")
        {
            Timeout = new(
                PackageSourceTimeoutKind.Operation,
                operation.OperationTimeout),
        };
}
