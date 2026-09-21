using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspector.PackageQueries;

internal enum PackageVersionCellAnalysisExecutionStage
{
    SourceSelection,
    Correspondence,
    Analysis,
    Cleanup,
}

/// <summary>
/// Executes bounded PackageHouse baseline and checkpoint Member Analysis.
/// </summary>
public static class PackageVersionCellAnalysisInspector
{
    public static Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>>>
        InspectBaselineAllocationsAsync(
            PackageVersionCellBaselineAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        InspectBaselineAsync(
            request,
            executor,
            PackageVersionCellAnalysisProducerKind.Allocation,
            AnalysisFindings.AllocationDescriptor,
            MatchedApiMemberAnalysisQuery.InspectAllocations,
            stageObserver: null,
            cancellationToken);

    public static Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellBaselineAnalysisResult<DirectCall>>>
        InspectBaselineCallSitesAsync(
            PackageVersionCellBaselineAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        InspectBaselineAsync(
            request,
            executor,
            PackageVersionCellAnalysisProducerKind.CallSite,
            AnalysisFindings.CallSiteDescriptor,
            MatchedApiMemberAnalysisQuery.InspectCallSites,
            stageObserver: null,
            cancellationToken);

    public static Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellBaselineAnalysisResult<UnsafetyOccurrence>>>
        InspectBaselineUnsafetyAsync(
            PackageVersionCellBaselineAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        InspectBaselineAsync(
            request,
            executor,
            PackageVersionCellAnalysisProducerKind.Unsafety,
            AnalysisFindings.UnsafetyDescriptor,
            MatchedApiMemberAnalysisQuery.InspectUnsafety,
            stageObserver: null,
            cancellationToken);

    public static Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellCheckpointAnalysisResult<AllocationOccurrence>>>
        InspectCheckpointAllocationsAsync(
            PackageVersionCellCheckpointAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        InspectCheckpointAsync(
            request,
            executor,
            PackageVersionCellAnalysisProducerKind.Allocation,
            AnalysisFindings.AllocationDescriptor,
            MatchedApiMemberAnalysisQuery.InspectAllocations,
            stageObserver: null,
            cancellationToken);

    public static Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellCheckpointAnalysisResult<DirectCall>>>
        InspectCheckpointCallSitesAsync(
            PackageVersionCellCheckpointAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        InspectCheckpointAsync(
            request,
            executor,
            PackageVersionCellAnalysisProducerKind.CallSite,
            AnalysisFindings.CallSiteDescriptor,
            MatchedApiMemberAnalysisQuery.InspectCallSites,
            stageObserver: null,
            cancellationToken);

    public static Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellCheckpointAnalysisResult<UnsafetyOccurrence>>>
        InspectCheckpointUnsafetyAsync(
            PackageVersionCellCheckpointAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        InspectCheckpointAsync(
            request,
            executor,
            PackageVersionCellAnalysisProducerKind.Unsafety,
            AnalysisFindings.UnsafetyDescriptor,
            MatchedApiMemberAnalysisQuery.InspectUnsafety,
            stageObserver: null,
            cancellationToken);

    internal static Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellBaselineAnalysisResult<AllocationOccurrence>>>
        InspectBaselineAllocationsWithStageObserverAsync(
            PackageVersionCellBaselineAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            Action<PackageVersionCellAnalysisExecutionStage> stageObserver,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stageObserver);
        return InspectBaselineAsync(
            request,
            executor,
            PackageVersionCellAnalysisProducerKind.Allocation,
            AnalysisFindings.AllocationDescriptor,
            MatchedApiMemberAnalysisQuery.InspectAllocations,
            stageObserver,
            cancellationToken);
    }

    static Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellBaselineAnalysisResult<T>>> InspectBaselineAsync<T>(
            PackageVersionCellBaselineAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            PackageVersionCellAnalysisProducerKind producer,
            FindingDescriptor descriptor,
            MatchedAnalysis<T> inspect,
            Action<PackageVersionCellAnalysisExecutionStage>? stageObserver,
            CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(
            [
                new(
                    PackageVersionCellAnalysisEndpointRole.Source,
                    request.Source),
            ],
            request.Limits,
            request.WorkspaceDeadline,
            executor,
            (workspace, endpoints, observations, token) =>
                EvaluateBaselineAsync(
                    workspace,
                    endpoints[0],
                    observations[0],
                    request,
                    producer,
                    descriptor,
                    inspect,
                    stageObserver,
                    token),
            provisionalOutcomeObserver: null,
            stageObserver,
            cancellationToken);
    }

    static Task<PackageVersionCellAnalysisOutcome<
        PackageVersionCellCheckpointAnalysisResult<T>>>
        InspectCheckpointAsync<T>(
            PackageVersionCellCheckpointAnalysisRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            PackageVersionCellAnalysisProducerKind producer,
            FindingDescriptor descriptor,
            MatchedAnalysis<T> inspect,
            Action<PackageVersionCellAnalysisExecutionStage>? stageObserver,
            CancellationToken cancellationToken)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(
            [
                new(
                    PackageVersionCellAnalysisEndpointRole.Source,
                    request.Source),
                new(
                    PackageVersionCellAnalysisEndpointRole.Destination,
                    request.Destination),
            ],
            request.Limits,
            request.WorkspaceDeadline,
            executor,
            (workspace, endpoints, observations, token) =>
                EvaluateCheckpointAsync(
                    workspace,
                    endpoints[1],
                    observations[0],
                    observations[1],
                    request,
                    producer,
                    descriptor,
                    inspect,
                    stageObserver,
                    token),
            provisionalOutcomeObserver: null,
            stageObserver,
            cancellationToken);
    }

    static async ValueTask<PackageVersionCellBaselineAnalysisResult<T>>
        EvaluateBaselineAsync<T>(
            InspectionWorkspace workspace,
            EndpointExecution source,
            CoordinatePackageObservation sourceObservation,
            PackageVersionCellBaselineAnalysisRequest request,
            PackageVersionCellAnalysisProducerKind producer,
            FindingDescriptor descriptor,
            MatchedAnalysis<T> inspect,
            Action<PackageVersionCellAnalysisExecutionStage>? stageObserver,
            CancellationToken cancellationToken)
        where T : notnull
    {
        PackageVersionCellMemberSelector selector = request.Selector;
        var selectionRequest = new ApiCoordinateSourceSelectionRequest(
            source.Endpoint.Cell.Population.Request.Range.PackageId,
            source.Endpoint.Cell.NormalizedVersion,
            selector.Type,
            selector.Member,
            selector.Library,
            selector.IncludeAll);
        stageObserver?.Invoke(
            PackageVersionCellAnalysisExecutionStage.SourceSelection);
        ApiCoordinateSourceSelectionResult selected =
            await ApiCoordinateSourceSelectionQuery.ExecuteSourceAsync(
                    workspace,
                    sourceObservation,
                    selectionRequest,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        ApiCoordinateSourceSelectionEvidence selection =
            selected.Detach(sourceObservation);
        if (selected.Status != ApiCoordinateSourceSelectionStatus.Selected)
        {
            return new PackageVersionCellBaselineAnalysisResult<T>.Unselected(
                producer,
                selection);
        }
        if (selected.Subject
            is not StructuralSubjectIdentity.MemberSubject member
            || selection.Selected is not { Member: not null } declaration)
        {
            throw new InvalidOperationException(
                "Exact baseline Member selection did not produce one Member declaration.");
        }

        var receipt = new DiffHistoryMemberSourceReceipt(
            source.Endpoint.Cell,
            declaration,
            request.FindingSubject);
        stageObserver?.Invoke(
            PackageVersionCellAnalysisExecutionStage.Correspondence);
        ApiCoordinateCorrespondenceResult correspondence =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                    workspace,
                    member,
                    receipt.DeclarationKind,
                    sourceObservation,
                    sourceObservation,
                    cancellationToken)
                .ConfigureAwait(false);
        PackageVersionCellAnalysisFinding<T> finding =
            correspondence.Status == ApiCoordinateCorrespondenceStatus.Exact
                ? await AnalyzeAsync(
                        workspace,
                        sourceObservation,
                        correspondence,
                        receipt.FindingSubject,
                        descriptor,
                        inspect,
                        stageObserver,
                        cancellationToken)
                    .ConfigureAwait(false)
                : Failed<T>(
                    receipt.FindingSubject,
                    descriptor,
                    correspondence.Failure?.Detail
                        ?? "Exact source self-correspondence did not complete.");
        ApiCoordinateCorrespondenceEvidence validation =
            correspondence.Detach();
        return new PackageVersionCellBaselineAnalysisResult<T>.Evaluated(
            producer,
            selection,
            receipt,
            validation,
            finding);
    }

    static async ValueTask<PackageVersionCellCheckpointAnalysisResult<T>>
        EvaluateCheckpointAsync<T>(
            InspectionWorkspace workspace,
            EndpointExecution destination,
            CoordinatePackageObservation sourceObservation,
            CoordinatePackageObservation destinationObservation,
            PackageVersionCellCheckpointAnalysisRequest request,
            PackageVersionCellAnalysisProducerKind producer,
            FindingDescriptor descriptor,
            MatchedAnalysis<T> inspect,
            Action<PackageVersionCellAnalysisExecutionStage>? stageObserver,
            CancellationToken cancellationToken)
        where T : notnull
    {
        (
            PackageVersionCellSourceBindingEvidence binding,
            StructuralSubjectIdentity.MemberSubject? source) =
            BindSource(request.Receipt, sourceObservation);
        if (source is null)
        {
            return new(
                producer,
                request.Receipt,
                binding,
                relationship: null,
                Failed<T>(
                    request.Receipt.FindingSubject,
                    descriptor,
                    binding.Detail
                        ?? "The source receipt did not bind in its exact cell."));
        }

        stageObserver?.Invoke(
            PackageVersionCellAnalysisExecutionStage.Correspondence);
        ApiCoordinateCorrespondenceResult correspondence =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                    workspace,
                    source,
                    request.Receipt.DeclarationKind,
                    sourceObservation,
                    destinationObservation,
                    cancellationToken)
                .ConfigureAwait(false);
        binding = CompleteSourceBinding(
            binding,
            correspondence.SourceBinding);
        PackageVersionCellAnalysisFinding<T> finding =
            binding.Status == PackageVersionCellSourceBindingStatus.Exact
                ? correspondence.Status switch
                {
                    ApiCoordinateCorrespondenceStatus.Exact =>
                        await AnalyzeAsync(
                                workspace,
                                destinationObservation,
                                correspondence,
                                request.Receipt.FindingSubject,
                                descriptor,
                                inspect,
                                stageObserver,
                                cancellationToken)
                            .ConfigureAwait(false),
                    ApiCoordinateCorrespondenceStatus.Absent =>
                        new(
                            new FindingInspection<T>.Absent(
                                FindingInspectionAbsenceKind.SubjectAbsent,
                                "The exact source declaration is absent from the destination cell.")),
                    _ => Failed<T>(
                        request.Receipt.FindingSubject,
                        descriptor,
                        correspondence.Failure?.Detail
                            ?? $"Declaration correspondence completed as {correspondence.Status}."),
                }
                : Failed<T>(
                    request.Receipt.FindingSubject,
                    descriptor,
                    binding.Detail
                        ?? "The receipt declaration did not bind in the source cell.");
        ApiCoordinateCorrespondenceEvidence relationship =
            correspondence.Detach();
        return new(
            producer,
            request.Receipt,
            binding,
            relationship,
            finding);
    }

    static async ValueTask<PackageVersionCellAnalysisFinding<T>>
        AnalyzeAsync<T>(
            InspectionWorkspace workspace,
            CoordinatePackageObservation destination,
            ApiCoordinateCorrespondenceResult correspondence,
            FindingSubject subject,
            FindingDescriptor descriptor,
            MatchedAnalysis<T> inspect,
            Action<PackageVersionCellAnalysisExecutionStage>? stageObserver,
            CancellationToken cancellationToken)
        where T : notnull
    {
        stageObserver?.Invoke(
            PackageVersionCellAnalysisExecutionStage.Analysis);
        ArtifactRootResult<MatchedApiMemberAnalysisResult<T>> access =
            await workspace.ExecutePackageRootQueryAsync(
                    destination.Correspondence,
                    destination.Generation,
                    (realization, token) => ValueTask.FromResult(
                        inspect(
                            realization,
                            correspondence,
                            subject,
                            token)),
                    destination.BindingPolicy,
                    cancellationToken)
                .ConfigureAwait(false);
        return access switch
        {
            ArtifactRootResult<
                MatchedApiMemberAnalysisResult<T>>.Available available =>
                new(
                    available.Value.Inspection,
                    available.Value.Resolution),
            ArtifactRootResult<
                MatchedApiMemberAnalysisResult<T>>.Rejected rejected =>
                Failed<T>(
                    subject,
                    descriptor,
                    $"The destination package Root rejected matched Analysis: "
                        + $"{rejected.Failure}.",
                    rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown matched Analysis Root-query result."),
        };
    }

    static (
        PackageVersionCellSourceBindingEvidence Evidence,
        StructuralSubjectIdentity.MemberSubject? Subject) BindSource(
            DiffHistoryMemberSourceReceipt receipt,
            CoordinatePackageObservation source)
    {
        CoordinateApiLibraryObservation[] matches =
        [
            .. source.Libraries.Where(library =>
                library.Subject.Package.Coordinate == receipt.Package
                && library.Asset == receipt.Asset
                && library.Assembly == receipt.Assembly),
        ];
        PackageVersionCellSourceBindingStatus status = matches.Length switch
        {
            0 => PackageVersionCellSourceBindingStatus.LibraryAbsent,
            1 => PackageVersionCellSourceBindingStatus.Exact,
            _ => PackageVersionCellSourceBindingStatus.LibraryAmbiguous,
        };
        string? detail = status switch
        {
            PackageVersionCellSourceBindingStatus.Exact => null,
            PackageVersionCellSourceBindingStatus.LibraryAbsent =>
                "The exact receipt package, asset, and assembly are absent from the source cell realization.",
            PackageVersionCellSourceBindingStatus.LibraryAmbiguous =>
                "The exact receipt package, asset, and assembly identify more than one source Library.",
            _ => throw new InvalidOperationException(
                "Unknown receipt source-binding status."),
        };
        var evidence = new PackageVersionCellSourceBindingEvidence(
            status,
            [.. matches.Select(match => match.Detach())],
            detail);
        StructuralSubjectIdentity.MemberSubject? subject =
            matches.Length == 1
                ? StructuralSubjectIdentity.ForMember(
                    StructuralSubjectIdentity.ForType(
                        matches[0].Subject,
                        receipt.DeclaringType),
                    receipt.Member)
                : null;
        return (evidence, subject);
    }

    static PackageVersionCellSourceBindingEvidence CompleteSourceBinding(
        PackageVersionCellSourceBindingEvidence libraryBinding,
        ApiDeclarationBindingResult? declarationBinding)
    {
        if (libraryBinding.Status
            != PackageVersionCellSourceBindingStatus.Exact)
        {
            return libraryBinding;
        }
        if (declarationBinding is null)
        {
            return libraryBinding with
            {
                Status =
                    PackageVersionCellSourceBindingStatus.DeclarationFailed,
                Detail =
                    "Source correspondence produced no declaration-binding evidence.",
            };
        }

        PackageVersionCellSourceBindingStatus status =
            declarationBinding.Status switch
            {
                ApiDeclarationCorrespondenceStatus.Exact =>
                    PackageVersionCellSourceBindingStatus.Exact,
                ApiDeclarationCorrespondenceStatus.Absent =>
                    PackageVersionCellSourceBindingStatus.DeclarationAbsent,
                ApiDeclarationCorrespondenceStatus.Ambiguous =>
                    PackageVersionCellSourceBindingStatus
                        .DeclarationAmbiguous,
                ApiDeclarationCorrespondenceStatus.Refused =>
                    PackageVersionCellSourceBindingStatus.DeclarationRefused,
                ApiDeclarationCorrespondenceStatus.Failed =>
                    PackageVersionCellSourceBindingStatus.DeclarationFailed,
                _ => throw new InvalidOperationException(
                    "Unknown source declaration-binding status."),
            };
        return libraryBinding with
        {
            Status = status,
            Detail = status == PackageVersionCellSourceBindingStatus.Exact
                ? null
                : declarationBinding.Detail
                    ?? $"Source declaration binding completed as "
                    + $"{declarationBinding.Status}.",
        };
    }

    static PackageVersionCellAnalysisFinding<T> Failed<T>(
        FindingSubject subject,
        FindingDescriptor descriptor,
        string reason,
        ArtifactRootFailure? rootFailure = null)
        where T : notnull =>
        new(
            new FindingInspection<T>.Failed(
                new InspectionError(subject, descriptor, reason)),
            resolution: null,
            analysisRootFailure: rootFailure);

    static async Task<PackageVersionCellAnalysisOutcome<TResult>>
        ExecuteAsync<TResult>(
            ImmutableArray<EndpointRequest> requests,
            PackageVersionCellWorkspaceLimits limits,
            DateTimeOffset workspaceDeadline,
            IPackageHouseVersionPopulationCellExecutor executor,
            Evaluate<TResult> evaluate,
            Action<PackageVersionCellAnalysisOutcome<TResult>>?
                provisionalOutcomeObserver,
            Action<PackageVersionCellAnalysisExecutionStage>? stageObserver,
            CancellationToken cancellationToken)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(evaluate);
        cancellationToken.ThrowIfCancellationRequested();

        var endpoints = ImmutableArray.CreateBuilder<EndpointExecution>(
            requests.Length);
        var noContributions =
            ImmutableArray.CreateBuilder<PackageVersionCellNoContribution>();
        foreach (EndpointRequest request in requests)
        {
            PackageHouseSettlement settlement =
                await executor.ExecuteAsync(
                        request.Endpoint.HouseExecution,
                        cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "The PackageHouse cell executor returned no settlement.");
            if (!request.Endpoint.HouseExecution.Accepts(settlement))
            {
                throw new InvalidOperationException(
                    "The PackageHouse settlement does not belong to the prepared version-population cell execution.");
            }

            PackageHouseRootContributionOutcome adaptation =
                PackageHouseRootContributionAdapter.Create(settlement);
            PackageHouseRootContribution? contribution =
                adaptation
                    is PackageHouseRootContributionOutcome.Contributed
                        contributed
                    ? contributed.Contribution
                    : null;
            endpoints.Add(
                new(
                    request.Role,
                    request.Endpoint,
                    PackageVersionCellExecutionEvidence.Create(
                        request.Endpoint.HouseExecution,
                        adaptation.Result,
                        contribution),
                    contribution));
            if (adaptation
                is PackageHouseRootContributionOutcome.NoContribution
                    noContribution)
            {
                noContributions.Add(
                    new(request.Role, noContribution.Reason));
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        ImmutableArray<EndpointExecution> executed = endpoints.ToImmutable();
        ImmutableArray<PackageVersionCellExecutionEvidence> executionEvidence =
        [
            .. executed.Select(endpoint => endpoint.Evidence),
        ];
        if (noContributions.Count > 0)
        {
            return new PackageVersionCellAnalysisOutcome<TResult>
                .NoContribution(
                    executionEvidence,
                    noContributions.ToImmutable());
        }

        var workspace = new InspectionWorkspace();
        PackageVersionCellAnalysisOutcome<TResult>? outcome = null;
        ExceptionDispatchInfo? primary = null;
        InspectionWorkspaceCloseReport? closeReport = null;
        bool scopeCommitted = false;
        bool closeFaulted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceScopeReadResult read =
                await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
            if (read is WorkspaceScopeReadResult.Unavailable unavailable)
            {
                outcome = new PackageVersionCellAnalysisOutcome<TResult>
                    .WorkspaceFailure(
                        executionEvidence,
                        PackageVersionCellAnalysisWorkspaceFailure.Failed(
                            PackageVersionCellAnalysisWorkspaceStage.ScopeRead,
                            unavailable.RuntimeFailure));
            }
            else
            {
                var current = (WorkspaceScopeReadResult.Available)read;
                var realizationOptions =
                    new PackageAssemblyContextRealizationOptions
                    {
                        MaxAssembliesPerRole = limits.MaximumAssemblies,
                        MaxAssemblyEntryBytes = limits.MaximumEntryBytes,
                        MaxAggregateRetainedImageBytes =
                            limits.MaximumRetainedImageBytes,
                    };
                WorkspaceScopeOperationResult admission =
                        await workspace.AddPackagesWithRealizationOptionsAsync(
                            current.Snapshot.Revision,
                            [
                                .. executed.Select(endpoint =>
                                    endpoint.Contribution!.Binding),
                            ],
                            realizationOptions,
                            workspaceDeadline,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (admission
                    is WorkspaceScopeOperationResult.Committed committed)
                {
                    scopeCommitted = true;
                    outcome = await EvaluateCommittedAsync(
                            workspace,
                            committed,
                            executed,
                            executionEvidence,
                            evaluate,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    outcome = AdmissionFailure<TResult>(
                        executionEvidence,
                        admission,
                        workspaceDeadline,
                        cancellationToken);
                }
            }
            if (outcome is not null)
                provisionalOutcomeObserver?.Invoke(outcome);
            stageObserver?.Invoke(
                PackageVersionCellAnalysisExecutionStage.Cleanup);
        }
        catch (Exception failure)
        {
            primary = ExceptionDispatchInfo.Capture(failure);
        }
        finally
        {
            try
            {
                closeReport =
                    await workspace.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                primary ??= ExceptionDispatchInfo.Capture(failure);
                closeFaulted = true;
                closeReport = workspace.CloseReport;
            }
        }

        PackageVersionCellWorkspaceCleanupEvidence cleanup =
            PackageVersionCellWorkspaceCleanup.Describe(
                closeReport,
                scopeCommitted,
                closeFaulted);
        if (primary is not null)
        {
            PackageVersionCellWorkspaceExceptionEvidence.Attach(
                primary.SourceException,
                cleanup);
            primary.Throw();
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException cancelled)
        {
            PackageVersionCellWorkspaceExceptionEvidence.Attach(
                cancelled,
                cleanup);
            throw;
        }

        if (outcome is null)
        {
            throw new InvalidOperationException(
                "Version-cell Analysis completed without an outcome.");
        }
        return Complete(outcome, cleanup);
    }

    static async ValueTask<PackageVersionCellAnalysisOutcome<TResult>>
        EvaluateCommittedAsync<TResult>(
            InspectionWorkspace workspace,
            WorkspaceScopeOperationResult.Committed committed,
            ImmutableArray<EndpointExecution> endpoints,
            ImmutableArray<PackageVersionCellExecutionEvidence>
                executionEvidence,
            Evaluate<TResult> evaluate,
            CancellationToken cancellationToken)
        where TResult : notnull
    {
        var observations =
            ImmutableArray.CreateBuilder<CoordinatePackageObservation>(
                endpoints.Length);
        foreach (EndpointExecution endpoint in endpoints)
        {
            PackageRootBinding binding = endpoint.Contribution!.Binding;
            WorkspacePackageOccurrenceDescriptor occurrence =
                committed.Snapshot.FindPackageOccurrence(binding)
                ?? throw new InvalidOperationException(
                    "The committed Scope did not retain an admitted package Root.");
            CoordinatePackageObservationResult observationResult =
                await CoordinateLibraryPairingQuery.ObserveAsync(
                        workspace,
                        binding,
                        occurrence,
                        cancellationToken)
                    .ConfigureAwait(false);
            if (observationResult
                is CoordinatePackageObservationResult.Unavailable failed)
            {
                return new PackageVersionCellAnalysisOutcome<TResult>
                    .WorkspaceFailure(
                        executionEvidence,
                        PackageVersionCellAnalysisWorkspaceFailure.Observation(
                            endpoint.Role,
                            failed.Failure));
            }
            observations.Add(
                ((CoordinatePackageObservationResult.Available)
                    observationResult)
                    .Observation);
        }

        TResult evaluated = await evaluate(
                workspace,
                endpoints,
                observations.ToImmutable(),
                cancellationToken)
            .ConfigureAwait(false);
        return new PackageVersionCellAnalysisOutcome<TResult>.Available(
            executionEvidence,
            evaluated);
    }

    static PackageVersionCellAnalysisOutcome<TResult>
        AdmissionFailure<TResult>(
            ImmutableArray<PackageVersionCellExecutionEvidence> executions,
            WorkspaceScopeOperationResult admission,
            DateTimeOffset workspaceDeadline,
            CancellationToken cancellationToken)
        where TResult : notnull
    {
        PackageVersionCellAnalysisWorkspaceFailure failure =
            admission switch
            {
                WorkspaceScopeOperationResult.Rejected rejected =>
                    PackageVersionCellAnalysisWorkspaceFailure.Rejected(
                        rejected.Reason),
                WorkspaceScopeOperationResult.Failed failed =>
                    PackageVersionCellAnalysisWorkspaceFailure.Failed(
                        PackageVersionCellAnalysisWorkspaceStage.ScopeAdmission,
                        failed.Failure),
                WorkspaceScopeOperationResult.Unavailable unavailable =>
                    PackageVersionCellAnalysisWorkspaceFailure.Failed(
                        PackageVersionCellAnalysisWorkspaceStage.ScopeAdmission,
                        unavailable.RuntimeFailure),
                WorkspaceScopeOperationResult.Cancelled =>
                    PackageVersionCellAnalysisWorkspaceFailure.Failed(
                        PackageVersionCellAnalysisWorkspaceStage.ScopeAdmission,
                        cancellationToken.IsCancellationRequested
                            ? ArtifactRootFailure.Cancelled
                            : DateTimeOffset.UtcNow >= workspaceDeadline
                                ? ArtifactRootFailure.DeadlineExpired
                                : ArtifactRootFailure.Cancelled),
                WorkspaceScopeOperationResult.NoEffect
                    or WorkspaceScopeOperationResult.Superseded =>
                    throw new InvalidOperationException(
                        "A fresh cell Workspace admission returned an impossible result."),
                _ => throw new InvalidOperationException(
                    "Unknown Workspace Scope admission outcome."),
            };
        return new PackageVersionCellAnalysisOutcome<TResult>
            .WorkspaceFailure(executions, failure);
    }

    internal static PackageVersionCellAnalysisOutcome<TResult>
        Complete<TResult>(
        PackageVersionCellAnalysisOutcome<TResult> outcome,
        PackageVersionCellWorkspaceCleanupEvidence cleanup)
        where TResult : notnull
    {
        if (cleanup.IsEmpty)
            return outcome;
        if (outcome
            is PackageVersionCellAnalysisOutcome<TResult>.WorkspaceFailure
                workspaceFailure)
        {
            return new PackageVersionCellAnalysisOutcome<TResult>
                .WorkspaceFailure(
                    workspaceFailure.Executions,
                    workspaceFailure.Failure,
                    cleanup);
        }
        if (outcome
            is PackageVersionCellAnalysisOutcome<TResult>.Available available)
        {
            return new PackageVersionCellAnalysisOutcome<TResult>
                .CleanupFailure(
                    available.Executions,
                    cleanup);
        }

        throw new InvalidOperationException(
            "Only a Workspace-bearing outcome can carry cleanup evidence.");
    }

    delegate MatchedApiMemberAnalysisResult<T> MatchedAnalysis<T>(
        PackageAssemblyContextRealization realization,
        ApiCoordinateCorrespondenceResult correspondence,
        FindingSubject subject,
        CancellationToken cancellationToken)
        where T : notnull;

    delegate ValueTask<TResult> Evaluate<TResult>(
        InspectionWorkspace workspace,
        ImmutableArray<EndpointExecution> endpoints,
        ImmutableArray<CoordinatePackageObservation> observations,
        CancellationToken cancellationToken)
        where TResult : notnull;

    sealed record EndpointRequest(
        PackageVersionCellAnalysisEndpointRole Role,
        PackageVersionCellAnalysisEndpoint Endpoint);

    sealed record EndpointExecution(
        PackageVersionCellAnalysisEndpointRole Role,
        PackageVersionCellAnalysisEndpoint Endpoint,
        PackageVersionCellExecutionEvidence Evidence,
        PackageHouseRootContribution? Contribution);
}
