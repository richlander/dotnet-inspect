using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using DotnetInspector.Queries.EmbeddedFixtures;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationDetachmentTests
{
    [Fact]
    public async Task ErasingIdentity_PreservesExactRegistrationEquality()
    {
        await using NavigationSessionTests.Fixture fixture = await NavigationSessionTests.Fixture.CreateAsync();
        WorkspaceContextMember original = fixture.Packages[0].Libraries[0].Library;
        ResolvedAssemblyReference assembly = original.Participant.Assembly;
        ResolvedAssemblyReference different = ResolvedAssemblyReference.Create(
            assembly.Identity, path: null, static () => Stream.Null, assembly.Provenance);
        var otherLibrary = original with
        {
            Participant = new AssemblyContextParticipant(different, NoResolverAssemblyBindingPolicy.Instance),
        };
        StructuralSubjectIdentity.PackageSubject package = fixture.Session.InstalledSnapshot.RetainedContext!.Package;
        StructuralSubjectIdentity.LibrarySubject first = StructuralSubjectIdentity.ForLibrary(package, original);
        StructuralSubjectIdentity.LibrarySubject same = StructuralSubjectIdentity.ForLibrary(package, original);
        StructuralSubjectIdentity.LibrarySubject other = StructuralSubjectIdentity.ForLibrary(package, otherLibrary);

        Assert.Equal(first, same);
        Assert.Same(first.Identity.Registration, same.Identity.Registration);
        Assert.Equal(first.Identity.Assembly, other.Identity.Assembly);
        Assert.Equal(first.Identity.Provenance, other.Identity.Provenance);
        Assert.NotEqual(first, other);
        Assert.NotSame(first.Identity.Registration, other.Identity.Registration);
    }

    [Fact]
    public async Task ArtifactBackedStateTicketsAndExactResults_DoNotRetainAcquisitionAuthority()
    {
        ArtifactSpecimen specimen = await CreateArtifactSpecimenAsync();
        Collect();

        Assert.False(specimen.Registration.IsAlive);
        Assert.False(specimen.Artifact.IsAlive);
        Assert.False(specimen.Error.IsAlive);
        Assert.All(specimen.Completions, completion =>
        {
            Assert.Equal(NavigationOutcomeKind.Applied, completion.Result!.Consumer.Outcome.Kind);
            Assert.NotNull(completion.Result.LensResolution);
            Assert.True(NavigationTransitions.ValidateAuthority(completion.State, completion.Result.Consumer.Authority));
            Assert.Equal(completion.Result.Consumer.Authority, completion.Result.LensResolution.Authority);
        });
        Assert.Equal(StructuralSubjectKind.Library, specimen.Completions[0].State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(StructuralSubjectKind.Type, specimen.Completions[1].State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(StructuralSubjectKind.Member, specimen.Completions[2].State.Snapshot.ActiveSubject.Kind);
        Assert.NotNull(specimen.Completions[1].Result!.LensResolution!.DescendantRequest);
        Assert.NotNull(specimen.Completions[2].Result!.LensResolution!.DescendantRequest);

        var evidence = Assert.IsType<NavigationInventoryEvidence.DetachedParticipantFailed>(
            Assert.Single(specimen.Failed.State.InstalledSnapshot.Inventory!.Types.Evidence));
        Assert.Equal(typeof(InvalidOperationException).FullName, evidence.Error.Type);
        Assert.Equal("fixture participant failed", evidence.Error.Message);
        Assert.Contains("fixture participant failed", evidence.Error.Detail);
        Assert.Equal(NavigationDiagnosticKind.ParticipantFailed,
            Assert.Single(specimen.Failed.State.Snapshot.Diagnostics).Kind);
        Assert.Same(evidence.Library.Identity.Registration, evidence.ProducerSubject.Registration);
        GC.KeepAlive(specimen);
    }

    [Fact]
    public async Task DetachedFailure_RefreshPreservesExactFailureIdentityAndSemanticRevision()
    {
        await using NavigationSessionTests.Fixture fixture = await NavigationSessionTests.Fixture.CreateAsync();
        NavigationPackageEvaluation package = fixture.Packages[0];
        var error = new InvalidOperationException("same producer failure");
        var surface = new AssemblyContextApiSurfaceResult(
            new AssemblyContextResult<AssemblyApiSurface>(
            [
                .. package.Libraries.Select(library =>
                    (AssemblyContextEntry<AssemblyApiSurface>)new AssemblyContextEntry<AssemblyApiSurface>.Failed(
                        new AssemblyContextSubject(library.Library.Participant.Assembly), error)),
            ]), [], Truncation: null);
        var failedPackage = new NavigationPackageEvaluation(
            package.Occurrence, fixture.Bindings[0], package.Libraries, surface);
        var facts = new NavigationEvaluationFacts(fixture.Scope, failedPackage, fixture.Availability);
        NavigationOperationInitialization initial = NavigationTransitions.Initialize(
            fixture.Workspace.Identity, facts, fixture.Registry);
        NavigationState installed = NavigationTransitions.RecordConsumerInstallation(
            initial.State, initial.Result.Consumer.Authority!).State;
        NavigationState acknowledged = NavigationTransitions.Acknowledge(
            installed, initial.Result.Consumer.Authority!).State;
        NavigationTransition queued = NavigationTransitions.QueueMaintenance(acknowledged);
        NavigationTransition begun = NavigationTransitions.Advance(queued.State);
        NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(
            begun.Work!, new NavigationPreparation.Ready(facts), fixture.Registry);
        NavigationTransition completed = NavigationTransitions.Complete(begun.State, begun.Work!, evaluation);

        Assert.Same(initial.State.InstalledSnapshot, completed.State.InstalledSnapshot);
        Assert.Equal(initial.State.Publication, completed.State.Publication);
        Assert.Equal(NavigationSynchronizationDisposition.Current, completed.Result!.Consumer.Synchronization);
        var original = Assert.IsType<NavigationInventoryEvidence.DetachedParticipantFailed>(
            initial.State.InstalledSnapshot.Inventory!.Types.Evidence[0]);
        var refreshed = Assert.IsType<NavigationInventoryEvidence.DetachedParticipantFailed>(
            evaluation.Snapshot.Inventory!.Types.Evidence[0]);
        Assert.Same(original.Error.Identity, refreshed.Error.Identity);
        Assert.Same(original.ProducerSubject.Registration, refreshed.ProducerSubject.Registration);
        Assert.Equal(original.Error, refreshed.Error);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static async Task<ArtifactSpecimen> CreateArtifactSpecimenAsync()
    {
        byte[] image = await File.ReadAllBytesAsync(
            typeof(EmbeddedSourceFixture).Assembly.Location, TestContext.Current.CancellationToken);
        PackageRootBinding binding = NavigationSnapshotTestData.BindingWithAssemblyImages(
            "Navigation.Artifact", "net11.0", ("Fixture", image));
        await using InspectionWorkspace workspace = new InspectionWorkspace();
        WorkspaceScopeSnapshot scope = await NavigationSnapshotTestData.ReplaceAsync(workspace, binding);
        WorkspacePackageOccurrenceDescriptor occurrence = Assert.Single(scope.Packages);
        var ready = Assert.IsType<ArtifactRootRealizationStatus.Ready>(occurrence.Realization.Status);
        ArtifactRootResult<ArtifactSpecimen> result = await workspace.ExecutePackageRootQueryAsync(
            (PackageArtifactRootCorrespondence)occurrence.Occurrence.Correspondence, ready.Generation,
            (realization, token) =>
            {
                token.ThrowIfCancellationRequested();
                ImmutableArray<NavigationLibraryEvaluation> libraries =
                    [.. realization.SurfaceParticipants.Select(participant =>
                        new NavigationLibraryEvaluation(binding.Coordinate, participant))];
                AssemblyAcquisitionRegistration registration = Assert.Single(libraries).Library.Participant.Assembly.Registration;
                Assert.NotNull(registration.ArtifactRegistration);
                AssemblyContextApiSurfaceResult surface = AssemblyContextApiSurfaceQuery.Execute(realization.SurfaceGroup);
                var package = new NavigationPackageEvaluation(occurrence, binding, libraries, surface);
                ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
                ViewFacetAvailabilitySnapshot availability = NavigationSnapshotTestData.AllAvailable(registry);
                var facts = new NavigationEvaluationFacts(scope, package, (_, _) => availability);
                var evaluations = ImmutableArray.CreateBuilder<NavigationEvaluationResult>();
                NavigationOperationInitialization initialized = NavigationTransitions.Initialize(workspace.Identity, facts, registry);
                StructuralSubjectIdentity.LibrarySubject library = Assert.IsType<StructuralSubjectIdentity.LibrarySubject>(
                    initialized.State.InstalledSnapshot.ActiveSubject);
                Assert.Equal(library, StructuralSubjectIdentity.ForLibrary(library.Package, libraries[0].Library));
                Assert.Same(library.Identity.Registration,
                    NavigationRegistrationIdentity.From(registration));
                NavigationTransition libraryBegin = NavigationTransitions.BeginLens(initialized.State,
                    new(library, new ViewFacetId("library.metadata")));
                NavigationTransition libraryResult = Complete(libraryBegin);
                NavigationAction typeAction = libraryResult.State.Snapshot.Types
                    .Single(type => type.Navigation.Subject?.Label == typeof(EmbeddedSourceFixture).FullName).DescendantLenses
                    .Single(lens => lens.Facet.Id == "type.api").Action!;
                NavigationTransition typeBegin = NavigationTransitions.Begin(libraryResult.State, typeAction);
                NavigationTransition typeResult = Complete(typeBegin);
                NavigationAction memberAction = typeResult.State.Snapshot.Members
                    .Single(member => member.Navigation.Subject?.Label == "Echo").DescendantLenses
                    .Single(lens => lens.Facet.Id == "member.overview").Action!;
                NavigationTransition memberBegin = NavigationTransitions.Begin(typeResult.State, memberAction);
                NavigationTransition memberResult = Complete(memberBegin);

                var error = new InvalidOperationException("fixture participant failed");
                error.Data["artifact"] = registration.ArtifactRegistration.Artifact;
                var failure = new AssemblyContextApiSurfaceResult(
                    new AssemblyContextResult<AssemblyApiSurface>(
                    [
                        new AssemblyContextEntry<AssemblyApiSurface>.Failed(
                            new AssemblyContextSubject(libraries[0].Library.Participant.Assembly), error),
                    ]), [], Truncation: null);
                var failedPackage = new NavigationPackageEvaluation(occurrence, binding, libraries, failure);
                NavigationOperationInitialization failed = NavigationTransitions.Initialize(
                    workspace.Identity, new(scope, failedPackage, (_, _) => availability), registry);
                return ValueTask.FromResult(new ArtifactSpecimen(
                    initialized, [libraryBegin, typeBegin, memberBegin],
                    evaluations.ToImmutable(), [libraryResult, typeResult, memberResult], failed,
                    new(registration), new(registration.ArtifactRegistration.Artifact), new(error)));

                NavigationTransition Complete(NavigationTransition begun)
                {
                    NavigationEvaluationResult evaluation = NavigationTransitions.Evaluate(
                        begun.Work!, new NavigationPreparation.Ready(facts), registry);
                    evaluations.Add(evaluation);
                    return NavigationTransitions.Complete(begun.State, begun.Work!, evaluation);
                }
            }, cancellationToken: TestContext.Current.CancellationToken);
        return Assert.IsType<ArtifactRootResult<ArtifactSpecimen>.Available>(result).Value;
    }

    static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    sealed record ArtifactSpecimen(
        NavigationOperationInitialization Initialized,
        ImmutableArray<NavigationTransition> Beginnings,
        ImmutableArray<NavigationEvaluationResult> Evaluations,
        ImmutableArray<NavigationTransition> Completions,
        NavigationOperationInitialization Failed,
        WeakReference Registration,
        WeakReference Artifact,
        WeakReference Error);
}
