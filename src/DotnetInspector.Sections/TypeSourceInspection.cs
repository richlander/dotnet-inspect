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
        return new(content, new InspectionShare.NonProjectable(
            "type-decompilation/share",
            "Type decompilation requests do not yet have a canonical Workspace Share projection."));
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
        return Envelope(content);
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
        return Envelope(content);
    }

    public static async Task<EvidenceInspectionEnvelope<
        AssemblyTypeSourceEntry,
        TypeSourcePdbAcquisitionEvidence>>
        ExecuteWithPdbLatencyHedgeAndEvidenceAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyTypeSourceRequest request,
            AssemblyContextSourceQueryContext context,
            TypeSourcePdbLatencyHedge latencyHedge,
            CancellationToken cancellationToken = default)
    {
        (
            AssemblyTypeSourceEntry content,
            TypeSourcePdbAcquisitionEvidence evidence) =
            await AssemblyContextSourceQuery
                .ExecuteTypeWithPdbLatencyHedgeAndEvidenceAsync(
                    group,
                    participant,
                    request,
                    context,
                    latencyHedge,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(Envelope(content), evidence);
    }

    private static InspectionEnvelope<AssemblyTypeSourceEntry> Envelope(
        AssemblyTypeSourceEntry content) =>
        new(
            content,
            new InspectionShare.NonProjectable(
                "type-source/share",
                "Type Source requests do not yet have a canonical Workspace Share projection."),
            PdbDiagnostics(content));

    private static IEnumerable<InspectionDiagnostic> PdbDiagnostics(
        AssemblyTypeSourceEntry content)
    {
        TypeSourcePortablePdbDisposition disposition =
            TypeSourcePdbAcquisitionEvidence.Classify(content);
        InspectionDiagnostic? diagnostic = disposition switch
        {
            TypeSourcePortablePdbDisposition.Available =>
                new(
                    "type-source.portable-pdb.available",
                    InspectionDiagnosticSeverity.Information,
                    "A matching Portable PDB was available to the Type Source operation."),
            TypeSourcePortablePdbDisposition.Unavailable =>
                new(
                    "type-source.portable-pdb.unavailable",
                    InspectionDiagnosticSeverity.Information,
                    "No matching Portable PDB was available; Type Source continued without one."),
            TypeSourcePortablePdbDisposition.PreferenceWindowElapsed =>
                new(
                    "type-source.portable-pdb.preference-window-elapsed",
                    InspectionDiagnosticSeverity.Information,
                    "Portable PDB acquisition did not settle within the preference window; Type Source continued without it."),
            TypeSourcePortablePdbDisposition.AcquisitionFailed =>
                new(
                    "type-source.portable-pdb.acquisition-failed",
                    InspectionDiagnosticSeverity.Warning,
                    "Portable PDB acquisition failed; Type Source continued with the remaining available providers."),
            _ => null,
        };
        if (diagnostic is not null)
            yield return diagnostic;
    }
}
