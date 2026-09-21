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
            CancellationToken cancellationToken = default)
    {
        AssemblyMemberSourcePairResult content =
            await AssemblyContextMemberSourcePairQuery.ExecuteAsync(
                    beforeGroup,
                    beforeParticipant,
                    afterGroup,
                    afterParticipant,
                    request,
                    context,
                    cancellationToken)
                .ConfigureAwait(false);

        return new(
            new ResourcePath("member-source-pair"),
            InspectionContentKind.Result,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
    }
}
