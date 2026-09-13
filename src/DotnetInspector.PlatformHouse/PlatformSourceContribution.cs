using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>The phase represented by one source contribution.</summary>
public enum PlatformSourceContributionKind
{
    TargetDiscovery,
    Realization,
    Documentation,
    Unavailable,
    Rejected,
    Failed,
    Incomplete,
}

/// <summary>Whether one realized source contribution is complete for its stated demand.</summary>
public enum PlatformSourceContributionCompleteness
{
    Authoritative,
    Partial,
}

/// <summary>Why an authorized source did not supply the requested value.</summary>
public enum PlatformSourceUnavailabilityKind
{
    Absent,
    Unavailable,
}

/// <summary>
/// One resource-free source contribution bound to the exact House request.
/// Source-owner results remain live outside the receipt and are joined by
/// owner-issued identities.
/// </summary>
public abstract class PlatformSourceContribution
{
    private protected PlatformSourceContribution(
        PlatformSourceContributionKind kind,
        PlatformSourceFacet facet,
        PlatformSourceCapabilityIdentity capability,
        PlatformHouseRequestSnapshot request,
        PlatformSourceGeneration generation,
        PlatformFamilyTarget? exactTarget,
        PlatformSourceEvidenceIdentity evidence)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(facet))
            throw new ArgumentOutOfRangeException(nameof(facet));
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(evidence);
        if (facet == PlatformSourceFacet.TargetDiscovery)
        {
            if (request.Target is not PlatformTargetDemand.Selecting)
            {
                throw new ArgumentException(
                    "Target discovery requires a selecting target demand.",
                    nameof(request));
            }
            if (exactTarget is not null)
            {
                throw new ArgumentException(
                    "Target-discovery contributions precede exact target settlement.",
                    nameof(exactTarget));
            }
        }
        else
        {
            ArgumentNullException.ThrowIfNull(exactTarget);
            ValidateCorrespondence(
                exactTarget,
                request.Target,
                nameof(exactTarget));
        }

        Kind = kind;
        Facet = facet;
        Capability = capability;
        Request = request;
        Generation = generation;
        ExactTarget = exactTarget;
        Evidence = evidence;
    }

    public PlatformSourceContributionKind Kind { get; }
    public PlatformSourceFacet Facet { get; }
    public PlatformSourceCapabilityIdentity Capability { get; }
    public PlatformHouseRequestSnapshot Request { get; }
    public PlatformTargetDemand RequestedTarget => Request.Target;
    public PlatformSourceGeneration Generation { get; }
    public PlatformFamilyTarget? ExactTarget { get; }
    public PlatformSourceEvidenceIdentity Evidence { get; }

    internal virtual IReadOnlyList<PlatformFamilyTarget> DiscoveredCandidates =>
        Array.Empty<PlatformFamilyTarget>();
    internal virtual PlatformViewDemand? SuppliedView => null;
    internal virtual PlatformPopulationDemand? SuppliedPopulation => null;
    internal virtual PlatformSourceContributionCompleteness? Completeness => null;

    public sealed class TargetDiscovery : PlatformSourceContribution
    {
        public TargetDiscovery(
            PlatformSourceCapabilityIdentity capability,
            PlatformHouseRequestSnapshot request,
            PlatformSourceGeneration generation,
            IEnumerable<PlatformFamilyTarget> candidates,
            PlatformSourceEvidenceIdentity evidence)
            : base(
                PlatformSourceContributionKind.TargetDiscovery,
                PlatformSourceFacet.TargetDiscovery,
                capability,
                request,
                generation,
                exactTarget: null,
                evidence)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            var selecting = (PlatformTargetDemand.Selecting)request.Target;
            PlatformFamilyTarget[] snapshot = [.. candidates];
            var seen = new HashSet<PlatformFamilyTarget>();
            for (int index = 0; index < snapshot.Length; index++)
            {
                PlatformFamilyTarget candidate = snapshot[index];
                ArgumentNullException.ThrowIfNull(candidate, nameof(candidates));
                ValidateCorrespondence(
                    candidate,
                    selecting,
                    nameof(candidates));
                if (!seen.Add(candidate))
                {
                    throw new ArgumentException(
                        $"Candidate at position {index} appears more than once.",
                        nameof(candidates));
                }
            }

            Candidates = Array.AsReadOnly(snapshot);
        }

        public IReadOnlyList<PlatformFamilyTarget> Candidates { get; }

        internal override IReadOnlyList<PlatformFamilyTarget>
            DiscoveredCandidates => Candidates;
    }

    public sealed class Realization : PlatformSourceContribution
    {
        public Realization(
            PlatformSourceFacet facet,
            PlatformSourceCapabilityIdentity capability,
            PlatformHouseRequestSnapshot request,
            PlatformSourceGeneration generation,
            PlatformFamilyTarget target,
            PlatformSourceCoordinateIdentity coordinate,
            PlatformTargetCorrespondenceIdentity correspondence,
            PlatformPopulationDemand suppliedPopulation,
            PlatformSourceContributionCompleteness completeness,
            PlatformSourceEvidenceIdentity evidence)
            : base(
                PlatformSourceContributionKind.Realization,
                facet,
                capability,
                request,
                generation,
                target,
                evidence)
        {
            if (facet is not PlatformSourceFacet.Reference
                and not PlatformSourceFacet.Implementation)
            {
                throw new ArgumentException(
                    "A realization contribution must use a reference or implementation facet.",
                    nameof(facet));
            }

            ArgumentNullException.ThrowIfNull(coordinate);
            ArgumentNullException.ThrowIfNull(correspondence);
            ArgumentNullException.ThrowIfNull(suppliedPopulation);
            if (!Enum.IsDefined(completeness))
                throw new ArgumentOutOfRangeException(nameof(completeness));

            Coordinate = coordinate;
            Correspondence = correspondence;
            Population = suppliedPopulation;
            RealizationCompleteness = completeness;
        }

        public PlatformFamilyTarget Target => ExactTarget!;
        public PlatformSourceCoordinateIdentity Coordinate { get; }
        public PlatformTargetCorrespondenceIdentity Correspondence { get; }
        public PlatformViewDemand View =>
            Facet == PlatformSourceFacet.Reference
                ? PlatformViewDemand.Reference
                : PlatformViewDemand.Implementation;
        public PlatformPopulationDemand Population { get; }
        public PlatformSourceContributionCompleteness RealizationCompleteness
        {
            get;
        }

        internal override PlatformViewDemand? SuppliedView => View;
        internal override PlatformPopulationDemand? SuppliedPopulation =>
            Population;
        internal override PlatformSourceContributionCompleteness? Completeness =>
            RealizationCompleteness;
    }

    public sealed class Documentation : PlatformSourceContribution
    {
        public Documentation(
            PlatformSourceFacet facet,
            PlatformSourceCapabilityIdentity capability,
            PlatformHouseRequestSnapshot request,
            PlatformSourceGeneration generation,
            PlatformFamilyTarget target,
            PlatformSourceEvidenceIdentity evidence)
            : base(
                PlatformSourceContributionKind.Documentation,
                facet,
                capability,
                request,
                generation,
                target,
                evidence)
        {
            if (facet is not PlatformSourceFacet.CompiledXml
                and not PlatformSourceFacet.SourceDerivedDocumentation)
            {
                throw new ArgumentException(
                    "A documentation contribution must use a documentation facet.",
                    nameof(facet));
            }
        }

        public PlatformFamilyTarget Target => ExactTarget!;
    }

    public sealed class Unavailable : PlatformSourceContribution
    {
        public Unavailable(
            PlatformSourceFacet facet,
            PlatformSourceCapabilityIdentity capability,
            PlatformHouseRequestSnapshot request,
            PlatformSourceGeneration generation,
            PlatformFamilyTarget? exactTarget,
            PlatformSourceUnavailabilityKind reason,
            PlatformSourceEvidenceIdentity evidence)
            : base(
                PlatformSourceContributionKind.Unavailable,
                facet,
                capability,
                request,
                generation,
                exactTarget,
                evidence)
        {
            if (!Enum.IsDefined(reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }

        public PlatformSourceUnavailabilityKind Reason { get; }
    }

    public sealed class Rejected : PlatformSourceContribution
    {
        public Rejected(
            PlatformSourceFacet facet,
            PlatformSourceCapabilityIdentity capability,
            PlatformHouseRequestSnapshot request,
            PlatformSourceGeneration generation,
            PlatformFamilyTarget? exactTarget,
            PlatformSourceEvidenceIdentity evidence)
            : base(
                PlatformSourceContributionKind.Rejected,
                facet,
                capability,
                request,
                generation,
                exactTarget,
                evidence)
        {
        }
    }

    public sealed class Failed : PlatformSourceContribution
    {
        public Failed(
            PlatformSourceFacet facet,
            PlatformSourceCapabilityIdentity capability,
            PlatformHouseRequestSnapshot request,
            PlatformSourceGeneration generation,
            PlatformFamilyTarget? exactTarget,
            PlatformSourceEvidenceIdentity evidence)
            : base(
                PlatformSourceContributionKind.Failed,
                facet,
                capability,
                request,
                generation,
                exactTarget,
                evidence)
        {
        }
    }

    public sealed class Incomplete : PlatformSourceContribution
    {
        public Incomplete(
            PlatformSourceFacet facet,
            PlatformSourceCapabilityIdentity capability,
            PlatformHouseRequestSnapshot request,
            PlatformSourceGeneration generation,
            PlatformFamilyTarget? exactTarget,
            PlatformSourceEvidenceIdentity evidence)
            : base(
                PlatformSourceContributionKind.Incomplete,
                facet,
                capability,
                request,
                generation,
                exactTarget,
                evidence)
        {
        }
    }

    static void ValidateCorrespondence(
        PlatformFamilyTarget target,
        PlatformTargetDemand demand,
        string parameterName)
    {
        if (target.Family != demand.Family
            || target.TargetFramework != demand.TargetFramework)
        {
            throw new ArgumentException(
                "The source target does not correspond to the requested target demand.",
                parameterName);
        }
    }
}
