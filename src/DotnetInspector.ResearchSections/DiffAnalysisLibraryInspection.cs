using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;
using Inspector.Findings;

namespace DotnetInspector.ResearchSections;

/// <summary>
/// Inputs for one selected-Library comparison completed through generic Diff.
/// </summary>
public sealed record DiffAnalysisLibraryInspectionRequest(
    string Name,
    string BeforeVersion,
    string AfterVersion,
    InspectionCapabilityCatalog Catalog,
    AnalysisSetValidationResult.Accepted Selection,
    DiffAnalysisDocumentViews Views,
    IReadOnlySet<string> TypeFilters,
    IReadOnlyList<string> TypeNames,
    IReadOnlySet<string>? ApiMemberTargetIdentities,
    IReadOnlyList<string> BeforePaths,
    IReadOnlyList<string> AfterPaths,
    Func<IReadOnlyList<FindingDescriptor>, ResearchComparison>?
        PrepareBodySignals,
    Func<ImplementationDiffResult>? PrepareImplementation,
    IReadOnlyList<DiffAnalysisHostUnavailability> HostUnavailability);

/// <summary>
/// Completes one selected-Library comparison once, retaining both generic Diff
/// evidence and the exact portable Library API presentation.
/// </summary>
public static class DiffAnalysisLibraryInspection
{
    public static InspectionEnvelope<DiffAnalysisDocument> Execute(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after,
        ApiSurfaceScope scope,
        ApiSurfaceProjectionLimits perEndpointLimits,
        DiffAnalysisLibraryInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        AssemblyContextApiComparisonResult comparison =
            AssemblyContextApiComparisonQuery.Execute(
                beforeGroup,
                before,
                afterGroup,
                after,
                scope,
                perEndpointLimits);
        LibraryApiDiffOutcome libraryApi =
            LibraryApiDiffPresentationAdapter.Create(comparison);
        DiffAnalysisInput input = CreateInput(comparison, libraryApi, request);
        return DiffAnalysisInspection.Execute(
            new DiffAnalysisInspectionRequest(
                request.Name,
                request.BeforeVersion,
                request.AfterVersion,
                request.Catalog,
                request.Selection,
                input,
                request.Views,
                libraryApi));
    }

    private static DiffAnalysisInput CreateInput(
        AssemblyContextApiComparisonResult comparison,
        LibraryApiDiffOutcome libraryApi,
        DiffAnalysisLibraryInspectionRequest request)
    {
        if (comparison.Before.Surface is { } beforeSurface
            && comparison.After.Surface is { } afterSurface)
        {
            return new DiffAnalysisInput(
                beforeSurface,
                afterSurface,
                request.BeforePaths,
                request.AfterPaths,
                request.TypeFilters,
                request.TypeNames,
                request.ApiMemberTargetIdentities,
                request.PrepareBodySignals,
                request.PrepareImplementation,
                comparison.Comparison,
                request.HostUnavailability);
        }

        string reason = libraryApi switch
        {
            LibraryApiDiffOutcome.Unavailable unavailable =>
                $"The selected Library API endpoint pair is incomplete "
                    + $"({unavailable.Kind}).",
            _ => "The selected Library API endpoint pair has no complete API surfaces.",
        };
        DiffAnalysisHostUnavailability[] unavailableAnalyses =
        [
            .. request.Selection.Analyses.Select(analysis =>
                new DiffAnalysisHostUnavailability(analysis.Id, reason)),
        ];
        return DiffAnalysisInput.WithoutApiSurfaces(
            request.BeforePaths,
            request.AfterPaths,
            request.TypeFilters,
            request.TypeNames,
            request.ApiMemberTargetIdentities,
            request.PrepareBodySignals,
            request.PrepareImplementation,
            unavailableAnalyses);
    }
}
