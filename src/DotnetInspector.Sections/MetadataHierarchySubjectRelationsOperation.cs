using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed class MetadataHierarchySubjectRelationsContinuationAuthority
{
    private MetadataHierarchySubjectRelationsContinuationAuthority(
        SubjectRelationPopulationContinuationAuthority population,
        AssemblyAcquisitionRegistration sourceRegistration,
        AssemblyAcquisitionRegistration focusRegistration,
        MetadataHierarchyTargetSelection target,
        bool includeNonPublic,
        bool includeHidden)
    {
        PopulationAuthority = population
            ?? throw new ArgumentNullException(nameof(population));
        SourceRegistration = sourceRegistration
            ?? throw new ArgumentNullException(nameof(sourceRegistration));
        FocusRegistration = focusRegistration
            ?? throw new ArgumentNullException(nameof(focusRegistration));
        Target = target
            ?? throw new ArgumentNullException(nameof(target));
        IncludeNonPublic = includeNonPublic;
        IncludeHidden = includeHidden;
    }

    internal SubjectRelationPopulationContinuationAuthority
        PopulationAuthority
    { get; }

    internal SubjectRelationPopulationContinuation Continuation =>
        PopulationAuthority.Continuation;

    internal int NextOrdinal => PopulationAuthority.NextOrdinal;

    internal AssemblyAcquisitionRegistration SourceRegistration { get; }

    internal AssemblyAcquisitionRegistration FocusRegistration { get; }

    internal MetadataHierarchyTargetSelection Target { get; }

    internal bool IncludeNonPublic { get; }

    internal bool IncludeHidden { get; }

    internal static MetadataHierarchySubjectRelationsContinuationAuthority
        Capture(
            SubjectRelationPopulationContinuationAuthority population,
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyAcquisitionRegistration focusRegistration,
            MetadataHierarchyTargetSelection target,
            bool includeNonPublic,
            bool includeHidden) =>
        new(
            population,
            sourceRegistration,
            focusRegistration,
            target,
            includeNonPublic,
            includeHidden);
}

public sealed record MetadataHierarchySubjectRelationsExecution
{
    public MetadataHierarchySubjectRelationsExecution(
        SubjectRelationPopulationResult population,
        MetadataHierarchySubjectRelationsContinuationAuthority?
            continuationAuthority)
    {
        Population = population
            ?? throw new ArgumentNullException(nameof(population));
        SubjectRelationPopulationContinuation? continuation =
            (population.Rows
                as SubjectRelationPopulationRowsOutcome.Read)
                ?.Continuation;
        if ((continuation is null)
                != (continuationAuthority is null)
            || continuation is not null
                && continuationAuthority!.Continuation != continuation)
        {
            throw new ArgumentException(
                "Continuation authority must exactly accompany the returned Rows continuation.",
                nameof(continuationAuthority));
        }

        ContinuationAuthority = continuationAuthority;
    }

    public SubjectRelationPopulationResult Population { get; }

    public MetadataHierarchySubjectRelationsContinuationAuthority?
        ContinuationAuthority
    { get; }
}

/// <summary>
/// Executes one incoming Type hierarchy relation population through the direct
/// Metadata producer.
/// </summary>
public static class MetadataHierarchySubjectRelationsOperation
{
    public static MetadataHierarchySubjectRelationsExecution Execute(
        AssemblyInspectionSession session,
        ResolvedAssemblyReference source,
        ResolvedAssemblyReference focusAssembly,
        SubjectRelationsInspectionRequest request,
        MetadataOperationPolicy policy,
        bool includeNonPublic = false,
        bool includeHidden = false,
        MetadataHierarchySubjectRelationsContinuationAuthority?
            continuationAuthority = null,
        CancellationToken cancellationToken = default) =>
        Execute(
            session,
            MetadataRelationGraphSource.From(source),
            MetadataRelationGraphSource.From(focusAssembly),
            request,
            policy,
            includeNonPublic,
            includeHidden,
            continuationAuthority,
            cancellationToken);

    public static MetadataHierarchySubjectRelationsExecution Execute(
        AssemblyInspectionSession session,
        MetadataRelationGraphSource source,
        MetadataRelationGraphSource focusAssembly,
        SubjectRelationsInspectionRequest request,
        MetadataOperationPolicy policy,
        bool includeNonPublic = false,
        bool includeHidden = false,
        MetadataHierarchySubjectRelationsContinuationAuthority?
            continuationAuthority = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(focusAssembly);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        MetadataHierarchyTargetSelection target =
            ValidateRequest(source, focusAssembly, request);

        SubjectRelationPopulationRowsRejection? continuationRejection =
            ValidateContinuation(
                request,
                continuationAuthority,
                source.Registration,
                focusAssembly.Registration,
                target,
                includeNonPublic,
                includeHidden);
        if (continuationRejection is { } rejection
            && request.Request.Count is null)
        {
            return RejectedContinuation(request, rejection);
        }

        MetadataHierarchyRelationAnalysisResult? countResult = null;
        if (request.Request.Count is not null)
        {
            MetadataHierarchyRelationAnalysisOutcome countOutcome =
                session.AnalyzeHierarchyRelations(
                    new(
                        target,
                        policy,
                        includeNonPublic,
                        includeHidden,
                        materializeRows: false),
                    cancellationToken);
            if (countOutcome
                is MetadataHierarchyRelationAnalysisOutcome.Rejected
                    countRejected)
            {
                return Rejected(
                    request,
                    target.Kind!.Value,
                    countRejected,
                    continuationRejection);
            }
            countResult =
                ((MetadataHierarchyRelationAnalysisOutcome.Available)
                    countOutcome).Result;
            MetadataRelationGraphAdapter.ValidateHierarchyAnalysis(
                source,
                countResult);
        }

        MetadataHierarchyRelationAnalysisResult? rowsResult = null;
        if (request.Request.Rows is { } rowsRequest
            && continuationRejection is null)
        {
            int startOrdinal =
                continuationAuthority?.NextOrdinal ?? 0;
            int producerMaximum =
                rowsRequest.MaximumRows == int.MaxValue
                    ? int.MaxValue
                    : rowsRequest.MaximumRows + 1;
            MetadataHierarchyRelationAnalysisOutcome rowsOutcome =
                session.AnalyzeHierarchyRelations(
                    new(
                        target,
                        policy,
                        includeNonPublic,
                        includeHidden,
                        materializeRows: true,
                        new(
                            startOrdinal,
                            producerMaximum)),
                    cancellationToken);
            if (rowsOutcome
                is MetadataHierarchyRelationAnalysisOutcome.Rejected
                    rowsRejected)
            {
                return Rejected(
                    request,
                    target.Kind!.Value,
                    rowsRejected,
                    continuationRejection);
            }
            rowsResult =
                ((MetadataHierarchyRelationAnalysisOutcome.Available)
                    rowsOutcome).Result;
            MetadataRelationGraphAdapter.ValidateHierarchyAnalysis(
                source,
                rowsResult);
        }

        MetadataHierarchyRelationAnalysisResult producerResult =
            SelectProducerResult(countResult, rowsResult);
        SubjectRelationProducerOutcome producer =
            MetadataRelationGraphAdapter.HierarchyProducerOutcome(
                producerResult,
                target.Kind!.Value);
        var evidence =
            new SubjectRelationPopulationEvidence(
                request.Population,
                [producer]);
        SubjectRelationPopulationCountOutcome? count =
            MapCount(countResult);
        SubjectRelationPopulationRowsOutcome? rows;
        MetadataHierarchySubjectRelationsContinuationAuthority?
            outputAuthority = null;
        if (continuationRejection is { } reason)
        {
            rows =
                new SubjectRelationPopulationRowsOutcome.Rejected(
                    reason);
        }
        else
        {
            rows = MapRows(
                source,
                focusAssembly,
                request,
                target,
                includeNonPublic,
                includeHidden,
                rowsResult,
                out outputAuthority);
        }

        SubjectRelationPopulationContinuationAuthority?
            inputPopulationAuthority =
            rows is SubjectRelationPopulationRowsOutcome.Read
                && request.Request.Rows?.Continuation is not null
                ? continuationAuthority?.PopulationAuthority
                : null;
        bool countHasOwnerAcceptedExactWitness =
            countResult?.Relations.Disposition
                == MetadataRelationFamilyDisposition.Complete
            && producer.Disposition
                != SubjectRelationProducerDisposition.Complete;
        SubjectRelationPopulationResult population =
            countHasOwnerAcceptedExactWitness
                ? SubjectRelationsPopulationOperation
                    .SettleWithOwnerAcceptedExactCount(
                        request,
                        evidence,
                        count,
                        rows,
                        inputPopulationAuthority)
                : SubjectRelationsPopulationOperation.Settle(
                    request,
                    evidence,
                    count,
                    rows,
                    inputPopulationAuthority);
        return new(population, outputAuthority);
    }

    private static MetadataHierarchyRelationAnalysisResult
        SelectProducerResult(
            MetadataHierarchyRelationAnalysisResult? count,
            MetadataHierarchyRelationAnalysisResult? rows)
    {
        if (count is null)
        {
            return rows
                ?? throw new InvalidOperationException(
                    "A hierarchy population requires Count or Rows.");
        }
        if (rows is null)
            return count;

        MetadataRelationFamilyResult<
            MetadataHierarchyRelationAnalysisRow> rowRelations =
                rows.Relations;
        return rowRelations.Disposition
                is MetadataRelationFamilyDisposition.Unavailable
                    or MetadataRelationFamilyDisposition.Failed
            || !rowRelations.Diagnostics.IsEmpty
                ? rows
                : count;
    }

    private static SubjectRelationPopulationCountOutcome? MapCount(
        MetadataHierarchyRelationAnalysisResult? result)
    {
        if (result is null)
            return null;

        MetadataRelationFamilyDisposition disposition =
            result.Relations.Disposition
            ?? throw new InvalidOperationException(
                "Requested hierarchy Count requires a disposition.");
        return disposition switch
        {
            MetadataRelationFamilyDisposition.Complete =>
                new SubjectRelationPopulationCountOutcome.Counted(
                    result.CandidateCount),
            MetadataRelationFamilyDisposition.Partial =>
                new SubjectRelationPopulationCountOutcome.Incomplete(),
            MetadataRelationFamilyDisposition.Unavailable =>
                new SubjectRelationPopulationCountOutcome.Unavailable(),
            MetadataRelationFamilyDisposition.Failed =>
                new SubjectRelationPopulationCountOutcome.Failed(),
            _ => throw new InvalidOperationException(
                "Unknown hierarchy producer disposition."),
        };
    }

    private static SubjectRelationPopulationRowsOutcome? MapRows(
        MetadataRelationGraphSource source,
        MetadataRelationGraphSource focusAssembly,
        SubjectRelationsInspectionRequest request,
        MetadataHierarchyTargetSelection target,
        bool includeNonPublic,
        bool includeHidden,
        MetadataHierarchyRelationAnalysisResult? result,
        out MetadataHierarchySubjectRelationsContinuationAuthority?
            continuationAuthority)
    {
        continuationAuthority = null;
        if (result is null)
            return null;

        MetadataRelationFamilyDisposition disposition =
            result.Relations.Disposition
            ?? throw new InvalidOperationException(
                "Requested hierarchy Rows require a disposition.");
        if (disposition == MetadataRelationFamilyDisposition.Unavailable)
            return new SubjectRelationPopulationRowsOutcome.Unavailable();
        if (disposition == MetadataRelationFamilyDisposition.Failed)
            return new SubjectRelationPopulationRowsOutcome.Failed();

        int startOrdinal =
            result.ForwardPlan?.StartOrdinal ?? 0;
        if (startOrdinal > 0
            && !result.WasStopped
            && result.CandidateCount <= startOrdinal
            && result.Relations.Evidence.IsEmpty)
        {
            return new SubjectRelationPopulationRowsOutcome.Rejected(
                SubjectRelationPopulationRowsRejection
                    .ContinuationOutOfRange);
        }

        int maximumRows =
            request.Request.Rows?.MaximumRows
            ?? throw new InvalidOperationException(
                "Hierarchy Rows mapping requires a Rows request.");
        ImmutableArray<MetadataHierarchyRelationAnalysisRow>
            returnedEvidence =
            [
                .. result.Relations.Evidence.Take(maximumRows),
            ];
        ImmutableArray<SubjectRelationRow> relationRows =
            MetadataRelationGraphAdapter.BindHierarchyRows(
                source,
                returnedEvidence,
                focusAssembly,
                request.Focus,
                request.Population,
                target);
        SubjectRelationPopulationContinuation? continuation = null;
        bool hasMore =
            result.Relations.Evidence.Length > maximumRows
            || maximumRows == int.MaxValue && result.WasStopped;
        if (hasMore)
        {
            int nextOrdinal = checked(startOrdinal + maximumRows);
            Guid sourceModuleVersionId =
                result.Receipt.ModuleVersionId
                ?? throw new InvalidOperationException(
                    "Usable Metadata hierarchy Rows require a source MVID.");
            continuation =
                new SubjectRelationPopulationContinuation(
                    new InertString(
                        TextPolicy.Field,
                        $"metadata-hierarchy-{sourceModuleVersionId:N}-"
                            + $"{nextOrdinal}"));
            SubjectRelationPopulationContinuationAuthority
                populationAuthority =
                SubjectRelationPopulationContinuationAuthority.Capture(
                    continuation,
                    request.Focus,
                    request.Population,
                    request.Request.Selection,
                    SubjectRelationPopulationOrdering.Producer,
                    SubjectRelationRowProjection.Canonical,
                    nextOrdinal);
            continuationAuthority =
                MetadataHierarchySubjectRelationsContinuationAuthority
                    .Capture(
                        populationAuthority,
                        source.Registration,
                        focusAssembly.Registration,
                        target,
                        includeNonPublic,
                        includeHidden);
        }

        return new SubjectRelationPopulationRowsOutcome.Read(
            SubjectRelationPopulationOrdering.Producer,
            relationRows,
            continuation);
    }

    private static MetadataHierarchySubjectRelationsExecution Rejected(
        SubjectRelationsInspectionRequest request,
        MetadataHierarchyRelationKind kind,
        MetadataHierarchyRelationAnalysisOutcome.Rejected rejected,
        SubjectRelationPopulationRowsRejection? continuationRejection)
    {
        var diagnostic =
            SubjectRelationProducerDiagnostic.Create(
                SubjectRelationProducerDiagnosticKind.Failure,
                rejected);
        InspectionGraphRelationshipDescriptor relationship =
            kind == MetadataHierarchyRelationKind.BaseType
                ? MetadataRelationGraphCatalog.BaseType
                : MetadataRelationGraphCatalog.Interface;
        var producer =
            new SubjectRelationProducerOutcome(
                MetadataRelationGraphAdapter.HierarchyQuery,
                SubjectRelationProducerDisposition.Failed,
                new(1, 0, 0, 1, 0),
                [relationship],
                [diagnostic]);
        var evidence =
            new SubjectRelationPopulationEvidence(
                request.Population,
                [producer]);
        SubjectRelationPopulationResult population =
            SubjectRelationsPopulationOperation.Settle(
                request,
                evidence,
                request.Request.Count is null
                    ? null
                    : new SubjectRelationPopulationCountOutcome.Failed(),
                request.Request.Rows is null
                    ? null
                    : continuationRejection is { } reason
                        ? new
                            SubjectRelationPopulationRowsOutcome.Rejected(
                                reason)
                        : new
                            SubjectRelationPopulationRowsOutcome.Failed());
        return new(population, continuationAuthority: null);
    }

    private static MetadataHierarchySubjectRelationsExecution
        RejectedContinuation(
            SubjectRelationsInspectionRequest request,
            SubjectRelationPopulationRowsRejection rejection)
    {
        SubjectRelationPopulationResult population =
            SubjectRelationsPopulationOperation.Settle(
                request,
                new SubjectRelationPopulationEvidence(
                    request.Population,
                    []),
                count: null,
                new SubjectRelationPopulationRowsOutcome.Rejected(
                    rejection));
        return new(population, continuationAuthority: null);
    }

    private static SubjectRelationPopulationRowsRejection?
        ValidateContinuation(
            SubjectRelationsInspectionRequest request,
            MetadataHierarchySubjectRelationsContinuationAuthority?
                authority,
            AssemblyAcquisitionRegistration sourceRegistration,
            AssemblyAcquisitionRegistration focusRegistration,
            MetadataHierarchyTargetSelection target,
            bool includeNonPublic,
            bool includeHidden)
    {
        SubjectRelationPopulationContinuation? continuation =
            request.Request.Rows?.Continuation;
        if (continuation is null)
        {
            if (authority is not null)
            {
                throw new ArgumentException(
                    "Continuation authority requires a continued Rows request.",
                    nameof(authority));
            }
            return null;
        }
        if (authority is null)
        {
            return SubjectRelationPopulationRowsRejection
                .InvalidContinuation;
        }
        if (!ReferenceEquals(
                authority.SourceRegistration,
                sourceRegistration))
        {
            return SubjectRelationPopulationRowsRejection
                .StaleContinuation;
        }
        if (!ReferenceEquals(
                authority.FocusRegistration,
                focusRegistration)
            || authority.Target != target
            || authority.IncludeNonPublic != includeNonPublic
            || authority.IncludeHidden != includeHidden)
        {
            return SubjectRelationPopulationRowsRejection
                .IncompatibleContinuation;
        }
        return SubjectRelationsPopulationOperation
            .ContinuationRejection(
                request,
                authority.PopulationAuthority);
    }

    internal static MetadataHierarchyTargetSelection ValidateRequest(
        MetadataRelationGraphSource source,
        MetadataRelationGraphSource focusAssembly,
        SubjectRelationsInspectionRequest request)
    {
        if (source.Registration.ModuleVersionId is null)
        {
            throw new ArgumentException(
                "Hierarchy relation execution requires an MVID-bound source acquisition.",
                nameof(source));
        }
        if (request.Route != SubjectRelationsRouteKind.Type
            || request.Focus.Kind != StructuralSubjectKind.Type)
        {
            throw new ArgumentException(
                "Hierarchy relation execution requires one exact Type focus.",
                nameof(request));
        }
        NavigationTypeIdentity focus =
            request.Focus.RequireTypeIdentity();
        SubjectRelationPopulationSelection selection =
            request.Request.Selection;
        MetadataHierarchyRelationKind kind;
        InspectionGraphRelationshipDescriptor relationship;
        switch (selection.Form)
        {
            case SubjectRelationForm.Interface:
                kind = MetadataHierarchyRelationKind.Interface;
                relationship = MetadataRelationGraphCatalog.Interface;
                break;
            case SubjectRelationForm.BaseType:
                kind = MetadataHierarchyRelationKind.BaseType;
                relationship = MetadataRelationGraphCatalog.BaseType;
                break;
            default:
                throw new ArgumentException(
                    "The request must select an incoming Metadata hierarchy population.",
                    nameof(request));
        }
        if (selection.Relationship is not null
                && !string.Equals(
                    selection.Relationship,
                    relationship.Id,
                    StringComparison.Ordinal)
            || selection.Direction
                is not SubjectRelationDirectionSelection.Incoming
            || selection.Evidence
                is not null
                and not SubjectRelationEvidenceKind.Declaration
            || selection.Integration
                != SubjectRelationIntegrationSelection.Any
            || selection.Ecosystem is not null
            || selection.Concept is not null)
        {
            throw new ArgumentException(
                "The request must select an incoming Metadata hierarchy population.",
                nameof(request));
        }

        var target = new MetadataHierarchyTargetSelection(
            focus.Type,
            kind);
        MetadataRelationGraphAdapter.ValidateHierarchyFocus(
            focusAssembly,
            request.Focus,
            target);
        return target;
    }
}
