using System.Collections.Immutable;
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
                provisionalOutcomeObserver: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

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
            provisionalOutcomeObserver,
            cancellationToken);
    }

    static async Task<PackageVersionCellMetadataInspectionOutcome>
        ExecuteCoreAsync(
            PackageVersionCellMetadataInspectionRequest request,
            IPackageHouseVersionPopulationCellExecutor executor,
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
                    Evidence(
                        request.HouseExecution,
                        noContribution.Result,
                        contribution: null),
                    noContribution.Reason);
        }

        PackageHouseRootContribution contribution =
            ((PackageHouseRootContributionOutcome.Contributed)adaptation)
                .Contribution;
        PackageVersionCellMetadataInspectionEvidence evidence =
            Evidence(
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
                PackageVersionCellMetadataInspectionLimits limits =
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
                        .ReplaceScopeWithRealizationOptionsAsync(
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

        var cleanup = new PackageVersionCellMetadataCleanupEvidence(
            DescribeClose(
                closeReport,
                scopeCommitted,
                closeFaulted));
        if (primary is not null)
        {
            PackageVersionCellMetadataInspectionExceptionEvidence.Attach(
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
            PackageVersionCellMetadataInspectionExceptionEvidence.Attach(
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
        PackageVersionCellMetadataCleanupEvidence cleanup)
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
            PackageVersionCellMetadataInspectionEvidence evidence,
            PackageVersionCellApiInspectionRequest? apiInspection,
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
                                    ? AssemblyContextApiSurfaceQuery.ExecuteBounded(
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
        PackageVersionCellMetadataInspectionEvidence evidence,
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
            PackageVersionCellMetadataInspectionEvidence evidence,
            PackageVersionCellMetadataWorkspaceStage stage,
            ArtifactRootFailure failure) =>
        new(
            evidence,
            PackageVersionCellMetadataWorkspaceFailure.Failed(
                stage,
                failure));

    static PackageVersionCellMetadataInspectionEvidence Evidence(
        PackageHouseVersionPopulationCellExecution execution,
        PackageHouseResult result,
        PackageHouseRootContribution? contribution) =>
        new(
            execution,
            result,
            contribution?.Realization
                ?? result.Evidence.Realization
                    as PackageHouseRealizationReceipt.Compile,
            contribution?.Binding.Coordinate);

    internal static ImmutableArray<
        PackageVersionCellMetadataCleanupFailure> DescribeClose(
            InspectionWorkspaceCloseReport? report,
            bool scopeCommitted,
            bool closeFaulted)
    {
        var failures = ImmutableArray.CreateBuilder<
            PackageVersionCellMetadataCleanupFailure>();
        if (report is not null)
        {
            if (!scopeCommitted && !report.Groups.IsEmpty)
            {
                Add(
                    PackageVersionCellMetadataCleanupStage
                        .CloseReportContract,
                    report.Groups.Length);
            }
            else if (scopeCommitted)
            {
                Add(
                    PackageVersionCellMetadataCleanupStage
                        .CloseReportContract,
                    report.Groups.Count(group =>
                        group is not
                            InspectionWorkspaceDirectGroupCloseResult));
                Add(
                    PackageVersionCellMetadataCleanupStage.GroupRelease,
                    report.Groups
                        .OfType<
                            InspectionWorkspaceDirectGroupCloseResult>()
                        .Count(group => !group.Succeeded));
            }
            Add(
                PackageVersionCellMetadataCleanupStage.ArtifactRelease,
                report.ArtifactSessionCleanupFailures.Length);
        }
        if (closeFaulted)
        {
            Add(
                PackageVersionCellMetadataCleanupStage.CloseOrchestration,
                1);
        }
        return failures.ToImmutable();

        void Add(
            PackageVersionCellMetadataCleanupStage stage,
            int count)
        {
            if (count > 0)
            {
                failures.Add(
                    new PackageVersionCellMetadataCleanupFailure(
                        stage,
                        count));
            }
        }
    }
}
