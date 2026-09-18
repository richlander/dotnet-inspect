using System.Collections.Immutable;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// One resource-free request to settle a configured-source package version
/// listing.
/// </summary>
public sealed class PackageHouseVersionListingRequest
{
    public PackageHouseVersionListingRequest(
        string packageId,
        PackageHouseOperation operation,
        bool includePrerelease = false,
        bool includeUnlisted = false,
        PackageHouseRequestAssociation? association = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(operation);
        if (!PackageCoordinateResolver.IsCanonicalPackageId(packageId))
        {
            throw new ArgumentException(
                "A version-listing request requires a valid package ID.",
                nameof(packageId));
        }
        if (operation.Profile != PackageHouseOperationProfile.Settle)
        {
            throw new ArgumentException(
                "A version-listing request requires a Settle operation.",
                nameof(operation));
        }

        PackageId = packageId.ToLowerInvariant();
        Operation = operation;
        IncludePrerelease = includePrerelease;
        IncludeUnlisted = includeUnlisted;
        Association = association;
    }

    public string PackageId { get; }

    public PackageHouseOperation Operation { get; }

    public bool IncludePrerelease { get; }

    public bool IncludeUnlisted { get; }

    public PackageHouseRequestAssociation? Association { get; }
}

/// <summary>
/// Immutable evidence retained by every version-listing terminal result.
/// </summary>
public sealed class PackageHouseVersionListingEvidence
{
    internal PackageHouseVersionListingEvidence(
        PackageHouseVersionListingRequest request,
        PackageVersionDiscoveryResult? discovery = null,
        IEnumerable<PackageHouseFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (discovery is not null
            && (discovery.PackageId is not { } packageId
                || !packageId.Equals(
                    request.PackageId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Version-listing discovery must belong to the requested package.",
                nameof(discovery));
        }

        Request = request;
        Discovery = discovery;
        Failures = failures is null ? [] : [.. failures];
        if (Failures.Any(failure => failure is null))
        {
            throw new ArgumentException(
                "Version-listing evidence cannot retain a null failure.",
                nameof(failures));
        }
        PackageHouseContractValidation.RequireFailuresMatchOperation(
            request.Operation,
            Failures,
            nameof(failures));
    }

    public PackageHouseVersionListingRequest Request { get; }

    public PackageVersionDiscoveryResult? Discovery { get; }

    public ImmutableArray<PackageHouseFailure> Failures { get; }

    public bool HasOperationTimeout =>
        Failures.OfType<PackageHouseFailure.Timeout>().Any(timeout =>
            timeout.Kind == PackageHouseTimeoutKind.Operation)
        || Failures.OfType<PackageHouseFailure.Authority>().Any(authority =>
            authority.Failure.Timeout?.Kind
                == PackageSourceTimeoutKind.Operation);
}

/// <summary>
/// Closed resource-free result of one PackageHouse version-listing settlement.
/// </summary>
public abstract class PackageHouseVersionListingResult
{
    private PackageHouseVersionListingResult(
        PackageHouseVersionListingEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Evidence = evidence;
    }

    public PackageHouseVersionListingEvidence Evidence { get; }

    public PackageHouseVersionListingRequest Request => Evidence.Request;

    public sealed class Available : PackageHouseVersionListingResult
    {
        internal Available(
            PackageHouseVersionListingEvidence evidence)
            : base(evidence)
        {
            PackageVersionDiscoveryResult discovery =
                evidence.Discovery
                ?? throw new ArgumentException(
                    "An available listing requires completed discovery.",
                    nameof(evidence));
            PackageVersionDiscoveryContract contract = discovery.Contract;
            if (discovery.State is not (
                    PackageVersionDiscoveryState.Authoritative
                    or PackageVersionDiscoveryState.Partial)
                || contract.ContractVersion != 1
                || contract.IncludePrerelease
                    != evidence.Request.IncludePrerelease
                || contract.IncludeUnlisted
                    != evidence.Request.IncludeUnlisted
                || contract.Limit is not null
                || evidence.HasOperationTimeout
                || (discovery.State
                    == PackageVersionDiscoveryState.Authoritative
                    && !discovery.HasAnyCandidate))
            {
                throw new ArgumentException(
                    "An available listing requires matching unbounded authoritative or partial discovery without an operation timeout.",
                    nameof(evidence));
            }
        }

        public bool IsAuthoritative =>
            Evidence.Discovery!.State
                == PackageVersionDiscoveryState.Authoritative;
    }

    public sealed class NotFound : PackageHouseVersionListingResult
    {
        internal NotFound(
            PackageHouseVersionListingEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Incomplete : PackageHouseVersionListingResult
    {
        internal Incomplete(
            PackageHouseVersionListingEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Rejected : PackageHouseVersionListingResult
    {
        internal Rejected(
            PackageHouseVersionListingEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Unavailable : PackageHouseVersionListingResult
    {
        internal Unavailable(
            PackageHouseVersionListingEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Failed : PackageHouseVersionListingResult
    {
        internal Failed(
            PackageHouseVersionListingEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }
}
