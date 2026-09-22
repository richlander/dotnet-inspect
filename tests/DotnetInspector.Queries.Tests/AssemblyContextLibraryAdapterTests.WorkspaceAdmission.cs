using DotnetInspector.Libraries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextLibraryAdapterTests
{
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
