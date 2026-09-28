using DotnetInspector.PackageQueries;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.ResearchQueries;

namespace DotnetInspector.Services.Tests;

public sealed partial class PackageHouseExecutionTests
{
    [Fact]
    public async Task
        IntrinsicCoreLibraryWorkspaceContinuation_PublishesCompletePopulationAndFreshOccurrence()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        WorkspacePlan plan = IntrinsicCoreLibraryWorkspacePlan(
            PlatformFamily.DotNetRuntime);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate predecessorCandidate =
            await BeginCandidateAsync(coordinator, plan, cancellationToken);
        PackageDependencyMemberCallGraphOutcome.Completed predecessorGraph;
        using (WorkspaceRealizationConstructionLease construction =
            predecessorCandidate.EnterConstruction())
        {
            predecessorGraph = await IntrinsicCoreLibraryGraphAsync(
                construction.Workspace,
                PlatformFamily.DotNetRuntime,
                cancellationToken);
        }
        WorkspaceRealization predecessorRealization =
            await ActivateCandidateAsync(
                coordinator,
                predecessorCandidate,
                cancellationToken);
        using WorkspaceRealizationOperationLease predecessor =
            await EnterOperationAsync(coordinator, cancellationToken);
        PlatformPopulationArtifactMaterializationOutcome.Completed platform =
            await CreateCoreLibraryPlatformPopulationAsync(cancellationToken);
        IntrinsicCoreLibraryRouteDecision.Applicable applicability =
            Applicable(
                predecessorGraph,
                platform,
                cancellationToken);
        PackageDependencyMemberCallGraphOutcome.Completed? successorGraph =
            null;

        IntrinsicCoreLibraryWorkspaceContinuationOutcome outcome =
            await IntrinsicCoreLibraryWorkspaceContinuationOperation
                .ExecuteAsync(
                    coordinator,
                    predecessor,
                    applicability,
                    platform,
                    async (workspace, token) =>
                    {
                        successorGraph =
                            await IntrinsicCoreLibraryGraphAsync(
                                workspace,
                                PlatformFamily.DotNetRuntime,
                                token);
                        return new(
                            ExactVoidOccurrence(successorGraph),
                            successorGraph.FocalScope);
                    },
                    CatalogBounds(),
                    cancellationToken);

        var published = Assert.IsType<
            IntrinsicCoreLibraryWorkspaceContinuationOutcome.Published>(
                outcome);
        Assert.NotNull(successorGraph);
        Assert.Same(
            predecessorRealization.Identity,
            published.Receipt.PredecessorDefinition.Workspace);
        Assert.Same(
            published.Receipt.Successor.Identity,
            coordinator.Current!.Identity);
        Assert.NotSame(
            predecessorRealization.Identity,
            published.Receipt.Successor.Identity);
        Assert.Same(
            predecessor.Definition.Plan,
            published.Receipt.Successor.OriginPlan);
        Assert.NotSame(
            applicability.Receipt.Context.ScopeRevision,
            published.Receipt.SuccessorApplicability.Context.ScopeRevision);
        Assert.NotSame(
            applicability.Receipt.FocalScope.RegistrationRevision,
            published.Receipt.SuccessorApplicability.FocalScope
                .RegistrationRevision);
        Assert.NotSame(
            applicability.Receipt.Context.Occurrence.CallSite.Identity,
            published.Receipt.SuccessorApplicability.Context.Occurrence
                .CallSite.Identity);
        Assert.NotSame(
            applicability.Receipt.Context.Occurrence.Context
                .BindingPolicyVersion,
            published.Receipt.SuccessorApplicability.Context.Occurrence
                .Context.BindingPolicyVersion);
        Assert.Equal(
            platform.Population.Value.Members.Count,
            published.Receipt.PlatformAdmission.Occurrences.Length);
        Assert.Equal(
            platform.Population.Value.Members.Count,
            published.Receipt.PlatformDeclarations.Members.Length);
        Assert.All(
            published.Receipt.PlatformAdmission.Occurrences,
            occurrence => Assert.Same(
                published.Receipt.Successor.Identity,
                occurrence.Identity.WorkspaceIdentity));
        Assert.NotNull(published.PredecessorRetirement);
        Assert.False(
            published.PredecessorRetirement.Completion.IsCompleted);

        using WorkspaceRealizationOperationLease successor =
            await EnterOperationAsync(coordinator, cancellationToken);
        Assert.Same(
            published.Receipt.Successor.Identity,
            successor.Realization);
        Assert.Same(
            published.Receipt.Successor.InitialDefinition.Scope.Identity,
            successor.Scope.Revision.Identity);

        predecessor.Dispose();
        WorkspaceRealizationSettlement settlement =
            await published.PredecessorRetirement.Completion;
        Assert.Equal(
            WorkspaceRealizationRetirementReason.Replaced,
            settlement.Reason);
        Assert.True(settlement.Succeeded);
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryWorkspaceContinuation_RejectsReusedPredecessorEvidenceAndCleansCandidate()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        WorkspacePlan plan = IntrinsicCoreLibraryWorkspacePlan(
            PlatformFamily.DotNetRuntime);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate predecessorCandidate =
            await BeginCandidateAsync(coordinator, plan, cancellationToken);
        PackageDependencyMemberCallGraphOutcome.Completed predecessorGraph;
        using (WorkspaceRealizationConstructionLease construction =
            predecessorCandidate.EnterConstruction())
        {
            predecessorGraph = await IntrinsicCoreLibraryGraphAsync(
                construction.Workspace,
                PlatformFamily.DotNetRuntime,
                cancellationToken);
        }
        WorkspaceRealization predecessorRealization =
            await ActivateCandidateAsync(
                coordinator,
                predecessorCandidate,
                cancellationToken);
        using WorkspaceRealizationOperationLease predecessor =
            await EnterOperationAsync(coordinator, cancellationToken);
        PlatformPopulationArtifactMaterializationOutcome.Completed platform =
            await CreateCoreLibraryPlatformPopulationAsync(cancellationToken);
        IntrinsicCoreLibraryRouteDecision.Applicable applicability =
            Applicable(
                predecessorGraph,
                platform,
                cancellationToken);

        IntrinsicCoreLibraryWorkspaceContinuationOutcome outcome =
            await IntrinsicCoreLibraryWorkspaceContinuationOperation
                .ExecuteAsync(
                    coordinator,
                    predecessor,
                    applicability,
                    platform,
                    async (workspace, token) =>
                    {
                        _ = await IntrinsicCoreLibraryGraphAsync(
                            workspace,
                            PlatformFamily.DotNetRuntime,
                            token);
                        return new(
                            applicability.Receipt.Context,
                            applicability.Receipt.FocalScope);
                    },
                    CatalogBounds(),
                    cancellationToken);

        var rejected = Assert.IsType<
            IntrinsicCoreLibraryWorkspaceContinuationOutcome.Rejected>(
                outcome);
        Assert.Equal(
            IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                .SuccessorWorkspaceMismatch,
            rejected.Reason);
        Assert.True(rejected.Cleanup.Succeeded);
        Assert.NotNull(rejected.Cleanup.Platform);
        Assert.NotNull(rejected.Cleanup.Candidate);
        Assert.Same(predecessorRealization, coordinator.Current);
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryWorkspaceContinuation_CancellationPreservesPredecessorAndCleansAuthorities()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        WorkspacePlan plan = IntrinsicCoreLibraryWorkspacePlan(
            PlatformFamily.DotNetRuntime);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate predecessorCandidate =
            await BeginCandidateAsync(coordinator, plan, cancellationToken);
        PackageDependencyMemberCallGraphOutcome.Completed predecessorGraph;
        using (WorkspaceRealizationConstructionLease construction =
            predecessorCandidate.EnterConstruction())
        {
            predecessorGraph = await IntrinsicCoreLibraryGraphAsync(
                construction.Workspace,
                PlatformFamily.DotNetRuntime,
                cancellationToken);
        }
        WorkspaceRealization predecessorRealization =
            await ActivateCandidateAsync(
                coordinator,
                predecessorCandidate,
                cancellationToken);
        using WorkspaceRealizationOperationLease predecessor =
            await EnterOperationAsync(coordinator, cancellationToken);
        PlatformPopulationArtifactMaterializationOutcome.Completed platform =
            await CreateCoreLibraryPlatformPopulationAsync(cancellationToken);
        IntrinsicCoreLibraryRouteDecision.Applicable applicability =
            Applicable(
                predecessorGraph,
                platform,
                cancellationToken);
        using var cancellation = new CancellationTokenSource();

        IntrinsicCoreLibraryWorkspaceContinuationOutcome outcome =
            await IntrinsicCoreLibraryWorkspaceContinuationOperation
                .ExecuteAsync(
                    coordinator,
                    predecessor,
                    applicability,
                    platform,
                    (_, token) =>
                    {
                        cancellation.Cancel();
                        token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException(
                            "Cancellation should have interrupted construction.");
                    },
                    CatalogBounds(),
                    cancellation.Token);

        var cancelled = Assert.IsType<
            IntrinsicCoreLibraryWorkspaceContinuationOutcome.Cancelled>(
                outcome);
        Assert.True(cancelled.Cleanup.Succeeded);
        Assert.NotNull(cancelled.Cleanup.Platform);
        Assert.NotNull(cancelled.Cleanup.Candidate);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.CandidateCancelled,
            cancelled.Cleanup.Candidate.Reason);
        Assert.Same(predecessorRealization, coordinator.Current);
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryWorkspaceContinuation_RejectsStalePredecessorEvidence()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        WorkspacePlan plan = IntrinsicCoreLibraryWorkspacePlan(
            PlatformFamily.DotNetRuntime);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate firstCandidate =
            await BeginCandidateAsync(coordinator, plan, cancellationToken);
        PackageDependencyMemberCallGraphOutcome.Completed firstGraph;
        using (WorkspaceRealizationConstructionLease construction =
            firstCandidate.EnterConstruction())
        {
            firstGraph = await IntrinsicCoreLibraryGraphAsync(
                construction.Workspace,
                PlatformFamily.DotNetRuntime,
                cancellationToken);
        }
        _ = await ActivateCandidateAsync(
            coordinator,
            firstCandidate,
            cancellationToken);
        PlatformPopulationArtifactMaterializationOutcome.Completed platform =
            await CreateCoreLibraryPlatformPopulationAsync(cancellationToken);
        IntrinsicCoreLibraryRouteDecision.Applicable staleApplicability =
            Applicable(
                firstGraph,
                platform,
                cancellationToken);

        WorkspaceRealizationCandidate currentCandidate =
            await BeginCandidateAsync(coordinator, plan, cancellationToken);
        using (WorkspaceRealizationConstructionLease construction =
            currentCandidate.EnterConstruction())
        {
            _ = await IntrinsicCoreLibraryGraphAsync(
                construction.Workspace,
                PlatformFamily.DotNetRuntime,
                cancellationToken);
        }
        WorkspaceRealization current = await ActivateCandidateAsync(
            coordinator,
            currentCandidate,
            cancellationToken);
        using WorkspaceRealizationOperationLease operation =
            await EnterOperationAsync(coordinator, cancellationToken);

        IntrinsicCoreLibraryWorkspaceContinuationOutcome outcome =
            await IntrinsicCoreLibraryWorkspaceContinuationOperation
                .ExecuteAsync(
                    coordinator,
                    operation,
                    staleApplicability,
                    platform,
                    (_, _) => throw new InvalidOperationException(
                        "Stale evidence must fail before construction."),
                    CatalogBounds(),
                    cancellationToken);

        var rejected = Assert.IsType<
            IntrinsicCoreLibraryWorkspaceContinuationOutcome.Rejected>(
                outcome);
        Assert.Equal(
            IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                .PredecessorEvidenceMismatch,
            rejected.Reason);
        Assert.True(rejected.Cleanup.Succeeded);
        Assert.NotNull(rejected.Cleanup.Platform);
        Assert.Null(rejected.Cleanup.Candidate);
        Assert.Same(current, coordinator.Current);
    }

    [Fact]
    public async Task
        IntrinsicCoreLibraryWorkspaceContinuation_RejectsForeignMaterializationBeforeCandidate()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        WorkspacePlan plan = IntrinsicCoreLibraryWorkspacePlan(
            PlatformFamily.DotNetRuntime);
        await using var coordinator = new WorkspaceReplacementCoordinator();
        WorkspaceRealizationCandidate predecessorCandidate =
            await BeginCandidateAsync(coordinator, plan, cancellationToken);
        PackageDependencyMemberCallGraphOutcome.Completed predecessorGraph;
        using (WorkspaceRealizationConstructionLease construction =
            predecessorCandidate.EnterConstruction())
        {
            predecessorGraph = await IntrinsicCoreLibraryGraphAsync(
                construction.Workspace,
                PlatformFamily.DotNetRuntime,
                cancellationToken);
        }
        WorkspaceRealization predecessorRealization =
            await ActivateCandidateAsync(
                coordinator,
                predecessorCandidate,
                cancellationToken);
        using WorkspaceRealizationOperationLease predecessor =
            await EnterOperationAsync(coordinator, cancellationToken);
        PlatformPopulationArtifactMaterializationOutcome.Completed expected =
            await CreateCoreLibraryPlatformPopulationAsync(cancellationToken);
        IntrinsicCoreLibraryRouteDecision.Applicable applicability =
            Applicable(
                predecessorGraph,
                expected,
                cancellationToken);
        PlatformPopulationArtifactMaterializationOutcome.Completed foreign =
            await CreateCoreLibraryPlatformPopulationAsync(cancellationToken);

        try
        {
            IntrinsicCoreLibraryWorkspaceContinuationOutcome outcome =
                await IntrinsicCoreLibraryWorkspaceContinuationOperation
                    .ExecuteAsync(
                        coordinator,
                        predecessor,
                        applicability,
                        foreign,
                        (_, _) => throw new InvalidOperationException(
                            "Foreign materialization must fail before construction."),
                        CatalogBounds(),
                        cancellationToken);

            var rejected = Assert.IsType<
                IntrinsicCoreLibraryWorkspaceContinuationOutcome.Rejected>(
                    outcome);
            Assert.Equal(
                IntrinsicCoreLibraryWorkspaceContinuationRejectionReason
                    .PlatformEvidenceMismatch,
                rejected.Reason);
            Assert.True(rejected.Cleanup.Succeeded);
            Assert.NotNull(rejected.Cleanup.Platform);
            Assert.Null(rejected.Cleanup.Candidate);
            Assert.Same(predecessorRealization, coordinator.Current);
        }
        finally
        {
            await RetireAsync(expected);
        }
    }

    private static IntrinsicCoreLibraryRouteDecision.Applicable Applicable(
        PackageDependencyMemberCallGraphOutcome.Completed graph,
        PlatformPopulationArtifactMaterializationOutcome.Completed platform,
        CancellationToken cancellationToken)
    {
        IntrinsicCoreLibraryPlatformApplicabilityPlanResult plan =
            IntrinsicCoreLibraryPlatformApplicabilityQuery.Plan(
                ExactVoidOccurrence(graph),
                graph.FocalScope,
                PlatformFamily.DotNetRuntime);
        var eligible = Assert.IsType<
            IntrinsicCoreLibraryPlatformApplicabilityPlanResult.Eligible>(
                plan);
        return Assert.IsType<IntrinsicCoreLibraryRouteDecision.Applicable>(
            IntrinsicCoreLibraryPlatformApplicabilityQuery.Execute(
                eligible.Plan,
                platform,
                CatalogBounds(),
                cancellationToken));
    }

    private static async ValueTask<WorkspaceRealizationCandidate>
        BeginCandidateAsync(
            WorkspaceReplacementCoordinator coordinator,
            WorkspacePlan plan,
            CancellationToken cancellationToken) =>
        Assert.IsType<WorkspaceRealizationCandidateStartResult.Prepared>(
            await coordinator.BeginCandidateAsync(plan, cancellationToken))
            .Candidate;

    private static async ValueTask<WorkspaceRealization>
        ActivateCandidateAsync(
            WorkspaceReplacementCoordinator coordinator,
            WorkspaceRealizationCandidate candidate,
            CancellationToken cancellationToken)
    {
        _ = Assert.IsType<
            WorkspaceRealizationCandidateCompletionResult.Ready>(
                await coordinator.CompleteCandidateAsync(
                    candidate,
                    cancellationToken));
        return Assert.IsType<WorkspaceRealizationCutoverResult.Activated>(
            coordinator.CutOver(candidate)).Realization;
    }

    private static async ValueTask<WorkspaceRealizationOperationLease>
        EnterOperationAsync(
            WorkspaceReplacementCoordinator coordinator,
            CancellationToken cancellationToken) =>
        Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
            await coordinator.EnterOperationAsync(cancellationToken)).Lease;
}
