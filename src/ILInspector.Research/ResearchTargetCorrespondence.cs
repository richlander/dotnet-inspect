using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

/// <summary>The health of one complete domain-side target census.</summary>
public enum ResearchTargetCensusHealth
{
    /// <summary>Every admitted input was evaluated as resolved or not found.</summary>
    Healthy,

    /// <summary>At least one input was blocked, failed, or not evaluated.</summary>
    Blocked,
}

/// <summary>The positive evidence from which an absence proof was minted.</summary>
public enum ResearchTargetAbsenceEvidenceKind
{
    /// <summary>The admitted question contains no input in this domain and side.</summary>
    NoAdmittedInput,

    /// <summary>One exact terminal attempt established typed not-found evidence.</summary>
    NotFound,
}

/// <summary>Why target correspondence could not complete.</summary>
public enum ResearchTargetTaintKind
{
    /// <summary>A domain-side census contains blocking or unevaluated evidence.</summary>
    BlockedDomain,

    /// <summary>Both sides resolved different target keys or relationship roles.</summary>
    SelectionDrift,

    /// <summary>The opposite not-found evidence does not cover the resolved key.</summary>
    AbsenceNotProven,
}

/// <summary>The closed correspondence outcome for one target domain.</summary>
public enum ResearchTargetCorrespondenceKind
{
    Paired,
    BeforeOnly,
    AfterOnly,
    Absent,
    CounterpartUnavailable,
    DomainUnavailable,
}

/// <summary>
/// The complete terminal target census for one side of one domain.
/// </summary>
public sealed class ResearchTargetDomainSideCensus
{
    internal ResearchTargetDomainSideCensus(
        ResearchTargetDomain domain,
        ResearchComparisonSide side,
        ImmutableArray<ResearchTargetInputDisposition> inputs,
        ImmutableArray<ResearchTargetAttempt> attempts,
        ResearchTargetCensusHealth health)
    {
        Domain = domain;
        Side = side;
        Inputs = inputs;
        Attempts = attempts;
        Health = health;
    }

    public ResearchTargetDomain Domain { get; }

    public ResearchTargetDomainId DomainId => Domain.Id;

    public ResearchTargetScopeId Scope => Domain.Scope;

    public ResearchComparisonSide Side { get; }

    public ImmutableArray<ResearchTargetInputDisposition> Inputs { get; }

    public ImmutableArray<ResearchTargetAttempt> Attempts { get; }

    public ResearchTargetCensusHealth Health { get; }
}

/// <summary>
/// The exact side-local identity of one resolved target.
/// </summary>
public sealed class ResearchStrictTargetKey :
    IEquatable<ResearchStrictTargetKey>
{
    internal ResearchStrictTargetKey(
        ResearchTargetScopeId scope,
        ResearchTargetDomainId domain,
        ResearchComparisonInputId input,
        ResearchTargetRelationshipRole role,
        MetadataMethodAddress? address,
        MemberAnchor? anchor)
    {
        Scope = scope;
        Domain = domain;
        Input = input;
        Role = role;
        Address = address;
        Anchor = anchor;
    }

    public ResearchTargetScopeId Scope { get; }

    public ResearchTargetDomainId Domain { get; }

    public ResearchComparisonInputId Input { get; }

    public ResearchComparisonSide Side => Input.Side;

    public ResearchTargetRelationshipRole Role { get; }

    public MetadataMethodAddress? Address { get; }

    public MemberAnchor? Anchor { get; }

    public bool Equals(ResearchStrictTargetKey? other)
        => other is not null
            && ReferenceEquals(Scope, other.Scope)
            && ReferenceEquals(Domain, other.Domain)
            && ReferenceEquals(Input, other.Input)
            && Role == other.Role
            && Address == other.Address
            && Anchor == other.Anchor;

    public override bool Equals(object? obj)
        => Equals(obj as ResearchStrictTargetKey);

    public override int GetHashCode()
        => HashCode.Combine(
            RuntimeHelpers.GetHashCode(Scope),
            RuntimeHelpers.GetHashCode(Domain),
            RuntimeHelpers.GetHashCode(Input),
            Role,
            Address,
            Anchor);
}

/// <summary>
/// The side-independent identity used to correspond resolved targets.
/// </summary>
public sealed class ResearchTargetCorrespondenceKey :
    IEquatable<ResearchTargetCorrespondenceKey>
{
    internal ResearchTargetCorrespondenceKey(
        ResearchTargetScopeId scope,
        ResearchTargetDomainId domain,
        ResearchTargetRelationshipRole role,
        string canonicalIdentity)
    {
        Scope = scope;
        Domain = domain;
        Role = role;
        CanonicalIdentity = canonicalIdentity;
    }

    public ResearchTargetScopeId Scope { get; }

    public ResearchTargetDomainId Domain { get; }

    public ResearchTargetRelationshipRole Role { get; }

    public string CanonicalIdentity { get; }

    public bool Equals(ResearchTargetCorrespondenceKey? other)
        => other is not null
            && ReferenceEquals(Scope, other.Scope)
            && ReferenceEquals(Domain, other.Domain)
            && Role == other.Role
            && string.Equals(
                CanonicalIdentity,
                other.CanonicalIdentity,
                StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => Equals(obj as ResearchTargetCorrespondenceKey);

    public override int GetHashCode()
        => HashCode.Combine(
            RuntimeHelpers.GetHashCode(Scope),
            RuntimeHelpers.GetHashCode(Domain),
            Role,
            StringComparer.Ordinal.GetHashCode(CanonicalIdentity));
}

/// <summary>
/// One resolved target together with both Research-owned target keys.
/// </summary>
public sealed class ResearchCorrespondingTarget
{
    internal ResearchCorrespondingTarget(
        ResearchTargetAttempt attempt,
        ResearchStrictTargetKey strictKey,
        ResearchTargetCorrespondenceKey correspondenceKey)
    {
        Attempt = attempt;
        StrictKey = strictKey;
        CorrespondenceKey = correspondenceKey;
    }

    public ResearchTargetAttempt Attempt { get; }

    public ResearchTargetOutcome.Resolved Target =>
        (ResearchTargetOutcome.Resolved)Attempt.Outcome;

    public ResearchComparisonSide Side => Attempt.Request.Side;

    public ResearchStrictTargetKey StrictKey { get; }

    public ResearchTargetCorrespondenceKey CorrespondenceKey { get; }
}

/// <summary>
/// Positive proof that one correspondence key is absent from one domain side.
/// </summary>
public sealed class ResearchTargetKeyAbsenceProof
{
    internal ResearchTargetKeyAbsenceProof(
        ResearchTargetDomainSideCensus census,
        ResearchTargetCorrespondenceKey key,
        ResearchTargetAbsenceEvidenceKind evidenceKind,
        ResearchTargetAttempt? notFoundAttempt)
    {
        Census = census;
        Key = key;
        EvidenceKind = evidenceKind;
        NotFoundAttempt = notFoundAttempt;
    }

    public ResearchTargetDomainSideCensus Census { get; }

    public ResearchTargetScopeId Scope => Census.Scope;

    public ResearchTargetDomainId Domain => Census.DomainId;

    public ResearchComparisonSide Side => Census.Side;

    public ResearchTargetCorrespondenceKey Key { get; }

    public ResearchTargetRelationshipRole Role => Key.Role;

    public ResearchTargetAbsenceEvidenceKind EvidenceKind { get; }

    public ResearchTargetAttempt? NotFoundAttempt { get; }
}

/// <summary>
/// Positive proof that one domain side contains no resolved target.
/// </summary>
public sealed class ResearchTargetDomainAbsenceProof
{
    internal ResearchTargetDomainAbsenceProof(
        ResearchTargetDomainSideCensus census,
        ResearchTargetAbsenceEvidenceKind evidenceKind,
        ResearchTargetAttempt? notFoundAttempt)
    {
        Census = census;
        EvidenceKind = evidenceKind;
        NotFoundAttempt = notFoundAttempt;
    }

    public ResearchTargetDomainSideCensus Census { get; }

    public ResearchTargetScopeId Scope => Census.Scope;

    public ResearchTargetDomainId Domain => Census.DomainId;

    public ResearchComparisonSide Side => Census.Side;

    public ResearchTargetAbsenceEvidenceKind EvidenceKind { get; }

    public ResearchTargetAttempt? NotFoundAttempt { get; }
}

/// <summary>
/// Complete domain-local evidence that prevented correspondence.
/// </summary>
public sealed class ResearchTargetTaintEvidence
{
    internal ResearchTargetTaintEvidence(
        ResearchTargetTaintKind kind,
        ResearchTargetDomain domain,
        ImmutableArray<ResearchTargetAttempt> attempts,
        ImmutableArray<ResearchStrictTargetKey> strictKeys,
        ImmutableArray<ResearchTargetInputDisposition> incompleteInputs)
    {
        Kind = kind;
        Domain = domain;
        Attempts = attempts;
        StrictKeys = strictKeys;
        IncompleteInputs = incompleteInputs;
    }

    public ResearchTargetTaintKind Kind { get; }

    public ResearchTargetDomain Domain { get; }

    public ImmutableArray<ResearchTargetAttempt> Attempts { get; }

    public ImmutableArray<ResearchStrictTargetKey> StrictKeys { get; }

    public ImmutableArray<ResearchTargetInputDisposition> IncompleteInputs
    {
        get;
    }
}

/// <summary>
/// One closed domain-local correspondence outcome.
/// </summary>
public abstract class ResearchTargetCorrespondenceOutcome
{
    private protected ResearchTargetCorrespondenceOutcome(
        ResearchTargetCorrespondenceKind kind,
        ResearchTargetDomain domain)
    {
        Kind = kind;
        Domain = domain;
    }

    public ResearchTargetCorrespondenceKind Kind { get; }

    public ResearchTargetDomain Domain { get; }

    public ResearchTargetDomainId DomainId => Domain.Id;

    public ResearchTargetScopeId Scope => Domain.Scope;

    public sealed class Paired : ResearchTargetCorrespondenceOutcome
    {
        internal Paired(
            ResearchTargetDomain domain,
            ResearchCorrespondingTarget before,
            ResearchCorrespondingTarget after)
            : base(ResearchTargetCorrespondenceKind.Paired, domain)
        {
            Before = before;
            After = after;
        }

        public ResearchCorrespondingTarget Before { get; }

        public ResearchCorrespondingTarget After { get; }
    }

    public sealed class BeforeOnly : ResearchTargetCorrespondenceOutcome
    {
        internal BeforeOnly(
            ResearchTargetDomain domain,
            ResearchCorrespondingTarget before,
            ResearchTargetKeyAbsenceProof afterAbsence)
            : base(ResearchTargetCorrespondenceKind.BeforeOnly, domain)
        {
            Before = before;
            AfterAbsence = afterAbsence;
        }

        public ResearchCorrespondingTarget Before { get; }

        public ResearchTargetKeyAbsenceProof AfterAbsence { get; }
    }

    public sealed class AfterOnly : ResearchTargetCorrespondenceOutcome
    {
        internal AfterOnly(
            ResearchTargetDomain domain,
            ResearchTargetKeyAbsenceProof beforeAbsence,
            ResearchCorrespondingTarget after)
            : base(ResearchTargetCorrespondenceKind.AfterOnly, domain)
        {
            BeforeAbsence = beforeAbsence;
            After = after;
        }

        public ResearchTargetKeyAbsenceProof BeforeAbsence { get; }

        public ResearchCorrespondingTarget After { get; }
    }

    public sealed class Absent : ResearchTargetCorrespondenceOutcome
    {
        internal Absent(
            ResearchTargetDomain domain,
            ResearchTargetDomainAbsenceProof beforeAbsence,
            ResearchTargetDomainAbsenceProof afterAbsence)
            : base(ResearchTargetCorrespondenceKind.Absent, domain)
        {
            BeforeAbsence = beforeAbsence;
            AfterAbsence = afterAbsence;
        }

        public ResearchTargetDomainAbsenceProof BeforeAbsence { get; }

        public ResearchTargetDomainAbsenceProof AfterAbsence { get; }
    }

    public sealed class CounterpartUnavailable :
        ResearchTargetCorrespondenceOutcome
    {
        internal CounterpartUnavailable(
            ResearchTargetDomain domain,
            ResearchTargetAttempt attempt,
            ResearchCorrespondingTarget? keyedTarget,
            ResearchTargetTaintEvidence taint)
            : base(
                ResearchTargetCorrespondenceKind.CounterpartUnavailable,
                domain)
        {
            Attempt = attempt;
            StrictKey = keyedTarget?.StrictKey;
            CorrespondenceKey = keyedTarget?.CorrespondenceKey;
            Taint = taint;
        }

        public ResearchTargetAttempt Attempt { get; }

        public ResearchTargetOutcome.Resolved Target =>
            (ResearchTargetOutcome.Resolved)Attempt.Outcome;

        public ResearchStrictTargetKey? StrictKey { get; }

        public ResearchTargetCorrespondenceKey? CorrespondenceKey { get; }

        public ResearchTargetTaintEvidence Taint { get; }
    }

    public sealed class DomainUnavailable :
        ResearchTargetCorrespondenceOutcome
    {
        internal DomainUnavailable(
            ResearchTargetDomain domain,
            ResearchTargetTaintEvidence taint)
            : base(ResearchTargetCorrespondenceKind.DomainUnavailable, domain)
            => Taint = taint;

        public ResearchTargetTaintEvidence Taint { get; }
    }
}

internal sealed record ResearchTargetCorrespondenceProjection(
    ImmutableArray<ResearchTargetDomainSideCensus> Censuses,
    ImmutableArray<ResearchTargetCorrespondenceOutcome> Outcomes);

internal static class ResearchTargetCorrespondenceBuilder
{
    internal static ResearchTargetCorrespondenceProjection Build(
        ImmutableArray<ResearchTargetScope> scopes)
    {
        var censuses =
            ImmutableArray.CreateBuilder<ResearchTargetDomainSideCensus>();
        var outcomes =
            ImmutableArray.CreateBuilder<ResearchTargetCorrespondenceOutcome>();

        foreach (ResearchTargetDomain domain in
            scopes.SelectMany(static scope => scope.Domains))
        {
            ResearchTargetDomainSideCensus before =
                Census(domain, ResearchComparisonSide.Before);
            ResearchTargetDomainSideCensus after =
                Census(domain, ResearchComparisonSide.After);
            censuses.Add(before);
            censuses.Add(after);
            BuildDomain(domain, before, after, outcomes);
        }

        return new(
            censuses.ToImmutable(),
            outcomes.ToImmutable());
    }

    static ResearchTargetDomainSideCensus Census(
        ResearchTargetDomain domain,
        ResearchComparisonSide side)
    {
        ImmutableArray<ResearchTargetInputDisposition> inputs =
            domain.Side(side);
        ImmutableArray<ResearchTargetAttempt> attempts =
        [
            .. domain.Attempts.Where(attempt => attempt.Request.Side == side),
        ];
        int requested = inputs.Count(
            input => input.Kind == ResearchTargetDispositionKind.Requested);
        bool healthy =
            inputs.All(
                input => input.Kind == ResearchTargetDispositionKind.Requested)
            && attempts.Length == requested
            && attempts.All(
                attempt => attempt.Outcome.Kind
                    is ResearchTargetOutcomeKind.Resolved
                        or ResearchTargetOutcomeKind.NotFound);
        return new(
            domain,
            side,
            inputs,
            attempts,
            healthy
                ? ResearchTargetCensusHealth.Healthy
                : ResearchTargetCensusHealth.Blocked);
    }

    static void BuildDomain(
        ResearchTargetDomain domain,
        ResearchTargetDomainSideCensus before,
        ResearchTargetDomainSideCensus after,
        ImmutableArray<ResearchTargetCorrespondenceOutcome>.Builder outcomes)
    {
        ImmutableArray<ResearchTargetAttempt> resolvedAttempts =
        [
            .. domain.Attempts
                .Where(attempt =>
                    attempt.Outcome is ResearchTargetOutcome.Resolved),
        ];

        if (before.Health == ResearchTargetCensusHealth.Blocked
            || after.Health == ResearchTargetCensusHealth.Blocked)
        {
            ResearchTargetTaintEvidence taint = BlockedTaint(domain);
            if (resolvedAttempts.IsEmpty)
            {
                outcomes.Add(
                    new ResearchTargetCorrespondenceOutcome.DomainUnavailable(
                        domain,
                        taint));
            }
            else
            {
                foreach (ResearchTargetAttempt attempt in resolvedAttempts)
                {
                    outcomes.Add(
                        new ResearchTargetCorrespondenceOutcome
                            .CounterpartUnavailable(
                                domain,
                                attempt,
                                keyedTarget: null,
                                taint));
                }
            }

            return;
        }

        ImmutableArray<ResearchCorrespondingTarget> targets =
            [.. resolvedAttempts.Select(Target)];
        ResearchCorrespondingTarget? beforeTarget =
            targets.SingleOrDefault(
                target => target.Side == ResearchComparisonSide.Before);
        ResearchCorrespondingTarget? afterTarget =
            targets.SingleOrDefault(
                target => target.Side == ResearchComparisonSide.After);

        if (beforeTarget is not null && afterTarget is not null)
        {
            if (beforeTarget.CorrespondenceKey.Equals(
                    afterTarget.CorrespondenceKey))
            {
                outcomes.Add(
                    new ResearchTargetCorrespondenceOutcome.Paired(
                        domain,
                        beforeTarget,
                        afterTarget));
                return;
            }

            ResearchTargetTaintEvidence taint = new(
                ResearchTargetTaintKind.SelectionDrift,
                domain,
                [beforeTarget.Attempt, afterTarget.Attempt],
                [beforeTarget.StrictKey, afterTarget.StrictKey],
                []);
            outcomes.Add(
                new ResearchTargetCorrespondenceOutcome.CounterpartUnavailable(
                    domain,
                    beforeTarget.Attempt,
                    beforeTarget,
                    taint));
            outcomes.Add(
                new ResearchTargetCorrespondenceOutcome.CounterpartUnavailable(
                    domain,
                    afterTarget.Attempt,
                    afterTarget,
                    taint));
            return;
        }

        if (beforeTarget is not null)
        {
            if (TryKeyAbsence(after, beforeTarget, out var proof))
            {
                outcomes.Add(
                    new ResearchTargetCorrespondenceOutcome.BeforeOnly(
                        domain,
                        beforeTarget,
                        proof));
            }
            else
            {
                outcomes.Add(
                    new ResearchTargetCorrespondenceOutcome
                        .CounterpartUnavailable(
                            domain,
                            beforeTarget.Attempt,
                            beforeTarget,
                            AbsenceTaint(domain, beforeTarget, after)));
            }

            return;
        }

        if (afterTarget is not null)
        {
            if (TryKeyAbsence(before, afterTarget, out var proof))
            {
                outcomes.Add(
                    new ResearchTargetCorrespondenceOutcome.AfterOnly(
                        domain,
                        proof,
                        afterTarget));
            }
            else
            {
                outcomes.Add(
                    new ResearchTargetCorrespondenceOutcome
                        .CounterpartUnavailable(
                            domain,
                            afterTarget.Attempt,
                            afterTarget,
                            AbsenceTaint(domain, afterTarget, before)));
            }

            return;
        }

        if (!TryDomainAbsence(before, out var beforeProof)
            || !TryDomainAbsence(after, out var afterProof))
        {
            throw new InvalidOperationException(
                "A healthy domain without targets must prove both sides absent.");
        }

        outcomes.Add(
            new ResearchTargetCorrespondenceOutcome.Absent(
                domain,
                beforeProof,
                afterProof));
    }

    static ResearchCorrespondingTarget Target(ResearchTargetAttempt attempt)
    {
        var resolved =
            (ResearchTargetOutcome.Resolved)attempt.Outcome;
        MetadataMethodAddress? address =
            resolved.Role == ResearchTargetRelationshipRole.None
                ? null
                : resolved.Address;
        MemberAnchor? anchor =
            resolved.Role == ResearchTargetRelationshipRole.None
                ? resolved.Anchor
                : null;
        var strict = new ResearchStrictTargetKey(
            attempt.Request.Scope,
            attempt.Request.Domain,
            attempt.Request.Input,
            resolved.Role,
            address,
            anchor);
        string canonical =
            resolved.Role == ResearchTargetRelationshipRole.None
                ? resolved.Anchor.CanonicalSignature
                : ResearchMemberIdentity.CanonicalBodyIdentity(resolved.Target);
        var correspondence = new ResearchTargetCorrespondenceKey(
            attempt.Request.Scope,
            attempt.Request.Domain,
            resolved.Role,
            canonical);
        return new ResearchCorrespondingTarget(
            attempt,
            strict,
            correspondence);
    }

    static ResearchTargetTaintEvidence BlockedTaint(
        ResearchTargetDomain domain)
        => new(
            ResearchTargetTaintKind.BlockedDomain,
            domain,
            [
                .. domain.Attempts.Where(
                    attempt => attempt.Outcome.Kind
                        is not (ResearchTargetOutcomeKind.Resolved
                            or ResearchTargetOutcomeKind.NotFound)),
            ],
            [],
            [
                .. domain.Inputs.Where(
                    input =>
                        input.Kind
                            == ResearchTargetDispositionKind.NotRequested),
            ]);

    static ResearchTargetTaintEvidence AbsenceTaint(
        ResearchTargetDomain domain,
        ResearchCorrespondingTarget target,
        ResearchTargetDomainSideCensus opposite)
        => new(
            ResearchTargetTaintKind.AbsenceNotProven,
            domain,
            [target.Attempt, .. opposite.Attempts],
            [target.StrictKey],
            []);

    static bool TryKeyAbsence(
        ResearchTargetDomainSideCensus census,
        ResearchCorrespondingTarget target,
        out ResearchTargetKeyAbsenceProof proof)
    {
        if (census.Inputs.IsEmpty)
        {
            proof = new(
                census,
                target.CorrespondenceKey,
                ResearchTargetAbsenceEvidenceKind.NoAdmittedInput,
                notFoundAttempt: null);
            return true;
        }

        ResearchTargetAttempt? attempt =
            census.Attempts.SingleOrDefault();
        if (attempt?.Outcome is ResearchTargetOutcome.NotFound notFound
            && Covers(attempt.Request, notFound, target))
        {
            proof = new(
                census,
                target.CorrespondenceKey,
                ResearchTargetAbsenceEvidenceKind.NotFound,
                attempt);
            return true;
        }

        proof = null!;
        return false;
    }

    static bool TryDomainAbsence(
        ResearchTargetDomainSideCensus census,
        out ResearchTargetDomainAbsenceProof proof)
    {
        if (census.Inputs.IsEmpty)
        {
            proof = new(
                census,
                ResearchTargetAbsenceEvidenceKind.NoAdmittedInput,
                notFoundAttempt: null);
            return true;
        }

        ResearchTargetAttempt? attempt =
            census.Attempts.SingleOrDefault();
        if (attempt?.Outcome is ResearchTargetOutcome.NotFound)
        {
            proof = new(
                census,
                ResearchTargetAbsenceEvidenceKind.NotFound,
                attempt);
            return true;
        }

        proof = null!;
        return false;
    }

    static bool Covers(
        ResearchTargetRequest request,
        ResearchTargetOutcome.NotFound notFound,
        ResearchCorrespondingTarget target)
    {
        if (notFound.ResearchDiagnostic?.Kind
            == ResearchTargetDiagnosticKind.DeclaringTypeAbsent)
        {
            return true;
        }

        return notFound.MetadataDiagnostic?.Kind switch
        {
            MemberTargetDiagnosticKind.MissingMember => true,
            MemberTargetDiagnosticKind.DigestNotFound
                when request.Selector.DigestPrefix is { Length: > 0 } digest =>
                target.Target.Anchor.Fingerprint.StartsWith(
                    digest,
                    StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }
}
