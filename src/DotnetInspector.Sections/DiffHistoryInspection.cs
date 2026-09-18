using DotnetInspector.PackageQueries;
using Inspector.Findings;

namespace DotnetInspector.Sections;

/// <summary>
/// Completes shared Diff History through the host-neutral envelope boundary.
/// </summary>
public static class DiffHistoryInspection
{
    public static async Task<InspectionEnvelope<DiffHistoryOutcome>>
        InspectApiMembersAsync(
            DiffHistoryApiMemberInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default)
    {
        DiffHistoryOutcome outcome =
            await DiffHistoryInspector.InspectApiMembersAsync(
                    request,
                    executor,
                    cancellationToken)
                .ConfigureAwait(false);
        return new(
            outcome,
            new InspectionShare.NonProjectable(
                "diff-history/share",
                "Diff History does not yet have a canonical Workspace Share projection."),
            Diagnostics(outcome));
    }

    static IEnumerable<InspectionDiagnostic> Diagnostics(
        DiffHistoryOutcome outcome)
    {
        if (outcome is not DiffHistoryOutcome.Available available)
            yield break;
        if (available.Document
            is not DiffHistoryDocument.ApiMembers apiMembers)
        {
            yield break;
        }

        foreach (DiffHistoryApiMemberEvaluation evaluation
            in apiMembers.Content.Evaluations)
        {
            if (evaluation.Inspection.Value
                is not FindingInspection<
                    ILInspector.Metadata.ApiMemberHandle>.Failed
                    failed)
            {
                continue;
            }

            yield return new(
                "diff-history.evaluation-failure",
                InspectionDiagnosticSeverity.Error,
                failed.Error.Reason,
                evaluation.Version.Display);
        }
    }
}
