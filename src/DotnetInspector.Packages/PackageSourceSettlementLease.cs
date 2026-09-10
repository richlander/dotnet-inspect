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
        return await DiscoverVersionsCoreAsync(
            packageId,
            authorization,
            PackageVersionDiscoveryContract.DependencyRangeResolution,
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
        return await DiscoverVersionsCoreAsync(
            packageId,
            authorization,
            PackageVersionDiscoveryContract.DependencyRangeResolution,
            cancellationToken,
            operation).ConfigureAwait(false);
    }

    internal async Task<PackageVersionDiscoveryResult>
        DiscoverVersionsAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ThrowIfRetired();
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(contract);
        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? CreateOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = operation.ResolveInvocationToken(
            cancellationToken);
        return await DiscoverVersionsCoreAsync(
            packageId,
            authorization,
            contract,
            cancellationToken,
            operation).ConfigureAwait(false);
    }

    private async Task<PackageVersionDiscoveryResult>
        DiscoverVersionsCoreAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
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
                    .CreateIncompleteVersionDiscovery(
                        packageId,
                        authorization,
                        outcomes,
                        contract,
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
                    .CreateIncompleteVersionDiscovery(
                        packageId,
                        authorization,
                        outcomes,
                        contract,
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
        PackageVersionDiscoveryResult discovery =
            _candidateIssuer.CreateVersionDiscovery(
                packageId,
                authorization,
                outcomes,
                contract);
        try
        {
            operation.ThrowIfExpired();
        }
        catch (NuGetOperationTimeoutException)
        {
            return _candidateIssuer.CreateIncompleteVersionDiscovery(
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

    /// <summary>
    /// Acquires one exact payload through a candidate issued by this lease.
    /// The store factory returns caller-owned stores scoped to each candidate
    /// authority.
    /// </summary>
    public async Task<ConfiguredPackagePayloadResult>
        AcquireCandidatePayloadAsync(
        PackageAcquisitionCandidate candidate,
        Func<
            ConfiguredPackageAuthority,
            PackageProducerIdentity,
            IPackageStore> createStore,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null,
        Action<string>? log = null,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null,
        IReadOnlyList<PackageAuthorityFailure>? priorFailures = null,
        bool selectionUsesOriginalSources = false)
    {
        ThrowIfRetired();
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(createStore);
        if (!_candidateIssuer.OwnsCandidate(candidate))
        {
            throw new InvalidOperationException(
                "The package acquisition candidate belongs to another source-settlement lease.");
        }

        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? CreateOperationContext(cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = operation.ResolveInvocationToken(
            cancellationToken);
        var failures = new List<PackageAuthorityFailure>(
            priorFailures ?? []);
        var notFoundAuthorities =
            new List<ConfiguredPackageAuthority>();
        try
        {
            operation.ThrowIfExpired();
            var entries = new List<(
                ConfiguredPackageAuthority Authority,
                IPackageSourceClient Client,
                IPackageStore Store)>();
            foreach (PackageAcquisitionAuthorityEvidence evidence
                in candidate.Authorities
                    .OrderBy(item =>
                        item.Authority.Kind
                            == ConfiguredPackageAuthorityKind.LocalFolder
                            ? 0
                            : 1)
                    .ThenBy(
                        item => item.Authority.Source.Url,
                        StringComparer.Ordinal))
            {
                operation.ThrowIfExpired();
                ConfiguredPackageAuthority authority =
                    evidence.Authority;
                IPackageSourceClient client = GetClient(authority);
                RequireAuthority(
                    client.Source,
                    authority);
                IPackageStore store = createStore(
                    authority,
                    client.Source.Producer)
                    ?? throw new InvalidOperationException(
                        "The package store capability returned null.");
                entries.Add((authority, client, store));
            }

            ConfiguredPackageAuthority[]? reportingAuthorities =
                candidate.Kind
                    == PackageAcquisitionCandidateKind.Discovered
                    ? [.. entries.Select(item => item.Authority)]
                    : null;

            foreach (var entry in entries)
            {
                operation.ThrowIfExpired();
                AcquiredPackageSourcePayload? cached =
                    await PackagePayloadAcquisition.TryGetCachedAsync(
                        candidate.Coordinate,
                        entry.Client.Source.Producer.Key,
                        entry.Store,
                        limits,
                        log,
                        operation.OperationToken).ConfigureAwait(false);
                operation.ThrowIfExpired();
                if (cached is not null)
                {
                    return new(
                        entry.Authority,
                        entry.Client.Source,
                        cached,
                        failures,
                        reportingAuthorities:
                            reportingAuthorities,
                        selectionUsesOriginalSources:
                            selectionUsesOriginalSources);
                }
            }

            foreach (var entry in entries)
            {
                operation.ThrowIfExpired();
                log?.Invoke(
                    $"Acquiring {candidate.Coordinate.PackageId} {candidate.Coordinate.Version} from "
                    + $"{PackageSourceDisplay.ForDiagnostics(entry.Authority.Source)}.");
                try
                {
                    PackageSourcePayloadResult result =
                        await PackagePayloadAcquisition
                            .AcquireAuthorizedAsync(
                                entry.Client,
                                candidate.Coordinate,
                                entry.Store,
                                operation,
                                log,
                                limits,
                                transferPolicy).ConfigureAwait(false);
                    operation.ThrowIfExpired();
                    RequireAuthority(
                        entry.Client.Source,
                        entry.Authority);
                    if (result
                        is PackageSourcePayloadResult.Acquired acquired)
                    {
                        return new(
                            entry.Authority,
                            entry.Client.Source,
                            acquired.Payload,
                            failures,
                            notFoundAuthorities,
                            reportingAuthorities,
                            selectionUsesOriginalSources);
                    }
                    if (result
                        is PackageSourcePayloadResult.Failed failed)
                    {
                        RequireAuthority(
                            failed.Failure.Source,
                            entry.Authority,
                            entry.Client.Source);
                        failures.Add(DescribePayloadFailure(
                            entry.Authority.Source,
                            failed.Failure));
                    }
                    else if (result
                        is PackageSourcePayloadResult.Unavailable unavailable)
                    {
                        if (unavailable.IsNotFound)
                        {
                            notFoundAuthorities.Add(
                                entry.Authority);
                        }
                        else
                        {
                            failures.Add(
                                new PackageAuthorityFailure(
                                    PackageSourceDisplay.ForDiagnostics(
                                        entry.Authority.Source),
                                    PackageAuthorityFailureKind
                                        .ResponseRejected,
                                    "The selected source did not supply a payload satisfying the package policy.")
                                {
                                    ResultSource =
                                        entry.Client.Source,
                                });
                        }
                    }
                }
                catch (PackageSourceStreamException exception)
                {
                    RequireAuthority(
                        exception.ResultSource,
                        entry.Authority,
                        entry.Client.Source);
                    failures.Add(new PackageAuthorityFailure(
                        PackageSourceDisplay.ForDiagnostics(
                            entry.Authority.Source),
                        ClassifySourceFailure(exception.Kind),
                        exception.Message)
                    {
                        ResultSource = exception.ResultSource,
                        Timeout = exception.Timeout,
                    });
                    if (exception.Timeout?.Kind
                        == PackageSourceTimeoutKind.Operation)
                    {
                        return new(
                            authority: null,
                            source: null,
                            payload: null,
                            failures,
                            notFoundAuthorities);
                    }
                }
            }

            operation.ThrowIfExpired();
            return new(
                authority: null,
                source: null,
                payload: null,
                failures,
                notFoundAuthorities);
        }
        catch (NuGetOperationTimeoutException)
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty,
                PackageAuthorityFailureKind.Timeout,
                "The package payload operation deadline expired before acquisition completed.")
            {
                Timeout = new(
                    PackageSourceTimeoutKind.Operation,
                    operation.OperationTimeout),
            });
            return new(
                authority: null,
                source: null,
                payload: null,
                failures,
                notFoundAuthorities);
        }
        catch (OperationCanceledException)
            when (operation.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                operation.CancellationToken);
        }
        catch (OperationCanceledException)
            when (operation.OperationToken.IsCancellationRequested)
        {
            failures.Add(new PackageAuthorityFailure(
                InertString.Empty,
                PackageAuthorityFailureKind.Timeout,
                "The package payload operation deadline expired before acquisition completed.")
            {
                Timeout = new(
                    PackageSourceTimeoutKind.Operation,
                    operation.OperationTimeout),
            });
            return new(
                authority: null,
                source: null,
                payload: null,
                failures,
                notFoundAuthorities);
        }
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

    private static PackageAuthorityFailure DescribePayloadFailure(
        PackageSource source,
        PackageSourceFailure failure) =>
        new(
            PackageSourceDisplay.ForDiagnostics(source),
            ClassifySourceFailure(failure.Kind),
            failure.Message)
        {
            SourceFailure = failure,
            ResultSource = failure.Source,
        };

    private static PackageAuthorityFailureKind ClassifySourceFailure(
        PackageSourceFailureKind kind) =>
        kind switch
        {
            PackageSourceFailureKind.AuthenticationRequired =>
                PackageAuthorityFailureKind.AuthenticationRequired,
            PackageSourceFailureKind.Timeout =>
                PackageAuthorityFailureKind.Timeout,
            PackageSourceFailureKind.Unsupported =>
                PackageAuthorityFailureKind.Unsupported,
            PackageSourceFailureKind.InvalidResponse =>
                PackageAuthorityFailureKind.InvalidResponse,
            PackageSourceFailureKind.ResponseRejected =>
                PackageAuthorityFailureKind.ResponseRejected,
            PackageSourceFailureKind.Transport =>
                PackageAuthorityFailureKind.Transport,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
}
