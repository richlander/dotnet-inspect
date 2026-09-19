using System.Collections.Immutable;
using System.Text.Json;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed partial class NavigationSessionTests
{
    [Theory]
    [InlineData(NavigationOutcomeKind.Unavailable)]
    [InlineData(NavigationOutcomeKind.Failed)]
    [InlineData(NavigationOutcomeKind.Aborted)]
    public async Task PreparationNonSuccess_ReturnsFreshRetryWithoutSemanticRevisionChange(NavigationOutcomeKind kind)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        fixture.Acknowledge(session.Initialization);
        NavigationWorkspaceSnapshot current = session.CurrentSnapshot;
        NavigationAction original = session.Snapshot.Types[0].Navigation.Action!;
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(kind switch
        {
            NavigationOutcomeKind.Unavailable => new NavigationPreparation.Unavailable("not ready"),
            NavigationOutcomeKind.Failed => new NavigationPreparation.Failed("preparation failed"),
            _ => new NavigationPreparation.Aborted("aborted"),
        });
        NavigationConsumerResult result = await session.ExecuteAsync(original, TestContext.Current.CancellationToken);
        NavigationAction retry = result.Snapshot.Types[0].Navigation.Action!;
        Assert.Equal(kind, result.Outcome.Kind);
        Assert.NotEqual(original.Id, retry.Id);
        Assert.NotEqual(original.Generation, retry.Generation);
        Assert.Same(current, session.CurrentSnapshot);
        Assert.Equal(session.Initialization.Authority!.Revision, result.Authority!.Revision);
        Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired, result.Synchronization);
        Assert.Equal(NavigationAuthorityResult.PostingRequired, session.Acknowledge(result.Authority));
        fixture.Acknowledge(result);

        fixture.Prepare = _ => throw new InvalidOperationException("A duplicate must not gather facts.");
        NavigationConsumerResult duplicate = await session.ExecuteAsync(original, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationRejectionKind.DuplicateAction, duplicate.Outcome.Rejection);
        Assert.Same(result.Snapshot, duplicate.Snapshot);
        Assert.Equal(NavigationSynchronizationDisposition.Current, duplicate.Synchronization);
        fixture.Prepare = null;
        NavigationConsumerResult applied = await session.ExecuteAsync(retry, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Applied, applied.Outcome.Kind);
        Assert.Equal(retry.Source, result.Snapshot.ActiveSubject.Id);
        Assert.Equal(result.Snapshot.Types[0].Navigation.Subject!.Id, applied.Snapshot.ActiveSubject.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DescendantNonSuccess_RenewsExactPairForRetry(bool failed)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationAction original = session.Snapshot.Types[0].DescendantLenses
            .Single(row => row.Facet.Id == "type.compare").Action!;
        ViewFacetAvailability status = failed
            ? new ViewFacetAvailability.Failed("compare failed", new Diagnostic())
            : new ViewFacetAvailability.Unavailable(ViewFacetUnavailableReason.CapabilityAbsent("compare absent"));
        fixture.Override = id => id.Value == "type.compare" ? status : null;
        NavigationConsumerResult result = await session.ExecuteAsync(original, TestContext.Current.CancellationToken);
        NavigationAction retry = result.Snapshot.Types[0].DescendantLenses
            .Single(row => row.Facet.Id == "type.compare").Action!;
        Assert.NotEqual(original.Id, retry.Id);
        Assert.Equal(original.Source, retry.Source);
        Assert.Equal(session.Initialization.Authority!.Revision, result.Authority!.Revision);
        Assert.Equal(session.Initialization.Snapshot.ActiveSubject, result.Snapshot.ActiveSubject);
        fixture.Override = null;
        NavigationConsumerResult applied = await session.ExecuteAsync(retry, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Applied, applied.Outcome.Kind);
        Assert.Equal(result.Outcome.Request!.Lens, applied.Snapshot.LensOutcome.EffectiveLens);
        NavigationConsumerResult duplicate = await session.ExecuteAsync(original, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationRejectionKind.DuplicateAction, duplicate.Outcome.Rejection);
    }

    [Theory]
    [InlineData(NavigationOutcomeKind.Unavailable)]
    [InlineData(NavigationOutcomeKind.Failed)]
    [InlineData(NavigationOutcomeKind.Aborted)]
    public async Task LensPreparationNonSuccess_RenewsAdvertisedLensAction(NavigationOutcomeKind kind)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationAction original = session.Snapshot.Lenses.Single(row => row.Facet.Id == "library.metadata").Action!;
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(kind switch
        {
            NavigationOutcomeKind.Unavailable => new NavigationPreparation.Unavailable("unavailable"),
            NavigationOutcomeKind.Failed => new NavigationPreparation.Failed("failed"),
            _ => new NavigationPreparation.Aborted("aborted"),
        });
        NavigationConsumerResult result = await session.ExecuteAsync(original, TestContext.Current.CancellationToken);
        NavigationAction retry = result.Snapshot.Lenses.Single(row => row.Facet.Id == "library.metadata").Action!;
        Assert.NotEqual(original.Id, retry.Id);
        Assert.Equal("library.metadata", result.Outcome.Request!.Lens!.Facet);
        Assert.Equal(session.Initialization.Authority!.Revision, result.Authority!.Revision);
        fixture.Prepare = null;
        NavigationConsumerResult applied = await session.ExecuteAsync(retry, TestContext.Current.CancellationToken);
        Assert.Equal("library.metadata", applied.Snapshot.LensOutcome.EffectiveLens!.Facet);
        Assert.Equal(NavigationLensBasisKind.ExactRequest, applied.Snapshot.LensOutcome.Basis);
    }

    [Fact]
    public async Task NonReplacingLensRejection_RenewsOriginalExactActionWithoutRegistryRediscovery()
    {
        bool applicable = true;
        bool throwOnDiscovery = false;
        var references = new ViewFacetDescriptor(
            new ViewFacetId("library.references"), StructuralSubjectKind.Library,
            "References", "Library references", 0, ViewFacetRole.LibraryReferences);
        var metadata = new ViewFacetDescriptor(
            new ViewFacetId("library.metadata"), StructuralSubjectKind.Library,
            "Metadata", "Library metadata", 1);
        var first = new ViewFacetRegistration.Active(
            references, references.Summary, target =>
            {
                if (throwOnDiscovery)
                    throw new InvalidOperationException("Renewal must not re-evaluate Registry options.");
                return target.Subject.Kind == StructuralSubjectKind.Library;
            },
            new ViewFacetExecutionBinding(references.Id, references), (_, facts) => facts.Get(references.Id));
        var second = new ViewFacetRegistration.Active(
            metadata, metadata.Summary,
            target => applicable && target.Subject.Kind == StructuralSubjectKind.Library,
            new ViewFacetExecutionBinding(metadata.Id, metadata), (_, facts) => facts.Get(metadata.Id));
        var registry = new ViewFacetRegistry([first, second], [first.Binding, second.Binding]);
        await using Fixture fixture = await Fixture.CreateAsync(registry: registry);
        NavigationTestHost session = fixture.Session;
        NavigationAction original = session.Snapshot.Lenses.Single(row => row.Facet.Id == metadata.Id.Value).Action!;
        applicable = false;
        NavigationConsumerResult rejected = await session.ExecuteAsync(original, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationResolutionKind.Inapplicable, rejected.Outcome.Resolution!.Kind);
        Assert.Equal(session.Initialization.Authority!.Revision, rejected.Authority!.Revision);
        NavigationAction retry = rejected.Snapshot.Lenses.Single(row => row.Facet.Id == metadata.Id.Value).Action!;
        Assert.NotEqual(original.Id, retry.Id);
        throwOnDiscovery = true;
        NavigationConsumerResult duplicate = await session.ExecuteAsync(original, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationRejectionKind.DuplicateAction, duplicate.Outcome.Rejection);
        Assert.Same(rejected.Snapshot, duplicate.Snapshot);
        throwOnDiscovery = false;
        applicable = true;
        NavigationConsumerResult applied = await session.ExecuteAsync(retry, TestContext.Current.CancellationToken);
        Assert.Equal(metadata.Id.Value, applied.Snapshot.LensOutcome.EffectiveLens!.Facet);
    }

    [Fact]
    public async Task RetryGenerationDebt_SurvivesAbandonmentWithoutSemanticRevisionChange()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        fixture.Acknowledge(session.Initialization);
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
            new NavigationPreparation.Unavailable("retry later"));
        NavigationConsumerResult result = await session.ExecuteAsync(
            session.Snapshot.Types[0].Navigation.Action!, TestContext.Current.CancellationToken);
        Assert.Equal(session.Initialization.Authority!.Revision, result.Authority!.Revision);
        session.RecordConsumerPosting(result.Authority);
        session.Abandon(result.Authority);
        NavigationConsumerResult sync = await session.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired, sync.Synchronization);
        Assert.Same(result.Snapshot, sync.Snapshot);
        fixture.Acknowledge(sync);
        NavigationConsumerResult current = await session.SynchronizeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(NavigationSynchronizationDisposition.Current, current.Synchronization);
    }

    [Theory]
    [InlineData(StructuralSubjectKind.Workspace)]
    [InlineData(StructuralSubjectKind.Package)]
    [InlineData(StructuralSubjectKind.Library)]
    [InlineData(StructuralSubjectKind.Type)]
    [InlineData(StructuralSubjectKind.Member)]
    public async Task NonReadyActiveOccurrence_PreservesExactContextAndSuspendsActions(StructuralSubjectKind kind)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationConsumerResult selected = await session.ExecuteAsync(
            session.Snapshot.Members[1].Navigation.Action!, TestContext.Current.CancellationToken);
        if (kind != StructuralSubjectKind.Member)
        {
            selected = await session.ExecuteAsync(
                session.Snapshot.Hierarchy.Single(row => row.Kind == kind).Action!,
                TestContext.Current.CancellationToken);
        }
        NavigationWorkspaceSnapshot current = session.CurrentSnapshot;
        NavigationAction stale = session.Snapshot.Types[0].Navigation.Action!;
        fixture.Acknowledge(selected);
        foreach (ArtifactRootRealizationStatus status in new ArtifactRootRealizationStatus[]
        {
            new ArtifactRootRealizationStatus.Pending(),
            new ArtifactRootRealizationStatus.Failed(ArtifactRootFailure.PreparationFailed),
        })
        {
            WorkspaceScopeSnapshot scope = WithStatus(fixture.Scope, status);
            var evaluation = new NavigationNonReadyPackageEvaluation(scope.Packages[0]);
            fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
                new NavigationPreparation.Ready(new(
                    scope, null, ThrowingAvailability, evaluation)));
            NavigationConsumerResult refreshed = await session.RefreshAsync(TestContext.Current.CancellationToken);
            string json = JsonSerializer.Serialize(refreshed, NavigationConsumerJsonContext.Default.NavigationConsumerResult);
            NavigationConsumerResult roundTrip = JsonSerializer.Deserialize(
                json, NavigationConsumerJsonContext.Default.NavigationConsumerResult)!;
            Assert.Equal(refreshed.Snapshot.LensOutcome.Suspension, roundTrip.Snapshot.LensOutcome.Suspension);
            Assert.Same(current.ActiveOccurrence, session.CurrentSnapshot.ActiveOccurrence);
            Assert.Same(current.ActiveSubject, session.CurrentSnapshot.ActiveSubject);
            Assert.Same(current.RetainedContext, session.CurrentSnapshot.RetainedContext);
            Assert.Same(current.TypeInventoryLibraryContext, session.CurrentSnapshot.TypeInventoryLibraryContext);
            Assert.Same(status, session.CurrentSnapshot.Scope.Packages[0].Realization.Status);
            Assert.IsNotType<ArtifactRootRealizationStatus.Ready>(
                session.CurrentSnapshot.Packages[0].Realization);
            Assert.Null(refreshed.Snapshot.Packages[0].Action);
            NavigationDescriptorState expected = status is ArtifactRootRealizationStatus.Pending
                ? NavigationDescriptorState.Pending : NavigationDescriptorState.Failed;
            Assert.Equal(expected, refreshed.Snapshot.Packages[0].State);
            Assert.All(refreshed.Snapshot.Hierarchy.Where(row => row.Kind != StructuralSubjectKind.Workspace), row =>
            {
                Assert.Equal(expected, row.State);
                Assert.Null(row.Action);
            });
            Assert.All(refreshed.Snapshot.Libraries, row => Assert.Null(row.Navigation.Action));
            Assert.All(refreshed.Snapshot.Types, row =>
            {
                Assert.Null(row.Navigation.Action);
                Assert.Empty(row.DescendantLenses);
            });
            Assert.All(refreshed.Snapshot.Members, row =>
            {
                Assert.Null(row.Navigation.Action);
                Assert.Empty(row.DescendantLenses);
            });
            if (kind != StructuralSubjectKind.Workspace)
            {
                Assert.IsType<NavigationLensOutcome.Suspended>(session.CurrentSnapshot.LensOutcome);
                Assert.Null(refreshed.Snapshot.LensOutcome.EffectiveLens);
                Assert.NotNull(refreshed.Snapshot.LensOutcome.Suspension);
                Assert.All(refreshed.Snapshot.Lenses, row =>
                {
                    Assert.Equal(expected, row.State);
                    Assert.Null(row.Target);
                    Assert.Null(row.Action);
                });
            }
            Assert.NotEqual(selected.Authority!.Revision, refreshed.Authority!.Revision);
            Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired, refreshed.Synchronization);
            fixture.Acknowledge(refreshed);
            NavigationConsumerResult equivalent = await session.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(refreshed.Authority.Revision, equivalent.Authority!.Revision);
            fixture.Acknowledge(equivalent);
        }
        NavigationConsumerResult rejected = await session.ExecuteAsync(stale, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationRejectionKind.StaleGeneration, rejected.Outcome.Rejection);
        fixture.Acknowledge(rejected);
        fixture.Prepare = null;
        NavigationConsumerResult resumed = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(selected.Snapshot.ActiveSubject, resumed.Snapshot.ActiveSubject);
        Assert.Equal(selected.Snapshot.Hierarchy[4].Subject, resumed.Snapshot.Hierarchy[4].Subject);
        Assert.Equal(selected.Snapshot.LensOutcome, resumed.Snapshot.LensOutcome);
        Assert.NotNull(resumed.Snapshot.Types[0].Navigation.Action);

        static IViewFacetAvailabilityFacts ThrowingAvailability(
            StructuralSubjectIdentity subject, NavigationSubjectInventory? inventory) =>
            throw new InvalidOperationException("Non-ready refresh must not reuse retired Registry or inventory facts.");
    }

    [Fact]
    public async Task NonReadyOccurrence_RejectsReadyForeignAndMembershipChangingInputs()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        await using Fixture foreign = await Fixture.CreateAsync();
        Assert.Throws<ArgumentException>(() => new NavigationNonReadyPackageEvaluation(fixture.Scope.Packages[0]));
        fixture.Acknowledge(fixture.Session.Initialization);
        WorkspaceScopeSnapshot foreignScope = WithStatus(foreign.Scope, new ArtifactRootRealizationStatus.Pending());
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
            new NavigationPreparation.Ready(new(
                foreignScope, null, fixture.Availability, new(foreignScope.Packages[0]))));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Session.RefreshAsync(TestContext.Current.CancellationToken).AsTask());
        WorkspaceScopeSnapshot nonReady = WithStatus(fixture.Scope, new ArtifactRootRealizationStatus.Pending());
        var revision = new WorkspaceScopeRevision(fixture.Workspace.Identity, [nonReady.Packages[0].Occurrence]);
        var changed = new WorkspaceScopeSnapshot(
            revision, nonReady.PhysicalComposition, [nonReady.Packages[0]],
            new WorkspaceClosureObservation(revision.Identity), null);
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
            new NavigationPreparation.Ready(new(
                changed, null, fixture.Availability, new(changed.Packages[0]))));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Session.RefreshAsync(TestContext.Current.CancellationToken).AsTask());
        Assert.Same(fixture.Scope, fixture.Session.CurrentSnapshot.Scope);
    }

    [Fact]
    public async Task NonReadyOccurrence_WorkspaceRemainsReachableAndExactBasisRecovers()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationConsumerResult exact = await session.ActivateLensAsync(
            new(session.CurrentSnapshot.ActiveSubject, new ViewFacetId("library.metadata")),
            TestContext.Current.CancellationToken);
        fixture.Acknowledge(exact);
        WorkspaceScopeSnapshot scope = WithStatus(fixture.Scope, new ArtifactRootRealizationStatus.Pending());
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
            new NavigationPreparation.Ready(new(scope, null, fixture.Availability, new(scope.Packages[0]))));
        NavigationConsumerResult suspended = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal("library.metadata", suspended.Snapshot.LensOutcome.Request!.Facet);
        fixture.Acknowledge(suspended);
        fixture.Prepare = null;
        NavigationConsumerResult resumed = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(exact.Snapshot.LensOutcome, resumed.Snapshot.LensOutcome);
        fixture.Acknowledge(resumed);
        fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
            new NavigationPreparation.Ready(new(scope, null, fixture.Availability, new(scope.Packages[0]))));
        suspended = await session.RefreshAsync(TestContext.Current.CancellationToken);
        NavigationConsumerResult workspace = await session.ExecuteAsync(
            suspended.Snapshot.Hierarchy[0].Action!, TestContext.Current.CancellationToken);
        Assert.Equal(StructuralSubjectKind.Workspace, workspace.Snapshot.ActiveSubject.Kind);
        Assert.Equal(suspended.Snapshot.ActivePackage, workspace.Snapshot.ActivePackage);
        Assert.Equal(NavigationDescriptorState.Pending, workspace.Snapshot.Packages[0].State);
        Assert.Null(workspace.Snapshot.Hierarchy[1].Action);
        Assert.Null(workspace.Snapshot.LensOutcome.Suspension);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ExactNonSuccessDuringSubjectReconciliation_PreservesRegistryEvidence(
        bool failed, bool occurrenceRemoved)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        await session.ExecuteAsync(session.Snapshot.Types[1].Navigation.Action!, TestContext.Current.CancellationToken);
        StructuralSubjectIdentity source = session.CurrentSnapshot.ActiveSubject;
        var request = new NavigationLensIdentity(source, new ViewFacetId("type.compare"));
        var evidence = new Diagnostic();
        var reason = ViewFacetUnavailableReason.CapabilityAbsent("original exact request unavailable");
        ViewFacetAvailability availability = failed
            ? new ViewFacetAvailability.Failed("original exact Registry failure", evidence)
            : new ViewFacetAvailability.Unavailable(reason);
        fixture.Override = id => id == request.Facet ? availability : null;
        if (occurrenceRemoved)
        {
            WorkspacePackageOccurrenceDescriptor remaining = fixture.Scope.Packages[1];
            var revision = new WorkspaceScopeRevision(fixture.Workspace.Identity, [remaining.Occurrence]);
            var scope = new WorkspaceScopeSnapshot(
                revision, fixture.Scope.PhysicalComposition, [remaining],
                new WorkspaceClosureObservation(revision.Identity), null);
            fixture.Prepare = _ => ValueTask.FromResult<NavigationPreparation>(
                new NavigationPreparation.Ready(new(scope, null, fixture.Availability)));
        }
        else
        {
            fixture.SetTypes(0, [NavigationSnapshotTestData.Type("Widget")], []);
        }
        NavigationOperationResult operation = await session.ActivateLensOperationAsync(request, TestContext.Current.CancellationToken);
        NavigationConsumerResult result = operation.Consumer;
        Assert.Equal(failed ? NavigationOutcomeKind.Failed : NavigationOutcomeKind.Unavailable, result.Outcome.Kind);
        Assert.Equal(failed ? NavigationResolutionKind.Failed : NavigationResolutionKind.Unavailable,
            result.Outcome.Resolution!.Kind);
        Assert.Equal(request.Facet.Value, result.Outcome.Request!.Lens!.Facet);
        Assert.NotEqual(result.Outcome.Request.Destination.Id, result.Snapshot.ActiveSubject.Id);
        Assert.Equal(occurrenceRemoved ? StructuralSubjectKind.Workspace : StructuralSubjectKind.Library,
            result.Snapshot.ActiveSubject.Kind);
        Assert.Equal(NavigationLensBasisKind.Recommendation, result.Snapshot.LensOutcome.Basis);
        Assert.Equal(result.Snapshot.ActiveSubject, result.Snapshot.LensOutcome.EffectiveLens!.Subject);
        NavigationLensResolution resolution = Assert.IsType<NavigationLensResolution>(operation.LensResolution);
        Assert.Equal(result.Authority, resolution.Authority);
        Assert.Equal(result.Request, resolution.Request);
        Assert.Equal(NavigationOperationKind.Lens, resolution.Operation);
        Assert.Null(resolution.DescendantRequest);
        NavigationLensActivationResult activation = resolution.Activation;
        Assert.Same(request, activation.Request);
        if (failed)
        {
            NavigationLensActivationResult.Failed failure = Assert.IsType<NavigationLensActivationResult.Failed>(activation);
            var basis = Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(failure.Outcome.Basis);
            Assert.Same(evidence, Assert.IsType<ViewFacetResolution.Failed>(basis.Result).Evidence);
            Assert.Equal(NavigationFailureSource.Registry, result.Outcome.FailureSource);
        }
        else
        {
            NavigationLensActivationResult.Unavailable unavailable = Assert.IsType<NavigationLensActivationResult.Unavailable>(activation);
            var basis = Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(unavailable.Outcome.Basis);
            Assert.Same(reason, Assert.IsType<ViewFacetResolution.Unavailable>(basis.Result).Reason);
        }
    }

    [Fact]
    public async Task SemanticEvidenceChanges_AdvanceRevisionEvenWhenConsumerDiagnosticTextIsEqual()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        fixture.Acknowledge(session.Initialization);
        var failure = new ApiSurfaceInspectionFailure(
            "read metadata", 1, MetadataTypeNameFailureMechanism.Metadata, "TypeDef", "same message")
        {
            AffectedTypeDefinitions = [NavigationSnapshotTestData.Type("Affected").DefinitionName!],
        };
        SetEvidence(fixture, failure);
        NavigationConsumerResult first = await session.RefreshAsync(TestContext.Current.CancellationToken);
        fixture.Acknowledge(first);
        ApiSurfaceInspectionFailure secondFailure = failure with
        {
            SubjectToken = 2,
            OwningTypeToken = 3,
        };
        SetEvidence(fixture, secondFailure);
        NavigationConsumerResult second = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(first.Snapshot.Diagnostics, second.Snapshot.Diagnostics);
        Assert.NotEqual(first.Authority!.Revision, second.Authority!.Revision);
        Assert.Equal(NavigationSynchronizationDisposition.SynchronizationRequired, second.Synchronization);
        var retained = Assert.IsType<NavigationInventoryEvidence.InspectionFailed>(
            Assert.Single(session.CurrentSnapshot.Inventory!.Types.Evidence));
        Assert.Same(secondFailure, retained.Failure);
        fixture.Acknowledge(second);
        SetEvidence(fixture, secondFailure with
        {
            AffectedTypeDefinitions = [NavigationSnapshotTestData.Type("Affected").DefinitionName!],
        });
        NavigationConsumerResult equivalent = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(second.Authority.Revision, equivalent.Authority!.Revision);
        Assert.Same(second.Snapshot, equivalent.Snapshot);
    }

    [Fact]
    public async Task DescendantLensEvidence_IsRetainedAndComparedAsSemanticState()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        fixture.Acknowledge(session.Initialization);
        var firstEvidence = new VariantDiagnostic(1);
        fixture.Override = id => id.Value == "type.compare"
            ? new ViewFacetAvailability.Failed("same failure", firstEvidence) : null;
        NavigationConsumerResult first = await session.RefreshAsync(TestContext.Current.CancellationToken);
        NavigationConsumerLensDescriptor firstLens = first.Snapshot.Types[0].DescendantLenses
            .Single(row => row.Facet.Id == "type.compare");
        Assert.Equal(NavigationDescriptorState.Failed, firstLens.State);
        Assert.Null(firstLens.Action);
        Assert.NotEqual(session.Initialization.Authority!.Revision, first.Authority!.Revision);
        fixture.Acknowledge(first);
        var secondEvidence = new VariantDiagnostic(2);
        fixture.Override = id => id.Value == "type.compare"
            ? new ViewFacetAvailability.Failed("same failure", secondEvidence) : null;
        NavigationConsumerResult second = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(firstLens, second.Snapshot.Types[0].DescendantLenses.Single(row => row.Facet.Id == "type.compare"));
        Assert.NotEqual(first.Authority.Revision, second.Authority!.Revision);
        NavigationDescendantLensDescriptor retained = session.CurrentSnapshot.DescendantLenses
            .Single(row => row.Request.Destination.Facet.Value == "type.compare");
        Assert.Same(secondEvidence, Assert.IsType<ViewFacetAvailability.Failed>(retained.Option.Availability).Evidence);
        fixture.Acknowledge(second);
        NavigationConsumerResult same = await session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(second.Authority.Revision, same.Authority!.Revision);
    }

    [Fact]
    public async Task SemanticEquality_PreservesNonProjectedProducerFactsAndEquivalentFreshRows()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        fixture.Acknowledge(session.Initialization);
        foreach (Action<ApiType> change in new Action<ApiType>[]
        {
            type => type.BaseType = "Sample.Base",
            type => type.SourceChecksum = [1, 2, 3],
            type => type.Documentation.Remarks = "new remarks",
            type => type.Documentation.Parameters = new() { ["x"] = "description" },
            type => type.InterfaceReferences.Add(new(
                new ApiAssemblyIdentity(
                    "Sample",
                    new Version(1, 0, 0, 0),
                    null,
                    null),
                "Sample.IWidget",
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Sample",
                        ["IWidget"])).Name)),
            type => type.TypeParameters.Add(new TypeParameter
                { Name = "T", StructuredConstraints = [new("class", false)], TypeKind = TypeParameterTypeKind.ReferenceType }),
            type => type.MemorySafety = new(Guid.Empty, new MemorySafetyRulesResult.Available(
                MemorySafetyRulesState.Updated, [new(1, MemorySafetyRulesObservationState.Decoded, 2, null)])),
            type => type.Members[0].IsObsolete = true,
            type => type.Members[0].DeclarationMetadataToken = 7,
            type => type.Members[0].JsonPropertyNameAttributeValues.Add("wire"),
            type => type.Members[0].SignatureModel = new ApiSignature
                { CanonicalReturnType = "System.Void", Parameters = [new() { Name = "x", Type = "int", HasDefault = true }] },
            type => type.Members[0].BackingStorage = new(
                Guid.Empty, ApiBackingStorageConvention.AutoProperty, ApiBackingStorageState.Associated, [new(1, "field", false)]),
        })
        {
            ApiType baseline = Row();
            ApiType changed = Row();
            change(changed);
            Assert.False(NavigationWorkspaceSnapshotEquality.Type(baseline, changed));
            fixture.SetTypes(0, [changed], [Row()]);
            NavigationConsumerResult result = await session.RefreshAsync(TestContext.Current.CancellationToken);
            fixture.Acknowledge(result);
            ApiType equivalent = Row();
            change(equivalent);
            Assert.True(NavigationWorkspaceSnapshotEquality.Type(changed, equivalent));
            fixture.SetTypes(0, [equivalent], [Row()]);
            NavigationConsumerResult unchanged = await session.RefreshAsync(TestContext.Current.CancellationToken);
            Assert.Equal(result.Authority!.Revision, unchanged.Authority!.Revision);
            fixture.Acknowledge(unchanged);
        }

        static ApiType Row() => NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Run"));
    }

    [Fact]
    public async Task FilteredJsonPropertyNames_EquivalentPopulatedFactsDoNotAdvanceRevision()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Acknowledge(fixture.Session.Initialization);
        ApiType firstRow = Row();
        fixture.SetTypes(0, [firstRow], []);
        NavigationConsumerResult first = await fixture.Session.RefreshAsync(TestContext.Current.CancellationToken);
        fixture.Acknowledge(first);
        ApiType secondRow = Row();
        Assert.NotSame(firstRow.FilteredJsonPropertyNameFacts[0].PropertyNames,
            secondRow.FilteredJsonPropertyNameFacts[0].PropertyNames);
        Assert.True(NavigationWorkspaceSnapshotEquality.Type(firstRow, secondRow));
        fixture.SetTypes(0, [secondRow], []);
        NavigationConsumerResult second = await fixture.Session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(first.Authority!.Revision, second.Authority!.Revision);
        Assert.Same(first.Snapshot, second.Snapshot);
        Assert.Equal(NavigationSynchronizationDisposition.Current, second.Synchronization);
        fixture.Acknowledge(second);
        ApiType changed = Row();
        changed.FilteredJsonPropertyNameFacts[0].PropertyNames[0] = "wirename";
        fixture.SetTypes(0, [changed], []);
        NavigationConsumerResult different = await fixture.Session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(second.Authority.Revision, different.Authority!.Revision);

        static ApiType Row()
        {
            ApiType type = NavigationSnapshotTestData.Type("Widget");
            type.FilteredJsonPropertyNameFacts =
            [
                new(FilteredJsonPropertyNameKind.AutoPropertyBackingField, "Value", 1, ["wireName", null, ""]),
                new(FilteredJsonPropertyNameKind.EventBackingField, "Changed", 2, ["eventName"]),
            ];
            return type;
        }
    }

    [Fact]
    public void FilteredJsonPropertyNames_CompareOrdinalOrderedValuesAndAllFactFields()
    {
        var original = new FilteredJsonPropertyNameFact(
            FilteredJsonPropertyNameKind.AutoPropertyBackingField, "Value", 1, ["Name", null, ""]);
        foreach (FilteredJsonPropertyNameFact changed in new[]
        {
            original with { PropertyNames = ["name", null, ""] },
            original with { PropertyNames = [null, "Name", ""] },
            original with { PropertyNames = ["Name", "", ""] },
            original with { PropertyNames = ["Name", null] },
            original with { Kind = FilteredJsonPropertyNameKind.CompilerNamedField },
            original with { AssociatedMemberName = "value" },
            original with { MetadataToken = 2 },
        })
        {
            ApiType first = NavigationSnapshotTestData.Type("Widget");
            ApiType second = NavigationSnapshotTestData.Type("Widget");
            first.FilteredJsonPropertyNameFacts = [original];
            second.FilteredJsonPropertyNameFacts = [changed];
            Assert.False(NavigationWorkspaceSnapshotEquality.Type(first, second));
        }
    }

    [Fact]
    public async Task NestedReferenceCollections_EquivalentPopulatedRowsDoNotAdvanceRevision()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        fixture.Acknowledge(fixture.Session.Initialization);
        ApiType firstRow = Row();
        ApiType secondRow = Row();
        secondRow.Documentation.Parameters = new() { ["second"] = "two", ["first"] = "one" };
        Assert.True(NavigationWorkspaceSnapshotEquality.Type(firstRow, secondRow));
        ApiType changedConstraintIdentity = Row();
        changedConstraintIdentity.TypeParameters[0].ConstraintTypeDefinitionNames =
            [DefinitionName("OtherBase")];
        Assert.False(
            NavigationWorkspaceSnapshotEquality.Type(
                firstRow,
                changedConstraintIdentity));
        fixture.SetTypes(0, [firstRow], []);
        NavigationConsumerResult first = await fixture.Session.RefreshAsync(TestContext.Current.CancellationToken);
        fixture.Acknowledge(first);
        fixture.SetTypes(0, [secondRow], []);
        NavigationConsumerResult second = await fixture.Session.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(first.Authority!.Revision, second.Authority!.Revision);
        Assert.Same(first.Snapshot, second.Snapshot);

        static ApiType Row()
        {
            ApiType type = NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Run"));
            var reference = new ApiTypeReferenceIdentity(
                new ApiAssemblyIdentity("Example", new Version(1, 0), null, null),
                "Sample.Widget", type.DefinitionName);
            ApiTypeShape shape = ApiTypeShape.GenericInstance(reference,
                [ApiTypeShape.Array(ApiTypeShape.PrimitiveType(ApiPrimitiveType.Int32), 2, [3, 4], [0, 1])]);
            type.JsonSerializableRoots = [new(reference, false) { Type = shape }];
            type.TypeParameters = [Parameter()];
            type.MemorySafety = new(Guid.Empty, new MemorySafetyRulesResult.Available(
                MemorySafetyRulesState.Updated, [new(1, MemorySafetyRulesObservationState.Decoded, 2, null)]));
            type.FilteredRuntimeJsExportFacts = [new("Hidden", 1, 1, true, false)];
            type.AdditionalSourceFiles = [new() { FilePath = "part.cs", SourceChecksum = [1, 2] }];
            type.Documentation = new()
            {
                Parameters = new() { ["first"] = "one", ["second"] = "two" },
                Samples = [new() { RelativePath = "example.cs", Region = "Example", Content = "sample" }],
            };
            ApiMember member = type.Members[0];
            member.SignatureModel = new()
            {
                ReturnType = "void",
                MemberName = "Run",
                ReturnAttributes = ["ReturnAttribute"],
                ReturnTypeReferences = [reference],
                ReturnTypeShape = shape,
                TypeParameters = [Parameter()],
                Parameters = [new() { Name = "value", Type = "int", Attributes = ["Attribute"], TypeReferences = [reference] }],
                Accessors = [new() { Kind = "get", ReturnAttributes = ["Attribute"] }],
            };
            member.BackingStorage = new(Guid.Empty, ApiBackingStorageConvention.AutoProperty,
                ApiBackingStorageState.Associated, [new(1, "field", false)]);
            member.AccessorMemorySafety =
            [
                new(Guid.Empty, new MemorySafetyMemberContractResult.None(
                    new(1, MemorySafetyRulesState.Legacy, MemorySafetyPointerEvidence.Absent,
                        MemorySafetyFixedBufferEvidence.Absent, RequiresUnsafeAttributeEvidence.None,
                        RequiresUnsafeAttributeEvidence.None, null)), MemorySafetyPointerEvidence.Absent),
            ];
            member.AccessorImplementations =
            [
                new(Guid.Empty, 1, System.Reflection.MethodAttributes.Public,
                    System.Reflection.MethodImplAttributes.IL, true),
            ];
            member.RuntimeJsExportWrapperCandidates = [new(1, 2, 1) { ModuleVersionId = Guid.Empty }];
            member.JsonPropertyNameAttributeValues = ["name", null];
            member.JsonStringEnumMemberNameAttributeValues = ["value", null];
            member.SourceChecksum = [1, 2, 3];
            member.Documentation.Samples = [new() { RelativePath = "member.cs", Description = "example" }];
            return type;
        }

        static TypeParameter Parameter() => new()
        {
            Name = "T",
            Constraints = ["Sample.Base"],
            StructuredConstraints = [new("Sample.Base", true)],
            ConstraintTypeDefinitionNames = [DefinitionName("Base")],
            TypeKind = TypeParameterTypeKind.ReferenceType,
        };

        static MetadataTypeDefinitionName DefinitionName(string name) =>
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "Sample",
                    [name])).Name;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DescendantNonSuccess_RetainsExactEvidenceUnderActionAuthority(bool failed, bool member)
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        if (member)
            await session.ExecuteAsync(session.Snapshot.Types[0].Navigation.Action!, TestContext.Current.CancellationToken);
        NavigationWorkspaceSnapshot current = session.CurrentSnapshot;
        string facet = member ? "member.compare" : "type.compare";
        NavigationConsumerLensDescriptor lens = (member
            ? session.Snapshot.Members[0].DescendantLenses
            : session.Snapshot.Types[0].DescendantLenses).Single(row => row.Facet.Id == facet);
        NavigationAction action = lens.Action!;
        DescendantSubjectLensRequest expected = current.DescendantLenses
            .Single(row => row.Request.Destination.Facet.Value == facet).Request;
        var evidence = new VariantDiagnostic(42);
        var reason = ViewFacetUnavailableReason.CapabilityAbsent("exact destination unavailable");
        fixture.Override = id => id.Value == facet
            ? failed
                ? new ViewFacetAvailability.Failed("exact destination failed", evidence)
                : new ViewFacetAvailability.Unavailable(reason)
            : null;
        NavigationOperationResult operation = await session.ExecuteOperationAsync(action, TestContext.Current.CancellationToken);
        NavigationConsumerResult result = operation.Consumer;
        NavigationLensResolution retained = Assert.IsType<NavigationLensResolution>(operation.LensResolution);
        Assert.Equal(action.Id, result.Request);
        Assert.Equal(action.Id, retained.Request);
        Assert.Equal(NavigationOperationKind.DescendantLens, retained.Operation);
        Assert.Equal(result.Authority, retained.Authority);
        Assert.True(session.ValidateAuthority(retained.Authority));
        Assert.Equal(expected, retained.DescendantRequest);
        Assert.Same(retained.DescendantRequest!.Destination, retained.Activation.Request);
        Assert.Equal(lens.Target, result.Outcome.Request!.Lens);
        Assert.Same(current, session.CurrentSnapshot);
        if (failed)
        {
            var activation = Assert.IsType<NavigationLensActivationResult.Failed>(retained.Activation);
            var basis = Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(activation.Outcome.Basis);
            ViewFacetResolution.Failed resolution = Assert.IsType<ViewFacetResolution.Failed>(basis.Result);
            Assert.Same(evidence, resolution.Evidence);
            Assert.Equal(resolution.Message, result.Outcome.Resolution!.Message);
        }
        else
        {
            var activation = Assert.IsType<NavigationLensActivationResult.Unavailable>(retained.Activation);
            var basis = Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(activation.Outcome.Basis);
            Assert.Same(reason, Assert.IsType<ViewFacetResolution.Unavailable>(basis.Result).Reason);
        }
        fixture.Prepare = _ => throw new InvalidOperationException("Duplicate requests must not resolve.");
        NavigationOperationResult duplicate = await session.ExecuteOperationAsync(action, TestContext.Current.CancellationToken);
        Assert.Equal(NavigationRejectionKind.DuplicateAction, duplicate.Consumer.Outcome.Rejection);
        Assert.Null(duplicate.LensResolution);
        Assert.Same(retained, operation.LensResolution);
        Assert.False(session.ValidateAuthority(retained.Authority));
    }

    [Fact]
    public async Task DescendantRegistryRejection_RetainsExactRequestDescriptorAndAuthority()
    {
        bool applicable = true;
        var references = new ViewFacetDescriptor(
            new("library.references"), StructuralSubjectKind.Library, "References", "References",
            0, ViewFacetRole.LibraryReferences);
        var compare = new ViewFacetDescriptor(
            new("type.compare"), StructuralSubjectKind.Type, "Compare", "Compare", 1);
        var first = new ViewFacetRegistration.Active(
            references, references.Summary, target => target.Subject.Kind == StructuralSubjectKind.Library,
            new(references.Id, references), (_, facts) => facts.Get(references.Id));
        var second = new ViewFacetRegistration.Active(
            compare, compare.Summary, target => applicable && target.Subject.Kind == StructuralSubjectKind.Type,
            new(compare.Id, compare), (_, facts) => facts.Get(compare.Id));
        await using Fixture fixture = await Fixture.CreateAsync(registry: new([first, second], [first.Binding, second.Binding]));
        NavigationTestHost session = fixture.Session;
        NavigationWorkspaceSnapshot current = session.CurrentSnapshot;
        NavigationAction action = session.Snapshot.Types[0].DescendantLenses.Single().Action!;
        DescendantSubjectLensRequest expected = current.DescendantLenses.Single().Request;
        applicable = false;
        NavigationOperationResult operation = await session.ExecuteOperationAsync(action, TestContext.Current.CancellationToken);
        NavigationConsumerResult result = operation.Consumer;
        NavigationLensResolution retained = Assert.IsType<NavigationLensResolution>(operation.LensResolution);
        Assert.Equal(NavigationOutcomeKind.Rejected, result.Outcome.Kind);
        Assert.Equal(NavigationResolutionKind.Inapplicable, result.Outcome.Resolution!.Kind);
        Assert.Equal(action.Id, retained.Request);
        Assert.Equal(expected, retained.DescendantRequest);
        Assert.Equal(result.Authority, retained.Authority);
        Assert.Equal(NavigationOperationKind.DescendantLens, retained.Operation);
        var activation = Assert.IsType<NavigationLensActivationResult.Rejected>(retained.Activation);
        var rejection = Assert.IsType<NavigationLensRejection.Registry>(activation.Rejection);
        Assert.Equal(expected.Destination, rejection.Basis.Request);
        Assert.Same(compare, Assert.IsType<ViewFacetResolution.Inapplicable>(rejection.Result).Descriptor);
        Assert.Same(current, session.CurrentSnapshot);
    }

    [Fact]
    public async Task SupersededDescendantCompletion_DoesNotReplaceCurrentResolutionEvidence()
    {
        await using Fixture fixture = await Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        var pending = new TaskCompletionSource<NavigationPreparation>(TaskCreationOptions.RunContinuationsAsynchronously);
        NavigationAction olderAction = session.Snapshot.Types[0].DescendantLenses.Single(row => row.Facet.Id == "type.compare").Action!;
        NavigationAction newerAction = session.Snapshot.Types[0].DescendantLenses.Single(row => row.Facet.Id == "type.api").Action!;
        var evidence = new VariantDiagnostic(42);
        fixture.Override = id => id.Value == "type.api" ? new ViewFacetAvailability.Failed("latest failure", evidence) : null;
        fixture.Prepare = request => request.Request == olderAction.Id
            ? new(pending.Task) : ValueTask.FromResult(fixture.Ready(request));
        Task<NavigationOperationResult> older = session.ExecuteOperationAsync(olderAction, TestContext.Current.CancellationToken).AsTask();
        NavigationEvaluationRequest oldRequest = fixture.LastRequest!;
        NavigationOperationResult newer = await session.ExecuteOperationAsync(newerAction, TestContext.Current.CancellationToken);
        NavigationLensResolution retained = Assert.IsType<NavigationLensResolution>(newer.LensResolution);
        pending.SetResult(fixture.Ready(oldRequest));
        NavigationOperationResult superseded = await older.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(NavigationOutcomeKind.Superseded, superseded.Consumer.Outcome.Kind);
        Assert.Null(superseded.Consumer.Authority);
        Assert.Null(superseded.LensResolution);
        Assert.Same(retained, newer.LensResolution);
        Assert.Equal(newerAction.Id, retained.Request);
        Assert.Equal(newer.Consumer.Authority, retained.Authority);
        Assert.True(session.ValidateAuthority(retained.Authority));
    }

    static WorkspaceScopeSnapshot WithStatus(WorkspaceScopeSnapshot scope, ArtifactRootRealizationStatus status) =>
        new(scope.Revision, scope.PhysicalComposition,
            [
                new(scope.Packages[0].Occurrence, scope.Packages[0].Realization with { Status = status }),
                .. scope.Packages.Skip(1),
            ], scope.Closure, scope.Preparing);

    sealed record VariantDiagnostic(int Value) : IViewFacetDiagnosticEvidence;

    static void SetEvidence(Fixture fixture, ApiSurfaceInspectionFailure failure)
    {
        NavigationPackageEvaluation package = fixture.Packages[0];
        fixture.Packages[0] = new NavigationPackageEvaluation(
            package.Occurrence, fixture.Bindings[0], package.Libraries,
            new AssemblyContextApiSurfaceResult(
                new AssemblyContextResult<AssemblyApiSurface>(
                [
                    .. package.Libraries.Select((library, index) =>
                        (AssemblyContextEntry<AssemblyApiSurface>)new AssemblyContextEntry<AssemblyApiSurface>.Available(
                            new AssemblyContextSubject(library.Library.Participant.Assembly),
                            new AssemblyApiSurface(
                                new ApiSurface
                                {
                                    Types = [NavigationSnapshotTestData.Type("Widget", NavigationSnapshotTestData.Member("Run"))],
                                }, index == 0 ? [failure] : []))),
                ]), [], Truncation: null));
    }
}
