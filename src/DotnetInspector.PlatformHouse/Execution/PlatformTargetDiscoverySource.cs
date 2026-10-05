using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// One source-issued exact target association. It remains callback-scoped and
/// must not enter a House outcome or receipt.
/// </summary>
public abstract class PlatformTargetDiscoveryCandidate
{
    private protected PlatformTargetDiscoveryCandidate(
        PlatformFamilyTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        Target = target;
    }

    public PlatformFamilyTarget Target { get; }
}

/// <summary>One typed source association paired with its exact target.</summary>
public sealed class PlatformTargetDiscoveryCandidate<TAssociation> :
    PlatformTargetDiscoveryCandidate
    where TAssociation : notnull
{
    public PlatformTargetDiscoveryCandidate(
        PlatformFamilyTarget target,
        TAssociation association)
        : base(target)
    {
        ArgumentNullException.ThrowIfNull(association);
        Association = association;
    }

    public TAssociation Association { get; }
}

/// <summary>One ephemeral result from an authorized discovery capability.</summary>
public abstract class PlatformTargetDiscoveryAttempt
{
    private protected PlatformTargetDiscoveryAttempt(
        PlatformSourceContribution contribution)
    {
        ArgumentNullException.ThrowIfNull(contribution);
        if (contribution.Facet != PlatformSourceFacet.TargetDiscovery)
        {
            throw new ArgumentException(
                "A target-discovery attempt requires target-discovery evidence.",
                nameof(contribution));
        }
        Contribution = contribution;
    }

    public PlatformSourceContribution Contribution { get; }

    /// <summary>
    /// One complete inventory with source-specific associations kept beside
    /// its resource-free contribution.
    /// </summary>
    public sealed class Succeeded : PlatformTargetDiscoveryAttempt
    {
        public Succeeded(
            PlatformSourceContribution.TargetDiscovery contribution,
            IEnumerable<PlatformTargetDiscoveryCandidate> candidates)
            : base(contribution)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            PlatformTargetDiscoveryCandidate[] snapshot = [.. candidates];
            if (snapshot.Length != contribution.Candidates.Count)
            {
                throw new ArgumentException(
                    "Every discovered target requires one aligned source association.",
                    nameof(candidates));
            }
            for (int index = 0; index < snapshot.Length; index++)
            {
                PlatformTargetDiscoveryCandidate candidate = snapshot[index];
                ArgumentNullException.ThrowIfNull(candidate, nameof(candidates));
                if (candidate.Target != contribution.Candidates[index])
                {
                    throw new ArgumentException(
                        $"Source association at position {index} does not match its resource-free target.",
                        nameof(candidates));
                }
            }

            Candidates = Array.AsReadOnly(snapshot);
        }

        public IReadOnlyList<PlatformTargetDiscoveryCandidate> Candidates
        {
            get;
        }
    }

    /// <summary>One resource-free terminal discovery result.</summary>
    public sealed class NotSucceeded : PlatformTargetDiscoveryAttempt
    {
        public NotSucceeded(
            PlatformSourceContribution contribution,
            PlatformHouseRejectionKind? rejectionKind = null)
            : base(contribution)
        {
            bool rejected =
                contribution is PlatformSourceContribution.Rejected;
            if (contribution
                    is not PlatformSourceContribution.Unavailable
                        and not PlatformSourceContribution.Rejected
                        and not PlatformSourceContribution.Incomplete
                        and not PlatformSourceContribution.Failed
                || rejected != rejectionKind.HasValue
                || rejectionKind is { } kind && !Enum.IsDefined(kind))
            {
                throw new ArgumentException(
                    "A terminal attempt requires a matching terminal contribution and rejection classification.",
                    nameof(contribution));
            }
            RejectionKind = rejectionKind;
        }

        public PlatformHouseRejectionKind? RejectionKind { get; }
    }
}

public delegate ValueTask<PlatformTargetDiscoveryAttempt>
    PlatformTargetDiscoveryOperation(
        PlatformHouseRequest request,
        PlatformHouseWorkBudget remainingWork);

/// <summary>One lazily invoked target-discovery capability.</summary>
public sealed class PlatformTargetDiscoverySource
{
    private readonly PlatformTargetDiscoveryOperation _discover;

    public PlatformTargetDiscoverySource(
        PlatformSourceCapabilityIdentity capability,
        PlatformTargetDiscoveryOperation discover,
        PlatformSourceAssociationRouteIdentity? associationRoute = null)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(discover);
        Capability = capability;
        AssociationRoute = associationRoute;
        _discover = discover;
    }

    public PlatformSourceCapabilityIdentity Capability { get; }
    public PlatformSourceAssociationRouteIdentity? AssociationRoute { get; }

    internal ValueTask<PlatformTargetDiscoveryAttempt> DiscoverAsync(
        PlatformHouseRequest request,
        PlatformHouseWorkBudget remainingWork) =>
        _discover(request, remainingWork);
}
