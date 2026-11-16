using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Platforms;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextLibraryAdapterTests
{
    sealed record SourceFacetDiagnostic : IViewFacetDiagnosticEvidence;

    [Fact]
    public async Task WorkspaceAdmission_NavigatesExactLibraryAndDefinitionWithoutPackage()
    {
        AssemblySource source = AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        await using var foreign = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [new AssemblyContextParticipant(source.Assembly, new TestBindingPolicy())]);
        AssemblyContextParticipant participant = Assert.Single(group.Participants);
        var completed = Assert.IsType<AssemblyContextLibraryAdapterResult.Completed>(
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.ApiOnly,
                Limits(source.Bytes.Length),
                cancellationToken: TestContext.Current.CancellationToken));
        WorkspaceLibraryAdmissionReceipt receipt =
            Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
                await workspace.AdmitLibraryBatchAsync(
                    CurrentRegistrations(workspace), completed.Artifacts,
                    [completed.Owner])).Receipt;
        WorkspaceLibraryOccurrence occurrence = Assert.Single(receipt.Occurrences);
        using LibraryOperationLease lease =
            Assert.IsType<WorkspaceLibraryOperationIssueOutcome.Issued>(
                workspace.IssueLibraryOperation(occurrence)).Lease;
        var inventory = Assert.IsType<LibraryTypeDeclarationInventoryInspectionOutcome.Completed>(
            LibraryTypeDeclarationInventoryInspection.Execute(
                new(occurrence.Library,
                    new(source.Bytes.Length, 100_000, 1_000_000, 10_000_000)),
                lease, TestContext.Current.CancellationToken));
        AssemblyTypeDeclaration jsonElement = Assert.Single(
            inventory.Correspondence.Inventory.Declarations,
            row => row.Kind == AssemblyTypeDeclarationKind.Definition
                && row.Name.ToMetadataFullName() == typeof(JsonElement).FullName);
        WorkspaceScopeSnapshot scope =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationAdmittedLibraryEvaluation evaluation = new(
            receipt, occurrence, inventory);
        StructuralSubjectIdentity.WorkspaceSubject root =
            StructuralSubjectIdentity.ForWorkspace(workspace.Identity);
        StructuralSubjectIdentity.AdmittedLibrarySubject library =
            StructuralSubjectIdentity.ForAdmittedLibrary(root, occurrence);
        StructuralSubjectIdentity.AdmittedTypeSubject type =
            StructuralSubjectIdentity.ForAdmittedType(library, jsonElement.Name);

        NavigationWorkspaceSnapshot unprepared =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new() { Scope = scope, AdmittedLibrary = new(receipt, occurrence) },
                registry, NavigationSnapshotTestData.AllAvailable(registry));
        Assert.Equal(library, unprepared.ActiveSubject);
        Assert.Null(unprepared.AdmittedInventory);
        Assert.Empty(unprepared.AdmittedTypes);
        Assert.Equal(NavigationDescriptorState.Unavailable,
            unprepared.Hierarchy.Single(row =>
                row.Kind == StructuralSubjectKind.Type).State);
        NavigationRestorationPreparationResult.Prepared libraryOnly =
            Assert.IsType<NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity,
                    new(scope, Package: null, (_, _) =>
                        NavigationSnapshotTestData.AllAvailable(registry))
                    {
                        AdmittedLibrary = new(receipt, occurrence),
                    },
                    registry, new()));
        Assert.Equal(library,
            libraryOnly.Initialization.State.CurrentSnapshot.ActiveSubject);
        Assert.Null(libraryOnly.Initialization.State.CurrentSnapshot.AdmittedInventory);
        Assert.True(NavigationTransitions.ValidateAuthority(
            libraryOnly.Initialization.State,
            libraryOnly.Initialization.Result.Consumer.Authority));

        NavigationWorkspaceSnapshot snapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new() { Scope = scope, AdmittedLibrary = evaluation, ActiveSubject = type },
                registry, NavigationSnapshotTestData.AllAvailable(registry));

        Assert.Equal(type, snapshot.ActiveSubject);
        Assert.Equal(library, snapshot.AdmittedContext!.Library);
        Assert.Equal(type, snapshot.AdmittedContext.Type);
        Assert.Null(snapshot.ActiveOccurrence);
        Assert.Null(snapshot.RetainedContext);
        Assert.Null(snapshot.Inventory);
        Assert.Empty(snapshot.Types);
        Assert.Contains(snapshot.AdmittedTypes, row => row.Subject == type && row.IsActive);
        Assert.Null(snapshot.Hierarchy.Single(row =>
            row.Kind == StructuralSubjectKind.Package).Subject);
        Assert.Equal(NavigationDescriptorState.Unavailable,
            snapshot.Hierarchy.Single(row =>
                row.Kind == StructuralSubjectKind.Package).State);
        Assert.Equal(type, snapshot.LensOutcome.Basis.Subject);
        Assert.Contains(snapshot.Lenses, row => row.IsEffective);
        var projection = new NavigationConsumerProjection(
            new("source", 0,
                ImmutableDictionary<StructuralSubjectIdentity, string>.Empty,
                ImmutableDictionary<NavigationLensIdentity, string>.Empty))
            .Build(snapshot, "source").Snapshot;
        Assert.Equal(projection.Workspace.Id,
            projection.Libraries.Single().Navigation.Subject!.Parent);
        Assert.Equal(projection.Libraries.Single().Navigation.Subject!.Id,
            projection.Types.Single(row =>
                row.Navigation.Subject!.Label == typeof(JsonElement).FullName)
                .Navigation.Subject!.Parent);
        Assert.Null(projection.Libraries.Single().Navigation.Action);
        Assert.Null(projection.Types.Single(row =>
            row.Navigation.Subject!.Label == typeof(JsonElement).FullName)
            .Navigation.Action);
        var exactLens = new NavigationLensIdentity(
            type, new ViewFacetId("type.api"));
        var restoration = new NavigationInitialization(
            type, Lens: exactLens)
        {
            AdmittedContext = new(library, type),
        };
        var facts = new NavigationEvaluationFacts(
            scope, Package: null, (_, _) =>
                NavigationSnapshotTestData.AllAvailable(registry))
        {
            AdmittedLibrary = evaluation,
        };
        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity, facts, registry, restoration));
        NavigationOperationInitialization initialized = prepared.Initialization;
        NavigationWorkspaceSnapshot retained = initialized.State.CurrentSnapshot;
        Assert.Equal(type, retained.ActiveSubject);
        Assert.Same(occurrence, retained.AdmittedEvaluation!.Occurrence);
        Assert.Same(inventory, retained.AdmittedInventory);
        Assert.Equal(type, retained.AdmittedContext!.Type);
        Assert.Contains(retained.AdmittedTypes, row => row.Subject == type);
        Assert.Equal(exactLens, retained.LensOutcome.EffectiveLens);
        Assert.Equal(NavigationOutcomeKind.Applied,
            initialized.Result.Consumer.Outcome.Kind);
        Assert.Equal(initialized.Result.Consumer.Authority,
            initialized.Result.LensResolution!.Authority);
        Assert.True(NavigationTransitions.ValidateAuthority(
            initialized.State, initialized.Result.Consumer.Authority));
        Assert.Equal(
            initialized.State.Snapshot.Libraries.Single().Navigation.Subject!.Id,
            initialized.State.Snapshot.Types.Single(row =>
                row.Navigation.Subject!.Label == typeof(JsonElement).FullName)
                .Navigation.Subject!.Parent);
        Assert.Null(initialized.State.Snapshot.Libraries.Single().Navigation.Action);
        Assert.All(initialized.State.Snapshot.Types,
            row => Assert.Null(row.Navigation.Action));
        Assert.All(initialized.State.Snapshot.Lenses,
            row => Assert.Null(row.Action));
        NavigationEffectAuthority authority =
            Assert.IsType<NavigationEffectAuthority>(
                initialized.Result.Consumer.Authority);
        NavigationState posted = NavigationTransitions.RecordConsumerPosting(
            initialized.State, authority).State;
        NavigationState acknowledged = NavigationTransitions.Acknowledge(
            posted, authority).State;
        NavigationTransition queued =
            NavigationTransitions.QueueMaintenance(acknowledged);
        NavigationTransition begun = NavigationTransitions.Advance(queued.State);
        NavigationEvaluationResult evaluationResult =
            NavigationTransitions.Evaluate(
                begun.Work!, new NavigationPreparation.Ready(facts), registry);
        NavigationTransition maintenance = NavigationTransitions.Complete(
            begun.State, begun.Work!, evaluationResult);
        Assert.Equal(NavigationOutcomeKind.Unavailable,
            maintenance.Result!.Consumer.Outcome.Kind);
        Assert.Same(retained, maintenance.State.CurrentSnapshot);
        NavigationRestorationPreparationResult.Prepared inferred =
            Assert.IsType<NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity, facts, registry,
                    restoration with { AdmittedContext = null }));
        Assert.Equal(type, inferred.Initialization.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal(library, inferred.Initialization.State.CurrentSnapshot
            .AdmittedContext!.Library);
        Assert.IsType<NavigationRestorationPreparationResult.Prepared>(
            NavigationTransitions.PrepareRestoration(
                workspace.Identity, facts, registry, new(library)));
        NavigationRestorationPreparationResult.Prepared retainedAtRoot =
            Assert.IsType<NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity, facts, registry,
                    new NavigationInitialization(root)
                    {
                        AdmittedContext = new(library, type),
                    }));
        Assert.Equal(root,
            retainedAtRoot.Initialization.State.CurrentSnapshot.ActiveSubject);
        Assert.Equal(type,
            retainedAtRoot.Initialization.State.CurrentSnapshot.AdmittedContext!.Type);
        Assert.True(retainedAtRoot.Initialization.State.Snapshot.Types.Single(
            row => row.Navigation.Subject!.Label == typeof(JsonElement).FullName)
            .Navigation.IsRetained);
        Assert.Empty(retainedAtRoot.Initialization.State.Data.Actions);

        foreach (bool failed in new[] { false, true })
        {
            ViewFacetAvailabilitySnapshot unavailableFacts = new(
                registry.Descriptors.Select(descriptor =>
                    new ViewFacetAvailabilityFact(descriptor.Id,
                        descriptor.Id == exactLens.Facet
                            ? failed
                                ? new ViewFacetAvailability.Failed(
                                    "Type API failed.", new SourceFacetDiagnostic())
                                : new ViewFacetAvailability.Unavailable(
                                    ViewFacetUnavailableReason.CapabilityAbsent(
                                        "Type API unavailable."))
                            : ViewFacetAvailability.Available.Instance)));
            var limited = facts with
            {
                Availability = (_, _) => unavailableFacts,
            };
            NavigationRestorationPreparationResult.Prepared nonEffective =
                Assert.IsType<NavigationRestorationPreparationResult.Prepared>(
                    NavigationTransitions.PrepareRestoration(
                        workspace.Identity, limited, registry, restoration));
            NavigationWorkspaceSnapshot result =
                nonEffective.Initialization.State.CurrentSnapshot;
            var basis = Assert.IsType<NavigationLensEvaluationBasis.ExactRequest>(
                result.LensOutcome.Basis);
            Assert.Equal(exactLens, basis.Request);
            Assert.Null(result.LensOutcome.EffectiveLens);
            Assert.Equal(failed ? NavigationOutcomeKind.Failed
                : NavigationOutcomeKind.Unavailable,
                nonEffective.Initialization.Result.Consumer.Outcome.Kind);
            Assert.Equal(nonEffective.Initialization.Result.Consumer.Authority,
                nonEffective.Initialization.Result.LensResolution!.Authority);
        }

        MetadataTypeDefinitionName missingName =
            ((MetadataTypeDefinitionNameResult.Valid)
                MetadataTypeDefinitionName.Create(
                    "System.Text.Json", ["NotAnActualDefinition"])).Name;
        Assert.Throws<ArgumentException>(() =>
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new() { Scope = scope, AdmittedLibrary = evaluation,
                    ActiveSubject = StructuralSubjectIdentity.ForAdmittedType(
                        library, missingName) },
                registry, NavigationSnapshotTestData.AllAvailable(registry)));

        Assert.Throws<ArgumentException>(() =>
            StructuralSubjectIdentity.ForAdmittedLibrary(
                StructuralSubjectIdentity.ForWorkspace(foreign.Identity),
                occurrence));
        WorkspaceScopeSnapshot foreignScope =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await foreign.GetScopeSnapshotAsync()).Snapshot;
        Assert.IsType<NavigationRestorationPreparationResult.Rejected>(
            NavigationTransitions.PrepareRestoration(
                foreign.Identity,
                facts with { Scope = foreignScope },
                registry, restoration));
        Assert.Throws<ArgumentException>(() =>
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new() { Scope = foreignScope, AdmittedLibrary = evaluation },
                registry, NavigationSnapshotTestData.AllAvailable(registry)));
    }

    [Fact]
    public async Task WorkspaceAdmission_EqualNamesRemainDistinctAndIncompleteInventoryCannotSelectType()
    {
        AssemblySource source = AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group = workspace.CreateAssemblyContextGroup(
            [new AssemblyContextParticipant(source.Assembly, new TestBindingPolicy())]);
        AssemblyContextParticipant participant = Assert.Single(group.Participants);
        var completed = Assert.IsType<AssemblyContextLibraryAdapterResult.Completed>(
            await AssemblyContextLibraryAdapter.MaterializeAsync(
                group, participant, AssemblyContextLibraryRole.ApiOnly,
                Limits(source.Bytes.Length),
                cancellationToken: TestContext.Current.CancellationToken));
        LibraryReference first = completed.Owner.Reference;
        var platformSource = new ExactLibrarySourceCoordinate.Platform(
            new PlatformLibraryPopulationDeclaration(PlatformFamily.DotNetRuntime),
            first.AssemblyCorrespondence.ApiIdentity);
        LibraryReference second = LibraryReference.CreateFromSource(
            platformSource,
            new LibraryAssemblyCorrespondence(
                first.ApiAssembly.ArtifactReference,
                first.AssemblyCorrespondence.ApiIdentity,
                first.ApiAssembly.ArtifactReference,
                first.AssemblyCorrespondence.ApiIdentity));
        ArtifactQueryAuthorization authorization =
            completed.Artifacts.CreateQueryAuthorization();
        using ArtifactQueryLease query =
            completed.Artifacts.IssueLease(authorization);
        var secondOwner = new LibraryContentOwner(second,
            [completed.Artifacts.IssueContentLease(
                first.ApiAssembly.ArtifactReference, query)]);
        WorkspaceLibraryAdmissionReceipt receipt =
            Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
                await workspace.AdmitLibraryBatchAsync(
                    CurrentRegistrations(workspace), completed.Artifacts,
                    [completed.Owner, secondOwner])).Receipt;
        WorkspaceLibraryOccurrence left = receipt.Occurrences[0];
        WorkspaceLibraryOccurrence right = receipt.Occurrences[1];
        StructuralSubjectIdentity.WorkspaceSubject root =
            StructuralSubjectIdentity.ForWorkspace(workspace.Identity);
        StructuralSubjectIdentity.AdmittedLibrarySubject leftLibrary =
            StructuralSubjectIdentity.ForAdmittedLibrary(root, left);
        StructuralSubjectIdentity.AdmittedLibrarySubject rightLibrary =
            StructuralSubjectIdentity.ForAdmittedLibrary(root, right);
        MetadataTypeDefinitionName name =
            ((MetadataTypeDefinitionNameResult.Valid)
                MetadataTypeDefinitionName.Create("System.Text.Json", ["JsonElement"]))
            .Name;
        StructuralSubjectIdentity.AdmittedTypeSubject leftType =
            StructuralSubjectIdentity.ForAdmittedType(leftLibrary, name);
        StructuralSubjectIdentity.AdmittedTypeSubject rightType =
            StructuralSubjectIdentity.ForAdmittedType(rightLibrary, name);
        Assert.NotEqual(leftLibrary, rightLibrary);
        Assert.NotEqual(leftType, rightType);
        Assert.Null(leftLibrary.SourceCoordinate);
        Assert.Same(platformSource, rightLibrary.SourceCoordinate);

        using LibraryOperationLease lease =
            Assert.IsType<WorkspaceLibraryOperationIssueOutcome.Issued>(
                workspace.IssueLibraryOperation(left)).Lease;
        var incomplete = Assert.IsType<LibraryTypeDeclarationInventoryInspectionOutcome.Incomplete>(
            LibraryTypeDeclarationInventoryInspection.Execute(
                new(first, new(source.Bytes.Length, 0, 1_000_000, 10_000_000)),
                lease, TestContext.Current.CancellationToken));
        WorkspaceScopeSnapshot scope =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationAdmittedLibraryEvaluation evaluation = new(receipt, left, incomplete);
        NavigationWorkspaceSnapshot snapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new() { Scope = scope, AdmittedLibrary = evaluation },
                registry, NavigationSnapshotTestData.AllAvailable(registry));
        Assert.Same(incomplete, snapshot.AdmittedInventory);
        Assert.Empty(snapshot.AdmittedTypes);
        Assert.Equal(NavigationDescriptorState.Failed,
            snapshot.Hierarchy.Single(row => row.Kind == StructuralSubjectKind.Type).State);
        var projected = new NavigationConsumerProjection(
            new("incomplete", 0,
                ImmutableDictionary<StructuralSubjectIdentity, string>.Empty,
                ImmutableDictionary<NavigationLensIdentity, string>.Empty))
            .Build(snapshot, "incomplete").Snapshot;
        Assert.Contains(projected.Diagnostics, diagnostic =>
            diagnostic.Kind == NavigationDiagnosticKind.LibraryInventoryIncomplete
            && diagnostic.Message.Contains(nameof(
                LibraryTypeDeclarationInventoryInspectionBound.RetainedDeclarations),
                StringComparison.Ordinal));
        Assert.Contains(projected.Hierarchy.Single(row =>
            row.Kind == StructuralSubjectKind.Type).Evidence,
            diagnostic =>
                diagnostic.Kind == NavigationDiagnosticKind.LibraryInventoryIncomplete);
        NavigationWorkspaceSnapshot detached =
            NavigationSnapshotDetachment.Detach(snapshot);
        Assert.NotSame(snapshot, detached);
        Assert.Same(incomplete, detached.AdmittedInventory);
        Assert.Same(evaluation, detached.AdmittedEvaluation);
        var detachedProjection = new NavigationConsumerProjection(
            new("detached", 0,
                ImmutableDictionary<StructuralSubjectIdentity, string>.Empty,
                ImmutableDictionary<NavigationLensIdentity, string>.Empty))
            .Build(detached, "detached");
        Assert.Empty(detachedProjection.Actions);
        Assert.Equal(projected.Diagnostics.Select(row => row.Kind),
            detachedProjection.Snapshot.Diagnostics.Select(row => row.Kind));
        var incompleteRequest = new NavigationInitialization(leftType)
        {
            AdmittedContext = new(leftLibrary, leftType),
        };
        var incompleteFacts = new NavigationEvaluationFacts(
            scope, Package: null, (_, _) =>
                NavigationSnapshotTestData.AllAvailable(registry))
        {
            AdmittedLibrary = evaluation,
        };
        NavigationRestorationPreparationResult.Failed cannotRestore =
            Assert.IsType<NavigationRestorationPreparationResult.Failed>(
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity, incompleteFacts, registry,
                    incompleteRequest));
        Assert.Equal(NavigationRestorationFailureKind.IncompleteInventory,
            cannotRestore.Kind);
        Assert.Same(incomplete, cannotRestore.AdmittedInventory);
        Assert.Equal(leftType, cannotRestore.Subject);
        Assert.IsType<NavigationRestorationPreparationResult.Rejected>(
            NavigationTransitions.PrepareRestoration(
                workspace.Identity, incompleteFacts, registry,
                new NavigationInitialization(rightType)
                {
                    AdmittedContext = new(rightLibrary, rightType),
                }));
        Assert.Throws<ArgumentException>(() =>
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new() { Scope = scope, AdmittedLibrary = evaluation, ActiveSubject = leftType },
                registry, NavigationSnapshotTestData.AllAvailable(registry)));
        Assert.Throws<ArgumentException>(() =>
            new NavigationAdmittedLibraryEvaluation(receipt, right, incomplete));

        var firstNames = Assert.IsType<LibraryTypeDeclarationInventoryInspectionOutcome.Completed>(
            LibraryTypeDeclarationInventoryInspection.Execute(
                new(first, new(source.Bytes.Length, 100_000, 1_000_000, 10_000_000)),
                lease, TestContext.Current.CancellationToken));
        var noInventory = incompleteFacts with
        {
            AdmittedLibrary = new(receipt, left),
        };
        NavigationRestorationPreparationResult.Failed unpreparedType =
            Assert.IsType<NavigationRestorationPreparationResult.Failed>(
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity, noInventory, registry, incompleteRequest));
        Assert.Equal(NavigationRestorationFailureKind.LibraryInventoryNotPrepared,
            unpreparedType.Kind);
        Assert.Null(unpreparedType.AdmittedInventory);
        MetadataTypeDefinitionName missing =
            ((MetadataTypeDefinitionNameResult.Valid)
                MetadataTypeDefinitionName.Create(
                    "System.Text.Json", ["NotAnActualDefinition"])).Name;
        StructuralSubjectIdentity.AdmittedTypeSubject absent =
            StructuralSubjectIdentity.ForAdmittedType(leftLibrary, missing);
        NavigationRestorationPreparationResult.Unavailable missingType =
            Assert.IsType<NavigationRestorationPreparationResult.Unavailable>(
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity,
                    incompleteFacts with
                    {
                        AdmittedLibrary = new(receipt, left, firstNames),
                    },
                    registry, new NavigationInitialization(absent)
                    {
                        AdmittedContext = new(leftLibrary, absent),
                    }));
        Assert.Equal(absent, missingType.Subject);

        using LibraryOperationLease rightLease =
            Assert.IsType<WorkspaceLibraryOperationIssueOutcome.Issued>(
                workspace.IssueLibraryOperation(right)).Lease;
        var rejectedNames = Assert.IsType<LibraryTypeDeclarationInventoryInspectionOutcome.Rejected>(
            LibraryTypeDeclarationInventoryInspection.Execute(
                new(first, new(source.Bytes.Length, 100_000, 1_000_000, 10_000_000)),
                rightLease, TestContext.Current.CancellationToken));
        NavigationRestorationPreparationResult.Failed rejectedType =
            Assert.IsType<NavigationRestorationPreparationResult.Failed>(
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity,
                    incompleteFacts with
                    {
                        AdmittedLibrary = new(receipt, left, rejectedNames),
                    },
                    registry, incompleteRequest));
        Assert.Equal(NavigationRestorationFailureKind.LibraryInventoryUnsuccessful,
            rejectedType.Kind);
        Assert.Same(rejectedNames, rejectedType.AdmittedInventory);
        var rightNames = Assert.IsType<LibraryTypeDeclarationInventoryInspectionOutcome.Completed>(
            LibraryTypeDeclarationInventoryInspection.Execute(
                new(second, new(source.Bytes.Length, 100_000, 1_000_000, 10_000_000)),
                rightLease, TestContext.Current.CancellationToken));
        NavigationWorkspaceSnapshot firstSnapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new() { Scope = scope,
                    AdmittedLibrary = new(receipt, left, firstNames),
                    ActiveSubject = leftType },
                registry, NavigationSnapshotTestData.AllAvailable(registry));
        NavigationWorkspaceSnapshot secondSnapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new() { Scope = scope,
                    AdmittedLibrary = new(receipt, right, rightNames),
                    ActiveSubject = rightType },
                registry, NavigationSnapshotTestData.AllAvailable(registry));
        Assert.NotEqual(firstSnapshot.ActiveSubject, secondSnapshot.ActiveSubject);
        Assert.NotEqual(firstSnapshot.LensOutcome.EffectiveLens,
            secondSnapshot.LensOutcome.EffectiveLens);
        Assert.Null(secondSnapshot.Hierarchy.Single(row =>
            row.Kind == StructuralSubjectKind.Package).Subject);
    }

    [Fact]
    public async Task
        WorkspaceAdmission_OwnsOperationsAndRetiresOwnersBeforeArtifacts()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        var workspace = new InspectionWorkspace();
        var participant =
            new AssemblyContextParticipant(
                source.Assembly,
                new TestBindingPolicy());
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        WorkspaceRegistrationRevision registrations =
            CurrentRegistrations(workspace);

        var accepted = Assert.IsType<
            WorkspaceLibraryAdmissionOutcome.Accepted>(
                await workspace.AdmitLibraryBatchAsync(
                    registrations,
                    completed.Artifacts,
                    [completed.Owner]));
        WorkspaceLibraryOccurrence occurrence =
            Assert.Single(accepted.Receipt.Occurrences);
        WorkspaceLibraryAdmissionRelation relation =
            Assert.Single(accepted.Receipt.LibraryRelations);
        Assert.Same(workspace.Identity, relation.Workspace);
        Assert.Same(workspace.Identity, relation.Identity.Workspace);
        Assert.Same(accepted.Receipt.Identity, relation.Admission);
        Assert.Same(occurrence, relation.Library);
        var issued = Assert.IsType<
            WorkspaceLibraryOperationIssueOutcome.Issued>(
                workspace.IssueLibraryOperation(occurrence));

        source.Disable();
        Task<InspectionWorkspaceCloseReport> close =
            workspace.CloseAsync();
        Assert.False(close.IsCompleted);
        Assert.True(
            issued.Lease.Snapshot(
                occurrence.Library.ApiAssembly,
                source.Bytes,
                static (view, expected, _) =>
                    view.Content.SequenceEqual(expected),
                TestContext.Current.CancellationToken));
        issued.Lease.Dispose();

        InspectionWorkspaceCloseReport report =
            await close.WaitAsync(
                TestContext.Current.CancellationToken);
        WorkspaceLibraryAdmissionCloseResult admissionClose =
            Assert.Single(report.LibraryAdmissions);
        Assert.Same(accepted.Receipt, admissionClose.Admission);
        Assert.True(admissionClose.Succeeded);
        Assert.Empty(report.ArtifactSessionCleanupFailures);
        Assert.True(report.Succeeded);
        Assert.Equal(
            LibraryContentOwnerState.Released,
            completed.Owner.State);
    }

    [Fact]
    public async Task
        WorkspaceAdmission_BatchPreservesOccurrenceRelationOrder()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        source.Assembly,
                        new TestBindingPolicy()),
                ]);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        ArtifactContentReference content =
            completed.Owner.Reference.ApiAssembly.ArtifactReference;
        var secondIdentity =
            new ManagedMetadataIdentity.Assembly(
                new AssemblyReferenceIdentity(
                    "Workspace.Admission.Second",
                    new Version(1, 0, 0, 0),
                    Culture: null,
                    PublicKeyToken: null));
        LibraryReference secondLibrary =
            LibraryReference.CreateDirect(
                new LibraryAssemblyCorrespondence(
                    content,
                    secondIdentity,
                    content,
                    secondIdentity));
        ArtifactQueryAuthorization authorization =
            completed.Artifacts.CreateQueryAuthorization();
        using ArtifactQueryLease queryLease =
            completed.Artifacts.IssueLease(authorization);
        var secondOwner =
            new LibraryContentOwner(
                secondLibrary,
                [
                    completed.Artifacts.IssueContentLease(
                        content,
                        queryLease),
                ]);

        WorkspaceLibraryAdmissionReceipt receipt =
            Assert.IsType<
                    WorkspaceLibraryAdmissionOutcome.Accepted>(
                        await workspace.AdmitLibraryBatchAsync(
                            CurrentRegistrations(workspace),
                            completed.Artifacts,
                            [completed.Owner, secondOwner]))
                .Receipt;

        Assert.Equal(2, receipt.Occurrences.Length);
        Assert.Equal(2, receipt.LibraryRelations.Length);
        Assert.Same(
            completed.Owner.Reference,
            receipt.LibraryRelations[0].Library.Library);
        Assert.Same(
            secondOwner.Reference,
            receipt.LibraryRelations[1].Library.Library);
        Assert.Same(
            receipt.Occurrences[0],
            receipt.LibraryRelations[0].Library);
        Assert.Same(
            receipt.Occurrences[1],
            receipt.LibraryRelations[1].Library);
        Assert.NotSame(
            receipt.LibraryRelations[0].Identity,
            receipt.LibraryRelations[1].Identity);
    }

    [Fact]
    public async Task
        LibraryDeclarationInventoriesRemainResidentAcrossBoundedAttempts()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        source.Assembly,
                        new TestBindingPolicy()),
                ]);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        LibraryReference firstLibrary = completed.Owner.Reference;
        ManagedMetadataIdentity.Assembly identity =
            Assert.IsType<ManagedMetadataIdentity.Assembly>(
                firstLibrary.ApiAssembly.AssemblyIdentity);
        ArtifactContentReference content =
            firstLibrary.ApiAssembly.ArtifactReference;
        LibraryReference secondLibrary =
            LibraryReference.CreateDirect(
                new LibraryAssemblyCorrespondence(
                    content,
                    identity,
                    content,
                    identity));
        ArtifactQueryAuthorization authorization =
            completed.Artifacts.CreateQueryAuthorization();
        using ArtifactQueryLease queryLease =
            completed.Artifacts.IssueLease(authorization);
        var secondOwner =
            new LibraryContentOwner(
                secondLibrary,
                [
                    completed.Artifacts.IssueContentLease(
                        content,
                        queryLease),
                ]);
        WorkspaceLibraryAdmissionReceipt receipt =
            Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
                    await workspace.AdmitLibraryBatchAsync(
                        CurrentRegistrations(workspace),
                        completed.Artifacts,
                        [completed.Owner, secondOwner]))
                .Receipt;
        var target = new PlatformFamilyTarget(
            PlatformFamily.DotNetRuntime,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        WorkspaceLibraryDeclarationContextAdmissionOutcome admission =
            WorkspaceLibraryDeclarationContextAdmission.Admit(
                workspace,
                receipt,
                new WorkspaceDeclarationRequest.PlatformPopulation(
                    target),
                receipt.Occurrences.Select(
                    occurrence =>
                        new WorkspaceLibraryDeclarationContextMember(
                            new ExactLibrarySourceCoordinate.Local(
                                identity),
                            identity.Identity,
                            new WorkspaceDeclarationOrigin
                                .PlatformPopulation(
                                    target,
                                    WorkspacePlatformPopulationMemberRole
                                        .Focus,
                                    "resident test",
                                    identity.Identity.Name),
                            AssemblyResolutionProvenance.Local(
                                "resident test"),
                            packageRequest: null))
                    .ToArray(),
                new LibraryTypeDeclarationInventoryInspectionBounds(
                    source.Bytes.Length,
                    maximumRetainedDeclarations: 100_000,
                    maximumMetadataRows: 1_000_000,
                    maximumRetainedTextCharacters: 10_000_000));
        Assert.IsType<
            WorkspaceLibraryDeclarationContextAdmissionOutcome.Admitted>(
                admission);
        WorkspaceDeclarationLocator locator =
            workspace.GetDeclarationLocator(
                new()
                {
                    MaxInventoryReadsPerAttempt = 1,
                    MaxRetainedInventories = 2,
                });

        var first = Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await locator.ExecuteAsync(
                [new TypeDeclarationLocatorRequest.Pattern("*")],
                includeAll: true,
                TestContext.Current.CancellationToken));
        Assert.False(first.Answers[0].IsComplete);
        Assert.Equal(1, locator.InventoryReadCount);
        Assert.Equal(1, locator.RetainedInventoryCount);

        var second = Assert.IsType<TypeDeclarationLocatorResult.Evaluated>(
            await locator.ExecuteAsync(
                [new TypeDeclarationLocatorRequest.Pattern("*")],
                includeAll: true,
                TestContext.Current.CancellationToken));
        Assert.True(second.Answers[0].IsComplete);
        Assert.Equal(2, locator.InventoryReadCount);
        Assert.Equal(2, locator.RetainedInventoryCount);
    }

    [Fact]
    public async Task
        WorkspaceAdmission_RepeatedLibraryAdmissionIssuesDistinctRelations()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        source.Assembly,
                        new TestBindingPolicy()),
                ]);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);

        async Task<WorkspaceLibraryAdmissionReceipt> AdmitAsync()
        {
            var completed = Assert.IsType<
                AssemblyContextLibraryAdapterResult.Completed>(
                    await AssemblyContextLibraryAdapter.MaterializeAsync(
                        group,
                        participant,
                        AssemblyContextLibraryRole.ApiOnly,
                        Limits(source.Bytes.Length),
                        cancellationToken:
                            TestContext.Current.CancellationToken));
            return Assert.IsType<
                    WorkspaceLibraryAdmissionOutcome.Accepted>(
                        await workspace.AdmitLibraryBatchAsync(
                            CurrentRegistrations(workspace),
                            completed.Artifacts,
                            [completed.Owner]))
                .Receipt;
        }

        WorkspaceLibraryAdmissionReceipt first = await AdmitAsync();
        WorkspaceLibraryAdmissionReceipt second = await AdmitAsync();
        WorkspaceLibraryAdmissionRelation firstRelation =
            Assert.Single(first.LibraryRelations);
        WorkspaceLibraryAdmissionRelation secondRelation =
            Assert.Single(second.LibraryRelations);

        Assert.Same(workspace.Identity, firstRelation.Workspace);
        Assert.Same(workspace.Identity, secondRelation.Workspace);
        Assert.NotSame(first.Identity, second.Identity);
        Assert.NotSame(firstRelation.Identity, secondRelation.Identity);
        Assert.NotSame(firstRelation.Library, secondRelation.Library);
    }

    [Fact]
    public async Task
        WorkspaceAdmission_CloseRejectsNewAdmissionAndOperationIssuance()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        var workspace = new InspectionWorkspace();
        var participant =
            new AssemblyContextParticipant(
                source.Assembly,
                new TestBindingPolicy());
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);

        async Task<AssemblyContextLibraryAdapterResult.Completed>
            MaterializeAsync() =>
                Assert.IsType<
                    AssemblyContextLibraryAdapterResult.Completed>(
                        await AssemblyContextLibraryAdapter
                            .MaterializeAsync(
                                group,
                                participant,
                                AssemblyContextLibraryRole.ApiOnly,
                                Limits(source.Bytes.Length),
                                cancellationToken:
                                    TestContext.Current
                                        .CancellationToken));

        AssemblyContextLibraryAdapterResult.Completed acceptedBatch =
            await MaterializeAsync();
        AssemblyContextLibraryAdapterResult.Completed closingBatch =
            await MaterializeAsync();
        AssemblyContextLibraryAdapterResult.Completed closedBatch =
            await MaterializeAsync();
        WorkspaceRegistrationRevision registrations =
            CurrentRegistrations(workspace);
        var accepted = Assert.IsType<
            WorkspaceLibraryAdmissionOutcome.Accepted>(
                await workspace.AdmitLibraryBatchAsync(
                    registrations,
                    acceptedBatch.Artifacts,
                    [acceptedBatch.Owner]));
        WorkspaceLibraryOccurrence occurrence =
            Assert.Single(accepted.Receipt.Occurrences);
        LibraryOperationLease activeLease =
            Assert.IsType<
                WorkspaceLibraryOperationIssueOutcome.Issued>(
                    workspace.IssueLibraryOperation(
                        occurrence))
                .Lease;
        Task<InspectionWorkspaceCloseReport>? close = null;

        try
        {
            close = workspace.CloseAsync();
            Assert.False(close.IsCompleted);

            var closingOperation = Assert.IsType<
                WorkspaceLibraryOperationIssueOutcome.Rejected>(
                    workspace.IssueLibraryOperation(
                        occurrence));
            Assert.Equal(
                WorkspaceLibraryOperationRejection
                    .WorkspaceClosing,
                closingOperation.Reason);

            var closingAdmission = Assert.IsType<
                WorkspaceLibraryAdmissionOutcome.Rejected>(
                    await workspace.AdmitLibraryBatchAsync(
                        registrations,
                        closingBatch.Artifacts,
                        [closingBatch.Owner]));
            Assert.Equal(
                WorkspaceLibraryAdmissionRejection
                    .WorkspaceClosing,
                closingAdmission.Reason);
            Assert.Equal(
                LibraryContentOwnerState.Released,
                closingBatch.Owner.State);
            Assert.Empty(
                closingBatch.Artifacts.CleanupFailures);
        }
        finally
        {
            activeLease.Dispose();
        }

        InspectionWorkspaceCloseReport report =
            await close.WaitAsync(
                TestContext.Current.CancellationToken);
        Assert.True(report.Succeeded);

        var closedOperation = Assert.IsType<
            WorkspaceLibraryOperationIssueOutcome.Rejected>(
                workspace.IssueLibraryOperation(
                    occurrence));
        Assert.Equal(
            WorkspaceLibraryOperationRejection.WorkspaceClosed,
            closedOperation.Reason);

        var closedAdmission = Assert.IsType<
            WorkspaceLibraryAdmissionOutcome.Rejected>(
                await workspace.AdmitLibraryBatchAsync(
                    registrations,
                    closedBatch.Artifacts,
                    [closedBatch.Owner]));
        Assert.Equal(
            WorkspaceLibraryAdmissionRejection.WorkspaceClosed,
            closedAdmission.Reason);
        Assert.Equal(
            LibraryContentOwnerState.Released,
            closedBatch.Owner.State);
        Assert.Empty(
            closedBatch.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        WorkspaceAdmission_RejectsStaleRevisionAndSettlesTransferredResources()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        source.Assembly,
                        new TestBindingPolicy()),
                ]);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        WorkspaceRegistrationRevision stale =
            CurrentRegistrations(workspace);
        var changed = Assert.IsType<
            WorkspaceRegistrationOperationResult.Committed>(
                workspace.ReplaceRegistrations(
                    stale,
                    [
                        new WorkspaceRegistration.PackagePrefix(
                            new("Microsoft.Extensions.")),
                    ]));

        var rejected = Assert.IsType<
            WorkspaceLibraryAdmissionOutcome.Rejected>(
                await workspace.AdmitLibraryBatchAsync(
                    stale,
                    completed.Artifacts,
                    [completed.Owner]));

        Assert.Equal(
            WorkspaceLibraryAdmissionRejection.RevisionMismatch,
            rejected.Reason);
        Assert.Same(
            changed.Revision,
            rejected.CurrentRevision);
        Assert.Equal(
            LibraryContentOwnerState.Released,
            completed.Owner.State);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        WorkspaceAdmission_RejectsForeignRevisionAndSettlesTransferredResources()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var materializationWorkspace =
            new InspectionWorkspace();
        using AssemblyContextGroup group =
            materializationWorkspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        source.Assembly,
                        new TestBindingPolicy()),
                ]);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        await using var target = new InspectionWorkspace();

        var rejected = Assert.IsType<
            WorkspaceLibraryAdmissionOutcome.Rejected>(
                await target.AdmitLibraryBatchAsync(
                    CurrentRegistrations(materializationWorkspace),
                    completed.Artifacts,
                    [completed.Owner]));

        Assert.Equal(
            WorkspaceLibraryAdmissionRejection.ForeignWorkspace,
            rejected.Reason);
        Assert.Same(
            CurrentRegistrations(target),
            rejected.CurrentRevision);
        Assert.Equal(
            LibraryContentOwnerState.Released,
            completed.Owner.State);
        Assert.Empty(completed.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        WorkspaceAdmission_RegistrationChangesDoNotRevokeAcceptedOccurrence()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        source.Assembly,
                        new TestBindingPolicy()),
                ]);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        WorkspaceRegistrationRevision admittedAgainst =
            CurrentRegistrations(workspace);
        var accepted = Assert.IsType<
            WorkspaceLibraryAdmissionOutcome.Accepted>(
                await workspace.AdmitLibraryBatchAsync(
                    admittedAgainst,
                    completed.Artifacts,
                    [completed.Owner]));
        WorkspaceLibraryOccurrence occurrence =
            Assert.Single(accepted.Receipt.Occurrences);
        WorkspaceLibraryAdmissionRelation relation =
            Assert.Single(accepted.Receipt.LibraryRelations);
        Assert.IsType<
            WorkspaceRegistrationOperationResult.Committed>(
                workspace.ReplaceRegistrations(
                    admittedAgainst,
                    [
                        new WorkspaceRegistration.PackagePrefix(
                            new("Microsoft.Extensions.")),
                    ]));

        using LibraryOperationLease operation =
            Assert.IsType<
                WorkspaceLibraryOperationIssueOutcome.Issued>(
                    workspace.IssueLibraryOperation(
                        occurrence))
                .Lease;
        Assert.True(
            operation.Snapshot(
                occurrence.Library.ApiAssembly,
                source.Bytes,
                static (view, expected, _) =>
                    view.Content.SequenceEqual(expected),
                TestContext.Current.CancellationToken));
        Assert.Same(
            relation,
            Assert.Single(accepted.Receipt.LibraryRelations));
        Assert.Same(occurrence, relation.Library);
    }

    [Fact]
    public async Task
        WorkspaceAdmission_SameWorkspaceDuplicateDoesNotRetireAcceptedResources()
    {
        AssemblySource source =
            AssemblySource.FromPathlessRuntimeImage();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        source.Assembly,
                        new TestBindingPolicy()),
                ]);
        AssemblyContextParticipant participant =
            Assert.Single(group.Participants);
        var completed = Assert.IsType<
            AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    group,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    Limits(source.Bytes.Length),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        WorkspaceRegistrationRevision admittedAgainst =
            CurrentRegistrations(workspace);
        var accepted = Assert.IsType<
            WorkspaceLibraryAdmissionOutcome.Accepted>(
                await workspace.AdmitLibraryBatchAsync(
                    admittedAgainst,
                    completed.Artifacts,
                    [completed.Owner]));
        Assert.IsType<
            WorkspaceRegistrationOperationResult.Committed>(
                workspace.ReplaceRegistrations(
                    admittedAgainst,
                    [
                        new WorkspaceRegistration.PackagePrefix(
                            new("Microsoft.Extensions.")),
                    ]));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
                await workspace.AdmitLibraryBatchAsync(
                    admittedAgainst,
                    completed.Artifacts,
                    [completed.Owner]));

        using LibraryOperationLease operation =
            Assert.IsType<
                WorkspaceLibraryOperationIssueOutcome.Issued>(
                    workspace.IssueLibraryOperation(
                        Assert.Single(
                            accepted.Receipt.Occurrences)))
                .Lease;
        Assert.Equal(
            LibraryContentOwnerState.Active,
            completed.Owner.State);
    }

    [Fact]
    public async Task
        WorkspaceAdmission_CleanupFailureProducesTypedFailedOutcome()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceRegistrationRevision stale =
            CurrentRegistrations(workspace);
        Assert.IsType<
            WorkspaceRegistrationOperationResult.Committed>(
                workspace.ReplaceRegistrations(
                    stale,
                    [
                        new WorkspaceRegistration.PackagePrefix(
                            new("Microsoft.Extensions.")),
                    ]));
        (
            ArtifactSetSession artifacts,
            LibraryContentOwner owner) =
                await CreateFailingBatchAsync();

        var failed = Assert.IsType<
            WorkspaceLibraryAdmissionOutcome.Failed>(
                await workspace.AdmitLibraryBatchAsync(
                    stale,
                    artifacts,
                    [owner]));

        Assert.Equal(
            WorkspaceLibraryAdmissionRejection.RevisionMismatch,
            failed.Reason);
        WorkspaceLibraryAdmissionCleanupFailure cleanup =
            Assert.Single(failed.CleanupFailures);
        Assert.Equal(
            WorkspaceLibraryAdmissionCleanupFailureKind
                .ArtifactSession,
            cleanup.Kind);
        Assert.Null(cleanup.Library);
        Assert.Equal(
            "Synthetic Artifact retirement failure.",
            cleanup.Failure.Message);
        Assert.Equal(
            LibraryContentOwnerState.Released,
            owner.State);
    }

    static WorkspaceRegistrationRevision CurrentRegistrations(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                workspace.GetRegistrationSnapshot())
            .Revision;

    static async Task<(
        ArtifactSetSession Artifacts,
        LibraryContentOwner Owner)> CreateFailingBatchAsync()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        var artifacts = new ArtifactSetSession();
        await artifacts.AddRequiredAcquisitionAsync(
            (scope, _) =>
            {
                ArtifactContribution contribution =
                    scope.Register(
                        new AdmissionProvenance(),
                        static _ =>
                            new MemoryStream(
                                [0],
                                writable: false));
                return ValueTask.FromResult<
                    ArtifactAcquisitionOutcome>(
                        new ArtifactAcquisitionOutcome.Acquired(
                            [contribution],
                            new ThrowingAcquisitionLease()));
            },
            cancellationToken: cancellationToken);
        Assert.IsType<ArtifactSetPublicationOutcome.Published>(
            await artifacts.SealAsync(cancellationToken));
        ArtifactQueryAuthorization authorization =
            artifacts.CreateQueryAuthorization();
        using ArtifactQueryLease queryLease =
            artifacts.IssueLease(authorization);
        ArtifactDescriptor descriptor =
            Assert.Single(artifacts.GetCatalog(queryLease));
        ArtifactContentReference content =
            artifacts.GetContentReference(
                descriptor.Identity,
                queryLease);
        var identity =
            new ManagedMetadataIdentity.Assembly(
                new AssemblyReferenceIdentity(
                    "Workspace.Admission.Failure",
                    new Version(1, 0, 0, 0),
                    Culture: null,
                    PublicKeyToken: null));
        LibraryReference library =
            LibraryReference.CreateDirect(
                new LibraryAssemblyCorrespondence(
                    content,
                    identity,
                    content,
                    identity));
        var owner =
            new LibraryContentOwner(
                library,
                [
                    artifacts.IssueContentLease(
                        content,
                        queryLease),
                ]);
        return (artifacts, owner);
    }

    sealed record AdmissionProvenance : IArtifactProvenance;

    sealed class ThrowingAcquisitionLease :
        IArtifactAcquisitionLease
    {
        public ValueTask DisposeAsync() =>
            ValueTask.FromException(
                new IOException(
                    "Synthetic Artifact retirement failure."));
    }
}
