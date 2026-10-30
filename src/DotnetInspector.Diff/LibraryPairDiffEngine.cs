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
        if (reuse is not null && request.TypeSelection is not null)
        {
            throw new ArgumentException(
                "A comparison narrowed by a Type selection cannot be reused.",
                nameof(reuse));
        }

        // The presentation is built only when the result carries it, a
        // reuse hook retains it, or an incomplete pair needs its reason.
        AssemblyContextApiComparisonResult CompareApi() =>
            AssemblyContextApiComparisonQuery.Execute(
                pair.BeforeGroup,
                pair.Before,
                pair.AfterGroup,
                pair.After,
                request.View,
                request.Bounds,
                request.TypeSelection);

        AssemblyContextApiComparisonResult comparison;
        LibraryApiDiffOutcome? libraryApi = null;
        if (reuse is not null)
        {
            (comparison, libraryApi) = reuse(() =>
            {
                AssemblyContextApiComparisonResult computed = CompareApi();
                return new(
                    computed,
                    LibraryApiDiffPresentationAdapter.Create(computed));
            });
        }
        else
        {
            comparison = CompareApi();
        }

        LibraryApiDiffOutcome LibraryApi() =>
            libraryApi ??= LibraryApiDiffPresentationAdapter.Create(comparison);

        if (request.Selection is not { } selection)
        {
            return DiffAnalysisInspection.LibraryApiOnly(
                request.Name,
                request.BeforeVersion,
                request.AfterVersion,
                request.Targets.Surface,
                LibraryApi());
        }

        DiffAnalysisApiSource api =
            comparison.Before.Surface is { } beforeSurface
                && comparison.After.Surface is { } afterSurface
                    ? new DiffAnalysisApiSource.Available(
                        beforeSurface,
                        afterSurface,
                        comparison.Comparison)
                    : new DiffAnalysisApiSource.Unavailable(
                        LibraryApi() switch
                        {
                            LibraryApiDiffOutcome.Unavailable unavailable =>
                                $"The selected Library API endpoint pair is incomplete "
                                    + $"({unavailable.Kind}).",
                            _ => "The selected Library API endpoint pair has no complete API surfaces.",
                        });
        DiffAnalysisInput input = DiffAnalysisInputBuilder.Create(
            selection,
            api,
            fromPaths: [],
            toPaths: [],
            DiffAnalysisTargets.FromExact(request.Targets),
            request.StringLiteralQuery,
            content,
            literal => StringLiteralComparisonQuery.ExecuteParticipants(
                pair.BeforeGroup,
                pair.Before,
                pair.AfterGroup,
                pair.After,
                $"library:{request.Name}",
                request.Name,
                literal));
        return DiffAnalysisInspection.Execute(
            new DiffAnalysisInspectionRequest(
                request.Name,
                request.BeforeVersion,
                request.AfterVersion,
                request.Catalog!,
                selection,
                input,
                request.Views,
                request.LibraryApi ? LibraryApi() : null));
    }
}
