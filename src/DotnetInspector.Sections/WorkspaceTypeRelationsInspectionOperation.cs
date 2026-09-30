using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using QuerySpace.Rows;

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

public sealed class WorkspaceTypeRelationsContinuationAuthority
{
    private WorkspaceTypeRelationsContinuationAuthority(
        SubjectRelationPopulationContinuationAuthority population,
        bool includeNonPublic)
    {
        PopulationAuthority = population
            ?? throw new ArgumentNullException(nameof(population));
        IncludeNonPublic = includeNonPublic;
    }

    internal SubjectRelationPopulationContinuationAuthority
        PopulationAuthority { get; }

    internal int NextOrdinal => PopulationAuthority.NextOrdinal;

    public SubjectRelationPopulationContinuation Continuation =>
        PopulationAuthority.Continuation;

    public bool IncludeNonPublic { get; }

    internal static WorkspaceTypeRelationsContinuationAuthority Capture(
        SubjectRelationPopulationContinuationAuthority population,
        bool includeNonPublic) =>
        new(population, includeNonPublic);
}

public sealed record WorkspaceTypeRelationsInspectionResult(
    WorkspaceTypeHierarchyRelationsResult Relations,
    SubjectRelationPopulationResult Population,
    ImmutableArray<WorkspaceTypeRelationCandidateRow> Candidates,
    WorkspaceTypeRelationsContinuationAuthority?
        ContinuationAuthority);

public sealed class WorkspaceTypeRelationRowSelectionException
    : InvalidOperationException
{
    public WorkspaceTypeRelationRowSelectionException(
        RowsCohortSemanticFailure<SubjectRelationForm> failure)
        : base(
            "The Subject Relations row selection could not be applied "
                + "to the complete candidate population.")
    {
        Failure = failure
            ?? throw new ArgumentNullException(nameof(failure));
    }

    public RowsCohortSemanticFailure<SubjectRelationForm> Failure
    {
        get;
    }
}

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
        WorkspaceTypeRelationsContinuationAuthority?
            continuationAuthority = null,
        RowSelectionIntent<string>? rowSelection = null,
        bool includeNonPublic = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "Subject Relations execution requires Count, Rows, or both.");
        }
        ValidateHierarchySelection(plan.Selection);
        bool appliesRowSelection =
            rowSelection is { Operations.Count: > 0 };
        if (appliesRowSelection
            && (rows?.Continuation is not null
                || continuationAuthority is not null))
        {
            throw new ArgumentException(
                "Subject Relations semantic row selection cannot resume "
                    + "a producer continuation.",
                nameof(rowSelection));
        }

        bool producerCandidatePopulation =
            CanUseProducerCandidatePopulation(plan.Selection);
        bool countSelectionNeedsNoRows =
            count is not null
            && rows is null
            && appliesRowSelection
            && producerCandidatePopulation
            && plan.Selection.Form is not null;
        bool countNeedsRows =
            count is not null
            && (!producerCandidatePopulation
                || (appliesRowSelection
                    && !countSelectionNeedsNoRows));
        int producerStart =
            rows?.Continuation is not null
            && continuationAuthority is not null
                ? continuationAuthority.NextOrdinal
                : 0;
        bool producerShapesRows =
            rows is not null
            && producerCandidatePopulation
            && !appliesRowSelection;
        WorkspaceTypeHierarchyRelationExecutionPlan executionPlan =
            producerShapesRows && count is null
                ? WorkspaceTypeHierarchyRelationExecutionPlan.RowsSegment(
                    producerStart,
                    rows!.MaximumRows)
                : WorkspaceTypeHierarchyRelationExecutionPlan.Exhaustive(
                    materializeRows:
                        rows is not null || countNeedsRows,
                    startOrdinal:
                        producerShapesRows
                            ? producerStart
                            : 0,
                    maximumRows:
                        producerShapesRows
                            ? rows!.MaximumRows
                            : int.MaxValue);
        WorkspaceTypeHierarchyRelationsResult relations =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                focus,
                includeNonPublic,
                plan.Selection.Form,
                executionPlan,
                cancellationToken: cancellationToken);
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
            OrderCandidates(
                ProjectCandidates(selected));
        int? selectedCount = null;
        if (countSelectionNeedsNoRows)
        {
            if (relations.Evidence.IsComplete)
            {
                RowSelectionPlan<string> selectionPlan =
                    RowsCohortExecutor.ResolveUnorderedSelection(
                        rowSelection!);
                if (!RowSelectionCountExecutor.TryApply(
                        relations.CandidateCount,
                        selectionPlan,
                        out RowSelectionCountResult countSelection))
                {
                    throw new InvalidOperationException(
                        "An unordered Subject Relations Count selection "
                            + "could not execute as a cardinality plan.");
                }
                if (!countSelection.IsSuccess)
                {
                    throw SelectionFailure(
                        plan.Selection.Form!.Value,
                        countSelection.Failure);
                }
                selectedCount = countSelection.Count;
            }
        }
        else if (appliesRowSelection)
        {
            RowsCohortResult<
                SubjectRelationForm,
                WorkspaceTypeRelationCandidateRow> selection =
                    RowsCohortExecutor.ApplyUnordered(
                        CandidateSequences(
                            candidates,
                            plan.Selection.Form),
                        rowSelection!);
            if (!selection.IsSuccess)
            {
                throw new WorkspaceTypeRelationRowSelectionException(
                    selection.Failure
                        ?? throw new InvalidOperationException(
                            "Failed row selection requires a semantic "
                                + "failure."));
            }
            candidates =
            [
                .. selection.RowSets.SelectMany(
                    static set => set.Values),
            ];
            if (rows is not null
                && candidates.Length > rows.MaximumRows)
            {
                throw new ArgumentException(
                    "Subject Relations semantic row selection cannot be "
                        + "combined with a Rows bound that would require "
                        + "producer continuation.",
                    nameof(rowSelection));
            }
        }

        SubjectRelationPopulationCountOutcome? countOutcome =
            count is null
                ? null
                : relations.Evidence.IsComplete
                    ? new SubjectRelationPopulationCountOutcome.Counted(
                        selectedCount
                            ?? (countNeedsRows
                                ? candidates.Length
                                : relations.CandidateCount))
                    : new SubjectRelationPopulationCountOutcome.Incomplete();
        SubjectRelationPopulationRowsOutcome? rowsOutcome = null;
        ImmutableArray<WorkspaceTypeRelationCandidateRow> candidateRows = [];
        WorkspaceTypeRelationsContinuationAuthority?
            nextContinuationAuthority = null;
        if (rows is not null)
        {
            int start = 0;
            if (rows.Continuation is not null)
            {
                if (continuationAuthority is null)
                {
                    rowsOutcome =
                        new SubjectRelationPopulationRowsOutcome.Rejected(
                            SubjectRelationPopulationRowsRejection
                                .InvalidContinuation);
                }
                else if (continuationAuthority.IncludeNonPublic
                    != includeNonPublic)
                {
                    rowsOutcome =
                        new SubjectRelationPopulationRowsOutcome.Rejected(
                            SubjectRelationPopulationRowsRejection
                                .IncompatibleContinuation);
                }
                else if (SubjectRelationsPopulationOperation
                    .ContinuationRejection(
                        inspectionRequest,
                        continuationAuthority.PopulationAuthority)
                    is { } rejection)
                {
                    rowsOutcome =
                        new SubjectRelationPopulationRowsOutcome.Rejected(
                            rejection);
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
                int populationCount =
                    producerShapesRows
                        ? relations.CandidateCount
                        : candidates.Length;
                if (relations.CandidateCountIsComplete
                    && start > populationCount)
                {
                    rowsOutcome =
                        new SubjectRelationPopulationRowsOutcome.Rejected(
                            SubjectRelationPopulationRowsRejection
                                .ContinuationOutOfRange);
                }
                else
                {
                    if (producerShapesRows)
                    {
                        candidateRows = candidates;
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
                    }
                    ImmutableArray<SubjectRelationRow> items =
                    [
                        .. candidateRows.Select(
                            static candidate =>
                                candidate.Representative),
                    ];
                    int next = checked(start + candidateRows.Length);
                    SubjectRelationPopulationContinuation? continuation =
                        next < populationCount
                        || !relations.CandidateCountIsComplete
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
                        SubjectRelationPopulationContinuationAuthority
                            populationContinuation =
                                SubjectRelationPopulationContinuationAuthority
                                    .Capture(
                                        continuation,
                                        relations.Focus,
                                        relations.Population,
                                        plan.Selection,
                                        rows.Ordering,
                                        rows.Projection,
                                        next);
                        nextContinuationAuthority =
                            WorkspaceTypeRelationsContinuationAuthority
                                .Capture(
                                    populationContinuation,
                                    includeNonPublic);
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
                    ? continuationAuthority?.PopulationAuthority
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

    private static WorkspaceTypeRelationRowSelectionException
        SelectionFailure(
            SubjectRelationForm form,
            RowWindowFailure? failure) =>
        new(
            new(
                form,
                failure
                    ?? throw new InvalidOperationException(
                        "Failed Count selection requires a semantic "
                            + "failure.")));

    private static ImmutableArray<WorkspaceTypeRelationCandidateRow>
        OrderCandidates(
            ImmutableArray<WorkspaceTypeRelationCandidateRow> candidates) =>
        [
            .. candidates
                .OrderBy(
                    static candidate =>
                        WorkspaceTypeHierarchyRelationOrdering.FormRank(
                            candidate.Form))
                .ThenBy(
                    static candidate =>
                        WorkspaceTypeHierarchyRelationOrdering.CandidateName(
                            candidate.Candidate),
                    StringComparer.Ordinal),
        ];

    private static RowsCohortSequence<
        SubjectRelationForm,
        WorkspaceTypeRelationCandidateRow>[] CandidateSequences(
            ImmutableArray<WorkspaceTypeRelationCandidateRow> candidates,
            SubjectRelationForm? selectedForm) =>
        [
            .. CandidateForms(selectedForm)
                .Select(form =>
                    RowsCohortSequence<
                        SubjectRelationForm,
                        WorkspaceTypeRelationCandidateRow>.Create(
                            form,
                            [
                                .. candidates.Where(
                                    candidate =>
                                        candidate.Form == form),
                            ])),
        ];

    private static IEnumerable<SubjectRelationForm> CandidateForms(
        SubjectRelationForm? selectedForm)
    {
        if (selectedForm is null
            or SubjectRelationForm.Interface)
        {
            yield return SubjectRelationForm.Interface;
        }
        if (selectedForm is null
            or SubjectRelationForm.BaseType)
        {
            yield return SubjectRelationForm.BaseType;
        }
    }

    private static bool CanUseProducerCandidatePopulation(
        SubjectRelationPopulationSelection selection)
    {
        if (selection.Direction
                is not SubjectRelationDirectionSelection.Incoming
            and not SubjectRelationDirectionSelection.Both
            || selection.Evidence
                is not null
                and not SubjectRelationEvidenceKind.Declaration
            || selection.Integration
                != SubjectRelationIntegrationSelection.Any
            || selection.Ecosystem is not null
            || selection.Concept is not null)
        {
            return false;
        }

        if (selection.Relationship is null)
            return true;
        InspectionGraphRelationshipDescriptor? relationship =
            selection.Form switch
            {
                SubjectRelationForm.Interface =>
                    MetadataRelationGraphCatalog.Interface,
                SubjectRelationForm.BaseType =>
                    MetadataRelationGraphCatalog.BaseType,
                _ => null,
            };
        return relationship is not null
            && string.Equals(
                selection.Relationship,
                relationship.Id,
                StringComparison.Ordinal);
    }

    private static void ValidateHierarchySelection(
        SubjectRelationPopulationSelection selection)
    {
        if (selection.Form
                is not null
                and not SubjectRelationForm.Interface
                and not SubjectRelationForm.BaseType
            || selection.Relationship is not null
                && !string.Equals(
                    selection.Relationship,
                    MetadataRelationGraphCatalog.Interface.Id,
                    StringComparison.Ordinal)
                && !string.Equals(
                    selection.Relationship,
                    MetadataRelationGraphCatalog.BaseType.Id,
                    StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The request must select the incoming interface or base-Type "
                    + "population owned by the hierarchy producer.",
                nameof(selection));
        }
    }
}
