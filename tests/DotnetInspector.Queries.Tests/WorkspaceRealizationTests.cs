using System.Collections.Immutable;
using DotnetInspector.QueriesConsumer;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceRealizationTests
{
    [Fact]
    public void ReplacementSurface_OmitsRedundantIdentityTypes()
    {
        var assembly = typeof(WorkspaceReplacementCoordinator).Assembly;

        Assert.Null(assembly.GetType(
            "DotnetInspector.Queries.WorkspaceDefinitionSnapshotIdentity"));
        Assert.Null(assembly.GetType(
            "DotnetInspector.Queries.WorkspaceRealizationCandidateIdentity"));
        Assert.Null(assembly.GetType(
            "DotnetInspector.Queries.WorkspaceRealizationReplacementAttemptIdentity"));
        Assert.Null(assembly.GetType(
            "DotnetInspector.Queries.WorkspaceRealizationCoordinator"));
        Assert.Null(typeof(WorkspaceDefinitionSnapshot).GetProperty("Identity"));
        Assert.Null(typeof(WorkspaceRealizationCandidate).GetProperty(
            "Identity"));
        Assert.Null(typeof(WorkspaceRealizationCandidate).GetProperty(
            "Attempt"));
    }

    [Fact]
    public async Task Cutover_StopsPredecessorAdmissionAndDrainsAdmittedOperation()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate firstCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(firstCandidate))
        {
            _ = await ReplaceScopeAsync(
                construction.Workspace,
                PackageAssemblyContextCompletionTests.SharedBinding(
                    "Predecessor.First"),
                PackageAssemblyContextCompletionTests.SharedBinding(
                    "Predecessor.Second"));
        }
        WorkspaceRealization first =
            await WorkspaceRealizationConsumer.ActivateAsync(
                coordinator,
                firstCandidate);
        using WorkspaceRealizationOperationLease predecessor =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);

        WorkspaceRealizationCandidate secondCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(secondCandidate))
        {
            _ = await ReplaceScopeAsync(
                construction.Workspace,
                PackageAssemblyContextCompletionTests.SharedBinding(
                    "Successor.Only"));
        }
        _ = await coordinator.CompleteCandidateAsync(
            secondCandidate,
            TestContext.Current.CancellationToken);
        var cutover = Assert.IsType<
            WorkspaceRealizationCutoverResult.Activated>(
                coordinator.CutOver(secondCandidate));

        Assert.Same(first.Identity, predecessor.Realization);
        Assert.Equal(2, predecessor.Scope.Packages.Length);
        Assert.NotSame(first.Identity, cutover.Realization.Identity);
        Assert.NotNull(cutover.Predecessor);
        Assert.False(cutover.Predecessor.Completion.IsCompleted);

        using WorkspaceRealizationOperationLease successor =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);
        Assert.Same(cutover.Realization.Identity, successor.Realization);
        Assert.Single(successor.Scope.Packages);
        Assert.Equal(2, predecessor.Scope.Packages.Length);

        predecessor.Dispose();
        WorkspaceRealizationSettlement settlement =
            await cutover.Predecessor.Completion;
        Assert.Equal(
            WorkspaceRealizationRetirementReason.Replaced,
            settlement.Reason);
        Assert.NotNull(settlement.Report);
        Assert.Null(settlement.Failure);
    }

    [Fact]
    public async Task CandidateFailure_PreservesActiveRealization()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate firstCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        WorkspaceRealization first =
            await WorkspaceRealizationConsumer.ActivateAsync(
                coordinator,
                firstCandidate);
        WorkspaceRealizationCandidate abandonedCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);

        var retiring = Assert.IsType<
            WorkspaceRealizationCandidateRetirementResult.Retiring>(
                coordinator.AbandonCandidate(abandonedCandidate));
        WorkspaceRealizationSettlement settlement =
            await retiring.Retirement.Completion;

        Assert.Same(first, coordinator.Current);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateAbandoned,
            settlement.Reason);
        Assert.True(settlement.Succeeded);
        using WorkspaceRealizationOperationLease operation =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);
        Assert.Same(first.Identity, operation.Realization);
    }

    [Fact]
    public async Task SupersedeCandidate_RetiresWithoutStartingReplacement()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);

        var retiring = Assert.IsType<
            WorkspaceRealizationCandidateRetirementResult.Retiring>(
                coordinator.SupersedeCandidate(candidate));
        WorkspaceRealizationSettlement settlement =
            await retiring.Retirement.Completion;

        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateSuperseded,
            settlement.Reason);
        Assert.True(settlement.Succeeded);
        Assert.Null(coordinator.Current);
        var admission = Assert.IsType<
            WorkspaceRealizationOperationAdmission.Unavailable>(
                await coordinator.EnterOperationAsync(
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceRealizationOperationUnavailableReason.NoActiveRealization,
            admission.Reason);
    }

    [Fact]
    public async Task CancelledCandidateStartWait_DoesNotCreateReplacement()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        WorkspaceRealizationConstructionLease construction =
            candidate.EnterConstruction();
        using var cancellation = new CancellationTokenSource();

        Task<WorkspaceRealizationCandidateStartResult> replacement =
            coordinator.BeginCandidateAsync(
                WorkspacePlan.Empty,
                cancellation.Token).AsTask();
        await Task.Yield();
        Assert.False(replacement.IsCompleted);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await replacement);
        construction.Dispose();
        WorkspaceRealizationSettlement settlement =
            await candidate.Settlement;

        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateSuperseded,
            settlement.Reason);
        Assert.True(settlement.Succeeded);
        WorkspaceRealizationCandidate next =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        Assert.NotSame(candidate.Realization, next.Realization);
    }

    [Fact]
    public async Task CandidateRuntimeFailure_RetiresCandidateAndPreservesActiveRealization()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate firstCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        WorkspaceRealization first =
            await WorkspaceRealizationConsumer.ActivateAsync(
                coordinator,
                firstCandidate);
        WorkspaceRealizationCandidate failedCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        InspectionWorkspace failedWorkspace;
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(failedCandidate))
        {
            failedWorkspace = construction.Workspace;
        }
        _ = await failedWorkspace.CloseAsync();

        var failed = Assert.IsType<
            WorkspaceRealizationCandidateCompletionResult.Rejected>(
                await coordinator.CompleteCandidateAsync(
                    failedCandidate,
                    TestContext.Current.CancellationToken));
        WorkspaceRealizationSettlement settlement =
            await failedCandidate.Settlement;

        Assert.Equal(
            WorkspaceRealizationCandidateRejection.RuntimeUnavailable,
            failed.Reason);
        Assert.NotNull(failed.RuntimeFailure);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateFailed,
            settlement.Reason);
        Assert.Same(first, coordinator.Current);
        using WorkspaceRealizationOperationLease operation =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);
        Assert.Same(first.Identity, operation.Realization);
    }

    [Fact]
    public async Task Completion_WaitsForAdmittedConstructionAndClosesAdmission()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(candidate);

        Task<WorkspaceRealizationCandidateCompletionResult> completion =
            coordinator.CompleteCandidateAsync(
                candidate,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();

        Assert.False(completion.IsCompleted);
        Assert.Throws<InvalidOperationException>(
            () => WorkspaceRealizationConsumer.EnterConstruction(candidate));
        var concurrent = Assert.IsType<
            WorkspaceRealizationCandidateCompletionResult.Rejected>(
                await coordinator.CompleteCandidateAsync(
                    candidate,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            WorkspaceRealizationCandidateRejection.CompletionInProgress,
            concurrent.Reason);

        construction.Dispose();
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await completion);
    }

    [Fact]
    public async Task CancelledCompletion_ReleasesCaptureAndSettlesCandidate()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        InspectionWorkspace workspace;
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(candidate))
        {
            workspace = construction.Workspace;
        }
        ArtifactRootResult<
            InspectionWorkspace.ArtifactRootCompositionReadLease> heldRead =
            await workspace.ReadArtifactRootCompositionAsync(
                workspace.Identity);
        InspectionWorkspace.ArtifactRootCompositionReadLease held =
            Assert.IsType<
                ArtifactRootResult<
                    InspectionWorkspace.ArtifactRootCompositionReadLease>
                    .Available>(heldRead).Value;
        using var cancellation = new CancellationTokenSource();

        Task<WorkspaceRealizationCandidateCompletionResult> completion =
            coordinator.CompleteCandidateAsync(
                candidate,
                cancellation.Token).AsTask();
        Assert.False(completion.IsCompleted);

        cancellation.Cancel();
        held.Dispose();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await completion);
        WorkspaceRealizationSettlement settlement =
            await candidate.Settlement.WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateCancelled,
            settlement.Reason);
        Assert.Null(settlement.Failure);
        Assert.Null(candidate.State.WorkspaceReference);
    }

    [Fact]
    public async Task SupersededCompletion_ReportsStaleCandidate()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        InspectionWorkspace workspace;
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(candidate))
        {
            workspace = construction.Workspace;
        }
        ArtifactRootResult<
            InspectionWorkspace.ArtifactRootCompositionReadLease> heldRead =
            await workspace.ReadArtifactRootCompositionAsync(
                workspace.Identity);
        using InspectionWorkspace.ArtifactRootCompositionReadLease held =
            Assert.IsType<
                ArtifactRootResult<
                    InspectionWorkspace.ArtifactRootCompositionReadLease>
                    .Available>(heldRead).Value;

        Task<WorkspaceRealizationCandidateCompletionResult> completion =
            coordinator.CompleteCandidateAsync(
                candidate,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();
        Assert.False(completion.IsCompleted);
        Task<WorkspaceRealizationCandidateStartResult> replacement =
            coordinator.BeginCandidateAsync(
                WorkspacePlan.Empty,
                TestContext.Current.CancellationToken).AsTask();

        held.Dispose();

        var rejected = Assert.IsType<
            WorkspaceRealizationCandidateCompletionResult.Rejected>(
                await completion);
        Assert.Equal(
            WorkspaceRealizationCandidateRejection.StaleCandidate,
            rejected.Reason);
        Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
            await replacement);
    }

    [Fact]
    public async Task ClosedCoordinatorCompletion_ReportsCoordinatorClosed()
    {
        var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        InspectionWorkspace workspace;
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(candidate))
        {
            workspace = construction.Workspace;
        }
        ArtifactRootResult<
            InspectionWorkspace.ArtifactRootCompositionReadLease> heldRead =
            await workspace.ReadArtifactRootCompositionAsync(
                workspace.Identity);
        using InspectionWorkspace.ArtifactRootCompositionReadLease held =
            Assert.IsType<
                ArtifactRootResult<
                    InspectionWorkspace.ArtifactRootCompositionReadLease>
                    .Available>(heldRead).Value;

        Task<WorkspaceRealizationCandidateCompletionResult> completion =
            coordinator.CompleteCandidateAsync(
                candidate,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();
        Assert.False(completion.IsCompleted);
        Task<WorkspaceReplacementCoordinatorCloseReport> close =
            coordinator.CloseAsync();

        held.Dispose();

        var rejected = Assert.IsType<
            WorkspaceRealizationCandidateCompletionResult.Rejected>(
                await completion);
        Assert.Equal(
            WorkspaceRealizationCandidateRejection.CoordinatorClosed,
            rejected.Reason);
        _ = await close;
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task NewCandidate_SupersedesAndSettlesPriorCandidate()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate prior =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);

        var next = Assert.IsType<
            WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(
                    WorkspacePlan.Empty,
                    TestContext.Current.CancellationToken));

        Assert.NotNull(next.SupersededCandidate);
        WorkspaceRealizationSettlement settlement =
            await next.SupersededCandidate.Completion;
        Assert.Same(prior.Realization, settlement.Realization);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateSuperseded,
            settlement.Reason);
        Assert.Throws<InvalidOperationException>(
            () => WorkspaceRealizationConsumer.EnterConstruction(prior));
        Assert.IsType<
            WorkspaceRealizationCutoverResult.Rejected>(
                coordinator.CutOver(prior));
    }

    [Fact]
    public async Task SupersededCandidate_DrainsAdmittedConstruction()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate prior =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(prior);

        Task<WorkspaceRealizationCandidateStartResult> replacement =
            coordinator.BeginCandidateAsync(
                WorkspacePlan.Empty,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();

        Assert.False(replacement.IsCompleted);
        Assert.False(prior.Settlement.IsCompleted);
        Assert.Throws<InvalidOperationException>(
            () => WorkspaceRealizationConsumer.EnterConstruction(prior));
        Assert.Same(prior.Realization, construction.Workspace.Identity);

        construction.Dispose();

        Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
            await replacement);
        WorkspaceRealizationSettlement settlement = await prior.Settlement;
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateSuperseded,
            settlement.Reason);
    }

    [Fact]
    public async Task ConcurrentReplacement_NewestIntentCreatesTheCandidate()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate prior =
                await WorkspaceRealizationConsumer.BeginAsync(
                    coordinator,
                    WorkspacePlan.Empty);
        PackageRootBinding binding =
                PackageAssemblyContextCompletionTests.SharedBinding(
                    "Candidate.Supersession");
        PackageAssemblyContextCompletion completion;
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(prior))
        {
            completion = await PreparePackageCompletionAsync(
                construction.Workspace,
                binding);
        }
        PackageAssemblyContextProjection retained =
                completion.CreateProjection([binding]);

        Task<WorkspaceRealizationCandidateStartResult> first =
                coordinator.BeginCandidateAsync(
                    WorkspacePlan.Empty,
                    TestContext.Current.CancellationToken).AsTask();
        Task<WorkspaceRealizationCandidateStartResult> second =
                coordinator.BeginCandidateAsync(
                    WorkspacePlan.Empty,
                    TestContext.Current.CancellationToken).AsTask();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        await retained.ReturnAsync();
        Assert.IsType<WorkspaceRealizationCandidateStartResult.Superseded>(
                await first);
        var prepared = Assert.IsType<
                WorkspaceRealizationCandidateStartResult.Prepared>(
                    await second);
        Assert.NotSame(prior.Realization, prepared.Candidate.Realization);
    }

    [Fact]
    public async Task OperationAuthority_RetainsExactDefinitionAssociation()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        _ = await WorkspaceRealizationConsumer.ActivateAsync(
            coordinator,
            candidate);
        using WorkspaceRealizationOperationLease before =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);

        var prefix = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Microsoft.Extensions."));
        WorkspaceRegistrationOperationResult result =
            before.Workspace.ReplaceRegistrations(
                before.Definition.Registrations,
                ImmutableArray.Create<WorkspaceRegistration>(prefix));
        WorkspaceRegistrationRevision afterRevision =
            Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
                result).Revision;

        using WorkspaceRealizationOperationLease after =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);

        Assert.Same(
            before.Realization,
            after.Realization);
        Assert.Same(before.Realization, before.Definition.Workspace);
        Assert.Same(after.Realization, after.Definition.Workspace);
        Assert.NotSame(
            before.Definition.Registrations,
            after.Definition.Registrations);
        Assert.Same(before.Definition.Scope, after.Definition.Scope);
        Assert.Empty(before.Definition.Registrations.Registrations);
        Assert.Same(afterRevision, after.Definition.Registrations);
        Assert.Single(after.Definition.Registrations.Registrations);
    }

    [Fact]
    public async Task EqualOriginPlan_SharesIntentButNotRealizationAuthority()
    {
        var plan = WorkspacePlan.Empty;
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate firstCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(coordinator, plan);
        WorkspaceRealization first =
            await WorkspaceRealizationConsumer.ActivateAsync(
                coordinator,
                firstCandidate);
        WorkspaceRealizationCandidate secondCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(coordinator, plan);
        _ = await coordinator.CompleteCandidateAsync(
            secondCandidate,
            TestContext.Current.CancellationToken);
        var second = Assert.IsType<
            WorkspaceRealizationCutoverResult.Activated>(
                coordinator.CutOver(secondCandidate));
        WorkspaceRealizationSettlement predecessor =
            await second.Predecessor!.Completion;

        Assert.Same(plan, first.OriginPlan);
        Assert.Same(plan, second.Realization.OriginPlan);
        Assert.NotSame(first.Identity, second.Realization.Identity);
        Assert.NotSame(
            first.InitialDefinition.Registrations,
            second.Realization.InitialDefinition.Registrations);
        Assert.NotSame(
            first.InitialDefinition.Scope,
            second.Realization.InitialDefinition.Scope);
        Assert.Null(predecessor.Failure);

        using WorkspaceRealizationOperationLease operation =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);
        Assert.Same(second.Realization.Identity, operation.Realization);
    }

    [Fact]
    public async Task SharedPackageContent_PredecessorSettlementKeepsSuccessorUsable()
    {
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Shared.Realization.Content");
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate firstCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(firstCandidate))
        {
            _ = await PreparePackageCompletionAsync(
                construction.Workspace,
                binding);
        }
        _ = await WorkspaceRealizationConsumer.ActivateAsync(
            coordinator,
            firstCandidate);

        WorkspaceRealizationCandidate secondCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        PackageAssemblyContextCompletion successor;
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(secondCandidate))
        {
            successor = await PreparePackageCompletionAsync(
                construction.Workspace,
                binding);
        }
        _ = await coordinator.CompleteCandidateAsync(
            secondCandidate,
            TestContext.Current.CancellationToken);
        var cutover = Assert.IsType<
            WorkspaceRealizationCutoverResult.Activated>(
                coordinator.CutOver(secondCandidate));

        _ = await cutover.Predecessor!.Completion;
        using WorkspaceRealizationOperationLease operation =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);
        PackageAssemblyContextProjection projection =
            successor.CreateProjection([binding]);
        var assembly =
            projection.SurfaceRole.Participants[0].Participant.Assembly;
        Assert.True(projection.SurfaceRole.Use(
            group => group.GetAssemblyImageSpan(assembly).IsAvailable));
        await projection.ReturnAsync();
    }

    [Fact]
    public async Task Cutover_RejectsCandidateUntilConstructionCompletes()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);

        var rejected = Assert.IsType<
            WorkspaceRealizationCutoverResult.Rejected>(
                coordinator.CutOver(candidate));

        Assert.Equal(
            WorkspaceRealizationCandidateRejection.NotReady,
            rejected.Reason);
        using WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(candidate);
        Assert.Same(candidate.Realization, construction.Workspace.Identity);
    }

    [Fact]
    public async Task OperationLease_DoubleDisposeDoesNotEndAnotherLease()
    {
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate firstCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        _ = await WorkspaceRealizationConsumer.ActivateAsync(
            coordinator,
            firstCandidate);
        WorkspaceRealizationOperationLease first =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);
        using WorkspaceRealizationOperationLease second =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);
        WorkspaceRealizationCandidate replacement =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        _ = await coordinator.CompleteCandidateAsync(
            replacement,
            TestContext.Current.CancellationToken);
        var cutover = Assert.IsType<
            WorkspaceRealizationCutoverResult.Activated>(
                coordinator.CutOver(replacement));

        first.Dispose();
        first.Dispose();
        Assert.False(cutover.Predecessor!.Completion.IsCompleted);

        second.Dispose();
        _ = await cutover.Predecessor.Completion;
        Assert.Throws<ObjectDisposedException>(
            () => _ = first.Workspace);
    }

    [Fact]
    public async Task Settlement_PreservesWorkspaceCloseFailure()
    {
        var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        InspectionWorkspace workspace;
        using (WorkspaceRealizationConstructionLease construction =
            WorkspaceRealizationConsumer.EnterConstruction(candidate))
        {
            workspace = construction.Workspace;
        }
        PackageRootBinding binding =
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Realization.Close.Failure");
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion([binding]);
        PackageAssemblyContextCompletion completion =
            await operation.ExecuteAsync(operation.Identity);
        completion.SurfaceAssemblyContextGroup.RegisterOwnedResource(
            new ThrowingResource());
        _ = await coordinator.CompleteCandidateAsync(
            candidate,
            TestContext.Current.CancellationToken);
        _ = coordinator.CutOver(candidate);

        WorkspaceReplacementCoordinatorCloseReport report =
            await coordinator.CloseAsync();

        WorkspaceRealizationSettlement settlement = Assert.Single(
            report.Settlements);
        Assert.NotNull(settlement.Report);
        Assert.False(settlement.Report.Succeeded);
        Assert.False(settlement.Succeeded);
        var group = Assert.IsType<
            InspectionWorkspaceCoordinatedGroupCloseResult<
                PackageRoleGroupCleanupRecord>>(
                    Assert.Single(settlement.Report.Groups));
        Assert.IsType<PackageRoleGroupCleanupRecord.Failed>(group.Result);
        Assert.Null(settlement.Failure);
        AggregateException disposal = await Assert.ThrowsAsync<
            AggregateException>(
                async () => await coordinator.DisposeAsync());
        var failure = Assert.IsType<
            WorkspaceRealizationSettlementException>(
                Assert.Single(disposal.InnerExceptions));
        Assert.Same(settlement, failure.Settlement);
    }

    [Fact]
    public async Task Close_WaitsForAdmittedOperationsAndReportsEverySettlement()
    {
        var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        _ = await WorkspaceRealizationConsumer.ActivateAsync(
            coordinator,
            candidate);
        WorkspaceRealizationOperationLease operation =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);

        Task<WorkspaceReplacementCoordinatorCloseReport> close =
            coordinator.CloseAsync();

        Assert.False(close.IsCompleted);
        Assert.IsType<WorkspaceRealizationOperationAdmission.Unavailable>(
            await coordinator.EnterOperationAsync(
                TestContext.Current.CancellationToken));

        operation.Dispose();
        WorkspaceReplacementCoordinatorCloseReport report = await close;
        WorkspaceRealizationSettlement settlement = Assert.Single(
            report.Settlements);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CoordinatorClosed,
            settlement.Reason);
        Assert.True(settlement.Succeeded);
        Assert.Null(settlement.Failure);
        Assert.Null(candidate.State.WorkspaceReference);
        await coordinator.DisposeAsync();
    }

    [Fact]
    public async Task Close_WaitsForInFlightUseAfterLeaseDisposal()
    {
        var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        _ = await WorkspaceRealizationConsumer.ActivateAsync(
            coordinator,
            candidate);
        WorkspaceRealizationOperationLease operation =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);
        WorkspaceRealizationOperationUse use =
            operation.EnterUse();

        Task<WorkspaceReplacementCoordinatorCloseReport> close =
            coordinator.CloseAsync();
        operation.Dispose();

        Assert.False(close.IsCompleted);

        use.Dispose();
        WorkspaceReplacementCoordinatorCloseReport report = await close;
        Assert.True(Assert.Single(report.Settlements).Succeeded);
        await coordinator.DisposeAsync();
    }

    sealed class ThrowingResource : IDisposable
    {
        public void Dispose() =>
            throw new InvalidOperationException(
                "Workspace realization cleanup failure.");
    }

    static async Task<PackageAssemblyContextCompletion>
        PreparePackageCompletionAsync(
            InspectionWorkspace workspace,
            PackageRootBinding binding)
    {
        PackageAssemblyContextCompletionOperation operation =
            workspace.PreparePackageAssemblyContextCompletion([binding]);
        return await operation.ExecuteAsync(operation.Identity);
    }

    static async Task<WorkspaceScopeSnapshot> ReplaceScopeAsync(
        InspectionWorkspace workspace,
        params PackageRootBinding[] bindings)
    {
        WorkspaceScopeSnapshot current =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        return Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ReplaceScopeAsync(
                current.Revision,
                [.. bindings],
                DateTimeOffset.UtcNow.AddMinutes(1),
                TestContext.Current.CancellationToken)).Snapshot;
    }
}
