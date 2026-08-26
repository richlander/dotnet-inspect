using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

internal static class AssemblyContextAnalysisSource
{
    internal static string Name(AssemblyContextSubject subject) =>
        subject.Identity.Name;

    internal static IAssemblyReferenceResolver Resolver(
        AssemblyContextGroup group,
        AssemblyContextSubject subject) =>
        new BindingPolicyResolver(group, Participant(group, subject));

    static AssemblyContextParticipant Participant(
        AssemblyContextGroup group,
        AssemblyContextSubject subject) =>
        group.Participants.Single(
            candidate => ReferenceEquals(
                candidate.Assembly.Registration,
                subject.Registration));

    sealed class BindingPolicyResolver(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
        : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            ArgumentNullException.ThrowIfNull(identity);
            AssemblyBindingSelection selection =
                participant.BindingPolicy.Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(identity),
                        AssemblyBindingOrigin.FromAssembly(
                            participant.Assembly),
                        scope));
            if (selection
                is not AssemblyBindingSelection.Selected selected)
            {
                return null;
            }

            ImmutableArray<AssemblyContextParticipant> participants =
                group.Participants;
            if (!participants.Any(
                    candidate => ReferenceEquals(
                        candidate.Assembly.Registration,
                        selected.Assembly.Registration)))
            {
                return null;
            }

            return group.RetainAssemblyReference(selected.Assembly)
                is AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Available retained
                    ? retained.Value
                    : null;
        }
    }
}
