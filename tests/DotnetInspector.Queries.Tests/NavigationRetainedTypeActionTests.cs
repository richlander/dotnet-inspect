using System.Collections.Immutable;
using System.Text.Json;

using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationRetainedTypeActionTests
{
    [Fact]
    public async Task RetainedTypeAction_ActivatesRealSystemTextJsonObservation()
    {
        byte[] image = await File.ReadAllBytesAsync(
            typeof(JsonSerializer).Assembly.Location,
            TestContext.Current.CancellationToken);
        PackageRootBinding[] bindings =
        [
            NavigationSnapshotTestData.BindingWithAssemblyImages(
                "System.Text.Json.Source",
                "net11.0",
                ("System.Text.Json", image)),
            NavigationSnapshotTestData.BindingWithAssemblyImages(
                "System.Text.Json.Destination",
                "net11.0",
                ("System.Text.Json", image)),
        ];
        await using var workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                bindings);
        WorkspacePackageOccurrenceDescriptor sourceOccurrence =
            scope.Packages[0];
        WorkspacePackageOccurrenceDescriptor destinationOccurrence =
            scope.Packages[1];
        var sourceReady =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                sourceOccurrence.Realization.Status);
        var destinationReady =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                destinationOccurrence.Realization.Status);

        ArtifactRootResult<bool> outer =
            await workspace.ExecutePackageRootQueryAsync(
                (PackageArtifactRootCorrespondence)
                    sourceOccurrence.Occurrence.Correspondence,
                sourceReady.Generation,
                async (sourceRealization, cancellationToken) =>
                {
                    NavigationPackageEvaluation source =
                        Evaluation(
                            sourceOccurrence,
                            bindings[0],
                            sourceRealization);
                    ArtifactRootResult<bool> inner =
                        await workspace.ExecutePackageRootQueryAsync(
                            (PackageArtifactRootCorrespondence)
                                destinationOccurrence.Occurrence
                                    .Correspondence,
                            destinationReady.Generation,
                            (destinationRealization, _) =>
                            {
                                NavigationPackageEvaluation destination =
                                    Evaluation(
                                        destinationOccurrence,
                                        bindings[1],
                                        destinationRealization);
                                ViewFacetRegistry registry =
                                    InspectionViewFacetCatalog.Registry;
                                ViewFacetAvailabilitySnapshot availability =
                                    NavigationSnapshotTestData.AllAvailable(
                                        registry);
                                NavigationOperationInitialization initial =
                                    NavigationTransitions.Initialize(
                                        workspace.Identity,
                                        new(
                                            scope,
                                            source,
                                            (_, _) => availability),
                                        registry);
                                NavigationWorkspaceSnapshot destinationSnapshot =
                                    NavigationWorkspaceSnapshotEvaluation
                                        .Evaluate(
                                            new()
                                            {
                                                Scope = scope,
                                                Package = destination,
                                            },
                                            registry,
                                            (_, _) => availability);
                                StructuralSubjectIdentity.TypeSubject type =
                                    destinationSnapshot.Types.Single(
                                        row =>
                                            row.Row.ProducerRow.FullName
                                            == typeof(JsonSerializer)
                                                .FullName).Row.Subject;

                                NavigationTransition publication =
                                    NavigationTransitions
                                        .PublishRetainedTypeAction(
                                            initial.State,
                                            initial.State.Publication,
                                            type);
                                NavigationAction action =
                                    Assert.IsType<NavigationAction>(
                                        publication.ActionPublication!.Action);
                                NavigationTransition beginning =
                                    NavigationTransitions.Begin(
                                        publication.State,
                                        action);
                                NavigationEvaluationRequest work =
                                    Assert.IsType<NavigationEvaluationRequest>(
                                        beginning.Work);
                                NavigationEvaluationResult evaluation =
                                    NavigationTransitions.Evaluate(
                                        work,
                                        new NavigationPreparation.Ready(
                                            new(
                                                scope,
                                                destination,
                                                (_, _) => availability)),
                                        registry);
                                NavigationTransition completed =
                                    NavigationTransitions.Complete(
                                        beginning.State,
                                        work,
                                        evaluation);

                                Assert.Equal(
                                    NavigationOutcomeKind.Applied,
                                    completed.Result!.Consumer.Outcome.Kind);
                                Assert.Same(
                                    destinationOccurrence.Occurrence,
                                    completed.State.CurrentSnapshot
                                        .ActiveOccurrence);
                                Assert.Equal(
                                    type,
                                    completed.State.CurrentSnapshot
                                        .ActiveSubject);
                                return ValueTask.FromResult(true);
                            },
                            cancellationToken:
                                cancellationToken);
                    Assert.True(
                        Assert.IsType<
                            ArtifactRootResult<bool>.Available>(inner).Value);
                    return true;
                },
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(
            Assert.IsType<ArtifactRootResult<bool>.Available>(outer).Value);
    }

    [Fact]
    public async Task RetainedTypeAction_PublicationRequiresCurrentReadyExactOccurrence()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync(
                selectPackage: false);
        StructuralSubjectIdentity.TypeSubject destination =
            Snapshot(fixture, 1).Types[0].Row.Subject;

        NavigationActionPublicationResult published =
            fixture.Session.PublishRetainedTypeAction(destination);
        Assert.Equal(
            NavigationActionPublicationKind.Published,
            published.Kind);
        Assert.Equal(
            NavigationOperationKind.RetainedType,
            published.Action!.Kind);

        NavigationActionPublicationResult stale =
            fixture.Session.PublishRetainedTypeAction(
                destination,
                fixture.Session.State.Publication with
                {
                    Revision = "stale",
                });
        Assert.Equal(
            NavigationActionPublicationKind.Stale,
            stale.Kind);
        Assert.Null(stale.Action);

        await using NavigationSessionTests.Fixture foreign =
            await NavigationSessionTests.Fixture.CreateAsync();
        StructuralSubjectIdentity.TypeSubject foreignType =
            Snapshot(foreign, 0).Types[0].Row.Subject;
        NavigationActionPublicationResult rejected =
            fixture.Session.PublishRetainedTypeAction(foreignType);
        Assert.Equal(
            NavigationActionPublicationKind.Rejected,
            rejected.Kind);
        Assert.Equal(
            NavigationRejectionKind.ForeignWorkspace,
            rejected.Rejection);
        Assert.Null(rejected.Action);

        var scope = new WorkspaceScopeSnapshot(
            fixture.Scope.Revision,
            fixture.Scope.PhysicalComposition,
            [
                new(
                    fixture.Scope.Packages[0].Occurrence,
                    fixture.Scope.Packages[0].Realization with
                    {
                        Status =
                            new ArtifactRootRealizationStatus.Pending(),
                    }),
                fixture.Scope.Packages[1],
            ],
            fixture.Scope.Closure,
            preparing: null);
        fixture.Prepare =
            _ => ValueTask.FromResult<NavigationPreparation>(
                new NavigationPreparation.Ready(
                    new(scope, null, fixture.Availability)));
        fixture.Acknowledge(fixture.Session.Initialization);
        await fixture.Session.RefreshAsync(
            TestContext.Current.CancellationToken);

        StructuralSubjectIdentity.TypeSubject pending =
            Snapshot(fixture, 0).Types[0].Row.Subject;
        NavigationActionPublicationResult unavailable =
            fixture.Session.PublishRetainedTypeAction(pending);
        Assert.Equal(
            NavigationActionPublicationKind.Unavailable,
            unavailable.Kind);
        Assert.Null(unavailable.Action);
    }

    [Fact]
    public async Task RetainedTypeAction_RetirementPreservesStartedSelection()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync(
                selectPackage: false);
        StructuralSubjectIdentity.TypeSubject first =
            Snapshot(fixture, 0).Types[0].Row.Subject;
        StructuralSubjectIdentity.TypeSubject second =
            Snapshot(fixture, 1).Types[0].Row.Subject;

        NavigationTransition firstPublication =
            NavigationTransitions.PublishRetainedTypeAction(
                fixture.Session.State,
                fixture.Session.State.Publication,
                first);
        NavigationAction firstAction = Assert.IsType<NavigationAction>(
            firstPublication.ActionPublication!.Action);
        NavigationTransition secondPublication =
            NavigationTransitions.PublishRetainedTypeAction(
                firstPublication.State,
                firstPublication.State.Publication,
                second);
        NavigationAction secondAction = Assert.IsType<NavigationAction>(
            secondPublication.ActionPublication!.Action);
        NavigationTransition beginning =
            NavigationTransitions.Begin(
                secondPublication.State,
                firstAction);
        NavigationEvaluationRequest work =
            Assert.IsType<NavigationEvaluationRequest>(beginning.Work);

        NavigationTransition retired =
            NavigationTransitions.RetireRetainedTypeActions(
                beginning.State,
                [firstAction, secondAction]);

        Assert.DoesNotContain(
            firstAction.Id,
            retired.State.Data.Actions.Keys);
        Assert.DoesNotContain(
            secondAction.Id,
            retired.State.Data.Actions.Keys);
        Assert.Equal(
            firstAction,
            retired.State.Data.ConsumedActions[firstAction.Id]);
        Assert.Same(work, retired.State.Data.Explicit);

        NavigationEvaluationResult evaluation =
            NavigationTransitions.Evaluate(
                work,
                fixture.Ready(work),
                fixture.Registry);
        NavigationTransition completed =
            NavigationTransitions.Complete(
                retired.State,
                work,
                evaluation);

        Assert.Equal(
            NavigationOutcomeKind.Applied,
            completed.Result!.Consumer.Outcome.Kind);
        Assert.Equal(
            first,
            completed.State.CurrentSnapshot.ActiveSubject);
    }

    [Fact]
    public async Task RetainedTypeAction_SelectsExactTypeAcrossOccurrencesWithoutIntermediateState()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationConsumerResult source = await session.ExecuteAsync(
            session.Snapshot.Types[0].Navigation.Action!,
            TestContext.Current.CancellationToken);
        fixture.Acknowledge(source);
        StructuralSubjectIdentity.TypeSubject sourceType =
            Assert.IsType<StructuralSubjectIdentity.TypeSubject>(
                session.CurrentSnapshot.ActiveSubject);
        StructuralSubjectIdentity.TypeSubject destination =
            Snapshot(fixture, 1).Types[1].Row.Subject;

        NavigationAction sourceAction = Assert.IsType<NavigationAction>(
            session.PublishRetainedTypeAction(sourceType).Action);
        NavigationAction destinationAction = Assert.IsType<NavigationAction>(
            session.PublishRetainedTypeAction(destination).Action);
        Assert.Equal(
            sourceAction.Generation,
            destinationAction.Generation);
        Assert.NotEqual(sourceAction.Id, destinationAction.Id);
        int beforeRequests = fixture.RequestCount;
        NavigationConsumerResult result = await session.ExecuteAsync(
            destinationAction,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            NavigationOperationKind.RetainedType,
            result.Operation);
        Assert.Equal(
            NavigationOutcomeKind.Applied,
            result.Outcome.Kind);
        Assert.Same(
            fixture.Scope.Packages[1].Occurrence,
            fixture.LastRequest!.Occurrence);
        Assert.Same(
            fixture.Scope.Packages[1].Occurrence,
            session.CurrentSnapshot.ActiveOccurrence);
        Assert.Equal(
            destination,
            session.CurrentSnapshot.ActiveSubject);
        Assert.Equal(
            destination.Library,
            session.CurrentSnapshot.TypeInventoryLibraryContext);
        Assert.Equal(
            StructuralSubjectKind.Type,
            result.Snapshot.ActiveSubject.Kind);
        Assert.NotEqual(
            source.Authority!.Revision,
            result.Authority!.Revision);
        Assert.Equal(
            Revision(source.Authority.Revision) + 1,
            Revision(result.Authority.Revision));
        Assert.Equal(beforeRequests + 1, fixture.RequestCount);
    }

    [Fact]
    public async Task RetainedTypeAction_AlreadyActiveTypeIsSemanticNoOp()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync();
        NavigationConsumerResult selected =
            await fixture.Session.ExecuteAsync(
                fixture.Session.Snapshot.Types[0].Navigation.Action!,
                TestContext.Current.CancellationToken);
        fixture.Acknowledge(selected);
        var active =
            Assert.IsType<StructuralSubjectIdentity.TypeSubject>(
                fixture.Session.CurrentSnapshot.ActiveSubject);
        NavigationConsumerResult explicitLens =
            await fixture.Session.ActivateLensAsync(
                new(active, new ViewFacetId("type.metadata")),
                TestContext.Current.CancellationToken);
        fixture.Acknowledge(explicitLens);
        NavigationAction action = Assert.IsType<NavigationAction>(
            fixture.Session.PublishRetainedTypeAction(active).Action);

        NavigationConsumerResult result =
            await fixture.Session.ExecuteAsync(
                action,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            NavigationOutcomeKind.Applied,
            result.Outcome.Kind);
        Assert.Equal(
            NavigationLensBasisKind.ExactRequest,
            result.Snapshot.LensOutcome.Basis);
        Assert.Equal(
            "type.metadata",
            result.Snapshot.LensOutcome.EffectiveLens!.Facet);
        Assert.Same(explicitLens.Snapshot, result.Snapshot);
        Assert.Equal(
            explicitLens.Authority!.Revision,
            result.Authority!.Revision);
        Assert.NotEqual(
            explicitLens.Authority.Epoch,
            result.Authority.Epoch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task RetainedTypeAction_SeparatesEqualCoordinateOccurrences(
        int selectedIndex)
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync(
                selectPackage: false,
                repeatedCoordinates: true);
        StructuralSubjectIdentity.TypeSubject destination =
            Snapshot(fixture, selectedIndex).Types[0].Row.Subject;
        NavigationAction action = Assert.IsType<NavigationAction>(
            fixture.Session.PublishRetainedTypeAction(destination).Action);

        NavigationConsumerResult result =
            await fixture.Session.ExecuteAsync(
                action,
                TestContext.Current.CancellationToken);

        Assert.Equal(
            NavigationOutcomeKind.Applied,
            result.Outcome.Kind);
        Assert.Same(
            fixture.Scope.Packages[selectedIndex].Occurrence,
            fixture.Session.CurrentSnapshot.ActiveOccurrence);
        Assert.Equal(
            destination,
            fixture.Session.CurrentSnapshot.ActiveSubject);
    }

    [Fact]
    public async Task RetainedTypeAction_MapsUnavailableRejectedAmbiguousAndFailed()
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationConsumerSnapshot installed = session.Snapshot;
        NavigationWorkspaceSnapshot destinationSnapshot =
            Snapshot(fixture, 1);
        StructuralSubjectIdentity.TypeSubject existing =
            destinationSnapshot.Types[0].Row.Subject;
        StructuralSubjectIdentity.PackageSubject package =
            existing.Library.Package;
        StructuralSubjectIdentity.TypeSubject missing =
            StructuralSubjectIdentity.ForType(
                existing.Library,
                TypeName("Sample", "Missing"));

        NavigationConsumerResult unavailable = await ExecuteAsync(
            session,
            missing);
        Assert.Equal(
            NavigationOutcomeKind.Unavailable,
            unavailable.Outcome.Kind);
        Assert.Same(installed, unavailable.Snapshot);

        NavigationLibraryEvaluation sourceLibrary =
            fixture.Packages[0].Libraries[0];
        var mismatchedMember = new WorkspaceContextMember(
            sourceLibrary.Library.Declared,
            package.Coordinate,
            sourceLibrary.Library.Participant);
        StructuralSubjectIdentity.TypeSubject mismatched =
            StructuralSubjectIdentity.ForType(
                StructuralSubjectIdentity.ForLibrary(
                    package,
                    mismatchedMember),
                existing.Identity.Type);
        NavigationConsumerResult rejected = await ExecuteAsync(
            session,
            mismatched);
        Assert.Equal(
            NavigationOutcomeKind.Rejected,
            rejected.Outcome.Kind);
        Assert.Equal(
            NavigationRejectionKind.ForeignLibrary,
            rejected.Outcome.Rejection);
        Assert.Same(installed, rejected.Snapshot);

        fixture.SetTypes(
            1,
            [
                NavigationSnapshotTestData.Type("Widget"),
                NavigationSnapshotTestData.Type("Widget"),
            ],
            [NavigationSnapshotTestData.Type("Widget")]);
        NavigationConsumerResult ambiguous = await ExecuteAsync(
            session,
            existing);
        Assert.Equal(
            NavigationOutcomeKind.Ambiguous,
            ambiguous.Outcome.Kind);
        Assert.Same(installed, ambiguous.Snapshot);

        NavigationPackageEvaluation evaluation = fixture.Packages[1];
        var error = new InvalidOperationException("surface failed");
        fixture.Packages[1] = new NavigationPackageEvaluation(
            evaluation.Occurrence,
            fixture.Bindings[1],
            evaluation.Libraries,
            new AssemblyContextApiSurfaceResult(
                new AssemblyContextResult<AssemblyApiSurface>(
                [
                    .. evaluation.Libraries.Select(
                        library =>
                            (AssemblyContextEntry<AssemblyApiSurface>)
                            new AssemblyContextEntry<AssemblyApiSurface>.Failed(
                                new AssemblyContextSubject(
                                    library.Library.Participant.Assembly),
                                error)),
                ]),
                [],
                Truncation: null));
        NavigationConsumerResult failed = await ExecuteAsync(
            session,
            existing);
        Assert.Equal(
            NavigationOutcomeKind.Failed,
            failed.Outcome.Kind);
        Assert.Equal(
            NavigationFailureSource.Preparation,
            failed.Outcome.FailureSource);
        Assert.Equal(
            "surface failed",
            Assert.Single(failed.Outcome.Diagnostics).Message);
        Assert.Same(installed, failed.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetainedTypeAction_DestinationRealizationChangeIsTyped(
        bool failed)
    {
        await using NavigationSessionTests.Fixture fixture =
            await NavigationSessionTests.Fixture.CreateAsync();
        NavigationTestHost session = fixture.Session;
        NavigationConsumerSnapshot installed = session.Snapshot;
        StructuralSubjectIdentity.TypeSubject destination =
            Snapshot(fixture, 1).Types[0].Row.Subject;
        NavigationAction action = Assert.IsType<NavigationAction>(
            session.PublishRetainedTypeAction(destination).Action);
        WorkspacePackageOccurrenceDescriptor original =
            fixture.Scope.Packages[1];
        WorkspacePackageOccurrenceDescriptor unsettled =
            new(
                original.Occurrence,
                original.Realization with
                {
                    Status = failed
                        ? new ArtifactRootRealizationStatus.Failed(
                            ArtifactRootFailure.PreparationFailed)
                        : new ArtifactRootRealizationStatus.Pending(),
                });
        var scope = new WorkspaceScopeSnapshot(
            fixture.Scope.Revision,
            fixture.Scope.PhysicalComposition,
            [fixture.Scope.Packages[0], unsettled],
            fixture.Scope.Closure,
            preparing: null);
        fixture.Prepare =
            _ => ValueTask.FromResult<NavigationPreparation>(
                new NavigationPreparation.Ready(
                    new(
                        scope,
                        Package: null,
                        fixture.Availability,
                        new NavigationNonReadyPackageEvaluation(
                            unsettled))));

        NavigationConsumerResult result = await session.ExecuteAsync(
            action,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            failed
                ? NavigationOutcomeKind.Failed
                : NavigationOutcomeKind.Unavailable,
            result.Outcome.Kind);
        Assert.Equal(
            NavigationFailureSource.Preparation,
            result.Outcome.FailureSource);
        Assert.Same(installed, result.Snapshot);
        Assert.Same(
            fixture.Scope.Packages[0].Occurrence,
            session.CurrentSnapshot.ActiveOccurrence);
    }

    static async Task<NavigationConsumerResult> ExecuteAsync(
        NavigationTestHost session,
        StructuralSubjectIdentity.TypeSubject subject)
    {
        NavigationAction action = Assert.IsType<NavigationAction>(
            session.PublishRetainedTypeAction(subject).Action);
        return await session.ExecuteAsync(
            action,
            TestContext.Current.CancellationToken);
    }

    static NavigationWorkspaceSnapshot Snapshot(
        NavigationSessionTests.Fixture fixture,
        int packageIndex) =>
        NavigationWorkspaceSnapshotEvaluation.Evaluate(
            new NavigationWorkspaceSnapshotRequest
            {
                Scope = fixture.Scope,
                Package = fixture.Packages[packageIndex],
            },
            fixture.Registry,
            fixture.Availability);

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                @namespace,
                [name])).Name;

    static long Revision(string revision) =>
        long.Parse(
            revision[(revision.LastIndexOf(':') + 1)..],
            System.Globalization.CultureInfo.InvariantCulture);

    static NavigationPackageEvaluation Evaluation(
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding,
        PackageAssemblyContextRealization realization)
    {
        ImmutableArray<NavigationLibraryEvaluation> libraries =
        [
            .. realization.SurfaceParticipants.Select(
                participant =>
                    new NavigationLibraryEvaluation(
                        binding.Coordinate,
                        participant)),
        ];
        return new(
            occurrence,
            binding,
            libraries,
            AssemblyContextApiSurfaceQuery.Execute(
                realization.SurfaceGroup));
    }
}
