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

        if (!group.Participants.Any(participant =>
                ReferenceEquals(
                    participant.Assembly.Registration,
                    root.Assembly.Registration)))
        {
            throw new ArgumentException(
                "The resolution root must belong to the assembly context group.",
                nameof(root));
        }

        var retained = new List<(
            AssemblyContextParticipant Participant,
            ResolvedAssemblyReference Assembly)>();
        foreach (AssemblyContextParticipant participant
            in group.Participants)
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
                    return new AssemblyContextTypeResolutionResult
                        .Rejected(
                            rejected.Assembly,
                            rejected.Failure);
            }
        }

        ResolvedAssemblyReference retainedRoot =
            retained.Single(item => ReferenceEquals(
                item.Participant.Assembly.Registration,
                root.Assembly.Registration)).Assembly;
        var policy = new SourceRelativeAssemblyGroupBindingPolicy(
            retained.Select(item => (
                item.Assembly,
                item.Participant.BindingPolicy)));
        TypeResolutionRequest request =
            TypeResolutionRequest.FromAssembly(
                retainedRoot,
                scope,
                type);
        using TypeResolutionContext context =
            TypeResolutionContext.Create(
                policy,
                retained.Select(item => item.Assembly),
                [request]);
        return new AssemblyContextTypeResolutionResult.Available(
            context.Resolve(request));
    }
}
