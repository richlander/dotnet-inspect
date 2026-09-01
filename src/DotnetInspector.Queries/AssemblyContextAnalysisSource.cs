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
        : IAssemblyReferenceResolver,
          IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version =>
            participant.BindingPolicy.Version;

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope)
        {
            ArgumentNullException.ThrowIfNull(identity);
            return Select(
                    new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(identity),
                        AssemblyBindingOrigin.FromAssembly(
                            participant.Assembly),
                        scope))
                is AssemblyBindingSelection.Selected selected
                    ? selected.Assembly
                    : null;
        }

        public AssemblyBindingSelection Select(
            AssemblyBindingRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var participantRequest = new AssemblyBindingRequest(
                request.Target,
                AssemblyBindingOrigin.FromAssembly(
                    participant.Assembly),
                request.Scope);
            AssemblyBindingSelection selection =
                AssemblyBindingSelection.ValidateForRequest(
                    participantRequest,
                    participant.BindingPolicy.Select(
                        participantRequest));
            return selection switch
            {
                AssemblyBindingSelection.Selected selected =>
                    RetainSelected(selected),
                AssemblyBindingSelection.Ambiguous ambiguous =>
                    RetainAmbiguous(ambiguous),
                _ => selection,
            };
        }

        AssemblyBindingSelection RetainSelected(
            AssemblyBindingSelection.Selected selected)
        {
            if (!TryRetain(
                    selected.Assembly,
                    out ResolvedAssemblyReference assembly,
                    out AssemblyBindingSelection failure))
            {
                return failure;
            }

            var shadows =
                ImmutableArray.CreateBuilder<ResolvedAssemblyReference>(
                    selected.ShadowedAssemblies.Length);
            foreach (ResolvedAssemblyReference shadow
                in selected.ShadowedAssemblies)
            {
                if (!TryRetain(
                        shadow,
                        out ResolvedAssemblyReference retained,
                        out failure))
                {
                    return failure;
                }
                shadows.Add(retained);
            }

            return AssemblyBindingSelection.Found(
                assembly,
                shadows.MoveToImmutable());
        }

        AssemblyBindingSelection RetainAmbiguous(
            AssemblyBindingSelection.Ambiguous ambiguous)
        {
            var assemblies =
                ImmutableArray.CreateBuilder<ResolvedAssemblyReference>(
                    ambiguous.Assemblies.Length);
            foreach (ResolvedAssemblyReference assembly
                in ambiguous.Assemblies)
            {
                if (!TryRetain(
                        assembly,
                        out ResolvedAssemblyReference retained,
                        out AssemblyBindingSelection failure))
                {
                    return failure;
                }
                assemblies.Add(retained);
            }

            return AssemblyBindingSelection.Multiple(
                assemblies.MoveToImmutable());
        }

        bool TryRetain(
            ResolvedAssemblyReference assembly,
            out ResolvedAssemblyReference retained,
            out AssemblyBindingSelection failure)
        {
            if (!group.Participants.Any(
                    candidate => ReferenceEquals(
                        candidate.Assembly.Registration,
                        assembly.Registration)))
            {
                retained = null!;
                failure = CandidateUnavailable();
                return false;
            }

            switch (group.RetainAssemblyReference(assembly))
            {
                case AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Available available:
                    retained = available.Value;
                    failure = null!;
                    return true;
                case AssemblyImageAccessResult<
                    ResolvedAssemblyReference>.Rejected rejected:
                    retained = null!;
                    failure = CandidateUnavailable(
                        rejected.Failure.Kind);
                    return false;
                default:
                    throw new InvalidOperationException(
                        "Unknown assembly image access result.");
            }
        }

        static AssemblyBindingSelection CandidateUnavailable(
            CandidateOpenFailureKind? candidateFailureKind = null) =>
            AssemblyBindingSelection.CannotSelect(
                new AssemblyBindingFailure(
                    AssemblyBindingFailureKind.CandidateUnavailable,
                    candidateFailureKind));
    }
}
