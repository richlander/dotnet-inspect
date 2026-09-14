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

/// <summary>
/// Resolves one exact type definition through the realized participants of an
/// assembly context group.
/// </summary>
public static class AssemblyContextTypeResolutionQuery
{
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
