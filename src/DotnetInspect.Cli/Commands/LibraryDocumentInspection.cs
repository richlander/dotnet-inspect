using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Inspectors;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal sealed record LibraryDocumentInspection(
    InspectionEnvelope<LibraryInspectionOutcome>? Envelope,
    string? ExecutionFailure)
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
        return Project(envelope);
    }

    internal static async Task<LibraryDocumentInspection> ReadExactAsync(
        ResolvedAssemblyReference assembly,
        AssemblyContextLibraryRole role)
    {
        InspectionEnvelope<LibraryInspectionOutcome>? envelope =
            await ExactLibraryInspectionExecutor.ExecuteAsync(
                    assembly,
                    session => session.Execute(
                        s_libraryInfoPlan,
                        CancellationToken.None),
                    CancellationToken.None,
                    role)
                .ConfigureAwait(false);
        return Project(envelope);
    }

    private static LibraryDocumentInspection Project(
        InspectionEnvelope<LibraryInspectionOutcome>? envelope) =>
        envelope is null
            ? new(null, "LibraryUnavailable")
            : new(envelope, null);
}

internal sealed record LibraryInspectionRenderInput(
    LibraryInspection Inspection,
    LibraryDocumentInspection? DocumentInspection);
