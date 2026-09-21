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
        return new(
            new ResourcePath("member-decompilation"),
            InspectionContentKind.Outcome,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
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
        return new(
            new ResourcePath("member-source"),
            InspectionContentKind.Outcome,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
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
        return new(
            new ResourcePath("member-source-comparison"),
            InspectionContentKind.Outcome,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
    }
}
