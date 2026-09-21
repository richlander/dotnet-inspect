using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>Shared completed member Source operations for CLI and Browser/Wasm.</summary>
public static class MemberSourceInspection
{
    public static async Task<InspectionEnvelope<AssemblyMemberDecompilationEntry>>
        DecompileAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyMemberSourceRequest request,
            AssemblyContextSourceQueryContext context,
            AssemblyContextLibraryPortablePdb? portablePdb = null,
            CancellationToken cancellationToken = default)
    {
        AssemblyMemberDecompilationEntry content =
            await AssemblyContextSourceQuery
                .ExecuteMemberDecompilationAsync(
                    group,
                    participant,
                    request,
                    context,
                    portablePdb,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(content, new InspectionShare.NonProjectable(
            "member-decompilation/share",
            "Member decompilation requests do not yet have a canonical Workspace Share projection."));
    }

    public static async Task<InspectionEnvelope<AssemblyMemberSourceEntry>> ExecuteAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyMemberSourceRequest request,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken = default)
    {
        AssemblyMemberSourceEntry content = await AssemblyContextSourceQuery.ExecuteMemberAsync(
            group, participant, request, context, cancellationToken).ConfigureAwait(false);
        return new(content, new InspectionShare.NonProjectable(
            "member-source/share",
            "Member Source requests do not yet have a canonical Workspace Share projection."));
    }

    public static async Task<InspectionEnvelope<AssemblyMemberSourceComparisonEntry>> CompareAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyMemberSourceRequest request,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken = default)
    {
        AssemblyMemberSourceComparisonEntry content = await AssemblyContextSourceComparisonQuery.ExecuteAsync(
            group, participant, request, context, cancellationToken).ConfigureAwait(false);
        return new(content, new InspectionShare.NonProjectable(
            "member-source-comparison/share",
            "Member Source comparison requests do not yet have a canonical Workspace Share projection."));
    }
}
