using System.Collections.Immutable;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Packages;

/// <summary>
/// One resource-free request to settle a complete configured-source package
/// version population.
/// </summary>
public sealed class PackageHouseVersionPopulationRequest
{
    public PackageHouseVersionPopulationRequest(
        PackageVersionRange range,
        PackageHouseOperation operation,
        bool includePrerelease = false,
        PackageHouseRequestAssociation? association = null,
        bool includeUnlisted = false)
        : this(
            range,
            operation,
            PackageVersionPopulationPolicy.ExactEndpoints,
            includePrerelease,
            association,
            includeUnlisted)
    {
    }

    public PackageHouseVersionPopulationRequest(
        PackageVersionRange range,
        PackageHouseOperation operation,
        PackageVersionPopulationPolicy policy,
        bool includePrerelease = false,
        PackageHouseRequestAssociation? association = null,
        bool includeUnlisted = false)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentNullException.ThrowIfNull(operation);
        if (!PackageCoordinateResolver.IsCanonicalPackageId(range.PackageId))
        {
            throw new ArgumentException(
                "A version-population request requires a valid package ID.",
                nameof(range));
        }
        if (operation.Profile != PackageHouseOperationProfile.Settle)
        {
            throw new ArgumentException(
                "A version-population request requires a Settle operation.",
                nameof(operation));
        }
        if (!Enum.IsDefined(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));

        Range = range;
        Operation = operation;
        IncludePrerelease = includePrerelease;
        IncludeUnlisted = includeUnlisted;
        Association = association;
        Policy = policy;
    }

    public PackageVersionRange Range { get; }

    public PackageHouseOperation Operation { get; }

    public bool IncludePrerelease { get; }

    public bool IncludeUnlisted { get; }

    public PackageHouseRequestAssociation? Association { get; }

    public PackageVersionPopulationPolicy Policy { get; }
}

/// <summary>
/// Immutable evidence retained by every version-population terminal result.
/// </summary>
public sealed class PackageHouseVersionPopulationEvidence
{
    internal PackageHouseVersionPopulationEvidence(
        PackageHouseVersionPopulationRequest request,
        PackageVersionDiscoveryResult? discovery = null,
        IEnumerable<PackageHouseFailure>? failures = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (discovery is not null
            && (discovery.PackageId is not { } packageId
                || !packageId.Equals(
                    request.Range.PackageId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "Version-population discovery must belong to the requested package.",
                nameof(discovery));
        }

        Request = request;
        Discovery = discovery;
        Failures = failures is null ? [] : [.. failures];
        if (Failures.Any(failure => failure is null))
        {
            throw new ArgumentException(
                "Version-population evidence cannot retain a null failure.",
                nameof(failures));
        }
        PackageHouseContractValidation.RequireFailuresMatchOperation(
            request.Operation,
            Failures,
            nameof(failures));
    }

    public PackageHouseVersionPopulationRequest Request { get; }

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
/// One selected address and its candidate from an available version population.
/// </summary>
public sealed class PackageHouseVersionPopulationCell
{
    internal PackageHouseVersionPopulationCell(
        PackageHouseVersionPopulationResult.Available population,
        PackageVersionAddress address)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(address);
        if (!population.Vector.Addresses.Any(candidate =>
                ReferenceEquals(candidate, address)))
        {
            throw new ArgumentException(
                "The selected address belongs to another package version population.",
                nameof(address));
        }

        Population = population;
        Address = address;
        Candidate = population.Evidence.Discovery!.SelectCandidate(
            address.Version.ToNormalizedString());
        Association = PackageHouseRequestAssociation.Create();
    }

    public PackageHouseVersionPopulationResult.Available Population { get; }

    public PackageVersionAddress Address { get; }

    public string NormalizedVersion => Candidate.Coordinate.Version;

    public PackageHouseRequestAssociation Association { get; }

    internal PackageAcquisitionCandidate Candidate { get; }

    /// <summary>
    /// Prepares one exact candidate-bound House execution for this cell.
    /// <paramref name="assetDemand"/> bounds a ranged read of the cell's
    /// payload (docs/design/package-read-demand.md#asset-demand).
    /// </summary>
    public PackageHouseVersionPopulationCellExecution PrepareExecution(
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext = null,
        PackageHouseAssetSelectionKind? assetSelection = null,
        PackageHouseLibraryHandoffMode libraryHandoff =
            PackageHouseLibraryHandoffMode.PackageOnly,
        PackageAssetDemand assetDemand =
            PackageAssetDemand.SurfaceAndImplementation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new(
            this,
            CreateRequest(
                operation,
                targetContext,
                assetSelection,
                libraryHandoff,
                assetDemand));
    }

    internal PackageHouseRequest CreateRequest(
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext,
        PackageHouseAssetSelectionKind? assetSelection,
        PackageHouseLibraryHandoffMode libraryHandoff,
        PackageAssetDemand assetDemand =
            PackageAssetDemand.SurfaceAndImplementation) =>
        new(
            new PackageHouseDemand.Candidate(Candidate),
            operation,
            targetContext,
            assetSelection,
            libraryHandoff,
            Association,
            assetDemand);
}

/// <summary>
/// One PackageHouse-issued exact request for a version-population cell.
/// </summary>
public sealed class PackageHouseVersionPopulationCellExecution
{
    internal PackageHouseVersionPopulationCellExecution(
        PackageHouseVersionPopulationCell cell,
        PackageHouseRequest request)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(request);
        Cell = cell;
        Request = request;
    }

    public PackageHouseVersionPopulationCell Cell { get; }

    public PackageHouseRequest Request { get; }

    /// <summary>
    /// Returns whether one settlement retains this exact prepared request.
    /// </summary>
    public bool Accepts(PackageHouseSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        return ReferenceEquals(settlement.Result.Request, Request);
    }
}

/// <summary>
/// Closed resource-free result of one PackageHouse version-population
/// settlement.
/// </summary>
public abstract class PackageHouseVersionPopulationResult
{
    private PackageHouseVersionPopulationResult(
        PackageHouseVersionPopulationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Evidence = evidence;
    }

    public PackageHouseVersionPopulationEvidence Evidence { get; }

    public PackageHouseVersionPopulationRequest Request => Evidence.Request;

    public sealed class Available : PackageHouseVersionPopulationResult
    {
        internal Available(
            PackageHouseVersionPopulationEvidence evidence)
            : base(evidence)
        {
            PackageVersionDiscoveryResult discovery =
                evidence.Discovery
                ?? throw new ArgumentException(
                    "An available population requires completed discovery.",
                    nameof(evidence));
            if (discovery.State != PackageVersionDiscoveryState.Authoritative
                || !discovery.Contract.SupportsCompleteVersionEnumeration
                || evidence.HasOperationTimeout)
            {
                throw new ArgumentException(
                    "An available population requires complete authoritative discovery without an operation timeout.",
                    nameof(evidence));
            }

            Vector = PackageVersionVector.Create(
                evidence.Request.Range,
                discovery.Versions,
                evidence.Request.IncludePrerelease,
                evidence.Request.Policy);
        }

        public PackageVersionVector Vector { get; }

        public PackageVersionMajorRepresentativeProjection
            ProjectMajorRepresentatives(
                PackageVersionMajorRepresentativePolicy policy) =>
            Vector.ProjectMajorRepresentatives(policy);

        public PackageHouseVersionPopulationCell SelectCell(
            PackageVersionAddress address)
        {
            ArgumentNullException.ThrowIfNull(address);
            if (!Vector.Addresses.Any(candidate =>
                    ReferenceEquals(candidate, address)))
            {
                throw new ArgumentException(
                    "The selected address belongs to another package version population.",
                    nameof(address));
            }

            return new(this, address);
        }
    }

    public sealed class NotFound : PackageHouseVersionPopulationResult
    {
        internal NotFound(
            PackageHouseVersionPopulationEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class NoMatch : PackageHouseVersionPopulationResult
    {
        internal NoMatch(
            PackageHouseVersionPopulationEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Incomplete : PackageHouseVersionPopulationResult
    {
        internal Incomplete(
            PackageHouseVersionPopulationEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Rejected : PackageHouseVersionPopulationResult
    {
        internal Rejected(
            PackageHouseVersionPopulationEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Unavailable : PackageHouseVersionPopulationResult
    {
        internal Unavailable(
            PackageHouseVersionPopulationEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }

    public sealed class Failed : PackageHouseVersionPopulationResult
    {
        internal Failed(
            PackageHouseVersionPopulationEvidence evidence,
            InertString reason)
            : base(evidence)
        {
            Reason = PackageHouseContractValidation.RequireReason(reason);
        }

        public InertString Reason { get; }
    }
}
