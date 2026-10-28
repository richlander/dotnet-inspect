using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.ResearchSections;
using DotnetInspector.Sections;
using ILInspector.Research;

namespace DotnetInspector.Diff;

/// <summary>
/// Completes one single-Library pair comparison once, retaining both generic
/// Diff evidence and the exact portable Library API presentation
/// (diff-system.md#single-library-api-diff-terminal).
/// </summary>
internal static class LibraryPairDiffEngine
{
    internal static InspectionEnvelope<DiffAnalysisDocument> Execute(
        LibraryPairDiffRequest request,
        DiffLibraryPair pair,
        DiffPairContent content,
        DiffComparisonReuse? reuse)
    {
        LibraryApiComparison Compute()
        {
            AssemblyContextApiComparisonResult result =
                AssemblyContextApiComparisonQuery.Execute(
                    pair.BeforeGroup,
                    pair.Before,
                    pair.AfterGroup,
                    pair.After,
                    request.View,
                    request.Bounds);
            return new(result, LibraryApiDiffPresentationAdapter.Create(result));
        }

        (AssemblyContextApiComparisonResult comparison, LibraryApiDiffOutcome libraryApi) =
            reuse is null ? Compute() : reuse(Compute);
        DiffAnalysisInput input = CreateInput(
            request,
            pair,
            content,
            comparison,
            libraryApi);
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

    static DiffAnalysisInput CreateInput(
        LibraryPairDiffRequest request,
        DiffLibraryPair pair,
        DiffPairContent content,
        AssemblyContextApiComparisonResult comparison,
        LibraryApiDiffOutcome libraryApi)
    {
        (IReadOnlySet<string> typeFilters,
            IReadOnlyList<string> typeNames,
            IReadOnlySet<string>? memberTargetIdentities) =
            request.Targets switch
            {
                DiffTargets.Library => (
                    (IReadOnlySet<string>)new HashSet<string>(StringComparer.Ordinal),
                    (IReadOnlyList<string>)[],
                    (IReadOnlySet<string>?)null),
                DiffTargets.Types types => (
                    new HashSet<string>(types.Names, StringComparer.Ordinal),
                    types.Names,
                    null),
                DiffTargets.Members members => (
                    new HashSet<string>(members.OwnerTypeNames, StringComparer.Ordinal),
                    members.OwnerTypeNames,
                    new HashSet<string>(members.Identities, StringComparer.Ordinal)),
                _ => throw new InvalidOperationException(
                    "Unknown Diff targets."),
            };
        Func<RetainedFindingComparisonSet>? prepareStringLiterals =
            request.StringLiteralQuery is { } literal
                ? () => StringLiteralComparisonQuery.ExecuteParticipants(
                    pair.BeforeGroup,
                    pair.Before,
                    pair.AfterGroup,
                    pair.After,
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
                fromPaths: [],
                toPaths: [],
                typeFilters,
                typeNames,
                memberTargetIdentities,
                prepareBodySignals: null,
                prepareImplementation: null,
                comparison.Comparison,
                content.HostUnavailability,
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
            fromPaths: [],
            toPaths: [],
            typeFilters,
            typeNames,
            memberTargetIdentities,
            prepareBodySignals: null,
            prepareImplementation: null,
            unavailableAnalyses,
            request.StringLiteralQuery,
            prepareStringLiterals);
    }
}
