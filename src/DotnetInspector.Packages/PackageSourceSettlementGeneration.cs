using System.Collections.Immutable;
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
            if (_operations == 0)
                throw new InvalidOperationException(
                    "Package Source operation registration cannot underflow.");
            _operations--;
            if (_operations == 0 && _settling)
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

    internal bool OwnsCandidate(PackageAcquisitionCandidate candidate) =>
        _candidateIssuer.OwnsCandidate(candidate);

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

    internal async Task<PackageAcquisitionPopulation>
        ResolvePinnedPopulationAsync(
        IPackageSourceAuthorization sourceAuthorization,
        IReadOnlyList<PackageSourceCoordinate> coordinates,
        NuGetOperationContext operationContext)
    {
        var candidates = new List<PackageAcquisitionCandidate>(
            coordinates.Count);
        var failures = new List<PackageAcquisitionPopulationFailure>();
        for (int index = 0; index < coordinates.Count; index++)
        {
            PackageSourceCoordinate coordinate = coordinates[index];
            PackageAcquisitionCandidateResult result =
                await ResolvePinnedCandidateAsync(
                    sourceAuthorization,
                    coordinate,
                    operationContext).ConfigureAwait(false);
            if (result.Candidate is { } candidate)
                candidates.Add(candidate);
            failures.AddRange(result.Failures.Select(failure =>
                PackageAcquisitionPopulationFailure.ForCandidate(
                    index + 1,
                    coordinate.PackageId,
                    coordinate,
                    failure)));
            if (result.Failures.Any(failure =>
                    IsTerminalOperationTimeout(failure)))
            {
                break;
            }
        }
        RetainOperationTimeoutIfExpired(
            failures,
            authority: null,
            operationContext);

        return new PackageAcquisitionPopulation(
            coordinates.Count,
            candidates,
            failures,
            PackageAcquisitionPopulationCompletionKind.ExactCoordinates);
    }

    internal async Task<PackageAcquisitionPopulation>
        ResolveGalleryExactPopulationAsync(
        string packageId,
        bool includePrerelease,
        PackageSourceAuthorization authorization,
        NuGetOperationContext operationContext)
    {
        ConfiguredPackageAuthority authority =
            authorization.Authorities[0];
        IPackageSourceClient client = GetClient(authority);
        RequireAuthority(client.Source, authority);
        if (client.Source.TransportKind
            != PackageSourceKind.NuGetGallery)
        {
            throw new InvalidOperationException(
                "Exact package population selection requires the NuGet Gallery client.");
        }

        PackageVersionDiscoveryResult discovery =
            await DiscoverVersionsAsync(
                packageId,
                authorization,
                PackageVersionDiscoveryContract.CompleteVersionEnumeration,
                operationContext).ConfigureAwait(false);
        PackageVersionSelectionRequest selection = includePrerelease
            ? new PackageVersionSelectionRequest.LatestPrerelease(packageId)
            : new PackageVersionSelectionRequest.LatestStable(packageId);
        PackageVersionResolutionReceipt resolution =
            PackageVersionSelectionResolver.Resolve(
                selection,
                discovery,
                PackageVersionDiscoveryFreshness.Current);
        ImmutableArray<PackageAcquisitionCandidate> candidates =
            resolution is PackageVersionResolutionReceipt.Resolved resolved
                ? [resolved.Candidate]
                : [];
        ImmutableArray<PackageAcquisitionPopulationFailure> failures =
        [
            .. discovery.Failures.Select(failure =>
                PackageAcquisitionPopulationFailure.ForSource(failure)),
        ];

        return new PackageAcquisitionPopulation(
            requestedCandidates: 1,
            candidates,
            failures,
            discovery.Failures.Count == 0
                ? PackageAcquisitionPopulationCompletionKind.ExactPackageComplete
                : PackageAcquisitionPopulationCompletionKind.SourceFailed);
    }

    internal async Task<PackageAcquisitionPopulation>
        ResolveGalleryPrefixPopulationAsync(
        string prefix,
        int maximumCandidates,
        bool includePrerelease,
        PackageSourceAuthorization authorization,
        NuGetOperationContext operationContext)
    {
        ConfiguredPackageAuthority authority =
            authorization.Authorities[0];
        IPackageSourceClient client = GetClient(authority);
        RequireAuthority(client.Source, authority);
        if (client.Source.TransportKind
            != PackageSourceKind.NuGetGallery)
        {
            throw new InvalidOperationException(
                "Package-prefix population selection requires the NuGet Gallery client.");
        }

        var packageIds = new List<string>(maximumCandidates);
        var seenPackageIds = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var failures = new List<PackageAcquisitionPopulationFailure>();
        PackageAcquisitionPopulationCompletionKind completion =
            PackageAcquisitionPopulationCompletionKind.PrefixExhausted;
        try
        {
            await foreach (
                PackageSourceOperationResult<PackageSearchResult> operation
                in client.SearchByPrefixPagesAsync(
                    prefix,
                    maximumCandidates,
                    includePrerelease,
                    operationContext.CancellationToken,
                    operationContext).ConfigureAwait(false))
            {
                if (operation.Failure is { } searchFailure)
                {
                    RequireAuthority(
                        searchFailure.Source,
                        authority,
                        client.Source);
                    failures.Add(
                        PackageAcquisitionPopulationFailure.ForSource(
                            PackageAuthorityFailureAdapter
                                .DescribeSearchFailure(
                                    authority.Source,
                                    searchFailure)));
                    completion =
                        PackageAcquisitionPopulationCompletionKind.SourceFailed;
                    break;
                }

                PackageSearchResult page = operation.Value
                    ?? throw new InvalidOperationException(
                        "The package source prefix search returned neither a value nor a failure.");
                RequireAuthority(page.Source, authority, client.Source);
                int remaining = maximumCandidates - packageIds.Count;
                if (page.Matches.Count > remaining)
                {
                    failures.Add(
                        PackageAcquisitionPopulationFailure.ForSource(
                            InvalidPrefixSearchFailure(
                                authority,
                                page.Source,
                                "The package source returned more prefix matches than requested.")));
                    packageIds.Clear();
                    completion =
                        PackageAcquisitionPopulationCompletionKind.SourceFailed;
                    break;
                }

                foreach (PackageSearchMatch match in page.Matches)
                {
                    PackageCandidateObservation observation =
                        match.Candidate;
                    RequireAuthority(
                        observation.Source,
                        authority,
                        client.Source);
                    string packageId =
                        observation.Coordinate.PackageId;
                    if (observation.DiscoveryContract
                            != PackageDiscoveryContract.KeywordSearch
                        || observation.ListingState
                            != PackageListingState.Listed
                        || !PackageExtractor.IsValidPackageId(packageId)
                        || !packageId.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase)
                        || !seenPackageIds.Add(packageId))
                    {
                        failures.Add(
                            PackageAcquisitionPopulationFailure.ForSource(
                                InvalidPrefixSearchFailure(
                                    authority,
                                    page.Source,
                                    "The package source returned invalid or duplicate prefix-search evidence.")));
                        packageIds.Clear();
                        completion =
                            PackageAcquisitionPopulationCompletionKind.SourceFailed;
                        break;
                    }

                    packageIds.Add(packageId);
                }

                if (completion
                    == PackageAcquisitionPopulationCompletionKind.SourceFailed)
                {
                    break;
                }

                if (page.TruncationReason
                        == PackageSearchTruncationReason.RequestedLimit
                    && packageIds.Count != maximumCandidates)
                {
                    failures.Add(
                        PackageAcquisitionPopulationFailure.ForSource(
                            InvalidPrefixSearchFailure(
                                authority,
                                page.Source,
                                "The package source reported candidate-limit completion before supplying the requested population.")));
                    packageIds.Clear();
                    completion =
                        PackageAcquisitionPopulationCompletionKind.SourceFailed;
                    break;
                }

                completion = page.TruncationReason switch
                {
                    PackageSearchTruncationReason.None =>
                        PackageAcquisitionPopulationCompletionKind.PrefixExhausted,
                    PackageSearchTruncationReason.RequestedLimit =>
                        PackageAcquisitionPopulationCompletionKind.CandidateLimitReached,
                    PackageSearchTruncationReason.SourcePageLimit =>
                        PackageAcquisitionPopulationCompletionKind.SourcePageLimitReached,
                    PackageSearchTruncationReason.ClientPageLimit =>
                        PackageAcquisitionPopulationCompletionKind.ClientPageLimitReached,
                    _ => throw new InvalidOperationException(
                        "Unknown package-prefix search completion."),
                };
                if (page.Truncated)
                    break;
            }
        }
        catch (NuGetOperationTimeoutException)
        {
            failures.Add(
                PackageAcquisitionPopulationFailure.ForSource(
                    OperationTimeoutFailure(
                        authority,
                        operationContext)));
            completion =
                PackageAcquisitionPopulationCompletionKind.SourceFailed;
        }
        catch (OperationCanceledException)
            when (operationContext.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(
                operationContext.CancellationToken);
        }

        var candidates = new List<PackageAcquisitionCandidate>(
            packageIds.Count);
        for (int index = 0; index < packageIds.Count; index++)
        {
            string packageId = packageIds[index];
            PackageVersionDiscoveryResult discovery =
                await DiscoverVersionsAsync(
                    packageId,
                    authorization,
                    PackageVersionDiscoveryContract.CompleteVersionEnumeration,
                    operationContext).ConfigureAwait(false);
            PackageVersionSelectionRequest selection = includePrerelease
                ? new PackageVersionSelectionRequest.LatestPrerelease(packageId)
                : new PackageVersionSelectionRequest.LatestStable(packageId);
            PackageVersionResolutionReceipt resolution =
                PackageVersionSelectionResolver.Resolve(
                    selection,
                    discovery,
                    PackageVersionDiscoveryFreshness.Current);
            if (resolution is PackageVersionResolutionReceipt.Resolved resolved)
            {
                candidates.Add(resolved.Candidate);
                continue;
            }

            failures.AddRange(discovery.Failures.Select(failure =>
                PackageAcquisitionPopulationFailure.ForCandidate(
                    index + 1,
                    packageId,
                    coordinate: null,
                    failure)));
            if (discovery.Failures.Count == 0)
            {
                failures.Add(
                    PackageAcquisitionPopulationFailure.ForCandidate(
                        index + 1,
                        packageId,
                        coordinate: null,
                        new PackageAuthorityFailure(
                            PackageSourceDisplay.ForDiagnostics(
                                authority.Source),
                            PackageAuthorityFailureKind.IncompleteMetadata,
                            $"Package source {PackageSourceDisplay.ForDiagnostics(authority.Source)} reported '{packageId}' in prefix search but did not provide an eligible listed version.")
                        {
                            ResultSource = client.Source,
                        }));
            }
            if (discovery.Failures.Any(failure =>
                    IsTerminalOperationTimeout(failure)))
            {
                break;
            }
        }
        RetainOperationTimeoutIfExpired(
            failures,
            authority,
            operationContext);

        return new PackageAcquisitionPopulation(
            maximumCandidates,
            candidates,
            failures,
            completion);
    }

    /// <summary>
    /// Authorizes and settles complete dependency-version discovery within one
    /// shared package-source operation.
    /// </summary>
    public Task<PackageVersionDiscoveryResult>
        DiscoverDependencyVersionsAsync(
        string packageId,
        IPackageSourceAuthorization sourceAuthorization,
        NuGetOperationContext operationContext) =>
        DiscoverVersionsAsync(
            packageId,
            sourceAuthorization,
            PackageVersionDiscoveryContract.DependencyRangeResolution,
            operationContext);

    internal Task<PackageVersionDiscoveryResult>
        DiscoverVersionsAsync(
        string packageId,
        IPackageSourceAuthorization sourceAuthorization,
        PackageVersionDiscoveryContract contract,
        NuGetOperationContext operationContext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        ArgumentNullException.ThrowIfNull(contract);
        CancellationToken cancellationToken = operationContext.CancellationToken;
        cancellationToken.ThrowIfCancellationRequested();
        PackageSourceAuthorization authorization =
            sourceAuthorization.AuthorizeSourcesFor(packageId);
        return DiscoverVersionsCoreAsync(
            packageId,
            authorization,
            contract,
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
        NuGetOperationContext operationContext,
        Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(contract);
        return DiscoverVersionsCoreAsync(
            packageId,
            authorization,
            contract,
            operationContext.CancellationToken,
            operationContext,
            log);
    }

    private async Task<PackageVersionDiscoveryResult>
        DiscoverVersionsCoreAsync(
        string packageId,
        PackageSourceAuthorization authorization,
        PackageVersionDiscoveryContract contract,
        CancellationToken cancellationToken,
        NuGetOperationContext operation,
        Action<string>? log = null)
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

            log?.Invoke(
                $"Fetching versions from {PackageSourceDisplay.ForDiagnostics(authority.Source)}.");
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
        IPackagePayloadTransferPolicy? transferPolicy = null,
        PackageEntrySelector? rangedSelection = null)
    {
        return _payloadAcquirer.AcquireAsync(
            candidate,
            createStore,
            log,
            limits,
            operationContext.CancellationToken,
            transferPolicy,
            operationContext,
            rangedSelection);
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

    private static PackageAuthorityFailure InvalidPrefixSearchFailure(
        ConfiguredPackageAuthority authority,
        PackageSourceResultIdentity resultSource,
        string message) =>
        new(
            PackageSourceDisplay.ForDiagnostics(authority.Source),
            PackageAuthorityFailureKind.InvalidResponse,
            message)
        {
            ResultSource = resultSource,
        };

    private static bool IsTerminalOperationTimeout(
        PackageAuthorityFailure failure) =>
        failure.Kind == PackageAuthorityFailureKind.Timeout
        && failure.Timeout?.Kind
            == PackageSourceTimeoutKind.Operation;

    private static void RetainOperationTimeoutIfExpired(
        List<PackageAcquisitionPopulationFailure> failures,
        ConfiguredPackageAuthority? authority,
        NuGetOperationContext operation)
    {
        if (failures.Any(failure =>
                IsTerminalOperationTimeout(failure.Failure)))
        {
            return;
        }

        try
        {
            operation.ThrowIfExpired();
        }
        catch (NuGetOperationTimeoutException)
        {
            failures.Add(
                PackageAcquisitionPopulationFailure.ForSource(
                OperationTimeoutFailure(authority, operation)));
        }
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
