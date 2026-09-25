using System.Collections.Immutable;

using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one resident Workspace declaration-locator query and projects its
/// complete host-neutral Section result.
/// </summary>
public static class TypeDeclarationLocatorInspection
{
    public static async ValueTask<
        InspectionEnvelope<TypeDeclarationLocatorSectionResult>> ExecuteAsync(
        InspectionWorkspace workspace,
        ImmutableArray<TypeDeclarationLocatorRequest> requests,
        TypeDeclarationLocatorSectionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(plan);

        TypeDeclarationLocatorResult result =
            await workspace.GetDeclarationLocator().ExecuteAsync(
                    requests,
                    includeAll: true,
                    cancellationToken)
                .ConfigureAwait(false);
        TypeDeclarationLocatorSectionResult content =
            TypeDeclarationLocatorSection.Project(result, plan);
        return new(
            content,
            new InspectionShare.NonProjectable(
                "type-declaration-locator/share",
                "Type declaration locator requests do not yet have a "
                    + "canonical Workspace Share projection."),
            Diagnostics(content));
    }

    private static IEnumerable<InspectionDiagnostic> Diagnostics(
        TypeDeclarationLocatorSectionResult content)
    {
        if (content
            is TypeDeclarationLocatorSectionResult.Rejected rejected)
        {
            string detail = rejected.PopulationFailure is { } failure
                ? $": {failure}"
                : ".";
            yield return new(
                "type-declaration-locator.rejected",
                InspectionDiagnosticSeverity.Error,
                $"Type declaration search was rejected as "
                    + $"{rejected.RejectionKind}{detail}",
                rejected.RejectionKind.ToString());
            yield break;
        }

        var evaluated =
            (TypeDeclarationLocatorSectionResult.Evaluated)content;
        if (evaluated.RowSelectionFailure is { } rowSelection)
        {
            yield return new(
                "type-declaration-locator.row-selection",
                InspectionDiagnosticSeverity.Error,
                "Type declaration row selection failed at stage "
                    + $"{rowSelection.StageNumber}.",
                $"answer {rowSelection.Answer.Ordinal}");
        }
        if (evaluated.VisibilityFailure is { } visibility)
        {
            yield return new(
                "type-declaration-locator.visibility",
                InspectionDiagnosticSeverity.Error,
                $"Type declaration visibility selection failed: "
                    + $"{visibility}.");
        }

        foreach (TypeDeclarationLocatorMemberCoverage member
            in evaluated.Members.Where(
                static member => !member.IsComplete))
        {
            string subject =
                member.Observation.AssemblyIdentity.Name;
            string detail = member switch
            {
                { CandidateFailure: { } failure } =>
                    failure.Detail,
                { WorkspaceFailure: { } failure } =>
                    $"workspace population failed as {failure}",
                {
                    Outcome:
                        TypeDeclarationLocatorMemberCoverageKind
                            .CoordinateUnavailable,
                } => "no exact source coordinate was available",
                {
                    Outcome:
                        TypeDeclarationLocatorMemberCoverageKind
                            .NotEvaluated,
                } => "the declaration inventory was not evaluated",
                {
                    Outcome:
                        TypeDeclarationLocatorMemberCoverageKind
                            .Searched,
                } =>
                    $"{member.UnsupportedDeclarations.Length} unsupported "
                    + "declaration form(s) were encountered",
                _ => $"evaluation ended as {member.Outcome}",
            };
            yield return new(
                "type-declaration-locator.member-incomplete",
                InspectionDiagnosticSeverity.Warning,
                $"Type declaration search in '{subject}' was incomplete: "
                    + detail,
                $"context {member.Observation.ContextOrder}, "
                    + $"member {member.Observation.MemberOrder}");
        }
    }
}
