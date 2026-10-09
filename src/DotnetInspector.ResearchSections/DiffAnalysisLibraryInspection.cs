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
    IReadOnlyList<DiffAnalysisHostUnavailability> HostUnavailability,
    StringLiteralComparisonQueryPlan? StringLiteralQuery = null);

/// <summary>
/// The whole-Library API comparison of one endpoint pair and its portable
/// presentation. Both are independent of the requested Diff surface, Types,
/// and Members.
/// </summary>
public sealed record LibraryApiComparison(
    AssemblyContextApiComparisonResult Comparison,
    LibraryApiDiffOutcome LibraryApi);

/// <summary>
/// Completes one selected-Library comparison once, retaining both generic Diff
/// evidence and the exact portable Library API presentation.
/// </summary>
public static class DiffAnalysisLibraryInspection
{
    /// <param name="reuse">
    /// Lets a host return a previously completed comparison of the same
    /// endpoint pair, scope, and limits instead of running the supplied
    /// computation.
    /// </param>
    public static InspectionEnvelope<DiffAnalysisDocument> Execute(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after,
        ApiSurfaceScope scope,
        ApiSurfaceProjectionLimits perEndpointLimits,
        DiffAnalysisLibraryInspectionRequest request,
        Func<Func<LibraryApiComparison>, LibraryApiComparison>? reuse = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        LibraryApiComparison Compute()
        {
            AssemblyContextApiComparisonResult result =
                AssemblyContextApiComparisonQuery.Execute(
                    beforeGroup,
                    before,
                    afterGroup,
                    after,
                    scope,
                    perEndpointLimits);
            return new(result, LibraryApiDiffPresentationAdapter.Create(result));
        }

        (AssemblyContextApiComparisonResult comparison, LibraryApiDiffOutcome libraryApi) =
            reuse is null ? Compute() : reuse(Compute);
        DiffAnalysisInput input = CreateInput(
            beforeGroup,
            before,
            afterGroup,
            after,
            comparison,
            libraryApi,
            request);
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
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant before,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant after,
        AssemblyContextApiComparisonResult comparison,
        LibraryApiDiffOutcome libraryApi,
        DiffAnalysisLibraryInspectionRequest request)
    {
        Func<RetainedFindingComparisonSet>? prepareStringLiterals =
            request.StringLiteralQuery is { } literal
                ? () => StringLiteralComparisonQuery.ExecuteParticipants(
                    beforeGroup,
                    before,
                    afterGroup,
                    after,
                    $"library:{request.Name}",
                    request.Name,
                    literal)
                : null;
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
                request.HostUnavailability,
                request.StringLiteralQuery,
                prepareStringLiterals);
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
            .. request.Selection.Analyses
                .Where(analysis =>
                    analysis.ParticipationFor(request.Selection.Operation)
                        ?.For(request.Selection.Surface)
                        ?.ProducerRoute
                    != DiffAnalysisCatalog.StringLiteralRoute)
                .Select(analysis =>
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
            unavailableAnalyses,
            request.StringLiteralQuery,
            prepareStringLiterals);
    }
}
