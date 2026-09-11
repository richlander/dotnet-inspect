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
    /// Consumes one source operation lease to settle an exact or selecting
    /// request. Stores and any returned payload remain caller-owned.
    /// </summary>
    public Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseRequest request,
        PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        return ExecuteCoreAsync(request, sourceOperation);
    }

    private async Task<PackageHouseSettlement> ExecuteCoreAsync(
        PackageHouseRequest request,
        PackageSourceOperationLease sourceOperation)
    {
        using (sourceOperation)
        {
            ArgumentNullException.ThrowIfNull(request);
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
                        PackageVersionDiscoveryResult discovery =
                            await sourceOperation
                                .DiscoverVersionsAsync(
                                    selection.PackageId,
                                    authorization,
                                    discoveryContract)
                                .ConfigureAwait(false);
                        failures = AdaptFailures(
                            request,
                            discovery.Failures);
                        if (discovery.Failures.Any(
                                failure => failure.Timeout?.Kind
                                    == PackageSourceTimeoutKind
                                        .Operation))
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
                        PackageVersionResolutionReceipt resolution =
                            PackageVersionSelectionResolver.Resolve(
                                selection,
                                discovery,
                                PackageVersionDiscoveryFreshness
                                    .RefreshedForRequest);
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
                        if (resolution
                            is not PackageVersionResolutionReceipt
                                .Resolved resolved)
                        {
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

                        candidate = resolved.Candidate;
                        decision =
                            PackageHouseDecisionReceipt
                                .RetainSelectedPackage(
                                    request,
                                    resolved);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(request),
                            request.Demand,
                            "Unknown PackageHouse demand.");
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
                        "An Acquire operation requires a payload acquisition plan.");
                ConfiguredPackagePayloadResult payloadResult =
                    await sourceOperation
                        .AcquireCandidatePayloadAsync(
                            candidate,
                            payloadAcquisition.GetStore,
                            log: payloadAcquisition.Log,
                            limits: payloadAcquisition.Limits,
                            transferPolicy:
                                payloadAcquisition.TransferPolicy)
                        .ConfigureAwait(false);
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
                    new PackageHouseResult.Settled(
                        settledEvidence),
                    payload);
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

    private static PackageHouseSettlement.ResourceFree ResourceFree(
        PackageHouseResult result) =>
        new(result);
}
