using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceScopeTests
{
    [Fact]
    public async Task IssuedScopeRequest_HasNoEffectsBeforeSubmission()
    {
        await using InspectionWorkspace workspace = new();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        int reads = 0;
        PackageRootBinding abandonedBinding =
            Binding("Abandoned.Package", onOpen: () => reads++);
        WorkspaceScopePackageTarget abandonedTarget =
            workspace.CreateScopePackageTarget(abandonedBinding);
        WorkspaceScopeRequest abandoned = workspace.IssueAddPackagesRequest(
            initial.Revision,
            [abandonedBinding],
            Deadline,
            abandonedTarget);

        Assert.Same(workspace.Identity, abandoned.Association.Workspace);
        Assert.Same(initial.Revision.Identity, abandoned.Association.ExpectedRevision);
        Assert.Same(workspace.Identity, abandoned.Association.ExpectedRevisionWorkspace);
        Assert.Equal(WorkspaceScopeOperationKind.Add, abandoned.Association.Kind);
        Assert.True(abandoned.Association.HasExplicitTarget);
        Assert.Same(abandonedTarget, abandoned.Target);
        Assert.Same(
            abandonedTarget.Correspondence,
            Assert.IsType<WorkspaceScopePackageTarget>(abandoned.Target)
                .Correspondence);
        Assert.Equal(WorkspaceScopeLimits.DefaultMaxPackages, abandoned.Limits.MaxPackages);
        Assert.Same(initial, await Current(workspace));
        Assert.Same(initial.PhysicalComposition, ArtifactAvailable(
            await workspace.GetCurrentArtifactRootCompositionGenerationAsync(workspace.Identity)));
        Assert.Null(initial.Preparing);
        Assert.Equal(0, reads);

        WorkspaceScopeRequest stale = workspace.IssueAddPackagesRequest(
            initial.Revision,
            [Binding("Stale.Package")],
            Deadline);
        WorkspaceScopeSnapshot changed = await Add(workspace, Binding("Current.Package"));
        var staleResult = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
            await workspace.SubmitScopeRequestAsync(stale, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceScopeRejection.RevisionMismatch, staleResult.Reason);
        AssertAssociation(stale, staleResult);

        WorkspaceScopeRequest guarded = workspace.IssueAddPackagesRequest(
            changed.Revision,
            changed.PublicationBase,
            [Binding("Guarded.Package")],
            Deadline);
        var failed = Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            await workspace.ReplaceScopeAsync(
                changed.Revision,
                [Binding("Failed.Guard", malformed: true)],
                Deadline,
                TestContext.Current.CancellationToken));
        Assert.Same(changed.Revision, failed.Snapshot.Revision);
        Assert.NotSame(changed.PublicationBase, failed.Snapshot.PublicationBase);
        var guardResult = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
            await workspace.SubmitScopeRequestAsync(guarded, TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceScopeRejection.PublicationBaseMismatch, guardResult.Reason);
        AssertAssociation(guarded, guardResult);

        await using InspectionWorkspace deadlineWorkspace = new();
        var time = new ScopeTimeProvider();
        deadlineWorkspace.ConfigureArtifactRootAdmission(new(), time);
        WorkspaceScopeSnapshot deadlineInitial = await Current(deadlineWorkspace);
        WorkspaceScopeRequest expired = deadlineWorkspace.IssueAddPackagesRequest(
            deadlineInitial.Revision,
            [Binding("Expired.Package")],
            time.GetUtcNow().AddMinutes(5));
        time.Advance(TimeSpan.FromMinutes(10));
        var deadlineResult = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
            await deadlineWorkspace.SubmitScopeRequestAsync(
                expired,
                TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceScopeRejection.DeadlineExpired, deadlineResult.Reason);
        AssertAssociation(expired, deadlineResult);

        await using InspectionWorkspace closing = new();
        WorkspaceScopeRequest unavailableRequest = closing.IssueAddPackagesRequest(
            expectedRevision: null,
            packages: default,
            deadline: DateTimeOffset.MinValue);
        await closing.DisposeAsync();
        var unavailable = Assert.IsType<WorkspaceScopeOperationResult.Unavailable>(
            await closing.SubmitScopeRequestAsync(
                unavailableRequest,
                TestContext.Current.CancellationToken));
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, unavailable.RuntimeFailure);
        Assert.Null(unavailableRequest.Association.ExpectedRevision);
        Assert.Null(unavailableRequest.Association.ExpectedRevisionWorkspace);
        AssertAssociation(unavailableRequest, unavailable);
    }

    [Fact]
    public async Task ScopeSettlement_PreservesOriginalOperationAssociation()
    {
        await using InspectionWorkspace workspace = new();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        WorkspaceScopeRequest committedRequest = workspace.IssueAddPackagesRequest(
            initial.Revision,
            [Binding("Committed.Package")],
            Deadline);
        var committed = Committed(await workspace.SubmitScopeRequestAsync(
            committedRequest,
            TestContext.Current.CancellationToken));
        AssertAssociation(committedRequest, committed);
        Assert.Null(committed.RequestedOccurrence);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await workspace.SubmitScopeRequestAsync(
                committedRequest,
                TestContext.Current.CancellationToken));

        WorkspaceScopeRequest noEffectRequest = workspace.IssueAddPackagesRequest(
            committed.Snapshot.Revision,
            [Binding("Committed.Package")],
            Deadline);
        var noEffect = Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
            await workspace.SubmitScopeRequestAsync(
                noEffectRequest,
                TestContext.Current.CancellationToken));
        AssertAssociation(noEffectRequest, noEffect);

        WorkspaceScopeRequest missingRevision = workspace.IssueAddPackagesRequest(
            expectedRevision: null,
            packages: default,
            deadline: Deadline);
        var malformed = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
            await workspace.SubmitScopeRequestAsync(
                missingRevision,
                TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceScopeRejection.Malformed, malformed.Reason);
        Assert.Null(malformed.Association.ExpectedRevision);
        Assert.Null(malformed.Association.ExpectedRevisionWorkspace);
        AssertAssociation(missingRevision, malformed);

        await using InspectionWorkspace foreign = new();
        WorkspaceScopeSnapshot foreignInitial = await Current(foreign);
        WorkspaceScopeRequest foreignRevision = workspace.IssueAddPackagesRequest(
            foreignInitial.Revision,
            [Binding("Foreign.Package")],
            Deadline);
        var foreignRejected = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
            await workspace.SubmitScopeRequestAsync(
                foreignRevision,
                TestContext.Current.CancellationToken));
        Assert.Equal(WorkspaceScopeRejection.ForeignWorkspace, foreignRejected.Reason);
        Assert.Same(foreign.Identity, foreignRejected.Association.ExpectedRevisionWorkspace);
        Assert.Same(foreignInitial.Revision.Identity, foreignRejected.Association.ExpectedRevision);
        AssertAssociation(foreignRevision, foreignRejected);

        WorkspaceScopeRequest failedRequest = workspace.IssueReplaceScopeRequest(
            committed.Snapshot.Revision,
            [Binding("Failed.Package", malformed: true)],
            Deadline);
        var failed = Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            await workspace.SubmitScopeRequestAsync(
                failedRequest,
                TestContext.Current.CancellationToken));
        AssertAssociation(failedRequest, failed);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        WorkspaceScopeRequest cancelledRequest = workspace.IssueAddPackagesRequest(
            committed.Snapshot.Revision,
            [Binding("Cancelled.Package")],
            Deadline);
        var cancelled = Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            await workspace.SubmitScopeRequestAsync(
                cancelledRequest,
                cancellation.Token));
        AssertAssociation(cancelledRequest, cancelled);

        await using InspectionWorkspace supersessionWorkspace = new();
        WorkspaceScopeSnapshot supersessionInitial = await Current(supersessionWorkspace);
        WorkspaceScopeOperationResult.Committed? winner = null;
        WorkspaceScopeRequest displacedRequest =
            supersessionWorkspace.IssueReplaceScopeRequest(
                supersessionInitial.Revision,
                [Binding("Displaced.Package", onOpen: () =>
                {
                    WorkspaceScopeRequest winnerRequest =
                        supersessionWorkspace.IssueClearScopeRequest(
                            supersessionInitial.Revision,
                            Deadline);
                    winner = Committed(
                        supersessionWorkspace.SubmitScopeRequestAsync(
                            winnerRequest,
                            TestContext.Current.CancellationToken)
                        .AsTask().GetAwaiter().GetResult());
                    AssertAssociation(winnerRequest, winner);
                })],
                Deadline);
        var superseded = Assert.IsType<WorkspaceScopeOperationResult.Superseded>(
            await supersessionWorkspace.SubmitScopeRequestAsync(
                displacedRequest,
                TestContext.Current.CancellationToken));
        Assert.NotNull(winner);
        AssertAssociation(displacedRequest, superseded);
        Assert.Same(winner.Operation, superseded.SupersedingOperation);
        Assert.NotSame(superseded.Operation, superseded.SupersedingOperation);

        await using InspectionWorkspace unavailableWorkspace = new();
        WorkspaceScopeSnapshot unavailableInitial = await Current(unavailableWorkspace);
        WorkspaceScopeRequest unavailableRequest =
            unavailableWorkspace.IssueClearScopeRequest(
                unavailableInitial.Revision,
                Deadline);
        await unavailableWorkspace.DisposeAsync();
        var unavailable = Assert.IsType<WorkspaceScopeOperationResult.Unavailable>(
            await unavailableWorkspace.SubmitScopeRequestAsync(
                unavailableRequest,
                TestContext.Current.CancellationToken));
        AssertAssociation(unavailableRequest, unavailable);
    }

    [Fact]
    public async Task ExplicitScopeTarget_ReturnsExactResultOccurrence()
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding avaloniaBefore =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding avaloniaAfter =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using (InspectionWorkspace workspace = new())
        {
            WorkspaceScopeSnapshot before =
                await Replace(workspace, avaloniaBefore);
            WorkspaceScopePackageTarget target =
                workspace.CreateScopePackageTarget(avaloniaAfter);
            WorkspaceScopeRequest request = workspace.IssueReplaceScopeRequest(
                before.Revision,
                [avaloniaAfter],
                Deadline,
                target);
            var replaced = Committed(await workspace.SubmitScopeRequestAsync(
                request,
                TestContext.Current.CancellationToken));

            WorkspacePackageOccurrenceDescriptor requested =
                Assert.IsType<WorkspacePackageOccurrenceDescriptor>(
                    replaced.RequestedOccurrence);
            Assert.Same(request.Association, replaced.Association);
            Assert.Equal(target.Correspondence, requested.Occurrence.Correspondence);
            Assert.Equal(
                "Avalonia",
                requested.Occurrence.Package.PackageId,
                ignoreCase: true);
            Assert.Equal("12.1.2", requested.Occurrence.Package.PackageVersion);
            Assert.Equal("net8.0", requested.Occurrence.Package.TargetFramework);
            Assert.Same(requested, Assert.Single(replaced.Snapshot.Packages));
        }

        PackageRootBinding json =
            await packages.BindingAsync("System.Text.Json", "10.0.0");
        await using (InspectionWorkspace workspace = new())
        {
            WorkspaceScopeSnapshot current = await Add(workspace, json);
            WorkspaceScopePackageTarget target =
                workspace.CreateScopePackageTarget(json);
            WorkspaceScopeRequest explicitDuplicate =
                workspace.IssueAddPackagesRequest(
                    current.Revision,
                    [json],
                    Deadline,
                    target);
            var explicitNoEffect =
                Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
                    await workspace.SubmitScopeRequestAsync(
                        explicitDuplicate,
                        TestContext.Current.CancellationToken));
            Assert.Same(current.Packages[0], explicitNoEffect.RequestedOccurrence);
            Assert.Same(current, explicitNoEffect.Snapshot);

            WorkspaceScopeRequest implicitDuplicate =
                workspace.IssueAddPackagesRequest(
                    current.Revision,
                    [json],
                    Deadline);
            var implicitNoEffect =
                Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
                    await workspace.SubmitScopeRequestAsync(
                        implicitDuplicate,
                        TestContext.Current.CancellationToken));
            Assert.Null(implicitNoEffect.RequestedOccurrence);
        }

        await AssertMixedTarget(existingTarget: true);
        await AssertMixedTarget(existingTarget: false);

        await using (InspectionWorkspace workspace = new())
        {
            PackageRootBinding pendingBinding = Binding("Pending.Package");
            WorkspaceScopeSnapshot ready = await Add(workspace, pendingBinding);
            ArtifactRootCompositionGenerationIdentity epoch = ArtifactAvailable(
                await workspace.RetireArtifactRootAsync(
                    ready.Packages[0].Occurrence.Correspondence,
                    Ready(ready.Packages[0])));
            WorkspaceScopeSnapshot pending = await Current(workspace);
            WorkspaceScopePackageTarget target =
                workspace.CreateScopePackageTarget(pendingBinding);
            var duplicate = Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
                await workspace.SubmitScopeRequestAsync(
                    workspace.IssueAddPackagesRequest(
                        pending.Revision,
                        [pendingBinding],
                        Deadline,
                        target),
                    TestContext.Current.CancellationToken));
            Assert.Same(pending.Packages[0], duplicate.RequestedOccurrence);
            Assert.IsType<ArtifactRootRealizationStatus.Pending>(
                duplicate.RequestedOccurrence!.Realization.Status);
            Assert.Same(epoch, duplicate.Snapshot.PhysicalComposition);
        }

        await using (InspectionWorkspace workspace = new())
        {
            WorkspaceScopeSnapshot initial = await Current(workspace);
            WorkspaceScopeOperationResult.Rejected? invalidReplace = null;
            WorkspaceScopeOperationResult.Rejected? invalidAdd = null;
            PackageRootBinding active = Binding("Active.Package", onOpen: () =>
            {
                WorkspaceScopeSnapshot preparing =
                    Current(workspace).GetAwaiter().GetResult();
                WorkspaceScopePackageTarget unrelated =
                    workspace.CreateScopePackageTarget(
                        Binding("Unrelated.Package"));
                invalidReplace = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
                    workspace.SubmitScopeRequestAsync(
                        workspace.IssueReplaceScopeRequest(
                            initial.Revision,
                            [Binding("Replacement.Package")],
                            Deadline,
                            unrelated),
                        TestContext.Current.CancellationToken)
                    .AsTask().GetAwaiter().GetResult());
                invalidAdd = Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
                    workspace.SubmitScopeRequestAsync(
                        workspace.IssueAddPackagesRequest(
                            initial.Revision,
                            [Binding("Addition.Package")],
                            Deadline,
                            unrelated),
                        TestContext.Current.CancellationToken)
                    .AsTask().GetAwaiter().GetResult());
                Assert.Equal(WorkspaceScopeRejection.Malformed, invalidReplace.Reason);
                Assert.Equal(WorkspaceScopeRejection.Malformed, invalidAdd.Reason);
                Assert.Same(preparing.Preparing, invalidReplace.Snapshot.Preparing);
                Assert.Same(preparing.Preparing, invalidAdd.Snapshot.Preparing);
            });
            WorkspaceScopeRequest noIntent = workspace.IssueReplaceScopeRequest(
                initial.Revision,
                [active],
                Deadline);
            var committed = Committed(await workspace.SubmitScopeRequestAsync(
                noIntent,
                TestContext.Current.CancellationToken));
            Assert.Null(committed.RequestedOccurrence);
            Assert.NotNull(invalidReplace);
            Assert.NotNull(invalidAdd);
        }

        async Task AssertMixedTarget(bool existingTarget)
        {
            await using InspectionWorkspace workspace = new();
            PackageRootBinding existing = Binding("Existing.Package");
            PackageRootBinding added = Binding("Added.Package");
            WorkspaceScopeSnapshot current = await Add(workspace, existing);
            PackageRootBinding selected = existingTarget ? existing : added;
            WorkspaceScopeRequest request = workspace.IssueAddPackagesRequest(
                current.Revision,
                [existing, added],
                Deadline,
                workspace.CreateScopePackageTarget(selected));
            var committed = Committed(await workspace.SubmitScopeRequestAsync(
                request,
                TestContext.Current.CancellationToken));
            WorkspacePackageOccurrenceDescriptor requested =
                Assert.IsType<WorkspacePackageOccurrenceDescriptor>(
                    committed.RequestedOccurrence);
            Assert.Equal(
                existingTarget ? "Existing.Package" : "Added.Package",
                requested.Occurrence.Package.PackageId);
            if (existingTarget)
                Assert.Same(
                    current.Packages[0].Occurrence,
                    requested.Occurrence);
            else
                Assert.Same(committed.Snapshot.Packages[1], requested);
        }
    }

    [Fact]
    public async Task ScopeCancellationControl_DistinguishesObservationFromSettlement()
    {
        await using InspectionWorkspace workspace = new();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        Task<WorkspaceScopeCancellationResult>? control = null;
        WorkspaceScopeCancellationAction? action = null;
        WorkspaceScopeRequest request = workspace.IssueReplaceScopeRequest(
            initial.Revision,
            [Binding("Cancelled.Package", onOpen: () =>
            {
                WorkspaceScopePreparationDescriptor preparing =
                    Assert.IsType<WorkspaceScopePreparationDescriptor>(
                        Current(workspace).GetAwaiter().GetResult().Preparing);
                action = preparing.Cancellation;
                control = workspace.CancelScopePreparationAsync(action).AsTask();
            })],
            Deadline);
        var cancelled = Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            await workspace.SubmitScopeRequestAsync(
                request,
                TestContext.Current.CancellationToken));
        Assert.NotNull(control);
        Assert.NotNull(action);
        var settled = Assert.IsType<WorkspaceScopeCancellationResult.Settled>(
            await control);
        Assert.Same(cancelled, settled.Settlement);
        AssertAssociation(request, settled.Settlement);

        WorkspaceScopeCancellationAction? committedAction = null;
        WorkspaceScopeRequest committedRequest = workspace.IssueReplaceScopeRequest(
            cancelled.Snapshot.Revision,
            [Binding("Committed.Package", onOpen: () =>
                committedAction = Assert.IsType<WorkspaceScopePreparationDescriptor>(
                    Current(workspace).GetAwaiter().GetResult().Preparing)
                    .Cancellation)],
            Deadline);
        var committed = Committed(await workspace.SubmitScopeRequestAsync(
            committedRequest,
            TestContext.Current.CancellationToken));
        Assert.NotNull(committedAction);
        var observation =
            Assert.IsType<WorkspaceScopeCancellationResult.ObservedNoEffect>(
                await workspace.CancelScopePreparationAsync(committedAction));
        Assert.Same(committed.Snapshot, observation.Snapshot);

        await using InspectionWorkspace foreign = new();
        var rejected = Assert.IsType<WorkspaceScopeCancellationResult.Rejected>(
            await foreign.CancelScopePreparationAsync(committedAction));
        Assert.Equal(WorkspaceScopeRejection.ForeignWorkspace, rejected.Reason);

        await workspace.DisposeAsync();
        var unavailable = Assert.IsType<WorkspaceScopeCancellationResult.Unavailable>(
            await workspace.CancelScopePreparationAsync(committedAction));
        Assert.Equal(ArtifactRootFailure.WorkspaceClosed, unavailable.RuntimeFailure);
        Assert.Same(committed.Snapshot, unavailable.LastSnapshot);
    }

    [Fact]
    public async Task RetainedScopeOperationEvidence_ErasesTransientInputs()
    {
        (WorkspaceScopeOperationAssociation association,
            WorkspaceScopeOperationResult result,
            ImmutableArray<WeakReference> references) =
            await WeakOperationEvidence();
        for (int attempt = 0;
            attempt < 10 && references.Any(reference => reference.IsAlive);
            attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Yield();
        }

        Assert.All(references, reference => Assert.False(reference.IsAlive));
        Assert.Same(association, result.Association);
        Assert.Same(association.Operation, result.Operation);
        GC.KeepAlive(association);
        GC.KeepAlive(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<(WorkspaceScopeOperationAssociation,
        WorkspaceScopeOperationResult,
        ImmutableArray<WeakReference>)> WeakOperationEvidence()
    {
        InspectionWorkspace workspace = new();
        WorkspaceScopeSnapshot initial = await Current(workspace);
        object callbackState = new();
        PackageRootBinding binding = Binding(
            "Operation.History",
            onOpen: () => GC.KeepAlive(callbackState));
        WorkspaceScopePackageTarget target =
            workspace.CreateScopePackageTarget(binding);
        WorkspaceScopeRequest request = workspace.IssueReplaceScopeRequest(
            initial.Revision,
            [binding],
            Deadline,
            target);
        WorkspaceScopeOperationAssociation association = request.Association;
        Task<WorkspaceScopeOperationResult> execution =
            workspace.SubmitScopeRequestAsync(
                request,
                TestContext.Current.CancellationToken).AsTask();
        WorkspaceScopeOperationResult.Committed published =
            Committed(await execution);
        InspectionWorkspace.RootLifetime lifetime =
            Assert.Single(Lifetimes(workspace));
        ImmutableArray<WeakReference> references =
        [
            new(workspace),
            new(request),
            new(target),
            new(binding),
            new(binding.Root.Content),
            new(callbackState),
            new(execution),
            new(lifetime),
            new(lifetime.Resources),
            new(lifetime.Resources.Realization),
            new(lifetime.Resources.Session!),
        ];
        Committed(await workspace.ClearScopeAsync(
            published.Snapshot.Revision,
            Deadline,
            TestContext.Current.CancellationToken));
        await lifetime.Released.Task.WaitAsync(TestContext.Current.CancellationToken);
        await workspace.DisposeAsync();
        return (association, published, references);
    }

    static void AssertAssociation(
        WorkspaceScopeRequest request,
        WorkspaceScopeOperationResult result)
    {
        Assert.Same(request.Association, result.Association);
        Assert.Same(request.Association.Operation, result.Operation);
        Assert.Same(request.Association.Workspace, result.Association.Workspace);
        Assert.Equal(request.Association.Kind, result.Association.Kind);
        Assert.Same(
            request.Association.ExpectedRevision,
            result.Association.ExpectedRevision);
        Assert.Same(
            request.Association.ExpectedRevisionWorkspace,
            result.Association.ExpectedRevisionWorkspace);
        Assert.Equal(
            request.Association.HasExplicitTarget,
            result.Association.HasExplicitTarget);
    }
}
