using System.Collections.Immutable;

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
