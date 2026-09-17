using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationScopeOperationTests
{
    static DateTimeOffset Deadline =>
        DateTimeOffset.UtcNow.AddMinutes(5);

    [Fact]
    public async Task ProtectedScope_AcceptanceCommitsExactCurrentSlotBeforeSubmission()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync();
        NavigationState initial = Acknowledge(
            fixture.Session.State,
            fixture.Session.Initialization.Authority!);
        NavigationTransition explicitBeginning =
            NavigationTransitions.Begin(
                initial,
                initial.Snapshot.Types[0].Navigation.Action!);
        NavigationEvaluationRequest staleExplicit =
            explicitBeginning.Work!;
        NavigationEvaluationResult staleExplicitEvaluation =
            NavigationTransitions.Evaluate(
                staleExplicit,
                fixture.Ready(staleExplicit),
                fixture.Registry);
        NavigationTransition queued =
            NavigationTransitions.QueueMaintenance(
                explicitBeginning.State);
        NavigationTransition advancing =
            NavigationTransitions.Advance(queued.State);
        Assert.Null(advancing.Work);
        NavigationTransition explicitProtected =
            NavigationTransitions.AcceptScopeOperation(
                advancing.State,
                fixture.Workspace.IssueAddPackagesRequest(
                    fixture.Scope.Revision,
                    [fixture.Bindings[0]],
                    Deadline).Association);
        NavigationTransition staleExplicitCompletion =
            NavigationTransitions.Complete(
                explicitProtected.State,
                staleExplicit,
                staleExplicitEvaluation);
        AssertRejected(
            explicitProtected.State,
            staleExplicitCompletion,
            NavigationCompletionRejection.StaleAttempt);
        NavigationTransition acceptedQueue =
            NavigationTransitions.QueueSynchronization(
                explicitProtected.State);
        Assert.NotNull(
            acceptedQueue.State.Data.ProtectedScope);
        Assert.Null(
            NavigationTransitions.Advance(
                acceptedQueue.State).Work);

        NavigationState maintenanceBasis =
            NavigationTransitions.Cancel(
                queued.State,
                staleExplicit.Identity).State;
        NavigationTransition maintenanceBeginning =
            NavigationTransitions.Advance(maintenanceBasis);
        NavigationEvaluationRequest staleWork =
            maintenanceBeginning.Work!;
        NavigationEvaluationResult staleEvaluation =
            NavigationTransitions.Evaluate(
                staleWork,
                fixture.Ready(staleWork),
                fixture.Registry);
        WorkspaceScopeRequest scopeRequest =
            fixture.Workspace.IssueAddPackagesRequest(
                fixture.Scope.Revision,
                [fixture.Bindings[0]],
                Deadline);
        await using var foreignWorkspace =
            new InspectionWorkspace();
        WorkspaceScopeSnapshot foreignScope = Current(
            await foreignWorkspace.GetScopeSnapshotAsync());
        WorkspaceScopeRequest foreignRequest =
            foreignWorkspace.IssueClearScopeRequest(
                foreignScope.Revision,
                Deadline);
        AssertRefused(
            advancing.State,
            NavigationTransitions.AcceptScopeOperation(
                advancing.State,
                foreignRequest.Association),
            NavigationAdmissionRefusalKind.ForeignWorkspace);
        WorkspaceScopeSnapshot beforeSubmission = Current(
            await fixture.Workspace.GetScopeSnapshotAsync());

        NavigationTransition staleCandidate =
            NavigationTransitions.AcceptScopeOperation(
                maintenanceBeginning.State,
                scopeRequest.Association);
        NavigationTransition moved =
            NavigationTransitions.QueueSynchronization(
                maintenanceBeginning.State);
        Assert.False(
            NavigationTransitions.CanCommit(
                moved.State,
                staleCandidate));

        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                moved.State,
                scopeRequest.Association);
        NavigationScopeEvaluationRequest work =
            Assert.IsType<NavigationScopeEvaluationRequest>(
                accepted.ScopeWork);
        Assert.True(
            NavigationTransitions.CanCommit(
                moved.State,
                accepted));
        Assert.Same(
            beforeSubmission,
            Current(await fixture.Workspace.GetScopeSnapshotAsync()));
        Assert.Same(
            scopeRequest.Association,
            work.Association);
        Assert.False(
            NavigationTransitions.ValidateAuthority(
                accepted.State,
                fixture.Session.Initialization.Authority));

        NavigationAction refusedAction =
            accepted.State.Snapshot.Types[1].Navigation.Action!;
        NavigationTransition laterAction =
            NavigationTransitions.Begin(
                accepted.State,
                refusedAction);
        AssertRefused(
            accepted.State,
            laterAction,
            NavigationAdmissionRefusalKind.ProtectedScopeOperation);
        Assert.DoesNotContain(
            refusedAction.Id,
            accepted.State.Data.ConsumedActions);
        NavigationTransition laterLens =
            NavigationTransitions.BeginLens(
                accepted.State,
                accepted.State.InstalledSnapshot.LensOutcome.EffectiveLens!);
        AssertRefused(
            accepted.State,
            laterLens,
            NavigationAdmissionRefusalKind.ProtectedScopeOperation);
        NavigationTransition laterScope =
            NavigationTransitions.AcceptScopeOperation(
                accepted.State,
                scopeRequest.Association);
        AssertRefused(
            accepted.State,
            laterScope,
            NavigationAdmissionRefusalKind.ProtectedScopeOperation);
        NavigationTransition publication =
            NavigationTransitions.PublishRetainedTypeAction(
                accepted.State,
                accepted.State.Publication,
                accepted.State.InstalledSnapshot.Types[0].Row.Subject);
        Assert.Equal(
            NavigationActionPublicationKind.Refused,
            publication.ActionPublication!.Kind);
        AssertRefused(
            accepted.State,
            publication,
            NavigationAdmissionRefusalKind.ProtectedScopeOperation);

        NavigationTransition staleCompletion =
            NavigationTransitions.Complete(
                accepted.State,
                staleWork,
                staleEvaluation);
        AssertRejected(
            accepted.State,
            staleCompletion,
            NavigationCompletionRejection.StaleAttempt);

        WorkspaceScopeOperationResult result =
            await fixture.Workspace.SubmitScopeRequestAsync(
                scopeRequest,
                TestContext.Current.CancellationToken);
        var noEffect =
            Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
                result);
        NavigationPackageEvaluation package =
            Rebind(
                fixture.Packages[0],
                noEffect.Snapshot.Packages.Single(
                    row =>
                        row.Occurrence
                            == fixture.Scope.Packages[0].Occurrence),
                fixture.Bindings[0]);
        NavigationScopeEvaluationResult evaluated =
            NavigationTransitions.EvaluateScopeOperation(
                work,
                result,
                new NavigationScopePreparation.Ready(
                    new(
                        noEffect.Snapshot,
                        package,
                        fixture.Availability)),
                fixture.Registry);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                work,
                evaluated);
        Assert.Equal(
            NavigationScopeSettlementKind.NoEffect,
            completed.Result!.Consumer.Outcome.Scope!.Kind);
        Assert.Same(result, completed.Result.ScopeResult);
        Assert.True(
            NavigationTransitions.ValidateAuthority(
                completed.State,
                completed.Result.Consumer.Authority));

        NavigationState released =
            NavigationTransitions.Abandon(
                completed.State,
                completed.Result.Consumer.Authority!).State;
        NavigationTransition resumed =
            NavigationTransitions.Advance(released);
        Assert.Same(staleWork.Identity, resumed.Work!.Identity);
        Assert.NotEqual(staleWork.Attempt, resumed.Work.Attempt);
    }

    [Theory]
    [InlineData(NavigationScopeSettlementKind.Committed)]
    [InlineData(NavigationScopeSettlementKind.NoEffect)]
    [InlineData(NavigationScopeSettlementKind.Rejected)]
    [InlineData(NavigationScopeSettlementKind.Failed)]
    [InlineData(NavigationScopeSettlementKind.Cancelled)]
    [InlineData(NavigationScopeSettlementKind.Superseded)]
    public async Task ProtectedScope_ConsumesEveryCompleteCurrentSettlementSnapshot(
        NavigationScopeSettlementKind expected)
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync(
                selectPackage: false);
        NavigationState state = fixture.Session.State;
        WorkspaceScopeRequest request =
            IssueSettlement(fixture, expected);
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                state,
                request.Association);
        WorkspaceScopeOperationResult settlement =
            await SubmitSettlement(
                fixture,
                expected,
                request);
        NavigationScopeEvaluationRequest work = accepted.ScopeWork!;
        NavigationScopeEvaluationResult evaluated =
            NavigationTransitions.EvaluateScopeOperation(
                work,
                settlement,
                new NavigationScopePreparation.Ready(
                    new(
                        Current(settlement),
                        Package: null,
                        fixture.Availability),
                    new(
                        StructuralSubjectIdentity.ForWorkspace(
                            fixture.Workspace.Identity))),
                fixture.Registry);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                work,
                evaluated);

        Assert.Equal(
            expected,
            completed.Result!.Consumer.Outcome.Scope!.Kind);
        Assert.Equal(
            Current(settlement).Packages.Length,
            completed.Result.Consumer.Snapshot.Packages.Length);
        Assert.Equal(
            NavigationScopeSnapshotKind.Current,
            completed.Result.Consumer.Snapshot.Scope.Kind);
        Assert.Same(settlement, completed.Result.ScopeResult);
        Assert.True(
            NavigationTransitions.ValidateAuthority(
                completed.State,
                completed.Result.Consumer.Authority));
        if (expected == NavigationScopeSettlementKind.NoEffect)
        {
            Assert.Equal(
                state.Publication.Revision,
                completed.State.Publication.Revision);
            Assert.NotEqual(
                state.Publication.Generation,
                completed.State.Publication.Generation);
        }
        Assert.Equal(
            ExpectedOutcome(expected),
            completed.Result.Consumer.Outcome.Kind);
        if (expected == NavigationScopeSettlementKind.Rejected)
        {
            Assert.NotEqual(
                state.Snapshot.Packages.Length,
                completed.State.Snapshot.Packages.Length);
        }
    }

    [Fact]
    public async Task ProtectedScope_OnlyOriginalAssociationCanSettleAndRelease()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync(
                selectPackage: false);
        WorkspaceScopeRequest original =
            fixture.Workspace.IssueAddPackagesRequest(
                fixture.Scope.Revision,
                [fixture.Bindings[0]],
                Deadline);
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                fixture.Session.State,
                original.Association);
        NavigationTransition sameInput =
            NavigationTransitions.AcceptScopeOperation(
                fixture.Session.State,
                original.Association);
        WorkspaceScopeRequest foreign =
            fixture.Workspace.IssueAddPackagesRequest(
                fixture.Scope.Revision,
                [fixture.Bindings[1]],
                Deadline);
        WorkspaceScopeOperationResult foreignResult =
            await fixture.Workspace.SubmitScopeRequestAsync(
                foreign,
                TestContext.Current.CancellationToken);
        Assert.Throws<ArgumentException>(
            () => NavigationTransitions.EvaluateScopeOperation(
                accepted.ScopeWork!,
                foreignResult,
                new NavigationScopePreparation.Ready(
                    new(
                        Current(foreignResult),
                        Package: null,
                        fixture.Availability),
                    new(
                        StructuralSubjectIdentity.ForWorkspace(
                            fixture.Workspace.Identity))),
                fixture.Registry));
        Assert.NotNull(accepted.State.Data.ProtectedScope);

        WorkspaceScopeOperationResult originalResult =
            await fixture.Workspace.SubmitScopeRequestAsync(
                original,
                TestContext.Current.CancellationToken);
        NavigationScopeEvaluationResult evaluation =
            NavigationTransitions.EvaluateScopeOperation(
                accepted.ScopeWork!,
                originalResult,
                new NavigationScopePreparation.Ready(
                    new(
                        Current(originalResult),
                        Package: null,
                        fixture.Availability),
                    new(
                        StructuralSubjectIdentity.ForWorkspace(
                            fixture.Workspace.Identity))),
                fixture.Registry);
        NavigationTransition wrongAttempt =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                sameInput.ScopeWork!,
                evaluation);
        AssertRejected(
            accepted.State,
            wrongAttempt,
            NavigationCompletionRejection.WrongScopeAttempt);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                accepted.ScopeWork!,
                evaluation);
        NavigationTransition duplicate =
            NavigationTransitions.CompleteScopeOperation(
                completed.State,
                accepted.ScopeWork!,
                evaluation);
        AssertRejected(
            completed.State,
            duplicate,
            NavigationCompletionRejection.StaleAttempt);
    }

    [Fact]
    public async Task ProtectedScope_UnavailableRetainsHistoricalEvidenceWithoutCurrentAuthority()
    {
        InspectionWorkspace workspace = new();
        WorkspaceScopeSnapshot scope = Current(
            await workspace.GetScopeSnapshotAsync());
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot availability =
            NavigationSnapshotTestData.AllAvailable(registry);
        NavigationOperationInitialization initialized =
            NavigationTransitions.Initialize(
                workspace.Identity,
                new(scope, null, (_, _) => availability),
                registry);
        WorkspaceScopeRequest request =
            workspace.IssueClearScopeRequest(
                scope.Revision,
                Deadline);
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                initialized.State,
                request.Association);
        await workspace.DisposeAsync();
        var unavailable =
            Assert.IsType<WorkspaceScopeOperationResult.Unavailable>(
                await workspace.SubmitScopeRequestAsync(
                    request,
                    TestContext.Current.CancellationToken));
        NavigationScopeEvaluationResult evaluation =
            NavigationTransitions.EvaluateScopeOperation(
                accepted.ScopeWork!,
                unavailable,
                new NavigationScopePreparation.Historical(),
                registry);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                accepted.ScopeWork!,
                evaluation);

        Assert.Equal(
            NavigationScopeSnapshotKind.Historical,
            completed.State.Scope.Kind);
        Assert.Equal(
            ArtifactRootFailure.WorkspaceClosed,
            completed.State.Scope.RuntimeFailure);
        string json = JsonSerializer.Serialize(
            completed.Result!.Consumer,
            NavigationConsumerJsonContext.Default
                .NavigationConsumerResult);
        NavigationConsumerResult roundTrip =
            JsonSerializer.Deserialize(
                json,
                NavigationConsumerJsonContext.Default
                    .NavigationConsumerResult)!;
        Assert.Equal(
            completed.State.Scope,
            roundTrip.Snapshot.Scope);
        Assert.Equal(
            NavigationScopeSettlementKind.Unavailable,
            roundTrip.Outcome.Scope!.Kind);
        Assert.All(
            completed.State.Snapshot.Packages,
            row => Assert.Null(row.Action));
        NavigationTransition refused =
            NavigationTransitions.BeginLens(
                completed.State,
                new(
                    completed.State.InstalledSnapshot.ActiveSubject,
                    new ViewFacetId("workspace.overview")));
        AssertRefused(
            completed.State,
            refused,
            NavigationAdmissionRefusalKind.HistoricalScope);

        NavigationState released =
            NavigationTransitions.Abandon(
                completed.State,
                completed.Result.Consumer.Authority!).State;
        NavigationTransition queued =
            NavigationTransitions.QueueMaintenance(released);
        NavigationTransition refreshed =
            NavigationTransitions.Advance(queued.State);
        Assert.Equal(
            NavigationOutcomeKind.Unavailable,
            refreshed.Result!.Consumer.Outcome.Kind);
        Assert.Equal(
            NavigationScopeSnapshotKind.Historical,
            refreshed.Result.Consumer.Snapshot.Scope.Kind);
    }

    [Fact]
    public async Task ProtectedScope_CancellationControlCannotManufactureSettlement()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync(
                selectPackage: false);
        WorkspaceScopeCancellationAction? cancellation = null;
        Task<WorkspaceScopeCancellationResult>? cancellationTask = null;
        PackageRootBinding binding = Binding(
            "Navigation.Cancelled",
            onOpen: () =>
            {
                WorkspaceScopePreparationDescriptor preparing =
                    Assert.IsType<WorkspaceScopePreparationDescriptor>(
                        Current(
                            fixture.Workspace
                                .GetScopeSnapshotAsync()
                                .AsTask()
                                .GetAwaiter()
                                .GetResult()).Preparing);
                cancellation = preparing.Cancellation;
                cancellationTask =
                    fixture.Workspace
                        .CancelScopePreparationAsync(cancellation)
                        .AsTask();
            });
        WorkspaceScopeRequest request =
            fixture.Workspace.IssueReplaceScopeRequest(
                fixture.Scope.Revision,
                [binding],
                Deadline);
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                fixture.Session.State,
                request.Association);
        NavigationTransition localCancellation =
            NavigationTransitions.Cancel(
                accepted.State,
                accepted.ScopeWork!.Identity);
        AssertRejected(
            accepted.State,
            localCancellation,
            NavigationCompletionRejection.StaleAttempt);
        WorkspaceScopeOperationResult settlement =
            await fixture.Workspace.SubmitScopeRequestAsync(
                request,
                TestContext.Current.CancellationToken);
        Assert.NotNull(cancellationTask);
        var settled =
            Assert.IsType<WorkspaceScopeCancellationResult.Settled>(
                await cancellationTask);
        NavigationScopeCancellationObservation observation =
            NavigationTransitions.ObserveScopeCancellation(
                accepted.ScopeWork!,
                settled);
        Assert.Equal(
            NavigationScopeCancellationObservationKind
                .CorrelatedSettlement,
            observation.Kind);
        Assert.Same(settlement, observation.Settlement);
        Assert.Same(settled, observation.Control);
        Assert.NotNull(accepted.State.Data.ProtectedScope);

        var noEffect =
            Assert.IsType<WorkspaceScopeCancellationResult.ObservedNoEffect>(
                await fixture.Workspace.CancelScopePreparationAsync(
                    cancellation!));
        NavigationScopeCancellationObservation ignored =
            NavigationTransitions.ObserveScopeCancellation(
                accepted.ScopeWork!,
                noEffect);
        Assert.Equal(
            NavigationScopeCancellationObservationKind.NoSettlement,
            ignored.Kind);
        Assert.Null(ignored.Settlement);
        Assert.Same(noEffect, ignored.Control);
        Assert.NotNull(accepted.State.Data.ProtectedScope);

        await fixture.Workspace.DisposeAsync();
        var unavailable =
            Assert.IsType<WorkspaceScopeCancellationResult.Unavailable>(
                await fixture.Workspace.CancelScopePreparationAsync(cancellation!));
        NavigationScopeCancellationObservation unavailableObservation =
            NavigationTransitions.ObserveScopeCancellation(
                accepted.ScopeWork!, unavailable);
        Assert.Equal(NavigationScopeCancellationObservationKind.NoSettlement,
            unavailableObservation.Kind);
        Assert.Same(unavailable, unavailableObservation.Control);
        Assert.Null(unavailableObservation.Settlement);
    }

    [Fact]
    public async Task ProtectedScope_ExplicitDuplicateUsesExactRequestedOccurrence()
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding json =
            await packages.BindingAsync(
                "System.Text.Json",
                "10.0.0");
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                json);
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot availability =
            NavigationSnapshotTestData.AllAvailable(registry);
        NavigationOperationInitialization explicitInitial =
            NavigationTransitions.Initialize(
                workspace.Identity,
                new(scope, null, (_, _) => availability),
                registry);
        WorkspaceScopeRequest explicitRequest =
            workspace.IssueAddPackagesRequest(
                scope.Revision,
                [json],
                Deadline,
                workspace.CreateScopePackageTarget(json));
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                explicitInitial.State,
                explicitRequest.Association);
        var noEffect =
            Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
                await workspace.SubmitScopeRequestAsync(
                    explicitRequest,
                    TestContext.Current.CancellationToken));
        WorkspacePackageOccurrenceDescriptor requested =
            Assert.IsType<WorkspacePackageOccurrenceDescriptor>(
                noEffect.RequestedOccurrence);
        var ready =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                requested.Realization.Status);
        ArtifactRootResult<NavigationTransition> result =
            await workspace.ExecutePackageRootQueryAsync(
                (PackageArtifactRootCorrespondence)
                    requested.Occurrence.Correspondence,
                ready.Generation,
                (realization, cancellationToken) =>
                {
                    NavigationPackageEvaluation package =
                        NavigationPackageEvaluationFactory.Create(
                            requested,
                            json,
                            realization,
                            cancellationToken);
                    NavigationScopeEvaluationResult evaluation =
                        NavigationTransitions.EvaluateScopeOperation(
                            accepted.ScopeWork!,
                            noEffect,
                            new NavigationScopePreparation.Ready(
                                new(
                                    noEffect.Snapshot,
                                    package,
                                    (_, _) => availability)),
                            registry);
                    return ValueTask.FromResult(
                        NavigationTransitions.CompleteScopeOperation(
                            accepted.State,
                            accepted.ScopeWork!,
                            evaluation));
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);
        NavigationTransition completed =
            Assert.IsType<
                ArtifactRootResult<NavigationTransition>.Available>(
                    result).Value;
        Assert.Same(
            requested.Occurrence,
            completed.State.InstalledSnapshot.ActiveOccurrence);
        Assert.Equal(
            NavigationScopeSettlementKind.NoEffect,
            completed.Result!.Consumer.Outcome.Scope!.Kind);
        Assert.Equal(
            "System.Text.Json",
            completed.State.Snapshot.Packages.Single().PackageId,
            ignoreCase: true);

        NavigationOperationInitialization implicitInitial =
            NavigationTransitions.Initialize(
                workspace.Identity,
                new(scope, null, (_, _) => availability),
                registry);
        WorkspaceScopeRequest implicitRequest =
            workspace.IssueAddPackagesRequest(
                scope.Revision,
                [json],
                Deadline);
        NavigationTransition implicitAccepted =
            NavigationTransitions.AcceptScopeOperation(
                implicitInitial.State,
                implicitRequest.Association);
        WorkspaceScopeOperationResult implicitSettlement =
            await workspace.SubmitScopeRequestAsync(
                implicitRequest,
                TestContext.Current.CancellationToken);
        NavigationScopeEvaluationResult implicitEvaluation =
            NavigationTransitions.EvaluateScopeOperation(
                implicitAccepted.ScopeWork!,
                implicitSettlement,
                new NavigationScopePreparation.Ready(
                    new(
                        Current(implicitSettlement),
                        Package: null,
                        (_, _) => availability),
                    new(
                        StructuralSubjectIdentity.ForWorkspace(
                            workspace.Identity))),
                registry);
        NavigationTransition implicitCompleted =
            NavigationTransitions.CompleteScopeOperation(
                implicitAccepted.State,
                implicitAccepted.ScopeWork!,
                implicitEvaluation);
        Assert.Null(
            implicitCompleted.State.InstalledSnapshot.ActiveOccurrence);
        Assert.Equal(
            StructuralSubjectKind.Workspace,
            implicitCompleted.State.Snapshot.ActiveSubject.Kind);
    }

    [Fact]
    public async Task ProtectedScope_MembershipPreparationFailurePublishesCurrentFailure()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync(
                selectPackage: false);
        PackageRootBinding added = Binding("Navigation.New");
        WorkspaceScopeRequest request =
            fixture.Workspace.IssueAddPackagesRequest(
                fixture.Scope.Revision,
                [added],
                Deadline,
                fixture.Workspace.CreateScopePackageTarget(added));
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                fixture.Session.State,
                request.Association);
        var committed =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await fixture.Workspace.SubmitScopeRequestAsync(
                    request,
                    TestContext.Current.CancellationToken));
        NavigationScopeEvaluationResult evaluation =
            NavigationTransitions.EvaluateScopeOperation(
                accepted.ScopeWork!,
                committed,
                new NavigationScopePreparation.Failed(
                    "destination Type preparation failed"),
                fixture.Registry);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                accepted.ScopeWork!,
                evaluation);

        Assert.Equal(
            committed.Snapshot.Packages.Length,
            completed.State.Snapshot.Packages.Length);
        Assert.Equal(
            NavigationOutcomeKind.Failed,
            completed.Result!.Consumer.Outcome.Kind);
        Assert.Equal(
            NavigationFailureSource.Preparation,
            completed.Result.Consumer.Outcome.FailureSource);
        Assert.Equal(
            NavigationScopeSnapshotKind.Current,
            completed.State.Scope.Kind);
        Assert.Same(
            committed.RequestedOccurrence!.Occurrence,
            completed.State.InstalledSnapshot.ActiveOccurrence);
        Assert.Equal(
            StructuralSubjectKind.Workspace,
            completed.State.Snapshot.ActiveSubject.Kind);
        Assert.NotEqual(
            fixture.Session.Initialization.Authority!.Revision,
            completed.Result.Consumer.Authority!.Revision);
        NavigationTransition installed =
            NavigationTransitions.RecordConsumerInstallation(
                completed.State,
                completed.Result.Consumer.Authority);
        NavigationTransition acknowledged =
            NavigationTransitions.Acknowledge(
                installed.State,
                completed.Result.Consumer.Authority);
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            acknowledged.AuthorityResult);
        Assert.Equal(
            NavigationSynchronizationDisposition.Current,
            NavigationTransitions.Advance(
                NavigationTransitions.QueueSynchronization(
                    acknowledged.State).State)
                .Result!.Consumer.Synchronization);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedScope_DuplicateHonorsActivationWithRetainedWorkspaceContext(
        bool explicitActivation)
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync();
        NavigationAction workspaceAction = fixture.Session.Snapshot.Hierarchy
            .Single(row => row.Kind == StructuralSubjectKind.Workspace).Action!;
        await fixture.Session.ExecuteAsync(
            workspaceAction, TestContext.Current.CancellationToken);
        NavigationState initial = fixture.Session.State;
        Assert.Equal(StructuralSubjectKind.Workspace, initial.Snapshot.ActiveSubject.Kind);
        Assert.NotNull(initial.InstalledSnapshot.ActiveOccurrence);

        WorkspaceScopeRequest request = fixture.Workspace.IssueAddPackagesRequest(
            fixture.Scope.Revision,
            [fixture.Bindings[0]],
            Deadline,
            explicitActivation
                ? fixture.Workspace.CreateScopePackageTarget(fixture.Bindings[0])
                : null);
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(initial, request.Association);
        var settlement = Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
            await fixture.Workspace.SubmitScopeRequestAsync(
                request, TestContext.Current.CancellationToken));
        NavigationPackageEvaluation package = Rebind(
            fixture.Packages[0],
            settlement.Snapshot.Packages.Single(row =>
                row.Occurrence == initial.InstalledSnapshot.ActiveOccurrence),
            fixture.Bindings[0]);
        NavigationScopeEvaluationResult evaluation =
            NavigationTransitions.EvaluateScopeOperation(
                accepted.ScopeWork!,
                settlement,
                new NavigationScopePreparation.Ready(
                    new(settlement.Snapshot, package, fixture.Availability)),
                fixture.Registry);
        NavigationTransition completed = NavigationTransitions.CompleteScopeOperation(
            accepted.State, accepted.ScopeWork!, evaluation);

        Assert.Same(initial.InstalledSnapshot.ActiveOccurrence,
            completed.State.InstalledSnapshot.ActiveOccurrence);
        Assert.Equal(explicitActivation
                ? StructuralSubjectKind.Library
                : StructuralSubjectKind.Workspace,
            completed.State.Snapshot.ActiveSubject.Kind);
    }

    [Fact]
    public async Task ProtectedScope_RetainsUnavailableExactInspectorRequest()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync();
        NavigationAction metadata = fixture.Session.Snapshot.Lenses
            .Single(row => row.Facet.Id == "library.metadata").Action!;
        fixture.Override = id => id.Value == "library.metadata"
            ? new ViewFacetAvailability.Unavailable(
                ViewFacetUnavailableReason.CapabilityAbsent("metadata unavailable"))
            : null;
        NavigationConsumerResult unavailable = await fixture.Session.ExecuteAsync(
            metadata, TestContext.Current.CancellationToken);
        Assert.Null(unavailable.Snapshot.LensOutcome.EffectiveLens);
        Assert.Equal("library.metadata", unavailable.Snapshot.LensOutcome.Request!.Facet);

        WorkspaceScopeRequest request = fixture.Workspace.IssueAddPackagesRequest(
            fixture.Scope.Revision, [fixture.Bindings[0]], Deadline);
        NavigationTransition accepted = NavigationTransitions.AcceptScopeOperation(
            fixture.Session.State, request.Association);
        var settlement = Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
            await fixture.Workspace.SubmitScopeRequestAsync(
                request, TestContext.Current.CancellationToken));
        fixture.Override = null;
        NavigationPackageEvaluation package = Rebind(
            fixture.Packages[0],
            settlement.Snapshot.Packages.Single(row =>
                row.Occurrence == fixture.Packages[0].Occurrence.Occurrence),
            fixture.Bindings[0]);
        NavigationScopeEvaluationResult evaluation =
            NavigationTransitions.EvaluateScopeOperation(
                accepted.ScopeWork!,
                settlement,
                new NavigationScopePreparation.Ready(
                    new(settlement.Snapshot, package, fixture.Availability)),
                fixture.Registry);
        NavigationTransition completed = NavigationTransitions.CompleteScopeOperation(
            accepted.State, accepted.ScopeWork!, evaluation);

        Assert.Equal(NavigationLensBasisKind.ExactRequest,
            completed.State.Snapshot.LensOutcome.Basis);
        Assert.Equal("library.metadata",
            completed.State.Snapshot.LensOutcome.EffectiveLens!.Facet);
    }

    [Fact]
    public async Task ProtectedScope_NoEffectConsumesNewerInventoryThanNavigation()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync(selectPackage: false);
        NavigationState initial = fixture.Session.State;
        WorkspaceScopeSnapshot changed = Current(
            await fixture.Workspace.AddPackagesAsync(
                fixture.Scope.Revision,
                [Binding("Navigation.Additional")],
                Deadline,
                TestContext.Current.CancellationToken));
        WorkspaceScopeRequest request = fixture.Workspace.IssueAddPackagesRequest(
            changed.Revision, [fixture.Bindings[0]], Deadline);
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(initial, request.Association);
        var settlement = Assert.IsType<WorkspaceScopeOperationResult.NoEffect>(
            await fixture.Workspace.SubmitScopeRequestAsync(
                request, TestContext.Current.CancellationToken));
        NavigationScopeEvaluationResult evaluation =
            NavigationTransitions.EvaluateScopeOperation(
                accepted.ScopeWork!,
                settlement,
                new NavigationScopePreparation.Ready(
                    new(settlement.Snapshot, null, fixture.Availability)),
                fixture.Registry);
        NavigationTransition completed = NavigationTransitions.CompleteScopeOperation(
            accepted.State, accepted.ScopeWork!, evaluation);

        Assert.Equal(initial.Snapshot.Packages.Length + 1,
            completed.State.Snapshot.Packages.Length);
        Assert.Contains(completed.State.Snapshot.Packages,
            row => row.PackageId == "Navigation.Additional");
        Assert.NotEqual(initial.Publication.Revision, completed.State.Publication.Revision);
        Assert.Equal(NavigationScopeSettlementKind.NoEffect,
            completed.Result!.Consumer.Outcome.Scope!.Kind);
    }

    [Fact]
    public async Task ProtectedScope_RetainedStateAndResultsDoNotRetainInvocationAuthority()
    {
        ProtectedSpecimen specimen = await CreateProtectedSpecimen();
        for (int attempt = 0;
            attempt < 10
                && specimen.References.Any(
                    reference => reference.IsAlive);
            attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Yield();
        }

        Assert.All(
            specimen.References,
            reference => Assert.False(reference.IsAlive));
        Assert.Equal(
            NavigationScopeSettlementKind.Committed,
            specimen.Completed.Result!.Consumer.Outcome.Scope!.Kind);
        Assert.True(
            NavigationTransitions.ValidateAuthority(
                specimen.Completed.State,
                specimen.Completed.Result.Consumer.Authority));
        GC.KeepAlive(specimen);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<ProtectedSpecimen> CreateProtectedSpecimen()
    {
        InspectionWorkspace workspace = new();
        WorkspaceScopeSnapshot scope = Current(
            await workspace.GetScopeSnapshotAsync());
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot availability =
            NavigationSnapshotTestData.AllAvailable(registry);
        NavigationOperationInitialization initialized =
            NavigationTransitions.Initialize(
                workspace.Identity,
                new(scope, null, (_, _) => availability),
                registry);
        object callback = new();
        PackageRootBinding binding = Binding(
            "Navigation.Detachment",
            onOpen: () => GC.KeepAlive(callback));
        WorkspaceScopeRequest request =
            workspace.IssueAddPackagesRequest(
                scope.Revision,
                [binding],
                Deadline);
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                initialized.State,
                request.Association);
        WorkspaceScopeOperationResult settlement =
            await workspace.SubmitScopeRequestAsync(
                request,
                TestContext.Current.CancellationToken);
        NavigationScopeEvaluationResult evaluation =
            NavigationTransitions.EvaluateScopeOperation(
                accepted.ScopeWork!,
                settlement,
                new NavigationScopePreparation.Ready(
                    new(
                        Current(settlement),
                        Package: null,
                        (_, _) => availability),
                    new(
                        StructuralSubjectIdentity.ForWorkspace(
                            workspace.Identity))),
                registry);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                accepted.ScopeWork!,
                evaluation);
        WeakReference[] references =
        [
            new(request),
            new(binding),
            new(binding.Root.Content),
            new(callback),
        ];
        await workspace.DisposeAsync();
        return new(completed, references);
    }

    static WorkspaceScopeRequest IssueSettlement(
        NavigationSessionTests.Fixture fixture,
        NavigationScopeSettlementKind kind)
    {
        return kind switch
        {
            NavigationScopeSettlementKind.Committed =>
                fixture.Workspace.IssueAddPackagesRequest(
                    fixture.Scope.Revision,
                    [Binding("Navigation.Committed")],
                    Deadline),
            NavigationScopeSettlementKind.NoEffect =>
                fixture.Workspace.IssueAddPackagesRequest(
                    fixture.Scope.Revision,
                    [fixture.Bindings[0]],
                    Deadline),
            NavigationScopeSettlementKind.Rejected =>
                fixture.Workspace.IssueAddPackagesRequest(
                    fixture.Scope.Revision,
                    [Binding("Navigation.Rejected")],
                    Deadline),
            NavigationScopeSettlementKind.Failed =>
                fixture.Workspace.IssueReplaceScopeRequest(
                    fixture.Scope.Revision,
                    [NavigationSnapshotTestData
                        .BindingWithAssemblyImages(
                            "Navigation.Failed",
                            "net11.0",
                            ("Broken", [1, 2, 3]))],
                    Deadline),
            NavigationScopeSettlementKind.Cancelled =>
                fixture.Workspace.IssueAddPackagesRequest(
                    fixture.Scope.Revision,
                    [Binding("Navigation.Cancelled")],
                    Deadline),
            NavigationScopeSettlementKind.Superseded =>
                fixture.Workspace.IssueReplaceScopeRequest(
                    fixture.Scope.Revision,
                    [
                        Binding(
                            "Navigation.Displaced",
                            onOpen: () =>
                            {
                                WorkspaceScopeRequest winnerRequest =
                                    fixture.Workspace
                                        .IssueClearScopeRequest(
                                            fixture.Scope.Revision,
                                                Deadline);
                                Assert.IsType<
                                    WorkspaceScopeOperationResult.Committed>(
                                            fixture.Workspace
                                            .SubmitScopeRequestAsync(
                                                winnerRequest,
                                                TestContext.Current
                                                    .CancellationToken)
                                            .AsTask()
                                            .GetAwaiter()
                                            .GetResult());
                            }),
                    ],
                    Deadline),
            _ => throw new InvalidOperationException(
                "Unavailable is tested through the historical path."),
        };
    }

    static async Task<WorkspaceScopeOperationResult>
        SubmitSettlement(
            NavigationSessionTests.Fixture fixture,
            NavigationScopeSettlementKind kind,
            WorkspaceScopeRequest request)
    {
        if (kind == NavigationScopeSettlementKind.Rejected)
        {
            WorkspaceScopeOperationResult changed =
                await fixture.Workspace.AddPackagesAsync(
                    fixture.Scope.Revision,
                    [Binding("Navigation.Concurrent")],
                    Deadline,
                    TestContext.Current.CancellationToken);
            Assert.IsType<
                WorkspaceScopeOperationResult.Committed>(
                    changed);
        }
        if (kind == NavigationScopeSettlementKind.Cancelled)
        {
            using var cancellation =
                new CancellationTokenSource();
            await cancellation.CancelAsync();
            return await fixture.Workspace.SubmitScopeRequestAsync(
                request,
                cancellation.Token);
        }

        WorkspaceScopeOperationResult result =
            await fixture.Workspace.SubmitScopeRequestAsync(
                request,
                TestContext.Current.CancellationToken);
        if (kind == NavigationScopeSettlementKind.Superseded)
        {
            var superseded =
                Assert.IsType<
                    WorkspaceScopeOperationResult.Superseded>(
                        result);
            Assert.NotSame(
                superseded.Operation,
                superseded.SupersedingOperation);
        }
        return result;
    }

    static WorkspaceScopeSnapshot Current(
        WorkspaceScopeOperationResult result) =>
        result switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                noEffect.Snapshot,
            WorkspaceScopeOperationResult.Rejected rejected =>
                rejected.Snapshot,
            WorkspaceScopeOperationResult.Failed failed =>
                failed.Snapshot,
            WorkspaceScopeOperationResult.Cancelled cancelled =>
                cancelled.Snapshot,
            WorkspaceScopeOperationResult.Superseded superseded =>
                superseded.Snapshot,
            _ => throw new InvalidOperationException(
                "The result is not a current Scope snapshot."),
        };

    static NavigationOutcomeKind ExpectedOutcome(
        NavigationScopeSettlementKind kind) =>
        kind switch
        {
            NavigationScopeSettlementKind.Committed
                or NavigationScopeSettlementKind.NoEffect =>
                    NavigationOutcomeKind.Applied,
            NavigationScopeSettlementKind.Rejected =>
                NavigationOutcomeKind.Rejected,
            NavigationScopeSettlementKind.Failed =>
                NavigationOutcomeKind.Failed,
            NavigationScopeSettlementKind.Cancelled =>
                NavigationOutcomeKind.Aborted,
            NavigationScopeSettlementKind.Superseded =>
                NavigationOutcomeKind.Superseded,
            _ => throw new InvalidOperationException(),
        };

    static WorkspaceScopeSnapshot Current(
        WorkspaceScopeReadResult result) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(
            result).Snapshot;

    static NavigationPackageEvaluation Rebind(
        NavigationPackageEvaluation package,
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding) =>
        new(
            occurrence,
            binding,
            package.Libraries,
            package.Surface);

    static NavigationState Acknowledge(
        NavigationState state,
        NavigationEffectAuthority authority)
    {
        NavigationTransition installed =
            NavigationTransitions.RecordConsumerInstallation(
                state,
                authority);
        return NavigationTransitions.Acknowledge(
            installed.State,
            authority).State;
    }

    static void AssertRefused(
        NavigationState state,
        NavigationTransition transition,
        NavigationAdmissionRefusalKind kind)
    {
        Assert.Same(state, transition.State);
        Assert.Equal(kind, transition.AdmissionRefusal!.Kind);
        Assert.Null(transition.Request);
        Assert.Null(transition.Work);
        Assert.Null(transition.ScopeWork);
        Assert.Null(transition.Result);
        Assert.Null(transition.AuthorityResult);
    }

    static void AssertRejected(
        NavigationState state,
        NavigationTransition transition,
        NavigationCompletionRejection rejection)
    {
        Assert.Same(state, transition.State);
        Assert.Equal(rejection, transition.Rejection);
        Assert.Null(transition.Result);
        Assert.Null(transition.AuthorityResult);
    }

    static PackageRootBinding Binding(
        string packageId,
        Action? onOpen = null)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
            bytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using Stream destination = archive.CreateEntry(
                $"lib/net11.0/{packageId}.dll").Open();
            destination.Write(
                File.ReadAllBytes(
                    typeof(AssemblyReferenceIdentity).Assembly.Location));
        }
        IPackageContent content = new InMemoryPackageContent(
            bytes.ToArray(),
            fromCache: false,
            producerKey: "tests");
        if (onOpen is not null)
            content = new CallbackPackageContent(content, onOpen);
        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(packageId, "1.0.0"),
                content,
                "tests",
                PackagePayloadOrigin.Download),
            "net11.0",
            displayPackageId: packageId);
    }

    sealed class CallbackPackageContent(
        IPackageContent inner,
        Action onOpen)
        : IPackageContent
    {
        Action? _onOpen = onOpen;

        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(relativePath, out stream);
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(
                relativePath,
                maxExpandedBytes,
                out stream);
        }

        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();
    }

    sealed record ProtectedSpecimen(
        NavigationTransition Completed,
        IReadOnlyList<WeakReference> References);
}
