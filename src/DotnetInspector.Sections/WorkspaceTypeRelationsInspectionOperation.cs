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

public sealed record WorkspaceTypeRelationsInspectionResult(
    WorkspaceTypeHierarchyRelationsResult Relations,
    SubjectRelationPopulationResult Population,
    ImmutableArray<WorkspaceTypeRelationCandidateRow> Candidates,
    SubjectRelationPopulationContinuationAuthority?
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
        SubjectRelationPopulationContinuationAuthority?
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
        bool countSelectionNeedsCandidates =
            count is not null
            && rows is null
            && appliesRowSelection
            && producerCandidatePopulation
            && plan.Selection.Form is null;
        bool countNeedsRows =
            count is not null
            && (!producerCandidatePopulation
                || (appliesRowSelection
                    && !countSelectionNeedsNoRows
                    && !countSelectionNeedsCandidates));
        int producerStart =
            rows?.Continuation is not null
            && continuationAuthority is not null
                ? continuationAuthority.NextOrdinal
                : 0;
        RowSelectionPlan<string>? producerSelection =
            appliesRowSelection
            && producerCandidatePopulation
                ? RowsCohortExecutor.ResolveUnorderedSelection(
                    rowSelection!)
                : null;
        bool producerShapesRows =
            rows is not null
            && producerCandidatePopulation;
        WorkspaceTypeHierarchyRelationExecutionPlan executionPlan =
            producerShapesRows
                ? producerSelection is null
                    ? WorkspaceTypeHierarchyRelationExecutionPlan
                        .CanonicalRows(
                            producerStart,
                            rows!.MaximumRows)
                    : WorkspaceTypeHierarchyRelationExecutionPlan
                        .CanonicalRows(
                            producerSelection,
                            plan.Selection.Form,
                            rows!.MaximumRows)
                : countSelectionNeedsCandidates
                    ? WorkspaceTypeHierarchyRelationExecutionPlan
                        .CanonicalCandidates(
                            producerSelection!,
                            plan.Selection.Form)
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
        if (relations.CandidateSelectionFailure is { } selectionFailure)
        {
            throw SelectionFailure(
                relations.CandidateSelectionFailureForm
                    ?? throw new InvalidOperationException(
                        "Candidate selection failure requires a relation "
                            + "form."),
                selectionFailure);
        }
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
        if (countSelectionNeedsNoRows
            && relations.Evidence.IsComplete)
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
        else if (appliesRowSelection)
        {
            if (producerSelection is null)
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
            }
            selectedCount =
                producerSelection is null
                    ? candidates.Length
                    : relations.SelectedCandidateCount;
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
                int populationCount =
                    producerShapesRows
                        ? relations.SelectedCandidateCount
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
            .. CandidateForms(null)
                .SelectMany(form =>
                    candidates
                        .Where(candidate => candidate.Form == form)
                        .OrderBy(
                            CandidateName,
                            StringComparer.Ordinal)),
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

    private static string CandidateName(
        WorkspaceTypeRelationCandidateRow candidate) =>
        MetadataTypeNameFormatter.FormatFullName(
            ((InspectionGraphTypeIdentity.AcquiredDefinition)
                candidate.Candidate.Identity).Type);

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
