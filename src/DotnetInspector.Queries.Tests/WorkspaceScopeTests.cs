using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceScopeTests
{
    static DateTimeOffset Deadline => DateTimeOffset.UtcNow.AddMinutes(5);

    [Fact]
    public async Task InitialScopeIsCompleteEmptyClosedAndWorkspaceExact()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        Assert.Same(workspace.Identity, initial.Revision.Workspace);
        Assert.Empty(initial.Revision.Roots);
        Assert.Empty(initial.Roots);
        Assert.Null(initial.Preparing);
        Assert.Equal(64, initial.Revision.Limits.MaxRoots);
        Assert.Equal(WorkspaceClosureState.ClosedBoundary, initial.Closure.State);
        Assert.Same(initial.Revision.Identity, initial.Closure.Revision);
        Assert.Same(initial, await Current(workspace));
        Assert.Same(initial.PhysicalComposition, ArtifactAvailable(
            await workspace.GetCurrentArtifactRootCompositionGenerationAsync(workspace.Identity)));
    }

    [Fact]
    public async Task DefaultReplacementPublishesTwoSmallPackagesAndResourceFreePresentation()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        WorkspaceScopeSnapshot current = Committed(await workspace.ReplaceScopeAsync(
            initial.Revision, [Binding("Mixed.Case"), Binding("Second.Package")],
            Deadline, TestContext.Current.CancellationToken)).Snapshot;

        Assert.Equal(["Mixed.Case", "Second.Package"], Names(current));
        Assert.NotSame(initial.Revision.Identity, current.Revision.Identity);
        Assert.NotSame(initial.PublicationBase, current.PublicationBase);
        Assert.NotSame(initial.Closure.Identity, current.Closure.Identity);
        Assert.Empty(initial.Roots);
        Assert.Null(current.Preparing);
        Assert.Same(current, await Current(workspace));
        Assert.All(current.Roots, row =>
        {
            var package = Assert.IsType<WorkspaceRootDescriptor.Package>(row.Occurrence.Root);
            Assert.Equal(WorkspaceRootKind.Package, package.Kind);
            Assert.Equal("1.0.0", package.PackageVersion);
            Assert.Equal("net11.0", package.TargetFramework);
            Assert.Equal(package.PackageId.ToLowerInvariant(), package.Coordinate.PackageId);
            Assert.Equal(PackageCompileAssetSelectionStatus.Selected, package.SelectionStatus);
            Assert.Same(workspace.Identity, row.Occurrence.Identity.WorkspaceIdentity);
            Assert.Same(row.Occurrence, current.Revision.Roots[current.Roots.IndexOf(row)]);
            Assert.Equal(row.Occurrence.Correspondence, row.Realization.Correspondence);
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(row.Realization.Status);
        });
    }

    [Fact]
    public async Task ReplacementKeepsPriorRevisionCurrentDuringPreparation()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Replace(workspace, Binding("Prior.Package"));
        WorkspaceScopeSnapshot? observed = null;
        PackageRootBinding next = Binding("Next.Package", onOpen: () =>
            observed = Current(workspace).GetAwaiter().GetResult());
        WorkspaceScopeSnapshot replacement =
            Committed(await workspace.ReplaceScopeAsync(
                prior.Revision, [next], Deadline, TestContext.Current.CancellationToken)).Snapshot;

        Assert.NotNull(observed);
        Assert.Same(prior.Revision, observed.Revision);
        Assert.Same(prior.PhysicalComposition, observed.PhysicalComposition);
        Assert.NotSame(prior.PublicationBase, observed.PublicationBase);
        Assert.Equal(["Prior.Package"], Names(observed));
        Assert.NotNull(observed.Preparing);
        Assert.Equal(WorkspaceScopeOperationKind.Replace, observed.Preparing.Kind);
        Assert.Equal(1, observed.Preparing.RequestedRootCount);
        Assert.Equal(["Next.Package"], Names(replacement));
        Assert.Null(replacement.Preparing);
    }

    [Fact]
    public async Task OneFailedRootPublishesNoSuccessfulPrefix()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Replace(workspace, Binding("Prior.Package"));
        var failed = Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            await workspace.ReplaceScopeAsync(prior.Revision,
                [Binding("Good.Package"), Binding("Bad.Package", malformed: true)],
                Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(ArtifactRootFailure.PreparationFailed, failed.Failure);
        Assert.Same(prior.Revision, failed.Snapshot.Revision);
        Assert.Same(prior.PhysicalComposition, failed.Snapshot.PhysicalComposition);
        Assert.Equal(["Prior.Package"], Names(failed.Snapshot));
        Assert.Null(failed.Snapshot.Preparing);
        Assert.Same(failed.Snapshot, await Current(workspace));
        using var lease = ArtifactAvailable(await workspace.ReadArtifactRootCompositionAsync(workspace.Identity));
        Assert.Single(lease.Roots);
    }

    [Fact]
    public async Task ExactDuplicatesCoalesceBeforePreparationAndRetainedOccurrencesFollowRequestOrder()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        int duplicateReads = 0;
        WorkspaceScopeSnapshot first = await Replace(workspace,
            Binding("First.Package"),
            Binding("Second.Package"),
            Binding("first.package", onOpen: () => duplicateReads++));
        Assert.Equal(0, duplicateReads);
        Assert.Equal(["First.Package", "Second.Package"], Names(first));
        WorkspaceRootOccurrence a = first.Roots[0].Occurrence;
        WorkspaceRootOccurrence b = first.Roots[1].Occurrence;
        ArtifactRootGenerationReference generation = Ready(first.Roots[0]);

        WorkspaceScopeSnapshot reordered = Committed(await workspace.ReplaceScopeAsync(
            first.Revision,
            [Binding("second.package", onOpen: () => duplicateReads++),
             Binding("first.package", onOpen: () => duplicateReads++),
             Binding("Second.Package", onOpen: () => duplicateReads++)],
            Deadline, TestContext.Current.CancellationToken)).Snapshot;
        Assert.Equal(0, duplicateReads);
        Assert.Same(b, reordered.Roots[0].Occurrence);
        Assert.Same(a, reordered.Roots[1].Occurrence);
        Assert.Same(generation, Ready(reordered.Roots[1]));
        Assert.NotSame(first.Revision.Identity, reordered.Revision.Identity);
        Assert.Equal(["First.Package", "Second.Package"], Names(first));
    }

    [Fact]
    public async Task RemovedThenEqualReaddedRootGetsFreshOccurrence()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot first = await Replace(workspace, Binding("Same.Package"));
        WorkspaceScopeSnapshot empty = Committed(
            await workspace.ReplaceScopeAsync(
                first.Revision, [], Deadline, TestContext.Current.CancellationToken)).Snapshot;
        WorkspaceScopeSnapshot readded = Committed(await workspace.ReplaceScopeAsync(
            empty.Revision, [Binding("Same.Package")], Deadline, TestContext.Current.CancellationToken)).Snapshot;
        Assert.Empty(empty.Roots);
        Assert.Equal(first.Roots[0].Occurrence.Correspondence, readded.Roots[0].Occurrence.Correspondence);
        Assert.NotSame(first.Roots[0].Occurrence.Identity, readded.Roots[0].Occurrence.Identity);
        Assert.NotSame(first.Revision.Identity, readded.Revision.Identity);
    }

    [Fact]
    public async Task ClearOfEmptyScopeStillIssuesFreshRevisionAndClosure()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        var clear = Committed(await workspace.ClearScopeAsync(
            initial.Revision, Deadline, TestContext.Current.CancellationToken));
        Assert.Empty(clear.Snapshot.Roots);
        Assert.Equal(WorkspaceScopeOperationKind.Clear, clear.Effect);
        Assert.Same(workspace.Identity, clear.Snapshot.Revision.Workspace);
        Assert.NotSame(initial.Revision.Identity, clear.Snapshot.Revision.Identity);
        Assert.NotSame(initial.PublicationBase, clear.Snapshot.PublicationBase);
        Assert.NotSame(initial.Closure.Identity, clear.Snapshot.Closure.Identity);
        Assert.Same(clear.Snapshot, await Current(workspace));
    }

    [Theory]
    [InlineData("README.md", PackageCompileAssetSelectionStatus.NoCompileAssets)]
    [InlineData("ref/net11.0/_._", PackageCompileAssetSelectionStatus.EmptyCompileGroup)]
    public async Task RootOnlyAndExplicitEmptyRemainLogicalRoots(
        string entry, PackageCompileAssetSelectionStatus expected)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot current = await Replace(workspace, Binding("Empty.Package", entry: entry));
        var package = Assert.IsType<WorkspaceRootDescriptor.Package>(Assert.Single(current.Roots).Occurrence.Root);
        Assert.Equal(expected, package.SelectionStatus);
        Assert.Equal("Empty.Package", package.PackageId);
        Assert.Equal("net11.0", package.TargetFramework);
        Assert.IsType<ArtifactRootRealizationStatus.Ready>(current.Roots[0].Realization.Status);
    }

    [Fact]
    public async Task ClearSupersedesBlockedPreparationWithoutWaitingForCurrentQuery()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Replace(workspace, Binding("Prior.Package"));
        InspectionWorkspace.RootLifetime lifetime = Assert.Single(Lifetimes(workspace));
        using InspectionWorkspace.ArtifactRootQueryLease query = ArtifactAvailable(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity,
                prior.Roots[0].Occurrence.Correspondence, Ready(prior.Roots[0])));
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        PackageRootBinding slow = Binding("Slow.Package", onOpen: () =>
        {
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)));
        });
        Task<WorkspaceScopeOperationResult> pending = Task.Run(async () =>
            await workspace.ReplaceScopeAsync(
                prior.Revision, [slow], Deadline, TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        WorkspaceScopeOperationResult.Committed clear;
        try
        {
            await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.NotNull((await Current(workspace)).Preparing);
            clear = Committed(await workspace.ClearScopeAsync(
                prior.Revision, Deadline, TestContext.Current.CancellationToken));
            Assert.Empty(clear.Snapshot.Roots);
            Assert.Null(clear.Snapshot.Preparing);
            Assert.Same(workspace.Identity, clear.Snapshot.Revision.Workspace);
            Assert.False(pending.IsCompleted);
            Assert.False(lifetime.Released.Task.IsCompleted);
            using Stream image = query.Realization.SurfaceParticipants[0].Participant.Assembly.OpenRead();
            Assert.True(image.Length > 0);
        }
        finally { release.Set(); }
        var superseded = Assert.IsType<WorkspaceScopeOperationResult.Superseded>(await pending);
        Assert.Same(clear.Operation, superseded.SupersedingOperation);
        Assert.Same(clear.Snapshot, superseded.Snapshot);
        query.Dispose();
        await lifetime.Released.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ValidReplaceSupersedesPreparationAndOldCompletionCannotOverwriteIt()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        WorkspaceScopeOperationResult.Committed? replacement = null;
        PackageRootBinding old = Binding("Displaced.Package", onOpen: () =>
            replacement = Committed(workspace.ReplaceScopeAsync(initial.Revision,
                [Binding("Winning.Package", entry: "README.md")], Deadline,
                TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult()));
        var displaced = Assert.IsType<WorkspaceScopeOperationResult.Superseded>(
            await workspace.ReplaceScopeAsync(initial.Revision, [old],
                Deadline, TestContext.Current.CancellationToken));
        Assert.NotNull(replacement);
        Assert.Same(replacement.Operation, displaced.SupersedingOperation);
        Assert.Same(replacement.Snapshot, displaced.Snapshot);
        Assert.Equal(["Winning.Package"], Names(displaced.Snapshot));
        Assert.Same(displaced.Snapshot, await Current(workspace));
    }

    [Theory]
    [InlineData("action", false)]
    [InlineData("action", true)]
    [InlineData("caller", false)]
    [InlineData("caller", true)]
    [InlineData("deadline", false)]
    [InlineData("deadline", true)]
    public async Task CancellationBeforeSupersessionRetainsFirstOutcome(string cause, bool clear)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var time = new ScopeTimeProvider();
        workspace.ConfigureArtifactRootAdmission(new(), time);
        WorkspaceScopeSnapshot initial = await Current(workspace);
        using var cancellation = new CancellationTokenSource();
        WorkspaceScopeOperationResult.Committed? replacement = null;
        Task<WorkspaceScopeOperationResult>? cancellationResult = null;
        WorkspaceScopePublicationOperationIdentity? operation = null;
        PackageRootBinding old = Binding("Cancelled.Package", onOpen: () =>
        {
            WorkspaceScopeSnapshot preparing = Current(workspace).GetAwaiter().GetResult();
            operation = Assert.IsType<WorkspaceScopePreparationDescriptor>(preparing.Preparing).Operation;
            switch (cause)
            {
                case "action":
                    cancellationResult = workspace.CancelScopePreparationAsync(
                        preparing.Preparing.Cancellation).AsTask();
                    break;
                case "caller":
                    cancellation.Cancel();
                    break;
                case "deadline":
                    time.Advance(TimeSpan.FromMinutes(10));
                    break;
            }
            DateTimeOffset deadline = time.GetUtcNow().AddMinutes(5);
            replacement = Committed((clear
                ? workspace.ClearScopeAsync(initial.Revision, deadline, TestContext.Current.CancellationToken)
                : workspace.ReplaceScopeAsync(initial.Revision,
                    [Binding("Winning.Package", entry: "README.md")], deadline,
                    TestContext.Current.CancellationToken)).AsTask().GetAwaiter().GetResult());
        });
        var cancelled = Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            await workspace.ReplaceScopeAsync(initial.Revision, [old],
                time.GetUtcNow().AddMinutes(5), cancellation.Token));
        Assert.NotNull(replacement);
        Assert.Same(operation, cancelled.Operation);
        Assert.Same(replacement.Snapshot, cancelled.Snapshot);
        Assert.Same(cancelled.Snapshot, await Current(workspace));
        Assert.Equal(clear ? [] : new[] { "Winning.Package" }, Names(cancelled.Snapshot));
        if (cancellationResult is not null)
            Assert.Same(cancelled, await cancellationResult);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersessionBeforeCancellationRetainsFirstOutcome(bool clear)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var time = new ScopeTimeProvider();
        workspace.ConfigureArtifactRootAdmission(new(), time);
        WorkspaceScopeSnapshot initial = await Current(workspace);
        using var cancellation = new CancellationTokenSource();
        WorkspaceScopeOperationResult.Committed? replacement = null;
        PackageRootBinding old = Binding("Superseded.Package", onOpen: () =>
        {
            WorkspaceScopeCancellationAction action = Assert.IsType<WorkspaceScopePreparationDescriptor>(
                Current(workspace).GetAwaiter().GetResult().Preparing).Cancellation;
            DateTimeOffset deadline = time.GetUtcNow().AddMinutes(5);
            replacement = Committed((clear
                ? workspace.ClearScopeAsync(initial.Revision, deadline, TestContext.Current.CancellationToken)
                : workspace.ReplaceScopeAsync(initial.Revision,
                    [Binding("Winning.Package", entry: "README.md")], deadline,
                    TestContext.Current.CancellationToken)).AsTask().GetAwaiter().GetResult());
            cancellation.Cancel();
            time.Advance(TimeSpan.FromMinutes(10));
            var noEffect = Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
                workspace.CancelScopePreparationAsync(action).AsTask().GetAwaiter().GetResult());
            Assert.Same(replacement.Snapshot, noEffect.Snapshot);
        });
        var superseded = Assert.IsType<WorkspaceScopeOperationResult.Superseded>(
            await workspace.ReplaceScopeAsync(initial.Revision, [old],
                time.GetUtcNow().AddMinutes(5), cancellation.Token));
        Assert.NotNull(replacement);
        Assert.Same(replacement.Operation, superseded.SupersedingOperation);
        Assert.Same(replacement.Snapshot, superseded.Snapshot);
        Assert.Same(superseded.Snapshot, await Current(workspace));
        Assert.Equal(clear ? [] : new[] { "Winning.Package" }, Names(superseded.Snapshot));
    }

    [Theory]
    [InlineData("stale", WorkspaceScopeRejection.RevisionMismatch)]
    [InlineData("foreign", WorkspaceScopeRejection.ForeignWorkspace)]
    [InlineData("malformed", WorkspaceScopeRejection.Malformed)]
    [InlineData("deadline", WorkspaceScopeRejection.DeadlineExpired)]
    [InlineData("capacity", WorkspaceScopeRejection.RootCapacityExceeded)]
    public async Task InvalidSubmissionsDoNotSupersedeAdmittedPreparation(
        string invalidKind, WorkspaceScopeRejection reason)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        await using InspectionWorkspace foreign = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        WorkspaceScopeSnapshot prior = await Replace(workspace, Binding("Prior.Package"));
        WorkspaceScopeSnapshot foreignSnapshot = await Current(foreign);
        WorkspaceScopePreparationDescriptor? preparing = null;
        WorkspaceScopeOperationResult.Rejected? rejection = null;
        PackageRootBinding next = Binding("Next.Package", onOpen: () =>
        {
            preparing = Current(workspace).GetAwaiter().GetResult().Preparing;
            WorkspaceScopeRevision revision = invalidKind switch
            {
                "stale" => initial.Revision,
                "foreign" => foreignSnapshot.Revision,
                _ => prior.Revision,
            };
            ImmutableArray<PackageRootBinding> roots = invalidKind switch
            {
                "malformed" => default,
                "capacity" => [.. Enumerable.Range(0, 65).Select(i => Binding($"Root.{i}", entry: "README.md"))],
                _ => [],
            };
            DateTimeOffset deadline = invalidKind == "deadline"
                ? DateTimeOffset.UtcNow.AddMinutes(-1) : Deadline;
            rejection = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
                workspace.ReplaceScopeAsync(revision, roots, deadline, TestContext.Current.CancellationToken)
                    .AsTask().GetAwaiter().GetResult());
            Assert.Same(preparing, rejection.Snapshot.Preparing);
            Assert.Same(prior.Revision, rejection.Snapshot.Revision);
        });
        WorkspaceScopeSnapshot committed = Committed(
            await workspace.ReplaceScopeAsync(
                prior.Revision, [next], Deadline, TestContext.Current.CancellationToken)).Snapshot;
        Assert.NotNull(rejection);
        Assert.Equal(reason, rejection.Reason);
        Assert.NotNull(preparing);
        Assert.Equal(["Next.Package"], Names(committed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeCommitPreservesPriorRevision(bool beforeAdmission)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Replace(workspace, Binding("Prior.Package"));
        using var cancellation = new CancellationTokenSource();
        if (beforeAdmission) cancellation.Cancel();
        PackageRootBinding next = Binding("Next.Package",
            onOpen: beforeAdmission ? null : cancellation.Cancel);
        var cancelled = Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            await workspace.ReplaceScopeAsync(prior.Revision, [next], Deadline, cancellation.Token));
        Assert.Same(prior.Revision, cancelled.Snapshot.Revision);
        Assert.Null(cancelled.Snapshot.Preparing);
        Assert.Equal(["Prior.Package"], Names(cancelled.Snapshot));
        Assert.Same(cancelled.Snapshot, await Current(workspace));
    }

    [Fact]
    public async Task CancellationAfterCommitCannotRetractPublication()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        using var cancellation = new CancellationTokenSource();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        var committed = Committed(await workspace.ReplaceScopeAsync(
            initial.Revision, [Binding("Committed.Package")], Deadline, cancellation.Token));
        cancellation.Cancel();
        Assert.Same(committed.Snapshot, await Current(workspace));
        Assert.Equal(["Committed.Package"], Names(committed.Snapshot));
    }

    [Fact]
    public async Task ExactCancellationActionSettlesTheOriginalOperationAndCannotCancelAnother()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        await using InspectionWorkspace foreign = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        WorkspaceScopeCancellationAction? action = null;
        Task<WorkspaceScopeOperationResult>? cancellation = null;
        PackageRootBinding package = Binding("Cancelled.Package", onOpen: () =>
        {
            WorkspaceScopeSnapshot preparing = Current(workspace).GetAwaiter().GetResult();
            action = Assert.IsType<WorkspaceScopePreparationDescriptor>(preparing.Preparing).Cancellation;
            Assert.Same(workspace.Identity, action.Workspace);
            Assert.Same(preparing.Preparing.Operation, action.Operation);
            var rejected = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
                foreign.CancelScopePreparationAsync(action).AsTask().GetAwaiter().GetResult());
            Assert.Equal(WorkspaceScopeRejection.ForeignWorkspace, rejected.Reason);
            cancellation = workspace.CancelScopePreparationAsync(action).AsTask();
            WorkspaceScopeSnapshot cancelling = Current(workspace).GetAwaiter().GetResult();
            Assert.NotSame(preparing.PublicationBase, cancelling.PublicationBase);
            Assert.Same(preparing.Revision, cancelling.Revision);
            Assert.Same(preparing.Preparing, cancelling.Preparing);
        });
        var cancelled = Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            await workspace.ReplaceScopeAsync(initial.Revision, [package],
                Deadline, TestContext.Current.CancellationToken));
        Assert.NotNull(cancellation);
        Assert.NotNull(action);
        Assert.Same(cancelled, await cancellation);
        Assert.Same(action.Operation, cancelled.Operation);
        Assert.Null(cancelled.Snapshot.Preparing);

        WorkspaceScopeSnapshot next = await Replace(workspace, Binding("Next.Package", onOpen: () =>
        {
            var noEffect = Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
                workspace.CancelScopePreparationAsync(action).AsTask().GetAwaiter().GetResult());
            Assert.NotNull(noEffect.Snapshot.Preparing);
            Assert.NotSame(action.Operation, noEffect.Snapshot.Preparing.Operation);
        }));
        Assert.Equal(["Next.Package"], Names(next));
    }

    [Fact]
    public async Task DeadlineExpiryAfterAdmissionCancelsRatherThanRejects()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var time = new ScopeTimeProvider();
        workspace.ConfigureArtifactRootAdmission(new(), time);
        WorkspaceScopeSnapshot initial = await Current(workspace);
        var cancelled = Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            await workspace.ReplaceScopeAsync(initial.Revision,
                [Binding("Deadline.Package", onOpen: () => time.Advance(TimeSpan.FromMinutes(10)))],
                time.GetUtcNow().AddMinutes(5), TestContext.Current.CancellationToken));
        Assert.Same(initial.Revision, cancelled.Snapshot.Revision);
        Assert.Empty(cancelled.Snapshot.Roots);
        Assert.Null(cancelled.Snapshot.Preparing);
        Assert.Same(cancelled.Snapshot, await Current(workspace));
    }

    [Fact]
    public async Task ObservationRefreshPreservesReadyPendingFailedAndDoesNotPublishArtifactComposition()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot ready = await Replace(workspace, Binding("A.Package"), Binding("B.Package"));
        WorkspaceRootOccurrenceDescriptor a = ready.Roots[0];
        ArtifactRootCompositionGenerationIdentity pendingEpoch = ArtifactAvailable(
            await workspace.RetireArtifactRootAsync(a.Occurrence.Correspondence, Ready(a)));
        WorkspaceScopeSnapshot pending = await Current(workspace);
        AssertRefresh(ready, pending, pendingEpoch);
        Assert.IsType<ArtifactRootRealizationStatus.Pending>(pending.Roots[0].Realization.Status);
        Assert.Same(ready.Roots[1].Realization, pending.Roots[1].Realization);
        Assert.Same(pendingEpoch, ArtifactAvailable(
            await workspace.GetCurrentArtifactRootCompositionGenerationAsync(workspace.Identity)));

        ArtifactRootCompositionGenerationIdentity failedEpoch = ArtifactAvailable(
            await workspace.FailArtifactRootReplacementAsync(a.Occurrence.Correspondence,
                pendingEpoch, ArtifactRootFailure.PreparationFailed));
        WorkspaceScopeSnapshot failed = await Current(workspace);
        AssertRefresh(pending, failed, failedEpoch);
        Assert.Equal(ArtifactRootFailure.PreparationFailed,
            Assert.IsType<ArtifactRootRealizationStatus.Failed>(failed.Roots[0].Realization.Status).Failure);
        Assert.Same(failedEpoch, ArtifactAvailable(
            await workspace.GetCurrentArtifactRootCompositionGenerationAsync(workspace.Identity)));

        var authority = new ArtifactRootPreparationAuthority(
            workspace.Identity, new(), Deadline, TestContext.Current.CancellationToken);
        ArtifactRootPreparationReceipt receipt = ArtifactAvailable(
            await workspace.PreparePackageArtifactRootsAsync(authority, [Binding("A.Package")]));
        ArtifactRootReplacementSettlement settlement = ArtifactAvailable(
            await workspace.SettleArtifactRootReplacementAsync(authority, receipt, failedEpoch));
        WorkspaceScopeSnapshot replaced = await Current(workspace);
        AssertRefresh(failed, replaced, settlement.Composition);
        Assert.NotSame(Ready(ready.Roots[0]), Ready(replaced.Roots[0]));
        Assert.Same(replaced, await Current(workspace));
    }

    [Fact]
    public async Task RefreshDuringPreparationPreservesPreparingAndStalePhysicalCandidateCannotRebase()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Replace(workspace, Binding("A.Package"));
        WorkspaceScopeSnapshot? refreshed = null;
        WorkspaceScopePreparationDescriptor? preparing = null;
        PackageRootBinding next = Binding("B.Package", onOpen: () =>
        {
            preparing = Current(workspace).GetAwaiter().GetResult().Preparing;
            ArtifactAvailable(workspace.RetireArtifactRootAsync(initial.Roots[0].Occurrence.Correspondence,
                Ready(initial.Roots[0])).AsTask().GetAwaiter().GetResult());
            refreshed = Current(workspace).GetAwaiter().GetResult();
        });
        var failed = Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            await workspace.ReplaceScopeAsync(
                initial.Revision, [next], Deadline, TestContext.Current.CancellationToken));
        Assert.NotNull(refreshed);
        Assert.NotNull(preparing);
        Assert.Same(preparing, refreshed.Preparing);
        Assert.Same(initial.Revision, refreshed.Revision);
        Assert.IsType<ArtifactRootRealizationStatus.Pending>(refreshed.Roots[0].Realization.Status);
        Assert.Equal(ArtifactRootFailure.CompositionMismatch, failed.Failure);
        Assert.Same(initial.Revision, failed.Snapshot.Revision);
        Assert.Same(refreshed.PhysicalComposition, failed.Snapshot.PhysicalComposition);
        Assert.Null(failed.Snapshot.Preparing);
        Assert.Same(failed.Snapshot, await Current(workspace));
    }

    [Fact]
    public async Task ExplicitReplaceCanPrepareANonReadyCorrespondingRootWithoutChangingItsOccurrence()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot first = await Replace(workspace, Binding("Same.Package"));
        WorkspaceRootOccurrenceDescriptor row = first.Roots[0];
        ArtifactAvailable(await workspace.RetireArtifactRootAsync(row.Occurrence.Correspondence, Ready(row)));
        WorkspaceScopeSnapshot pending = await Current(workspace);
        var replaced = Committed(await workspace.ReplaceScopeAsync(
            pending.Revision, [Binding("Same.Package")], Deadline, TestContext.Current.CancellationToken));
        Assert.Same(row.Occurrence, replaced.Snapshot.Roots[0].Occurrence);
        Assert.NotSame(Ready(row), Ready(replaced.Snapshot.Roots[0]));
        Assert.NotSame(first.Revision.Identity, replaced.Snapshot.Revision.Identity);
        Assert.Same(replaced.Snapshot, await Current(workspace));
    }

    [Fact]
    public async Task ClosingWorkspaceReportsUnavailableWhileAnAdmittedQueryDrains()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Replace(workspace, Binding("Prior.Package"));
        using InspectionWorkspace.ArtifactRootQueryLease query = ArtifactAvailable(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity,
                prior.Roots[0].Occurrence.Correspondence, Ready(prior.Roots[0])));
        Task<InspectionWorkspaceCloseReport> close = workspace.CloseAsync();
        try
        {
            Assert.False(close.IsCompleted);
            var read = Assert.IsType<WorkspaceScopeReadResult.Unavailable>(await workspace.GetScopeSnapshotAsync());
            Assert.Equal(ArtifactRootFailure.WorkspaceClosing, read.RuntimeFailure);
            Assert.Same(prior, read.LastSnapshot);
            var unavailable = Assert.IsType<WorkspaceScopeOperationResult.Unavailable>(
                await workspace.ClearScopeAsync(null!, DateTimeOffset.MinValue, TestContext.Current.CancellationToken));
            Assert.Equal(ArtifactRootFailure.WorkspaceClosing, unavailable.RuntimeFailure);
            Assert.Same(prior, unavailable.LastSnapshot);
        }
        finally { query.Dispose(); }
        await close;
    }

    [Fact]
    public async Task CloseDuringPreparationSettlesUnavailableAndReleasesOperationAuthority()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        Task<InspectionWorkspaceCloseReport>? close = null;
        var unavailable = Assert.IsType<WorkspaceScopeOperationResult.Unavailable>(
            await workspace.ReplaceScopeAsync(initial.Revision,
                [Binding("Closing.Package", onOpen: () => close = workspace.CloseAsync())],
                Deadline, TestContext.Current.CancellationToken));
        Assert.NotNull(close);
        Assert.NotNull(unavailable.LastSnapshot);
        Assert.Same(initial.Revision, unavailable.LastSnapshot.Revision);
        Assert.True(unavailable.RuntimeFailure is ArtifactRootFailure.WorkspaceClosing or ArtifactRootFailure.WorkspaceClosed);
        await close;
        Assert.Null(typeof(InspectionWorkspace).GetField("_scopePreparation", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(workspace));
    }

    [Fact]
    public async Task RuntimeUnavailablePrecedesInvalidSubmissionAndDoesNotInventCurrentState()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Replace(workspace, Binding("Prior.Package"));
        await workspace.DisposeAsync();
        var read = Assert.IsType<WorkspaceScopeReadResult.Unavailable>(await workspace.GetScopeSnapshotAsync());
        Assert.Same(prior, read.LastSnapshot);
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, read.RuntimeFailure);
        var unavailable = Assert.IsType<WorkspaceScopeOperationResult.Unavailable>(
            await workspace.ReplaceScopeAsync(
                null!, default, DateTimeOffset.MinValue, TestContext.Current.CancellationToken));
        Assert.Same(prior, unavailable.LastSnapshot);
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, unavailable.RuntimeFailure);
    }

    [Fact]
    public async Task HistoricalSnapshotsAndResultsDoNotRetainRetiredResources()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        (WorkspaceScopeSnapshot historical, WorkspaceScopeOperationResult result,
            ImmutableArray<WeakReference> references) = await WeakHistory(workspace);
        for (int attempt = 0; attempt < 10 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Yield();
        }
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        Assert.Equal(["History.Package"], Names(historical));
        Assert.NotNull(historical.Preparing);
        GC.KeepAlive(historical);
        GC.KeepAlive(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<(WorkspaceScopeSnapshot, WorkspaceScopeOperationResult, ImmutableArray<WeakReference>)>
        WeakHistory(InspectionWorkspace workspace)
    {
        PackageRootBinding binding = Binding("History.Package");
        WorkspaceScopeSnapshot initial = await Current(workspace);
        WorkspaceScopeOperationResult.Committed published = Committed(
            await workspace.ReplaceScopeAsync(
                initial.Revision, [binding], Deadline, TestContext.Current.CancellationToken));
        InspectionWorkspace.RootLifetime lifetime = Assert.Single(Lifetimes(workspace));
        WorkspaceScopeSnapshot? preparing = null;
        PackageRootBinding invalid = Binding("Failed.History", malformed: true, onOpen: () =>
            preparing = Current(workspace).GetAwaiter().GetResult());
        Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            await workspace.ReplaceScopeAsync(published.Snapshot.Revision, [invalid],
                Deadline, TestContext.Current.CancellationToken));
        Assert.NotNull(preparing);
        ImmutableArray<WeakReference> references =
            [new(binding), new(binding.Root.Content), new(lifetime),
             new(lifetime.Resources), new(lifetime.Resources.Realization), new(lifetime.Resources.Session!),
             new(invalid), new(invalid.Root.Content)];
        Committed(await workspace.ClearScopeAsync(
            published.Snapshot.Revision, Deadline, TestContext.Current.CancellationToken));
        await lifetime.Released.Task.WaitAsync(TestContext.Current.CancellationToken);
        return (preparing, published, references);
    }

    static IEnumerable<InspectionWorkspace.RootLifetime> Lifetimes(InspectionWorkspace workspace) =>
        Assert.IsAssignableFrom<IEnumerable<InspectionWorkspace.RootLifetime>>(
            typeof(InspectionWorkspace).GetField("_rootLifetimes", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(workspace));

    static void AssertRefresh(
        WorkspaceScopeSnapshot previous,
        WorkspaceScopeSnapshot current,
        ArtifactRootCompositionGenerationIdentity epoch)
    {
        Assert.Same(previous.Revision, current.Revision);
        Assert.Same(epoch, current.PhysicalComposition);
        Assert.NotSame(previous.PublicationBase, current.PublicationBase);
        Assert.NotSame(previous.Closure.Identity, current.Closure.Identity);
        Assert.Equal(previous.Roots.Select(row => row.Occurrence), current.Roots.Select(row => row.Occurrence));
        Assert.Equal(WorkspaceClosureState.ClosedBoundary, current.Closure.State);
    }

    static string[] Names(WorkspaceScopeSnapshot snapshot) =>
        [.. snapshot.Roots.Select(row => Assert.IsType<WorkspaceRootDescriptor.Package>(row.Occurrence.Root).PackageId)];

    static ArtifactRootGenerationReference Ready(WorkspaceRootOccurrenceDescriptor row) =>
        Assert.IsType<ArtifactRootRealizationStatus.Ready>(row.Realization.Status).Generation;

    static T ArtifactAvailable<T>(ArtifactRootResult<T> result) =>
        Assert.IsType<ArtifactRootResult<T>.Available>(result).Value;

    static WorkspaceScopeOperationResult.Committed Committed(WorkspaceScopeOperationResult result) =>
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(result);

    static async Task<WorkspaceScopeSnapshot> Current(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(await workspace.GetScopeSnapshotAsync()).Snapshot;

    static async Task<WorkspaceScopeSnapshot> Replace(
        InspectionWorkspace workspace, params PackageRootBinding[] bindings) =>
        Committed(await workspace.ReplaceScopeAsync((await Current(workspace)).Revision,
            [.. bindings], Deadline, TestContext.Current.CancellationToken)).Snapshot;

    static PackageRootBinding Binding(
        string packageId, bool malformed = false, string? entry = null, Action? onOpen = null)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            bool emptyCompileGroup = entry == "ref/net11.0/_._";
            using (Stream destination = archive.CreateEntry(entry ?? $"lib/net11.0/{packageId}.dll").Open())
            {
                if (!emptyCompileGroup)
                    destination.Write(malformed ? [1, 2, 3] :
                        File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location));
            }
            if (emptyCompileGroup)
            {
                using Stream library = archive.CreateEntry($"lib/net11.0/{packageId}.dll").Open();
                library.Write(File.ReadAllBytes(typeof(AssemblyReferenceIdentity).Assembly.Location));
            }
        }
        IPackageContent content = new InMemoryPackageContent(bytes.ToArray(), fromCache: false, producerKey: "tests");
        if (onOpen is not null)
            content = new CallbackPackageContent(content, onOpen);
        return PackageRootBinding.CreateFromSource(new AcquiredPackageSourcePayload(
            PackageSourceCoordinate.Create(packageId, "1.0.0"), content, "tests", PackagePayloadOrigin.Download),
            "net11.0", displayPackageId: packageId);
    }

    sealed class CallbackPackageContent(IPackageContent inner, Action onOpen) : IPackageContent
    {
        Action? _onOpen = onOpen;
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public bool RequiresArchiveTreeMatch => inner.RequiresArchiveTreeMatch;
        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream) => inner.TryOpenArchive(out stream);
        public bool TryOpenEntry(string relativePath, [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(relativePath, out stream);
        }
        public bool TryOpenEntry(string relativePath, long maxExpandedBytes, [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(relativePath, maxExpandedBytes, out stream);
        }
        public IEnumerable<string> EnumerateEntries() => inner.EnumerateEntries();
    }

    sealed class ScopeTimeProvider : TimeProvider
    {
        DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan duration) => _now += duration;
    }
}
