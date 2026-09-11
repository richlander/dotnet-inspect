using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// One completed House operation, separating resource-free and live-payload
/// results in the type system.
/// </summary>
public abstract class PackageHouseSettlement
{
    private PackageHouseSettlement(PackageHouseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    public PackageHouseResult Result { get; }

    public sealed class ResourceFree : PackageHouseSettlement
    {
        internal ResourceFree(PackageHouseResult result)
            : base(result)
        {
        }
    }

    public sealed class Acquired : PackageHouseSettlement
    {
        internal Acquired(
            PackageHouseResult result,
            AcquiredPackageSourcePayload payload)
            : base(result)
        {
            ArgumentNullException.ThrowIfNull(payload);
            PackageHouseAcquisitionReceipt acquisition =
                result.Evidence.Acquisition
                ?? throw new ArgumentException(
                    "An acquired House settlement requires its exact acquisition receipt.",
                    nameof(result));
            if (payload.Coordinate != acquisition.Candidate.Coordinate
                || !payload.ProducerKey.Equals(
                    acquisition.Producer.Key,
                    StringComparison.Ordinal)
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
    }
}

/// <summary>
/// Host-neutral settlement of exact and selecting package requests.
/// </summary>
public sealed class PackageHouse
{
    private readonly IPackageSourceAuthorization _sourceAuthorization;
    private readonly PackagePayloadAcquisitionPlan? _payloadAcquisition;

    public PackageHouse(
        IPackageSourceAuthorization sourceAuthorization,
        PackagePayloadAcquisitionPlan? payloadAcquisition = null)
    {
        ArgumentNullException.ThrowIfNull(sourceAuthorization);
        _sourceAuthorization = sourceAuthorization;
        _payloadAcquisition = payloadAcquisition;
    }

    /// <summary>
    /// Settles one exact or selecting request through a source-owner lease.
    /// The caller owns the lease, any supplied operation context, all stores,
    /// and any returned payload.
    /// </summary>
    /// <remarks>
    /// This preserves the current Package Source Model lifetime contract.
    /// Declared ownership effects for async lease use remain a focused
    /// adoption under the resource-ownership tracker.
    /// </remarks>
    public async Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseRequest request,
        PackageSourceSettlementLease sourceLease,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sourceLease);
        if (request.Operation.Profile
            == PackageHouseOperationProfile.Realize)
        {
            throw new NotSupportedException(
                "This PackageHouse execution slice supports Settle and Acquire operations.");
        }
        if (request.Operation.Profile
                == PackageHouseOperationProfile.Acquire
            && _payloadAcquisition is null)
        {
            throw new InvalidOperationException(
                "An Acquire operation requires an authority-scoped package store capability.");
        }
        if (operationContext is not null
            && (operationContext.RequestTimeout
                    != request.Operation.RequestTimeout
                || operationContext.OperationTimeout
                    != request.Operation.OperationTimeout))
        {
            throw new ArgumentException(
                "The supplied operation context deadlines must match the PackageHouse request.",
                nameof(operationContext));
        }

        using NuGetOperationContext? ownedOperation =
            operationContext is null
                ? new(
                    request.Operation.RequestTimeout,
                    request.Operation.OperationTimeout,
                    cancellationToken)
                : null;
        NuGetOperationContext operation =
            operationContext ?? ownedOperation!;
        cancellationToken = operation.ResolveInvocationToken(
            cancellationToken);
        try
        {
            operation.ThrowIfExpired();
            return request.Demand switch
            {
                PackageHouseDemand.Exact exact =>
                    await ExecuteExactAsync(
                        request,
                        exact,
                        sourceLease,
                        cancellationToken,
                        operation).ConfigureAwait(false),
                PackageHouseDemand.Candidate candidate =>
                    await ExecuteCandidateAsync(
                        request,
                        candidate,
                        sourceLease,
                        cancellationToken,
                        operation).ConfigureAwait(false),
                PackageHouseDemand.Selecting selecting =>
                    await ExecuteSelectingAsync(
                        request,
                        selecting,
                        sourceLease,
                        cancellationToken,
                        operation).ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(request),
                    request.Demand,
                    "Unknown PackageHouse demand."),
            };
        }
        catch (NuGetOperationTimeoutException)
        {
            return OperationTimedOut(request);
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
            return OperationTimedOut(request);
        }
    }

    private async Task<PackageHouseSettlement> ExecuteExactAsync(
        PackageHouseRequest request,
        PackageHouseDemand.Exact exact,
        PackageSourceSettlementLease sourceLease,
        CancellationToken cancellationToken,
        NuGetOperationContext operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();
        PackageSourceAuthorization authorization =
            _sourceAuthorization.AuthorizeSourcesFor(
                exact.Coordinate.PackageId);
        cancellationToken.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();
        PackageAcquisitionCandidateResult candidateResult =
            sourceLease.ResolvePinnedCandidate(
                authorization,
                exact.Coordinate);
        operation.ThrowIfExpired();
        List<PackageHouseFailure> failures =
            AdaptFailures(request, candidateResult.Failures);
        if (candidateResult.Candidate is not { } candidate)
        {
            PackageHouseDecisionReceipt decision =
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

        PackageHouseDecisionReceipt retained =
            PackageHouseDecisionReceipt.RetainPackage(
                request,
                candidate.Coordinate,
                candidate);
        if (request.Operation.Profile
            == PackageHouseOperationProfile.Settle)
        {
            return ResourceFree(
                new PackageHouseResult.Settled(
                    new PackageHouseEvidence(
                        request,
                        retained,
                        failures: failures)));
        }

        return await AcquireAsync(
            request,
            retained,
            candidate,
            sourceLease,
            cancellationToken,
            operation,
            failures).ConfigureAwait(false);
    }

    private async Task<PackageHouseSettlement> ExecuteCandidateAsync(
        PackageHouseRequest request,
        PackageHouseDemand.Candidate candidateDemand,
        PackageSourceSettlementLease sourceLease,
        CancellationToken cancellationToken,
        NuGetOperationContext operation)
    {
        cancellationToken.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();
        PackageAcquisitionCandidate candidate = candidateDemand.Value;
        if (!sourceLease.OwnsCandidate(candidate))
        {
            throw new InvalidOperationException(
                "The package acquisition candidate belongs to another source settlement lease.");
        }

        PackageSourceAuthorization authorization =
            _sourceAuthorization.AuthorizeSourcesFor(
                candidate.Coordinate.PackageId);
        cancellationToken.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();
        if (!CandidateRemainsAuthorized(candidate, authorization))
        {
            PackageHouseDecisionReceipt stopped =
                PackageHouseDecisionReceipt.Stop(
                    request,
                    candidate.Coordinate);
            return ResourceFree(
                new PackageHouseResult.Rejected(
                    new PackageHouseEvidence(request, stopped),
                    Reason(
                        "The resolved package candidate is not authorized by this PackageHouse.")));
        }

        PackageHouseDecisionReceipt retained =
            PackageHouseDecisionReceipt.RetainPackage(
                request,
                candidate.Coordinate,
                candidate);
        if (request.Operation.Profile
            == PackageHouseOperationProfile.Settle)
        {
            return ResourceFree(
                new PackageHouseResult.Settled(
                    new PackageHouseEvidence(
                        request,
                        retained)));
        }

        return await AcquireAsync(
            request,
            retained,
            candidate,
            sourceLease,
            cancellationToken,
            operation,
            []).ConfigureAwait(false);
    }

    private async Task<PackageHouseSettlement>
        ExecuteSelectingAsync(
        PackageHouseRequest request,
        PackageHouseDemand.Selecting selecting,
        PackageSourceSettlementLease sourceLease,
        CancellationToken cancellationToken,
        NuGetOperationContext operation)
    {
        PackageVersionSelectionRequest selection =
            selecting.Request;
        cancellationToken.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();
        PackageSourceAuthorization authorization =
            _sourceAuthorization.AuthorizeSourcesFor(
                selection.PackageId);
        cancellationToken.ThrowIfCancellationRequested();
        operation.ThrowIfExpired();
        PackageVersionDiscoveryContract discoveryContract =
            PackageVersionDiscoveryContract.Create(
                selection.Discovery.IncludePrerelease,
                includeUnlisted: false,
                limit: null);
        PackageVersionDiscoveryResult discovery =
            await sourceLease.DiscoverVersionsAsync(
                selection.PackageId,
                authorization,
                discoveryContract,
                cancellationToken,
                operation).ConfigureAwait(false);
        PackageVersionResolutionReceipt resolution =
            PackageVersionSelectionResolver.Resolve(
                selection,
                discovery,
                PackageVersionDiscoveryFreshness
                    .RefreshedForRequest);
        List<PackageHouseFailure> failures =
            AdaptFailures(request, discovery.Failures);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            operation.ThrowIfExpired();
        }
        catch (NuGetOperationTimeoutException)
        {
            return SelectingOperationTimedOut(
                request,
                resolution,
                failures);
        }
        if (resolution
            is not PackageVersionResolutionReceipt.Resolved resolved)
        {
            PackageHouseDecisionReceipt decision =
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

        PackageHouseDecisionReceipt retained =
            PackageHouseDecisionReceipt.RetainSelectedPackage(
                request,
                resolved);
        if (request.Operation.Profile
            == PackageHouseOperationProfile.Settle)
        {
            return ResourceFree(
                new PackageHouseResult.Settled(
                    new PackageHouseEvidence(
                        request,
                        retained,
                        failures: failures)));
        }

        return await AcquireAsync(
            request,
            retained,
            resolved.Candidate,
            sourceLease,
            cancellationToken,
            operation,
            failures).ConfigureAwait(false);
    }

    private static bool CandidateRemainsAuthorized(
        PackageAcquisitionCandidate candidate,
        PackageSourceAuthorization authorization) =>
        candidate.Authorities.All(
            evidence =>
                authorization.TryGetAuthority(
                    evidence.Authority.Association,
                    out _));

    private async Task<PackageHouseSettlement> AcquireAsync(
        PackageHouseRequest request,
        PackageHouseDecisionReceipt decision,
        PackageAcquisitionCandidate candidate,
        PackageSourceSettlementLease sourceLease,
        CancellationToken cancellationToken,
        NuGetOperationContext operation,
        List<PackageHouseFailure> failures)
    {
        PackagePayloadAcquisitionPlan payloadAcquisition =
            _payloadAcquisition
            ?? throw new InvalidOperationException(
                "An Acquire operation requires a payload acquisition plan.");
        ConfiguredPackagePayloadResult payloadResult =
            await sourceLease.AcquireCandidatePayloadAsync(
                candidate,
                payloadAcquisition.GetStore,
                log: payloadAcquisition.Log,
                limits: payloadAcquisition.Limits,
                cancellationToken: cancellationToken,
                transferPolicy: payloadAcquisition.TransferPolicy,
                operationContext: operation).ConfigureAwait(false);
        failures.AddRange(
            AdaptFailures(request, payloadResult.Failures));
        if (payloadResult.Payload is not { } payload)
        {
            PackageHouseEvidence evidence = new(
                request,
                decision,
                failures: failures);
            return ResourceFree(
                CreatePayloadTerminalResult(
                    candidate,
                    payloadResult,
                    evidence));
        }

        PackageHouseAcquisitionReceipt acquisition = new(
            decision,
            candidate,
            payloadResult.Authority!,
            payloadResult.Source!,
            payload.Origin,
            payload.Content.GenerationIdentity);
        PackageHouseEvidence settledEvidence = new(
            request,
            decision,
            acquisition,
            failures: failures);
        return new PackageHouseSettlement.Acquired(
            new PackageHouseResult.Settled(settledEvidence),
            payload);
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
        if (result.Failures.Count == 0
            && candidate.Authorities.All(evidence =>
                result.NotFoundAuthorities.Contains(
                    evidence.Authority,
                    ReferenceEqualityComparer.Instance)))
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
        [
            .. failures.Select(failure =>
                new PackageHouseFailure.Authority(
                    request.Operation.Identity,
                    failure)),
        ];

    private static InertString Reason(string text) =>
        new(TextPolicy.Field, text);

    private static PackageHouseSettlement OperationTimedOut(
        PackageHouseRequest request)
    {
        PackageHouseFailure.Timeout timeout = new(
            request.Operation.Identity,
            PackageHouseTimeoutKind.Operation,
            request.Operation.OperationTimeout);
        PackageHouseEvidence evidence = new(
            request,
            failures: [timeout]);
        return ResourceFree(
            new PackageHouseResult.Failed(
                evidence,
                Reason("The PackageHouse operation deadline expired.")));
    }

    private static PackageHouseSettlement SelectingOperationTimedOut(
        PackageHouseRequest request,
        PackageVersionResolutionReceipt resolution,
        List<PackageHouseFailure> failures)
    {
        PackageHouseDecisionReceipt decision = resolution switch
        {
            PackageVersionResolutionReceipt.Resolved resolved =>
                PackageHouseDecisionReceipt.RetainSelectedPackage(
                    request,
                    resolved),
            _ => PackageHouseDecisionReceipt.Stop(
                request,
                versionResolution: resolution),
        };
        failures.Add(new PackageHouseFailure.Timeout(
            request.Operation.Identity,
            PackageHouseTimeoutKind.Operation,
            request.Operation.OperationTimeout));
        PackageHouseEvidence evidence = new(
            request,
            decision,
            failures: failures);
        return ResourceFree(
            new PackageHouseResult.Failed(
                evidence,
                Reason("The PackageHouse operation deadline expired.")));
    }

    private static PackageHouseSettlement.ResourceFree ResourceFree(
        PackageHouseResult result) =>
        new(result);
}
