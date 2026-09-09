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
        var policy = new RetainedGroupBindingPolicy(
            new SourceRelativeAssemblyGroupBindingPolicy(
                retained.Select(item => (
                    item.Assembly,
                    item.Participant.BindingPolicy))),
            retained.Select(item => item.Assembly.Registration));
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

    // TypeResolutionContext discovers and opens policy-selected candidates. This
    // query may consume retained group images, but must not acquire a new image.
    sealed class RetainedGroupBindingPolicy(
        IAssemblyBindingPolicy inner,
        IEnumerable<AssemblyAcquisitionRegistration> registrations) : IAssemblyBindingPolicy
    {
        readonly HashSet<AssemblyAcquisitionRegistration> _registrations =
            new(registrations, ReferenceEqualityComparer.Instance);

        public AssemblyBindingPolicyVersion Version => inner.Version;

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
        {
            AssemblyBindingSelectionSnapshot snapshot = inner.Select(request);
            bool outsideGroup = snapshot.Selection switch
            {
                AssemblyBindingSelection.Selected selected =>
                    !_registrations.Contains(selected.Assembly.Registration)
                    || selected.ShadowedAssemblies.Any(assembly => !_registrations.Contains(assembly.Registration)),
                AssemblyBindingSelection.Ambiguous ambiguous =>
                    ambiguous.Assemblies.Any(assembly => !_registrations.Contains(assembly.Registration)),
                _ => false,
            };
            return outsideGroup
                ? new(snapshot.Version, AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(AssemblyBindingFailureKind.CandidateUnavailable)))
                : snapshot;
        }
    }
}
