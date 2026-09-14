using System.Collections.Immutable;
using DotnetInspector.QueriesConsumer;
using DotnetInspector.SourceSelection;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceRealizationTests
{
    [Fact]
    public async Task Cutover_StopsPredecessorAdmissionAndDrainsAdmittedOperation()
    {
        await using var coordinator = new WorkspaceRealizationCoordinator();
        WorkspaceRealizationCandidate firstCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        _ = await ReplaceScopeAsync(
            firstCandidate.ConstructionWorkspace,
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Predecessor.First"),
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Predecessor.Second"));
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
        _ = await ReplaceScopeAsync(
            secondCandidate.ConstructionWorkspace,
            PackageAssemblyContextCompletionTests.SharedBinding(
                "Successor.Only"));
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
        await using var coordinator = new WorkspaceRealizationCoordinator();
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

        var retiring = Assert.IsType<
            WorkspaceRealizationCandidateRetirementResult.Retiring>(
                coordinator.AbandonCandidate(failedCandidate));
        WorkspaceRealizationSettlement settlement =
            await retiring.Retirement.Completion;

        Assert.Same(first, coordinator.Current);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateAbandoned,
            settlement.Reason);
        using WorkspaceRealizationOperationLease operation =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);
        Assert.Same(first.Identity, operation.Realization);
    }

    [Fact]
    public async Task CancelledCompletion_ReleasesCaptureAndSettlesCandidate()
    {
        await using var coordinator = new WorkspaceRealizationCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        InspectionWorkspace workspace = candidate.ConstructionWorkspace;
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
    public async Task NewCandidate_SupersedesAndSettlesPriorCandidate()
    {
        await using var coordinator = new WorkspaceRealizationCoordinator();
        WorkspaceRealizationCandidate prior =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);

        var next = Assert.IsType<
            WorkspaceRealizationCandidateStartResult.Prepared>(
                await coordinator.BeginCandidateAsync(WorkspacePlan.Empty));

        Assert.NotNull(next.SupersededCandidate);
        WorkspaceRealizationSettlement settlement =
            await next.SupersededCandidate.Completion;
        Assert.Same(prior.Realization, settlement.Realization);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateSuperseded,
            settlement.Reason);
        Assert.Throws<InvalidOperationException>(
            () => _ = prior.ConstructionWorkspace);
        Assert.IsType<
            WorkspaceRealizationCutoverResult.Rejected>(
                coordinator.CutOver(prior));
    }

    [Fact]
    public async Task ConcurrentReplacement_NewestIntentCreatesTheCandidate()
    {
        await using var coordinator = new WorkspaceRealizationCoordinator();
        WorkspaceRealizationCandidate prior =
                await WorkspaceRealizationConsumer.BeginAsync(
                    coordinator,
                    WorkspacePlan.Empty);
        PackageRootBinding binding =
                PackageAssemblyContextCompletionTests.SharedBinding(
                    "Candidate.Supersession");
        PackageAssemblyContextCompletion completion =
                await PreparePackageCompletionAsync(
                    prior.ConstructionWorkspace,
                    binding);
        PackageAssemblyContextProjection retained =
                completion.CreateProjection([binding]);

        Task<WorkspaceRealizationCandidateStartResult> first =
                coordinator.BeginCandidateAsync(WorkspacePlan.Empty).AsTask();
        Task<WorkspaceRealizationCandidateStartResult> second =
                coordinator.BeginCandidateAsync(WorkspacePlan.Empty).AsTask();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        await retained.ReturnAsync();
        Assert.IsType<WorkspaceRealizationCandidateStartResult.Superseded>(
                await first);
        var prepared = Assert.IsType<
                WorkspaceRealizationCandidateStartResult.Prepared>(
                    await second);
        Assert.NotSame(prior.Identity, prepared.Candidate.Identity);
    }

    [Fact]
    public async Task OperationAuthority_RetainsExactDefinitionSnapshot()
    {
        await using var coordinator = new WorkspaceRealizationCoordinator();
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
        Assert.NotSame(
            before.Definition.Identity,
            after.Definition.Identity);
        Assert.Empty(before.Definition.Registrations.Registrations);
        Assert.Same(afterRevision, after.Definition.Registrations);
        Assert.Single(after.Definition.Registrations.Registrations);
    }

    [Fact]
    public async Task EqualOriginPlan_SharesIntentButNotRealizationAuthority()
    {
        var plan = WorkspacePlan.Empty;
        await using var coordinator = new WorkspaceRealizationCoordinator();
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
            first.InitialDefinition.Identity,
            second.Realization.InitialDefinition.Identity);
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
        await using var coordinator = new WorkspaceRealizationCoordinator();
        WorkspaceRealizationCandidate firstCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        _ = await PreparePackageCompletionAsync(
            firstCandidate.ConstructionWorkspace,
            binding);
        _ = await WorkspaceRealizationConsumer.ActivateAsync(
            coordinator,
            firstCandidate);

        WorkspaceRealizationCandidate secondCandidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        PackageAssemblyContextCompletion successor =
            await PreparePackageCompletionAsync(
                secondCandidate.ConstructionWorkspace,
                binding);
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
        await using var coordinator = new WorkspaceRealizationCoordinator();
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
        Assert.Same(
            candidate.Realization,
            candidate.ConstructionWorkspace.Identity);
    }

    [Fact]
    public async Task OperationLease_DoubleDisposeDoesNotEndAnotherLease()
    {
        await using var coordinator = new WorkspaceRealizationCoordinator();
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
        await using var coordinator = new WorkspaceRealizationCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        InspectionWorkspace workspace = candidate.ConstructionWorkspace;
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

        WorkspaceRealizationCoordinatorCloseReport report =
            await coordinator.CloseAsync();

        WorkspaceRealizationSettlement settlement = Assert.Single(
            report.Settlements);
        Assert.NotNull(settlement.Report);
        var group = Assert.IsType<
            InspectionWorkspaceCoordinatedGroupCloseResult<
                PackageRoleGroupCleanupRecord>>(
                    Assert.Single(settlement.Report.Groups));
        Assert.IsType<PackageRoleGroupCleanupRecord.Failed>(group.Result);
        Assert.Null(settlement.Failure);
    }

    [Fact]
    public async Task Close_WaitsForAdmittedOperationsAndReportsEverySettlement()
    {
        var coordinator = new WorkspaceRealizationCoordinator();
        WorkspaceRealizationCandidate candidate =
            await WorkspaceRealizationConsumer.BeginAsync(
                coordinator,
                WorkspacePlan.Empty);
        _ = await WorkspaceRealizationConsumer.ActivateAsync(
            coordinator,
            candidate);
        WorkspaceRealizationOperationLease operation =
            await WorkspaceRealizationConsumer.EnterAsync(coordinator);

        Task<WorkspaceRealizationCoordinatorCloseReport> close =
            coordinator.CloseAsync();

        Assert.False(close.IsCompleted);
        Assert.IsType<WorkspaceRealizationOperationAdmission.Unavailable>(
            await coordinator.EnterOperationAsync(
                TestContext.Current.CancellationToken));

        operation.Dispose();
        WorkspaceRealizationCoordinatorCloseReport report = await close;
        WorkspaceRealizationSettlement settlement = Assert.Single(
            report.Settlements);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CoordinatorClosed,
            settlement.Reason);
        Assert.Null(settlement.Failure);
        Assert.Null(candidate.State.WorkspaceReference);
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
