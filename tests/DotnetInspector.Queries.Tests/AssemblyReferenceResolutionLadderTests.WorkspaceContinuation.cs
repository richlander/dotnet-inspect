using System.Collections.Immutable;

using DotnetInspector.ResearchQueries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyReferenceResolutionLadderTests
{
    [Fact]
    public async Task
        WorkspaceContinuationPublishesFreshContextOwnedGeneration()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                new AssemblyBindingPolicyVersion(),
                AssemblyBindingSelection.NameNotOwned());
        TestExternalRoute route = new(binding, request.Generation);
        AssemblyReferenceExternalRouteSet routeSet =
            RouteSet(request, route);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            routeSet,
            route,
            ownerEvidence: new object());

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                coordinator,
                predecessor,
                demand,
                async (workspace, _, _) =>
                    await ResolutionRequestAsync(
                        workspace,
                        new(
                            binding.Target,
                            SuccessorOrigin(assembly),
                            binding.Scope),
                        new AssemblyBindingPolicyVersion(),
                        AssemblyBindingSelection.Found(assembly),
                        request.Work),
                TestContext.Current.CancellationToken);

        using var published = Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Published>(outcome);
        Assert.Same(route, published.External.SelectedRoute);
        Assert.Same(
            published.Replacement,
            published.External.Continuation!.Replacement);
        Assert.Same(
            request.Generation,
            published.Replacement.Predecessor);
        Assert.Same(
            published.SuccessorResolution.Generation,
            published.Replacement.Successor);
        Assert.Equal(
            AssemblyReferenceResolutionRung.ReferencingContext,
            published.SuccessorResolution.Rung);
        Assert.Same(
            published.SuccessorOperation.Realization,
            coordinator.Current!.Identity);
        Assert.NotSame(
            predecessor.Realization,
            published.SuccessorOperation.Realization);
        Assert.NotNull(published.PredecessorRetirement);
        Assert.False(
            published.PredecessorRetirement.Completion.IsCompleted);
        Assert.Equal(1, request.Work.Capture().WorkspaceReplacements);

        predecessor.Dispose();
        WorkspaceRealizationSettlement settlement =
            await published.PredecessorRetirement.Completion;
        Assert.Equal(
            WorkspaceRealizationRetirementReason.Replaced,
            settlement.Reason);
        Assert.True(settlement.Succeeded);
    }

    [Fact]
    public async Task WorkspaceContinuationChargesBeforeCandidateWork()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var work = new AssemblyReferenceResolutionWorkLedger(
            new(
                maxPackageRouteOccurrences: 1,
                maxPackageCandidateOperations: 1,
                maxSourceOperations: 1,
                maxAcquisitions: 1,
                maxRealizedAssemblies: 1,
                maxTransferBytes: 1,
                maxRetainedAssemblyBytes: 1,
                maxWorkspaceReplacements: 0,
                deadline: DateTimeOffset.UtcNow.AddMinutes(5)));
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                new AssemblyBindingPolicyVersion(),
                AssemblyBindingSelection.NameNotOwned(),
                work);
        TestExternalRoute route = new(binding, request.Generation);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            RouteSet(request, route),
            route,
            ownerEvidence: new object());
        var constructorCalled = false;

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                coordinator,
                predecessor,
                demand,
                (_, _, _) =>
                {
                    constructorCalled = true;
                    throw new InvalidOperationException();
                },
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Incomplete>(
                outcome);
        Assert.False(constructorCalled);
        Assert.Same(predecessor.Realization, coordinator.Current!.Identity);
        var exhaustion = Assert.IsType<
            AssemblyReferenceResolutionWorkExhaustion>(incomplete.Evidence);
        Assert.Equal(
            AssemblyReferenceResolutionWorkKind.WorkspaceReplacement,
            exhaustion.Kind);
    }

    [Fact]
    public async Task
        WorkspaceContinuationProjectsConstructionWorkExhaustion()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var work = new AssemblyReferenceResolutionWorkLedger(
            new(
                maxPackageRouteOccurrences: 1,
                maxPackageCandidateOperations: 1,
                maxSourceOperations: 0,
                maxAcquisitions: 1,
                maxRealizedAssemblies: 1,
                maxTransferBytes: 1,
                maxRetainedAssemblyBytes: 1,
                maxWorkspaceReplacements: 1,
                deadline: DateTimeOffset.UtcNow.AddMinutes(5)));
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                new AssemblyBindingPolicyVersion(),
                AssemblyBindingSelection.NameNotOwned(),
                work);
        TestExternalRoute route = new(binding, request.Generation);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            RouteSet(request, route),
            route,
            ownerEvidence: new object());

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                coordinator,
                predecessor,
                demand,
                (_, _, _) =>
                {
                    work.Charge(
                        AssemblyReferenceResolutionWorkKind.SourceOperation,
                        amount: 1);
                    throw new InvalidOperationException(
                        "Exhausted work must stop successor construction.");
                },
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Incomplete>(
                outcome);
        var exhaustion = Assert.IsType<
            AssemblyReferenceResolutionWorkExhaustion>(
                incomplete.Evidence);
        Assert.Equal(
            AssemblyReferenceResolutionWorkKind.SourceOperation,
            exhaustion.Kind);
        Assert.NotNull(incomplete.Cleanup.Candidate);
        Assert.True(incomplete.Cleanup.Succeeded);
        Assert.Null(incomplete.Cleanup.Publication);
        Assert.Same(predecessor.Realization, coordinator.Current!.Identity);
    }

    [Fact]
    public async Task
        WorkspaceContinuationCancelsConstructionAtWorkDeadline()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var time = new WorkspaceContinuationTimeProvider();
        var work = new AssemblyReferenceResolutionWorkLedger(
            new(
                maxPackageRouteOccurrences: 1,
                maxPackageCandidateOperations: 1,
                maxSourceOperations: 1,
                maxAcquisitions: 1,
                maxRealizedAssemblies: 1,
                maxTransferBytes: 1,
                maxRetainedAssemblyBytes: 1,
                maxWorkspaceReplacements: 1,
                deadline: time.GetUtcNow().AddMinutes(1)),
            time);
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                new AssemblyBindingPolicyVersion(),
                AssemblyBindingSelection.NameNotOwned(),
                work);
        TestExternalRoute route = new(binding, request.Generation);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            RouteSet(request, route),
            route,
            ownerEvidence: new object());
        var constructorEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var constructorCancellationObserved = false;

        Task<AssemblyReferenceWorkspaceContinuationOutcome> operation =
            AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                    coordinator,
                    predecessor,
                    demand,
                    async (_, _, token) =>
                    {
                        constructorEntered.SetResult();
                        try
                        {
                            await Task.Delay(
                                Timeout.InfiniteTimeSpan,
                                token);
                        }
                        finally
                        {
                            constructorCancellationObserved =
                                token.IsCancellationRequested;
                        }
                        throw new InvalidOperationException();
                    },
                    TestContext.Current.CancellationToken)
                .AsTask();
        await constructorEntered.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromMinutes(2));

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await operation.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        var incomplete = Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Incomplete>(
                outcome);
        Assert.True(constructorCancellationObserved);
        Assert.NotNull(incomplete.Cleanup.Candidate);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateCancelled,
            incomplete.Cleanup.Candidate.Reason);
        Assert.True(incomplete.Cleanup.Succeeded);
        Assert.Null(incomplete.Cleanup.Publication);
        Assert.Same(predecessor.Realization, coordinator.Current!.Identity);
        var exhaustion = Assert.IsType<
            AssemblyReferenceResolutionWorkExhaustion>(incomplete.Evidence);
        Assert.Equal(
            AssemblyReferenceResolutionWorkKind.Deadline,
            exhaustion.Kind);
        Assert.Same(exhaustion, work.Capture().Exhaustion);
    }

    [Fact]
    public async Task
        WorkspaceContinuationPreservesCallerCancellationDuringConstruction()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                new AssemblyBindingPolicyVersion(),
                AssemblyBindingSelection.NameNotOwned());
        TestExternalRoute route = new(binding, request.Generation);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            RouteSet(request, route),
            route,
            ownerEvidence: new object());
        var constructorEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

        Task<AssemblyReferenceWorkspaceContinuationOutcome> operation =
            AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                    coordinator,
                    predecessor,
                    demand,
                    async (_, _, token) =>
                    {
                        constructorEntered.SetResult();
                        await Task.Delay(
                            Timeout.InfiniteTimeSpan,
                            token);
                        throw new InvalidOperationException();
                    },
                    cancellation.Token)
                .AsTask();
        await constructorEntered.Task.WaitAsync(
            TestContext.Current.CancellationToken);

        cancellation.Cancel();

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await operation.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        var cancelled = Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Cancelled>(
                outcome);
        Assert.NotNull(cancelled.Cleanup.Candidate);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateCancelled,
            cancelled.Cleanup.Candidate.Reason);
        Assert.True(cancelled.Cleanup.Succeeded);
        Assert.Null(cancelled.Cleanup.Publication);
        Assert.Same(predecessor.Realization, coordinator.Current!.Identity);
        Assert.Null(request.Work.Capture().Exhaustion);
    }

    [Fact]
    public async Task
        PublishedSuccessorAdoptionCompletesAfterCallerCancellation()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        var start = Assert.IsType<
            WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(
                    predecessor.Definition.Plan,
                    TestContext.Current.CancellationToken));
        _ = Assert.IsType<
            WorkspaceRealizationCandidateCompletionResult.Ready>(
                await coordinator.CompleteCandidateAsync(
                    start.Candidate,
                    TestContext.Current.CancellationToken));
        WorkspaceRealizationCutoverResult.Activated activated =
            Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
                coordinator.CutOver(
                    start.Candidate,
                    predecessor.Definition));
        var continuation =
            new AssemblyReferenceWorkspaceContinuationOutcome.Cancelled(
                new(
                    candidate: null,
                    new(
                        activated.Realization,
                        activated.Predecessor)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.EnterOperationAsync(cancellation.Token).AsTask());
        var disposed = false;

        using WorkspaceRealizationOperationLease successor =
            Assert.IsType<WorkspaceRealizationOperationLease>(
                await PackageDependencyMemberCallGraphContinuation
                    .AdoptPublishedSuccessorOperationAsync(
                        coordinator,
                        continuation,
                        () =>
                        {
                            disposed = true;
                            return ValueTask.CompletedTask;
                        }));

        Assert.Same(activated.Realization.Identity, successor.Realization);
        Assert.False(disposed);
        predecessor.Dispose();
        Assert.True(
            (await activated.Predecessor!.Completion).Succeeded);
    }

    [Fact]
    public async Task
        PublishedSuccessorAdoptionFailureDisposesUnownedGeneration()
    {
        await using var publicationCoordinator =
            new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease publicationPredecessor =
            await ActivateAsync(publicationCoordinator);
        var start = Assert.IsType<
            WorkspaceRealizationCandidateStartResult.Prepared>(
                await publicationCoordinator.BeginCandidateAsync(
                    publicationPredecessor.Definition.Plan,
                    TestContext.Current.CancellationToken));
        _ = Assert.IsType<
            WorkspaceRealizationCandidateCompletionResult.Ready>(
                await publicationCoordinator.CompleteCandidateAsync(
                    start.Candidate,
                    TestContext.Current.CancellationToken));
        WorkspaceRealizationCutoverResult.Activated activated =
            Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
                publicationCoordinator.CutOver(
                    start.Candidate,
                    publicationPredecessor.Definition));
        var continuation =
            new AssemblyReferenceWorkspaceContinuationOutcome.Cancelled(
                new(
                    candidate: null,
                    new(
                        activated.Realization,
                        activated.Predecessor)));
        await using var foreignCoordinator =
            new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease foreign =
            await ActivateAsync(foreignCoordinator);
        var disposalCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                PackageDependencyMemberCallGraphContinuation
                    .AdoptPublishedSuccessorOperationAsync(
                        foreignCoordinator,
                        continuation,
                        () =>
                        {
                            disposalCount++;
                            return ValueTask.CompletedTask;
                        })
                    .AsTask());

        Assert.Equal(1, disposalCount);
        publicationPredecessor.Dispose();
        Assert.True(
            (await activated.Predecessor!.Completion).Succeeded);
    }

    [Fact]
    public async Task
        WorkspaceContinuationRejectsForeignSelectedRouteAtDemandFormation()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                new AssemblyBindingPolicyVersion(),
                AssemblyBindingSelection.NameNotOwned());
        TestExternalRoute route = new(binding, request.Generation);
        TestExternalRoute foreign = new(binding, request.Generation);

        Assert.Throws<ArgumentException>(
            () => new AssemblyReferenceWorkspaceContinuationDemand(
                request,
                RouteSet(request, route),
                foreign,
                ownerEvidence: new object()));
    }

    [Fact]
    public async Task
        WorkspaceContinuationRejectsChangedPredecessorAtCutover()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                new AssemblyBindingPolicyVersion(),
                AssemblyBindingSelection.NameNotOwned());
        TestExternalRoute route = new(binding, request.Generation);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            RouteSet(request, route),
            route,
            ownerEvidence: new object());

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                coordinator,
                predecessor,
                demand,
                async (workspace, _, _) =>
                {
                    _ = predecessor.Workspace.ReplaceRegistrations(
                        predecessor.Definition.Registrations,
                        [
                            new WorkspaceRegistration.PackagePrefix(
                                new("Changed.")),
                        ]);
                    return await ResolutionRequestAsync(
                        workspace,
                        new(
                            binding.Target,
                            SuccessorOrigin(assembly),
                            binding.Scope),
                        new AssemblyBindingPolicyVersion(),
                        AssemblyBindingSelection.Found(assembly),
                        request.Work);
                },
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Rejected>(outcome);
        Assert.Equal(
            AssemblyReferenceWorkspaceContinuationRejectionReason
                .CandidateCutoverRejected,
            rejected.Reason);
        Assert.Equal(
            WorkspaceRealizationCandidateRejection.PredecessorChanged,
            rejected.CandidateRejection);
        Assert.Same(predecessor.Realization, coordinator.Current!.Identity);
    }

    [Fact]
    public async Task
        WorkspaceContinuationRejectsStaleSuccessorAndCleansCandidate()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var policy = new AssemblyBindingPolicyVersion();
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                policy,
                AssemblyBindingSelection.NameNotOwned());
        TestExternalRoute route = new(binding, request.Generation);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            RouteSet(request, route),
            route,
            ownerEvidence: new object());

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                coordinator,
                predecessor,
                demand,
                async (workspace, _, _) =>
                    await ResolutionRequestAsync(
                        workspace,
                        new(
                            binding.Target,
                            SuccessorOrigin(assembly),
                            binding.Scope),
                        policy,
                        AssemblyBindingSelection.Found(assembly),
                        request.Work),
                TestContext.Current.CancellationToken);

        var rejected = Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Rejected>(outcome);
        Assert.Equal(
            AssemblyReferenceWorkspaceContinuationRejectionReason
                .SuccessorEvidenceMismatch,
            rejected.Reason);
        Assert.NotNull(rejected.Cleanup.Candidate);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateAbandoned,
            rejected.Cleanup.Candidate.Reason);
        Assert.True(rejected.Cleanup.Succeeded);
        Assert.Same(predecessor.Realization, coordinator.Current!.Identity);
    }

    [Fact]
    public async Task
        WorkspaceContinuationReleasesConstructedSuccessorBeforeRetirement()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var policy = new AssemblyBindingPolicyVersion();
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                policy,
                AssemblyBindingSelection.NameNotOwned());
        TestExternalRoute route = new(binding, request.Generation);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            RouteSet(request, route),
            route,
            ownerEvidence: new object());
        var released = false;

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                coordinator,
                predecessor,
                demand,
                async (workspace, _, _) =>
                    await ResolutionRequestAsync(
                        workspace,
                        new(
                            binding.Target,
                            SuccessorOrigin(assembly),
                            binding.Scope),
                        policy,
                        AssemblyBindingSelection.Found(assembly),
                        request.Work),
                () =>
                {
                    released = true;
                    return ValueTask.CompletedTask;
                },
                TestContext.Current.CancellationToken);

        Assert.True(released);
        Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Rejected>(outcome);
        Assert.Same(predecessor.Realization, coordinator.Current!.Identity);
    }

    [Fact]
    public async Task
        WorkspaceContinuationRetiresCandidateWhenSuccessorReleaseFails()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        var policy = new AssemblyBindingPolicyVersion();
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                policy,
                AssemblyBindingSelection.NameNotOwned());
        TestExternalRoute route = new(binding, request.Generation);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            RouteSet(request, route),
            route,
            ownerEvidence: new object());
        var releaseFailure = new InvalidOperationException(
            "constructed successor release failed");

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                coordinator,
                predecessor,
                demand,
                async (workspace, _, _) =>
                    await ResolutionRequestAsync(
                        workspace,
                        new(
                            binding.Target,
                            SuccessorOrigin(assembly),
                            binding.Scope),
                        policy,
                        AssemblyBindingSelection.Found(assembly),
                        request.Work),
                () => ValueTask.FromException(releaseFailure),
                TestContext.Current.CancellationToken);

        var failed = Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Failed>(outcome);
        Assert.Same(releaseFailure, failed.Failure);
        Assert.NotNull(failed.Cleanup.Candidate);
        Assert.True(failed.Cleanup.Candidate.Succeeded);
        Assert.Same(predecessor.Realization, coordinator.Current!.Identity);
    }

    [Fact]
    public async Task
        WorkspaceContinuationReportsIncompleteAfterPublishedCutover()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        using WorkspaceRealizationOperationLease predecessor =
            await ActivateAsync(coordinator);
        ResolvedAssemblyReference assembly = TestAssembly();
        AssemblyBindingRequest binding = ReferenceRequest(assembly);
        AssemblyReferenceResolutionRequest request =
            ResolutionRequest(
                predecessor,
                binding,
                new AssemblyBindingPolicyVersion(),
                AssemblyBindingSelection.NameNotOwned());
        TestExternalRoute route = new(binding, request.Generation);
        var demand = new AssemblyReferenceWorkspaceContinuationDemand(
            request,
            RouteSet(request, route),
            route,
            ownerEvidence: new object());

        AssemblyReferenceWorkspaceContinuationOutcome outcome =
            await AssemblyReferenceWorkspaceContinuationOperation.ExecuteAsync(
                coordinator,
                predecessor,
                demand,
                async (workspace, _, _) =>
                    await IncompleteResolutionRequestAsync(
                        workspace,
                        new(
                            binding.Target,
                            SuccessorOrigin(assembly),
                            binding.Scope),
                        request.Work),
                TestContext.Current.CancellationToken);

        var incomplete = Assert.IsType<
            AssemblyReferenceWorkspaceContinuationOutcome.Incomplete>(
                outcome);
        var successor = Assert.IsType<
            AssemblyReferenceResolutionOutcome.Incomplete>(
                incomplete.Evidence);
        Assert.Equal(
            AssemblyReferenceResolutionRung.ExternalSupplier,
            successor.Rung);
        Assert.NotNull(incomplete.Cleanup.Publication);
        Assert.Same(
            coordinator.Current,
            incomplete.Cleanup.Publication.Successor);
        Assert.NotSame(
            predecessor.Realization,
            incomplete.Cleanup.Publication.Successor.Identity);
        Assert.NotNull(
            incomplete.Cleanup.Publication.PredecessorRetirement);
        Assert.NotNull(incomplete.External);
        Assert.Same(
            incomplete.Cleanup.Publication.Successor.Identity,
            incomplete.External.Continuation!.SuccessorGeneration.Workspace);
        Assert.Same(route, incomplete.External.RouteSet.Routes.Single());
    }

    static async Task<WorkspaceRealizationOperationLease> ActivateAsync(
        WorkspaceReplacementCoordinator coordinator)
    {
        var start = Assert.IsType<
            WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(
                    WorkspacePlan.Empty,
                    TestContext.Current.CancellationToken));
        _ = Assert.IsType<
            WorkspaceRealizationCandidateCompletionResult.Ready>(
                await coordinator.CompleteCandidateAsync(
                    start.Candidate,
                    TestContext.Current.CancellationToken));
        _ = Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
            coordinator.CutOver(start.Candidate));
        return Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
            await coordinator.EnterOperationAsync(
                TestContext.Current.CancellationToken)).Lease;
    }

    static AssemblyReferenceResolutionRequest ResolutionRequest(
        WorkspaceRealizationOperationLease operation,
        AssemblyBindingRequest binding,
        AssemblyBindingPolicyVersion policy,
        AssemblyBindingSelection selection,
        AssemblyReferenceResolutionWorkLedger? work = null) =>
        ResolutionRequest(
            binding,
            policy,
            selection,
            work,
            operation.Scope,
            operation.Definition.Registrations);

    static async ValueTask<AssemblyReferenceResolutionRequest>
        ResolutionRequestAsync(
        InspectionWorkspace workspace,
        AssemblyBindingRequest binding,
        AssemblyBindingPolicyVersion policy,
        AssemblyBindingSelection selection,
        AssemblyReferenceResolutionWorkLedger? work = null)
    {
        WorkspaceScopeReadResult.Available scope = Assert.IsType<
            WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync());
        WorkspaceRegistrationReadResult.Available registrations =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                workspace.GetRegistrationSnapshot());
        return ResolutionRequest(
            binding,
            policy,
            selection,
            work,
            scope.Snapshot,
            registrations.Revision);
    }

    static async ValueTask<AssemblyReferenceResolutionRequest>
        IncompleteResolutionRequestAsync(
        InspectionWorkspace workspace,
        AssemblyBindingRequest binding,
        AssemblyReferenceResolutionWorkLedger work)
    {
        WorkspaceScopeReadResult.Available scope = Assert.IsType<
            WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync());
        WorkspaceRegistrationReadResult.Available registrations =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                workspace.GetRegistrationSnapshot());
        MemberCallGraphFocalScopeReceipt focalScope =
            MemberCallGraphFocalScopeReceipt.CaptureEverything(
                scope.Snapshot,
                registrations.Revision);
        AssemblyReferenceResolutionGenerationReceipt generation =
            AssemblyReferenceResolutionGenerationReceipt.Capture(
                scope.Snapshot,
                focalScope);
        var policy = new AssemblyBindingPolicyVersion();
        return new(
            new(
                binding,
                generation,
                focalScope,
                policy,
                (_, _) => ValueTask.FromResult<
                    AssemblyReferenceResolutionContextOutcome>(
                    new AssemblyReferenceResolutionContextOutcome.Selected(
                        binding,
                        generation,
                        new(
                            policy,
                            AssemblyBindingSelection.NameNotOwned()))),
                (_, _, _) => ValueTask.FromResult<
                    AssemblyReferenceExternalRouteSetFormationOutcome>(
                    new
                        AssemblyReferenceExternalRouteSetFormationOutcome
                        .Incomplete(new object()))),
            work);
    }

    static AssemblyReferenceResolutionRequest ResolutionRequest(
        AssemblyBindingRequest binding,
        AssemblyBindingPolicyVersion policy,
        AssemblyBindingSelection selection,
        AssemblyReferenceResolutionWorkLedger? work,
        WorkspaceScopeSnapshot scope,
        WorkspaceRegistrationRevision registrations)
    {
        MemberCallGraphFocalScopeReceipt focalScope =
            MemberCallGraphFocalScopeReceipt.CaptureEverything(
                scope,
                registrations);
        AssemblyReferenceResolutionGenerationReceipt generation =
            AssemblyReferenceResolutionGenerationReceipt.Capture(
                scope,
                focalScope);
        return new(
            new(
                binding,
                generation,
                focalScope,
                policy,
                (_, _) => ValueTask.FromResult<
                    AssemblyReferenceResolutionContextOutcome>(
                    new AssemblyReferenceResolutionContextOutcome.Selected(
                        binding,
                        generation,
                        new(policy, selection))),
                (_, _, _) => throw new InvalidOperationException(
                    "Context-owned successor resolution must not form external routes.")),
            work ?? new AssemblyReferenceResolutionWorkLedger(Budget()));
    }

    static AssemblyReferenceExternalRouteSet RouteSet(
        AssemblyReferenceResolutionRequest request,
        AssemblyReferenceExternalRoute route)
    {
        var selection = new AssemblyBindingSelectionSnapshot(
            request.PolicyVersion,
            AssemblyBindingSelection.NameNotOwned());
        return new(
            request.BindingRequest,
            request.Generation,
            new AssemblyReferenceResolutionContextAdvancement.NoNameOwner(
                request.BindingRequest,
                request.Generation,
                selection),
            ImmutableArray.Create(route),
            (_, _) => throw new InvalidOperationException());
    }

    sealed class WorkspaceContinuationTimeProvider : TimeProvider
    {
        readonly object _gate = new();
        readonly List<ManualTimer> _timers = [];
        DateTimeOffset _now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_gate)
            {
                timer.ChangeCore(dueTime, period, _now);
                _timers.Add(timer);
            }
            return timer;
        }

        internal void Advance(TimeSpan duration)
        {
            List<(TimerCallback Callback, object? State)> callbacks = [];
            lock (_gate)
            {
                _now += duration;
                foreach (ManualTimer timer in _timers)
                {
                    if (timer.TryTakeCallback(_now, out var callback))
                        callbacks.Add(callback);
                }
            }
            foreach (var callback in callbacks)
                callback.Callback(callback.State);
        }

        sealed class ManualTimer(
            WorkspaceContinuationTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            DateTimeOffset _dueAt;
            TimeSpan _period;
            bool _enabled;
            bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._gate)
                {
                    if (_disposed)
                        return false;

                    ChangeCore(dueTime, period, owner._now);
                    return true;
                }
            }

            internal void ChangeCore(
                TimeSpan dueTime,
                TimeSpan period,
                DateTimeOffset now)
            {
                _enabled = dueTime != Timeout.InfiniteTimeSpan;
                _dueAt = _enabled ? now + dueTime : DateTimeOffset.MaxValue;
                _period = period;
            }

            internal bool TryTakeCallback(
                DateTimeOffset now,
                out (TimerCallback Callback, object? State) result)
            {
                if (_disposed || !_enabled || now < _dueAt)
                {
                    result = default;
                    return false;
                }

                if (_period == Timeout.InfiniteTimeSpan)
                {
                    _enabled = false;
                }
                else
                {
                    _dueAt = now + _period;
                }
                result = (callback, state);
                return true;
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    sealed class ReplacementFixture : IAsyncDisposable
    {
        readonly WorkspaceReplacementCoordinator _coordinator;
        readonly WorkspaceRealizationOperationLease _predecessor;
        readonly WorkspaceRealizationOperationLease _successor;

        ReplacementFixture(
            WorkspaceReplacementCoordinator coordinator,
            WorkspaceRealizationOperationLease predecessor,
            WorkspaceRealizationOperationLease successor,
            ResolutionEnvironment predecessorEnvironment,
            AssemblyReferenceResolutionGenerationReceipt successorGeneration)
        {
            _coordinator = coordinator;
            _predecessor = predecessor;
            _successor = successor;
            PredecessorEnvironment = predecessorEnvironment;
            SuccessorGeneration = successorGeneration;
            Receipt =
                new AssemblyReferenceResolutionWorkspaceReplacementReceipt(
                    coordinator,
                    predecessor,
                    successor,
                    predecessorEnvironment.Generation,
                    successorGeneration,
                    demandEvidence: new object());
        }

        internal ResolutionEnvironment PredecessorEnvironment { get; }

        internal AssemblyReferenceResolutionGenerationReceipt
            SuccessorGeneration
        { get; }

        internal AssemblyReferenceResolutionWorkspaceReplacementReceipt Receipt
        { get; }

        internal static async Task<ReplacementFixture> CreateAsync()
        {
            var coordinator = new WorkspaceReplacementCoordinator();
            WorkspaceRealizationOperationLease predecessor =
                await ActivateAsync(coordinator);
            ResolutionEnvironment predecessorEnvironment =
                Environment(predecessor);
            var start = Assert.IsType<
                WorkspaceRealizationCandidateStartResult.Prepared>(
                    await coordinator.BeginCandidateAsync(
                        predecessor.Definition.Plan,
                        TestContext.Current.CancellationToken));
            _ = Assert.IsType<
                WorkspaceRealizationCandidateCompletionResult.Ready>(
                    await coordinator.CompleteCandidateAsync(
                        start.Candidate,
                        TestContext.Current.CancellationToken));
            _ = Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
                coordinator.CutOver(
                    start.Candidate,
                    predecessor.Definition));
            WorkspaceRealizationOperationLease successor =
                Assert.IsType<
                    WorkspaceRealizationOperationAdmission.Admitted>(
                        await coordinator.EnterOperationAsync(
                            TestContext.Current.CancellationToken)).Lease;
            return new(
                coordinator,
                predecessor,
                successor,
                predecessorEnvironment,
                Environment(successor).Generation);
        }

        static ResolutionEnvironment Environment(
            WorkspaceRealizationOperationLease operation)
        {
            MemberCallGraphFocalScopeReceipt focalScope =
                MemberCallGraphFocalScopeReceipt.CaptureEverything(
                    operation.Scope,
                    operation.Definition.Registrations);
            return new(
                AssemblyReferenceResolutionGenerationReceipt.Capture(
                    operation.Scope,
                    focalScope),
                focalScope);
        }

        public async ValueTask DisposeAsync()
        {
            _successor.Dispose();
            _predecessor.Dispose();
            await _coordinator.DisposeAsync();
        }
    }
}
