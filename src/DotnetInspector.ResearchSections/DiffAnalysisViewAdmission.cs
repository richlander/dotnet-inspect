using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspector.ResearchSections;

public enum DiffAnalysisViewRejectionReason
{
    ChangesRequireApi,
    TransitionsRequireTypeOrMember,
}

/// <summary>Validates semantic combinations of one accepted Diff analysis set and its views.</summary>
public static class DiffAnalysisViewAdmission
{
    public static DiffAnalysisViewRejectionReason? Validate(
        AnalysisSetValidationResult.Accepted selection,
        DiffAnalysisDocumentViews views)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (views.HasFlag(DiffAnalysisDocumentViews.Changes)
            && !IncludesApi(selection))
        {
            return DiffAnalysisViewRejectionReason.ChangesRequireApi;
        }
        if (views.HasFlag(DiffAnalysisDocumentViews.Transitions)
            && selection.Surface == AnalysisReportSurfaceKind.Library)
        {
            return DiffAnalysisViewRejectionReason
                .TransitionsRequireTypeOrMember;
        }
        return null;
    }

    public static bool IncludesApi(
        AnalysisSetValidationResult.Accepted selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return selection.Analyses.Any(analysis =>
            analysis.ParticipationFor(AnalysisOperationKind.Compare)
                ?.Surfaces.Any(surface =>
                    surface.ProducerRoute == DiffAnalysisCatalog.ApiRoute)
                == true);
    }
}
