using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public static partial class AssemblyContextMemberSourcePairQuery
{
    static async Task<AssemblyMemberSourcePairEndpoint.Resolved> AcquireAuthoredEndpointAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyContextSubject subject,
        AssemblyMemberSourceRequest request,
        AssemblyContextSourceQueryContext context,
        ResolvedAssemblyReference retained,
        AssemblyBindingPolicyVersion version,
        CancellationToken cancellationToken)
    {
        AssemblyContextSourceQuery.MemberPdbInspection pdb =
            await AssemblyContextSourceQuery.InspectMemberPdbAsync(
                group, participant, request, context, retained, version,
                context.MemberSourcePairLimits, context.MemberSourcePairTimeout,
                cancellationToken).ConfigureAwait(false);
        return new(subject, request, pdb.ToAttempt())
        {
            HouseOutcome = pdb.HouseOutcome,
            LibraryFailure = pdb.LibraryFailure,
        };
    }
}
