using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one exact two-endpoint member source comparison and returns its
/// detached host-neutral inspection envelope.
/// </summary>
public static class MemberSourcePairInspection
{
    public static async Task<
        InspectionEnvelope<AssemblyMemberSourcePairResult>> ExecuteAsync(
            AssemblyContextGroup beforeGroup,
            AssemblyContextParticipant beforeParticipant,
            AssemblyContextGroup afterGroup,
            AssemblyContextParticipant afterParticipant,
            AssemblyMemberSourcePairRequest request,
            AssemblyContextSourceQueryContext context,
            CancellationToken cancellationToken = default,
            Action<AssemblyMemberSourcePairEndpoint, AssemblyMemberSourcePairEndpoint>?
                admitEndpoints = null)
    {
        AssemblyMemberSourcePairResult content =
            await AssemblyContextMemberSourcePairQuery.ExecuteAsync(
                    beforeGroup,
                    beforeParticipant,
                    afterGroup,
                    afterParticipant,
                    request,
                    context,
                    cancellationToken,
                    admitEndpoints)
                .ConfigureAwait(false);

        return new(
            content,
            new InspectionShare.NonProjectable(
                "member-source-pair/share",
                "Member source pair requests do not yet have a canonical Workspace Share projection."));
    }
}
