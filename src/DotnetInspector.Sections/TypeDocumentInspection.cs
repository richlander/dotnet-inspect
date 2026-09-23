using DotnetInspector.Queries;
using DotnetInspector.SourceHouse;
using ILInspector.Decompiler;

namespace DotnetInspector.Sections;

/// <summary>Shared completed structured C# Type document operation.</summary>
public static class TypeDocumentInspection
{
    public static async Task<
        InspectionEnvelope<CSharpTypeDocumentOutcome>> ExecuteAsync(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            AssemblyTypeSourceRequest request,
            AssemblyContextSourceQueryContext context,
            AssemblyContextLibraryPortablePdb? portablePdb = null,
            CancellationToken cancellationToken = default)
    {
        AssemblyTypeDocumentEntry entry =
            await AssemblyContextSourceQuery
                .ExecuteTypeDocumentAsync(
                    group,
                    participant,
                    request,
                    context,
                    portablePdb,
                    cancellationToken)
                .ConfigureAwait(false);
        CSharpTypeDocumentOutcome content = Content(entry);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "type-document/share",
                "Structured Type document requests do not yet have a canonical Workspace Share projection."),
            Diagnostics(entry, content));
    }

    private static CSharpTypeDocumentOutcome Content(
        AssemblyTypeDocumentEntry entry) =>
        entry switch
        {
            AssemblyTypeDocumentEntry.Settled settled =>
                settled.Outcome,
            AssemblyTypeDocumentEntry.Rejected rejected =>
                new CSharpTypeDocumentOutcome.Rejected(
                    $"Structured Type document inspection rejected the assembly candidate: {rejected.Failure.Kind}: {rejected.Failure.Detail}"),
            AssemblyTypeDocumentEntry.Unavailable unavailable =>
                new CSharpTypeDocumentOutcome.Unavailable(
                    $"Structured Type document inspection is unavailable: {unavailable.Failure.Kind}: {unavailable.Failure.Detail}"),
            _ => throw new InvalidOperationException(
                "Unknown structured Type document entry."),
        };

    private static IEnumerable<InspectionDiagnostic> Diagnostics(
        AssemblyTypeDocumentEntry entry,
        CSharpTypeDocumentOutcome content)
    {
        if (entry is AssemblyTypeDocumentEntry.Settled settled)
        {
            foreach (InspectionDiagnostic diagnostic in
                HouseDiagnostics(settled.HouseOutcome))
            {
                yield return diagnostic;
            }
        }
        else if (entry is AssemblyTypeDocumentEntry.Rejected)
        {
            yield return new(
                "type-document.request-rejected",
                InspectionDiagnosticSeverity.Error,
                "The structured Type document request was rejected before SourceHouse settlement.");
        }
        else if (entry is AssemblyTypeDocumentEntry.Unavailable)
        {
            yield return new(
                "type-document.inspection-unavailable",
                InspectionDiagnosticSeverity.Error,
                "The structured Type document operation could not reach SourceHouse settlement.");
        }

        InspectionDiagnostic? outcomeDiagnostic = content switch
        {
            CSharpTypeDocumentOutcome.Incomplete =>
                new(
                    "type-document.incomplete",
                    InspectionDiagnosticSeverity.Warning,
                    "The structured Type document preserves incomplete body production."),
            CSharpTypeDocumentOutcome.Unavailable =>
                new(
                    "type-document.unavailable",
                    InspectionDiagnosticSeverity.Warning,
                    "Structured Type document production is unavailable."),
            CSharpTypeDocumentOutcome.Rejected =>
                new(
                    "type-document.rejected",
                    InspectionDiagnosticSeverity.Error,
                    "Structured Type document production rejected its input."),
            _ => null,
        };
        if (outcomeDiagnostic is not null)
            yield return outcomeDiagnostic;
    }

    private static IEnumerable<InspectionDiagnostic> HouseDiagnostics(
        SourceHouseDecompilationOutcome outcome)
    {
        InspectionDiagnostic? pdbDiagnostic =
            outcome.PdbContribution.Kind switch
            {
                SourceHousePdbContributionKind.SuppliedCompanion =>
                    new(
                        "type-document.portable-pdb.supplied",
                        InspectionDiagnosticSeverity.Information,
                        "The structured Type document used the selected Library companion Portable PDB."),
                SourceHousePdbContributionKind.Embedded =>
                    new(
                        "type-document.portable-pdb.embedded",
                        InspectionDiagnosticSeverity.Information,
                        "The structured Type document used the selected assembly's embedded Portable PDB."),
                SourceHousePdbContributionKind.Unavailable =>
                    new(
                        "type-document.portable-pdb.unavailable",
                        InspectionDiagnosticSeverity.Information,
                        "No selected Library companion or embedded Portable PDB was available; production continued without symbols."),
                SourceHousePdbContributionKind.Incomplete =>
                    new(
                        "type-document.portable-pdb.incomplete",
                        InspectionDiagnosticSeverity.Warning,
                        "Portable PDB contribution exceeded its SourceHouse boundary; production continued without it."),
                SourceHousePdbContributionKind.Failed =>
                    new(
                        "type-document.portable-pdb.failed",
                        InspectionDiagnosticSeverity.Warning,
                        "Portable PDB contribution failed during SourceHouse settlement."),
                SourceHousePdbContributionKind.Rejected =>
                    new(
                        "type-document.portable-pdb.rejected",
                        InspectionDiagnosticSeverity.Warning,
                        "Portable PDB contribution was rejected during SourceHouse settlement."),
                _ => null,
            };
        if (pdbDiagnostic is not null)
            yield return pdbDiagnostic;

        InspectionDiagnostic? settlementDiagnostic = outcome switch
        {
            SourceHouseDecompilationOutcome.Incomplete =>
                new(
                    "type-document.source-house.incomplete",
                    InspectionDiagnosticSeverity.Warning,
                    "SourceHouse could not complete structured Type document production within its finite boundaries."),
            SourceHouseDecompilationOutcome.Rejected =>
                new(
                    "type-document.source-house.rejected",
                    InspectionDiagnosticSeverity.Error,
                    "SourceHouse rejected structured Type document production."),
            SourceHouseDecompilationOutcome.Failed =>
                new(
                    "type-document.source-house.failed",
                    InspectionDiagnosticSeverity.Error,
                    "SourceHouse failed structured Type document production."),
            _ => null,
        };
        if (settlementDiagnostic is not null)
            yield return settlementDiagnostic;
    }
}
