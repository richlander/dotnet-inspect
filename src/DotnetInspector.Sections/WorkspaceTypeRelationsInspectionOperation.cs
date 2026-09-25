using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record WorkspaceTypeRelationsInspectionResult(
    WorkspaceTypeHierarchyRelationsResult Relations,
    SubjectRelationPopulationResult Population,
    SubjectRelationPopulationContinuationAuthority?
        ContinuationAuthority);

/// <summary>
/// Executes one resolved QuerySpace Subject Relations plan against an exact
/// Workspace Type and settles its requested Count and Rows terminals.
/// </summary>
public static class WorkspaceTypeRelationsInspectionOperation
{
    public static WorkspaceTypeRelationsInspectionResult Execute(
        InspectionWorkspace workspace,
        WorkspaceDeclarationPopulation population,
        WorkspaceDeclarationOccurrence focusOccurrence,
        MetadataTypeDefinitionName focusType,
        SubjectRelationsQueryPlan plan,
        SubjectRelationPopulationCountRequest? count = null,
        SubjectRelationPopulationRowsRequest? rows = null,
        SubjectRelationPopulationContinuationAuthority?
            continuationAuthority = null,
        bool includeNonPublic = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "Subject Relations execution requires Count, Rows, or both.");
        }

        WorkspaceTypeHierarchyRelationsResult relations =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                focusOccurrence,
                focusType,
                includeNonPublic,
                cancellationToken);
        var inspectionRequest = new SubjectRelationsInspectionRequest(
            SubjectRelationsRouteKind.Type,
            relations.Focus,
            relations.Population,
            new(plan.Selection, count, rows));
        ImmutableArray<SubjectRelationRow> selected =
        [
            .. relations.Rows.Where(plan.Selection.Matches),
        ];

        SubjectRelationPopulationCountOutcome? countOutcome =
            count is null
                ? null
                : relations.Evidence.IsComplete
                    ? new SubjectRelationPopulationCountOutcome.Counted(
                        selected.Length)
                    : new SubjectRelationPopulationCountOutcome.Incomplete();
        SubjectRelationPopulationRowsOutcome? rowsOutcome = null;
        SubjectRelationPopulationContinuationAuthority?
            nextContinuationAuthority = null;
        if (rows is not null)
        {
            int start = 0;
            if (rows.Continuation is not null)
            {
                if (continuationAuthority is null
                    || SubjectRelationsPopulationOperation
                        .ContinuationRejection(
                            inspectionRequest,
                            continuationAuthority)
                        is not null)
                {
                    rowsOutcome =
                        new SubjectRelationPopulationRowsOutcome.Rejected(
                            SubjectRelationPopulationRowsRejection
                                .InvalidContinuation);
                }
                else
                {
                    start = continuationAuthority.NextOrdinal;
                }
            }
            else if (continuationAuthority is not null)
            {
                throw new ArgumentException(
                    "Initial Rows execution cannot use continuation "
                        + "authority.",
                    nameof(continuationAuthority));
            }

            if (rowsOutcome is null)
            {
                if (start > selected.Length)
                {
                    rowsOutcome =
                        new SubjectRelationPopulationRowsOutcome.Rejected(
                            SubjectRelationPopulationRowsRejection
                                .ContinuationOutOfRange);
                }
                else
                {
                    int take = Math.Min(
                        rows.MaximumRows,
                        selected.Length - start);
                    ImmutableArray<SubjectRelationRow> items =
                    [
                        .. selected.Skip(start).Take(take),
                    ];
                    int next = checked(start + take);
                    SubjectRelationPopulationContinuation? continuation =
                        next < selected.Length
                            ? new(
                                new InertString(
                                    TextPolicy.Field,
                                    Guid.NewGuid().ToString("N")))
                            : null;
                    rowsOutcome =
                        new SubjectRelationPopulationRowsOutcome.Read(
                            rows.Ordering,
                            items,
                            continuation);
                    if (continuation is not null)
                    {
                        nextContinuationAuthority =
                            SubjectRelationPopulationContinuationAuthority
                                .Capture(
                                    continuation,
                                    relations.Focus,
                                    relations.Population,
                                    plan.Selection,
                                    rows.Ordering,
                                    rows.Projection,
                                    next);
                    }
                }
            }
        }

        SubjectRelationPopulationResult settled =
            SubjectRelationsPopulationOperation.Settle(
                inspectionRequest,
                relations.Evidence,
                countOutcome,
                rowsOutcome,
                rowsOutcome
                    is SubjectRelationPopulationRowsOutcome.Read
                    ? continuationAuthority
                    : null);
        return new(
            relations,
            settled,
            nextContinuationAuthority);
    }
}
