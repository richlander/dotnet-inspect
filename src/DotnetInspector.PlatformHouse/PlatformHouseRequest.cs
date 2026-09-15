namespace DotnetInspector.PlatformHouse;

/// <summary>The complete immutable context for one PlatformHouse operation.</summary>
public sealed class PlatformHouseRequest
{
    public PlatformHouseRequest(
        PlatformHouseRequestIdentity identity,
        PlatformTargetDemand target,
        PlatformHouseRequestOrigin origin,
        PlatformHouseOperation operation,
        PlatformSourcePlan sources,
        PlatformHouseWorkBudget work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(work);

        Identity = identity;
        Target = target;
        Origin = origin;
        Operation = operation;
        Sources = sources;
        Work = work;
        CancellationToken = cancellationToken;
        Snapshot = new PlatformHouseRequestSnapshot(
            identity,
            target,
            origin,
            operation.Snapshot,
            sources,
            work);
    }

    public PlatformHouseRequestIdentity Identity { get; }
    public PlatformTargetDemand Target { get; }
    public PlatformHouseRequestOrigin Origin { get; }
    public PlatformHouseOperation Operation { get; }
    public PlatformSourcePlan Sources { get; }
    public PlatformHouseWorkBudget Work { get; }
    public CancellationToken CancellationToken { get; }
    public PlatformHouseRequestSnapshot Snapshot { get; }
}

/// <summary>
/// Resource-free request evidence retained by outcomes and receipts.
/// Cancellation remains operation state and is deliberately omitted.
/// </summary>
public sealed class PlatformHouseRequestSnapshot
{
    internal PlatformHouseRequestSnapshot(
        PlatformHouseRequestIdentity identity,
        PlatformTargetDemand target,
        PlatformHouseRequestOrigin origin,
        PlatformHouseOperationSnapshot operation,
        PlatformSourcePlan sources,
        PlatformHouseWorkBudget work)
    {
        Identity = identity;
        Target = target;
        Origin = origin;
        Operation = operation;
        Sources = sources;
        Work = work;
    }

    public PlatformHouseRequestIdentity Identity { get; }
    public PlatformTargetDemand Target { get; }
    public PlatformHouseRequestOrigin Origin { get; }
    public PlatformHouseOperationSnapshot Operation { get; }
    public PlatformSourcePlan Sources { get; }
    public PlatformHouseWorkBudget Work { get; }
}

/// <summary>Why a PlatformHouse request or retained settlement was rejected.</summary>
public enum PlatformHouseRejectionKind
{
    InvalidRequest,
    InvalidSourcePlan,
    InvalidTargetCorrespondence,
    InvalidCandidate,
    InvalidOwnerResult,
    InvalidBudget,
    InvalidRetainedEvidence,
}

/// <summary>Typed rejection evidence retained by a House outcome.</summary>
public abstract class PlatformHouseRejection
{
    private protected PlatformHouseRejection()
    {
    }

    public sealed class TargetDiscoveryCapabilityNotAuthorized :
        PlatformHouseRejection
    {
        public TargetDiscoveryCapabilityNotAuthorized(
            PlatformSourceCapabilityIdentity capability)
        {
            ArgumentNullException.ThrowIfNull(capability);
            Capability = capability;
        }

        public PlatformSourceCapabilityIdentity Capability { get; }
    }

    public sealed class OwnerEvidence : PlatformHouseRejection
    {
        public OwnerEvidence(
            PlatformHouseRejectionKind kind,
            PlatformHouseTerminalEvidenceIdentity evidence)
        {
            if (!Enum.IsDefined(kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            ArgumentNullException.ThrowIfNull(evidence);
            Kind = kind;
            Evidence = evidence;
        }

        public PlatformHouseRejectionKind Kind { get; }
        public PlatformHouseTerminalEvidenceIdentity Evidence { get; }
    }
}

/// <summary>Pure validation result for one request before any source work.</summary>
public abstract class PlatformHouseRequestValidation
{
    private protected PlatformHouseRequestValidation()
    {
    }

    public sealed class Accepted : PlatformHouseRequestValidation
    {
        internal Accepted(PlatformHouseRequestSnapshot request) =>
            Request = request;

        public PlatformHouseRequestSnapshot Request { get; }
    }

    public sealed class Rejected : PlatformHouseRequestValidation
    {
        internal Rejected(PlatformHouseRejection rejection) =>
            Rejection = rejection;

        public PlatformHouseRejection Rejection { get; }
    }

    public static PlatformHouseRequestValidation Validate(
        PlatformHouseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Target is PlatformTargetDemand.Selecting selecting)
        {
            foreach (PlatformSourceCapabilityIdentity capability
                in selecting.DiscoveryCapabilities)
            {
                if (!request.Sources.Authorizes(
                        PlatformSourceFacet.TargetDiscovery,
                        capability))
                {
                    return new Rejected(
                        new PlatformHouseRejection
                            .TargetDiscoveryCapabilityNotAuthorized(capability));
                }
            }
        }

        return new Accepted(request.Snapshot);
    }
}
