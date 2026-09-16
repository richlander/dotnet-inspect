using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Host-supplied execution of one PackageHouse version cell as a compile-only,
/// package-shaped realization.
/// </summary>
public interface IPackageVersionCellCompileExecutor
{
    Task<PackageHouseSettlement> ExecuteAsync(
        PackageHouseVersionPopulationCell cell,
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One prepared request to inspect the Metadata images selected for a
/// PackageHouse version-population cell.
/// </summary>
public sealed class PackageVersionCellMetadataInspectionRequest
{
    public PackageVersionCellMetadataInspectionRequest(
        PackageHouseVersionPopulationCell cell,
        PackageHouseOperation operation,
        PackageHouseTargetContext? targetContext,
        DateTimeOffset workspaceDeadline)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Profile != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Version-cell Metadata inspection requires a Realize PackageHouse operation.",
                nameof(operation));
        }
        if (workspaceDeadline == DateTimeOffset.MinValue
            || workspaceDeadline == DateTimeOffset.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(workspaceDeadline),
                workspaceDeadline,
                "Version-cell Metadata inspection requires a finite Workspace deadline.");
        }

        Cell = cell;
        Operation = operation;
        TargetContext = targetContext;
        WorkspaceDeadline = workspaceDeadline;
    }

    public PackageHouseVersionPopulationCell Cell { get; }

    public PackageHouseOperation Operation { get; }

    public PackageHouseTargetContext? TargetContext { get; }

    public DateTimeOffset WorkspaceDeadline { get; }
}

/// <summary>The resource-free semantic result before Workspace cleanup settles.</summary>
public abstract record PackageVersionCellMetadataInspectionResult
{
    private protected PackageVersionCellMetadataInspectionResult(
        PackageHouseVersionPopulationCell cell,
        PackageHouseResult houseResult)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentNullException.ThrowIfNull(houseResult);
        Cell = cell;
        HouseResult = houseResult;
    }

    public PackageHouseVersionPopulationCell Cell { get; }

    public PackageHouseResult HouseResult { get; }

    public sealed record Inspected : PackageVersionCellMetadataInspectionResult
    {
        internal Inspected(
            PackageHouseVersionPopulationCell cell,
            PackageHouseResult houseResult,
            AssemblyContextResult<MetadataImageOverview> metadata)
            : base(cell, houseResult)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            Metadata = metadata;
        }

        public AssemblyContextResult<MetadataImageOverview> Metadata { get; }
    }

    public sealed record NoAssemblyContext
        : PackageVersionCellMetadataInspectionResult
    {
        internal NoAssemblyContext(
            PackageHouseVersionPopulationCell cell,
            PackageHouseResult houseResult,
            PackageCompileAssetSelectionStatus selectionStatus)
            : base(cell, houseResult)
        {
            if (!Enum.IsDefined(selectionStatus))
                throw new ArgumentOutOfRangeException(nameof(selectionStatus));
            SelectionStatus = selectionStatus;
        }

        public PackageCompileAssetSelectionStatus SelectionStatus { get; }
    }

    public sealed record NoContribution
        : PackageVersionCellMetadataInspectionResult
    {
        internal NoContribution(
            PackageHouseVersionPopulationCell cell,
            PackageHouseResult houseResult,
            PackageHouseRootNoContributionReason reason)
            : base(cell, houseResult)
        {
            Reason = reason;
        }

        public PackageHouseRootNoContributionReason Reason { get; }
    }

    public sealed record WorkspaceNotCommitted
        : PackageVersionCellMetadataInspectionResult
    {
        internal WorkspaceNotCommitted(
            PackageHouseVersionPopulationCell cell,
            PackageHouseResult houseResult,
            WorkspaceScopeOperationResult result)
            : base(cell, houseResult)
        {
            ArgumentNullException.ThrowIfNull(result);
            Result = result;
        }

        public WorkspaceScopeOperationResult Result { get; }
    }

    public sealed record RootQueryRejected
        : PackageVersionCellMetadataInspectionResult
    {
        internal RootQueryRejected(
            PackageHouseVersionPopulationCell cell,
            PackageHouseResult houseResult,
            ArtifactRootFailure failure)
            : base(cell, houseResult)
        {
            Failure = failure;
        }

        public ArtifactRootFailure Failure { get; }
    }
}

public enum PackageVersionCellMetadataCleanupStage
{
    WorkspaceGroupRelease,
    WorkspaceArtifactRootRelease,
    WorkspaceCloseOrchestration,
}

public readonly record struct PackageVersionCellMetadataCleanupFailure(
    PackageVersionCellMetadataCleanupStage Stage,
    int Count);

/// <summary>Resource-free cleanup evidence for one ephemeral Workspace.</summary>
public sealed record PackageVersionCellMetadataCleanupEvidence
{
    internal PackageVersionCellMetadataCleanupEvidence(
        ImmutableArray<PackageVersionCellMetadataCleanupFailure> failures)
    {
        Failures = failures;
    }

    public ImmutableArray<PackageVersionCellMetadataCleanupFailure> Failures
    {
        get;
    }

    public bool IsEmpty => Failures.IsEmpty;
}

/// <summary>
/// The final result published only after the ephemeral Workspace has closed.
/// </summary>
public abstract record PackageVersionCellMetadataInspectionOutcome
{
    private protected PackageVersionCellMetadataInspectionOutcome(
        PackageVersionCellMetadataInspectionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Result = result;
    }

    public PackageVersionCellMetadataInspectionResult Result { get; }

    public sealed record Completed
        : PackageVersionCellMetadataInspectionOutcome
    {
        internal Completed(
            PackageVersionCellMetadataInspectionResult result)
            : base(result)
        {
        }
    }

    public sealed record CleanupFailed
        : PackageVersionCellMetadataInspectionOutcome
    {
        internal CleanupFailed(
            PackageVersionCellMetadataInspectionResult result,
            PackageVersionCellMetadataCleanupEvidence cleanup)
            : base(result)
        {
            ArgumentNullException.ThrowIfNull(cleanup);
            if (cleanup.IsEmpty)
            {
                throw new ArgumentException(
                    "A cleanup-failed outcome requires at least one cleanup failure.",
                    nameof(cleanup));
            }
            Cleanup = cleanup;
        }

        public PackageVersionCellMetadataCleanupEvidence Cleanup { get; }
    }
}

/// <summary>Cleanup evidence attached when a primary exception still propagates.</summary>
public static class PackageVersionCellMetadataInspectionExceptionEvidence
{
    private static readonly object CleanupKey = new();

    public static bool TryGetCleanup(
        Exception primary,
        [NotNullWhen(true)]
        out PackageVersionCellMetadataCleanupEvidence? cleanup)
    {
        ArgumentNullException.ThrowIfNull(primary);
        cleanup = primary.Data[CleanupKey]
            as PackageVersionCellMetadataCleanupEvidence;
        return cleanup is not null;
    }

    internal static void Attach(
        Exception primary,
        PackageVersionCellMetadataCleanupEvidence cleanup)
    {
        if (!cleanup.IsEmpty)
            primary.Data[CleanupKey] = cleanup;
    }
}

/// <summary>
/// Composes one PackageHouse version cell through an ephemeral Workspace and
/// the existing assembly-context Metadata image query.
/// </summary>
public static class PackageVersionCellMetadataInspection
{
    public static async Task<PackageVersionCellMetadataInspectionOutcome>
        ExecuteAsync(
            PackageVersionCellMetadataInspectionRequest request,
            IPackageVersionCellCompileExecutor executor,
            CancellationToken cancellationToken = default)
        => await ExecuteWithCancellationNormalizationAsync(
            request,
            executor,
            static () => new InspectionWorkspace(),
            cancellationToken).ConfigureAwait(false);

    internal static Task<PackageVersionCellMetadataInspectionOutcome>
        ExecuteAsync(
            PackageVersionCellMetadataInspectionRequest request,
            IPackageVersionCellCompileExecutor executor,
            Func<InspectionWorkspace> workspaceFactory,
            CancellationToken cancellationToken = default) =>
        ExecuteWithCancellationNormalizationAsync(
            request,
            executor,
            workspaceFactory,
            cancellationToken);

    private static async Task<
        PackageVersionCellMetadataInspectionOutcome>
        ExecuteWithCancellationNormalizationAsync(
            PackageVersionCellMetadataInspectionRequest request,
            IPackageVersionCellCompileExecutor executor,
            Func<InspectionWorkspace> workspaceFactory,
            CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteCoreAsync(
                request,
                executor,
                workspaceFactory,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException canceled)
            when (cancellationToken.IsCancellationRequested
                && canceled.CancellationToken != cancellationToken)
        {
            var normalized = new OperationCanceledException(
                canceled.Message,
                canceled,
                cancellationToken);
            if (PackageVersionCellMetadataInspectionExceptionEvidence
                .TryGetCleanup(canceled, out var cleanup))
            {
                PackageVersionCellMetadataInspectionExceptionEvidence.Attach(
                    normalized,
                    cleanup);
            }
            throw normalized;
        }
    }

    private static async Task<
        PackageVersionCellMetadataInspectionOutcome> ExecuteCoreAsync(
            PackageVersionCellMetadataInspectionRequest request,
            IPackageVersionCellCompileExecutor executor,
            Func<InspectionWorkspace> workspaceFactory,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(workspaceFactory);
        cancellationToken.ThrowIfCancellationRequested();

        PackageHouseSettlement settlement =
            await executor.ExecuteAsync(
                request.Cell,
                request.Operation,
                request.TargetContext,
                cancellationToken).ConfigureAwait(false);
        ValidateSettlement(request, settlement);
        cancellationToken.ThrowIfCancellationRequested();

        PackageHouseRootContributionOutcome adapted =
            PackageHouseRootContributionAdapter.Create(settlement);
        if (adapted
            is PackageHouseRootContributionOutcome.NoContribution
                noContribution)
        {
            return new PackageVersionCellMetadataInspectionOutcome.Completed(
                new PackageVersionCellMetadataInspectionResult.NoContribution(
                    request.Cell,
                    noContribution.Result,
                    noContribution.Reason));
        }

        PackageHouseRootContribution contribution =
            ((PackageHouseRootContributionOutcome.Contributed)adapted)
                .Contribution;
        InspectionWorkspace workspace =
            workspaceFactory()
            ?? throw new InvalidOperationException(
                "The Workspace factory returned null.");
        PackageVersionCellMetadataInspectionResult? result = null;
        ExceptionDispatchInfo? primary = null;
        InspectionWorkspaceCloseReport? closeReport = null;
        bool closeFaulted = false;
        try
        {
            WorkspaceScopeReadResult initial =
                await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
            if (initial is WorkspaceScopeReadResult.Unavailable unavailable)
            {
                result = new PackageVersionCellMetadataInspectionResult
                    .WorkspaceNotCommitted(
                        request.Cell,
                        contribution.Result,
                        new WorkspaceScopeOperationResult.Unavailable(
                            unavailable.LastSnapshot,
                            unavailable.RuntimeFailure));
            }
            else
            {
                WorkspaceScopeSnapshot initialSnapshot =
                    ((WorkspaceScopeReadResult.Available)initial).Snapshot;
                WorkspaceScopeOperationResult admission =
                    await workspace.ReplaceScopeAsync(
                        initialSnapshot.Revision,
                        [contribution.Binding],
                        request.WorkspaceDeadline,
                        cancellationToken).ConfigureAwait(false);
                result = admission
                    is WorkspaceScopeOperationResult.Committed committed
                    ? await QueryCommittedRootAsync(
                        request,
                        contribution,
                        workspace,
                        committed.Snapshot,
                        cancellationToken).ConfigureAwait(false)
                    : new PackageVersionCellMetadataInspectionResult
                        .WorkspaceNotCommitted(
                            request.Cell,
                            contribution.Result,
                            admission);
            }
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
            catch (Exception)
            {
                closeFaulted = true;
                closeReport = workspace.CloseReport;
            }
        }

        PackageVersionCellMetadataCleanupEvidence cleanup =
            DescribeCleanup(closeReport, closeFaulted);
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
        catch (OperationCanceledException canceled)
        {
            PackageVersionCellMetadataInspectionExceptionEvidence.Attach(
                canceled,
                cleanup);
            throw;
        }

        if (result is null)
        {
            throw new InvalidOperationException(
                "Version-cell Metadata inspection completed without a result.");
        }

        return cleanup.IsEmpty
            ? new PackageVersionCellMetadataInspectionOutcome.Completed(result)
            : new PackageVersionCellMetadataInspectionOutcome.CleanupFailed(
                result,
                cleanup);
    }

    private static async Task<PackageVersionCellMetadataInspectionResult>
        QueryCommittedRootAsync(
            PackageVersionCellMetadataInspectionRequest request,
            PackageHouseRootContribution contribution,
            InspectionWorkspace workspace,
            WorkspaceScopeSnapshot snapshot,
            CancellationToken cancellationToken)
    {
        WorkspacePackageOccurrenceDescriptor? occurrence =
            snapshot.FindPackageOccurrence(contribution.Binding);
        if (occurrence is null)
        {
            return Rejected(ArtifactRootFailure.CompositionMismatch);
        }

        if (occurrence.Realization.Status
            is ArtifactRootRealizationStatus.Failed failed)
        {
            return Rejected(failed.Failure);
        }
        if (occurrence.Realization.Status
            is not ArtifactRootRealizationStatus.Ready ready)
        {
            return Rejected(ArtifactRootFailure.CompositionMismatch);
        }

        PackageArtifactRootCorrespondence correspondence =
            occurrence.Occurrence.Correspondence
                as PackageArtifactRootCorrespondence
            ?? throw new InvalidOperationException(
                "A package occurrence must retain package Root correspondence.");
        ArtifactRootResult<RootMetadataResult> query =
            await workspace.ExecutePackageRootQueryAsync(
                correspondence,
                ready.Generation,
                (realization, _) => ValueTask.FromResult(
                    InspectRoot(
                        realization,
                        contribution.SelectionReceipt.Selection.Status)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        return query switch
        {
            ArtifactRootResult<RootMetadataResult>.Rejected rejected =>
                Rejected(rejected.Failure),
            ArtifactRootResult<RootMetadataResult>.Available available =>
                available.Value switch
                {
                    RootMetadataResult.Inspected inspected =>
                        new PackageVersionCellMetadataInspectionResult.Inspected(
                            request.Cell,
                            contribution.Result,
                            inspected.Metadata),
                    RootMetadataResult.NoAssemblyContext noContext =>
                        new PackageVersionCellMetadataInspectionResult
                            .NoAssemblyContext(
                                request.Cell,
                                contribution.Result,
                                noContext.SelectionStatus),
                    _ => throw new InvalidOperationException(
                        "Unknown package Root Metadata result."),
                },
            _ => throw new InvalidOperationException(
                "Unknown package Root query result."),
        };

        PackageVersionCellMetadataInspectionResult Rejected(
            ArtifactRootFailure failure) =>
            new PackageVersionCellMetadataInspectionResult.RootQueryRejected(
                request.Cell,
                contribution.Result,
                failure);
    }

    private static RootMetadataResult InspectRoot(
        PackageAssemblyContextRealization realization,
        PackageCompileAssetSelectionStatus selectionStatus)
    {
        if (!realization.HasAssemblyContexts)
        {
            if (selectionStatus == PackageCompileAssetSelectionStatus.Selected)
            {
                throw new InvalidOperationException(
                    "A selected compile surface produced no assembly context.");
            }
            return new RootMetadataResult.NoAssemblyContext(selectionStatus);
        }

        return new RootMetadataResult.Inspected(
            AssemblyContextMetadataImageQuery.Execute(
                realization.SurfaceGroup));
    }

    private static void ValidateSettlement(
        PackageVersionCellMetadataInspectionRequest request,
        PackageHouseSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        PackageHouseRequest houseRequest = settlement.Result.Evidence.Request;
        if (!request.Cell.OwnsRequest(houseRequest)
            || !ReferenceEquals(
                houseRequest.Operation,
                request.Operation)
            || !ReferenceEquals(
                houseRequest.TargetContext,
                request.TargetContext)
            || houseRequest.AssetSelection
                != PackageHouseAssetSelectionKind.Compile
            || houseRequest.LibraryHandoff
                != PackageHouseLibraryHandoffMode.PackageOnly)
        {
            throw new InvalidOperationException(
                "The version-cell executor returned a settlement for another request.");
        }
    }

    private static PackageVersionCellMetadataCleanupEvidence DescribeCleanup(
        InspectionWorkspaceCloseReport? report,
        bool closeFaulted)
    {
        var failures =
            ImmutableArray.CreateBuilder<
                PackageVersionCellMetadataCleanupFailure>();
        if (report is not null)
        {
            Add(
                PackageVersionCellMetadataCleanupStage.WorkspaceGroupRelease,
                report.Groups.Count(group => !group.Succeeded));
            Add(
                PackageVersionCellMetadataCleanupStage
                    .WorkspaceArtifactRootRelease,
                report.ArtifactSessionCleanupFailures.Length);
        }
        if (closeFaulted)
        {
            Add(
                PackageVersionCellMetadataCleanupStage
                    .WorkspaceCloseOrchestration,
                1);
        }
        return new(failures.ToImmutable());

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

    private abstract record RootMetadataResult
    {
        private RootMetadataResult()
        {
        }

        internal sealed record Inspected(
            AssemblyContextResult<MetadataImageOverview> Metadata)
            : RootMetadataResult;

        internal sealed record NoAssemblyContext(
            PackageCompileAssetSelectionStatus SelectionStatus)
            : RootMetadataResult;
    }
}
