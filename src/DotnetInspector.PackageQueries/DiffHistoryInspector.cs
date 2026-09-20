using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

/// <summary>Evaluates Count-free API Finding Diff History.</summary>
public static class DiffHistoryInspector
{
    public static Task<DiffHistoryOutcome> InspectApiMembersAsync(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DiffHistoryApiFindingEngine.InspectMembersAsync(
            request,
            executor,
            cancellationToken);
    }

    public static Task<DiffHistoryOutcome> InspectApiAsync(
        DiffHistoryApiInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Finding switch
        {
            DiffHistoryApiFindingKind.Type =>
                DiffHistoryApiFindingEngine.InspectTypesAsync(
                    request.Common,
                    executor,
                    cancellationToken),
            DiffHistoryApiFindingKind.Members =>
                DiffHistoryApiFindingEngine.InspectMembersAsync(
                    request.Common,
                    executor,
                    cancellationToken),
            DiffHistoryApiFindingKind.Attributes =>
                DiffHistoryApiFindingEngine.InspectAttributesAsync(
                    request.Common,
                    executor,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };
    }
}

static class DiffHistoryApiFindingEngine
{
    public static async Task<DiffHistoryOutcome> InspectMembersAsync(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken)
    {
        Execution<ApiMemberHandle> execution =
            await ExecuteAsync<ApiMemberHandle>(
                    request,
                    executor,
                    new(
                        MetadataFindings.MemberDescriptor,
                        static set => set.Members,
                        static typeFullName =>
                            new FindingInspection<ApiMemberHandle>(
                                new FindingInspection<ApiMemberHandle>.Absent(
                                    FindingInspectionAbsenceKind.SubjectAbsent,
                                    $"Type '{typeFullName}' is absent.")),
                        RequiresCompleteMemberSurface: true,
                        static (value, source, destination) =>
                            CompareMembers(value, source, destination)),
                    cancellationToken)
                .ConfigureAwait(false);
        Dictionary<int, DiffHistoryApiMemberEvaluation> evaluations =
            execution.Points.ToDictionary(
                static point => point.Address.Position,
                static point => new DiffHistoryApiMemberEvaluation(
                    point.Address,
                    point.Version,
                    point.Inspection,
                    point.CellOutcome,
                    point.SubjectResolution,
                    point.ProjectionTruncation,
                    point.Participants));
        ImmutableArray<DiffHistoryApiMemberProbe> probes =
        [
            .. execution.Probes.Select(probe =>
                new DiffHistoryApiMemberProbe(
                    probe.Step,
                    probe.Purpose,
                    probe.SelectedInterval,
                    evaluations[probe.Point.Address.Position],
                    probe.Learning)),
        ];
        var content = new DiffHistoryApiMemberDocument(
            request.Population.Vector,
            request.ApiInspection.TypeFullName,
            request.ApiInspection.Scope,
            request.TargetContext,
            request.EvaluationLimits,
            request.EvaluationPlan,
            request.WorkspaceLimits,
            request.ApiInspection.Limits,
            [.. execution.Points.Select(static point => point.Address)],
            [.. execution.Points.Select(point =>
                evaluations[point.Address.Position])],
            probes,
            execution.Correlation,
            execution.Transitions,
            execution.ChangedVersionAssessments,
            execution.TerminalOutcome,
            execution.NextActions,
            request.ComparisonOptions,
            request.MatchAcceptanceThreshold,
            request.ReplayContext);
        return new DiffHistoryOutcome.Available(
            new DiffHistoryDocument.ApiMembers(content));
    }

    public static async Task<DiffHistoryOutcome> InspectTypesAsync(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken)
    {
        Execution<ApiTypeHandle> execution =
            await ExecuteAsync<ApiTypeHandle>(
                    request,
                    executor,
                    new(
                        MetadataFindings.TypeDescriptor,
                        static set => set.Type,
                        static _ =>
                            new FindingInspection<ApiTypeHandle>(
                                new FindingInspection<ApiTypeHandle>.Complete(
                                    [])),
                        RequiresCompleteMemberSurface: false,
                        static (value, source, destination) =>
                            CompareTypes(value, source, destination)),
                    cancellationToken)
                .ConfigureAwait(false);
        return Available(
            request,
            execution,
            static content => new DiffHistoryDocument.ApiTypes(content));
    }

    public static async Task<DiffHistoryOutcome> InspectAttributesAsync(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken)
    {
        Execution<ApiAttributeHandle> execution =
            await ExecuteAsync<ApiAttributeHandle>(
                    request,
                    executor,
                    new(
                        MetadataFindings.AttributeDescriptor,
                        static set => set.Attributes,
                        static typeFullName =>
                            new FindingInspection<ApiAttributeHandle>(
                                new FindingInspection<ApiAttributeHandle>.Absent(
                                    FindingInspectionAbsenceKind.SubjectAbsent,
                                    $"Type '{typeFullName}' is absent.")),
                        RequiresCompleteMemberSurface: false,
                        static (value, source, destination) =>
                            CompareAttributes(value, source, destination)),
                    cancellationToken)
                .ConfigureAwait(false);
        return Available(
            request,
            execution,
            static content =>
                new DiffHistoryDocument.ApiAttributes(content));
    }

    static DiffHistoryOutcome Available<T>(
        DiffHistoryApiMemberInspectionRequest request,
        Execution<T> execution,
        Func<DiffHistoryApiFindingDocument<T>, DiffHistoryDocument> wrap)
        where T : notnull
    {
        Dictionary<int, DiffHistoryApiFindingEvaluation<T>> evaluations =
            execution.Points.ToDictionary(
                static point => point.Address.Position,
                static point => new DiffHistoryApiFindingEvaluation<T>(
                    point.Address,
                    point.Version,
                    point.Inspection,
                    point.CellOutcome,
                    point.SubjectResolution,
                    point.ProjectionTruncation,
                    point.Participants));
        ImmutableArray<DiffHistoryApiFindingProbe<T>> probes =
        [
            .. execution.Probes.Select(probe =>
                new DiffHistoryApiFindingProbe<T>(
                    probe.Step,
                    probe.Purpose,
                    probe.SelectedInterval,
                    evaluations[probe.Point.Address.Position],
                    probe.Learning)),
        ];
        var content = new DiffHistoryApiFindingDocument<T>(
            request.Population.Vector,
            request.ApiInspection.TypeFullName,
            request.ApiInspection.Scope,
            request.TargetContext,
            request.EvaluationLimits,
            request.EvaluationPlan,
            request.WorkspaceLimits,
            request.ApiInspection.Limits,
            [.. execution.Points.Select(static point => point.Address)],
            [.. execution.Points.Select(point =>
                evaluations[point.Address.Position])],
            probes,
            execution.Correlation,
            execution.Transitions,
            execution.ChangedVersionAssessments,
            execution.TerminalOutcome,
            execution.NextActions,
            request.ComparisonOptions,
            request.MatchAcceptanceThreshold,
            request.ReplayContext);
        return new DiffHistoryOutcome.Available(wrap(content));
    }

    static async Task<Execution<T>> ExecuteAsync<T>(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        Producer<T> producer,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executor);
        cancellationToken.ThrowIfCancellationRequested();

        var chronological = ImmutableArray.CreateBuilder<Point<T>>(
            request.EvaluationLimits.MaximumEvaluations);
        var probes = ImmutableArray.CreateBuilder<Probe<T>>(
            request.EvaluationLimits.MaximumEvaluations);
        if (request.EvaluationPlan
            is DiffHistoryEvaluationPlan.AdaptiveBisect adaptive)
        {
            await EvaluateAdaptiveAsync(
                    request,
                    adaptive,
                    executor,
                    producer,
                    chronological,
                    probes,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            DiffHistoryProbePurpose purpose =
                request.EvaluationPlan
                    is DiffHistoryEvaluationPlan.FullPopulation
                    ? DiffHistoryProbePurpose.DenseCensus
                    : DiffHistoryProbePurpose.ExplicitCheckpoint;
            foreach (PackageVersionAddress address
                in request.InitialEvaluationSelection)
            {
                Point<T> point = await EvaluateAsync(
                        request,
                        address,
                        executor,
                        producer,
                        cancellationToken)
                    .ConfigureAwait(false);
                chronological.Add(point);
                probes.Add(
                    new(
                        probes.Count + 1,
                        purpose,
                        SelectedInterval: null,
                        point,
                        new(
                            probes.Count == 0
                                ? DiffHistoryProbeLearningKind.Baseline
                                : DiffHistoryProbeLearningKind
                                    .ObservationRecorded)));
            }
        }

        ImmutableArray<Point<T>> points = InPopulationOrder(chronological);
        FindingCensusCorrelation<T> correlation =
            FindingCensusCorrelation<T>.Create(
                points.Select(static point =>
                    new VersionedFindingInspection<T>(
                        point.Version,
                        point.Inspection)));
        ImmutableArray<DiffHistoryTransition<T>> transitions =
            BuildTransitions(request, producer, points);
        ImmutableArray<DiffHistoryChangedVersionAssessment<T>> assessments =
            BuildChangedVersionAssessments(
                request,
                points,
                transitions);
        return new(
            points,
            probes.ToImmutable(),
            correlation,
            transitions,
            assessments,
            BuildTerminalOutcome(request, points, transitions),
            BuildNextActions(
                request,
                producer.Descriptor,
                points,
                transitions));
    }

    static async Task EvaluateAdaptiveAsync<T>(
        DiffHistoryApiMemberInspectionRequest request,
        DiffHistoryEvaluationPlan.AdaptiveBisect plan,
        IPackageHouseVersionPopulationCellExecutor executor,
        Producer<T> producer,
        ImmutableArray<Point<T>>.Builder chronological,
        ImmutableArray<Probe<T>>.Builder probes,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ImmutableArray<PackageVersionAddress> population =
            request.Population.Vector.Addresses;
        Point<T> first = await EvaluateAsync(
                request,
                population[0],
                executor,
                producer,
                cancellationToken)
            .ConfigureAwait(false);
        chronological.Add(first);
        probes.Add(
            new(
                1,
                DiffHistoryProbePurpose.PopulationStart,
                SelectedInterval: null,
                first,
                new(DiffHistoryProbeLearningKind.Baseline)));

        Point<T> last = await EvaluateAsync(
                request,
                population[^1],
                executor,
                producer,
                cancellationToken)
            .ConfigureAwait(false);
        chronological.Add(last);
        ImmutableArray<DiffHistoryTransition<T>> transitions =
            BuildTransitions(
                request,
                producer,
                InPopulationOrder(chronological));
        probes.Add(
            new(
                2,
                DiffHistoryProbePurpose.PopulationEnd,
                SelectedInterval: null,
                last,
                Learning(transitions)));

        while (chronological.Count < plan.MaximumProbes)
        {
            if (HasFailedTransition(transitions))
                break;
            DiffHistoryInterval? selected =
                SelectNextInterval(transitions);
            if (selected is null)
                break;

            int midpoint = selected.Source.Position
                + ((selected.Destination.Position
                    - selected.Source.Position) / 2);
            Point<T> point = await EvaluateAsync(
                    request,
                    population[midpoint],
                    executor,
                    producer,
                    cancellationToken)
                .ConfigureAwait(false);
            chronological.Add(point);
            transitions = BuildTransitions(
                request,
                producer,
                InPopulationOrder(chronological));
            probes.Add(
                new(
                    probes.Count + 1,
                    DiffHistoryProbePurpose.AdaptiveMidpoint,
                    selected,
                    point,
                    Learning(transitions)));
        }
    }

    static async Task<Point<T>> EvaluateAsync<T>(
        DiffHistoryApiMemberInspectionRequest request,
        PackageVersionAddress address,
        IPackageHouseVersionPopulationCellExecutor executor,
        Producer<T> producer,
        CancellationToken cancellationToken)
        where T : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        PackageHouseVersionPopulationCell cell =
            request.Population.SelectCell(address);
        var cellRequest = new PackageVersionCellMetadataInspectionRequest(
            cell,
            request.Operation,
            request.TargetContext,
            request.WorkspaceLimits,
            request.WorkspaceDeadline,
            request.ApiInspection);
        PackageVersionCellMetadataInspectionOutcome outcome =
            await PackageVersionCellMetadataInspector
                .ExecuteForApiComparisonAsync(
                    cellRequest,
                    executor,
                    cancellationToken)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Project(request, cell, outcome, producer);
    }

    static Point<T> Project<T>(
        DiffHistoryApiMemberInspectionRequest request,
        PackageHouseVersionPopulationCell cell,
        PackageVersionCellMetadataInspectionOutcome outcome,
        Producer<T> producer)
        where T : notnull
    {
        FindingSubject subject = Subject(
            request.ApiInspection.TypeFullName);
        var version = new FindingVersion(
            cell.Address.Selector,
            cell.NormalizedVersion,
            cell.Address.Position);
        if (outcome
            is not PackageVersionCellMetadataInspectionOutcome.Available
                available)
        {
            return Failed<T>(
                cell.Address,
                version,
                outcome,
                subject,
                producer.Descriptor,
                Describe(outcome));
        }
        if (available.ApiInspection is not { } api)
        {
            return Failed<T>(
                cell.Address,
                version,
                outcome,
                subject,
                producer.Descriptor,
                "The package cell did not return the requested API inspection.");
        }
        ImmutableArray<DiffHistoryApiParticipantEvidence> participants =
        [
            .. api.Surfaces.Assemblies.Assemblies.Select(
                DetachParticipant),
        ];
        if (!api.Surfaces.IsComplete)
        {
            return Failed<T>(
                cell.Address,
                version,
                outcome,
                subject,
                producer.Descriptor,
                api.Surfaces.Truncation is null
                    ? "The package cell API projection contains an unavailable participant."
                    : "The package cell API projection reached a declared bound.",
                api.Surfaces.Truncation,
                participants);
        }
        if (api.Findings.IsEmpty)
        {
            return new(
                cell.Address,
                version,
                new FindingInspection<T>(
                    new FindingInspection<T>.Absent(
                        FindingInspectionAbsenceKind.NoApplicableInput,
                        "The package cell has no applicable assembly input.")),
                outcome,
                new DiffHistoryApiMemberSubjectResolution
                    .NoApplicableInput(),
                ProjectionTruncation: null,
                participants,
                ComparisonSurface: null);
        }

        var matches = new List<PackageVersionCellApiFindingSet>();
        var failures = new List<string>();
        foreach (PackageVersionCellApiFindingSet findingSet
            in api.Findings)
        {
            switch (findingSet.Type.Value)
            {
                case FindingInspection<ApiTypeHandle>.Complete complete
                    when !complete.Findings.IsEmpty:
                    matches.Add(findingSet);
                    break;
                case FindingInspection<ApiTypeHandle>.Complete:
                case FindingInspection<ApiTypeHandle>.Absent:
                    break;
                case FindingInspection<ApiTypeHandle>.Failed failed:
                    failures.Add(failed.Error.Reason);
                    break;
            }
        }
        if (failures.Count > 0)
        {
            return Failed<T>(
                cell.Address,
                version,
                outcome,
                subject,
                producer.Descriptor,
                string.Join("; ", failures),
                participants: participants);
        }
        if (matches.Count == 0)
        {
            return new(
                cell.Address,
                version,
                producer.SubjectAbsent(
                    request.ApiInspection.TypeFullName),
                outcome,
                new DiffHistoryApiMemberSubjectResolution.SubjectAbsent(),
                ProjectionTruncation: null,
                participants,
                ComparisonSurface: null);
        }
        if (matches.Count > 1)
        {
            ImmutableArray<DiffHistoryResolvedAssembly> assemblies =
            [
                .. matches.Select(static match =>
                    Resolve(match.Assembly.Subject)),
            ];
            return new(
                cell.Address,
                version,
                new FindingInspection<T>(
                    new FindingInspection<T>.Failed(
                        new InspectionError(
                            subject,
                            producer.Descriptor,
                            $"Type '{request.ApiInspection.TypeFullName}' resolved in more than one package assembly."))),
                outcome,
                new DiffHistoryApiMemberSubjectResolution
                    .Ambiguous(assemblies),
                ProjectionTruncation: null,
                participants,
                ComparisonSurface: null);
        }

        PackageVersionCellApiFindingSet match = matches[0];
        ApiSurface comparisonSurface = match.Assembly.Value.Surface;
        IEnumerable<ApiSurface> contextualSurfaces =
            api.Surfaces.Assemblies.Assemblies
                .OfType<
                    AssemblyContextEntry<AssemblyApiSurface>.Available>()
                .Select(static participant =>
                    participant.Value.Surface);
        return new(
            cell.Address,
            version,
            producer.Select(match),
            outcome,
            new DiffHistoryApiMemberSubjectResolution.Resolved(
                Resolve(match.Assembly.Subject)),
            ProjectionTruncation: null,
            participants,
            (!producer.RequiresCompleteMemberSurface
                || MetadataFindings.IsApiMemberComparisonComplete(
                    comparisonSurface,
                    request.ApiInspection.TypeFullName,
                    contextualSurfaces))
                    ? comparisonSurface
                    : null);
    }

    static Point<T> Failed<T>(
        PackageVersionAddress address,
        FindingVersion version,
        PackageVersionCellMetadataInspectionOutcome outcome,
        FindingSubject subject,
        FindingDescriptor descriptor,
        string reason,
        ApiSurfaceProjectionTruncation? projectionTruncation = null,
        IEnumerable<DiffHistoryApiParticipantEvidence>? participants = null)
        where T : notnull =>
        new(
            address,
            version,
            new FindingInspection<T>(
                new FindingInspection<T>.Failed(
                    new InspectionError(subject, descriptor, reason))),
            outcome,
            new DiffHistoryApiMemberSubjectResolution.Failed(),
            projectionTruncation,
            [.. participants ?? []],
            ComparisonSurface: null);

    static ImmutableArray<Point<T>> InPopulationOrder<T>(
        ImmutableArray<Point<T>>.Builder chronological)
        where T : notnull =>
        [
            .. chronological.OrderBy(
                static point => point.Address.Position),
        ];

    static ImmutableArray<DiffHistoryTransition<T>> BuildTransitions<T>(
        DiffHistoryApiMemberInspectionRequest request,
        Producer<T> producer,
        ImmutableArray<Point<T>> points)
        where T : notnull
    {
        var transitions =
            ImmutableArray.CreateBuilder<DiffHistoryTransition<T>>(
                Math.Max(0, points.Length - 1));
        for (int i = 1; i < points.Length; i++)
        {
            Point<T> source = points[i - 1];
            Point<T> destination = points[i];
            ImmutableArray<PackageVersionAddress> skipped =
            [
                .. request.Population.Vector.Addresses.Where(address =>
                    address.Position > source.Address.Position
                    && address.Position < destination.Address.Position),
            ];
            transitions.Add(
                new(
                    source.Address,
                    destination.Address,
                    skipped,
                    producer.Compare(request, source, destination)));
        }
        return transitions.ToImmutable();
    }

    static ImmutableArray<DiffHistoryChangedVersionAssessment<T>>
        BuildChangedVersionAssessments<T>(
            DiffHistoryApiMemberInspectionRequest request,
            ImmutableArray<Point<T>> points,
            ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull
    {
        HashSet<int> evaluated =
        [
            .. points.Select(static point => point.Address.Position),
        ];
        Dictionary<int, DiffHistoryTransition<T>> adjacentByDestination =
            transitions
                .Where(static transition =>
                    transition.IsPopulationAdjacent)
                .ToDictionary(static transition =>
                    transition.Destination.Position);
        ImmutableArray<PackageVersionAddress> population =
            request.Population.Vector.Addresses;
        var assessments =
            ImmutableArray.CreateBuilder<
                DiffHistoryChangedVersionAssessment<T>>(
                Math.Max(0, population.Length - 1));
        for (int i = 1; i < population.Length; i++)
        {
            PackageVersionAddress predecessor = population[i - 1];
            PackageVersionAddress destination = population[i];
            bool predecessorEvaluated =
                evaluated.Contains(predecessor.Position);
            bool destinationEvaluated =
                evaluated.Contains(destination.Position);
            if (!predecessorEvaluated || !destinationEvaluated)
            {
                assessments.Add(
                    new(
                        predecessor,
                        destination,
                        DiffHistoryChangedVersionState.Unevaluated,
                        comparison: null,
                        predecessorEvaluated,
                        destinationEvaluated));
                continue;
            }

            FindingComparison<T> comparison =
                adjacentByDestination[destination.Position].Comparison;
            DiffHistoryChangedVersionState state =
                comparison.Value switch
                {
                    FindingComparison<T>.Failed =>
                        DiffHistoryChangedVersionState.Failed,
                    FindingComparison<T>.Complete complete
                        when complete.Transition.Old
                                == FindingInspectionState.NoApplicableInput
                            || complete.Transition.New
                                == FindingInspectionState.NoApplicableInput =>
                        DiffHistoryChangedVersionState.Inapplicable,
                    FindingComparison<T>.Complete =>
                        comparison.IsExact
                            ? DiffHistoryChangedVersionState.Unchanged
                            : DiffHistoryChangedVersionState.Changed,
                    _ => throw new InvalidOperationException(
                        "Unknown Finding comparison outcome."),
                };
            assessments.Add(
                new(
                    predecessor,
                    destination,
                    state,
                    comparison,
                    predecessorEvaluated: true,
                    destinationEvaluated: true));
        }
        return assessments.ToImmutable();
    }

    static DiffHistoryProbeLearning Learning<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull
    {
        ImmutableArray<DiffHistoryInterval> changed =
            ChangedIntervals(transitions);
        if (HasFailedTransition(transitions))
        {
            return new(
                DiffHistoryProbeLearningKind.BlockedByFailure,
                changed);
        }
        return changed.IsEmpty
            ? new(DiffHistoryProbeLearningKind.NoChangeObserved)
            : new(
                DiffHistoryProbeLearningKind.ChangedIntervals,
                changed);
    }

    static DiffHistoryTerminalOutcome BuildTerminalOutcome<T>(
        DiffHistoryApiMemberInspectionRequest request,
        ImmutableArray<Point<T>> points,
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull
    {
        ImmutableArray<DiffHistoryInterval> changed =
            ChangedIntervals(transitions);
        ImmutableArray<DiffHistoryInterval> boundaries =
        [
            .. changed.Where(static interval => interval.IsAdjacent),
        ];
        ImmutableArray<DiffHistoryInterval> unresolved =
        [
            .. changed.Where(static interval => !interval.IsAdjacent),
        ];
        ImmutableArray<PackageVersionAddress> failedAddresses =
        [
            .. points
                .Where(static point =>
                    point.Inspection.Value is FindingInspection<T>.Failed)
                .Select(static point => point.Address),
        ];
        ImmutableArray<DiffHistoryInterval> failed =
            FailedIntervals(transitions);
        if (!failedAddresses.IsEmpty || !failed.IsEmpty)
        {
            return new DiffHistoryTerminalOutcome.BlockedByFailure(
                boundaries,
                unresolved,
                failedAddresses,
                failed);
        }
        if (request.EvaluationPlan
            is DiffHistoryEvaluationPlan.FullPopulation)
        {
            return new DiffHistoryTerminalOutcome
                .FullPopulationCompleted();
        }
        if (request.EvaluationPlan
            is DiffHistoryEvaluationPlan.ExplicitCheckpoints)
        {
            return new DiffHistoryTerminalOutcome
                .ExplicitCheckpointsCompleted();
        }
        if (!unresolved.IsEmpty)
        {
            return new DiffHistoryTerminalOutcome.BudgetExhausted(
                boundaries,
                unresolved);
        }
        if (!boundaries.IsEmpty)
        {
            return new DiffHistoryTerminalOutcome.BoundariesResolved(
                boundaries);
        }
        return new DiffHistoryTerminalOutcome.EqualEndpoints(
            new(
                request.Population.Vector.Addresses[0],
                request.Population.Vector.Addresses[^1]));
    }

    static ImmutableArray<DiffHistoryNextAction> BuildNextActions<T>(
        DiffHistoryApiMemberInspectionRequest request,
        FindingDescriptor descriptor,
        ImmutableArray<Point<T>> points,
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull
    {
        if (request.EvaluationPlan
            is DiffHistoryEvaluationPlan.ExplicitCheckpoints)
        {
            DiffHistoryInterval? interval =
                SelectNextInterval(transitions);
            if (interval is null)
                return [];
            int midpoint = interval.Source.Position
                + ((interval.Destination.Position
                    - interval.Source.Position) / 2);
            PackageVersionAddress address =
                request.Population.Vector.Addresses[midpoint];
            return
            [
                new DiffHistoryNextAction.Probe(
                    interval,
                    address,
                    points.Select(static point => point.Address)
                        .Append(address)
                        .OrderBy(static value => value.Position)
                        .ToImmutableArray()),
            ];
        }
        if (request.EvaluationPlan
            is not DiffHistoryEvaluationPlan.AdaptiveBisect)
        {
            return [];
        }
        return
        [
            .. ChangedIntervals(transitions)
                .Where(static interval => interval.IsAdjacent)
                .Select(interval =>
                    (DiffHistoryNextAction)new
                        DiffHistoryNextAction.PairwiseDiff(
                            interval,
                            request.Population.Vector.PackageId,
                            request.ApiInspection.TypeFullName,
                            descriptor.Id,
                            request.ApiInspection.Scope,
                            request.TargetContext,
                            request.ReplayContext)),
        ];
    }

    static DiffHistoryInterval? SelectNextInterval<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull =>
        ChangedIntervals(transitions)
            .Where(static interval => !interval.IsAdjacent)
            .OrderByDescending(static interval =>
                interval.Destination.Position - interval.Source.Position)
            .ThenBy(static interval => interval.Source.Position)
            .FirstOrDefault();

    static ImmutableArray<DiffHistoryInterval> ChangedIntervals<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull =>
        [
            .. transitions
                .Where(static transition =>
                    transition.Comparison.Value
                        is FindingComparison<T>.Complete
                    && !transition.Comparison.IsExact)
                .Select(static transition => new DiffHistoryInterval(
                    transition.Source,
                    transition.Destination)),
        ];

    static ImmutableArray<DiffHistoryInterval> FailedIntervals<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull =>
        [
            .. transitions
                .Where(static transition =>
                    transition.Comparison.Value
                        is FindingComparison<T>.Failed)
                .Select(static transition => new DiffHistoryInterval(
                    transition.Source,
                    transition.Destination)),
        ];

    static bool HasFailedTransition<T>(
        ImmutableArray<DiffHistoryTransition<T>> transitions)
        where T : notnull =>
        transitions.Any(static transition =>
            transition.Comparison.Value
                is FindingComparison<T>.Failed);

    static FindingComparison<ApiMemberHandle> CompareMembers(
        DiffHistoryApiMemberInspectionRequest request,
        Point<ApiMemberHandle> source,
        Point<ApiMemberHandle> destination)
    {
        if (source.Inspection.Value
                is not FindingInspection<ApiMemberHandle>.Complete
            || destination.Inspection.Value
                is not FindingInspection<ApiMemberHandle>.Complete)
        {
            return FindingComparison.Compare(
                source.Inspection,
                destination.Inspection);
        }
        return MetadataFindings.CompareApiMembers(
            source.ComparisonSurface,
            destination.ComparisonSurface,
            Subject(request.ApiInspection.TypeFullName),
            request.ApiInspection.TypeFullName,
            request.ComparisonOptions,
            request.MatchAcceptanceThreshold);
    }

    static FindingComparison<ApiTypeHandle> CompareTypes(
        DiffHistoryApiMemberInspectionRequest request,
        Point<ApiTypeHandle> source,
        Point<ApiTypeHandle> destination)
    {
        if (source.ComparisonSurface is null
            || destination.ComparisonSurface is null)
        {
            return FindingComparison.Compare(
                source.Inspection,
                destination.Inspection);
        }
        return MetadataFindings.CompareApiType(
            source.ComparisonSurface,
            destination.ComparisonSurface,
            Subject(request.ApiInspection.TypeFullName),
            request.ApiInspection.TypeFullName,
            request.ComparisonOptions);
    }

    static FindingComparison<ApiAttributeHandle> CompareAttributes(
        DiffHistoryApiMemberInspectionRequest request,
        Point<ApiAttributeHandle> source,
        Point<ApiAttributeHandle> destination)
    {
        if (source.ComparisonSurface is null
            || destination.ComparisonSurface is null)
        {
            return FindingComparison.Compare(
                source.Inspection,
                destination.Inspection);
        }
        return MetadataFindings.CompareApiAttributes(
            source.ComparisonSurface,
            destination.ComparisonSurface,
            Subject(request.ApiInspection.TypeFullName),
            request.ApiInspection.TypeFullName);
    }

    static DiffHistoryApiParticipantEvidence DetachParticipant(
        AssemblyContextEntry<AssemblyApiSurface> participant)
    {
        DiffHistoryResolvedAssembly subject =
            Resolve(participant.Subject);
        return participant switch
        {
            AssemblyContextEntry<AssemblyApiSurface>.Available available =>
                new DiffHistoryApiParticipantEvidence.Available(
                    subject,
                    available.Value.InspectionFailures),
            AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected =>
                new DiffHistoryApiParticipantEvidence.Rejected(
                    subject,
                    rejected.Failure),
            AssemblyContextEntry<AssemblyApiSurface>.Failed failed =>
                new DiffHistoryApiParticipantEvidence.Failed(
                    subject,
                    new(
                        failed.Error.GetType().FullName
                            ?? failed.Error.GetType().Name,
                        failed.Error.HResult,
                        failed.Error.Message,
                        failed.Error.ToString())),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context API participant outcome."),
        };
    }

    static DiffHistoryResolvedAssembly Resolve(
        AssemblyContextSubject subject) =>
        new(subject.Identity, subject.Provenance);

    static string Describe(
        PackageVersionCellMetadataInspectionOutcome outcome) =>
        outcome switch
        {
            PackageVersionCellMetadataInspectionOutcome.NoContribution value =>
                $"The package cell produced no query contribution ({value.Reason}).",
            PackageVersionCellMetadataInspectionOutcome.WorkspaceFailure value =>
                $"The package cell Workspace query failed at {value.Failure.Stage}.",
            PackageVersionCellMetadataInspectionOutcome.CleanupFailure =>
                "The package cell cleanup failed after inspection.",
            PackageVersionCellMetadataInspectionOutcome.Available =>
                throw new ArgumentException(
                    "An available cell requires an API-specific failure reason.",
                    nameof(outcome)),
            _ => throw new InvalidOperationException(
                "Unknown package version-cell Metadata outcome."),
        };

    static FindingSubject Subject(string typeFullName) =>
        new($"api.type:{typeFullName}", typeFullName);

    sealed record Producer<T>(
        FindingDescriptor Descriptor,
        Func<PackageVersionCellApiFindingSet, FindingInspection<T>> Select,
        Func<string, FindingInspection<T>> SubjectAbsent,
        bool RequiresCompleteMemberSurface,
        Func<
            DiffHistoryApiMemberInspectionRequest,
            Point<T>,
            Point<T>,
            FindingComparison<T>> Compare)
        where T : notnull;

    sealed record Point<T>(
        PackageVersionAddress Address,
        FindingVersion Version,
        FindingInspection<T> Inspection,
        PackageVersionCellMetadataInspectionOutcome CellOutcome,
        DiffHistoryApiMemberSubjectResolution SubjectResolution,
        ApiSurfaceProjectionTruncation? ProjectionTruncation,
        ImmutableArray<DiffHistoryApiParticipantEvidence> Participants,
        ApiSurface? ComparisonSurface)
        where T : notnull;

    sealed record Probe<T>(
        int Step,
        DiffHistoryProbePurpose Purpose,
        DiffHistoryInterval? SelectedInterval,
        Point<T> Point,
        DiffHistoryProbeLearning Learning)
        where T : notnull;

    sealed record Execution<T>(
        ImmutableArray<Point<T>> Points,
        ImmutableArray<Probe<T>> Probes,
        FindingCensusCorrelation<T> Correlation,
        ImmutableArray<DiffHistoryTransition<T>> Transitions,
        ImmutableArray<DiffHistoryChangedVersionAssessment<T>>
            ChangedVersionAssessments,
        DiffHistoryTerminalOutcome TerminalOutcome,
        ImmutableArray<DiffHistoryNextAction> NextActions)
        where T : notnull;
}
