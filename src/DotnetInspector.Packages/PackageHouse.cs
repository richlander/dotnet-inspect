using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// One completed House operation, separating resource-free and live-payload
/// results in the type system.
/// </summary>
public abstract class PackageHouseSettlement
{
    private PackageHouseSettlement(
        PackageHouseResult result,
        ConfiguredPackagePayloadResult? sourcePayloadResult,
        bool selectionUsesOriginalSources)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (sourcePayloadResult?.Payload is not null
            && result.Evidence.Acquisition is null)
        {
            throw new ArgumentException(
                "A source payload result requires matching House acquisition evidence.",
                nameof(sourcePayloadResult));
        }

        Result = result;
        SourcePayloadResult = sourcePayloadResult;
        SelectionUsesOriginalSources = selectionUsesOriginalSources;
    }

    public PackageHouseResult Result { get; }

    internal ConfiguredPackagePayloadResult? SourcePayloadResult { get; }

    internal bool SelectionUsesOriginalSources { get; }

    public sealed class ResourceFree : PackageHouseSettlement
    {
        internal ResourceFree(PackageHouseResult result)
            : base(
                result,
                sourcePayloadResult: null,
                selectionUsesOriginalSources: false)
        {
        }

        internal ResourceFree(
            PackageHouseResult result,
            ConfiguredPackagePayloadResult sourcePayloadResult,
            bool selectionUsesOriginalSources)
            : base(
                result,
                sourcePayloadResult,
                selectionUsesOriginalSources)
        {
        }
    }

    public sealed class Acquired : PackageHouseSettlement
    {
        internal Acquired(
            PackageHouseResult result,
            AcquiredPackageSourcePayload payload,
            ConfiguredPackagePayloadResult sourcePayloadResult,
            bool selectionUsesOriginalSources)
            : base(
                result,
                sourcePayloadResult,
                selectionUsesOriginalSources)
        {
            ArgumentNullException.ThrowIfNull(payload);
            if (!ReferenceEquals(sourcePayloadResult.Payload, payload))
            {
                throw new ArgumentException(
                    "The source payload result must retain the live House payload.",
                    nameof(sourcePayloadResult));
            }
            PackageHouseAcquisitionReceipt acquisition =
                result.Evidence.Acquisition
                ?? throw new ArgumentException(
                    "An acquired House settlement requires its exact acquisition receipt.",
                    nameof(result));
            if (payload.Coordinate != acquisition.Candidate.Coordinate
                || !payload.ProducerKey.Equals(
                    acquisition.Producer.Key,
                    StringComparison.Ordinal)
                || payload.Producer != acquisition.Producer
                || payload.Origin != acquisition.Origin
                || !ReferenceEquals(
                    payload.Content.GenerationIdentity,
                    acquisition.Generation))
            {
                throw new ArgumentException(
                    "The live House payload must match its acquisition receipt exactly.",
                    nameof(payload));
            }

            Payload = payload;
        }

        public AcquiredPackageSourcePayload Payload { get; }

        /// <summary>
        /// Creates a cold, single-use read of one exact entry in this acquired
        /// package generation.
        /// </summary>
        public PackageHousePayloadRead OpenPayloadRead(
            string relativePath,
            long maxExpandedBytes) =>
            new(this, relativePath, maxExpandedBytes);
    }
}

/// <summary>
/// Host-neutral settlement of exact and selecting package requests.
/// </summary>
public sealed class PackageHouse
{
    private readonly IPackageSourceAuthorization _sourceAuthorization;
    private readonly PackagePayloadAcquisitionPlan? _payloadAcquisition;
    private readonly PackageVersionServicePlan? _versionSettlement;
    private readonly Action<string>? _log;

    /// <param name="versionSettlement">
    /// The Package Version Service this House consults for its latest
    /// selecting demands. Without one, every selecting demand discovers.
    /// </param>
    public PackageHouse(
        IPackageSourceAuthorization sourceAuthorization,
        PackagePayloadAcquisitionPlan? payloadAcquisition = null,
        Action<string>? log = null,
        PackageVersionServicePlan? versionSettlement = null)
    {
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        _sourceAuthorization = sourceAuthorization;
        _payloadAcquisition = payloadAcquisition;
        _versionSettlement = versionSettlement;
        _log = log;
    }

    /// <summary>
    /// The demands the Package Version Service settles at this head: the
    /// latest family. Wildcard and range demands keep discovering until a
    /// later slice adopts their keys.
    /// </summary>
    private static bool UsesVersionService(
        PackageVersionSelectionRequest selection) =>
        selection is PackageVersionSelectionRequest.LatestStable
            or PackageVersionSelectionRequest.LatestPrerelease
            or PackageVersionSelectionRequest.AlwaysLatest;

    /// <summary>
    /// What a retained prior needs for eviction when its coordinate no
    /// longer acquires: the request, authorization, and contract that
    /// together identify the store entry.
    /// </summary>
    private sealed record PriorSettlementScope(
        PackageVersionServicePlan Plan,
        PackageVersionSelectionRequest Request,
        PackageSourceAuthorization Authorization,
        PackageVersionDiscoveryContract Contract);

    /// <summary>
    /// Consumes one source operation lease to settle an exact, candidate-bound,
    /// or selecting request. Stores and any returned payload remain caller-owned.
    /// </summary>
    public Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseRequest request,
        PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        return ExecuteCoreAsync(
            request,
            sourceOperation,
            pruning: null,
            disposeSourceOperation: true);
    }

    /// <summary>
    /// Consumes one source operation lease to settle a complete, resource-free
    /// package version population.
    /// </summary>
    public Task<PackageHouseVersionPopulationResult>
        SettleVersionPopulationAsync(
            PackageHouseVersionPopulationRequest request,
            PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        return SettleVersionPopulationCoreAsync(
            request,
            sourceOperation);
    }

    /// <summary>
    /// Consumes one source operation lease to settle a resource-free package
    /// version listing.
    /// </summary>
    public Task<PackageHouseVersionListingResult>
        SettleVersionListingAsync(
            PackageHouseVersionListingRequest request,
            PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        return SettleVersionListingCoreAsync(
            request,
            sourceOperation);
    }

    /// <summary>
    /// Consumes one source operation lease to settle a candidate-bound
    /// dependency request with its PackageHouse-issued pruning receipt.
    /// </summary>
    public Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseRequest request,
        PackageSourceOperationLease sourceOperation,
        PackageHousePruningReceipt pruning)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        return ExecuteCoreAsync(
            request,
            sourceOperation,
            pruning,
            disposeSourceOperation: true);
    }

    /// <summary>
    /// Settles one sequential step without consuming the caller-owned Package
    /// Source operation lease.
    /// </summary>
    /// <remarks>
    /// The caller must await this step before starting another and remains
    /// responsible for disposing <paramref name="sourceOperation"/>.
    /// </remarks>
    public Task<PackageHouseSettlement> ExecuteStepAsync(
        PackageHouseRequest request,
        PackageSourceOperationLease sourceOperation,
        PackageHousePruningReceipt? pruning = null)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        return ExecuteCoreAsync(
            request,
            sourceOperation,
            pruning,
            disposeSourceOperation: false);
    }

    /// <summary>
    /// Consumes one source operation lease to acquire the exact manifest for a
    /// candidate issued by the same Package Source root generation.
    /// </summary>
    public static Task<ConfiguredPackageManifestResult>
        AcquireCandidateManifestAsync(
            PackageAcquisitionCandidate candidate,
            PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        return AcquireCandidateManifestCoreAsync(
            candidate,
            sourceOperation);
    }

    private static async Task<ConfiguredPackageManifestResult>
        AcquireCandidateManifestCoreAsync(
            PackageAcquisitionCandidate candidate,
            PackageSourceOperationLease sourceOperation)
    {
        using (sourceOperation)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (!sourceOperation.OwnsCandidate(candidate))
            {
                throw new InvalidOperationException(
                    "The package acquisition candidate belongs to another Package Source root generation.");
            }

            return await sourceOperation
                .AcquireCandidateManifestAsync(candidate)
                .ConfigureAwait(false);
        }
    }

    private async Task<PackageHouseSettlement> ExecuteCoreAsync(
        PackageHouseRequest request,
        PackageSourceOperationLease sourceOperation,
        PackageHousePruningReceipt? pruning,
        bool disposeSourceOperation)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(request);
            if (pruning is not null)
            {
                if (!ReferenceEquals(pruning.Request, request))
                {
                    throw new ArgumentException(
                        "The pruning receipt must belong to the exact PackageHouse request.",
                        nameof(pruning));
                }
                if (request.Demand
                    is not PackageHouseDemand.Candidate)
                {
                    throw new NotSupportedException(
                        "Receipt-aware PackageHouse execution currently requires a candidate-bound dependency demand.");
                }
            }
            if ((request.Operation.Profile
                    is PackageHouseOperationProfile.Acquire
                        or PackageHouseOperationProfile.Realize)
                && _payloadAcquisition is null
                && pruning is null)
            {
                throw new InvalidOperationException(
                    "An Acquire or Realize operation requires an authority-scoped package store capability.");
            }
            if (request.Operation.Profile
                    == PackageHouseOperationProfile.Acquire
                && request.DocumentDemand is null
                && _payloadAcquisition?.Access
                    == PackagePayloadAccess.Ranged)
            {
                throw new InvalidOperationException(
                    "Ranged payload access requires a Realize operation or a document demand; an Acquire operation without one selects nothing to bound the read.");
            }
            if (sourceOperation.RequestTimeout
                    != request.Operation.RequestTimeout
                || sourceOperation.OperationTimeout
                    != request.Operation.OperationTimeout)
            {
                throw new ArgumentException(
                    "The Package Source operation deadlines must match the PackageHouse request.",
                    nameof(sourceOperation));
            }

            try
            {
                sourceOperation.ThrowIfExpired();
                PackageSourceAuthorization authorization;
                PackageHouseDecisionReceipt decision;
                PackageAcquisitionCandidate candidate;
                List<PackageHouseFailure> failures;
                PriorSettlementScope? priorScope = null;

                switch (request.Demand)
                {
                    case PackageHouseDemand.Exact exact:
                        authorization =
                            _sourceAuthorization.AuthorizeSourcesFor(
                                exact.Coordinate.PackageId);
                        sourceOperation.ThrowIfExpired();
                        PackageAcquisitionCandidateResult candidateResult =
                            sourceOperation.ResolvePinnedCandidate(
                                authorization,
                                exact.Coordinate);
                        sourceOperation.ThrowIfExpired();
                        failures = AdaptFailures(
                            request,
                            candidateResult.Failures);
                        if (candidateResult.Candidate
                            is not { } exactCandidate)
                        {
                            decision =
                                PackageHouseDecisionReceipt.Stop(
                                    request,
                                    exact.Coordinate);
                            PackageHouseEvidence evidence = new(
                                request,
                                decision,
                                failures: failures);
                            return ResourceFree(
                                CreateCandidateTerminalResult(
                                    candidateResult,
                                    authorization,
                                    evidence));
                        }

                        candidate = exactCandidate;
                        decision =
                            PackageHouseDecisionReceipt.RetainPackage(
                                request,
                                candidate.Coordinate,
                                candidate);
                        break;

                    case PackageHouseDemand.Candidate candidateDemand:
                        candidate = candidateDemand.Value;
                        if (!sourceOperation.OwnsCandidate(candidate))
                        {
                            throw new InvalidOperationException(
                                "The package acquisition candidate belongs to another Package Source root generation.");
                        }
                        authorization =
                            _sourceAuthorization.AuthorizeSourcesFor(
                                candidate.Coordinate.PackageId);
                        sourceOperation.ThrowIfExpired();
                        failures = [];
                        if (!CandidateRemainsAuthorized(
                                candidate,
                                authorization))
                        {
                            decision =
                                PackageHouseDecisionReceipt.Stop(
                                    request,
                                    candidate.Coordinate);
                            return ResourceFree(
                                new PackageHouseResult.Rejected(
                                    new PackageHouseEvidence(
                                        request,
                                        decision),
                                    Reason(
                                        "The resolved package candidate is not authorized by this PackageHouse.")));
                        }
                        decision =
                            PackageHouseDecisionReceipt.RetainPackage(
                                request,
                                candidate.Coordinate,
                                candidate);
                        break;

                    case PackageHouseDemand.Selecting selecting:
                        PackageVersionSelectionRequest selection =
                            selecting.Request;
                        authorization =
                            _sourceAuthorization.AuthorizeSourcesFor(
                                selection.PackageId);
                        sourceOperation.ThrowIfExpired();
                        PackageVersionDiscoveryContract
                            discoveryContract =
                                PackageVersionDiscoveryContract.Create(
                                    selection.Discovery
                                        .IncludePrerelease,
                                    includeUnlisted: false,
                                    limit: null);
                        PackageVersionResolutionReceipt resolution;
                        if (_versionSettlement is { } plan
                            && UsesVersionService(selection))
                        {
                            PackageVersionServiceSettlement settled;
                            try
                            {
                                settled = await plan.Service.SettleAsync(
                                    selection,
                                    authorization,
                                    discoveryContract,
                                    new PackageVersionServiceOperation
                                    {
                                        Discover = scope =>
                                            scope.Bound is { } bound
                                                ? sourceOperation
                                                    .DiscoverVersionsAsync(
                                                        selection.PackageId,
                                                        authorization,
                                                        discoveryContract,
                                                        bound,
                                                        _log)
                                                : sourceOperation
                                                    .DiscoverVersionsAsync(
                                                        selection.PackageId,
                                                        authorization,
                                                        discoveryContract,
                                                        _log),
                                        Pin = coordinate =>
                                            sourceOperation
                                                .ResolvePinnedCandidate(
                                                    authorization,
                                                    coordinate),
                                        Budget = plan.Budget,
                                        Offline = plan.Offline,
                                    },
                                    sourceOperation.CancellationToken)
                                    .ConfigureAwait(false);
                            }
                            catch (NuGetOperationTimeoutException)
                            {
                                return OperationTimedOut(request);
                            }
                            failures = AdaptFailures(
                                request,
                                settled.PinFailures);
                            resolution = settled.Receipt;
                            if (resolution
                                is PackageVersionResolutionReceipt
                                    .Discovered discovered)
                            {
                                failures.AddRange(
                                    AdaptFailures(
                                        request,
                                        discovered.Discovery.Failures));
                            }
                            else if (resolution
                                is PackageVersionResolutionReceipt
                                    .Prior served)
                            {
                                priorScope = new(
                                    plan,
                                    selection,
                                    authorization,
                                    discoveryContract);
                                // The plan's ledger is what the host discloses
                                // once per invocation; the per-package detail
                                // stays at diagnostic verbosity here.
                                plan.RecordServedPrior(served);
                                _log?.Invoke(
                                    served.Age is { } age
                                        ? $"Version settlement: {served.Coordinate.PackageId}@{served.Coordinate.Version} served from a prior settlement ({settled.Path}, {age.TotalMinutes:F0} min old)."
                                        : $"Version settlement: {served.Coordinate.PackageId}@{served.Coordinate.Version} served from a prior settlement ({settled.Path}).");
                                foreach (PackageAuthorityFailure refreshFailure
                                    in settled.RefreshFailures)
                                {
                                    _log?.Invoke(
                                        $"Version settlement: refresh failure absorbed: {refreshFailure.Authority}: {refreshFailure.Message}");
                                }
                            }
                        }
                        else
                        {
                            PackageVersionDiscoveryResult discovery =
                                await sourceOperation
                                    .DiscoverVersionsAsync(
                                        selection.PackageId,
                                        authorization,
                                        discoveryContract,
                                        _log)
                                    .ConfigureAwait(false);
                            failures = AdaptFailures(
                                request,
                                discovery.Failures);
                            resolution =
                                PackageVersionSelectionResolver.Resolve(
                                    selection,
                                    discovery,
                                    PackageVersionDiscoveryFreshness
                                        .RefreshedForRequest);
                        }
                        if (failures.Any(
                                failure => failure
                                    is PackageHouseFailure.Authority
                                    {
                                        Failure.Timeout.Kind:
                                            PackageSourceTimeoutKind
                                                .Operation,
                                    }))
                        {
                            return OperationTimedOut(
                                request,
                                failures);
                        }
                        try
                        {
                            sourceOperation.ThrowIfExpired();
                        }
                        catch (NuGetOperationTimeoutException)
                        {
                            return OperationTimedOut(
                                request,
                                failures);
                        }
                        switch (resolution)
                        {
                            case PackageVersionResolutionReceipt
                                .Resolved resolved:
                                candidate = resolved.Candidate;
                                decision =
                                    PackageHouseDecisionReceipt
                                        .RetainSelectedPackage(
                                            request,
                                            resolved);
                                break;
                            case PackageVersionResolutionReceipt
                                .Prior prior:
                                candidate = prior.Candidate;
                                decision =
                                    PackageHouseDecisionReceipt
                                        .RetainPriorPackage(
                                            request,
                                            prior);
                                break;
                            default:
                                decision =
                                    PackageHouseDecisionReceipt.Stop(
                                        request,
                                        versionResolution: resolution);
                                PackageHouseEvidence evidence = new(
                                    request,
                                    decision,
                                    failures: failures);
                                return ResourceFree(
                                    CreateResolutionTerminalResult(
                                        resolution,
                                        evidence));
                        }
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(request),
                            request.Demand,
                            "Unknown PackageHouse demand.");
                }

                if (pruning is not null)
                {
                    if (pruning.Supply.DelegatesToPlatform)
                    {
                        decision =
                            PackageHouseDecisionReceipt.DelegateToPlatform(
                                request,
                                candidate.Coordinate,
                                candidate,
                                pruning);
                        PackageHouseEvidence evidence = new(
                            request,
                            decision,
                            failures: failures);
                        return ResourceFree(
                            new PackageHouseResult.Delegated(
                                evidence,
                                new PlatformDelegation(decision)));
                    }

                    decision =
                        PackageHouseDecisionReceipt.RetainPackage(
                            request,
                            candidate.Coordinate,
                            candidate,
                            pruning);
                }

                if (request.Operation.Profile
                    == PackageHouseOperationProfile.Settle)
                {
                    return ResourceFree(
                        new PackageHouseResult.Settled(
                            new PackageHouseEvidence(
                                request,
                                decision,
                                failures: failures)));
                }

                PackagePayloadAcquisitionPlan payloadAcquisition =
                    _payloadAcquisition
                    ?? throw new InvalidOperationException(
                        "An Acquire or Realize operation requires an authority-scoped package store capability.");
                bool selectionUsesOriginalSources =
                    request.Demand is not PackageHouseDemand.Selecting
                    || failures.Count == 0
                        && AuthoritiesMatch(
                            candidate,
                            authorization);
                string rangedPackageId = candidate.Coordinate.PackageId;
                PackageRangedRead? rangedRead =
                    payloadAcquisition.Access == PackagePayloadAccess.Ranged
                        ? new PackageRangedRead(
                            directory => request.DocumentDemand is { } documents
                                ? new PackageRangedSelection(
                                    documents.Select(
                                        [.. directory.EnumerateEntries()]))
                                : SelectRangedEntries(
                                    request,
                                    rangedPackageId,
                                    directory),
                            payloadAcquisition.RangedSizeCut)
                        : null;
                ConfiguredPackagePayloadResult payloadResult =
                    await sourceOperation
                        .AcquireCandidatePayloadAsync(
                            candidate,
                            payloadAcquisition.GetStore,
                            log: payloadAcquisition.Log,
                            limits: payloadAcquisition.Limits,
                            transferPolicy:
                                payloadAcquisition.TransferPolicy,
                            rangedRead: rangedRead)
                        .ConfigureAwait(false);
                failures.AddRange(
                    AdaptFailures(request, payloadResult.Failures));
                if (payloadResult.Payload is not { } payload)
                {
                    if (priorScope is { } scope
                        && PayloadNotFound(candidate, payloadResult)
                        && !failures.Any(IsOperationTimeout))
                    {
                        // The prior coordinate vanished from every authorized
                        // source. Only the House observes that, so it evicts
                        // the entry and says so; this request still fails
                        // with the ordinary not-found result and the next
                        // one settles by discovery.
                        scope.Plan.Service.Evict(
                            scope.Request,
                            scope.Authorization,
                            scope.Contract);
                        string priorCoordinate =
                            $"{candidate.Coordinate.PackageId}@{candidate.Coordinate.Version}";
                        failures.Add(
                            new PackageHouseFailure.Stage(
                                PackageHouseFailureStage.Acquisition,
                                Reason(
                                    $"The retained prior settlement {priorCoordinate} is no longer supplied by any authorized source and was evicted; rerun to settle by discovery.")));
                        _log?.Invoke(
                            $"Version settlement: evicted the prior settlement {priorCoordinate} after acquisition found no payload.");
                    }
                    PackageHouseEvidence evidence = new(
                        request,
                        decision,
                        failures: failures);
                    return ResourceFree(
                        CreatePayloadTerminalResult(
                            candidate,
                            payloadResult,
                            evidence),
                        payloadResult,
                        selectionUsesOriginalSources);
                }

                PackageHouseAcquisitionReceipt acquisition = new(
                    decision,
                    payloadResult.Authority!,
                    payloadResult.Source!,
                    payload.Origin,
                    payload.Content.GenerationIdentity,
                    payloadResult.Transfer!);
                if (request.DocumentDemand?.Unmatched(
                        payload.Content.EnumerateEntries())
                    is [_, ..] unmatchedDocuments)
                {
                    // A named document the archive does not list is a
                    // visible failure, never an empty success
                    // (docs/design/package-read-demand.md#document-demand).
                    InertString reason = Reason(
                        "The package does not contain "
                        + string.Join(
                            ", ",
                            unmatchedDocuments.Select(
                                static name => $"'{name}'"))
                        + ".");
                    failures.Add(
                        new PackageHouseFailure.Stage(
                            PackageHouseFailureStage.Selection,
                            reason));
                    return new PackageHouseSettlement.Acquired(
                        new PackageHouseResult.NoMatch(
                            new PackageHouseEvidence(
                                request,
                                decision,
                                acquisition,
                                failures: failures),
                            reason),
                        payload,
                        payloadResult,
                        selectionUsesOriginalSources);
                }

                if (request.Operation.Profile
                    == PackageHouseOperationProfile.Acquire)
                {
                    PackageHouseEvidence acquiredEvidence = new(
                        request,
                        decision,
                        acquisition,
                        failures: failures);
                    return new PackageHouseSettlement.Acquired(
                        new PackageHouseResult.Settled(
                            acquiredEvidence),
                        payload,
                        payloadResult,
                        selectionUsesOriginalSources);
                }

                if (failures.Any(IsOperationTimeout))
                {
                    return OperationTimedOut(
                        request,
                        payload,
                        payloadResult,
                        selectionUsesOriginalSources,
                        decision,
                        acquisition,
                        realization: null,
                        failures);
                }

                try
                {
                    sourceOperation.ThrowIfExpired();
                }
                catch (NuGetOperationTimeoutException)
                {
                    return OperationTimedOut(
                        request,
                        payload,
                        payloadResult,
                        selectionUsesOriginalSources,
                        decision,
                        acquisition,
                        realization: null,
                        failures);
                }

                if (request.AssetSelection
                        == PackageHouseAssetSelectionKind.Runtime
                    && request.TargetContext?.RequestedFramework is null)
                {
                    failures.Add(
                        new PackageHouseFailure.Stage(
                            PackageHouseFailureStage.Selection,
                            Reason(
                                "Runtime package realization requires an exact target framework.")));
                    PackageHouseEvidence rejectedEvidence = new(
                        request,
                        decision,
                        acquisition,
                        failures: failures);
                    return new PackageHouseSettlement.Acquired(
                        new PackageHouseResult.Rejected(
                            rejectedEvidence,
                            Reason(
                                "Runtime package realization requires an exact target framework.")),
                        payload,
                        payloadResult,
                        selectionUsesOriginalSources);
                }

                PackageHouseRealizationReceipt realization =
                    CreateRealization(
                        request,
                        acquisition,
                        payload.Content);
                PackageHouseEvidence settledEvidence = new(
                    request,
                    decision,
                    acquisition,
                    realization,
                    failures: failures);

                try
                {
                    sourceOperation.ThrowIfExpired();
                }
                catch (NuGetOperationTimeoutException)
                {
                    return OperationTimedOut(
                        request,
                        payload,
                        payloadResult,
                        selectionUsesOriginalSources,
                        decision,
                        acquisition,
                        realization,
                        failures);
                }

                return new PackageHouseSettlement.Acquired(
                    CreateRealizationTerminalResult(
                        realization,
                        settledEvidence),
                    payload,
                    payloadResult,
                    selectionUsesOriginalSources);
            }
            catch (NuGetOperationTimeoutException)
            {
                return OperationTimedOut(request);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    sourceOperation.ThrowIfExpired();
                }
                catch (NuGetOperationTimeoutException)
                {
                    return OperationTimedOut(request);
                }

                throw;
            }
        }
        finally
        {
            if (disposeSourceOperation)
            {
                sourceOperation.Dispose();
            }
        }
    }

    private async Task<PackageHouseVersionPopulationResult>
        SettleVersionPopulationCoreAsync(
            PackageHouseVersionPopulationRequest request,
            PackageSourceOperationLease sourceOperation)
    {
        using (sourceOperation)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (sourceOperation.RequestTimeout
                    != request.Operation.RequestTimeout
                || sourceOperation.OperationTimeout
                    != request.Operation.OperationTimeout)
            {
                throw new ArgumentException(
                    "The Package Source operation deadlines must match the PackageHouse version-population request.",
                    nameof(sourceOperation));
            }

            try
            {
                sourceOperation.ThrowIfExpired();
                PackageSourceAuthorization authorization =
                    _sourceAuthorization.AuthorizeSourcesFor(
                        request.Range.PackageId);
                sourceOperation.ThrowIfExpired();
                PackageVersionDiscoveryResult discovery =
                    await sourceOperation.DiscoverVersionsAsync(
                        request.Range.PackageId,
                        authorization,
                        request.IncludeUnlisted
                            ? PackageVersionDiscoveryContract
                                .CompleteVersionEnumerationIncludingUnlisted
                            : PackageVersionDiscoveryContract
                                .CompleteVersionEnumeration,
                        _log)
                    .ConfigureAwait(false);
                List<PackageHouseFailure> failures =
                    AdaptFailures(
                        request.Operation,
                        discovery.Failures);
                if (failures.Any(IsOperationTimeout))
                {
                    return PopulationOperationTimedOut(
                        request,
                        discovery,
                        failures);
                }
                try
                {
                    sourceOperation.ThrowIfExpired();
                }
                catch (NuGetOperationTimeoutException)
                {
                    return PopulationOperationTimedOut(
                        request,
                        discovery,
                        failures);
                }

                PackageHouseVersionPopulationEvidence evidence =
                    new(request, discovery, failures);
                PackageHouseVersionPopulationResult result;
                if (!discovery.Contract
                        .SupportsCompleteVersionEnumeration)
                {
                    result = new PackageHouseVersionPopulationResult
                        .Rejected(
                            evidence,
                            Reason(
                                "Configured-authority evidence does not contain a complete package version enumeration."));
                }
                else if (discovery.State
                    == PackageVersionDiscoveryState.Authoritative)
                {
                    if (!discovery.HasAnyCandidate)
                    {
                        result = new PackageHouseVersionPopulationResult
                            .NotFound(
                                evidence,
                                Reason(
                                    "No configured authority reported the package."));
                    }
                    else if (request.Policy
                            == PackageVersionPopulationPolicy.ExactEndpoints
                            && (!PackageVersionVector.ContainsVersion(
                                discovery.Versions,
                                request.Range.Start)
                                || !PackageVersionVector.ContainsVersion(
                                    discovery.Versions,
                                    request.Range.End)))
                    {
                        result = new PackageHouseVersionPopulationResult
                                .NoMatch(
                                evidence,
                                Reason(
                                    "The configured package version population does not contain both requested range endpoints."));
                    }
                    else if (request.Policy
                        == PackageVersionPopulationPolicy.MajorBounds
                        && !PackageVersionVector.ContainsMajorBounds(
                            request.Range,
                            discovery.Versions,
                            request.IncludePrerelease))
                    {
                        result = new PackageHouseVersionPopulationResult
                            .NoMatch(
                                evidence,
                                Reason(
                                    "The configured package version population does not contain an admitted version in both boundary majors."));
                    }
                    else
                    {
                        try
                        {
                            result = new PackageHouseVersionPopulationResult
                                .Available(evidence);
                        }
                        catch (ArgumentException)
                        {
                            result = new PackageHouseVersionPopulationResult
                                .Rejected(
                                    evidence,
                                    Reason(
                                        "Configured-authority version evidence is unusable for population settlement."));
                        }
                    }
                }
                else
                {
                    result = PackageVersionSelectionResolver
                        .ClassifyNonAuthoritativeDiscovery(discovery) switch
                    {
                        PackageVersionDiscoveryTerminalKind.Incomplete =>
                            new PackageHouseVersionPopulationResult
                                .Incomplete(
                                    evidence,
                                    Reason(
                                        "Required configured-authority discovery is incomplete.")),
                        PackageVersionDiscoveryTerminalKind.Failed =>
                            new PackageHouseVersionPopulationResult
                                .Failed(
                                    evidence,
                                    Reason(
                                        "Version discovery failed before the package population could be settled.")),
                        PackageVersionDiscoveryTerminalKind.Rejected =>
                            new PackageHouseVersionPopulationResult
                                .Rejected(
                                    evidence,
                                    Reason(
                                        "Configured-authority evidence is unusable for package population settlement.")),
                        PackageVersionDiscoveryTerminalKind.Unavailable =>
                            new PackageHouseVersionPopulationResult
                                .Unavailable(
                                    evidence,
                                    Reason(
                                        "Required package version population capability is unavailable.")),
                        _ => throw new InvalidOperationException(
                            "Version discovery returned an unknown terminal classification."),
                    };
                }

                try
                {
                    sourceOperation.ThrowIfExpired();
                }
                catch (NuGetOperationTimeoutException)
                {
                    return PopulationOperationTimedOut(
                        request,
                        discovery,
                        failures);
                }

                return result;
            }
            catch (NuGetOperationTimeoutException)
            {
                return PopulationOperationTimedOut(request);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    sourceOperation.ThrowIfExpired();
                }
                catch (NuGetOperationTimeoutException)
                {
                    return PopulationOperationTimedOut(request);
                }

                throw;
            }
        }
    }

    private async Task<PackageHouseVersionListingResult>
        SettleVersionListingCoreAsync(
            PackageHouseVersionListingRequest request,
            PackageSourceOperationLease sourceOperation)
    {
        using (sourceOperation)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (sourceOperation.RequestTimeout
                    != request.Operation.RequestTimeout
                || sourceOperation.OperationTimeout
                    != request.Operation.OperationTimeout)
            {
                throw new ArgumentException(
                    "The Package Source operation deadlines must match the PackageHouse version-listing request.",
                    nameof(sourceOperation));
            }

            try
            {
                sourceOperation.ThrowIfExpired();
                PackageSourceAuthorization authorization =
                    _sourceAuthorization.AuthorizeSourcesFor(
                        request.PackageId);
                sourceOperation.ThrowIfExpired();
                PackageVersionDiscoveryResult discovery =
                    await sourceOperation.DiscoverVersionsAsync(
                        request.PackageId,
                        authorization,
                        PackageVersionDiscoveryContract.Create(
                            request.IncludePrerelease,
                            request.IncludeUnlisted,
                            limit: null),
                        _log)
                    .ConfigureAwait(false);
                List<PackageHouseFailure> failures =
                    AdaptFailures(
                        request.Operation,
                        discovery.Failures);
                if (failures.Any(IsOperationTimeout))
                {
                    return ListingOperationTimedOut(
                        request,
                        discovery,
                        failures);
                }
                try
                {
                    sourceOperation.ThrowIfExpired();
                }
                catch (NuGetOperationTimeoutException)
                {
                    return ListingOperationTimedOut(
                        request,
                        discovery,
                        failures);
                }

                PackageHouseVersionListingEvidence evidence =
                    new(request, discovery, failures);
                PackageHouseVersionListingResult result =
                    discovery.State switch
                    {
                        PackageVersionDiscoveryState.Authoritative
                            when !discovery.HasAnyCandidate =>
                                new PackageHouseVersionListingResult
                                    .NotFound(
                                        evidence,
                                        Reason(
                                            "No configured authority reported the package.")),
                        PackageVersionDiscoveryState.Authoritative
                            or PackageVersionDiscoveryState.Partial =>
                                new PackageHouseVersionListingResult
                                    .Available(evidence),
                        PackageVersionDiscoveryState.Failed =>
                            PackageVersionSelectionResolver
                                .ClassifyNonAuthoritativeDiscovery(discovery)
                            switch
                            {
                                PackageVersionDiscoveryTerminalKind.Incomplete =>
                                    new PackageHouseVersionListingResult
                                        .Incomplete(
                                            evidence,
                                            Reason(
                                                "Required configured-authority discovery is incomplete.")),
                                PackageVersionDiscoveryTerminalKind.Failed =>
                                    new PackageHouseVersionListingResult
                                        .Failed(
                                            evidence,
                                            Reason(
                                                "Version discovery failed before the package listing could be settled.")),
                                PackageVersionDiscoveryTerminalKind.Rejected =>
                                    new PackageHouseVersionListingResult
                                        .Rejected(
                                            evidence,
                                            Reason(
                                                "Configured-authority evidence is unusable for package version listing.")),
                                PackageVersionDiscoveryTerminalKind.Unavailable =>
                                    new PackageHouseVersionListingResult
                                        .Unavailable(
                                            evidence,
                                            Reason(
                                                "Required package version-listing capability is unavailable.")),
                                _ => throw new InvalidOperationException(
                                    "Version discovery returned an unknown terminal classification."),
                            },
                        _ => throw new InvalidOperationException(
                            "Version discovery returned an unknown state."),
                    };

                try
                {
                    sourceOperation.ThrowIfExpired();
                }
                catch (NuGetOperationTimeoutException)
                {
                    return ListingOperationTimedOut(
                        request,
                        discovery,
                        failures);
                }

                return result;
            }
            catch (NuGetOperationTimeoutException)
            {
                return ListingOperationTimedOut(request);
            }
        }
    }

    private static bool CandidateRemainsAuthorized(
        PackageAcquisitionCandidate candidate,
        PackageSourceAuthorization authorization) =>
        candidate.Authorities.All(
            evidence =>
                authorization.TryGetAuthority(
                    evidence.Authority.Association,
                    out _));

    private static bool AuthoritiesMatch(
        PackageAcquisitionCandidate candidate,
        PackageSourceAuthorization authorization)
    {
        var candidateAuthorities =
            new HashSet<ConfiguredPackageAuthority>(
                candidate.Authorities.Select(
                    evidence => evidence.Authority),
                ReferenceEqualityComparer.Instance);
        return candidateAuthorities.SetEquals(
            authorization.Authorities);
    }

    private static PackageHouseResult CreateCandidateTerminalResult(
        PackageAcquisitionCandidateResult candidate,
        PackageSourceAuthorization authorization,
        PackageHouseEvidence evidence)
    {
        if (evidence.HasOperationTimeout)
        {
            return new PackageHouseResult.Failed(
                evidence,
                Reason("The PackageHouse operation deadline expired."));
        }
        return candidate.State switch
        {
            PackageAcquisitionCandidateResultState.Incomplete =>
                new PackageHouseResult.Incomplete(
                    evidence,
                    Reason(
                        "Exact package authorization did not complete.")),
            PackageAcquisitionCandidateResultState.Denied =>
                new PackageHouseResult.Unavailable(
                    evidence,
                    Reason(
                        authorization.DenialReason
                        ?? "No configured authority is authorized for the exact package.")),
            _ => throw new InvalidOperationException(
                "A candidate terminal result requires a non-resolved candidate."),
        };
    }

    private static PackageHouseResult CreateResolutionTerminalResult(
        PackageVersionResolutionReceipt resolution,
        PackageHouseEvidence evidence) =>
        resolution switch
        {
            PackageVersionResolutionReceipt.NotFound notFound =>
                new PackageHouseResult.NotFound(
                    evidence,
                    notFound.Reason),
            PackageVersionResolutionReceipt.NoMatch noMatch =>
                new PackageHouseResult.NoMatch(
                    evidence,
                    noMatch.Reason),
            PackageVersionResolutionReceipt.Ambiguous ambiguous =>
                new PackageHouseResult.Ambiguous(
                    evidence,
                    ambiguous.Reason),
            PackageVersionResolutionReceipt.Rejected rejected =>
                new PackageHouseResult.Rejected(
                    evidence,
                    rejected.Reason),
            PackageVersionResolutionReceipt.Unavailable unavailable =>
                new PackageHouseResult.Unavailable(
                    evidence,
                    unavailable.Reason),
            PackageVersionResolutionReceipt.Incomplete incomplete =>
                new PackageHouseResult.Incomplete(
                    evidence,
                    incomplete.Reason),
            PackageVersionResolutionReceipt.Failed failed =>
                new PackageHouseResult.Failed(
                    evidence,
                    failed.Reason),
            _ => throw new InvalidOperationException(
                "A terminal selection result requires a non-resolved receipt."),
        };

    /// <summary>
    /// Every authority the candidate names reported the exact coordinate
    /// absent, with no failure: the not-found outcome.
    /// </summary>
    private static bool PayloadNotFound(
        PackageAcquisitionCandidate candidate,
        ConfiguredPackagePayloadResult result) =>
        result.Failures.Count == 0
        && candidate.Authorities.All(evidence =>
            result.NotFoundAuthorities.Contains(
                evidence.Authority,
                ReferenceEqualityComparer.Instance));

    private static PackageHouseResult CreatePayloadTerminalResult(
        PackageAcquisitionCandidate candidate,
        ConfiguredPackagePayloadResult result,
        PackageHouseEvidence evidence)
    {
        if (evidence.HasOperationTimeout)
        {
            return new PackageHouseResult.Failed(
                evidence,
                Reason("The PackageHouse operation deadline expired."));
        }
        if (PayloadNotFound(candidate, result))
        {
            return new PackageHouseResult.NotFound(
                evidence,
                Reason(
                    "No authorized source supplied the exact package payload."));
        }
        if (result.Failures.Any(failure =>
                failure.Kind is PackageAuthorityFailureKind.Input
                    or PackageAuthorityFailureKind.InvalidResponse
                    or PackageAuthorityFailureKind.ResponseRejected))
        {
            return new PackageHouseResult.Rejected(
                evidence,
                Reason(
                    "Authorized source evidence was rejected during payload acquisition."));
        }
        if (result.Failures.Count > 0
            && result.Failures.All(failure =>
                failure.Kind
                    is PackageAuthorityFailureKind.AuthenticationRequired
                    or PackageAuthorityFailureKind.Configuration
                    or PackageAuthorityFailureKind.Unsupported))
        {
            return new PackageHouseResult.Unavailable(
                evidence,
                Reason(
                    "The exact package payload capability is unavailable."));
        }

        return new PackageHouseResult.Failed(
            evidence,
            Reason(
                "The exact package payload could not be acquired."));
    }

    private static List<PackageHouseFailure> AdaptFailures(
        PackageHouseRequest request,
        IEnumerable<PackageAuthorityFailure> failures) =>
        AdaptFailures(request.Operation, failures);

    private static List<PackageHouseFailure> AdaptFailures(
        PackageHouseOperation operation,
        IEnumerable<PackageAuthorityFailure> failures) =>
        [
            .. failures.Select(failure =>
                new PackageHouseFailure.Authority(
                    operation.Identity,
                    failure)),
        ];

    private static PackageCompileAssetSelectionPolicy CompilePolicy(
        PackageHouseRequest request) =>
        request.TargetContext?.RequestedFramework is null
            ? PackageCompileAssetSelectionPolicy.HighestAvailable
            : PackageCompileAssetSelectionPolicy.ExplicitTarget;

    /// <summary>
    /// The entries a ranged acquisition materializes: exactly what this
    /// request's realization selects over the archive directory, so the
    /// retained content can answer the realization it was read for. Surface
    /// assets, and unnamed implementation assets, are read as whole folders;
    /// named implementation assets are block anchors, read with their aligned
    /// block (docs/design/package-read-demand.md). A runtime request without
    /// an exact framework selects nothing here and is visibly rejected after
    /// acquisition, as it is for complete access.
    /// </summary>
    private static PackageRangedSelection SelectRangedEntries(
        PackageHouseRequest request,
        string packageId,
        IPackageContent directory)
    {
        PackageImplementationNames? names = request.ImplementationNames;
        switch (request.AssetSelection)
        {
            case PackageHouseAssetSelectionKind.Compile:
                PackageCompileAssetSelection compile =
                    PackageCompileAssetSelector.Evaluate(
                        directory,
                        packageId,
                        CompilePolicy(request),
                        request.TargetContext?.RequestedFramework,
                        request.TargetContext?.RuntimeIdentifier).Selection;
                IEnumerable<string> surface =
                    compile.Assets.Select(static asset => asset.Path);
                if (request.AssetDemand == PackageAssetDemand.Surface)
                    return new(WholeFolders(surface, directory));
                IEnumerable<string> implementation =
                    compile.ImplementationAssets.Select(static asset => asset.Path);
                if (names is null)
                    return new(WholeFolders(surface.Concat(implementation), directory));
                // A named implementation asset whose folder is already read
                // whole as the surface, as in a package with no ref/ folder,
                // needs no block: its block would add only other folders'
                // interleaved entries (docs/design/package-read-demand.md).
                IReadOnlyList<string> surfaceEntries = WholeFolders(surface, directory);
                var readWhole = new HashSet<string>(surfaceEntries, StringComparer.Ordinal);
                return new(
                    surfaceEntries,
                    [.. Anchors(implementation.Where(names.MatchesPath), directory)
                        .Where(anchor => !readWhole.Contains(anchor))]);
            case PackageHouseAssetSelectionKind.Runtime:
                if (request.TargetContext?.RequestedFramework is not { } framework)
                    return new([]);
                if (PackageAssetSelector.Evaluate(
                        directory,
                        framework,
                        request.TargetContext.RuntimeIdentifier).Selection
                    is not PackageAssetSelection.Selected selected)
                {
                    return new([]);
                }
                IEnumerable<string> universe =
                    selected.Universe.Assets.Select(static asset => asset.EntryPath);
                return names is null
                    ? new(WholeFolders(universe, directory))
                    : new([], Anchors(universe.Where(names.MatchesPath), directory));
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.AssetSelection,
                    "A ranged Realize operation requires a known asset-selection kind.");
        }
    }

    /// <summary>The directory entries the selected asset paths name.</summary>
    private static IReadOnlyList<string> Anchors(
        IEnumerable<string> selected,
        IPackageContent directory)
    {
        var paths = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);
        return [.. directory.EnumerateEntries().Where(paths.Contains)];
    }

    /// <summary>
    /// A ranged read fetches whole folders: every entry directly inside the
    /// folder of each selected asset (its documentation XML beside it, for
    /// one), never a subfolder. A folder is contiguous in most archives, so
    /// it is usually one request, and a later reader that needs the folder's
    /// other files finds them cached.
    /// </summary>
    private static IReadOnlyList<string> WholeFolders(
        IEnumerable<string> selected,
        IPackageContent directory)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in selected)
        {
            int slash = path.LastIndexOf('/');
            folders.Add(slash < 0 ? "" : path[..(slash + 1)]);
        }

        var entries = new List<string>();
        foreach (string entry in directory.EnumerateEntries())
        {
            int slash = entry.LastIndexOf('/');
            string folder = slash < 0 ? "" : entry[..(slash + 1)];
            if (folders.Contains(folder))
                entries.Add(entry);
        }
        return entries;
    }

    /// <summary>
    /// The realization, evaluated over the materialized content. With named
    /// implementation demand the selection keeps only the named
    /// implementation assets, so the receipt names only entries the read
    /// fetched; a name that selects no implementation asset leaves the
    /// realization unmatched.
    /// </summary>
    private static PackageHouseRealizationReceipt CreateRealization(
        PackageHouseRequest request,
        PackageHouseAcquisitionReceipt acquisition,
        IPackageContent content) =>
        request.AssetSelection switch
        {
            PackageHouseAssetSelectionKind.Compile =>
                new PackageHouseRealizationReceipt.Compile(
                    acquisition,
                    NameImplementation(
                        PackageCompileAssetSelector.Evaluate(
                            content,
                            acquisition.Candidate.Coordinate.PackageId,
                            CompilePolicy(request),
                            request.TargetContext?.RequestedFramework,
                            request.TargetContext?.RuntimeIdentifier),
                        request.ImplementationNames)),
            PackageHouseAssetSelectionKind.Runtime =>
                new PackageHouseRealizationReceipt.Runtime(
                    acquisition,
                    NameImplementation(
                        PackageAssetSelector.Evaluate(
                            content,
                            request.TargetContext!.RequestedFramework!,
                            request.TargetContext.RuntimeIdentifier),
                        request.ImplementationNames)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.AssetSelection,
                "A Realize operation requires a known asset-selection kind."),
        };

    private static PackageCompileAssetSelectionReceipt NameImplementation(
        PackageCompileAssetSelectionReceipt receipt,
        PackageImplementationNames? names) =>
        names is null
            ? receipt
            : new PackageCompileAssetSelectionReceipt(
                receipt.Generation,
                receipt.PackageId,
                receipt.Policy,
                receipt.RequestedTargetFramework,
                receipt.RequestedRuntimeIdentifier,
                receipt.Selection with
                {
                    ImplementationAssets = Array.AsReadOnly(
                    [
                        .. receipt.Selection.ImplementationAssets.Where(
                            asset => names.MatchesPath(asset.Path)),
                    ]),
                });

    private static PackageAssetSelectionReceipt NameImplementation(
        PackageAssetSelectionReceipt receipt,
        PackageImplementationNames? names) =>
        names is null
            || receipt.Selection is not PackageAssetSelection.Selected selected
            ? receipt
            : new PackageAssetSelectionReceipt(
                receipt.Generation,
                receipt.RequestedTargetFramework,
                receipt.RequestedRuntimeIdentifier,
                new PackageAssetSelection.Selected(
                    new PackageAssetUniverse(
                        selected.Universe.TargetFramework,
                        selected.Universe.RuntimeIdentifier,
                        selected.Universe.Assets.Where(
                            asset => names.MatchesPath(asset.EntryPath))))
                {
                    UsesCompatibleTargetSelection =
                        selected.UsesCompatibleTargetSelection,
                });

    private static PackageHouseResult CreateRealizationTerminalResult(
        PackageHouseRealizationReceipt realization,
        PackageHouseEvidence evidence) =>
        realization.Completion switch
        {
            PackageHouseRealizationCompletion.Settled =>
                new PackageHouseResult.Settled(evidence),
            PackageHouseRealizationCompletion.NoMatch =>
                new PackageHouseResult.NoMatch(
                    evidence,
                    RealizationReason(realization)),
            PackageHouseRealizationCompletion.Ambiguous =>
                new PackageHouseResult.Ambiguous(
                    evidence,
                    RealizationReason(realization)),
            PackageHouseRealizationCompletion.Rejected =>
                new PackageHouseResult.Rejected(
                    evidence,
                    RealizationReason(realization)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(realization),
                realization.Completion,
                "Unknown PackageHouse realization completion."),
        };

    private static InertString RealizationReason(
        PackageHouseRealizationReceipt realization) =>
        realization.UnmatchedImplementationNames is [_, ..] unmatched
            ? Reason(
                "No selected implementation asset is named "
                + string.Join(", ", unmatched.Select(static name => $"'{name}'"))
                + ".")
            : realization switch
        {
            PackageHouseRealizationReceipt.Compile compile =>
                Reason(
                    compile.Selection.Message
                    ?? compile.Selection.Status switch
                    {
                        PackageCompileAssetSelectionStatus.NoCompileAssets =>
                            "The acquired package carries no compile assets.",
                        PackageCompileAssetSelectionStatus
                                .NoMatchingTargetFramework =>
                            "The acquired package has no compile assets matching the requested target framework.",
                        _ =>
                            "The acquired package compile assets could not be selected.",
                    }),
            PackageHouseRealizationReceipt.Runtime runtime =>
                Reason(
                    runtime.Selection switch
                    {
                        PackageAssetSelection.NoMatch noMatch =>
                            noMatch.Message,
                        PackageAssetSelection.Ambiguous ambiguous =>
                            ambiguous.Message,
                        PackageAssetSelection.Invalid invalid =>
                            invalid.Message,
                        _ =>
                            "The acquired package runtime assets could not be selected.",
                    }),
            _ => throw new ArgumentOutOfRangeException(
                nameof(realization)),
        };

    private static bool IsOperationTimeout(
        PackageHouseFailure failure) =>
        failure is PackageHouseFailure.Timeout
            {
                Kind: PackageHouseTimeoutKind.Operation,
            }
        || failure is PackageHouseFailure.Authority
            {
                Failure.Timeout.Kind:
                    PackageSourceTimeoutKind.Operation,
            };

    private static InertString Reason(string text) =>
        new(TextPolicy.Field, text);

    private static PackageHouseSettlement OperationTimedOut(
        PackageHouseRequest request,
        IEnumerable<PackageHouseFailure>? existingFailures = null)
    {
        var failures = existingFailures is null
            ? new List<PackageHouseFailure>()
            : [.. existingFailures];
        PackageHouseFailure.Timeout timeout = new(
            request.Operation.Identity,
            PackageHouseTimeoutKind.Operation,
            request.Operation.OperationTimeout);
        failures.Add(timeout);
        PackageHouseEvidence evidence = new(
            request,
            failures: failures);
        return ResourceFree(
            new PackageHouseResult.Failed(
                evidence,
                Reason("The PackageHouse operation deadline expired.")));
    }

    private static PackageHouseVersionPopulationResult
        PopulationOperationTimedOut(
            PackageHouseVersionPopulationRequest request,
            PackageVersionDiscoveryResult? discovery = null,
            IEnumerable<PackageHouseFailure>? existingFailures = null)
    {
        var failures = existingFailures is null
            ? new List<PackageHouseFailure>()
            : [.. existingFailures];
        failures.Add(
            new PackageHouseFailure.Timeout(
                request.Operation.Identity,
                PackageHouseTimeoutKind.Operation,
                request.Operation.OperationTimeout));
        return new PackageHouseVersionPopulationResult.Failed(
            new PackageHouseVersionPopulationEvidence(
                request,
                discovery,
                failures),
            Reason(
                "The PackageHouse version-population operation deadline expired."));
    }

    private static PackageHouseVersionListingResult
        ListingOperationTimedOut(
            PackageHouseVersionListingRequest request,
            PackageVersionDiscoveryResult? discovery = null,
            IEnumerable<PackageHouseFailure>? existingFailures = null)
    {
        var failures = existingFailures is null
            ? new List<PackageHouseFailure>()
            : [.. existingFailures];
        failures.Add(
            new PackageHouseFailure.Timeout(
                request.Operation.Identity,
                PackageHouseTimeoutKind.Operation,
                request.Operation.OperationTimeout));
        return new PackageHouseVersionListingResult.Failed(
            new PackageHouseVersionListingEvidence(
                request,
                discovery,
                failures),
            Reason(
                "The PackageHouse version-listing operation deadline expired."));
    }

    private static PackageHouseSettlement OperationTimedOut(
        PackageHouseRequest request,
        AcquiredPackageSourcePayload payload,
        ConfiguredPackagePayloadResult sourcePayloadResult,
        bool selectionUsesOriginalSources,
        PackageHouseDecisionReceipt decision,
        PackageHouseAcquisitionReceipt acquisition,
        PackageHouseRealizationReceipt? realization,
        IEnumerable<PackageHouseFailure>? existingFailures = null)
    {
        var failures = existingFailures is null
            ? new List<PackageHouseFailure>()
            : [.. existingFailures];
        if (!failures.Any(IsOperationTimeout))
        {
            failures.Add(
                new PackageHouseFailure.Timeout(
                    request.Operation.Identity,
                    PackageHouseTimeoutKind.Operation,
                    request.Operation.OperationTimeout));
        }
        PackageHouseEvidence evidence = new(
            request,
            decision,
            acquisition,
            realization,
            failures);
        return new PackageHouseSettlement.Acquired(
            new PackageHouseResult.Failed(
                evidence,
                Reason("The PackageHouse operation deadline expired.")),
            payload,
            sourcePayloadResult,
            selectionUsesOriginalSources);
    }

    private static PackageHouseSettlement.ResourceFree ResourceFree(
        PackageHouseResult result) =>
        new(result);

    private static PackageHouseSettlement.ResourceFree ResourceFree(
        PackageHouseResult result,
        ConfiguredPackagePayloadResult sourcePayloadResult,
        bool selectionUsesOriginalSources) =>
        new(
            result,
            sourcePayloadResult,
            selectionUsesOriginalSources);
}
