using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

internal sealed class PackageSourceSettlementGeneration
{
    private readonly Func<
        ConfiguredPackageAuthority,
        IPackageSourceClient> _getClient;
    private readonly PackageAcquisitionCandidateIssuer _candidateIssuer =
        new();
    private readonly PackageAcquisitionCandidateManifestAcquirer
        _manifestAcquirer;
    private readonly PackageAcquisitionCandidatePayloadAcquirer
        _payloadAcquirer;
    private readonly object _gate = new();
    private readonly TaskCompletionSource _quiescence =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _settling;
    private int _operations;

    internal PackageSourceSettlementGeneration(
        Func<ConfiguredPackageAuthority, IPackageSourceClient> getClient)
    {
        _getClient = getClient;
        _manifestAcquirer = new(
            _candidateIssuer,
            GetClient);
        _payloadAcquirer = new(
            _candidateIssuer,
            GetClient);
    }

    internal object CandidateIssuerIdentity =>
        _candidateIssuer.Identity;

    internal PackageSourceSettlementAuthorization CreateAuthorization()
    {
        lock (_gate)
        {
            ThrowIfSettling();
            return new(this);
        }
    }

    internal void RegisterOperation()
    {
        lock (_gate)
        {
            ThrowIfSettling();
            _operations++;
        }
    }

    internal void ReleaseOperation()
    {
        lock (_gate)
        {
            if (--_operations == 0 && _settling)
                _quiescence.SetResult();
        }
    }

    internal ValueTask SettleAsync()
    {
        lock (_gate)
        {
            if (!_settling)
            {
                _settling = true;
                if (_operations == 0)
                    _quiescence.SetResult();
            }
            return new(_quiescence.Task);
        }
    }

    private void ThrowIfSettling() =>
        ObjectDisposedException.ThrowIf(
            _settling,
            typeof(PackageSourceSettlementLease));

    internal PackageAcquisitionCandidate CreatePinnedCandidate(
        PackageSourceCoordinate coordinate,
        IReadOnlyList<ConfiguredPackageAuthority> authorities)
    {
        return PackageAcquisitionCandidate.CreatePinned(
            CandidateIssuerIdentity,
            coordinate,
            authorities);
    }

    /// <summary>
    /// Settles one caller-pinned coordinate against an already-authorized
    /// source set.
    /// </summary>
    public PackageAcquisitionCandidateResult ResolvePinnedCandidate(
        PackageSourceAuthorization authorization,
        PackageSourceCoordinate coordinate)
    {
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
        NuGetOperationContext operationContext)
    {
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        ArgumentNullException.ThrowIfNull(coordinate);
        NuGetOperationContext operation = operationContext;
        CancellationToken cancellationToken = operation.CancellationToken;
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
    public Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsAsync(
        string packageId,
        IPackageSourceAuthorization sourceAuthorization,
        NuGetOperationContext operationContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        CancellationToken cancellationToken = operationContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        PackageSourceAuthorization authorization =
            sourceAuthorization.AuthorizeSourcesFor(packageId);
        return DiscoverVersionsCoreAsync(
            packageId,
            authorization,
            PackageVersionDiscoveryContract.DependencyRangeResolution,
            cancellationToken,
            operationContext);
    }

    /// <summary>
    /// Settles complete dependency-version discovery across every authorized
    /// authority.
    /// </summary>
    public Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        NuGetOperationContext operationContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        return DiscoverVersionsCoreAsync(
            packageId,
            authorization,
            PackageVersionDiscoveryContract.DependencyRangeResolution,
            operationContext.CancellationToken,
            operationContext);
    }

    internal Task<PackageVersionDiscoveryResult>
        DiscoverVersionsAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        NuGetOperationContext operationContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(contract);
        return DiscoverVersionsCoreAsync(
            packageId,
            authorization,
            contract,
            operationContext.CancellationToken,
            operationContext);
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
    /// Acquires one exact manifest through a candidate issued by this generation.
    /// </summary>
    public Task<ConfiguredPackageManifestResult>
        AcquireCandidateManifestAsync(
        PackageAcquisitionCandidate candidate,
        NuGetOperationContext operationContext)
    {
        return _manifestAcquirer.AcquireAsync(
            candidate,
            operationContext.CancellationToken,
            operationContext);
    }

    /// <summary>
    /// Acquires one exact admitted retained payload through a candidate issued
    /// by this generation.
    /// The store factory returns caller-owned stores scoped to each candidate
    /// authority.
    /// </summary>
    public Task<ConfiguredPackagePayloadResult>
        AcquireCandidatePayloadAsync(
        PackageAcquisitionCandidate candidate,
        Func<
            ConfiguredPackageAuthority,
            PackageProducerIdentity,
            IPackageStore> createStore,
        NuGetOperationContext operationContext,
        Action<string>? log = null,
        PackagePayloadLimits? limits = null,
        IPackagePayloadTransferPolicy? transferPolicy = null)
    {
        return _payloadAcquirer.AcquireAsync(
            candidate,
            createStore,
            log,
            limits,
            operationContext.CancellationToken,
            transferPolicy,
            operationContext);
    }

    internal Task<ConfiguredPackagePayloadResult>
        AcquireCandidatePayloadAsync(
        PackageAcquisitionCandidate candidate,
        Func<
            ConfiguredPackageAuthority,
            PackageProducerIdentity,
            IPackageStore> createStore,
        Action<string>? log,
        NuGetOperationContext operationContext,
        PackagePayloadLimits? limits,
        IPackagePayloadTransferPolicy? transferPolicy,
        List<PackageAuthorityFailure> failures,
        bool selectionUsesOriginalSources)
    {
        return _payloadAcquirer.AcquireAsync(
            candidate,
            createStore,
            log,
            limits,
            operationContext.CancellationToken,
            transferPolicy,
            operationContext,
            failures,
            selectionUsesOriginalSources);
    }

    private IPackageSourceClient GetClient(
        ConfiguredPackageAuthority authority)
    {
        IPackageSourceClient client = _getClient(authority)
            ?? throw new InvalidOperationException(
                "The package source client capability returned null.");
        return client;
    }

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
