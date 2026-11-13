using System.Collections.Immutable;

using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public abstract record AssemblyContextTypeResolutionResult
{
    private AssemblyContextTypeResolutionResult()
    {
    }

    public sealed record Available(
        TypeResolutionOutcome Outcome)
        : AssemblyContextTypeResolutionResult;

    public sealed record Rejected(
        ResolvedAssemblyReference Assembly,
        CandidateOpenFailure Failure)
        : AssemblyContextTypeResolutionResult;

    /// <summary>
    /// The participant policy does not attest acquisition-free selection.
    /// This query has neither opened a participant image nor begun Metadata resolution.
    /// </summary>
    public sealed record UnsupportedBindingPolicy(
        ResolvedAssemblyReference Assembly)
        : AssemblyContextTypeResolutionResult;
}

public enum AssemblyContextTypeResolutionFailureKind
{
    DefinitionNotFound,
    UnboundBinding,
    Unavailable,
    Ambiguous,
    Rejected,
}

/// <summary>
/// Opaque parsed type identity for repeated closed-world resolution attempts.
/// </summary>
public sealed class AssemblyContextTypeResolutionTypeIdentity
{
    internal AssemblyContextTypeResolutionTypeIdentity(
        MetadataTypeDefinitionName type) =>
        Type = type;

    internal MetadataTypeDefinitionName Type { get; }
}

/// <summary>Parses one serialized type identity without exposing Metadata state.</summary>
public abstract record AssemblyContextTypeResolutionTypeIdentityResult
{
    private AssemblyContextTypeResolutionTypeIdentityResult()
    {
    }

    public sealed record Valid(
        AssemblyContextTypeResolutionTypeIdentity Identity)
        : AssemblyContextTypeResolutionTypeIdentityResult;

    public sealed record Rejected(
        MetadataTypeNameRejectionKind Rejection)
        : AssemblyContextTypeResolutionTypeIdentityResult;
}

/// <summary>
/// Projects one closed-world resolution attempt into host-neutral continuation
/// evidence. No result carries an opened image or a Metadata resolution object.
/// </summary>
public abstract record AssemblyContextTypeResolutionDecision
{
    private AssemblyContextTypeResolutionDecision()
    {
    }

    public sealed record Resolved(
        AssemblyContextParticipant Participant)
        : AssemblyContextTypeResolutionDecision;

    public sealed record RequiresAssembly(
        AssemblyReferenceIdentity Assembly)
        : AssemblyContextTypeResolutionDecision;

    public sealed record IdentityConflict(
        AssemblyReferenceIdentity Required,
        ImmutableArray<AssemblyReferenceIdentity> Retained)
        : AssemblyContextTypeResolutionDecision;

    public sealed record Failed(
        AssemblyContextTypeResolutionFailureKind Failure)
        : AssemblyContextTypeResolutionDecision;

    public sealed record QueryRejected(
        AssemblyReferenceIdentity Assembly,
        CandidateOpenFailure Failure)
        : AssemblyContextTypeResolutionDecision;

    public sealed record UnsupportedBindingPolicy(
        AssemblyReferenceIdentity Assembly)
        : AssemblyContextTypeResolutionDecision;
}

/// <summary>
/// Resolves one exact type definition through the realized participants of an
/// assembly context group.
/// </summary>
public static class AssemblyContextTypeResolutionQuery
{
    public static AssemblyContextTypeResolutionTypeIdentityResult
        ParseTypeIdentity(string typeIdentity)
    {
        ArgumentNullException.ThrowIfNull(typeIdentity);
        return MetadataTypeDefinitionName.ParseSerialized(typeIdentity) switch
        {
            MetadataTypeDefinitionNameResult.Valid valid =>
                new AssemblyContextTypeResolutionTypeIdentityResult.Valid(
                    new(valid.Name)),
            MetadataTypeDefinitionNameResult.Rejected rejected =>
                new AssemblyContextTypeResolutionTypeIdentityResult.Rejected(
                    rejected.Rejection.Kind),
            _ => throw new InvalidOperationException(
                "Unknown metadata type-name result."),
        };
    }

    public static AssemblyContextTypeResolutionDecision ExecuteDecision(
        AssemblyContextGroup group,
        AssemblyContextParticipant root,
        AssemblyContextTypeResolutionTypeIdentity typeIdentity,
        AssemblyResolutionScope scope)
    {
        ArgumentNullException.ThrowIfNull(typeIdentity);
        AssemblyContextTypeResolutionResult result =
            Execute(group, root, typeIdentity.Type, scope);
        return result switch
        {
            AssemblyContextTypeResolutionResult.Available available =>
                Project(group.Participants, available.Outcome),
            AssemblyContextTypeResolutionResult.Rejected queryRejected =>
                new AssemblyContextTypeResolutionDecision.QueryRejected(
                    queryRejected.Assembly.Identity,
                    queryRejected.Failure),
            AssemblyContextTypeResolutionResult.UnsupportedBindingPolicy
                unsupported =>
                new AssemblyContextTypeResolutionDecision
                    .UnsupportedBindingPolicy(
                        unsupported.Assembly.Identity),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context type-resolution result."),
        };
    }

    public static AssemblyContextTypeResolutionResult Execute(
        AssemblyContextGroup group,
        AssemblyContextParticipant root,
        MetadataTypeDefinitionName type,
        AssemblyResolutionScope scope)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(type);

        ImmutableArray<AssemblyContextParticipant> participants =
            group.Participants;
        AssemblyBindingPolicyVersion expectedVersion =
            group.BindingPolicyVersion;
        ImmutableArray<IAssemblyBindingPolicy> policies =
            [.. participants.Select(participant => participant.BindingPolicy)];
        if (!participants.Any(participant =>
                ReferenceEquals(
                    participant.Assembly.Registration,
                    root.Assembly.Registration)))
        {
            throw new ArgumentException(
                "The resolution root must belong to the assembly context group.",
                nameof(root));
        }

        EnsureBindingPolicyVersion(policies, expectedVersion);
        foreach (AssemblyContextParticipant participant in participants)
        {
            if (participant.BindingPolicy is not IAcquisitionFreeAssemblyBindingPolicy)
            {
                var rejected = new AssemblyContextTypeResolutionResult
                    .UnsupportedBindingPolicy(participant.Assembly);
                EnsureBindingPolicyVersion(policies, expectedVersion);
                return rejected;
            }
        }

        var retained = new List<(
            AssemblyContextParticipant Participant,
            ResolvedAssemblyReference Assembly)>();
        foreach (AssemblyContextParticipant participant
            in participants)
        {
            AssemblyImageAccessResult<ResolvedAssemblyReference> access =
                group.RetainAssemblyReference(
                    participant.Assembly);
            switch (access)
            {
                case AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Available available:
                    retained.Add((participant, available.Value));
                    break;
                case AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Rejected rejected:
                    var pending = new AssemblyContextTypeResolutionResult
                        .Rejected(
                            rejected.Assembly,
                            rejected.Failure);
                    EnsureBindingPolicyVersion(policies, expectedVersion);
                    return pending;
                default:
                    throw new InvalidOperationException("Unknown participant image-access result.");
            }
        }

        ResolvedAssemblyReference retainedRoot =
            retained.Single(item => ReferenceEquals(
                item.Participant.Assembly.Registration,
                root.Assembly.Registration)).Assembly;
        IAcquisitionFreeAssemblyBindingPolicy policy =
            SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld(
                retained.Select(item => (
                    item.Assembly,
                    (IAcquisitionFreeAssemblyBindingPolicy)item.Participant.BindingPolicy)));
        TypeResolutionRequest request =
            TypeResolutionRequest.FromAssembly(
                retainedRoot,
                scope,
                type);
        TypeResolutionOutcome outcome;
        using (TypeResolutionContext context =
            TypeResolutionContext.Create(
                policy,
                retained.Select(item => item.Assembly),
                [request]))
        {
            outcome = context.Resolve(request);
            EnsureBindingPolicyVersion(policies, expectedVersion);
        }

        var result = new AssemblyContextTypeResolutionResult.Available(outcome);
        EnsureBindingPolicyVersion(policies, expectedVersion);
        return result;
    }

    static AssemblyContextTypeResolutionDecision Project(
        ImmutableArray<AssemblyContextParticipant> participants,
        TypeResolutionOutcome outcome)
    {
        if (outcome is TypeResolutionOutcome.Resolved resolved)
        {
            AssemblyReferenceIdentity identity =
                resolved.Definition.Assembly.Assembly.Identity;
            AssemblyContextParticipant[] matches =
            [
                .. participants.Where(participant =>
                    participant.Assembly.Identity.IsEquivalentTo(identity)),
            ];
            if (matches.Length != 1)
            {
                throw new InvalidOperationException(
                    "A resolved definition must identify exactly one "
                    + "assembly-context participant.");
            }
            return new AssemblyContextTypeResolutionDecision.Resolved(
                matches[0]);
        }

        AssemblyContextTypeResolutionFailureKind failure =
            Failure(outcome);
        AssemblyReferenceIdentity? required =
            outcome.TerminalAssemblyIdentity;
        if (required is null)
        {
            return new AssemblyContextTypeResolutionDecision.Failed(
                failure);
        }

        ImmutableArray<AssemblyContextParticipant> sameName =
        [
            .. participants.Where(participant =>
                participant.Assembly.Identity.Name.Equals(
                    required.Name,
                    StringComparison.OrdinalIgnoreCase)),
        ];
        if (sameName.Any(participant =>
                participant.Assembly.Identity.IsEquivalentTo(required)))
        {
            return new AssemblyContextTypeResolutionDecision.Failed(
                failure);
        }
        if (!sameName.IsEmpty)
        {
            return new AssemblyContextTypeResolutionDecision.IdentityConflict(
                required,
                [.. sameName.Select(participant =>
                    participant.Assembly.Identity)]);
        }
        return new AssemblyContextTypeResolutionDecision.RequiresAssembly(
            required);
    }

    static AssemblyContextTypeResolutionFailureKind Failure(
        TypeResolutionOutcome outcome) =>
        outcome switch
        {
            TypeResolutionOutcome.NotFound =>
                AssemblyContextTypeResolutionFailureKind.DefinitionNotFound,
            TypeResolutionOutcome.UnboundBinding =>
                AssemblyContextTypeResolutionFailureKind.UnboundBinding,
            TypeResolutionOutcome.Unavailable =>
                AssemblyContextTypeResolutionFailureKind.Unavailable,
            TypeResolutionOutcome.Ambiguous =>
                AssemblyContextTypeResolutionFailureKind.Ambiguous,
            TypeResolutionOutcome.Rejected =>
                AssemblyContextTypeResolutionFailureKind.Rejected,
            _ => throw new InvalidOperationException(
                "Unknown unresolved type-resolution outcome."),
        };

    static void EnsureBindingPolicyVersion(
        ImmutableArray<IAssemblyBindingPolicy> policies,
        AssemblyBindingPolicyVersion expected)
    {
        foreach (IAssemblyBindingPolicy policy in policies)
        {
            if (!ReferenceEquals(policy.Version, expected))
                throw new InvalidOperationException(
                    "A participant binding-policy snapshot changed during type resolution.");
        }
    }
}
