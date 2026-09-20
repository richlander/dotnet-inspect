using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

/// <summary>Evaluates Count-free API Finding Diff History.</summary>
public static class DiffHistoryInspector
{
    public static Task<DiffHistoryOutcome> InspectAnalysisAsync(
        DiffHistoryAnalysisInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return DiffHistoryAnalysisEngine.InspectAsync(
            request,
            executor,
            cancellationToken);
    }

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
                request.Member is null
                    ? DiffHistoryApiFindingEngine.InspectMembersAsync(
                        request.Common,
                        executor,
                        cancellationToken)
                    : DiffHistoryApiFindingEngine
                        .InspectExactMemberAsync(
                            request.Common,
                            request.Member,
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
    static Producer<ApiMemberHandle> MemberProducer() =>
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
                CompareMembers(value, source, destination));

    public static async Task<DiffHistoryOutcome> InspectMembersAsync(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken)
    {
        DiffHistoryMethodologyExecution<
            ApiMemberHandle,
            Point<ApiMemberHandle>> execution =
            await ExecuteAsync<ApiMemberHandle>(
                    request,
                    executor,
                    MemberProducer(),
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

    public static async Task<DiffHistoryOutcome> InspectExactMemberAsync(
        DiffHistoryApiMemberInspectionRequest request,
        MemberTargetSelector selector,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(executor);
        Producer<ApiMemberHandle> producer = MemberProducer();
        PackageVersionAddress sourceAddress =
            request.InitialEvaluationSelection[0];
        Point<ApiMemberHandle> source = await EvaluateAsync(
                request,
                sourceAddress,
                executor,
                producer,
                cancellationToken)
            .ConfigureAwait(false);
        DiffHistoryApiFindingEvaluation<ApiMemberHandle> sourceEvaluation =
            Evaluation(source);
        DiffHistoryExactApiMemberSelection selection =
            SelectExactMember(
                request,
                selector,
                source,
                sourceEvaluation);
        if (selection.State
            != DiffHistoryExactApiMemberSelectionState.Selected)
        {
            return new DiffHistoryOutcome.ExactApiMemberUnavailable(
                selection);
        }

        FindingCorrelationKey key = selection.CorrelationKey!;
        bool sourceReturned = false;
        Task<Point<ApiMemberHandle>> Evaluate(
            PackageVersionAddress address,
            CancellationToken token)
        {
            if (!sourceReturned && ReferenceEquals(address, sourceAddress))
            {
                sourceReturned = true;
                return Task.FromResult(source);
            }
            return EvaluateAsync(
                request,
                address,
                executor,
                producer,
                token);
        }

        DiffHistoryMethodologyExecution<
            ApiMemberHandle,
            Point<ApiMemberHandle>> execution =
                await DiffHistoryMethodology.ExecuteAsync<
                    ApiMemberHandle,
                    Point<ApiMemberHandle>>(
                        request.Population.Vector,
                        request.EvaluationPlan,
                        request.InitialEvaluationSelection,
                        request.EvaluationLimits,
                        Evaluate,
                        (oldPoint, newPoint) =>
                            CompareMembers(
                                request,
                                oldPoint,
                                newPoint).Focus(key),
                        boundary =>
                            new DiffHistoryNextAction.PairwiseDiff(
                                boundary,
                                request.Population.Vector.PackageId,
                                request.ApiInspection.TypeFullName,
                                selection.Member!.Anchor,
                                sourceAsset: null,
                                producer.Descriptor.Id,
                                request.ApiInspection.Scope,
                                request.TargetContext,
                                request.ReplayContext),
                        cancellationToken)
                    .ConfigureAwait(false);
        DiffHistoryApiFindingDocument<ApiMemberHandle> history =
            CreateDocument(request, execution);
        var content = new DiffHistoryExactApiMemberDocument(
            selection,
            history,
            execution.Correlation.Correlate(key));
        return new DiffHistoryOutcome.Available(
            new DiffHistoryDocument.ExactApiMember(content));
    }

    public static async Task<DiffHistoryOutcome> InspectTypesAsync(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken)
    {
        DiffHistoryMethodologyExecution<
            ApiTypeHandle,
            Point<ApiTypeHandle>> execution =
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
        DiffHistoryMethodologyExecution<
            ApiAttributeHandle,
            Point<ApiAttributeHandle>> execution =
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
        DiffHistoryMethodologyExecution<T, Point<T>> execution,
        Func<DiffHistoryApiFindingDocument<T>, DiffHistoryDocument> wrap)
        where T : notnull
    {
        DiffHistoryApiFindingDocument<T> content =
            CreateDocument(request, execution);
        return new DiffHistoryOutcome.Available(wrap(content));
    }

    static DiffHistoryApiFindingDocument<T> CreateDocument<T>(
        DiffHistoryApiMemberInspectionRequest request,
        DiffHistoryMethodologyExecution<T, Point<T>> execution)
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
        return new DiffHistoryApiFindingDocument<T>(
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
    }

    static Task<DiffHistoryMethodologyExecution<T, Point<T>>> ExecuteAsync<T>(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        Producer<T> producer,
        CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executor);
        return DiffHistoryMethodology.ExecuteAsync<T, Point<T>>(
            request.Population.Vector,
            request.EvaluationPlan,
            request.InitialEvaluationSelection,
            request.EvaluationLimits,
            (address, token) => EvaluateAsync(
                request,
                address,
                executor,
                producer,
                token),
            (source, destination) =>
                producer.Compare(request, source, destination),
            boundary => new DiffHistoryNextAction.PairwiseDiff(
                boundary,
                request.Population.Vector.PackageId,
                request.ApiInspection.TypeFullName,
                member: null,
                sourceAsset: null,
                producer.Descriptor.Id,
                request.ApiInspection.Scope,
                request.TargetContext,
                request.ReplayContext),
            cancellationToken);
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

    static DiffHistoryApiFindingEvaluation<T> Evaluation<T>(
        Point<T> point)
        where T : notnull =>
        new(
            point.Address,
            point.Version,
            point.Inspection,
            point.CellOutcome,
            point.SubjectResolution,
            point.ProjectionTruncation,
            point.Participants);

    static DiffHistoryExactApiMemberSelection SelectExactMember(
        DiffHistoryApiMemberInspectionRequest request,
        MemberTargetSelector selector,
        Point<ApiMemberHandle> source,
        DiffHistoryApiFindingEvaluation<ApiMemberHandle> sourceEvaluation)
    {
        if (source.SubjectResolution
            is DiffHistoryApiMemberSubjectResolution.SubjectAbsent)
        {
            return new(
                selector,
                DiffHistoryExactApiMemberSelectionState.SubjectAbsent,
                sourceEvaluation);
        }
        if (source.Inspection.Value
                is not FindingInspection<ApiMemberHandle>.Complete complete
            || source.ComparisonSurface is null)
        {
            return new(
                selector,
                DiffHistoryExactApiMemberSelectionState.Failed,
                sourceEvaluation);
        }

        ApiType? type = source.ComparisonSurface.Types.FirstOrDefault(
            candidate => string.Equals(
                candidate.FullName,
                request.ApiInspection.TypeFullName,
                StringComparison.Ordinal));
        if (type is null)
        {
            return new(
                selector,
                DiffHistoryExactApiMemberSelectionState.SubjectAbsent,
                sourceEvaluation);
        }
        MemberTargetResolution resolution =
            MemberTargetResolver.Resolve(type, selector);
        if (!resolution.Found)
        {
            return new(
                selector,
                DiffHistoryExactApiMemberSelectionState.MemberUnresolved,
                sourceEvaluation,
                diagnostic: resolution.Diagnostic);
        }

        ApiMemberHandle member = resolution.Target!.ApiMember;
        Finding<ApiMemberHandle>? finding =
            complete.Findings.FirstOrDefault(candidate =>
                candidate.Payload.Anchor == member.Anchor);
        if (finding is null)
        {
            return new(
                selector,
                DiffHistoryExactApiMemberSelectionState.Failed,
                sourceEvaluation);
        }
        return new(
            selector,
            DiffHistoryExactApiMemberSelectionState.Selected,
            sourceEvaluation,
            member,
            FindingCorrelationKey.From(finding));
    }

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
        : IDiffHistoryPoint<T>
        where T : notnull
    {
        public bool BlocksFurtherEvaluation => false;
    }
}
