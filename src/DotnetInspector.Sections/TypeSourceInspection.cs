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
            InspectionContentKind.Outcome,
            content,
            new InspectionPortableProjection.NonProjectable(
                "type-decompilation/share",
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
            InspectionContentKind.Outcome,
            content,
            new InspectionPortableProjection.NonProjectable(
                "type-source/share",
                InspectionPortableProjectionFailureReason.NotSupported));
    }

    /// <summary>
    /// Executes ordinary authored-first type source with finite PDB and
    /// authored-source preference windows. Explicit document requests retain
    /// the serial exact-document operation.
    /// </summary>
    public static async Task<InspectionEnvelope<AssemblyTypeSourceEntry>>
        ExecuteWithLatencyHedgeAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyTypeSourceRequest request,
            AssemblyContextSourceQueryContext context,
            TypeSourceLatencyHedge latencyHedge,
            CancellationToken cancellationToken = default)
    {
        AssemblyTypeSourceEntry content =
            await AssemblyContextSourceQuery
                .ExecuteTypeWithLatencyHedgeAsync(
                    group,
                    participant,
                    request,
                    context,
                    latencyHedge,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(
            InspectionContentKind.Outcome,
            content,
            new InspectionPortableProjection.NonProjectable(
                "type-source/share",
                InspectionPortableProjectionFailureReason.NotSupported));
    }
}
