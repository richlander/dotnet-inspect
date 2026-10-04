using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Inspectors;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

internal sealed record LibraryDocumentInspection(
    LibraryDocument? Document,
    string? Failure)
{
    private static readonly LibraryInspectionPlan s_libraryInfoPlan =
        new(
            types: null,
            DirectLibraryInspectionCommand.s_bounds,
            new LibraryEnablementsRequest(),
            new LibraryImageFactsRequest(),
            new LibraryDescriptionFactsRequest());

    internal static async Task<LibraryDocumentInspection?> ReadAsync(
        LibraryInspection inspection,
        string path,
        string? packageName,
        bool isPlatformAssembly,
        bool requested)
    {
        if (!requested || inspection.AssemblyInfo?.AssemblyName is null)
            return null;

        InspectionEnvelope<LibraryInspectionOutcome>? envelope =
            await ExactLibraryInspectionExecutor.ExecuteAsync(
                    path,
                    "library info",
                    session => session.Execute(
                        s_libraryInfoPlan,
                        CancellationToken.None),
                    CancellationToken.None,
                    LibraryMetadataService.LibraryInfoRole(
                        path,
                        packageName,
                        isPlatformAssembly))
                .ConfigureAwait(false);
        return envelope?.Content switch
        {
            LibraryInspectionOutcome.Available available =>
                new(available.Document, null),
            LibraryInspectionOutcome.Rejected rejected =>
                new(null, rejected.Reason.ToString()),
            LibraryInspectionOutcome.Failed failed =>
                new(null, failed.Reason.ToString()),
            _ => new(null, "LibraryUnavailable"),
        };
    }
}

internal sealed record LibraryInspectionRenderInput(
    LibraryInspection Inspection,
    LibraryDocumentInspection? DocumentInspection);
