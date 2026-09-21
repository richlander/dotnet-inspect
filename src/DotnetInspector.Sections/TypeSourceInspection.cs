using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>Shared completed type Source operation.</summary>
public static class TypeSourceInspection
{
    public static async Task<InspectionEnvelope<AssemblyTypeDecompilationEntry>>
        DecompileAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyTypeSourceRequest request,
            AssemblyContextSourceQueryContext context,
            AssemblyContextLibraryPortablePdb? portablePdb = null,
            CancellationToken cancellationToken = default)
    {
        AssemblyTypeDecompilationEntry content =
            await AssemblyContextSourceQuery
                .ExecuteTypeDecompilationAsync(
                    group,
                    participant,
                    request,
                    context,
                    portablePdb,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(
            new ResourcePath("type-decompilation"),
            InspectionContentKind.Outcome,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
    }

    public static async Task<InspectionEnvelope<AssemblyTypeSourceEntry>> ExecuteAsync(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        AssemblyTypeSourceRequest request,
        AssemblyContextSourceQueryContext context,
        CancellationToken cancellationToken = default)
    {
        AssemblyTypeSourceEntry content = await AssemblyContextSourceQuery.ExecuteTypeAsync(
            group, participant, request, context, cancellationToken).ConfigureAwait(false);
        return new(
            new ResourcePath("type-source"),
            InspectionContentKind.Outcome,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
    }

    /// <summary>
    /// Executes the conservative Portable PDB hedge while preserving serial
    /// authored-first behavior after prompt PDB availability. Explicit
    /// document requests retain the serial exact-document operation.
    /// </summary>
    public static async Task<InspectionEnvelope<AssemblyTypeSourceEntry>>
        ExecuteWithPdbLatencyHedgeAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyTypeSourceRequest request,
            AssemblyContextSourceQueryContext context,
            TypeSourcePdbLatencyHedge latencyHedge,
            CancellationToken cancellationToken = default)
    {
        AssemblyTypeSourceEntry content =
            await AssemblyContextSourceQuery
                .ExecuteTypeWithPdbLatencyHedgeAsync(
                    group,
                    participant,
                    request,
                    context,
                    latencyHedge,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(
            new ResourcePath("type-source"),
            InspectionContentKind.Outcome,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
    }
}
