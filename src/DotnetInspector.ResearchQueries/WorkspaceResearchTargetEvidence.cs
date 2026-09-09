using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.Research;
using InertText;

namespace DotnetInspector.Queries;

/// <summary>Materialized selection intent, without a Metadata selector object.</summary>
public sealed record WorkspaceResearchTargetSelectorEvidence(
    InertString RequestedText,
    InertString Name,
    int? OverloadIndex,
    InertString? DigestPrefix,
    InertString? Kind,
    int? GenericArity,
    InertString NormalizedSelector);

/// <summary>A durable MethodDef address, without a metadata handle or reader.</summary>
public readonly record struct WorkspaceResearchTargetMethodAddress(Guid ModuleVersionId, int Token);

/// <summary>The assembly and module facts of the selected Analysis module.</summary>
public sealed record WorkspaceResearchTargetModuleEvidence(
    AssemblyReferenceIdentity? AssemblyIdentity,
    Guid ModuleVersionId);

/// <summary>The physical member relationships checked during terminal validation.</summary>
public sealed record WorkspaceResearchTargetMemberTokens(
    int? Method,
    int? Getter,
    int? Setter,
    int? Adder,
    int? Remover);

/// <summary>Owner-issued request identity and materialized selection intent.</summary>
public sealed class WorkspaceResearchTargetRequestEvidence
{
    internal WorkspaceResearchTargetRequestEvidence(
        ResearchTargetRequestId id,
        ResearchTargetRequestKind kind,
        InertString declaringTypeFullName,
        WorkspaceResearchTargetSelectorEvidence selector,
        WorkspaceResearchTargetMethodAddress? assertedAddress,
        ResearchTargetRelationshipRole? assertedRole)
    {
        Id = id;
        Kind = kind;
        DeclaringTypeFullName = declaringTypeFullName;
        Selector = selector;
        AssertedAddress = assertedAddress;
        AssertedRole = assertedRole;
    }

    public ResearchTargetRequestId Id { get; }
    public ResearchComparisonOperationId Operation => Id.Operation;
    public ResearchComparisonQuestionId Question => Id.Question;
    public ResearchComparisonInputId Input => Id.Input;
    public ResearchTargetScopeId Scope => Id.Scope;
    public ResearchTargetDomainId Domain => Id.Domain;
    public ResearchComparisonSide Side => Id.Side;
    public ResearchTargetRequestKind Kind { get; }
    public InertString DeclaringTypeFullName { get; }
    public WorkspaceResearchTargetSelectorEvidence Selector { get; }
    public WorkspaceResearchTargetMethodAddress? AssertedAddress { get; }
    public ResearchTargetRelationshipRole? AssertedRole { get; }
}

/// <summary>
/// A closed projection of one existing Research attempt. Only the owner-issued
/// identity and inert evidence survive; targets, candidates and diagnostics do not.
/// </summary>
public abstract class WorkspaceResearchTargetAttemptEvidence
{
    private WorkspaceResearchTargetAttemptEvidence(
        ResearchTargetAttemptId id,
        WorkspaceResearchTargetRequestEvidence request,
        ResearchTargetOutcomeKind kind)
    {
        Id = id;
        Request = request;
        Kind = kind;
    }

    public ResearchTargetAttemptId Id { get; }
    public WorkspaceResearchTargetRequestEvidence Request { get; }
    public ResearchTargetOutcomeKind Kind { get; }

    public sealed class Resolved : WorkspaceResearchTargetAttemptEvidence
    {
        internal Resolved(
            ResearchTargetAttemptId id,
            WorkspaceResearchTargetRequestEvidence request,
            WorkspaceResearchTargetMethodAddress? address,
            ResearchTargetRelationshipRole role,
            WorkspaceResearchTargetModuleEvidence module,
            WorkspaceMetadataEvidence.TypeName? declaringType,
            int? declaringTypeToken,
            InertString normalizedSelector,
            int? bodyToken,
            WorkspaceResearchTargetMemberTokens memberTokens)
            : base(id, request, ResearchTargetOutcomeKind.Resolved)
        {
            Address = address;
            Role = role;
            Module = module;
            DeclaringType = declaringType;
            DeclaringTypeToken = declaringTypeToken;
            NormalizedSelector = normalizedSelector;
            BodyToken = bodyToken;
            MemberTokens = memberTokens;
        }

        public WorkspaceResearchTargetMethodAddress? Address { get; }
        public ResearchTargetRelationshipRole Role { get; }
        public WorkspaceResearchTargetModuleEvidence Module { get; }
        public WorkspaceMetadataEvidence.TypeName? DeclaringType { get; }
        public int? DeclaringTypeToken { get; }
        public InertString NormalizedSelector { get; }
        public int? BodyToken { get; }
        public WorkspaceResearchTargetMemberTokens MemberTokens { get; }
    }

    public sealed class NotFound : WorkspaceResearchTargetAttemptEvidence
    {
        internal NotFound(
            ResearchTargetAttemptId id, WorkspaceResearchTargetRequestEvidence request,
            MemberTargetDiagnosticKind? metadataDiagnostic, ResearchTargetDiagnosticKind? researchDiagnostic)
            : base(id, request, ResearchTargetOutcomeKind.NotFound)
        {
            MetadataDiagnostic = metadataDiagnostic;
            ResearchDiagnostic = researchDiagnostic;
        }

        public MemberTargetDiagnosticKind? MetadataDiagnostic { get; }
        public ResearchTargetDiagnosticKind? ResearchDiagnostic { get; }
    }

    public sealed class Ambiguous : WorkspaceResearchTargetAttemptEvidence
    {
        internal Ambiguous(
            ResearchTargetAttemptId id, WorkspaceResearchTargetRequestEvidence request,
            MemberTargetDiagnosticKind diagnostic)
            : base(id, request, ResearchTargetOutcomeKind.Ambiguous) => Diagnostic = diagnostic;

        public MemberTargetDiagnosticKind Diagnostic { get; }
    }

    public sealed class Rejected : WorkspaceResearchTargetAttemptEvidence
    {
        internal Rejected(
            ResearchTargetAttemptId id, WorkspaceResearchTargetRequestEvidence request,
            MemberTargetDiagnosticKind diagnostic)
            : base(id, request, ResearchTargetOutcomeKind.Rejected) => Diagnostic = diagnostic;

        public MemberTargetDiagnosticKind Diagnostic { get; }
    }

    public sealed class Unavailable : WorkspaceResearchTargetAttemptEvidence
    {
        internal Unavailable(
            ResearchTargetAttemptId id, WorkspaceResearchTargetRequestEvidence request,
            ResearchTargetDiagnosticKind diagnostic)
            : base(id, request, ResearchTargetOutcomeKind.Unavailable) => Diagnostic = diagnostic;

        public ResearchTargetDiagnosticKind Diagnostic { get; }
    }

    public sealed class Failed : WorkspaceResearchTargetAttemptEvidence
    {
        internal Failed(
            ResearchTargetAttemptId id, WorkspaceResearchTargetRequestEvidence request,
            ResearchTargetDiagnosticKind diagnostic)
            : base(id, request, ResearchTargetOutcomeKind.Failed) => Diagnostic = diagnostic;

        public ResearchTargetDiagnosticKind Diagnostic { get; }
    }
}

/// <summary>The complete ordered identity sequences of one validated live census.</summary>
public sealed class WorkspaceResearchTargetCensusEvidence
{
    internal WorkspaceResearchTargetCensusEvidence(
        ResearchTargetScopeId scope,
        ResearchTargetDomainId domainId,
        ResearchComparisonSide side,
        ResearchTargetCensusHealth health,
        ImmutableArray<ResearchComparisonInputId> inputs,
        ImmutableArray<ResearchTargetAttemptId> attempts)
    {
        Scope = scope;
        DomainId = domainId;
        Side = side;
        Health = health;
        Inputs = inputs;
        Attempts = attempts;
    }

    public ResearchTargetScopeId Scope { get; }
    public ResearchTargetDomainId DomainId { get; }
    public ResearchComparisonSide Side { get; }
    public ResearchTargetCensusHealth Health { get; }
    public ImmutableArray<ResearchComparisonInputId> Inputs { get; }
    public ImmutableArray<ResearchTargetAttemptId> Attempts { get; }
}

internal static class WorkspaceResearchTargetEvidenceProjection
{
    internal static WorkspaceResearchTargetAttemptEvidence? Attempt(
        WorkspaceProjectionContext context, ResearchTargetAttempt? attempt)
    {
        if (attempt is null)
            return null;
        ResearchTargetRequest source = attempt.Request;
        MemberTargetSelector selector = source.Selector;
        var request = new WorkspaceResearchTargetRequestEvidence(
            source.Id, source.Kind, Inert(source.DeclaringTypeFullName),
            new(Inert(selector.RequestedText), Inert(selector.Name), selector.OverloadIndex,
                InertOptional(selector.DigestPrefix), InertOptional(selector.Kind), selector.GenericArity,
                Inert(selector.NormalizedSelector)),
            Address(source.AssertedAddress), source.AssertedRole);
        return attempt.Outcome switch
        {
            ResearchTargetOutcome.Resolved resolved => Resolved(context, attempt.Id, request, resolved),
            ResearchTargetOutcome.NotFound notFound => new WorkspaceResearchTargetAttemptEvidence.NotFound(
                attempt.Id, request, notFound.MetadataDiagnostic?.Kind, notFound.ResearchDiagnostic?.Kind),
            ResearchTargetOutcome.Ambiguous ambiguous => new WorkspaceResearchTargetAttemptEvidence.Ambiguous(
                attempt.Id, request, ambiguous.Diagnostic.Kind),
            ResearchTargetOutcome.Rejected rejected => new WorkspaceResearchTargetAttemptEvidence.Rejected(
                attempt.Id, request, rejected.Diagnostic.Kind),
            ResearchTargetOutcome.Unavailable unavailable => new WorkspaceResearchTargetAttemptEvidence.Unavailable(
                attempt.Id, request, unavailable.Diagnostic.Kind),
            ResearchTargetOutcome.Failed failed => new WorkspaceResearchTargetAttemptEvidence.Failed(
                attempt.Id, request, failed.Diagnostic.Kind),
            _ => throw new InvalidOperationException("Unknown Research target outcome."),
        };
    }

    static WorkspaceResearchTargetAttemptEvidence.Resolved Resolved(
        WorkspaceProjectionContext context,
        ResearchTargetAttemptId id,
        WorkspaceResearchTargetRequestEvidence request,
        ResearchTargetOutcome.Resolved outcome)
    {
        ResolvedMemberTarget target = outcome.Target;
        var member = target.ApiMember.Member;
        return new(id, request, Address(outcome.Address), outcome.Role,
            new(outcome.Module.AssemblyIdentity, outcome.Module.ModuleVersionId),
            target.ApiType.DefinitionName is { } definitionName
                ? WorkspaceTypeResolutionProjectionManifest.TypeName.Project(context, definitionName) : null,
            target.ApiType.MetadataToken, Inert(target.NormalizedSelector), target.Body?.MetadataToken,
            new(member.MetadataToken, member.GetterToken, member.SetterToken, member.AdderToken, member.RemoverToken));
    }

    internal static WorkspaceResearchTargetCensusEvidence Census(ResearchTargetDomainSideCensus census)
        => new(census.Scope, census.DomainId, census.Side, census.Health,
            [.. census.Inputs.Select(input => input.Input)],
            [.. census.Attempts.Select(attempt => attempt.Id)]);

    internal static bool Matches(
        WorkspaceResearchTargetCensusEvidence evidence, ResearchTargetDomainSideCensus census)
        => ReferenceEquals(evidence.Scope, census.Scope)
            && ReferenceEquals(evidence.DomainId, census.DomainId)
            && evidence.Side == census.Side
            && evidence.Health == census.Health
            && Enumerable.SequenceEqual(evidence.Inputs, census.Inputs.Select(input => input.Input), ReferenceEqualityComparer.Instance)
            && Enumerable.SequenceEqual(evidence.Attempts, census.Attempts.Select(attempt => attempt.Id), ReferenceEqualityComparer.Instance);

    static WorkspaceResearchTargetMethodAddress? Address(ILInspector.MetadataPrimitives.MetadataMethodAddress? address)
        => address is { } value ? new(value.ModuleVersionId, value.Token) : null;

    static InertString Inert(string value) => WorkspaceTypeResolutionProjectionManifest.Inert(value);
    static InertString? InertOptional(string? value) => WorkspaceTypeResolutionProjectionManifest.InertOptional(value);
}
