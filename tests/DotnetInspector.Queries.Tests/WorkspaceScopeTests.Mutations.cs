using System.Collections.Immutable;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceScopeTests
{
    [Fact]
    public async Task AddPreservesExistingOrderAndAppendsOneDistinctBatch()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Add(workspace, Binding("First.Package"), Binding("Second.Package"));
        int duplicateReads = 0;
        WorkspaceScopeSnapshot? preparing = null;
        var added = Committed(await workspace.AddRootsAsync(initial.Revision,
            [Binding("second.package", onOpen: () => duplicateReads++),
             Binding("Third.Package", onOpen: () => preparing = Current(workspace).GetAwaiter().GetResult()),
             Binding("third.package", onOpen: () => duplicateReads++),
             Binding("Fourth.Package"),
             Binding("first.package", onOpen: () => duplicateReads++)],
            Deadline, TestContext.Current.CancellationToken));

        Assert.Equal(WorkspaceScopeOperationKind.Add, added.Effect);
        Assert.Equal(["First.Package", "Second.Package", "Third.Package", "Fourth.Package"], Names(added.Snapshot));
        Assert.Equal(0, duplicateReads);
        Assert.NotNull(preparing);
        Assert.Same(initial.Revision, preparing.Revision);
        Assert.Same(initial.PhysicalComposition, preparing.PhysicalComposition);
        Assert.Equal(["First.Package", "Second.Package"], Names(preparing));
        Assert.NotNull(preparing.Preparing);
        Assert.Equal(WorkspaceScopeOperationKind.Add, preparing.Preparing.Kind);
        Assert.Equal(2, preparing.Preparing.RequestedRootCount);
        for (int index = 0; index < initial.Roots.Length; index++)
        {
            Assert.Same(initial.Roots[index].Occurrence, added.Snapshot.Roots[index].Occurrence);
            Assert.Same(Ready(initial.Roots[index]), Ready(added.Snapshot.Roots[index]));
        }
        Assert.NotSame(initial.Revision.Identity, added.Snapshot.Revision.Identity);
        Assert.NotSame(initial.Closure.Identity, added.Snapshot.Closure.Identity);
        Assert.Null(added.Snapshot.Preparing);
        Assert.Same(added.Snapshot, await Current(workspace));
    }

    [Theory]
    [InlineData("ready", false)]
    [InlineData("ready", true)]
    [InlineData("pending", false)]
    [InlineData("pending", true)]
    [InlineData("failed", false)]
    [InlineData("failed", true)]
    public async Task EmptyOrDuplicateOnlyAddHasNoPhysicalEffect(string status, bool empty)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot current = await Add(workspace, Binding("Same.Package"));
        if (status != "ready")
            current = await NonReady(workspace, current, 0, status == "failed");
        int reads = 0;
        ImmutableArray<PackageRootBinding> roots = empty ? [] :
            [Binding("same.package", onOpen: () => reads++), Binding("Same.Package", onOpen: () => reads++)];
        var noEffect = Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
            await workspace.AddRootsAsync(current.Revision, roots, Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(0, reads);
        Assert.Same(current, noEffect.Snapshot);
        Assert.Same(current, await Current(workspace));
        Assert.Same(current.PhysicalComposition, ArtifactAvailable(
            await workspace.GetCurrentArtifactRootCompositionGenerationAsync(workspace.Identity)));
    }

    [Fact]
    public async Task AddReducesExactCorrespondenceBeforeLogicalCapacityAndPreparation()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot initial = await Add(workspace,
            [.. Enumerable.Range(0, 63).Select(index => Binding($"Root.{index}", entry: "README.md"))]);
        int duplicateReads = 0;
        var full = Committed(await workspace.AddRootsAsync(initial.Revision,
            [Binding("Root.0", onOpen: () => duplicateReads++),
             Binding("Last.Root", entry: "README.md"),
             Binding("last.root", onOpen: () => duplicateReads++)],
            Deadline, TestContext.Current.CancellationToken)).Snapshot;
        Assert.Equal(64, full.Roots.Length);
        Assert.Equal(0, duplicateReads);
        Assert.Equal("Last.Root", Names(full)[^1]);

        int refusedReads = 0;
        var rejected = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
            await workspace.AddRootsAsync(full.Revision,
                [Binding("Last.Root", onOpen: () => refusedReads++),
                 Binding("Over.Capacity", onOpen: () => refusedReads++),
                 Binding("over.capacity", onOpen: () => refusedReads++)],
                Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceScopeRejection.RootCapacityExceeded, rejected.Reason);
        Assert.Equal(0, refusedReads);
        Assert.Same(full, rejected.Snapshot);
    }

    [Fact]
    public async Task LaterAddFailureDoesNotPublishSuccessfulPrefix()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Add(workspace, Binding("Prior.Package"));
        var failure = Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            await workspace.AddRootsAsync(prior.Revision,
                [Binding("prior.package"), Binding("Good.Package"), Binding("Bad.Package", malformed: true)],
                Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(ArtifactRootFailure.PreparationFailed, failure.Failure);
        Assert.Same(prior.Revision, failure.Snapshot.Revision);
        Assert.Same(prior.PhysicalComposition, failure.Snapshot.PhysicalComposition);
        Assert.Equal(["Prior.Package"], Names(failure.Snapshot));
        Assert.Null(failure.Snapshot.Preparing);
        Assert.Same(failure.Snapshot, await Current(workspace));
        using var lease = ArtifactAvailable(await workspace.ReadArtifactRootCompositionAsync(workspace.Identity));
        Assert.Single(lease.Roots);
    }

    [Fact]
    public async Task RemoveRetainsOtherOccurrencesAndDoesNotWaitForAnAdmittedQuery()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Add(workspace,
            Binding("First.Package"), Binding("Removed.Package"), Binding("Last.Package"));
        WorkspaceRootOccurrenceDescriptor target = prior.Roots[1];
        InspectionWorkspace.RootLifetime lifetime = Lifetimes(workspace)
            .Single(root => root.Projection.Correspondence.Equals(target.Occurrence.Correspondence));
        using InspectionWorkspace.ArtifactRootQueryLease query = ArtifactAvailable(
            await workspace.EnterArtifactRootQueryAsync(workspace.Identity, target.Occurrence.Correspondence, Ready(target)));

        var removed = Committed(await workspace.RemoveRootOccurrenceAsync(
            prior.Revision, target.Occurrence.Identity, Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceScopeOperationKind.Remove, removed.Effect);
        Assert.Equal(["First.Package", "Last.Package"], Names(removed.Snapshot));
        Assert.Same(prior.Roots[0].Occurrence, removed.Snapshot.Roots[0].Occurrence);
        Assert.Same(prior.Roots[2].Occurrence, removed.Snapshot.Roots[1].Occurrence);
        Assert.Same(Ready(prior.Roots[0]), Ready(removed.Snapshot.Roots[0]));
        Assert.Same(Ready(prior.Roots[2]), Ready(removed.Snapshot.Roots[1]));
        Assert.NotSame(prior.Revision.Identity, removed.Snapshot.Revision.Identity);
        Assert.NotSame(prior.Closure.Identity, removed.Snapshot.Closure.Identity);
        Assert.False(lifetime.Released.Task.IsCompleted);
        using (Stream image = query.Realization.SurfaceParticipants[0].Participant.Assembly.OpenRead())
            Assert.True(image.Length > 0);
        query.Dispose();
        await lifetime.Released.Task.WaitAsync(TestContext.Current.CancellationToken);

        WorkspaceScopeSnapshot readded = await Add(workspace, Binding("Removed.Package"));
        Assert.Equal(target.Occurrence.Correspondence, readded.Roots[^1].Occurrence.Correspondence);
        Assert.NotSame(target.Occurrence.Identity, readded.Roots[^1].Occurrence.Identity);
        var retired = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
            await workspace.RemoveRootOccurrenceAsync(readded.Revision, target.Occurrence.Identity,
                Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceScopeRejection.OccurrenceNotCurrent, retired.Reason);
        Assert.Same(readded, retired.Snapshot);
        Assert.Equal(["First.Package", "Removed.Package", "Last.Package"], Names(prior));
    }

    [Theory]
    [InlineData("ready")]
    [InlineData("pending")]
    [InlineData("failed")]
    public async Task RemovingTheLastOccurrenceCommitsAnEmptyClosedRevision(string status)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Add(workspace, Binding("Last.Package"));
        if (status != "ready")
            prior = await NonReady(workspace, prior, 0, status == "failed");
        var removed = Committed(await workspace.RemoveRootOccurrenceAsync(
            prior.Revision, prior.Roots[0].Occurrence.Identity, Deadline, TestContext.Current.CancellationToken));
        Assert.Empty(removed.Snapshot.Roots);
        Assert.Empty(removed.Snapshot.Revision.Roots);
        Assert.Same(workspace.Identity, removed.Snapshot.Revision.Workspace);
        Assert.Equal(WorkspaceClosureState.ClosedBoundary, removed.Snapshot.Closure.State);
        Assert.NotSame(prior.Revision.Identity, removed.Snapshot.Revision.Identity);
        Assert.Same(removed.Snapshot, await Current(workspace));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncrementalEditsRefuseNonReadySurvivorsBeforePreparingMaterial(bool failed)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot ready = await Add(workspace, Binding("NonReady.Package"), Binding("Ready.Package"));
        WorkspaceScopeSnapshot prior = await NonReady(workspace, ready, 0, failed);
        int reads = 0;
        var add = Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            await workspace.AddRootsAsync(prior.Revision,
                [Binding("NonReady.Package", onOpen: () => reads++), Binding("New.Package", onOpen: () => reads++)],
                Deadline, TestContext.Current.CancellationToken));
        var remove = Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            await workspace.RemoveRootOccurrenceAsync(prior.Revision, prior.Roots[1].Occurrence.Identity,
                Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(0, reads);
        Assert.Equal(ArtifactRootFailure.ArtifactGenerationMismatch, add.Failure);
        Assert.Equal(ArtifactRootFailure.ArtifactGenerationMismatch, remove.Failure);
        Assert.Same(prior, add.Snapshot);
        Assert.Same(prior, remove.Snapshot);
        Assert.Same(prior, await Current(workspace));

        var removed = Committed(await workspace.RemoveRootOccurrenceAsync(
            prior.Revision, prior.Roots[0].Occurrence.Identity, Deadline, TestContext.Current.CancellationToken));
        Assert.Same(prior.Roots[1].Occurrence, Assert.Single(removed.Snapshot.Roots).Occurrence);
        Assert.Same(Ready(prior.Roots[1]), Ready(removed.Snapshot.Roots[0]));
    }

    [Fact]
    public async Task OrdinaryAddAndRemoveAreBusyWithoutSupersedingPreparation()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Add(workspace, Binding("Prior.Package"));
        int busyReads = 0;
        PackageRootBinding package = Binding("Added.Package", onOpen: () =>
        {
            WorkspaceScopeSnapshot preparing = Current(workspace).GetAwaiter().GetResult();
            ImmutableArray<WorkspaceScopeOperationResult> results =
            [
                workspace.AddRootsAsync(prior.Revision, [Binding("Busy.Package", onOpen: () => busyReads++)],
                    Deadline, TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult(),
                workspace.AddRootsAsync(prior.Revision, [Binding("Prior.Package")],
                    Deadline, TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult(),
                workspace.AddRootsAsync(prior.Revision, [],
                    Deadline, TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult(),
                workspace.RemoveRootOccurrenceAsync(prior.Revision, prior.Roots[0].Occurrence.Identity,
                    Deadline, TestContext.Current.CancellationToken).AsTask().GetAwaiter().GetResult(),
            ];
            Assert.All(results, result =>
            {
                var rejected = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(result);
                Assert.Equal(WorkspaceScopeRejection.Busy, rejected.Reason);
                Assert.Same(preparing, rejected.Snapshot);
            });
        });
        WorkspaceScopeSnapshot current = await Add(workspace, package);
        Assert.Equal(0, busyReads);
        Assert.Equal(["Prior.Package", "Added.Package"], Names(current));
        Assert.Null(current.Preparing);
    }

    [Theory]
    [InlineData(false, "stale", WorkspaceScopeRejection.RevisionMismatch)]
    [InlineData(false, "foreign", WorkspaceScopeRejection.ForeignWorkspace)]
    [InlineData(false, "malformed", WorkspaceScopeRejection.Malformed)]
    [InlineData(false, "null-root", WorkspaceScopeRejection.Malformed)]
    [InlineData(false, "capacity", WorkspaceScopeRejection.RootCapacityExceeded)]
    [InlineData(false, "deadline", WorkspaceScopeRejection.DeadlineExpired)]
    [InlineData(true, "stale", WorkspaceScopeRejection.RevisionMismatch)]
    [InlineData(true, "foreign", WorkspaceScopeRejection.ForeignWorkspace)]
    [InlineData(true, "malformed", WorkspaceScopeRejection.Malformed)]
    [InlineData(true, "foreign-occurrence", WorkspaceScopeRejection.ForeignWorkspace)]
    [InlineData(true, "retired", WorkspaceScopeRejection.OccurrenceNotCurrent)]
    [InlineData(true, "deadline", WorkspaceScopeRejection.DeadlineExpired)]
    public async Task IncrementalValidationPrecedesBusy(
        bool remove, string invalid, WorkspaceScopeRejection reason)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        await using InspectionWorkspace foreign = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot old = await Add(workspace, Binding("Prior.Package"), Binding("Retired.Package"));
        WorkspaceRootOccurrenceIdentity retired = old.Roots[1].Occurrence.Identity;
        WorkspaceScopeSnapshot prior = Committed(await workspace.RemoveRootOccurrenceAsync(
            old.Revision, retired, Deadline, TestContext.Current.CancellationToken)).Snapshot;
        WorkspaceScopeSnapshot other = await Add(foreign, Binding("Foreign.Package"));
        WorkspaceScopeOperationResult.Rejected? rejection = null;
        PackageRootBinding package = Binding("Added.Package", onOpen: () =>
        {
            WorkspaceScopeSnapshot preparing = Current(workspace).GetAwaiter().GetResult();
            WorkspaceScopeRevision revision = invalid switch
            {
                "stale" => old.Revision,
                "foreign" => other.Revision,
                _ => prior.Revision,
            };
            WorkspaceRootOccurrenceIdentity target = invalid switch
            {
                "malformed" => null!,
                "foreign-occurrence" => other.Roots[0].Occurrence.Identity,
                "retired" => retired,
                _ => prior.Roots[0].Occurrence.Identity,
            };
            ImmutableArray<PackageRootBinding> roots = invalid switch
            {
                "malformed" => default,
                "null-root" => [null!],
                "capacity" => [.. Enumerable.Range(0, 64).Select(index => Binding($"Excess.{index}", entry: "README.md"))],
                _ => [],
            };
            DateTimeOffset deadline = invalid == "deadline" ? DateTimeOffset.UtcNow.AddMinutes(-1) : Deadline;
            rejection = Assert.IsType<WorkspaceScopeOperationResult.Rejected>((remove
                ? workspace.RemoveRootOccurrenceAsync(revision, target, deadline, TestContext.Current.CancellationToken)
                : workspace.AddRootsAsync(revision, roots, deadline, TestContext.Current.CancellationToken))
                .AsTask().GetAwaiter().GetResult());
            Assert.Equal(reason, rejection.Reason);
            Assert.Same(preparing, rejection.Snapshot);
        });
        WorkspaceScopeSnapshot current = await Add(workspace, package);
        Assert.NotNull(rejection);
        Assert.Equal(["Prior.Package", "Added.Package"], Names(current));
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("none", true)]
    [InlineData("caller", false)]
    [InlineData("caller", true)]
    [InlineData("deadline", false)]
    [InlineData("deadline", true)]
    [InlineData("action", false)]
    [InlineData("action", true)]
    [InlineData("late", false)]
    [InlineData("late", true)]
    public async Task AddSupersessionPreservesFirstObservedStop(string stop, bool clear)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var time = new ScopeTimeProvider();
        workspace.ConfigureArtifactRootAdmission(new(), time);
        WorkspaceScopeSnapshot initial = await Add(workspace, Binding("Prior.Package"));
        using var cancellation = new CancellationTokenSource();
        WorkspaceScopeOperationResult.Committed? winner = null;
        WorkspaceScopePreparationDescriptor? preparing = null;
        Task<WorkspaceScopeOperationResult>? cancellationResult = null;
        PackageRootBinding package = Binding("Displaced.Package", onOpen: () =>
        {
            preparing = Assert.IsType<WorkspaceScopePreparationDescriptor>(
                Current(workspace).GetAwaiter().GetResult().Preparing);
            if (stop == "caller") cancellation.Cancel();
            if (stop == "deadline") time.Advance(TimeSpan.FromMinutes(10));
            if (stop == "action")
                cancellationResult = workspace.CancelScopePreparationAsync(preparing.Cancellation).AsTask();
            DateTimeOffset deadline = time.GetUtcNow().AddMinutes(5);
            winner = Committed((clear
                ? workspace.ClearScopeAsync(initial.Revision, deadline, TestContext.Current.CancellationToken)
                : workspace.ReplaceScopeAsync(initial.Revision,
                    [Binding("Winning.Package", entry: "README.md")], deadline, TestContext.Current.CancellationToken))
                .AsTask().GetAwaiter().GetResult());
            if (stop == "late")
            {
                cancellation.Cancel();
                time.Advance(TimeSpan.FromMinutes(10));
            }
        });
        WorkspaceScopeOperationResult result = await workspace.AddRootsAsync(
            initial.Revision, [package], time.GetUtcNow().AddMinutes(5), cancellation.Token);
        Assert.NotNull(winner);
        Assert.NotNull(preparing);
        if (stop is "none" or "late")
        {
            var superseded = Assert.IsType<WorkspaceScopeOperationResult.Superseded>(result);
            Assert.Same(winner.Operation, superseded.SupersedingOperation);
            Assert.Same(winner.Snapshot, superseded.Snapshot);
        }
        else
        {
            var cancelled = Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(result);
            Assert.Same(preparing.Operation, cancelled.Operation);
            Assert.Same(winner.Snapshot, cancelled.Snapshot);
        }
        if (cancellationResult is not null)
            Assert.Same(result, await cancellationResult);
        Assert.Same(winner.Snapshot, await Current(workspace));
    }

    [Theory]
    [InlineData("before")]
    [InlineData("caller")]
    [InlineData("deadline")]
    public async Task AddCancellationOrExpiryLeavesThePriorRevisionCurrent(string cause)
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        var time = new ScopeTimeProvider();
        workspace.ConfigureArtifactRootAdmission(new(), time);
        WorkspaceScopeSnapshot prior = await Add(workspace, Binding("Prior.Package"));
        using var cancellation = new CancellationTokenSource();
        if (cause == "before") cancellation.Cancel();
        PackageRootBinding package = Binding("Cancelled.Package", onOpen: () =>
        {
            if (cause == "deadline") time.Advance(TimeSpan.FromMinutes(10));
            else cancellation.Cancel();
        });
        var cancelled = Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            await workspace.AddRootsAsync(prior.Revision, [package], time.GetUtcNow().AddMinutes(5), cancellation.Token));
        Assert.Same(prior.Revision, cancelled.Snapshot.Revision);
        Assert.Same(prior.PhysicalComposition, cancelled.Snapshot.PhysicalComposition);
        Assert.Equal(["Prior.Package"], Names(cancelled.Snapshot));
        Assert.Null(cancelled.Snapshot.Preparing);
        Assert.Same(cancelled.Snapshot, await Current(workspace));
    }

    [Fact]
    public async Task RemoveHonorsCancellationBeforeAdmissionAndCannotBeRetractedAfterCommit()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Add(workspace, Binding("Prior.Package"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var stopped = Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            await workspace.RemoveRootOccurrenceAsync(prior.Revision, prior.Roots[0].Occurrence.Identity,
                Deadline, cancelled.Token));
        Assert.Same(prior, stopped.Snapshot);
        using var after = new CancellationTokenSource();
        var removed = Committed(await workspace.RemoveRootOccurrenceAsync(
            prior.Revision, prior.Roots[0].Occurrence.Identity, Deadline, after.Token));
        after.Cancel();
        Assert.Empty(removed.Snapshot.Roots);
        Assert.Same(removed.Snapshot, await Current(workspace));
    }

    [Fact]
    public async Task PhysicalMovementDuringAddRefusesTheStaleCandidateAndRefreshesAllCurrentRoots()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Add(workspace, Binding("Prior.Package"), Binding("Other.Package"));
        PackageRootBinding package = Binding("Added.Package", onOpen: () =>
            ArtifactAvailable(workspace.RetireArtifactRootAsync(
                prior.Roots[0].Occurrence.Correspondence, Ready(prior.Roots[0])).AsTask().GetAwaiter().GetResult()));
        var failed = Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            await workspace.AddRootsAsync(prior.Revision, [package], Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(ArtifactRootFailure.CompositionMismatch, failed.Failure);
        Assert.Same(prior.Revision, failed.Snapshot.Revision);
        Assert.Equal(["Prior.Package", "Other.Package"], Names(failed.Snapshot));
        Assert.IsType<ArtifactRootRealizationStatus.Pending>(failed.Snapshot.Roots[0].Realization.Status);
        Assert.Same(prior.Roots[1].Realization, failed.Snapshot.Roots[1].Realization);
        Assert.Null(failed.Snapshot.Preparing);
        Assert.Same(failed.Snapshot, await Current(workspace));
    }

    [Fact]
    public async Task IncrementalOperationsRespectRuntimeUnavailabilityBeforeInvalidRequests()
    {
        await using InspectionWorkspace workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeSnapshot prior = await Add(workspace, Binding("Prior.Package"));
        await workspace.DisposeAsync();
        var add = Assert.IsType<WorkspaceScopeOperationResult.Unavailable>(
            await workspace.AddRootsAsync(null!, default, DateTimeOffset.MinValue, TestContext.Current.CancellationToken));
        var remove = Assert.IsType<WorkspaceScopeOperationResult.Unavailable>(
            await workspace.RemoveRootOccurrenceAsync(null!, null!, DateTimeOffset.MinValue, TestContext.Current.CancellationToken));
        Assert.Same(prior, add.LastSnapshot);
        Assert.Same(prior, remove.LastSnapshot);
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, add.RuntimeFailure);
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, remove.RuntimeFailure);
    }

    static async Task<WorkspaceScopeSnapshot> Add(
        InspectionWorkspace workspace, params PackageRootBinding[] bindings) =>
        Committed(await workspace.AddRootsAsync((await Current(workspace)).Revision,
            [.. bindings], Deadline, TestContext.Current.CancellationToken)).Snapshot;

    static async Task<WorkspaceScopeSnapshot> NonReady(
        InspectionWorkspace workspace, WorkspaceScopeSnapshot snapshot, int index, bool failed)
    {
        WorkspaceRootOccurrenceDescriptor root = snapshot.Roots[index];
        ArtifactRootCompositionGenerationIdentity epoch = ArtifactAvailable(
            await workspace.RetireArtifactRootAsync(root.Occurrence.Correspondence, Ready(root)));
        if (failed)
            ArtifactAvailable(await workspace.FailArtifactRootReplacementAsync(
                root.Occurrence.Correspondence, epoch, ArtifactRootFailure.PreparationFailed));
        return await Current(workspace);
    }
}
