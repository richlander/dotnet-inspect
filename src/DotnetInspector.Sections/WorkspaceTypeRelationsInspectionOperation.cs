using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record WorkspaceTypeRelationCandidateRow
{
    public WorkspaceTypeRelationCandidateRow(
        SubjectRelationForm form,
        InspectionGraphSubject.TypeSubject candidate,
        IEnumerable<SubjectRelationRow> evidence)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        SubjectRelationRow[] evidenceRows = [.. evidence];
        if (evidenceRows.Length == 0
            || evidenceRows.Any(row =>
                row.Form != form
                || row.Source != candidate))
        {
            throw new ArgumentException(
                "A Type relation candidate requires one or more canonical "
                    + "rows for the same relation form and source Type.",
                nameof(evidence));
        }

        Form = form;
        Candidate = candidate;
        Evidence = [.. evidenceRows];
    }

    public SubjectRelationForm Form { get; }

    public InspectionGraphSubject.TypeSubject Candidate { get; }

    public ImmutableArray<SubjectRelationRow> Evidence { get; }

    internal SubjectRelationRow Representative => Evidence[0];
}

public sealed record WorkspaceTypeRelationsInspectionResult(
    WorkspaceTypeHierarchyRelationsResult Relations,
    SubjectRelationPopulationResult Population,
    ImmutableArray<WorkspaceTypeRelationCandidateRow> Candidates,
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
        WorkspaceExactTypeFocusOutcome.Found focus,
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
                focus,
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
        ImmutableArray<WorkspaceTypeRelationCandidateRow> candidates =
            ProjectCandidates(selected);

        SubjectRelationPopulationCountOutcome? countOutcome =
            count is null
                ? null
                : relations.Evidence.IsComplete
                    ? new SubjectRelationPopulationCountOutcome.Counted(
                        candidates.Length)
                    : new SubjectRelationPopulationCountOutcome.Incomplete();
        SubjectRelationPopulationRowsOutcome? rowsOutcome = null;
        ImmutableArray<WorkspaceTypeRelationCandidateRow> candidateRows = [];
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
                if (start > candidates.Length)
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
                        candidates.Length - start);
                    candidateRows =
                    [
                        .. candidates.Skip(start).Take(take),
                    ];
                    ImmutableArray<SubjectRelationRow> items =
                    [
                        .. candidateRows.Select(
                            static candidate =>
                                candidate.Representative),
                    ];
                    int next = checked(start + take);
                    SubjectRelationPopulationContinuation? continuation =
                        next < candidates.Length
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
            candidateRows,
            nextContinuationAuthority);
    }

    private static ImmutableArray<WorkspaceTypeRelationCandidateRow>
        ProjectCandidates(ImmutableArray<SubjectRelationRow> rows)
    {
        var groups = new Dictionary<
            (SubjectRelationForm Form,
                InspectionGraphSubject.TypeSubject Candidate),
            List<SubjectRelationRow>>();
        foreach (SubjectRelationRow row in rows)
        {
            var candidate =
                row.Source as InspectionGraphSubject.TypeSubject
                ?? throw new InvalidOperationException(
                    "Type hierarchy relations require a Type source.");
            var key = (row.Form, candidate);
            if (!groups.TryGetValue(key, out List<SubjectRelationRow>? evidence))
            {
                evidence = [];
                groups.Add(key, evidence);
            }
            evidence.Add(row);
        }

        return
        [
            .. groups.Select(group =>
                new WorkspaceTypeRelationCandidateRow(
                    group.Key.Form,
                    group.Key.Candidate,
                    group.Value)),
        ];
    }
}
