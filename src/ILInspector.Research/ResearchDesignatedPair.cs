using System.Collections.Immutable;

namespace ILInspector.Research;

/// <summary>One owner-issued association of two explicitly selected physical methods.</summary>
public sealed class ResearchDesignatedPair
{
    internal ResearchDesignatedPair(
        ResearchTargetResolution resolution,
        ResearchTargetAttempt before,
        ResearchTargetAttempt after)
    {
        Resolution = resolution;
        Before = before;
        After = after;
    }

    public ResearchTargetResolution Resolution { get; }

    public ResearchComparisonOperationId Operation => Resolution.Operation;

    public ResearchComparisonQuestionId Question => Before.Request.Question;

    public ResearchTargetAttempt Before { get; }

    public ResearchTargetAttempt After { get; }
}

/// <summary>Why an explicit pair does not have a valid input association.</summary>
public enum ResearchDesignatedPairRejectionKind
{
    UnsupportedProfile,
    ForeignResolution,
    InvalidIdentityClosure,
    MissingEndpoint,
    ForeignAttempt,
    CrossQuestion,
    WrongSide,
}

/// <summary>Why an associated endpoint cannot designate a physical method.</summary>
public enum ResearchDesignatedPairUnavailableKind
{
    TargetUnavailable,
    DomainSideBlocked,
    EndpointAddressUnavailable,
}

/// <summary>One endpoint's original evidence and its designation failure.</summary>
public sealed class ResearchDesignatedPairUnavailable
{
    internal ResearchDesignatedPairUnavailable(
        ResearchDesignatedPairUnavailableKind kind,
        ResearchTargetAttempt attempt,
        ResearchTargetDomainSideCensus census)
    {
        Kind = kind;
        Attempt = attempt;
        Census = census;
    }

    public ResearchDesignatedPairUnavailableKind Kind { get; }

    public ResearchComparisonSide Side => Attempt.Request.Side;

    public ResearchTargetAttempt Attempt { get; }

    public ResearchTargetDomainSideCensus Census { get; }
}

/// <summary>The inert, closed result of explicit method-pair admission.</summary>
public abstract class ResearchDesignatedPairOutcome
{
    private protected ResearchDesignatedPairOutcome()
    {
    }

    public sealed class Admitted : ResearchDesignatedPairOutcome
    {
        internal Admitted(ResearchDesignatedPair pair) => Pair = pair;

        public ResearchDesignatedPair Pair { get; }
    }

    public sealed class Rejected : ResearchDesignatedPairOutcome
    {
        internal Rejected(ResearchDesignatedPairRejectionKind kind) => Kind = kind;

        public ResearchDesignatedPairRejectionKind Kind { get; }
    }

    public sealed class Unavailable : ResearchDesignatedPairOutcome
    {
        internal Unavailable(ImmutableArray<ResearchDesignatedPairUnavailable> endpoints)
            => Endpoints = endpoints;

        public ImmutableArray<ResearchDesignatedPairUnavailable> Endpoints { get; }
    }
}

/// <summary>Admits explicit physical method pairs without changing correspondence.</summary>
public static class ResearchDesignatedPairAdmission
{
    public static ResearchDesignatedPairOutcome Admit(
        ResearchAdmittedPopulation population,
        ResearchTargetResolution resolution,
        ResearchTargetAttempt? before,
        ResearchTargetAttempt? after)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(resolution);

        if (population.Profile != ResearchComparisonProfile.ImplementationComparison)
            return new ResearchDesignatedPairOutcome.Rejected(
                ResearchDesignatedPairRejectionKind.UnsupportedProfile);
        if (!ReferenceEquals(population.Operation, resolution.Operation))
            return new ResearchDesignatedPairOutcome.Rejected(
                ResearchDesignatedPairRejectionKind.ForeignResolution);
        if (!ResearchProducerSessionValidator.HasValidIdentityClosure(population, resolution))
            return new ResearchDesignatedPairOutcome.Rejected(
                ResearchDesignatedPairRejectionKind.InvalidIdentityClosure);
        if (before is null || after is null)
            return new ResearchDesignatedPairOutcome.Rejected(
                ResearchDesignatedPairRejectionKind.MissingEndpoint);
        if (!Contains(resolution, before) || !Contains(resolution, after))
            return new ResearchDesignatedPairOutcome.Rejected(
                ResearchDesignatedPairRejectionKind.ForeignAttempt);
        if (!ReferenceEquals(before.Request.Question, after.Request.Question))
            return new ResearchDesignatedPairOutcome.Rejected(
                ResearchDesignatedPairRejectionKind.CrossQuestion);
        if (before.Request.Side != ResearchComparisonSide.Before
            || after.Request.Side != ResearchComparisonSide.After)
            return new ResearchDesignatedPairOutcome.Rejected(
                ResearchDesignatedPairRejectionKind.WrongSide);

        var unavailable = ImmutableArray.CreateBuilder<ResearchDesignatedPairUnavailable>();
        CheckEndpoint(resolution, before, unavailable);
        CheckEndpoint(resolution, after, unavailable);
        return unavailable.Count == 0
            ? new ResearchDesignatedPairOutcome.Admitted(
                new ResearchDesignatedPair(resolution, before, after))
            : new ResearchDesignatedPairOutcome.Unavailable(unavailable.ToImmutable());
    }

    static bool Contains(ResearchTargetResolution resolution, ResearchTargetAttempt attempt)
        => resolution.TryGetAttempt(attempt.Request.Id, out ResearchTargetAttempt? retained)
            && ReferenceEquals(retained, attempt);

    static void CheckEndpoint(
        ResearchTargetResolution resolution,
        ResearchTargetAttempt attempt,
        ImmutableArray<ResearchDesignatedPairUnavailable>.Builder unavailable)
    {
        ResearchTargetDomainSideCensus census = resolution.Censuses.Single(
            census => ReferenceEquals(census.DomainId, attempt.Request.Domain)
                && census.Side == attempt.Request.Side);
        ResearchDesignatedPairUnavailableKind? kind =
            attempt.Outcome is not ResearchTargetOutcome.Resolved resolved
                ? ResearchDesignatedPairUnavailableKind.TargetUnavailable
                : census.Health != ResearchTargetCensusHealth.Healthy
                    ? ResearchDesignatedPairUnavailableKind.DomainSideBlocked
                    : resolved.Role == ResearchTargetRelationshipRole.None
                        || resolved.Address is null
                        ? ResearchDesignatedPairUnavailableKind.EndpointAddressUnavailable
                        : null;
        if (kind is { } reason)
            unavailable.Add(new ResearchDesignatedPairUnavailable(reason, attempt, census));
    }
}
