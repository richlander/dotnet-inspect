using System.Diagnostics;
using DotnetInspector.PlatformHouse;
using DotnetInspector.PlatformHouse.Installed;
using DotnetInspector.PlatformQueries;
using DotnetInspector.Platforms.Installed;

namespace DotnetInspector.Sections.Installed;

/// <summary>
/// Binds the installed PlatformHouse source to the shared compiled
/// documentation inspection execution.
/// </summary>
public static class InstalledPlatformCompiledDocumentationInspection
{
    public static async Task<
        InspectionEnvelope<PlatformCompiledDocumentationInspectionOutcome>>
        ExecuteAsync(
            PlatformCompiledDocumentationInspectionRequest request,
            InstalledPlatformHouseAdapter adapter,
            PlatformHouseWorkBudget realizationWork,
            PlatformCompiledDocumentationQueryLimits? queryLimits = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        PlatformCompiledDocumentationInspection.Execution execution =
            PlatformCompiledDocumentationInspection.Prepare(
                request,
                adapter.Capabilities.ReferenceRealization,
                realizationWork,
                cancellationToken);

        Stopwatch stopwatch = Stopwatch.StartNew();
        InstalledPlatformHouseResult<InstalledReferenceRealization>
            sourceResult =
                await adapter.RealizeReferenceAsync(execution.HouseRequest)
                    .ConfigureAwait(false);
        stopwatch.Stop();
        if (sourceResult
            is not InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded reference)
        {
            var terminal = (InstalledPlatformHouseResult<
                InstalledReferenceRealization>.NotSucceeded)sourceResult;
            return execution.SourceFailure(
                terminal.Diagnostic.Summary,
                new(
                    PlatformCompiledDocumentationSource.Installed,
                    terminal.Diagnostic.Kind.ToString()));
        }

        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: reference.Value.Libraries.Count,
            xmlDocuments: reference.Value.Libraries.Count(
                static library => library.Documentation is not null),
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: reference.Value.Libraries.Sum(
                static library => library.TotalContentLength),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: stopwatch.Elapsed);
        InstalledPlatformLibraryMaterializationResult materialization =
            await InstalledPlatformLibraryMaterializer
                .MaterializeReferenceAsync(
                    execution.HouseRequest,
                    reference,
                    consumed)
                .ConfigureAwait(false);
        if (materialization
            is not InstalledPlatformLibraryMaterializationResult.Completed
                completed)
        {
            PlatformHouseSettlementKind settlement =
                materialization.Realization.Outcome.Receipt
                    .SettlementKind;
            return execution.MaterializationFailure(settlement);
        }

        return await execution.CompleteAsync(
                completed.Library,
                completed.Artifacts,
                queryLimits,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
