using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>
/// One source participant identity used by Integration Census addressing.
/// </summary>
public sealed class IntegrationSourceParticipantIdentity :
    IEquatable<IntegrationSourceParticipantIdentity>
{
    IntegrationSourceParticipantIdentity(
        RealizedMemberCoordinate? coordinate,
        AssemblyAcquisitionRegistration? registration,
        AssemblyReferenceIdentity assembly)
    {
        Coordinate = coordinate;
        Registration = registration;
        Assembly = assembly;
    }

    public RealizedMemberCoordinate? Coordinate { get; }
    public AssemblyAcquisitionRegistration? Registration { get; }
    public AssemblyReferenceIdentity Assembly { get; }
    public bool IsPortable => Coordinate is not null;

    public static IntegrationSourceParticipantIdentity Portable(
        RealizedMemberCoordinate coordinate,
        AssemblyReferenceIdentity assembly)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(assembly);
        return new(coordinate, registration: null, assembly);
    }

    public static IntegrationSourceParticipantIdentity Workspace(
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity assembly)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(assembly);
        return new(coordinate: null, registration, assembly);
    }

    public bool Equals(IntegrationSourceParticipantIdentity? other) =>
        other is not null
        && IsPortable == other.IsPortable
        && Assembly.IsEquivalentTo(other.Assembly)
        && (IsPortable
            ? Coordinate == other.Coordinate
            : ReferenceEquals(Registration, other.Registration));

    public override bool Equals(object? obj) =>
        obj is IntegrationSourceParticipantIdentity other
        && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            IsPortable ? 0 : 1,
            Coordinate,
            Registration,
            AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(
                Assembly));
}

/// <summary>One typed source element that supplied candidate evidence.</summary>
public abstract record IntegrationCandidateSourceElement
{
    private protected IntegrationCandidateSourceElement()
    {
    }

    public abstract MetadataTypeDefinitionName SourceType { get; }

    public sealed record Type : IntegrationCandidateSourceElement
    {
        public Type(MetadataTypeDefinitionName name)
        {
            ArgumentNullException.ThrowIfNull(name);
            Name = name;
        }

        public MetadataTypeDefinitionName Name { get; }
        public override MetadataTypeDefinitionName SourceType => Name;
    }

    public sealed record Member : IntegrationCandidateSourceElement
    {
        public Member(
            MetadataTypeDefinitionName declaringType,
            MemberAnchor anchor)
        {
            ArgumentNullException.ThrowIfNull(declaringType);
            ArgumentNullException.ThrowIfNull(anchor);
            if (!string.Equals(
                    declaringType.ToMetadataFullName(),
                    anchor.TypeFullName,
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The member anchor must name its structured declaring type.",
                    nameof(anchor));
            }

            DeclaringType = declaringType;
            Anchor = anchor;
        }

        public MetadataTypeDefinitionName DeclaringType { get; }
        public MemberAnchor Anchor { get; }
        public override MetadataTypeDefinitionName SourceType => DeclaringType;
    }
}

/// <summary>One Integration-owned source identity for candidate evidence.</summary>
public sealed record IntegrationCandidateSourceIdentity
{
    public IntegrationCandidateSourceIdentity(
        IntegrationSourceParticipantIdentity participant,
        IntegrationCandidateSourceElement element)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(element);
        Participant = participant;
        Element = element;
    }

    public IntegrationSourceParticipantIdentity Participant { get; }
    public IntegrationCandidateSourceElement Element { get; }
    public MetadataTypeDefinitionName SourceType => Element.SourceType;
    public bool IsPortable => Participant.IsPortable;
}

/// <summary>
/// One structured peer named by candidate evidence before binding.
/// </summary>
public abstract class IntegrationCandidatePeerIdentity :
    IEquatable<IntegrationCandidatePeerIdentity>
{
    private protected IntegrationCandidatePeerIdentity()
    {
    }

    public abstract MetadataTypeDefinitionName Type { get; }

    public bool Equals(IntegrationCandidatePeerIdentity? other) =>
        ReferenceEquals(this, other)
        || other is not null
            && GetType() == other.GetType()
            && EqualsCore(other);

    public override bool Equals(object? obj) =>
        obj is IntegrationCandidatePeerIdentity other
        && Equals(other);

    public abstract override int GetHashCode();

    private protected abstract bool EqualsCore(
        IntegrationCandidatePeerIdentity other);

    public sealed class NamedType : IntegrationCandidatePeerIdentity
    {
        public NamedType(MetadataNamedTypeReference reference)
        {
            ArgumentNullException.ThrowIfNull(reference);
            Reference = reference;
        }

        public MetadataNamedTypeReference Reference { get; }
        public override MetadataTypeDefinitionName Type => Reference.Type;

        private protected override bool EqualsCore(
            IntegrationCandidatePeerIdentity other) =>
            MetadataNamedTypeReference.EquivalentComparer.Equals(
                Reference,
                ((NamedType)other).Reference);

        public override int GetHashCode() =>
            HashCode.Combine(
                0,
                MetadataNamedTypeReference.EquivalentComparer.GetHashCode(
                    Reference));
    }

    public sealed class PolicyTarget : IntegrationCandidatePeerIdentity
    {
        public PolicyTarget(IntegrationOpportunityTarget target)
        {
            ArgumentNullException.ThrowIfNull(target);
            Target = target;
        }

        public IntegrationOpportunityTarget Target { get; }
        public override MetadataTypeDefinitionName Type => Target.Type;

        private protected override bool EqualsCore(
            IntegrationCandidatePeerIdentity other)
        {
            IntegrationOpportunityTarget target =
                ((PolicyTarget)other).Target;
            return StringComparer.OrdinalIgnoreCase.Equals(
                    Target.AssemblyName,
                    target.AssemblyName)
                && Target.Type == target.Type;
        }

        public override int GetHashCode() =>
            HashCode.Combine(
                1,
                StringComparer.OrdinalIgnoreCase.GetHashCode(
                    Target.AssemblyName),
                Target.Type);
    }
}

/// <summary>
/// Stable candidate identity issued before peer binding and universe
/// disposition.
/// </summary>
public sealed class IntegrationCandidateIdentity :
    IEquatable<IntegrationCandidateIdentity>
{
    public IntegrationCandidateIdentity(
        InspectionGraphRelationshipDescriptor relationship,
        IntegrationConceptDescriptor concept,
        IntegrationCandidateSourceIdentity source,
        IntegrationCandidatePeerIdentity peer)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        ArgumentNullException.ThrowIfNull(concept);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(peer);
        if (!IntegrationAnalysisCatalog.ProducerPolicies.Any(binding =>
                ReferenceEquals(binding.Relationship, relationship)
                && binding.Policy.Concepts.Contains(
                    concept,
                    ReferenceEqualityComparer.Instance)))
        {
            throw new ArgumentException(
                "The relationship and concept are not a configured Integration candidate pair.",
                nameof(relationship));
        }
        if (ReferenceEquals(
                relationship,
                InspectionGraphIntegrationsCatalog.IntegrationObserved)
            && peer is not IntegrationCandidatePeerIdentity.NamedType)
        {
            throw new ArgumentException(
                "Observed Integration evidence requires a structured named peer Type.",
                nameof(peer));
        }
        if (ReferenceEquals(
                relationship,
                InspectionGraphIntegrationsCatalog.IntegrationOpportunity)
            && (source.Element is not IntegrationCandidateSourceElement.Type
                || peer is not IntegrationCandidatePeerIdentity.PolicyTarget))
        {
            throw new ArgumentException(
                "Integration opportunity evidence requires a Type source and policy-issued target.",
                nameof(source));
        }

        Relationship = relationship;
        Concept = concept;
        Source = source;
        Peer = peer;
    }

    public InspectionGraphRelationshipDescriptor Relationship { get; }
    public IntegrationConceptDescriptor Concept { get; }
    public IntegrationCandidateSourceIdentity Source { get; }
    public IntegrationCandidatePeerIdentity Peer { get; }
    public bool IsPortable => Source.IsPortable;

    public bool Equals(IntegrationCandidateIdentity? other) =>
        other is not null
        && ReferenceEquals(Relationship, other.Relationship)
        && ReferenceEquals(Concept, other.Concept)
        && Source == other.Source
        && Peer.Equals(other.Peer);

    public override bool Equals(object? obj) =>
        obj is IntegrationCandidateIdentity other
        && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Relationship, Concept, Source, Peer);
}

public interface IIntegrationSourceParticipantRejection
{
}

public interface IIntegrationSourceParticipantFailure
{
}

public interface IIntegrationProducerPolicyUnavailable
{
}

public interface IIntegrationProducerPolicyFailure
{
}

public interface IIntegrationCandidateFailure
{
}

public interface IIntegrationBindingContextIdentity
{
}

/// <summary>
/// One source participant and the binding contexts in which its evidence is
/// evaluated.
/// </summary>
public sealed class IntegrationSourceBindingContextIncidence
{
    public IntegrationSourceBindingContextIncidence(
        IntegrationSourceParticipantIdentity participant,
        IEnumerable<IIntegrationBindingContextIdentity> bindingContexts)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(bindingContexts);
        Participant = participant;
        BindingContexts = [.. bindingContexts];
        if (BindingContexts.IsEmpty)
        {
            throw new ArgumentException(
                "A source participant requires at least one incident context.",
                nameof(bindingContexts));
        }
        var identities =
            new HashSet<IIntegrationBindingContextIdentity>();
        foreach (IIntegrationBindingContextIdentity context in BindingContexts)
        {
            if (context is null)
            {
                throw new ArgumentException(
                    "Context incidence cannot contain null.",
                    nameof(bindingContexts));
            }
            if (!identities.Add(context))
            {
                throw new ArgumentException(
                    "Context incidence cannot contain duplicate identities.",
                    nameof(bindingContexts));
            }
        }
    }

    public IntegrationSourceParticipantIdentity Participant { get; }
    public ImmutableArray<IIntegrationBindingContextIdentity> BindingContexts
        { get; }
}

/// <summary>
/// Immutable owner-issued binding-context roster and source incidence.
/// </summary>
public sealed class IntegrationBindingContextAccess
{
    public IntegrationBindingContextAccess(
        IEnumerable<IIntegrationBindingContextIdentity> bindingContexts,
        IEnumerable<IntegrationSourceBindingContextIncidence> sourceIncidence)
    {
        ArgumentNullException.ThrowIfNull(bindingContexts);
        ArgumentNullException.ThrowIfNull(sourceIncidence);

        BindingContexts = [.. bindingContexts];
        var contextIdentities =
            new HashSet<IIntegrationBindingContextIdentity>();
        foreach (IIntegrationBindingContextIdentity context in BindingContexts)
        {
            if (context is null)
            {
                throw new ArgumentException(
                    "The binding-context roster cannot contain null.",
                    nameof(bindingContexts));
            }
            if (!contextIdentities.Add(context))
            {
                throw new ArgumentException(
                    "The binding-context roster cannot contain duplicate identities.",
                    nameof(bindingContexts));
            }
        }

        ImmutableArray<IntegrationSourceBindingContextIncidence> incidence =
            [.. sourceIncidence];
        var participants =
            new HashSet<IntegrationSourceParticipantIdentity>();
        var canonical =
            ImmutableArray.CreateBuilder<
                IntegrationSourceBindingContextIncidence>(incidence.Length);
        foreach (IntegrationSourceBindingContextIncidence entry in incidence)
        {
            if (entry is null)
            {
                throw new ArgumentException(
                    "Source incidence cannot contain null.",
                    nameof(sourceIncidence));
            }
            if (!participants.Add(entry.Participant))
            {
                throw new ArgumentException(
                    "Source incidence cannot contain duplicate participants.",
                    nameof(sourceIncidence));
            }

            var remaining =
                entry.BindingContexts.ToHashSet();
            var ordered =
                ImmutableArray.CreateBuilder<
                    IIntegrationBindingContextIdentity>(
                        entry.BindingContexts.Length);
            foreach (IIntegrationBindingContextIdentity context
                in BindingContexts)
            {
                if (remaining.Remove(context))
                    ordered.Add(context);
            }
            if (remaining.Count != 0)
            {
                throw new ArgumentException(
                    "Source incidence cannot reference a foreign binding context.",
                    nameof(sourceIncidence));
            }

            canonical.Add(
                new IntegrationSourceBindingContextIncidence(
                    entry.Participant,
                    ordered));
        }

        SourceIncidence = canonical.MoveToImmutable();
    }

    public ImmutableArray<IIntegrationBindingContextIdentity> BindingContexts
        { get; }
    public ImmutableArray<IntegrationSourceBindingContextIncidence>
        SourceIncidence { get; }
}

/// <summary>One terminal source-participant receipt.</summary>
public abstract class IntegrationSourceParticipantAttempt
{
    private protected IntegrationSourceParticipantAttempt(
        IntegrationSourceParticipantIdentity participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        Participant = participant;
    }

    public IntegrationSourceParticipantIdentity Participant { get; }

    public sealed class Available :
        IntegrationSourceParticipantAttempt
    {
        public Available(IntegrationSourceParticipantIdentity participant)
            : base(participant)
        {
        }
    }

    public sealed class Rejected :
        IntegrationSourceParticipantAttempt
    {
        public Rejected(
            IntegrationSourceParticipantIdentity participant,
            IIntegrationSourceParticipantRejection rejection)
            : base(participant)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            Rejection = rejection;
        }

        public IIntegrationSourceParticipantRejection Rejection { get; }
    }

    public sealed class Failed :
        IntegrationSourceParticipantAttempt
    {
        public Failed(
            IntegrationSourceParticipantIdentity participant,
            IIntegrationSourceParticipantFailure failure)
            : base(participant)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public IIntegrationSourceParticipantFailure Failure { get; }
    }
}

/// <summary>
/// One required participant and producer-policy address.
/// </summary>
public sealed class IntegrationProducerPolicyAttemptAddress :
    IEquatable<IntegrationProducerPolicyAttemptAddress>
{
    public IntegrationProducerPolicyAttemptAddress(
        IntegrationSourceParticipantIdentity participant,
        IntegrationProducerPolicyBinding policy)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(policy);
        Participant = participant;
        Policy = policy;
    }

    public IntegrationSourceParticipantIdentity Participant { get; }
    public IntegrationProducerPolicyBinding Policy { get; }

    public bool Equals(IntegrationProducerPolicyAttemptAddress? other) =>
        other is not null
        && Participant.Equals(other.Participant)
        && ReferenceEquals(Policy, other.Policy);

    public override bool Equals(object? obj) =>
        obj is IntegrationProducerPolicyAttemptAddress other
        && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Participant, Policy);
}

/// <summary>
/// One candidate plus structured source lookups that may prove an observed
/// Integration fulfills an opportunity.
/// </summary>
public sealed class IntegrationCandidateEvidence
{
    public IntegrationCandidateEvidence(
        IntegrationCandidateIdentity candidate,
        IEnumerable<IntegrationCandidatePeerIdentity.NamedType>?
            fulfillmentSourceLookups = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
        FulfillmentSourceLookups =
            [.. fulfillmentSourceLookups ?? []];
        if (FulfillmentSourceLookups.Any(lookup => lookup is null))
        {
            throw new ArgumentException(
                "Fulfillment-source lookups cannot contain null.",
                nameof(fulfillmentSourceLookups));
        }
        if (FulfillmentSourceLookups.Distinct().Count()
            != FulfillmentSourceLookups.Length)
        {
            throw new ArgumentException(
                "Fulfillment-source lookups cannot contain duplicates.",
                nameof(fulfillmentSourceLookups));
        }
        if (!FulfillmentSourceLookups.IsEmpty
            && !ReferenceEquals(
                candidate.Relationship,
                InspectionGraphIntegrationsCatalog.IntegrationObserved))
        {
            throw new ArgumentException(
                "Only observed Integration evidence can declare fulfillment-source lookups.",
                nameof(fulfillmentSourceLookups));
        }
    }

    public IntegrationCandidateIdentity Candidate { get; }
    public ImmutableArray<IntegrationCandidatePeerIdentity.NamedType>
        FulfillmentSourceLookups { get; }
}

/// <summary>One terminal producer-policy receipt.</summary>
public abstract class IntegrationProducerPolicyAttempt
{
    private protected IntegrationProducerPolicyAttempt(
        IntegrationProducerPolicyAttemptAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Address = address;
    }

    public IntegrationProducerPolicyAttemptAddress Address { get; }

    public sealed class Completed : IntegrationProducerPolicyAttempt
    {
        public Completed(
            IntegrationProducerPolicyAttemptAddress address,
            IEnumerable<IntegrationCandidateIdentity> candidates)
            : base(address)
        {
            ArgumentNullException.ThrowIfNull(candidates);
            ImmutableArray<IntegrationCandidateIdentity> copied =
                [.. candidates];
            if (copied.Any(candidate => candidate is null))
            {
                throw new ArgumentException(
                    "Completed candidate evidence cannot contain null.",
                    nameof(candidates));
            }
            Initialize(
                [.. copied.Select(candidate =>
                    new IntegrationCandidateEvidence(candidate))]);
        }

        Completed(
            IntegrationProducerPolicyAttemptAddress address,
            ImmutableArray<IntegrationCandidateEvidence> evidence)
            : base(address)
            => Initialize(evidence);

        public static Completed WithEvidence(
            IntegrationProducerPolicyAttemptAddress address,
            IEnumerable<IntegrationCandidateEvidence> evidence)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            return new Completed(address, [.. evidence]);
        }

        void Initialize(
            ImmutableArray<IntegrationCandidateEvidence> evidence)
        {
            Evidence = evidence;
            if (Evidence.Any(candidate => candidate is null))
            {
                throw new ArgumentException(
                    "Completed candidate evidence cannot contain null.",
                    nameof(evidence));
            }
            if (Evidence.Any(candidate =>
                    !candidate.Candidate.Source.Participant.Equals(
                        Address.Participant)))
            {
                throw new ArgumentException(
                    "Completed candidate evidence must belong to the addressed participant.",
                    nameof(evidence));
            }
            if (Evidence.Any(evidence =>
                    !ReferenceEquals(
                        evidence.Candidate.Relationship,
                        Address.Policy.Relationship)
                    || !Address.Policy.Policy.Concepts.Contains(
                        evidence.Candidate.Concept,
                        ReferenceEqualityComparer.Instance)))
            {
                throw new ArgumentException(
                    "Completed candidate evidence must match the addressed producer policy.",
                    nameof(evidence));
            }
            if (!ReferenceEquals(
                    Address.Policy,
                    IntegrationAnalysisCatalog.EcosystemObserved)
                && Evidence.Any(evidence =>
                    !evidence.FulfillmentSourceLookups.IsEmpty))
            {
                throw new ArgumentException(
                    "Only the ecosystem-observed producer policy can declare fulfillment-source lookups.",
                    nameof(evidence));
            }

            Candidates =
            [
                .. Evidence.Select(static evidence => evidence.Candidate),
            ];
        }

        public ImmutableArray<IntegrationCandidateEvidence> Evidence
            { get; private set; } = [];
        public ImmutableArray<IntegrationCandidateIdentity> Candidates
            { get; private set; } = [];
    }

    public sealed class Unavailable : IntegrationProducerPolicyAttempt
    {
        public Unavailable(
            IntegrationProducerPolicyAttemptAddress address,
            IIntegrationProducerPolicyUnavailable reason)
            : base(address)
        {
            ArgumentNullException.ThrowIfNull(reason);
            Reason = reason;
        }

        public IIntegrationProducerPolicyUnavailable Reason { get; }
    }

    public sealed class Failed : IntegrationProducerPolicyAttempt
    {
        public Failed(
            IntegrationProducerPolicyAttemptAddress address,
            IIntegrationProducerPolicyFailure failure)
            : base(address)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public IIntegrationProducerPolicyFailure Failure { get; }
    }
}

/// <summary>One exact resolved Type in the binding/comparison domain.</summary>
public sealed class IntegrationTypeIdentity :
    IEquatable<IntegrationTypeIdentity>
{
    public IntegrationTypeIdentity(
        IntegrationSourceParticipantIdentity participant,
        MetadataTypeDefinitionName type)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(type);
        Participant = participant;
        Type = type;
    }

    public IntegrationSourceParticipantIdentity Participant { get; }
    public MetadataTypeDefinitionName Type { get; }

    public bool Equals(IntegrationTypeIdentity? other) =>
        other is not null
        && Participant.Equals(other.Participant)
        && Type == other.Type;

    public override bool Equals(object? obj) =>
        obj is IntegrationTypeIdentity other
        && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Participant, Type);
}

/// <summary>
/// Successful exact peer resolution, including every forwarding definition
/// and the terminal definition.
/// </summary>
public sealed class IntegrationResolvedPeer
{
    public IntegrationResolvedPeer(
        IntegrationCandidatePeerIdentity lookup,
        IEnumerable<IntegrationTypeIdentity> resolutionPath)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        ArgumentNullException.ThrowIfNull(resolutionPath);
        Lookup = lookup;
        ResolutionPath = [.. resolutionPath];
        if (ResolutionPath.IsEmpty)
        {
            throw new ArgumentException(
                "A resolved peer requires a terminal Type.",
                nameof(resolutionPath));
        }
        if (ResolutionPath.Any(type => type is null))
        {
            throw new ArgumentException(
                "A resolved peer path cannot contain null.",
                nameof(resolutionPath));
        }
        for (int left = 0; left < ResolutionPath.Length; left++)
        {
            for (int right = left + 1;
                right < ResolutionPath.Length;
                right++)
            {
                if (ResolutionPath[left].Equals(ResolutionPath[right]))
                {
                    throw new ArgumentException(
                        "A resolved peer path cannot contain a forwarding cycle.",
                        nameof(resolutionPath));
                }
            }
        }
    }

    public IntegrationCandidatePeerIdentity Lookup { get; }
    public ImmutableArray<IntegrationTypeIdentity> ResolutionPath { get; }
    public IntegrationTypeIdentity Terminal => ResolutionPath[^1];
}

public enum IntegrationCandidateOutReason
{
    PeerOutsideUniverse,
}

/// <summary>One closed successful candidate disposition.</summary>
public abstract class IntegrationCandidateDisposition
{
    private protected IntegrationCandidateDisposition(
        IntegrationResolvedPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        Peer = peer;
    }

    public IntegrationResolvedPeer Peer { get; }

    public sealed class In : IntegrationCandidateDisposition
    {
        public In(IntegrationResolvedPeer peer)
            : base(peer)
        {
        }
    }

    public sealed class Out : IntegrationCandidateDisposition
    {
        public Out(IntegrationResolvedPeer peer)
            : base(peer)
        {
        }

        public IntegrationCandidateOutReason Reason =>
            IntegrationCandidateOutReason.PeerOutsideUniverse;
    }
}

/// <summary>One candidate and binding-context evaluation address.</summary>
public sealed class IntegrationCandidateAttemptAddress :
    IEquatable<IntegrationCandidateAttemptAddress>
{
    public IntegrationCandidateAttemptAddress(
        IntegrationCandidateIdentity candidate,
        IIntegrationBindingContextIdentity bindingContext)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(bindingContext);
        Candidate = candidate;
        BindingContext = bindingContext;
    }

    public IntegrationCandidateIdentity Candidate { get; }
    public IIntegrationBindingContextIdentity BindingContext { get; }

    public bool Equals(IntegrationCandidateAttemptAddress? other) =>
        other is not null
        && Candidate.Equals(other.Candidate)
        && EqualityComparer<IIntegrationBindingContextIdentity>.Default.Equals(
            BindingContext,
            other.BindingContext);

    public override bool Equals(object? obj) =>
        obj is IntegrationCandidateAttemptAddress other
        && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Candidate, BindingContext);
}

public enum IntegrationCandidateSuppressionReason
{
    FulfilledByObservation,
}

/// <summary>
/// Exact source and target Types used by Integration policy to prove one
/// opportunity is fulfilled by one observation.
/// </summary>
public sealed class IntegrationOpportunityFulfillment
{
    public IntegrationOpportunityFulfillment(
        IntegrationTypeIdentity sourceType,
        IntegrationResolvedPeer target)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(target);
        SourceType = sourceType;
        Target = target;
    }

    public IntegrationTypeIdentity SourceType { get; }
    public IntegrationResolvedPeer Target { get; }
    public IntegrationTypeIdentity TargetType => Target.Terminal;
}

/// <summary>One terminal candidate evaluation receipt.</summary>
public abstract class IntegrationCandidateAttempt
{
    private protected IntegrationCandidateAttempt(
        IntegrationCandidateAttemptAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Address = address;
    }

    public IntegrationCandidateAttemptAddress Address { get; }

    public sealed class Classified : IntegrationCandidateAttempt
    {
        public Classified(
            IntegrationCandidateAttemptAddress address,
            IntegrationCandidateDisposition disposition,
            IEnumerable<IntegrationResolvedPeer>?
                fulfillmentSourceResolutions = null)
            : base(address)
        {
            ArgumentNullException.ThrowIfNull(disposition);
            Disposition = disposition;
            FulfillmentSourceResolutions =
                [.. fulfillmentSourceResolutions ?? []];
            if (FulfillmentSourceResolutions.Any(
                    resolution => resolution is null))
            {
                throw new ArgumentException(
                    "Fulfillment-source resolutions cannot contain null.",
                    nameof(fulfillmentSourceResolutions));
            }
            if (FulfillmentSourceResolutions
                    .Select(resolution => resolution.Lookup)
                    .Distinct()
                    .Count()
                != FulfillmentSourceResolutions.Length)
            {
                throw new ArgumentException(
                    "Fulfillment-source resolutions cannot repeat a lookup.",
                    nameof(fulfillmentSourceResolutions));
            }
        }

        public IntegrationCandidateDisposition Disposition { get; }
        public ImmutableArray<IntegrationResolvedPeer>
            FulfillmentSourceResolutions { get; }
    }

    public sealed class Suppressed : IntegrationCandidateAttempt
    {
        public Suppressed(
            IntegrationCandidateAttemptAddress address,
            IntegrationCandidateAttemptAddress fulfilledBy,
            IntegrationOpportunityFulfillment fulfillment)
            : base(address)
        {
            ArgumentNullException.ThrowIfNull(fulfilledBy);
            ArgumentNullException.ThrowIfNull(fulfillment);
            FulfilledBy = fulfilledBy;
            Fulfillment = fulfillment;
        }

        public IntegrationCandidateSuppressionReason Reason =>
            IntegrationCandidateSuppressionReason.FulfilledByObservation;
        public IntegrationCandidateAttemptAddress FulfilledBy { get; }
        public IntegrationOpportunityFulfillment Fulfillment { get; }
    }

    public sealed class Failed : IntegrationCandidateAttempt
    {
        public Failed(
            IntegrationCandidateAttemptAddress address,
            IIntegrationCandidateFailure failure)
            : base(address)
        {
            ArgumentNullException.ThrowIfNull(failure);
            Failure = failure;
        }

        public IIntegrationCandidateFailure Failure { get; }
    }
}

/// <summary>
/// One coalesced candidate and every producer-policy receipt that supplied it.
/// </summary>
public sealed class IntegrationCensusCandidate
{
    internal IntegrationCensusCandidate(
        IntegrationCandidateIdentity identity,
        IEnumerable<IntegrationProducerPolicyAttemptAddress> producerAttempts,
        IEnumerable<IntegrationCandidateEvidence> evidence)
    {
        Identity = identity;
        ProducerAttempts = [.. producerAttempts];
        Evidence = [.. evidence];
    }

    public IntegrationCandidateIdentity Identity { get; }
    public ImmutableArray<IntegrationProducerPolicyAttemptAddress>
        ProducerAttempts { get; }
    public ImmutableArray<IntegrationCandidateEvidence> Evidence { get; }
}

/// <summary>
/// Immutable projection-neutral Integration Census receipts and
/// classifications for one validated request input.
/// </summary>
public sealed class IntegrationCensusSnapshot
{
    readonly Dictionary<
        IntegrationCandidateAttemptAddress,
        IntegrationCandidateAttempt> _candidateAttemptsByAddress;
    readonly Dictionary<
        IntegrationCandidateIdentity,
        IntegrationCensusCandidate> _candidatesByIdentity;
    readonly HashSet<IntegrationTypeIdentity> _selectedTypeSet;

    public IntegrationCensusSnapshot(
        AnalysisRequestPlan plan,
        IEnumerable<IntegrationSourceParticipantIdentity> sourceParticipants,
        IEnumerable<IntegrationTypeIdentity> selectedTypes,
        IntegrationBindingContextAccess bindingContextAccess,
        IEnumerable<IntegrationSourceParticipantAttempt> sourceAttempts,
        IEnumerable<IntegrationProducerPolicyAttempt> producerPolicyAttempts,
        IEnumerable<IntegrationCandidateAttempt> candidateAttempts)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidatePlan(plan);

        Analysis = plan.Analysis;
        ReportSurface = plan.ReportSurface;
        Universe = plan.Universe;
        Mode = plan.Mode;
        UniverseRequirements = plan.UniverseRequirements;
        CatalogRevision = IntegrationConceptCatalog.Revision;

        SourceParticipants = CopyUnique(
            sourceParticipants,
            EqualityComparer<IntegrationSourceParticipantIdentity>.Default,
            nameof(sourceParticipants));
        SelectedTypes = CopyUnique(
            selectedTypes,
            EqualityComparer<IntegrationTypeIdentity>.Default,
            nameof(selectedTypes));
        _selectedTypeSet = SelectedTypes.ToHashSet();
        ArgumentNullException.ThrowIfNull(bindingContextAccess);
        BindingContexts = bindingContextAccess.BindingContexts;
        SourceContextIncidence = CanonicalizeIncidence(
            SourceParticipants,
            bindingContextAccess.SourceIncidence,
            nameof(bindingContextAccess));
        Dictionary<
            IntegrationSourceParticipantIdentity,
            IntegrationSourceBindingContextIncidence>
            incidenceByParticipant = SourceContextIncidence.ToDictionary(
                static incidence => incidence.Participant);

        SourceAttempts = Canonicalize(
            SourceParticipants,
            sourceAttempts,
            static attempt => attempt.Participant,
            EqualityComparer<IntegrationSourceParticipantIdentity>.Default,
            nameof(sourceAttempts));

        RequiredProducerPolicies = RequiredPolicies(plan);
        ImmutableArray<IntegrationProducerPolicyAttemptAddress>
            expectedProducerAddresses =
            [
                .. SourceParticipants.SelectMany(participant =>
                    RequiredProducerPolicies.Select(policy =>
                        new IntegrationProducerPolicyAttemptAddress(
                            participant,
                            policy))),
            ];
        ProducerPolicyAttempts = Canonicalize(
            expectedProducerAddresses,
            producerPolicyAttempts,
            static attempt => attempt.Address,
            EqualityComparer<IntegrationProducerPolicyAttemptAddress>.Default,
            nameof(producerPolicyAttempts));
        ValidateProducerAttempts();

        Candidates = BuildCandidates(ProducerPolicyAttempts);
        _candidatesByIdentity = Candidates.ToDictionary(
            static candidate => candidate.Identity);
        var expectedCandidateAddresses =
            ImmutableArray.CreateBuilder<
                IntegrationCandidateAttemptAddress>();
        foreach (IntegrationCensusCandidate candidate in Candidates)
        {
            IntegrationSourceBindingContextIncidence incidence =
                incidenceByParticipant[candidate.Identity.Source.Participant];
            foreach (IIntegrationBindingContextIdentity context
                in incidence.BindingContexts)
            {
                expectedCandidateAddresses.Add(
                    new IntegrationCandidateAttemptAddress(
                        candidate.Identity,
                        context));
            }
        }

        CandidateAttempts = Canonicalize(
            expectedCandidateAddresses.ToImmutable(),
            candidateAttempts,
            static attempt => attempt.Address,
            EqualityComparer<IntegrationCandidateAttemptAddress>.Default,
            nameof(candidateAttempts));
        _candidateAttemptsByAddress = CandidateAttempts.ToDictionary(
            static attempt => attempt.Address);
        ValidateCandidateAttempts();

        ClassifiedAttempts =
        [
            .. CandidateAttempts.OfType<
                IntegrationCandidateAttempt.Classified>(),
        ];
        SuppressedAttempts =
        [
            .. CandidateAttempts.OfType<
                IntegrationCandidateAttempt.Suppressed>(),
        ];
        FailedCandidateAttempts =
        [
            .. CandidateAttempts.OfType<
                IntegrationCandidateAttempt.Failed>(),
        ];
    }

    public AnalysisDescriptor Analysis { get; }
    public AnalysisReportSurface ReportSurface { get; }
    public AnalysisUniverseDescription Universe { get; }
    public AnalysisQuestionMode Mode { get; }
    public IntegrationConceptCatalogRevision CatalogRevision { get; }
    public ImmutableArray<AnalysisUniverseRequirementDescriptor>
        UniverseRequirements { get; }
    public ImmutableArray<IntegrationSourceParticipantIdentity>
        SourceParticipants { get; }
    public ImmutableArray<IntegrationTypeIdentity> SelectedTypes { get; }
    public ImmutableArray<IIntegrationBindingContextIdentity> BindingContexts
        { get; }
    public ImmutableArray<IntegrationSourceBindingContextIncidence>
        SourceContextIncidence { get; }
    public ImmutableArray<IntegrationProducerPolicyBinding>
        RequiredProducerPolicies { get; }
    public ImmutableArray<IntegrationSourceParticipantAttempt> SourceAttempts
        { get; }
    public ImmutableArray<IntegrationProducerPolicyAttempt>
        ProducerPolicyAttempts { get; }
    public ImmutableArray<IntegrationCensusCandidate> Candidates { get; }
    public ImmutableArray<IntegrationCandidateAttempt> CandidateAttempts
        { get; }
    public ImmutableArray<IntegrationCandidateAttempt.Classified>
        ClassifiedAttempts { get; }
    public ImmutableArray<IntegrationCandidateAttempt.Suppressed>
        SuppressedAttempts { get; }
    public ImmutableArray<IntegrationCandidateAttempt.Failed>
        FailedCandidateAttempts { get; }
    public IAnalysisUniverseCompleteness UniverseCompleteness =>
        Universe.Completeness;
    public ImmutableArray<IAnalysisUniverseFailure> UniverseFailures =>
        Universe.Failures;

    public bool IsComplete =>
        SourceAttempts.All(
            static attempt =>
                attempt is IntegrationSourceParticipantAttempt.Available)
        && ProducerPolicyAttempts.All(
            static attempt =>
                attempt is IntegrationProducerPolicyAttempt.Completed)
        && CandidateAttempts.All(
            static attempt =>
                attempt is IntegrationCandidateAttempt.Classified
                    or IntegrationCandidateAttempt.Suppressed);

    public bool IsCompatibleWith(AnalysisRequestPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return ReferenceEquals(Analysis, plan.Analysis)
            && ReferenceEquals(ReportSurface, plan.ReportSurface)
            && ReferenceEquals(Universe, plan.Universe)
            && Mode == plan.Mode
            && ReferenceEquals(
                CatalogRevision,
                IntegrationConceptCatalog.Revision)
            && SameReferences(
                UniverseRequirements,
                plan.UniverseRequirements);
    }

    void ValidateProducerAttempts()
    {
        for (int index = 0; index < ProducerPolicyAttempts.Length; index++)
        {
            IntegrationProducerPolicyAttempt attempt =
                ProducerPolicyAttempts[index];
            int participantIndex =
                index / RequiredProducerPolicies.Length;
            if (SourceAttempts[participantIndex]
                    is not IntegrationSourceParticipantAttempt.Available
                && attempt is IntegrationProducerPolicyAttempt.Completed)
            {
                throw new ArgumentException(
                    "An unavailable source participant cannot have a completed producer-policy attempt.",
                    nameof(ProducerPolicyAttempts));
            }
            if (attempt is IntegrationProducerPolicyAttempt.Completed completed
                && completed.Candidates.Any(candidate =>
                    !_selectedTypeSet.Contains(SourceTypeOf(candidate))))
            {
                throw new ArgumentException(
                    "Candidate evidence requires its source Type in the selected universe.",
                    nameof(ProducerPolicyAttempts));
            }
        }
    }

    internal static ImmutableArray<IntegrationCensusCandidate> BuildCandidates(
        IEnumerable<IntegrationProducerPolicyAttempt> producerPolicyAttempts)
    {
        var candidateOrder = new List<IntegrationCandidateIdentity>();
        var candidateProducers = new Dictionary<
            IntegrationCandidateIdentity,
            (
                List<IntegrationProducerPolicyAttemptAddress> Order,
                HashSet<IntegrationProducerPolicyAttemptAddress> Set,
                List<IntegrationCandidateEvidence> Evidence)>();
        foreach (IntegrationProducerPolicyAttempt.Completed attempt
            in producerPolicyAttempts.OfType<
                IntegrationProducerPolicyAttempt.Completed>())
        {
            foreach (IntegrationCandidateEvidence evidence
                in attempt.Evidence)
            {
                IntegrationCandidateIdentity candidate =
                    evidence.Candidate;
                if (!candidateProducers.TryGetValue(
                        candidate,
                        out var producers))
                {
                    producers = (
                        [],
                        [],
                        []);
                    candidateProducers.Add(candidate, producers);
                    candidateOrder.Add(candidate);
                }

                if (producers.Set.Add(attempt.Address))
                {
                    producers.Order.Add(attempt.Address);
                }
                producers.Evidence.Add(evidence);
            }
        }

        return
        [
            .. candidateOrder.Select(candidate =>
                new IntegrationCensusCandidate(
                    candidate,
                    candidateProducers[candidate].Order,
                    candidateProducers[candidate].Evidence)),
        ];
    }

    void ValidateCandidateAttempts()
    {
        foreach (IntegrationCandidateAttempt attempt in CandidateAttempts)
        {
            switch (attempt)
            {
                case IntegrationCandidateAttempt.Classified classified:
                    ValidateClassification(classified);
                    break;
                case IntegrationCandidateAttempt.Suppressed suppressed:
                    ValidateSuppression(suppressed);
                    break;
            }
        }
    }

    void ValidateClassification(
        IntegrationCandidateAttempt.Classified attempt)
    {
        IntegrationCandidateIdentity candidate = attempt.Address.Candidate;
        ValidateResolution(candidate, attempt.Disposition.Peer);
        IntegrationCensusCandidate censusCandidate =
            _candidatesByIdentity[candidate];
        IntegrationCandidatePeerIdentity.NamedType[] declaredSources =
        [
            .. censusCandidate.Evidence
                .SelectMany(evidence =>
                    evidence.FulfillmentSourceLookups)
                .Distinct(),
        ];
        if (declaredSources.Length
            != attempt.FulfillmentSourceResolutions.Length)
        {
            throw new ArgumentException(
                "Fulfillment-source resolutions must exactly cover the declared lookups.",
                nameof(CandidateAttempts));
        }
        foreach (IntegrationResolvedPeer source
            in attempt.FulfillmentSourceResolutions)
        {
            if (!ReferenceEquals(
                    candidate.Relationship,
                    InspectionGraphIntegrationsCatalog.IntegrationObserved)
                || !censusCandidate.Evidence.Any(evidence =>
                    evidence.FulfillmentSourceLookups.Any(
                        lookup => lookup.Equals(source.Lookup))))
            {
                throw new ArgumentException(
                    "Fulfillment-source resolution requires a declared observed-source lookup.",
                    nameof(CandidateAttempts));
            }
            ValidateResolvedLookup(source.Lookup, source);
        }

        IntegrationTypeIdentity terminal =
            attempt.Disposition.Peer.Terminal;
        bool selected = _selectedTypeSet.Contains(terminal);
        if (attempt.Disposition
                is IntegrationCandidateDisposition.In
            && !selected
            || attempt.Disposition
                is IntegrationCandidateDisposition.Out
            && selected)
        {
            throw new ArgumentException(
                "Candidate disposition must agree with terminal Type membership.",
                nameof(CandidateAttempts));
        }
    }

    void ValidateSuppression(
        IntegrationCandidateAttempt.Suppressed attempt)
    {
        if (attempt.Address.Equals(attempt.FulfilledBy)
            || !EqualityComparer<IIntegrationBindingContextIdentity>
                .Default.Equals(
                    attempt.Address.BindingContext,
                    attempt.FulfilledBy.BindingContext))
        {
            throw new ArgumentException(
                "Suppression requires a distinct candidate in the same binding context.",
                nameof(CandidateAttempts));
        }

        IntegrationCandidateIdentity suppressed =
            attempt.Address.Candidate;
        IntegrationCandidateIdentity fulfilling =
            attempt.FulfilledBy.Candidate;
        if (!ReferenceEquals(
                suppressed.Relationship,
                InspectionGraphIntegrationsCatalog.IntegrationOpportunity)
            || !ReferenceEquals(
                fulfilling.Relationship,
                InspectionGraphIntegrationsCatalog.IntegrationObserved)
            || !ReferenceEquals(
                suppressed.Concept,
                fulfilling.Concept))
        {
            throw new ArgumentException(
                "Only an observed candidate for the same concept can fulfill an opportunity.",
                nameof(CandidateAttempts));
        }

        if (!_candidateAttemptsByAddress.TryGetValue(
                attempt.FulfilledBy,
                out IntegrationCandidateAttempt? fulfillingAttempt))
        {
            throw new ArgumentException(
                "Suppression requires a retained fulfilling candidate attempt in the same incident context.",
                nameof(CandidateAttempts));
        }
        if (fulfillingAttempt
            is not IntegrationCandidateAttempt.Classified classified)
        {
            throw new ArgumentException(
                "Suppression requires a successfully classified fulfilling observation.",
                nameof(CandidateAttempts));
        }
        if (!attempt.Fulfillment.SourceType.Equals(
                SourceTypeOf(suppressed))
            || !attempt.Fulfillment.Target.Terminal.Equals(
                classified.Disposition.Peer.Terminal)
            || !classified.FulfillmentSourceResolutions.Any(
                source => source.Terminal.Equals(
                    attempt.Fulfillment.SourceType)))
        {
            throw new ArgumentException(
                "Suppression fulfillment must retain an observed source and the opportunity's exact resolved target Type.",
                nameof(CandidateAttempts));
        }
        ValidateResolution(suppressed, attempt.Fulfillment.Target);
    }

    internal static IntegrationTypeIdentity SourceTypeOf(
        IntegrationCandidateIdentity candidate) =>
        new(
            candidate.Source.Participant,
            candidate.Source.SourceType);

    internal static void ValidateResolution(
        IntegrationCandidateIdentity candidate,
        IntegrationResolvedPeer resolved)
    {
        if (!candidate.Peer.Equals(resolved.Lookup))
        {
            throw new ArgumentException(
                "Resolved peer evidence must retain its exact candidate lookup.",
                nameof(CandidateAttempts));
        }
        ValidateResolvedLookup(candidate.Peer, resolved);
    }

    internal static void ValidateResolvedLookup(
        IntegrationCandidatePeerIdentity lookup,
        IntegrationResolvedPeer resolved)
    {
        if (!lookup.Equals(resolved.Lookup)
            || resolved.ResolutionPath.Any(
                type => type.Type != lookup.Type))
        {
            throw new ArgumentException(
                "Every resolved peer hop must retain the candidate Type name.",
                nameof(CandidateAttempts));
        }

        if (resolved.Lookup
                is IntegrationCandidatePeerIdentity.NamedType
                {
                    Reference.Scope:
                        MetadataTypeReferenceScope.ModuleReference,
                })
        {
            throw new ArgumentException(
                "Module-reference peers cannot be classified by the current resolution owner.",
                nameof(CandidateAttempts));
        }
    }

    internal static void ValidatePlan(AnalysisRequestPlan plan)
    {
        if (!ReferenceEquals(
                plan.Analysis,
                IntegrationAnalysisCatalog.Analysis)
            || plan.Mode != AnalysisQuestionMode.Census
            || plan.ReportSurface.Kind
                != AnalysisReportSurfaceKind.Workspace
            || !plan.Universe.IsFinite
            || !plan.UniverseRequirements.Contains(
                IntegrationAnalysisCatalog.BindingContextsRequirement,
                ReferenceEqualityComparer.Instance)
            || plan.UniverseRequirements.Any(requirement =>
                !IntegrationAnalysisCatalog.UniverseRequirements.Contains(
                    requirement,
                    ReferenceEqualityComparer.Instance)))
        {
            throw new ArgumentException(
                "The plan is not a validated Integration Workspace Census request.",
                nameof(plan));
        }
    }

    internal static ImmutableArray<IntegrationProducerPolicyBinding>
        RequiredPolicies(AnalysisRequestPlan plan)
    {
        var policies =
            ImmutableArray.CreateBuilder<IntegrationProducerPolicyBinding>();
        foreach (AnalysisUniverseRequirementDescriptor requirement
            in plan.UniverseRequirements)
        {
            if (IntegrationAnalysisCatalog.TryGetProducerPolicy(
                    requirement,
                    out IntegrationProducerPolicyBinding? policy))
            {
                policies.Add(policy);
            }
        }

        if (policies.Count == 0)
        {
            throw new ArgumentException(
                "The Integration Census plan has no producer-policy requirements.",
                nameof(plan));
        }
        return policies.ToImmutable();
    }

    internal static ImmutableArray<IntegrationSourceBindingContextIncidence>
        CanonicalizeIncidence(
            ImmutableArray<IntegrationSourceParticipantIdentity> participants,
            ImmutableArray<IntegrationSourceBindingContextIncidence> supplied,
            string parameterName)
    {
        var incidenceByParticipant = new Dictionary<
            IntegrationSourceParticipantIdentity,
            IntegrationSourceBindingContextIncidence>();
        foreach (IntegrationSourceBindingContextIncidence incidence in supplied)
        {
            if (!incidenceByParticipant.TryAdd(
                    incidence.Participant,
                    incidence))
            {
                throw new ArgumentException(
                    "Source incidence cannot contain duplicate participants.",
                    parameterName);
            }
        }

        var ordered =
            ImmutableArray.CreateBuilder<
                IntegrationSourceBindingContextIncidence>(
                    participants.Length);
        foreach (IntegrationSourceParticipantIdentity participant
            in participants)
        {
            if (!incidenceByParticipant.Remove(
                    participant,
                    out IntegrationSourceBindingContextIncidence? incidence))
            {
                throw new ArgumentException(
                    "Source incidence is missing a declared participant.",
                    parameterName);
            }
            ordered.Add(
                new IntegrationSourceBindingContextIncidence(
                    participant,
                    incidence.BindingContexts));
        }
        if (incidenceByParticipant.Count != 0)
        {
            throw new ArgumentException(
                "Source incidence contains an extraneous participant.",
                parameterName);
        }
        return ordered.MoveToImmutable();
    }

    static ImmutableArray<T> CopyUnique<T>(
        IEnumerable<T> values,
        IEqualityComparer<T> comparer,
        string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(values);
        ImmutableArray<T> result = [.. values];
        var identities = new HashSet<T>(comparer);
        foreach (T value in result)
        {
            if (value is null)
            {
                throw new ArgumentException(
                    "The collection cannot contain null.",
                    parameterName);
            }
            if (!identities.Add(value))
            {
                throw new ArgumentException(
                    "The collection cannot contain duplicate identities.",
                    parameterName);
            }
        }
        return result;
    }

    static ImmutableArray<TAttempt> Canonicalize<TAddress, TAttempt>(
        ImmutableArray<TAddress> expected,
        IEnumerable<TAttempt> supplied,
        Func<TAttempt, TAddress> addressOf,
        IEqualityComparer<TAddress> comparer,
        string parameterName)
        where TAddress : class
        where TAttempt : class
    {
        ArgumentNullException.ThrowIfNull(supplied);
        ImmutableArray<TAttempt> actual = [.. supplied];
        if (actual.Any(attempt => attempt is null))
            throw new ArgumentException(
                "Attempt collections cannot contain null.",
                parameterName);

        var attemptsByAddress =
            new Dictionary<TAddress, TAttempt>(comparer);
        foreach (TAttempt attempt in actual)
        {
            if (!attemptsByAddress.TryAdd(
                    addressOf(attempt),
                    attempt))
            {
                throw new ArgumentException(
                    "Attempt collections cannot contain duplicate addresses.",
                    parameterName);
            }
        }

        var ordered = ImmutableArray.CreateBuilder<TAttempt>(expected.Length);
        foreach (TAddress expectedAddress in expected)
        {
            if (!attemptsByAddress.Remove(
                    expectedAddress,
                    out TAttempt? attempt))
            {
                throw new ArgumentException(
                    "Attempt collection is missing an expected address.",
                    parameterName);
            }
            ordered.Add(attempt);
        }

        if (attemptsByAddress.Count != 0)
        {
            throw new ArgumentException(
                "Attempt collection contains an extraneous address.",
                parameterName);
        }
        return ordered.MoveToImmutable();
    }

    static bool SameReferences<T>(
        ImmutableArray<T> left,
        ImmutableArray<T> right)
        where T : class
    {
        if (left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (!ReferenceEquals(left[index], right[index]))
                return false;
        }
        return true;
    }
}
