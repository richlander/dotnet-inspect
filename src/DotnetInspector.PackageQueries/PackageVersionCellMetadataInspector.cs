using System.Runtime.ExceptionServices;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Inspects one PackageHouse version-population cell through a bounded
/// ephemeral Workspace.
/// </summary>
public static class PackageVersionCellMetadataInspector
{
    public static async Task<PackageVersionCellMetadataInspectionOutcome>
        ExecuteAsync(
            PackageVersionCellMetadataInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default)
    {
        return await ExecuteCoreAsync(
                request,
                executor,
                resolveApiConstraints: false,
                provisionalOutcomeObserver: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    internal static Task<PackageVersionCellMetadataInspectionOutcome>
        ExecuteForApiComparisonAsync(
            PackageVersionCellMetadataInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(
            request,
            executor,
            resolveApiConstraints: true,
            provisionalOutcomeObserver: null,
            cancellationToken);

    internal static Task<PackageVersionCellMetadataInspectionOutcome>
        ExecuteWithProvisionalOutcomeObserverAsync(
            PackageVersionCellMetadataInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            Action<PackageVersionCellMetadataInspectionOutcome>
                provisionalOutcomeObserver,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provisionalOutcomeObserver);
        return ExecuteCoreAsync(
            request,
            executor,
            resolveApiConstraints: false,
            provisionalOutcomeObserver,
            cancellationToken);
    }

    static async Task<PackageVersionCellMetadataInspectionOutcome>
        ExecuteCoreAsync(
            PackageVersionCellMetadataInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
            bool resolveApiConstraints,
            Action<PackageVersionCellMetadataInspectionOutcome>?
                provisionalOutcomeObserver,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executor);
        cancellationToken.ThrowIfCancellationRequested();

        PackageHouseSettlement settlement =
            await executor.ExecuteAsync(
                    request.HouseExecution,
                    cancellationToken)
                .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "The PackageHouse cell executor returned no settlement.");
        if (!request.HouseExecution.Accepts(settlement))
        {
            throw new InvalidOperationException(
                "The PackageHouse settlement does not belong to the prepared version-population cell execution.");
        }

        PackageHouseRootContributionOutcome adaptation =
            PackageHouseRootContributionAdapter.Create(settlement);
        if (adaptation
            is PackageHouseRootContributionOutcome.NoContribution
                noContribution)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PackageVersionCellMetadataInspectionOutcome
                .NoContribution(
                    PackageVersionCellExecutionEvidence.Create(
                        request.HouseExecution,
                        noContribution.Result,
                        contribution: null),
                    noContribution.Reason);
        }

        PackageHouseRootContribution contribution =
            ((PackageHouseRootContributionOutcome.Contributed)adaptation)
                .Contribution;
        PackageVersionCellExecutionEvidence evidence =
            PackageVersionCellExecutionEvidence.Create(
                request.HouseExecution,
                contribution.Result,
                contribution);
        var workspace = new InspectionWorkspace();
        PackageVersionCellMetadataInspectionOutcome? outcome = null;
        ExceptionDispatchInfo? primary = null;
        InspectionWorkspaceCloseReport? closeReport = null;
        bool scopeCommitted = false;
        bool closeFaulted = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkspaceScopeReadResult read =
                await workspace.GetScopeSnapshotAsync()
                    .ConfigureAwait(false);
            if (read is WorkspaceScopeReadResult.Unavailable unavailable)
            {
                outcome = WorkspaceFailure(
                    evidence,
                    PackageVersionCellMetadataWorkspaceStage.ScopeRead,
                    unavailable.RuntimeFailure);
            }
            else
            {
                var current =
                    (WorkspaceScopeReadResult.Available)read;
                PackageVersionCellWorkspaceLimits limits =
                    request.Limits;
                var realizationOptions =
                    new PackageAssemblyContextRealizationOptions
                    {
                        MaxAssembliesPerRole =
                            limits.MaximumAssemblies,
                        MaxAssemblyEntryBytes =
                            limits.MaximumEntryBytes,
                        MaxAggregateRetainedImageBytes =
                            limits.MaximumRetainedImageBytes,
                    };
                WorkspaceScopeOperationResult admission =
                    await workspace
                        .AddPackagesWithRealizationOptionsAsync(
                            current.Snapshot.Revision,
                            [contribution.Binding],
                            realizationOptions,
                            request.WorkspaceDeadline,
                            cancellationToken)
                        .ConfigureAwait(false);
                if (admission
                    is WorkspaceScopeOperationResult.Committed committed)
                {
                    scopeCommitted = true;
                    outcome = await QueryCommittedRootAsync(
                            workspace,
                            committed,
                            contribution.Binding,
                            evidence,
                            request.ApiInspection,
                            resolveApiConstraints,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    outcome = AdmissionFailure(
                        request,
                        evidence,
                        admission,
                        cancellationToken);
                }
            }
            if (outcome is not null)
                provisionalOutcomeObserver?.Invoke(outcome);
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
                "Version-cell Metadata inspection completed without an outcome.");
        }
        return Complete(outcome, cleanup);
    }

    internal static PackageVersionCellMetadataInspectionOutcome Complete(
        PackageVersionCellMetadataInspectionOutcome outcome,
        PackageVersionCellWorkspaceCleanupEvidence cleanup)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(cleanup);
        if (cleanup.IsEmpty)
            return outcome;
        if (outcome
            is PackageVersionCellMetadataInspectionOutcome.WorkspaceFailure
                workspaceFailure)
        {
            return new PackageVersionCellMetadataInspectionOutcome
                .WorkspaceFailure(
                    workspaceFailure.Evidence,
                    workspaceFailure.Failure,
                    cleanup);
        }

        return new PackageVersionCellMetadataInspectionOutcome
            .CleanupFailure(
                outcome.Evidence,
                cleanup);
    }

    static async ValueTask<PackageVersionCellMetadataInspectionOutcome>
        QueryCommittedRootAsync(
            InspectionWorkspace workspace,
            WorkspaceScopeOperationResult.Committed committed,
            PackageRootBinding binding,
            PackageVersionCellExecutionEvidence evidence,
            PackageVersionCellApiInspectionRequest? apiInspection,
            bool resolveApiConstraints,
            CancellationToken cancellationToken)
    {
        WorkspacePackageOccurrenceDescriptor occurrence =
            committed.Snapshot.FindPackageOccurrence(binding)
            ?? throw new InvalidOperationException(
                "The committed Scope did not retain the admitted package Root.");
        if (occurrence.Occurrence.Correspondence
                is not PackageArtifactRootCorrespondence correspondence
            || occurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready ready)
        {
            throw new InvalidOperationException(
                "The committed package Root did not publish query admission.");
        }

        ArtifactRootResult<PackageVersionCellMetadataInspectionOutcome.Available>
            query = await workspace.ExecutePackageRootQueryAsync(
                    correspondence,
                    ready.Generation,
                    (realization, token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        AssemblyContextResult<MetadataImageOverview> metadata =
                            realization.HasAssemblyContexts
                                ? AssemblyContextMetadataImageQuery.Execute(
                                    realization.SurfaceGroup)
                                : new([]);
                        token.ThrowIfCancellationRequested();
                        PackageVersionCellApiInspectionResult? api = null;
                        if (apiInspection is not null)
                        {
                            AssemblyContextApiSurfaceResult surfaces =
                                realization.HasAssemblyContexts
                                    ? resolveApiConstraints
                                        ? AssemblyContextApiSurfaceQuery
                                            .ExecuteBoundedResolved(
                                                realization.SurfaceGroup,
                                                apiInspection.Scope,
                                                apiInspection.Limits)
                                        : AssemblyContextApiSurfaceQuery
                                            .ExecuteBounded(
                                                realization.SurfaceGroup,
                                                apiInspection.Scope,
                                                apiInspection.Limits)
                                    : new(new([]), ApiAccessibility.Buckets([]));
                            api = new(apiInspection, surfaces, token);
                        }
                        token.ThrowIfCancellationRequested();
                        return ValueTask.FromResult(
                            new PackageVersionCellMetadataInspectionOutcome.Available(
                                evidence,
                                metadata,
                                api));
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        return query switch
        {
            ArtifactRootResult<
                PackageVersionCellMetadataInspectionOutcome.Available>.Available
                    available =>
                available.Value,
            ArtifactRootResult<
                PackageVersionCellMetadataInspectionOutcome.Available>.Rejected
                    rejected =>
                WorkspaceFailure(
                    evidence,
                    PackageVersionCellMetadataWorkspaceStage.RootQuery,
                    rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown package Root query outcome."),
        };
    }

    static PackageVersionCellMetadataInspectionOutcome AdmissionFailure(
        PackageVersionCellMetadataInspectionRequest request,
        PackageVersionCellExecutionEvidence evidence,
        WorkspaceScopeOperationResult admission,
        CancellationToken cancellationToken) =>
        admission switch
        {
            WorkspaceScopeOperationResult.Rejected rejected =>
                new PackageVersionCellMetadataInspectionOutcome
                    .WorkspaceFailure(
                        evidence,
                        PackageVersionCellMetadataWorkspaceFailure.Rejected(
                            PackageVersionCellMetadataWorkspaceStage
                                .ScopeAdmission,
                            rejected.Reason)),
            WorkspaceScopeOperationResult.Failed failed =>
                WorkspaceFailure(
                    evidence,
                    PackageVersionCellMetadataWorkspaceStage.ScopeAdmission,
                    failed.Failure),
            WorkspaceScopeOperationResult.Unavailable unavailable =>
                WorkspaceFailure(
                    evidence,
                    PackageVersionCellMetadataWorkspaceStage.ScopeAdmission,
                    unavailable.RuntimeFailure),
            WorkspaceScopeOperationResult.Cancelled =>
                WorkspaceFailure(
                    evidence,
                    PackageVersionCellMetadataWorkspaceStage.ScopeAdmission,
                    cancellationToken.IsCancellationRequested
                        ? ArtifactRootFailure.Cancelled
                        : DateTimeOffset.UtcNow
                            >= request.WorkspaceDeadline
                            ? ArtifactRootFailure.DeadlineExpired
                            : ArtifactRootFailure.Cancelled),
            WorkspaceScopeOperationResult.NoEffect
                or WorkspaceScopeOperationResult.Superseded =>
                throw new InvalidOperationException(
                    "A fresh one-Root Workspace admission returned an impossible result."),
            _ => throw new InvalidOperationException(
                "Unknown Workspace Scope admission outcome."),
        };

    static PackageVersionCellMetadataInspectionOutcome.WorkspaceFailure
        WorkspaceFailure(
            PackageVersionCellExecutionEvidence evidence,
            PackageVersionCellMetadataWorkspaceStage stage,
            ArtifactRootFailure failure) =>
        new(
            evidence,
            PackageVersionCellMetadataWorkspaceFailure.Failed(
                stage,
                failure));

}
