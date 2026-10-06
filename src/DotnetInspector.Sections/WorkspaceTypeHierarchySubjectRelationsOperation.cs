using System.Collections.Immutable;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>
/// Exact acquired Type focus within one captured declaration population.
/// </summary>
public sealed record WorkspaceTypeHierarchySubjectRelationsFocus
{
    public WorkspaceTypeHierarchySubjectRelationsFocus(
        WorkspaceDeclarationOccurrence occurrence,
        MetadataTypeDefinitionName type)
    {
        Occurrence = occurrence
            ?? throw new ArgumentNullException(nameof(occurrence));
        Type = type
            ?? throw new ArgumentNullException(nameof(type));
    }

    public WorkspaceDeclarationOccurrence Occurrence { get; }

    public MetadataTypeDefinitionName Type { get; }
}

/// <summary>
/// Request for one hierarchy form across a captured Workspace population.
/// </summary>
public sealed record WorkspaceTypeHierarchySubjectRelationsRequest
{
    public WorkspaceTypeHierarchySubjectRelationsRequest(
        WorkspaceTypeHierarchySubjectRelationsFocus focus,
        SubjectRelationPopulationRequest population)
    {
        Focus = focus
            ?? throw new ArgumentNullException(nameof(focus));
        Population = population
            ?? throw new ArgumentNullException(nameof(population));
    }

    public WorkspaceTypeHierarchySubjectRelationsFocus Focus { get; }

    public SubjectRelationPopulationRequest Population { get; }
}

/// <summary>
/// One focused hierarchy candidate and its canonical relation rows.
/// </summary>
public sealed record WorkspaceTypeHierarchyCandidate
{
    public WorkspaceTypeHierarchyCandidate(
        InspectionGraphSubject.TypeSubject type,
        ImmutableArray<SubjectRelationRow> relations)
    {
        Type = type
            ?? throw new ArgumentNullException(nameof(type));
        Relations =
            !relations.IsDefault
            && !relations.IsEmpty
            && relations.All(row =>
                row is not null
                && row.Source == type)
                ? relations
                : throw new ArgumentException(
                    "A hierarchy candidate requires matching canonical rows.",
                    nameof(relations));
    }

    public InspectionGraphSubject.TypeSubject Type { get; }

    public ImmutableArray<SubjectRelationRow> Relations { get; }
}

/// <summary>
/// Detached canonical and focused hierarchy content shared by every host.
/// </summary>
public sealed record WorkspaceTypeHierarchySubjectRelationsDocument
{
    public WorkspaceTypeHierarchySubjectRelationsDocument(
        SubjectRelationPopulationResult relations,
        ImmutableArray<WorkspaceTypeHierarchyCandidate> implementers,
        ImmutableArray<WorkspaceTypeHierarchyCandidate> derivedTypes)
    {
        Relations = relations
            ?? throw new ArgumentNullException(nameof(relations));
        Implementers = ValidateCandidates(
            implementers,
            SubjectRelationForm.Interface,
            nameof(implementers));
        DerivedTypes = ValidateCandidates(
            derivedTypes,
            SubjectRelationForm.BaseType,
            nameof(derivedTypes));
        if (!Implementers.IsEmpty && !DerivedTypes.IsEmpty)
        {
            throw new ArgumentException(
                "One hierarchy document contains one selected hierarchy form.");
        }
    }

    public SubjectRelationPopulationResult Relations { get; }

    public ImmutableArray<WorkspaceTypeHierarchyCandidate> Implementers
    { get; }

    public ImmutableArray<WorkspaceTypeHierarchyCandidate> DerivedTypes
    { get; }

    private static ImmutableArray<WorkspaceTypeHierarchyCandidate>
        ValidateCandidates(
            ImmutableArray<WorkspaceTypeHierarchyCandidate> candidates,
            SubjectRelationForm form,
            string parameterName)
    {
        if (candidates.IsDefault
            || candidates.Any(candidate =>
                candidate is null
                || candidate.Relations.Any(row => row.Form != form)))
        {
            throw new ArgumentException(
                "Focused hierarchy candidates must match their section form.",
                parameterName);
        }

        return candidates;
    }
}

/// <summary>
/// Candidate access failure retained by the hierarchy producer evidence.
/// </summary>
public sealed record WorkspaceTypeHierarchyCandidateFailure(
    WorkspaceDeclarationMember Candidate,
    string Detail);

/// <summary>
/// Selected context realization failure retained by the hierarchy producer
/// evidence.
/// </summary>
public sealed record WorkspaceTypeHierarchyContextFailure(
    WorkspaceDeclarationContextReceipt Context);

/// <summary>
/// Process-local authority for one Workspace-wide hierarchy continuation.
/// </summary>
public sealed class WorkspaceTypeHierarchySubjectRelationsContinuationAuthority
{
    private WorkspaceTypeHierarchySubjectRelationsContinuationAuthority(
        SubjectRelationPopulationContinuationAuthority population,
        AssemblyAcquisitionRegistration focusRegistration,
        int candidateIndex,
        MetadataHierarchySubjectRelationsContinuationAuthority?
            candidateContinuation,
        MetadataOperationPolicy policy,
        bool includeNonPublic,
        bool includeHidden)
    {
        PopulationAuthority = population
            ?? throw new ArgumentNullException(nameof(population));
        FocusRegistration = focusRegistration
            ?? throw new ArgumentNullException(nameof(focusRegistration));
        ArgumentOutOfRangeException.ThrowIfNegative(candidateIndex);
        CandidateIndex = candidateIndex;
        CandidateContinuation = candidateContinuation;
        Policy = policy
            ?? throw new ArgumentNullException(nameof(policy));
        IncludeNonPublic = includeNonPublic;
        IncludeHidden = includeHidden;
    }

    internal SubjectRelationPopulationContinuationAuthority
        PopulationAuthority
    { get; }

    internal AssemblyAcquisitionRegistration FocusRegistration { get; }

    internal int CandidateIndex { get; }

    internal MetadataHierarchySubjectRelationsContinuationAuthority?
        CandidateContinuation
    { get; }

    internal MetadataOperationPolicy Policy { get; }

    internal bool IncludeNonPublic { get; }

    internal bool IncludeHidden { get; }

    internal static
        WorkspaceTypeHierarchySubjectRelationsContinuationAuthority
        Capture(
            SubjectRelationPopulationContinuationAuthority population,
            AssemblyAcquisitionRegistration focusRegistration,
            int candidateIndex,
            MetadataHierarchySubjectRelationsContinuationAuthority?
                candidateContinuation,
            MetadataOperationPolicy policy,
            bool includeNonPublic,
            bool includeHidden) =>
        new(
            population,
            focusRegistration,
            candidateIndex,
            candidateContinuation,
            policy,
            includeNonPublic,
            includeHidden);
}

/// <summary>
/// Completed hierarchy document and optional process-local continuation
/// authority.
/// </summary>
public sealed record WorkspaceTypeHierarchySubjectRelationsExecution
{
    public WorkspaceTypeHierarchySubjectRelationsExecution(
        InspectionEnvelope<WorkspaceTypeHierarchySubjectRelationsDocument>
            inspection,
        WorkspaceTypeHierarchySubjectRelationsContinuationAuthority?
            continuationAuthority)
    {
        Inspection = inspection
            ?? throw new ArgumentNullException(nameof(inspection));
        SubjectRelationPopulationContinuation? continuation =
            (inspection.Content.Relations.Rows
                as SubjectRelationPopulationRowsOutcome.Read)
                ?.Continuation;
        if ((continuation is null) != (continuationAuthority is null)
            || continuation is not null
                && continuationAuthority!.PopulationAuthority.Continuation
                    != continuation)
        {
            throw new ArgumentException(
                "Continuation authority must exactly accompany the returned Rows continuation.",
                nameof(continuationAuthority));
        }

        ContinuationAuthority = continuationAuthority;
    }

    public InspectionEnvelope<WorkspaceTypeHierarchySubjectRelationsDocument>
        Inspection
    { get; }

    public WorkspaceTypeHierarchySubjectRelationsContinuationAuthority?
        ContinuationAuthority
    { get; }
}

/// <summary>
/// Composes the direct Metadata hierarchy producer across one captured
/// Workspace declaration population.
/// </summary>
public static class WorkspaceTypeHierarchySubjectRelationsOperation
{
    public static WorkspaceTypeHierarchySubjectRelationsExecution Execute(
        WorkspaceDeclarationPopulation population,
        WorkspaceTypeHierarchySubjectRelationsRequest request,
        MetadataOperationPolicy policy,
        bool includeNonPublic = false,
        bool includeHidden = false,
        WorkspaceTypeHierarchySubjectRelationsContinuationAuthority?
            continuationAuthority = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(policy);
        cancellationToken.ThrowIfCancellationRequested();

        MetadataRelationGraphSource focusSource = ResolveFocus(
            population,
            request.Focus,
            cancellationToken);
        SubjectRelationFocusAuthority.AcquiredType focus =
            SubjectRelationFocusAuthority.ForAcquiredType(
                population.RelationAuthority.Workspace,
                focusSource.Registration,
                focusSource.Identity,
                focusSource.Provenance,
                request.Focus.Type);
        SubjectRelationsInspectionRequest relationRequest =
            new(
                SubjectRelationsRouteKind.Type,
                focus,
                population.RelationAuthority,
                request.Population);
        MetadataHierarchyTargetSelection target =
            MetadataHierarchySubjectRelationsOperation.ValidateRequest(
                focusSource,
                focusSource,
                relationRequest);

        SubjectRelationPopulationRowsRejection? continuationRejection =
            ValidateContinuation(
                relationRequest,
                focusSource,
                population,
                policy,
                includeNonPublic,
                includeHidden,
                continuationAuthority);
        if (continuationRejection is { } rejection
            && request.Population.Count is null)
        {
            return Complete(
                relationRequest,
                target.Kind!.Value,
                new SubjectRelationPopulationEvidence(
                    population.RelationAuthority,
                    []),
                count: null,
                new SubjectRelationPopulationRowsOutcome.Rejected(
                    rejection),
                continuationAuthority: null,
                []);
        }

        ImmutableArray<WorkspaceDeclarationMember> candidates =
            population.Receipt.Members;
        int rowsStartCandidate =
            continuationAuthority?.CandidateIndex ?? 0;
        int globalStartOrdinal =
            continuationAuthority?.PopulationAuthority.NextOrdinal ?? 0;
        int maximumRows =
            request.Population.Rows?.MaximumRows ?? 0;
        var returnedRows =
            ImmutableArray.CreateBuilder<SubjectRelationRow>(
                maximumRows);
        var producer = new ProducerAccumulator(
            target.Kind!.Value,
            request.Population.Count is null
                ? rowsStartCandidate
                : 0);
        var count = new CountAccumulator();
        var rows = new RowsAccumulator();
        bool rowsActive =
            request.Population.Rows is not null
            && continuationRejection is null;
        bool needsBoundaryProbe = false;
        int? outputCandidateIndex = null;
        MetadataHierarchySubjectRelationsContinuationAuthority?
            outputCandidateContinuation = null;

        foreach (WorkspaceDeclarationContextReceipt context
            in population.Receipt.Contexts)
        {
            if (context.IsRealized)
                continue;

            producer.AddUnavailable(context);
            if (request.Population.Count is not null)
                count.AddUnavailable();
            if (rowsActive)
                rows.AddUnavailable();
        }

        for (int index = 0; index < candidates.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool beforeRowsStart = index < rowsStartCandidate;
            bool requestRows =
                rowsActive
                && !beforeRowsStart
                && outputCandidateIndex is null
                && (returnedRows.Count < maximumRows
                    || needsBoundaryProbe);
            bool requestCount = request.Population.Count is not null;
            if (!requestCount && !requestRows)
                continue;

            int remainingRows =
                requestRows
                    ? needsBoundaryProbe
                        ? 1
                        : maximumRows - returnedRows.Count
                    : 0;
            MetadataHierarchySubjectRelationsContinuationAuthority?
                inputCandidateContinuation =
                index == rowsStartCandidate
                    ? continuationAuthority?.CandidateContinuation
                    : null;
            SubjectRelationPopulationContinuation? candidateContinuation =
                inputCandidateContinuation?.Continuation;
            SubjectRelationPopulationRequest candidatePopulation =
                new(
                    request.Population.Selection,
                    requestCount
                        ? new SubjectRelationPopulationCountRequest()
                        : null,
                    requestRows
                        ? new SubjectRelationPopulationRowsRequest(
                            remainingRows,
                            continuation:
                                candidateContinuation)
                        : null);
            SubjectRelationsInspectionRequest candidateRequest =
                new(
                    SubjectRelationsRouteKind.Type,
                    focus,
                    population.RelationAuthority,
                    candidatePopulation);

            WorkspaceDeclarationAssemblyUseOutcome<
                MetadataHierarchySubjectRelationsExecution> use =
                population.UseAssemblySession(
                    candidates[index].Occurrence,
                    (session, source) =>
                        MetadataHierarchySubjectRelationsOperation.Execute(
                            session,
                            source,
                            focusSource,
                            candidateRequest,
                            policy,
                            includeNonPublic,
                            includeHidden,
                            inputCandidateContinuation,
                            cancellationToken),
                    cancellationToken);
            if (use is WorkspaceDeclarationAssemblyUseOutcome<
                    MetadataHierarchySubjectRelationsExecution>.Unavailable
                unavailable)
            {
                producer.AddUnavailable(
                    candidates[index],
                    unavailable.Detail);
                if (requestCount)
                    count.AddUnavailable();
                if (requestRows)
                    rows.AddUnavailable();
                continue;
            }

            MetadataHierarchySubjectRelationsExecution candidateExecution =
                ((WorkspaceDeclarationAssemblyUseOutcome<
                    MetadataHierarchySubjectRelationsExecution>.Available)
                    use).Value;
            producer.Add(candidateExecution.Population.Evidence);
            if (requestCount)
                count.Add(candidateExecution.Population.Count);
            if (!requestRows)
                continue;

            switch (candidateExecution.Population.Rows)
            {
                case SubjectRelationPopulationRowsOutcome.Read read:
                    rows.AddRead();
                    if (needsBoundaryProbe)
                    {
                        if (!read.Items.IsEmpty)
                        {
                            outputCandidateIndex = index;
                            outputCandidateContinuation = null;
                            needsBoundaryProbe = false;
                        }
                        break;
                    }

                    returnedRows.AddRange(read.Items);
                    if (read.Continuation is not null)
                    {
                        outputCandidateIndex = index;
                        outputCandidateContinuation =
                            candidateExecution.ContinuationAuthority;
                    }
                    else if (returnedRows.Count == maximumRows)
                    {
                        needsBoundaryProbe = true;
                    }
                    break;
                case SubjectRelationPopulationRowsOutcome.Rejected rejected:
                    continuationRejection = rejected.Reason;
                    rowsActive = false;
                    break;
                case SubjectRelationPopulationRowsOutcome.Incomplete:
                    rows.AddIncomplete();
                    break;
                case SubjectRelationPopulationRowsOutcome.Unavailable:
                    rows.AddUnavailable();
                    break;
                case SubjectRelationPopulationRowsOutcome.Failed:
                    rows.AddFailed();
                    break;
                case null:
                    throw new InvalidOperationException(
                        "Requested candidate Rows require an outcome.");
            }
        }

        if (request.Population.Count is null)
        {
            int firstUnvisited =
                outputCandidateIndex is int index
                    ? index + 1
                    : candidates.Length;
            producer.AddLimitedCandidates(
                Math.Max(0, candidates.Length - firstUnvisited));
        }

        SubjectRelationPopulationCountOutcome? countOutcome =
            request.Population.Count is null
                ? null
                : count.Complete();
        SubjectRelationPopulationRowsOutcome? rowsOutcome =
            request.Population.Rows is null
                ? null
                : continuationRejection is { } reason
                    ? new SubjectRelationPopulationRowsOutcome.Rejected(
                        reason)
                    : rows.Complete(
                        returnedRows.ToImmutable(),
                        outputCandidateIndex is null
                            ? null
                            : NewContinuation());
        SubjectRelationPopulationEvidence evidence =
            new(
                population.RelationAuthority,
                [producer.Complete()]);
        WorkspaceTypeHierarchySubjectRelationsContinuationAuthority?
            outputAuthority = null;
        SubjectRelationPopulationContinuationAuthority?
            inputPopulationAuthority =
            rowsOutcome is SubjectRelationPopulationRowsOutcome.Read
                && request.Population.Rows?.Continuation is not null
                ? continuationAuthority?.PopulationAuthority
                : null;
        if (rowsOutcome is
            SubjectRelationPopulationRowsOutcome.Read
            {
                Continuation: { } outputContinuation,
            })
        {
            SubjectRelationPopulationContinuationAuthority
                populationContinuation =
                SubjectRelationPopulationContinuationAuthority.Capture(
                    outputContinuation,
                    focus,
                    population.RelationAuthority,
                    request.Population.Selection,
                    SubjectRelationPopulationOrdering.Producer,
                    SubjectRelationRowProjection.Canonical,
                    checked(
                        globalStartOrdinal
                        + returnedRows.Count));
            outputAuthority =
                WorkspaceTypeHierarchySubjectRelationsContinuationAuthority
                    .Capture(
                        populationContinuation,
                        focusSource.Registration,
                        outputCandidateIndex!.Value,
                        outputCandidateContinuation,
                        policy,
                        includeNonPublic,
                        includeHidden);
        }

        bool countHasOwnerAcceptedExactWitness =
            countOutcome
                is SubjectRelationPopulationCountOutcome.Counted
            && !evidence.IsComplete;
        SubjectRelationPopulationResult settled =
            countHasOwnerAcceptedExactWitness
                ? SubjectRelationsPopulationOperation
                    .SettleWithOwnerAcceptedExactCount(
                        relationRequest,
                        evidence,
                        countOutcome,
                        rowsOutcome,
                        inputPopulationAuthority)
                : SubjectRelationsPopulationOperation.Settle(
                    relationRequest,
                    evidence,
                    countOutcome,
                    rowsOutcome,
                    inputPopulationAuthority);
        return Complete(
            settled,
            target.Kind!.Value,
            outputAuthority,
            producer.InspectionDiagnostics);
    }

    private static MetadataRelationGraphSource ResolveFocus(
        WorkspaceDeclarationPopulation population,
        WorkspaceTypeHierarchySubjectRelationsFocus focus,
        CancellationToken cancellationToken)
    {
        WorkspaceDeclarationAssemblyUseOutcome<MetadataRelationGraphSource>
            use =
            population.UseAssemblySession(
                focus.Occurrence,
                static (_, source) => source,
                cancellationToken);
        if (use is WorkspaceDeclarationAssemblyUseOutcome<
                MetadataRelationGraphSource>.Available available)
        {
            return available.Value;
        }

        var unavailable =
            (WorkspaceDeclarationAssemblyUseOutcome<
                MetadataRelationGraphSource>.Unavailable)use;
        if (unavailable.Member is null)
        {
            throw new ArgumentException(
                "The exact hierarchy focus occurrence is not in the captured population.",
                nameof(focus));
        }
        throw new InvalidOperationException(
            $"The exact hierarchy focus is unavailable: {unavailable.Detail}.");
    }

    private static SubjectRelationPopulationRowsRejection?
        ValidateContinuation(
            SubjectRelationsInspectionRequest request,
            MetadataRelationGraphSource focusSource,
            WorkspaceDeclarationPopulation population,
            MetadataOperationPolicy policy,
            bool includeNonPublic,
            bool includeHidden,
            WorkspaceTypeHierarchySubjectRelationsContinuationAuthority?
                authority)
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
        if (authority.CandidateIndex
                >= population.Receipt.Members.Length)
        {
            return SubjectRelationPopulationRowsRejection
                .ContinuationOutOfRange;
        }
        if (!ReferenceEquals(
                authority.FocusRegistration,
                focusSource.Registration))
        {
            return SubjectRelationPopulationRowsRejection
                .StaleContinuation;
        }
        if (authority.Policy != policy
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

    private static WorkspaceTypeHierarchySubjectRelationsExecution
        Complete(
            SubjectRelationsInspectionRequest request,
            MetadataHierarchyRelationKind kind,
            SubjectRelationPopulationEvidence evidence,
            SubjectRelationPopulationCountOutcome? count,
            SubjectRelationPopulationRowsOutcome? rows,
            WorkspaceTypeHierarchySubjectRelationsContinuationAuthority?
                continuationAuthority,
            ImmutableArray<InspectionDiagnostic> diagnostics)
    {
        SubjectRelationPopulationResult settled =
            SubjectRelationsPopulationOperation.Settle(
                request,
                evidence,
                count,
                rows);
        return Complete(
            settled,
            kind,
            continuationAuthority,
            diagnostics);
    }

    private static WorkspaceTypeHierarchySubjectRelationsExecution
        Complete(
            SubjectRelationPopulationResult population,
            MetadataHierarchyRelationKind kind,
            WorkspaceTypeHierarchySubjectRelationsContinuationAuthority?
                continuationAuthority,
            ImmutableArray<InspectionDiagnostic> diagnostics)
    {
        ImmutableArray<SubjectRelationRow> rows =
            (population.Rows
                as SubjectRelationPopulationRowsOutcome.Read)
                ?.Items
            ?? [];
        ImmutableArray<WorkspaceTypeHierarchyCandidate> candidates =
            Curate(rows);
        var document =
            new WorkspaceTypeHierarchySubjectRelationsDocument(
                population,
                kind == MetadataHierarchyRelationKind.Interface
                    ? candidates
                    : [],
                kind == MetadataHierarchyRelationKind.BaseType
                    ? candidates
                    : []);
        var envelope =
            new InspectionEnvelope<
                WorkspaceTypeHierarchySubjectRelationsDocument>(
                document,
                new InspectionShare.NonProjectable(
                    "scenario.type.relations",
                    "The Workspace hierarchy operation does not yet own a "
                        + "complete portable scenario projection."),
                diagnostics);
        return new(envelope, continuationAuthority);
    }

    private static ImmutableArray<WorkspaceTypeHierarchyCandidate> Curate(
        ImmutableArray<SubjectRelationRow> rows)
    {
        var groups =
            new List<(
                InspectionGraphSubject.TypeSubject Type,
                ImmutableArray<SubjectRelationRow>.Builder Rows)>();
        foreach (SubjectRelationRow row in rows)
        {
            InspectionGraphSubject.TypeSubject type =
                row.Source as InspectionGraphSubject.TypeSubject
                ?? throw new InvalidOperationException(
                    "Hierarchy candidate rows require Type sources.");
            int existing = groups.FindIndex(group =>
                group.Type == type);
            if (existing < 0)
            {
                groups.Add(
                    (
                        type,
                        ImmutableArray.CreateBuilder<
                            SubjectRelationRow>()));
                existing = groups.Count - 1;
            }
            groups[existing].Rows.Add(row);
        }

        return
        [
            .. groups.Select(group =>
                new WorkspaceTypeHierarchyCandidate(
                    group.Type,
                    group.Rows.ToImmutable())),
        ];
    }

    private static SubjectRelationPopulationContinuation
        NewContinuation() =>
        new(
            new InertString(
                TextPolicy.Field,
                $"workspace-hierarchy-{Guid.NewGuid():N}"));

    private sealed class CountAccumulator
    {
        int _count;
        int _counted;
        int _incomplete;
        int _unavailable;
        int _failed;

        internal void Add(
            SubjectRelationPopulationCountOutcome? outcome)
        {
            switch (outcome)
            {
                case SubjectRelationPopulationCountOutcome.Counted counted:
                    _count = checked(_count + counted.Value);
                    _counted++;
                    break;
                case SubjectRelationPopulationCountOutcome.Incomplete:
                    _incomplete++;
                    break;
                case SubjectRelationPopulationCountOutcome.Unavailable:
                    _unavailable++;
                    break;
                case SubjectRelationPopulationCountOutcome.Failed:
                    _failed++;
                    break;
                case null:
                    throw new InvalidOperationException(
                        "Requested candidate Count requires an outcome.");
            }
        }

        internal void AddUnavailable() => _unavailable++;

        internal SubjectRelationPopulationCountOutcome Complete()
        {
            if (_incomplete == 0
                && _unavailable == 0
                && _failed == 0)
            {
                return new SubjectRelationPopulationCountOutcome.Counted(
                    _count);
            }
            if (_counted > 0 || _incomplete > 0)
                return new SubjectRelationPopulationCountOutcome.Incomplete();
            if (_failed > 0)
                return new SubjectRelationPopulationCountOutcome.Failed();
            return new SubjectRelationPopulationCountOutcome.Unavailable();
        }
    }

    private sealed class RowsAccumulator
    {
        int _read;
        int _incomplete;
        int _unavailable;
        int _failed;

        internal void AddRead() => _read++;

        internal void AddIncomplete() => _incomplete++;

        internal void AddUnavailable() => _unavailable++;

        internal void AddFailed() => _failed++;

        internal SubjectRelationPopulationRowsOutcome Complete(
            ImmutableArray<SubjectRelationRow> items,
            SubjectRelationPopulationContinuation? continuation)
        {
            if (_read > 0)
            {
                return new SubjectRelationPopulationRowsOutcome.Read(
                    SubjectRelationPopulationOrdering.Producer,
                    items,
                    continuation);
            }
            if (_incomplete > 0)
                return new SubjectRelationPopulationRowsOutcome.Incomplete();
            if (_failed > 0)
                return new SubjectRelationPopulationRowsOutcome.Failed();
            return new SubjectRelationPopulationRowsOutcome.Unavailable();
        }
    }

    private sealed class ProducerAccumulator
    {
        readonly MetadataHierarchyRelationKind _kind;
        readonly List<SubjectRelationProducerDiagnostic> _diagnostics = [];
        readonly ImmutableArray<InspectionDiagnostic>.Builder
            _inspectionDiagnostics =
                ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        int _considered;
        int _examined;
        int _excluded;
        int _unavailable;
        int _limited;
        bool _hasUsable;
        bool _hasFailed;

        internal ProducerAccumulator(
            MetadataHierarchyRelationKind kind,
            int excludedCandidates)
        {
            _kind = kind;
            _considered = excludedCandidates;
            _excluded = excludedCandidates;
        }

        internal ImmutableArray<InspectionDiagnostic>
            InspectionDiagnostics =>
            _inspectionDiagnostics.ToImmutable();

        internal void Add(SubjectRelationPopulationEvidence evidence)
        {
            SubjectRelationProducerOutcome outcome =
                AssertHierarchyProducer(evidence);
            SubjectRelationCoverage coverage = outcome.Coverage;
            _considered = checked(
                _considered + coverage.Considered);
            _examined = checked(_examined + coverage.Examined);
            _excluded = checked(_excluded + coverage.Excluded);
            _unavailable = checked(
                _unavailable + coverage.Unavailable);
            _limited = checked(_limited + coverage.Limited);
            _diagnostics.AddRange(outcome.Diagnostics);
            _hasUsable |= outcome.Disposition
                is SubjectRelationProducerDisposition.Complete
                    or SubjectRelationProducerDisposition.Partial;
            _hasFailed |= outcome.Disposition
                == SubjectRelationProducerDisposition.Failed;
            foreach (SubjectRelationProducerDiagnostic diagnostic
                in outcome.Diagnostics)
            {
                _inspectionDiagnostics.Add(
                    new(
                        diagnostic.Kind
                            == SubjectRelationProducerDiagnosticKind.Limit
                                ? "workspace-hierarchy-producer-limited"
                                : "workspace-hierarchy-producer-failed",
                        diagnostic.Kind
                            == SubjectRelationProducerDiagnosticKind.Limit
                                ? InspectionDiagnosticSeverity.Warning
                                : InspectionDiagnosticSeverity.Error,
                        diagnostic.Evidence.ToString()
                            ?? "Metadata hierarchy producer diagnostic."));
            }
        }

        internal void AddUnavailable(
            WorkspaceDeclarationMember candidate,
            string detail)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(detail);
            _considered = checked(_considered + 1);
            _unavailable = checked(_unavailable + 1);
            var evidence =
                new WorkspaceTypeHierarchyCandidateFailure(
                    candidate,
                    detail);
            _diagnostics.Add(
                SubjectRelationProducerDiagnostic.Create(
                    SubjectRelationProducerDiagnosticKind.Failure,
                    evidence));
            _inspectionDiagnostics.Add(
                new(
                    "workspace-hierarchy-candidate-unavailable",
                    InspectionDiagnosticSeverity.Warning,
                    $"The hierarchy candidate '{candidate.AssemblyIdentity.Name}' "
                        + $"was unavailable: {detail}."));
        }

        internal void AddUnavailable(
            WorkspaceDeclarationContextReceipt context)
        {
            ArgumentNullException.ThrowIfNull(context);
            _considered = checked(_considered + 1);
            _unavailable = checked(_unavailable + 1);
            var evidence =
                new WorkspaceTypeHierarchyContextFailure(context);
            _diagnostics.Add(
                SubjectRelationProducerDiagnostic.Create(
                    SubjectRelationProducerDiagnosticKind.Failure,
                    evidence));
            _inspectionDiagnostics.Add(
                new(
                    "workspace-hierarchy-context-unavailable",
                    InspectionDiagnosticSeverity.Warning,
                    $"The selected hierarchy context at order "
                        + $"{context.Order} was unavailable with "
                        + $"{context.Failures.Length} recorded "
                        + $"{(context.Failures.Length == 1 ? "failure" : "failures")}."));
        }

        internal void AddLimitedCandidates(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            _considered = checked(_considered + count);
            _limited = checked(_limited + count);
        }

        internal SubjectRelationProducerOutcome Complete()
        {
            SubjectRelationProducerDisposition disposition =
                _unavailable == 0
                    && _limited == 0
                    && _diagnostics.Count == 0
                    ? SubjectRelationProducerDisposition.Complete
                    : _hasUsable
                        ? SubjectRelationProducerDisposition.Partial
                        : _hasFailed
                            ? SubjectRelationProducerDisposition.Failed
                            : SubjectRelationProducerDisposition.Unavailable;
            return new(
                MetadataRelationGraphAdapter.HierarchyQuery,
                disposition,
                new(
                    _considered,
                    _examined,
                    _excluded,
                    _unavailable,
                    _limited),
                [
                    _kind == MetadataHierarchyRelationKind.Interface
                        ? MetadataRelationGraphCatalog.Interface
                        : MetadataRelationGraphCatalog.BaseType,
                ],
                _diagnostics);
        }

        private static SubjectRelationProducerOutcome
            AssertHierarchyProducer(
                SubjectRelationPopulationEvidence evidence)
        {
            SubjectRelationProducerOutcome outcome =
                AssertSingle(evidence.Producers);
            if (!ReferenceEquals(
                    outcome.Producer,
                    MetadataRelationGraphAdapter.HierarchyQuery))
            {
                throw new InvalidOperationException(
                    "Workspace hierarchy composition requires the Metadata hierarchy producer.");
            }
            return outcome;
        }

        private static T AssertSingle<T>(ImmutableArray<T> items)
        {
            if (items.Length != 1)
            {
                throw new InvalidOperationException(
                    "Workspace hierarchy composition requires one producer outcome.");
            }
            return items[0];
        }
    }
}
