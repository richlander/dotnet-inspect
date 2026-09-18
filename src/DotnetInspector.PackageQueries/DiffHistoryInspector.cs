using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Serially evaluates a Count-free whole-Type API Member Diff History.
/// </summary>
public static class DiffHistoryInspector
{
    public static async Task<DiffHistoryOutcome> InspectApiMembersAsync(
        DiffHistoryApiMemberInspectionRequest request,
        IPackageHouseVersionPopulationCellExecutor executor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executor);
        cancellationToken.ThrowIfCancellationRequested();

        var evaluated =
            ImmutableArray.CreateBuilder<EvaluatedApiMembers>(
                request.EvaluationSelection.Length);
        foreach (PackageVersionAddress address
            in request.EvaluationSelection)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageHouseVersionPopulationCell cell =
                request.Population.SelectCell(address);
            var cellRequest =
                new PackageVersionCellMetadataInspectionRequest(
                    cell,
                    request.Operation,
                    request.TargetContext,
                    request.WorkspaceLimits,
                    request.WorkspaceDeadline,
                    request.ApiInspection);
            PackageVersionCellMetadataInspectionOutcome outcome =
                await PackageVersionCellMetadataInspector.ExecuteAsync(
                        cellRequest,
                        executor,
                        cancellationToken)
                    .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            evaluated.Add(Project(request, cell, outcome));
        }

        ImmutableArray<EvaluatedApiMembers> points =
            evaluated.ToImmutable();
        FindingCensusCorrelation<ApiMemberHandle> correlation =
            FindingCensusCorrelation<ApiMemberHandle>.Create(
                points.Select(static point =>
                    new VersionedFindingInspection<ApiMemberHandle>(
                        point.Row.Version,
                        point.Row.Inspection)));
        ImmutableArray<DiffHistoryTransition<ApiMemberHandle>>
            transitions = BuildTransitions(request, points);
        ImmutableArray<
            DiffHistoryChangedVersionAssessment<ApiMemberHandle>>
            changedVersionAssessments =
                BuildChangedVersionAssessments(
                    request,
                    points,
                    transitions);
        var content = new DiffHistoryApiMemberDocument(
            request.Population.Vector,
            request.ApiInspection.TypeFullName,
            request.ApiInspection.Scope,
            request.EvaluationLimits,
            request.WorkspaceLimits,
            request.ApiInspection.Limits,
            request.EvaluationSelection,
            [.. points.Select(static point => point.Row)],
            correlation,
            transitions,
            changedVersionAssessments,
            request.ComparisonOptions,
            request.MatchAcceptanceThreshold);
        return new DiffHistoryOutcome.Available(
            new DiffHistoryDocument.ApiMembers(content));
    }

    static EvaluatedApiMembers Project(
        DiffHistoryApiMemberInspectionRequest request,
        PackageHouseVersionPopulationCell cell,
        PackageVersionCellMetadataInspectionOutcome outcome)
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
            return Failed(
                cell.Address,
                version,
                outcome,
                subject,
                Describe(outcome));
        }
        if (available.ApiInspection is not { } api)
        {
            return Failed(
                cell.Address,
                version,
                outcome,
                subject,
                "The package cell did not return the requested API inspection.");
        }
        ImmutableArray<DiffHistoryApiParticipantEvidence> participants =
        [
            .. api.Surfaces.Assemblies.Assemblies.Select(
                DetachParticipant),
        ];
        if (!api.Surfaces.IsComplete)
        {
            return Failed(
                cell.Address,
                version,
                outcome,
                subject,
                api.Surfaces.Truncation is null
                    ? "The package cell API projection contains an unavailable participant."
                    : "The package cell API projection reached a declared bound.",
                api.Surfaces.Truncation,
                participants);
        }
        if (api.Findings.IsEmpty)
        {
            var inspection =
                new FindingInspection<ApiMemberHandle>.Absent(
                    FindingInspectionAbsenceKind.NoApplicableInput,
                    "The package cell has no applicable assembly input.");
            return new(
                new(
                    cell.Address,
                    version,
                    inspection,
                    outcome,
                    new DiffHistoryApiMemberSubjectResolution
                        .NoApplicableInput(),
                    participants: participants),
                Surface: null);
        }

        var matches =
            new List<PackageVersionCellApiFindingSet>();
        var failures = new List<string>();
        foreach (PackageVersionCellApiFindingSet findingSet
            in api.Findings)
        {
            switch (findingSet.Members.Value)
            {
                case FindingInspection<ApiMemberHandle>.Complete:
                    matches.Add(findingSet);
                    break;
                case FindingInspection<ApiMemberHandle>.Absent:
                    break;
                case FindingInspection<ApiMemberHandle>.Failed failed:
                    failures.Add(failed.Error.Reason);
                    break;
            }
        }

        if (failures.Count > 0)
        {
            return Failed(
                cell.Address,
                version,
                outcome,
                subject,
                string.Join("; ", failures),
                participants: participants);
        }
        if (matches.Count == 0)
        {
            var inspection =
                new FindingInspection<ApiMemberHandle>.Absent(
                    FindingInspectionAbsenceKind.SubjectAbsent,
                    $"Type '{request.ApiInspection.TypeFullName}' is absent.");
            return new(
                new(
                    cell.Address,
                    version,
                    inspection,
                    outcome,
                    new DiffHistoryApiMemberSubjectResolution
                        .SubjectAbsent(),
                    participants: participants),
                Surface: null);
        }
        if (matches.Count > 1)
        {
            ImmutableArray<DiffHistoryResolvedAssembly> assemblies =
            [
                .. matches.Select(static match =>
                    Resolve(match.Assembly.Subject)),
            ];
            var inspection =
                new FindingInspection<ApiMemberHandle>.Failed(
                    new InspectionError(
                        subject,
                        MetadataFindings.MemberDescriptor,
                        $"Type '{request.ApiInspection.TypeFullName}' resolved in more than one package assembly."));
            return new(
                new(
                    cell.Address,
                    version,
                    inspection,
                    outcome,
                    new DiffHistoryApiMemberSubjectResolution
                        .Ambiguous(assemblies),
                    participants: participants),
                Surface: null);
        }

        PackageVersionCellApiFindingSet match = matches[0];
        return new(
            new(
                cell.Address,
                version,
                match.Members,
                outcome,
                new DiffHistoryApiMemberSubjectResolution.Resolved(
                    Resolve(match.Assembly.Subject)),
                participants: participants),
            match.Assembly.Value.Surface);
    }

    static EvaluatedApiMembers Failed(
        PackageVersionAddress address,
        FindingVersion version,
        PackageVersionCellMetadataInspectionOutcome outcome,
        FindingSubject subject,
        string reason,
        ApiSurfaceProjectionTruncation? projectionTruncation = null,
        IEnumerable<DiffHistoryApiParticipantEvidence>? participants = null) =>
        new(
            new(
                address,
                version,
                new FindingInspection<ApiMemberHandle>.Failed(
                    new InspectionError(
                        subject,
                        MetadataFindings.MemberDescriptor,
                        reason)),
                outcome,
                new DiffHistoryApiMemberSubjectResolution.Failed(),
                projectionTruncation,
                participants),
            Surface: null);

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

    static ImmutableArray<DiffHistoryTransition<ApiMemberHandle>>
        BuildTransitions(
            DiffHistoryApiMemberInspectionRequest request,
            ImmutableArray<EvaluatedApiMembers> points)
    {
        var transitions =
            ImmutableArray.CreateBuilder<
                DiffHistoryTransition<ApiMemberHandle>>(
                Math.Max(0, points.Length - 1));
        for (int i = 1; i < points.Length; i++)
        {
            EvaluatedApiMembers source = points[i - 1];
            EvaluatedApiMembers destination = points[i];
            ImmutableArray<PackageVersionAddress> skipped =
            [
                .. request.Population.Vector.Addresses.Where(address =>
                    address.Position > source.Row.Address.Position
                    && address.Position
                        < destination.Row.Address.Position),
            ];
            transitions.Add(
                new(
                    source.Row.Address,
                    destination.Row.Address,
                    skipped,
                    Compare(request, source, destination)));
        }

        return transitions.ToImmutable();
    }

    static ImmutableArray<
        DiffHistoryChangedVersionAssessment<ApiMemberHandle>>
        BuildChangedVersionAssessments(
            DiffHistoryApiMemberInspectionRequest request,
            ImmutableArray<EvaluatedApiMembers> points,
            ImmutableArray<DiffHistoryTransition<ApiMemberHandle>>
                transitions)
    {
        Dictionary<int, EvaluatedApiMembers> byPosition =
            points.ToDictionary(
                static point => point.Row.Address.Position);
        Dictionary<int, DiffHistoryTransition<ApiMemberHandle>>
            adjacentByDestination = transitions
                .Where(static transition =>
                    transition.IsPopulationAdjacent)
                .ToDictionary(
                    static transition =>
                        transition.Destination.Position);
        ImmutableArray<PackageVersionAddress> population =
            request.Population.Vector.Addresses;
        var assessments =
            ImmutableArray.CreateBuilder<
                DiffHistoryChangedVersionAssessment<ApiMemberHandle>>(
                Math.Max(0, population.Length - 1));
        for (int i = 1; i < population.Length; i++)
        {
            PackageVersionAddress predecessor = population[i - 1];
            PackageVersionAddress destination = population[i];
            bool predecessorEvaluated =
                byPosition.ContainsKey(predecessor.Position);
            bool destinationEvaluated =
                byPosition.ContainsKey(destination.Position);
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

            FindingComparison<ApiMemberHandle> comparison =
                adjacentByDestination[destination.Position]
                    .Comparison;
            DiffHistoryChangedVersionState state =
                comparison.Value switch
                {
                    FindingComparison<ApiMemberHandle>.Failed =>
                        DiffHistoryChangedVersionState.Failed,
                    FindingComparison<ApiMemberHandle>.Complete complete
                        when complete.Transition.Old
                                == FindingInspectionState.NoApplicableInput
                            || complete.Transition.New
                                == FindingInspectionState.NoApplicableInput =>
                        DiffHistoryChangedVersionState.Inapplicable,
                    FindingComparison<ApiMemberHandle>.Complete =>
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

    static FindingComparison<ApiMemberHandle> Compare(
        DiffHistoryApiMemberInspectionRequest request,
        EvaluatedApiMembers source,
        EvaluatedApiMembers destination)
    {
        if (source.Row.Inspection.Value
                is not FindingInspection<ApiMemberHandle>.Complete
            || destination.Row.Inspection.Value
                is not FindingInspection<ApiMemberHandle>.Complete)
        {
            return FindingComparison.Compare(
                source.Row.Inspection,
                destination.Row.Inspection);
        }

        return MetadataFindings.CompareApiMembers(
            source.Surface,
            destination.Surface,
            Subject(request.ApiInspection.TypeFullName),
            request.ApiInspection.TypeFullName,
            request.ComparisonOptions,
            request.MatchAcceptanceThreshold);
    }

    static FindingSubject Subject(string typeFullName) =>
        new($"api.type:{typeFullName}", typeFullName);

    sealed record EvaluatedApiMembers(
        DiffHistoryApiMemberEvaluation Row,
        ApiSurface? Surface);
}
