using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.Json;
using DotnetInspector.Libraries;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;
using Inspector.Resources;

namespace DotnetInspector.EcosystemLoading.Tests;

public sealed class EcosystemPopulationLoadingTests
{
    [Theory]
    [InlineData("ecosystem-loader.runtime")]
    [InlineData("ecosystem-loader.aspnetcore")]
    [InlineData("ecosystem-loader.a1-b2")]
    public void LoaderIdsAcceptCanonicalValues(string value)
    {
        EcosystemPopulationLoaderId id =
            EcosystemPopulationLoaderId.Create(value);

        Assert.Equal(value, id.Value);
        Assert.Equal(id, EcosystemPopulationLoaderId.Create(value));
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("ecosystem.runtime")]
    [InlineData("ecosystem-loader.Dotnet")]
    [InlineData("ecosystem-loader.-dotnet")]
    [InlineData("ecosystem-loader.runtime-")]
    [InlineData("ecosystem-loader.runtime--platform")]
    [InlineData("ecosystem-loader.runtime.platform")]
    public void LoaderIdsRejectNonCanonicalValues(string value)
    {
        Assert.False(
            EcosystemPopulationLoaderId.TryCreate(value, out _));
        Assert.Throws<ArgumentException>(
            () => EcosystemPopulationLoaderId.Create(value));
    }

    [Fact]
    public void BindingRequiresOneTargetFreeStaticMethod()
    {
        EcosystemPopulationLoaderId id =
            EcosystemPopulationLoaderId.Create("ecosystem-loader.test");
        EcosystemPopulationLoaderBinding<TestInputs> binding =
            EcosystemPopulationLoaderBinding.Create<TestInputs>(
                id,
                LoadAsync);

        Assert.Same(id, binding.Id);

        int captured = 0;
        Assert.Throws<ArgumentException>(
            () => EcosystemPopulationLoaderBinding.Create<TestInputs>(
                id,
                request =>
                {
                    captured++;
                    return LoadAsync(request);
                }));
        Assert.Equal(0, captured);

        Func<
            EcosystemPopulationLoadRequest<TestInputs>,
            ValueTask<EcosystemPopulationLoaderReply>> combined = LoadAsync;
        combined += LoadAsync;
        Assert.Throws<ArgumentException>(
            () => EcosystemPopulationLoaderBinding.Create(id, combined));
    }

    [Fact]
    public async Task SelectionRequiresExactRegistrationCorrespondence()
    {
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        WorkspaceEcosystemRegistrationDeclaration retained =
            Declaration("ecosystem.test");
        WorkspaceEcosystemRegistrationDeclaration equalButDistinct =
            Declaration("ecosystem.test");
        await using var workspace = new InspectionWorkspace(
            new WorkspacePlan(
                ImmutableArray.Create<WorkspaceRegistration>(
                    new WorkspaceRegistration.Ecosystem(retained))));
        WorkspaceRegistrationRevision revision = Read(workspace);

        EcosystemPopulationLoaderCorrespondence exact =
            binding.CreateCorrespondence(retained);
        EcosystemPopulationLoaderCorrespondence mismatch =
            binding.CreateCorrespondence(equalButDistinct);

        EcosystemPopulationLoaderSelection exactSelection =
            exact.Select(
                revision,
                retained,
                EcosystemPopulationDemand.WholePopulation.Instance);
        Assert.Same(retained, exact.Registration);
        Assert.Same(binding, exact.Binding);
        Assert.Same(
            binding,
            Assert.IsAssignableFrom<
                EcosystemPopulationLoaderSelection.Known>(
                    exactSelection).Binding);
        Assert.IsType<
            EcosystemPopulationLoaderSelection.Known<TestInputs>>(
                exactSelection);
        var rejected =
            Assert.IsType<EcosystemPopulationLoaderSelection.Rejected>(
                mismatch.Select(
                    revision,
                    retained,
                    EcosystemPopulationDemand.WholePopulation.Instance));
        Assert.Same(retained, rejected.Registration);
        Assert.Equal(
            "ecosystem-loader.registration-mismatch",
            Assert.Single(rejected.Diagnostics).Code);

        var unavailable =
            EcosystemPopulationLoaderSelection.UnavailableFor(
                revision,
                retained,
                EcosystemPopulationDemand.WholePopulation.Instance,
                [Diagnostic("ecosystem-loader.unavailable")]);
        Assert.Same(revision, unavailable.Revision);
        Assert.Same(retained, unavailable.Registration);
    }

    [Fact]
    public async Task BoundRequestInvokesOnceAndRetainsExactAssociation()
    {
        TestInputs inputs = new(LoadMode.CompletedNoMembers);
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture fixture =
            WorkspaceFixture.Create(binding);
        EcosystemPopulationLoadRequest<TestInputs> request =
            fixture.Request(
                inputs,
                TestContext.Current.CancellationToken);

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(request));

        Assert.Equal(1, inputs.InvocationCount);
        Assert.Same(request.Snapshot, outcome.Receipt.Request);
        Assert.Same(
            fixture.Workspace.Identity,
            outcome.Receipt.Request.Workspace);
        Assert.Same(
            fixture.Revision,
            outcome.Receipt.Request.RegistrationRevision);
        Assert.Same(
            fixture.Declaration,
            outcome.Receipt.Request.Registration);
        Assert.Same(binding.Id, outcome.Receipt.Request.Loader);
        Assert.Same(inputs.Snapshot, outcome.Receipt.Request.Inputs);
        Assert.Equal(
            EcosystemPopulationLoadSettlementKind.Completed,
            outcome.Receipt.SettlementKind);
        Assert.Equal(
            EcosystemPopulationCompletionKind.NoMembers,
            outcome.Receipt.Completion!.Kind);
        Assert.False(
            outcome.Receipt.Children
                is EcosystemPopulationChildSettlement[]);
        Assert.False(
            outcome.Receipt.Diagnostics
                is EcosystemPopulationLoadDiagnostic[]);
        Assert.Empty(outcome.Owners.Libraries);
        await outcome.Owners.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(request)
                .AsTask());
        Assert.Equal(1, inputs.InvocationCount);
    }

    [Fact]
    public async Task CancellationBeforeInvocationDoesNotCallLoader()
    {
        TestInputs inputs = new(LoadMode.CompletedNoMembers);
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture fixture =
            WorkspaceFixture.Create(binding);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        EcosystemPopulationLoadRequest<TestInputs> request =
            fixture.Request(inputs, cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(request)
                .AsTask());
        Assert.Equal(0, inputs.InvocationCount);
    }

    [Fact]
    public async Task CompletedOwnerTransfersExactlyOnce()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner = artifacts.CreateOwner("Contoso.Focus");
        TestInputs inputs = new(LoadMode.CompletedMembers)
        {
            Owner = owner,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));
        EcosystemPopulationLoadedLibraryReference reference =
            Assert.Single(outcome.Owners.Libraries);

        var transferred =
            Assert.IsType<EcosystemPopulationOwnerTakeOutcome.Transferred>(
                outcome.Owners.Take(reference.Reference));
        Assert.Same(owner, transferred.Owner);
        Assert.Equal(
            EcosystemPopulationLibraryRole.Focus,
            transferred.Library.Roles);
        Assert.IsType<
            EcosystemPopulationOwnerTakeOutcome.AlreadyTransferred>(
                outcome.Owners.Take(reference.Reference));

        await outcome.Owners.DisposeAsync();
        Assert.Equal(
            EcosystemPopulationOwnerBatchState.Retired,
            outcome.Owners.State);
        Assert.Equal(LibraryContentOwnerState.Active, owner.State);
        await owner.DisposeAsync();
    }

    [Fact]
    public async Task
        PlatformCompletedChildPreservesEvidenceAndTransfersAuthoritiesOnce()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        TestInputs inputs = new(LoadMode.PlatformCompleted)
        {
            Platform = platform,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));
        EcosystemPopulationChildSettlement child =
            Assert.Single(outcome.Receipt.Children);
        EcosystemPlatformPopulationChildEvidence evidence =
            Assert.IsType<EcosystemPlatformPopulationChildEvidence>(
                child.PlatformEvidence);
        Assert.Same(
            platform.Population.Receipt.HouseReceipt.Request,
            evidence.Request);
        Assert.Same(platform.Population.Receipt, evidence.Receipt);
        Assert.Same(
            child,
            Assert.Single(outcome.Owners.ArtifactSessionChildren));

        Assert.Collection(
            outcome.Owners.Libraries,
            library => Assert.Equal(
                EcosystemPopulationLibraryRole.Focus,
                library.Roles),
            library => Assert.Equal(
                EcosystemPopulationLibraryRole.BindingSupport,
                library.Roles));
        EcosystemPopulationLoadedLibraryReference library =
            outcome.Owners.Libraries[0];
        var transferredOwner =
            Assert.IsType<EcosystemPopulationOwnerTakeOutcome.Transferred>(
                outcome.Owners.Take(library.Reference));
        var transferredArtifacts =
            Assert.IsType<
                EcosystemPopulationArtifactSessionTakeOutcome.Transferred>(
                    outcome.Owners.TakeArtifactSession(child));
        Assert.Same(platform.Artifacts, transferredArtifacts.Session);
        Assert.IsType<
            EcosystemPopulationArtifactSessionTakeOutcome.AlreadyTransferred>(
                outcome.Owners.TakeArtifactSession(child));

        await outcome.Owners.DisposeAsync();
        Assert.Equal(
            EcosystemPopulationOwnerBatchState.Retired,
            outcome.Owners.State);
        await transferredOwner.Owner.DisposeAsync();
        await transferredArtifacts.Session.DisposeAsync();
        Assert.Empty(transferredArtifacts.Session.CleanupFailures);
    }

    [Fact]
    public async Task
        PlatformPopulationAdmissionPreservesCorrespondenceAndFocusRoles()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        TestInputs inputs = new(LoadMode.PlatformCompleted)
        {
            Platform = platform,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        EcosystemPopulationAdmissionResult result =
            await EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome);

        Assert.Same(outcome.Receipt, result.LoadReceipt);
        var childAdmission =
            Assert.IsType<EcosystemPopulationChildAdmission.Attempted>(
                Assert.Single(result.ChildAdmissions));
        Assert.Same(
            Assert.Single(outcome.Receipt.Children),
            childAdmission.Child);
        var accepted =
            Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
                childAdmission.Outcome);
        Assert.Same(
            workspace.Revision,
            accepted.Receipt.RegistrationRevision);
        Assert.Equal(2, result.Libraries.Count);
        Assert.All(
            result.Libraries,
            correspondence =>
            {
                Assert.Same(outcome.Receipt, correspondence.LoadReceipt);
                Assert.Same(
                    accepted.Receipt,
                    correspondence.Admission);
                Assert.Same(
                    correspondence.LoadedLibrary.Reference,
                    correspondence.Occurrence.Library);
            });
        EcosystemPopulationLibraryContributionWitness contribution =
            Assert.Single(result.Contributions);
        Assert.Equal(
            EcosystemPopulationLibraryRole.Focus,
            contribution.Correspondence.LoadedLibrary.Roles);
        Assert.DoesNotContain(
            result.Contributions,
            candidate =>
                candidate.Correspondence.LoadedLibrary.Roles
                == EcosystemPopulationLibraryRole.BindingSupport);
        Assert.Equal(
            EcosystemPopulationOwnerBatchState.Retired,
            outcome.Owners.State);
    }

    [Fact]
    public async Task
        CurrentNavigationContributionPreservesExactFocusAdmission()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationAdmissionResult admission =
            await AdmitPlatformAsync(workspace, platform);
        WorkspaceEcosystemRegistrationDeclaration neighboring =
            Declaration("ecosystem.neighboring");
        var changed =
            Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
                workspace.Workspace.ReplaceRegistrations(
                    workspace.Revision,
                    [
                        new WorkspaceRegistration.Ecosystem(
                            workspace.Declaration),
                        new WorkspaceRegistration.Ecosystem(neighboring),
                    ]));
        WorkspaceEcosystemContributionRelation currentRelation =
            Assert.Single(workspace.Revision.EcosystemContributions);

        EcosystemPopulationNavigationContribution projected =
            Assert.Single(
                EcosystemPopulationNavigationProjection.Project(
                    changed.Revision,
                    admission));

        Assert.Same(
            Assert.Single(admission.Contributions),
            projected.Source);
        Assert.Equal(
            EcosystemPopulationLibraryRole.Focus,
            projected.Source.Correspondence.LoadedLibrary.Roles);
        var available =
            Assert.IsType<
                NavigationEcosystemContributionOutcome.Available>(
                    projected.Outcome);
        NavigationEcosystemLibraryContribution contribution =
            available.Contribution;
        Assert.Same(
            workspace.Workspace.Identity,
            contribution.Workspace);
        Assert.Same(
            workspace.Revision,
            contribution.HistoricalRevision);
        Assert.Same(changed.Revision, contribution.CurrentRevision);
        Assert.Same(
            currentRelation,
            contribution.EcosystemRelation);
        Assert.Same(
            workspace.Declaration,
            contribution.Ecosystem.Declaration);
        Assert.Same(
            projected.Source.Correspondence.Admission,
            contribution.Admission);
        Assert.Same(
            projected.Source.Correspondence.Occurrence,
            contribution.Library);
        Assert.DoesNotContain(
            typeof(NavigationEcosystemLibraryContribution)
                .GetProperties(),
            property => property.Name.Contains(
                "Package",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task
        EqualTextRegistrationReplacementDoesNotReauthorizeContribution()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationAdmissionResult admission =
            await AdmitPlatformAsync(workspace, platform);
        WorkspaceEcosystemRegistrationDeclaration replacement =
            Declaration(workspace.Declaration.Id.Value);
        var changed =
            Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
                workspace.Workspace.ReplaceRegistrations(
                    workspace.Revision,
                    [
                        new WorkspaceRegistration.Ecosystem(
                            replacement),
                    ]));

        EcosystemPopulationNavigationContribution projected =
            Assert.Single(
                EcosystemPopulationNavigationProjection.Project(
                    changed.Revision,
                    admission));

        var unavailable =
            Assert.IsType<
                NavigationEcosystemContributionOutcome.Unavailable>(
                    projected.Outcome);
        Assert.Equal(
            NavigationEcosystemContributionUnavailableReason
                .RegistrationNotCurrent,
            unavailable.Reason);
        Assert.Same(changed.Revision, unavailable.CurrentRevision);
        Assert.Same(
            workspace.Declaration,
            projected.Source.Correspondence.LoadReceipt.Request
                .Registration);
    }

    [Fact]
    public async Task ForeignWorkspaceCannotConsumeContribution()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationAdmissionResult admission =
            await AdmitPlatformAsync(workspace, platform);
        await using var foreignWorkspace = new InspectionWorkspace(
            new WorkspacePlan(
                [
                    new WorkspaceRegistration.Ecosystem(
                        workspace.Declaration),
                ]));
        WorkspaceRegistrationRevision foreignRevision =
            Read(foreignWorkspace);

        EcosystemPopulationNavigationContribution projected =
            Assert.Single(
                EcosystemPopulationNavigationProjection.Project(
                    foreignRevision,
                    admission));

        var rejected =
            Assert.IsType<
                NavigationEcosystemContributionOutcome.Rejected>(
                    projected.Outcome);
        Assert.Equal(
            NavigationEcosystemContributionRejection.ForeignWorkspace,
            rejected.Reason);
    }

    [Fact]
    public async Task
        IncompleteLoadAdmitsOnlyItsIndependentlyCompletedPlatformChild()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        TestInputs inputs = new(LoadMode.PlatformIncomplete)
        {
            Platform = platform,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Incomplete>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        EcosystemPopulationAdmissionResult result =
            await EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome);

        Assert.Equal(2, outcome.Receipt.Children.Count);
        var childAdmission =
            Assert.IsType<EcosystemPopulationChildAdmission.Attempted>(
                Assert.Single(result.ChildAdmissions));
        Assert.Equal(
            EcosystemPopulationChildSettlementKind.Completed,
            childAdmission.Child.Kind);
        Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
            childAdmission.Outcome);
        Assert.Equal(2, result.Libraries.Count);
        Assert.Single(result.Contributions);
    }

    [Fact]
    public async Task MultipleSessionBackedChildrenAdmitInReceiptOrder()
    {
        CompletedPlatformPopulation first =
            await MaterializePlatformPopulationAsync();
        CompletedPlatformPopulation second =
            await MaterializePlatformPopulationAsync();
        TestInputs inputs = new(LoadMode.MultiplePlatformCompleted)
        {
            Platforms = [first, second],
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        EcosystemPopulationAdmissionResult result =
            await EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome);

        Assert.Equal(2, result.ChildAdmissions.Count);
        Assert.Same(
            outcome.Receipt.Children[0],
            result.ChildAdmissions[0].Child);
        Assert.Same(
            outcome.Receipt.Children[1],
            result.ChildAdmissions[1].Child);
        Assert.All(
            result.ChildAdmissions,
            child => Assert.IsType<
                EcosystemPopulationChildAdmission.Attempted>(child));
        Assert.Equal(4, result.Libraries.Count);
        Assert.Equal(2, result.Contributions.Count);
    }

    [Fact]
    public async Task AcceptedChildRemainsUsableWhenLaterChildIsUnsupported()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner unsupportedOwner =
            artifacts.CreateOwner("Contoso.UnsupportedAfterAccepted");
        TestInputs inputs = new(LoadMode.PlatformThenUnsupported)
        {
            Owner = unsupportedOwner,
            Platform = platform,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        EcosystemPopulationAdmissionResult result =
            await EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome);

        Assert.Collection(
            result.ChildAdmissions,
            first => Assert.IsType<
                EcosystemPopulationChildAdmission.Attempted>(first),
            second => Assert.IsType<
                EcosystemPopulationChildAdmission.Unsupported>(second));
        Assert.Equal(2, result.Libraries.Count);
        Assert.Single(result.Contributions);
        using LibraryOperationLease operation =
            Assert.IsType<WorkspaceLibraryOperationIssueOutcome.Issued>(
                workspace.Workspace.IssueLibraryOperation(
                    result.Libraries[0].Occurrence))
                .Lease;
        Assert.Same(
            result.Libraries[0].Occurrence.Library,
            operation.Reference);
        Assert.Equal(
            LibraryContentOwnerState.Released,
            unsupportedOwner.State);
    }

    [Fact]
    public async Task
        PartialAdmissionExceptionRetainsAcceptedCorrespondenceAndCleansRemainder()
    {
        CompletedPlatformPopulation first =
            await MaterializePlatformPopulationAsync();
        CompletedPlatformPopulation second =
            await MaterializePlatformPopulationAsync();
        TestInputs inputs = new(LoadMode.MultiplePlatformCompleted)
        {
            Platforms = [first, second],
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));
        EcosystemPopulationChildSettlement secondChild =
            outcome.Receipt.Children[1];
        EcosystemPopulationLoadedLibraryReference heldReference =
            outcome.Owners.Libraries.First(
                library => ReferenceEquals(
                    library.ChildSettlement,
                    secondChild));
        LibraryContentOwner heldOwner =
            Assert.IsType<EcosystemPopulationOwnerTakeOutcome.Transferred>(
                outcome.Owners.Take(heldReference.Reference))
                .Owner;

        Task<EcosystemPopulationAdmissionResult> admission =
            EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome).AsTask();
        Assert.False(admission.IsCompleted);
        await heldOwner.DisposeAsync();
        EcosystemPopulationAdmissionException failure =
            await Assert.ThrowsAsync<
                EcosystemPopulationAdmissionException>(
                    () => admission);

        Assert.Same(outcome.Receipt, failure.LoadReceipt);
        var acceptedChild =
            Assert.IsType<EcosystemPopulationChildAdmission.Attempted>(
                Assert.Single(failure.ChildAdmissions));
        Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
            acceptedChild.Outcome);
        Assert.Equal(2, failure.Libraries.Count);
        Assert.Single(failure.Contributions);
        Assert.Null(failure.RetirementFailure);
        using LibraryOperationLease operation =
            Assert.IsType<WorkspaceLibraryOperationIssueOutcome.Issued>(
                workspace.Workspace.IssueLibraryOperation(
                    failure.Libraries[0].Occurrence))
                .Lease;
        Assert.Same(
            failure.Libraries[0].Occurrence.Library,
            operation.Reference);
        Assert.Equal(
            EcosystemPopulationOwnerBatchState.Retired,
            outcome.Owners.State);
        Assert.Empty(second.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        WorkspaceRejectionRetainsLoaderReceiptAndIssuesNoContribution()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        TestInputs inputs = new(LoadMode.PlatformCompleted)
        {
            Platform = platform,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));
        var replacement =
            Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
                workspace.Workspace.ReplaceRegistrations(
                    workspace.Revision,
                    ImmutableArray<WorkspaceRegistration>.Empty));

        EcosystemPopulationAdmissionResult result =
            await EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome);

        Assert.Same(outcome.Receipt, result.LoadReceipt);
        var childAdmission =
            Assert.IsType<EcosystemPopulationChildAdmission.Attempted>(
                Assert.Single(result.ChildAdmissions));
        var rejected =
            Assert.IsType<WorkspaceLibraryAdmissionOutcome.Rejected>(
                childAdmission.Outcome);
        Assert.Same(replacement.Revision, rejected.CurrentRevision);
        Assert.Equal(
            WorkspaceLibraryAdmissionRejection.RevisionMismatch,
            rejected.Reason);
        Assert.Empty(result.Libraries);
        Assert.Empty(result.Contributions);
        Assert.All(
            platform.Population.Owners,
            owner => Assert.Equal(
                LibraryContentOwnerState.Released,
                owner.State));
        Assert.Empty(platform.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task
        WorkspaceFailureRetainsCleanupEvidenceWithoutSecondRetirement()
    {
        var cleanupFailure =
            new IOException("synthetic admission cleanup failure");
        var cleanupLease = new FailingArtifactLease(cleanupFailure);
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationWithCleanupFailureAsync(
                cleanupLease);
        TestInputs inputs = new(LoadMode.PlatformCompleted)
        {
            Platform = platform,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));
        _ = Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            workspace.Workspace.ReplaceRegistrations(
                workspace.Revision,
                ImmutableArray<WorkspaceRegistration>.Empty));

        EcosystemPopulationAdmissionResult result =
            await EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome);

        var attempted =
            Assert.IsType<EcosystemPopulationChildAdmission.Attempted>(
                Assert.Single(result.ChildAdmissions));
        var failed =
            Assert.IsType<WorkspaceLibraryAdmissionOutcome.Failed>(
                attempted.Outcome);
        Assert.Same(
            cleanupFailure,
            Assert.Single(failed.CleanupFailures).Failure);
        Assert.Empty(result.Libraries);
        Assert.Empty(result.Contributions);
        Assert.Equal(1, cleanupLease.Disposals);
        await workspace.Workspace.CloseAsync();
        Assert.Equal(1, cleanupLease.Disposals);
    }

    [Fact]
    public async Task NonOwningOutcomeProducesNoAdmissionAttempt()
    {
        TestInputs inputs = new(LoadMode.Ambiguous);
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadOutcome outcome =
            await EcosystemPopulationLoadOperation.InvokeAsync(
                workspace.Request(
                    inputs,
                    TestContext.Current.CancellationToken));

        EcosystemPopulationAdmissionResult result =
            await EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome);

        Assert.Same(outcome.Receipt, result.LoadReceipt);
        Assert.Empty(result.ChildAdmissions);
        Assert.Empty(result.Libraries);
        Assert.Empty(result.Contributions);
    }

    [Fact]
    public async Task EmptyOwningOutcomeProducesNoAdmissionAttempt()
    {
        TestInputs inputs = new(LoadMode.CompletedNoMembers);
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        EcosystemPopulationAdmissionResult result =
            await EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome);

        Assert.Same(outcome.Receipt, result.LoadReceipt);
        Assert.Empty(result.ChildAdmissions);
        Assert.Empty(result.Libraries);
        Assert.Empty(result.Contributions);
        Assert.Equal(
            EcosystemPopulationOwnerBatchState.Retired,
            outcome.Owners.State);
    }

    [Fact]
    public async Task
        LibraryChildWithoutArtifactSessionIsRetiredAsUnsupported()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.UnsupportedAdmission");
        TestInputs inputs = new(LoadMode.CompletedMembers)
        {
            Owner = owner,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        EcosystemPopulationAdmissionResult result =
            await EcosystemPopulationAdmissionOperation.AdmitAsync(
                workspace.Workspace,
                outcome);

        var unsupported =
            Assert.IsType<EcosystemPopulationChildAdmission.Unsupported>(
                Assert.Single(result.ChildAdmissions));
        Assert.Equal(
            EcosystemPopulationChildAdmissionUnsupportedReason
                .LibrariesWithoutArtifactSession,
            unsupported.Reason);
        Assert.Empty(unsupported.RetirementFailures);
        Assert.Same(
            owner.Reference,
            Assert.Single(unsupported.Libraries).Reference);
        Assert.Empty(result.Libraries);
        Assert.Empty(result.Contributions);
        Assert.Equal(LibraryContentOwnerState.Released, owner.State);
        Assert.Equal(
            EcosystemPopulationOwnerBatchState.Retired,
            outcome.Owners.State);
    }

    [Fact]
    public async Task
        PlatformArtifactRetirementWaitsForTransferredLibraryOwner()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        TestInputs inputs = new(LoadMode.PlatformCompleted)
        {
            Platform = platform,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));
        EcosystemPopulationLoadedLibraryReference library =
            outcome.Owners.Libraries[0];
        LibraryContentOwner owner =
            Assert.IsType<EcosystemPopulationOwnerTakeOutcome.Transferred>(
                outcome.Owners.Take(library.Reference)).Owner;

        Task retirement = outcome.Owners.DisposeAsync().AsTask();
        Assert.False(retirement.IsCompleted);
        await owner.DisposeAsync();
        await retirement.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            EcosystemPopulationOwnerBatchState.Retired,
            outcome.Owners.State);
        Assert.Empty(platform.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task AmbiguityRemainsDistinctFromRejection()
    {
        TestInputs inputs = new(LoadMode.Ambiguous);
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Ambiguous>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Assert.Equal(
            EcosystemPopulationLoadSettlementKind.Ambiguous,
            outcome.Receipt.SettlementKind);
        Assert.Equal(
            EcosystemPopulationChildSettlementKind.Ambiguous,
            Assert.Single(outcome.Receipt.Children).Kind);
    }

    [Fact]
    public async Task PlatformTerminalChildPreservesExactEvidence()
    {
        PlatformPopulationArtifactMaterializationOutcome.Terminal platform =
            await MaterializeIncompletePlatformPopulationAsync();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadRequest<TestInputs> request =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        ChildEvidence child = Child(request, "platform-terminal");

        EcosystemPopulationChildSettlement settlement =
            request.PlatformTerminalChild(
                child.Request,
                child.Receipt,
                platform.TerminalRealization);

        Assert.Equal(
            EcosystemPopulationChildSettlementKind.Incomplete,
            settlement.Kind);
        EcosystemPlatformPopulationChildEvidence evidence =
            Assert.IsType<EcosystemPlatformPopulationChildEvidence>(
                settlement.PlatformEvidence);
        Assert.Same(
            platform.TerminalRealization.Receipt.HouseReceipt.Request,
            evidence.Request);
        Assert.Same(
            platform.TerminalRealization.Receipt,
            evidence.Receipt);
        Assert.IsType<EcosystemPopulationLoaderReply.Incomplete>(
            request.Incomplete(
                [settlement],
                [],
                [Diagnostic("ecosystem-loader.platform-incomplete")]));
    }

    [Fact]
    public async Task OwnerBatchRetirementFailureRemainsVisible()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        OwnerWithChild owned =
            artifacts.CreateOwnerWithChild("Contoso.BatchFailure");
        TestInputs inputs = new(LoadMode.CompletedMembers)
        {
            Owner = owned.Owner,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Task retirement = WhileBorrowed(
            owned.Child,
            () => outcome.Owners.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<AggregateException>(() => retirement);

        Assert.Equal(
            EcosystemPopulationOwnerBatchState.RetirementFailed,
            outcome.Owners.State);
        Assert.Equal(
            LibraryContentOwnerState.ReleaseFailed,
            owned.Owner.State);
        owned.Child.Dispose();
    }

    [Fact]
    public async Task PlatformArtifactCleanupFailureRemainsVisible()
    {
        var cleanupFailure =
            new IOException("synthetic Platform artifact cleanup failure");
        var cleanupLease = new FailingArtifactLease(cleanupFailure);
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationWithCleanupFailureAsync(
                cleanupLease);
        TestInputs inputs = new(LoadMode.PlatformCompleted)
        {
            Platform = platform,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));
        EcosystemPopulationLoadedLibraryReference library =
            Assert.Single(outcome.Owners.Libraries);
        var transferred =
            Assert.IsType<EcosystemPopulationOwnerTakeOutcome.Transferred>(
                outcome.Owners.Take(library.Reference));

        Task retirement = outcome.Owners.DisposeAsync().AsTask();
        Assert.False(retirement.IsCompleted);
        Assert.Equal(0, cleanupLease.Disposals);

        await transferred.Owner.DisposeAsync();
        AggregateException failure =
            await Assert.ThrowsAsync<AggregateException>(() => retirement);

        Assert.Same(
            cleanupFailure,
            Assert.Single(failure.Flatten().InnerExceptions));
        Assert.Same(
            cleanupFailure,
            Assert.Single(platform.Artifacts.CleanupFailures));
        Assert.Equal(1, cleanupLease.Disposals);
        Assert.Equal(
            EcosystemPopulationOwnerBatchState.RetirementFailed,
            outcome.Owners.State);
    }

    [Fact]
    public async Task CancellationAfterReplyRetiresUntransferredOwners()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Cancelled");
        using var cancellation = new CancellationTokenSource();
        TestInputs inputs = new(LoadMode.CancelAfterCompletedReply)
        {
            Owner = owner,
            Cancellation = cancellation,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(inputs, cancellation.Token))
                .AsTask());

        Assert.Equal(1, inputs.InvocationCount);
        Assert.Equal(LibraryContentOwnerState.Released, owner.State);
    }

    [Fact]
    public async Task
        CancellationAfterPlatformReplyRetiresLibraryAndArtifactAuthorities()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        using var cancellation = new CancellationTokenSource();
        TestInputs inputs = new(LoadMode.PlatformCompleted)
        {
            Platform = platform,
            Cancellation = cancellation,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(inputs, cancellation.Token))
                .AsTask());

        Assert.All(
            platform.Population.Owners,
            owner => Assert.Equal(
                LibraryContentOwnerState.Released,
                owner.State));
        Assert.Empty(platform.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task CancellationCleanupFailureRemainsVisible()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        OwnerWithChild owned =
            artifacts.CreateOwnerWithChild("Contoso.CancelledFailure");
        using var cancellation = new CancellationTokenSource();
        TestInputs inputs = new(LoadMode.CancelAfterCompletedReply)
        {
            Owner = owned.Owner,
            Cancellation = cancellation,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        Task operation = WhileBorrowed(
            owned.Child,
            () => EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(inputs, cancellation.Token))
                .AsTask());
        OperationCanceledException failure =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => operation);

        Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Equal(
            LibraryContentOwnerState.ReleaseFailed,
            owned.Owner.State);
        owned.Child.Dispose();
    }

    [Fact]
    public async Task IncompleteMayTransferOnlyCompletedChildOwners()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Partial");
        TestInputs inputs = new(LoadMode.Incomplete)
        {
            Owner = owner,
        };
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());

        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Incomplete>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        inputs,
                        TestContext.Current.CancellationToken)));

        Assert.Equal(2, outcome.Receipt.Children.Count);
        Assert.Single(outcome.Owners.Libraries);
        await outcome.Owners.DisposeAsync();
        Assert.Equal(LibraryContentOwnerState.Released, owner.State);
    }

    [Fact]
    public async Task NonCompletedChildCannotCarryLibraryOwnership()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Invalid");
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadRequest<TestInputs> request =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        ChildEvidence child = Child(request, "child");
        EcosystemPopulationChildSettlement incomplete =
            request.ChildIncomplete(
                child.Request,
                child.Receipt);

        Assert.Throws<ArgumentException>(
            () => new EcosystemPopulationCompletedChild(
                incomplete,
                [
                    new(
                        owner,
                        EcosystemPopulationLibraryRole.Focus),
                ]));

        await owner.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateOwnerCannotEnterOneLoadReply()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Duplicate");
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadRequest<TestInputs> request =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        var ownership = new EcosystemPopulationLibraryOwnership(
            owner,
            EcosystemPopulationLibraryRole.Focus);
        ChildEvidence evidence = Child(request, "child");
        EcosystemPopulationChildSettlement child =
            request.ChildCompleted(
                evidence.Request,
                evidence.Receipt,
                EcosystemPopulationChildCompletionKind.Members);

        Assert.Throws<ArgumentException>(
            () => new EcosystemPopulationCompletedChild(
                child,
                [ownership, ownership]));

        await owner.DisposeAsync();
    }

    [Fact]
    public async Task DuplicateChildIdentityCannotEnterOneLoadReply()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.DuplicateChild");
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadRequest<TestInputs> request =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        ChildEvidence evidence = Child(request, "child");
        EcosystemPopulationChildSettlement completed =
            request.ChildCompleted(
                evidence.Request,
                evidence.Receipt,
                EcosystemPopulationChildCompletionKind.Members);
        EcosystemPopulationChildSettlement incomplete =
            request.ChildIncomplete(
                evidence.Request,
                evidence.Receipt);
        var completedChild = new EcosystemPopulationCompletedChild(
            completed,
            [
                new(
                    owner,
                    EcosystemPopulationLibraryRole.Focus),
            ]);

        Assert.Throws<ArgumentException>(
            () => request.Incomplete(
                [completed, incomplete],
                [completedChild],
                [Diagnostic("duplicate-child")]));

        await owner.DisposeAsync();
    }

    [Fact]
    public async Task ReplyFromAnotherRequestIsRejected()
    {
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(binding);
        TestInputs firstInputs = new(LoadMode.ForeignReply);
        TestInputs secondInputs = new(LoadMode.CompletedNoMembers);
        EcosystemPopulationLoadRequest<TestInputs> first =
            workspace.Request(
                firstInputs,
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> second =
            workspace.Request(
                secondInputs,
                TestContext.Current.CancellationToken);
        firstInputs.ForeignReply = second.Completed(
            second.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    "test.foreign"),
                EcosystemPopulationCompletionKind.NoMembers),
            []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(first)
                .AsTask());
    }

    [Fact]
    public async Task ForeignPopulatedReplyRetiresItsLibraryOwner()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        LibraryContentOwner owner =
            artifacts.CreateOwner("Contoso.Foreign");
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(binding);
        TestInputs firstInputs = new(LoadMode.ForeignReply);
        TestInputs secondInputs = new(LoadMode.CompletedMembers);
        EcosystemPopulationLoadRequest<TestInputs> first =
            workspace.Request(
                firstInputs,
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> second =
            workspace.Request(
                secondInputs,
                TestContext.Current.CancellationToken);
        firstInputs.ForeignReply = second.Completed(
            second.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    "test.foreign-members"),
                EcosystemPopulationCompletionKind.Satisfied),
            [CompletedChild(second, owner)]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(first)
                .AsTask());

        Assert.Equal(LibraryContentOwnerState.Released, owner.State);
    }

    [Fact]
    public async Task
        ForeignPlatformReplyRetiresLibraryAndArtifactAuthorities()
    {
        CompletedPlatformPopulation platform =
            await MaterializePlatformPopulationAsync();
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(binding);
        TestInputs firstInputs = new(LoadMode.ForeignReply);
        EcosystemPopulationLoadRequest<TestInputs> first =
            workspace.Request(
                firstInputs,
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> second =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        firstInputs.ForeignReply = second.Completed(
            second.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    "test.foreign-platform"),
                EcosystemPopulationCompletionKind.Satisfied),
            [PlatformCompletedChild(second, platform)]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(first)
                .AsTask());

        Assert.All(
            platform.Population.Owners,
            owner => Assert.Equal(
                LibraryContentOwnerState.Released,
                owner.State));
        Assert.Empty(platform.Artifacts.CleanupFailures);
    }

    [Fact]
    public async Task ForeignReplyCleanupFailureRemainsVisible()
    {
        await using ArtifactFixture artifacts =
            await ArtifactFixture.CreateAsync();
        OwnerWithChild owned =
            artifacts.CreateOwnerWithChild("Contoso.ForeignFailure");
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(binding);
        TestInputs firstInputs = new(LoadMode.ForeignReply);
        TestInputs secondInputs = new(LoadMode.CompletedMembers);
        EcosystemPopulationLoadRequest<TestInputs> first =
            workspace.Request(
                firstInputs,
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> second =
            workspace.Request(
                secondInputs,
                TestContext.Current.CancellationToken);
        firstInputs.ForeignReply = second.Completed(
            second.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    "test.foreign-failure"),
                EcosystemPopulationCompletionKind.Satisfied),
            [CompletedChild(second, owned.Owner)]);

        Task operation = WhileBorrowed(
            owned.Child,
            () => EcosystemPopulationLoadOperation
                .InvokeAsync(first)
                .AsTask());
        InvalidOperationException failure =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => operation);

        Assert.IsType<AggregateException>(failure.InnerException);
        Assert.Equal(
            LibraryContentOwnerState.ReleaseFailed,
            owned.Owner.State);
        owned.Child.Dispose();
    }

    [Fact]
    public async Task CompletionEvidenceIsBoundToExactRequest()
    {
        EcosystemPopulationLoaderBinding<TestInputs> binding = Binding();
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(binding);
        EcosystemPopulationLoadRequest<TestInputs> first =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> second =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        EcosystemPopulationCompletionWitness foreign =
            second.Completion(
                EcosystemPopulationCompletionIdentity.Create(
                    "test.foreign-completion"),
                EcosystemPopulationCompletionKind.NoMembers);

        Assert.Throws<ArgumentException>(
            () => first.Completed(foreign, []));

        ChildEvidence evidence = Child(second, "foreign");
        EcosystemPopulationChildSettlement foreignChild =
            second.ChildCompleted(
                evidence.Request,
                evidence.Receipt,
                EcosystemPopulationChildCompletionKind.NoMembers);
        var completedForeignChild =
            new EcosystemPopulationCompletedChild(foreignChild, []);
        Assert.Throws<ArgumentException>(
            () => first.Completed(
                first.Completion(
                    EcosystemPopulationCompletionIdentity.Create(
                        "test.local-completion"),
                    EcosystemPopulationCompletionKind.NoMembers),
                [completedForeignChild]));
    }

    [Fact]
    public void OwningResultsDeclareResourceOwnership()
    {
        Assert.NotNull(
            typeof(EcosystemPopulationOwnerBatch)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLoadOutcome.Completed)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLoadOutcome.Incomplete)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLibraryOwnership)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationCompletedChild)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationOwnerTakeOutcome.Transferred)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(
                EcosystemPopulationArtifactSessionTakeOutcome.Transferred)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLoaderReply.Completed)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
        Assert.NotNull(
            typeof(EcosystemPopulationLoaderReply.Incomplete)
                .GetCustomAttribute<ResourceOwnershipAttribute>());
    }

    [Fact]
    public async Task RetainedCollectionsAreNotMutableArrays()
    {
        EcosystemPopulationLoadDiagnostic diagnostic =
            Diagnostic("ecosystem-loader.test");
        WorkspaceEcosystemRegistrationDeclaration declaration =
            Declaration("ecosystem.test");
        await using var workspace = new InspectionWorkspace(
            new WorkspacePlan(
                ImmutableArray.Create<WorkspaceRegistration>(
                    new WorkspaceRegistration.Ecosystem(declaration))));
        WorkspaceRegistrationRevision revision = Read(workspace);

        var unavailable =
            EcosystemPopulationLoaderSelection.UnavailableFor(
                revision,
                declaration,
                EcosystemPopulationDemand.WholePopulation.Instance,
                [diagnostic]);

        Assert.False(
            unavailable.Diagnostics
                is EcosystemPopulationLoadDiagnostic[]);
    }

    [Fact]
    public void PublicRequestSurfaceHasNoWorkspaceMutationAuthority()
    {
        Type request = typeof(EcosystemPopulationLoadRequest<TestInputs>);

        Assert.DoesNotContain(
            request.GetProperties(),
            property =>
                property.PropertyType == typeof(InspectionWorkspace));
        Assert.DoesNotContain(
            request.GetMethods(),
            method => method.GetParameters().Any(
                parameter =>
                    parameter.ParameterType == typeof(InspectionWorkspace)));
        Assert.DoesNotContain(
            typeof(EcosystemPopulationLoaderBinding<TestInputs>)
                .GetProperties(),
            property => typeof(Delegate).IsAssignableFrom(
                property.PropertyType));
    }

    [Fact]
    public async Task ChildEvidenceSurfaceIsResourceFreeAndExactlyBound()
    {
        await using WorkspaceFixture workspace =
            WorkspaceFixture.Create(Binding());
        EcosystemPopulationLoadRequest<TestInputs> request =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        EcosystemPopulationLoadRequest<TestInputs> foreign =
            workspace.Request(
                new(LoadMode.CompletedNoMembers),
                TestContext.Current.CancellationToken);
        EcosystemPopulationChildRequestIdentity childRequest =
            request.ChildRequest("child.request");
        EcosystemPopulationChildRequestIdentity foreignRequest =
            foreign.ChildRequest("foreign.request");
        EcosystemPopulationChildReceiptIdentity foreignReceipt =
            foreign.ChildReceipt(
                foreignRequest,
                "foreign.receipt");

        Assert.Throws<ArgumentException>(
            () => request.ChildIncomplete(
                childRequest,
                foreignReceipt));
        Assert.Throws<ArgumentException>(
            () => request.ChildReceipt(
                foreignRequest,
                "cross-request.receipt"));
        Assert.Throws<ArgumentException>(
            () => request.ChildIncomplete(
                foreignRequest,
                foreignReceipt));

        Type[] evidenceTypes =
        [
            typeof(EcosystemPopulationChildRequestIdentity),
            typeof(EcosystemPopulationChildReceiptIdentity),
            typeof(EcosystemPopulationChildSettlement),
            typeof(EcosystemPlatformPopulationChildEvidence),
        ];
        Assert.All(
            evidenceTypes,
            type =>
            {
                Assert.False(typeof(IDisposable).IsAssignableFrom(type));
                Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(type));
                Assert.Null(
                    type.GetCustomAttribute<ResourceOwnershipAttribute>());
                Assert.DoesNotContain(
                    type.GetFields(
                        BindingFlags.Instance
                        | BindingFlags.Public
                        | BindingFlags.NonPublic),
                    field =>
                        typeof(Delegate).IsAssignableFrom(field.FieldType)
                        || typeof(InspectionWorkspace).IsAssignableFrom(
                            field.FieldType)
                        || typeof(LibraryContentOwner).IsAssignableFrom(
                            field.FieldType));
            });
    }

    static EcosystemPopulationLoaderBinding<TestInputs> Binding() =>
        EcosystemPopulationLoaderBinding.Create<TestInputs>(
            EcosystemPopulationLoaderId.Create(
                "ecosystem-loader.test"),
            LoadAsync);

    static ValueTask<EcosystemPopulationLoaderReply> LoadAsync(
        EcosystemPopulationLoadRequest<TestInputs> request)
    {
        TestInputs inputs = request.Inputs;
        inputs.InvocationCount++;
        EcosystemPopulationLoaderReply reply = inputs.Mode switch
        {
            LoadMode.CompletedNoMembers =>
                request.Completed(
                    Completion(
                        request,
                        EcosystemPopulationCompletionKind.NoMembers),
                    []),
            LoadMode.CompletedMembers =>
                request.Completed(
                    Completion(
                        request,
                        EcosystemPopulationCompletionKind.Satisfied),
                    [CompletedChild(request, inputs.Owner!)]),
            LoadMode.PlatformCompleted =>
                request.Completed(
                    Completion(
                        request,
                        EcosystemPopulationCompletionKind.Satisfied),
                    [PlatformCompletedChild(request, inputs.Platform!)]),
            LoadMode.MultiplePlatformCompleted =>
                request.Completed(
                    Completion(
                        request,
                        EcosystemPopulationCompletionKind.Satisfied),
                    [
                        .. inputs.Platforms!.Select(
                            (platform, index) =>
                                PlatformCompletedChild(
                                    request,
                                    platform,
                                    $"platform-{index}")),
                    ]),
            LoadMode.PlatformIncomplete =>
                PlatformIncomplete(request),
            LoadMode.PlatformThenUnsupported =>
                request.Completed(
                    Completion(
                        request,
                        EcosystemPopulationCompletionKind.Satisfied),
                    [
                        PlatformCompletedChild(
                            request,
                            inputs.Platform!,
                            "platform"),
                        CompletedChild(
                            request,
                            inputs.Owner!,
                            "unsupported"),
                    ]),
            LoadMode.Ambiguous =>
                request.Ambiguous(
                    [AmbiguousChild(request)],
                    [Diagnostic("ecosystem-loader.ambiguous")]),
            LoadMode.CancelAfterCompletedReply =>
                request.Completed(
                    Completion(
                        request,
                        EcosystemPopulationCompletionKind.Satisfied),
                    [CompletedChild(request, inputs.Owner!)]),
            LoadMode.Incomplete => Incomplete(request),
            LoadMode.ForeignReply =>
                inputs.ForeignReply
                ?? throw new InvalidOperationException(
                    "The foreign reply was not configured."),
            _ => throw new InvalidOperationException(
                "Unknown test load mode."),
        };

        inputs.Cancellation?.Cancel();
        return ValueTask.FromResult(reply);
    }

    static EcosystemPopulationLoaderReply Incomplete(
        EcosystemPopulationLoadRequest<TestInputs> request)
    {
        EcosystemPopulationCompletedChild completed =
            CompletedChild(request, request.Inputs.Owner!);
        ChildEvidence evidence = Child(request, "second");
        EcosystemPopulationChildSettlement incomplete =
            request.ChildIncomplete(
                evidence.Request,
                evidence.Receipt);
        return request.Incomplete(
            [completed.Settlement, incomplete],
            [completed],
            [Diagnostic("ecosystem-loader.incomplete")]);
    }

    static EcosystemPopulationLoaderReply PlatformIncomplete(
        EcosystemPopulationLoadRequest<TestInputs> request)
    {
        EcosystemPopulationCompletedChild completed =
            PlatformCompletedChild(request, request.Inputs.Platform!);
        ChildEvidence evidence = Child(request, "incomplete");
        EcosystemPopulationChildSettlement incomplete =
            request.ChildIncomplete(
                evidence.Request,
                evidence.Receipt);
        return request.Incomplete(
            [completed.Settlement, incomplete],
            [completed],
            [Diagnostic("ecosystem-loader.platform-incomplete")]);
    }

    static EcosystemPopulationCompletedChild CompletedChild(
        EcosystemPopulationLoadRequest<TestInputs> request,
        LibraryContentOwner owner,
        string childName = "child")
    {
        ChildEvidence evidence = Child(request, childName);
        EcosystemPopulationChildSettlement settlement =
            request.ChildCompleted(
                evidence.Request,
                evidence.Receipt,
                EcosystemPopulationChildCompletionKind.Members);
        return new EcosystemPopulationCompletedChild(
            settlement,
            [
                new(
                    owner,
                    EcosystemPopulationLibraryRole.Focus),
            ]);
    }

    static EcosystemPopulationCompletedChild PlatformCompletedChild(
        EcosystemPopulationLoadRequest<TestInputs> request,
        CompletedPlatformPopulation platform,
        string childName = "platform")
    {
        ChildEvidence evidence = Child(request, childName);
        return request.PlatformCompletedChild(
            evidence.Request,
            evidence.Receipt,
            platform.Population,
            platform.Artifacts);
    }

    static EcosystemPopulationChildSettlement AmbiguousChild(
        EcosystemPopulationLoadRequest<TestInputs> request)
    {
        ChildEvidence evidence = Child(request, "ambiguous");
        return request.ChildAmbiguous(
            evidence.Request,
            evidence.Receipt);
    }

    static EcosystemPopulationCompletionWitness Completion(
        EcosystemPopulationLoadRequest<TestInputs> request,
        EcosystemPopulationCompletionKind kind) =>
        request.Completion(
            EcosystemPopulationCompletionIdentity.Create(
                kind == EcosystemPopulationCompletionKind.NoMembers
                    ? "test.no-members"
                    : "test.completed"),
            kind);

    static EcosystemPopulationLoadDiagnostic Diagnostic(string code) =>
        new(code, "Test diagnostic.");

    static ChildEvidence Child(
        EcosystemPopulationLoadRequest<TestInputs> parent,
        string name)
    {
        EcosystemPopulationChildRequestIdentity request =
            parent.ChildRequest($"{name}.request");
        return new(
            request,
            parent.ChildReceipt(
                request,
                $"{name}.receipt"));
    }

    static Task WhileBorrowed(
        ArtifactContentLease child,
        Func<Task> operation) =>
        Assert.IsType<ArtifactContentAccessOutcome<Task>.Accessed>(
            child.WithContent(
                (_, _) => operation(),
                TestContext.Current.CancellationToken))
            .Value;

    static async Task<EcosystemPopulationAdmissionResult>
        AdmitPlatformAsync(
            WorkspaceFixture workspace,
            CompletedPlatformPopulation platform)
    {
        var outcome =
            Assert.IsType<EcosystemPopulationLoadOutcome.Completed>(
                await EcosystemPopulationLoadOperation.InvokeAsync(
                    workspace.Request(
                        new(LoadMode.PlatformCompleted)
                        {
                            Platform = platform,
                        },
                        TestContext.Current.CancellationToken)));
        return await EcosystemPopulationAdmissionOperation.AdmitAsync(
            workspace.Workspace,
            outcome);
    }

    static WorkspaceEcosystemRegistrationDeclaration Declaration(
        string id) =>
        new(
            WorkspaceEcosystemRegistrationId.Create(id),
            ["Contoso"],
            [],
            []);

    static WorkspaceRegistrationRevision Read(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;

    static async Task<CompletedPlatformPopulation>
        MaterializePlatformPopulationAsync() =>
        Completed(
            await MaterializePlatformPopulationOutcomeAsync(
                consumedAssemblies: 2));

    static async Task<
        PlatformPopulationArtifactMaterializationOutcome.Terminal>
        MaterializeIncompletePlatformPopulationAsync() =>
        Assert.IsType<
            PlatformPopulationArtifactMaterializationOutcome.Terminal>(
                await MaterializePlatformPopulationOutcomeAsync(
                    consumedAssemblies: 5));

    static async Task<PlatformPopulationArtifactMaterializationOutcome>
        MaterializePlatformPopulationOutcomeAsync(int consumedAssemblies)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformFamilyTarget target = new(
            PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create("runtime-pack");
        var operation = new PlatformHouseOperation.Realize(
            new PlatformPopulationDemand.CompletePopulation(),
            PlatformViewDemand.Implementation);
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create("ecosystem-platform"),
            new PlatformTargetDemand.Exact(target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "ecosystem-platform")),
            operation,
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "ecosystem-platform-sources"),
                PlatformSourcePolicyGeneration.Create(
                    "ecosystem-platform-generation"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [capability]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 4,
                maxTargetCandidates: 4,
                maxAssemblies: 4,
                maxXmlDocuments: 4,
                maxPortablePdbs: 4,
                maxSourceDocuments: 4,
                maxBytes: 16 * 1024 * 1024,
                maxForwardingHops: 4,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);
        var contribution = new PlatformSourceContribution.Realization(
            PlatformSourceFacet.Implementation,
            capability,
            request.Snapshot,
            PlatformSourceGeneration.Create(
                "ecosystem-platform-source-generation"),
            target,
            PlatformSourceCoordinateIdentity.Create(
                "ecosystem-platform-coordinate"),
            operation.Population,
            PlatformSourceContributionCompleteness.Authoritative);
        byte[] focusContent = await File.ReadAllBytesAsync(
            typeof(EcosystemPopulationLoadingTests).Assembly.Location,
            cancellationToken);
        byte[] supportContent = await File.ReadAllBytesAsync(
            typeof(JsonSerializer).Assembly.Location,
            cancellationToken);
        AssemblyReferenceIdentity focusIdentity =
            ReadAssemblyIdentity(focusContent);
        AssemblyReferenceIdentity supportIdentity =
            ReadAssemblyIdentity(supportContent);
        PlatformPopulationLibraryArtifactMaterializationItem Item(
            byte[] content,
            AssemblyReferenceIdentity identity,
            string provenance,
            PlatformPopulationMemberAttribution attribution) =>
            new(
                new PlatformLibraryArtifactMaterializationItem(
                    contribution,
                    new Provenance(provenance),
                    identity,
                    content.LongLength,
                    _ => new MemoryStream(content, writable: false)),
                attribution);
        PlatformPopulationLibraryArtifactMaterializationItem focus = Item(
            focusContent,
            focusIdentity,
            "platform-focus",
            new PlatformPopulationMemberAttribution(
                target,
                PlatformPopulationMemberRole.Focus));
        PlatformPopulationLibraryArtifactMaterializationItem support = Item(
            supportContent,
            supportIdentity,
            "platform-support",
            new PlatformPopulationMemberAttribution(
                new PlatformFamilyTarget(
                    PlatformFamily.DotNetRuntime,
                    target.TargetFramework,
                    PlatformVersion.Parse("11.0.0")),
                PlatformPopulationMemberRole.BindingSupport));
        var consumed = new PlatformHouseConsumedWork(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: consumedAssemblies,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: checked(
                focusContent.LongLength + supportContent.LongLength),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);

        return await PlatformHousePopulationArtifactMaterializer
            .MaterializeImplementationsAsync(
                request,
                [focus, support],
                consumed,
                "ecosystem-platform");
    }

    static CompletedPlatformPopulation Completed(
        PlatformPopulationArtifactMaterializationOutcome outcome)
    {
        var completed =
            Assert.IsType<
                PlatformPopulationArtifactMaterializationOutcome.Completed>(
                    outcome);
        return new(completed.Population, completed.Artifacts);
    }

    static async Task<CompletedPlatformPopulation>
        MaterializePlatformPopulationWithCleanupFailureAsync(
            IArtifactAcquisitionLease cleanupLease)
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        PlatformFamilyTarget target = new(
            PlatformFamily.AspNetCore,
            PlatformTargetFramework.Parse("net11.0"),
            PlatformVersion.Parse("11.0.0"));
        PlatformSourceCapabilityIdentity capability =
            PlatformSourceCapabilityIdentity.Create("runtime-pack");
        var operation = new PlatformHouseOperation.Realize(
            new PlatformPopulationDemand.CompletePopulation(),
            PlatformViewDemand.Implementation);
        var request = new PlatformHouseRequest(
            PlatformHouseRequestIdentity.Create(
                "ecosystem-platform-cleanup"),
            new PlatformTargetDemand.Exact(target),
            new PlatformHouseRequestOrigin.Standalone(
                PlatformStandaloneOperationIdentity.Create(
                    "ecosystem-platform-cleanup")),
            operation,
            new PlatformSourcePlan(
                PlatformSourcePlanIdentity.Create(
                    "ecosystem-platform-cleanup-sources"),
                PlatformSourcePolicyGeneration.Create(
                    "ecosystem-platform-cleanup-generation"),
                [
                    new PlatformSourceSelection(
                        PlatformSourceFacet.Implementation,
                        PlatformSourceSelectionMode.Precedence,
                        [capability]),
                ]),
            new PlatformHouseWorkBudget(
                maxSourceOperations: 2,
                maxTargetCandidates: 2,
                maxAssemblies: 2,
                maxXmlDocuments: 2,
                maxPortablePdbs: 2,
                maxSourceDocuments: 2,
                maxBytes: 16 * 1024 * 1024,
                maxForwardingHops: 2,
                maxDuration: TimeSpan.FromSeconds(30)),
            cancellationToken);
        var contribution = new PlatformSourceContribution.Realization(
            PlatformSourceFacet.Implementation,
            capability,
            request.Snapshot,
            PlatformSourceGeneration.Create(
                "ecosystem-platform-cleanup-source-generation"),
            target,
            PlatformSourceCoordinateIdentity.Create(
                "ecosystem-platform-cleanup-coordinate"),
            operation.Population,
            PlatformSourceContributionCompleteness.Authoritative);
        byte[] content = await File.ReadAllBytesAsync(
            typeof(EcosystemPopulationLoadingTests).Assembly.Location,
            cancellationToken);
        var session = new ArtifactSetSession();
        ArtifactQueryLease? queryLease = null;
        ArtifactContentLease? contentLease = null;
        try
        {
            await session.AddRequiredAcquisitionAsync(
                (scope, _) =>
                {
                    ArtifactContribution artifact = scope.Register(
                        new PlatformLibraryArtifactProvenance(
                            contribution,
                            new Provenance("platform-cleanup")),
                        _ => new MemoryStream(content, writable: false));
                    return ValueTask.FromResult<ArtifactAcquisitionOutcome>(
                        new ArtifactAcquisitionOutcome.Acquired(
                            [artifact],
                            cleanupLease));
                },
                cancellationToken: cancellationToken);
            ArtifactAssemblyProjection? projection = null;
            Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                await session.SealWithProjectionAsync(
                    (view, token) =>
                    {
                        projection =
                            Assert.IsType<
                                ArtifactAssemblyProjectionOutcome.Projected>(
                                    ArtifactAssemblyInspection.Project(
                                        view,
                                        token))
                                .Value;
                        return null;
                    },
                    cancellationToken));
            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            queryLease = session.IssueLease(authorization);
            ArtifactContentReference reference =
                session.GetCatalog(queryLease)
                    .Select(
                        descriptor => session.GetContentReference(
                            descriptor.Identity,
                            queryLease))
                    .Single();
            contentLease =
                session.IssueContentLease(reference, queryLease);
            ArtifactAssemblyProjection exactProjection =
                Assert.IsType<ArtifactAssemblyProjection>(projection);
            var selection =
                new PlatformPopulationLibraryContentSelection(
                    reference,
                    exactProjection,
                    new PlatformPopulationMemberAttribution(
                        target,
                        PlatformPopulationMemberRole.Focus));
            var consumed = new PlatformHouseConsumedWork(
                sourceOperations: 1,
                targetCandidates: 0,
                assemblies: 1,
                xmlDocuments: 0,
                portablePdbs: 0,
                sourceDocuments: 0,
                bytes: content.LongLength,
                forwardingHops: 0,
                targetComparisons: 0,
                elapsed: TimeSpan.Zero);
            var population =
                Assert.IsType<
                    PlatformPopulationRealizationResult.Completed>(
                        await PlatformHousePopulationRealizer
                            .RealizeImplementationsAsync(
                                request,
                                [selection],
                                [contentLease],
                                consumed));
            contentLease = null;
            queryLease.Dispose();
            queryLease = null;
            return new(population, session);
        }
        catch
        {
            contentLease?.Dispose();
            queryLease?.Dispose();
            await session.DisposeAsync();
            throw;
        }
    }

    static AssemblyReferenceIdentity ReadAssemblyIdentity(byte[] content)
    {
        using var reader =
            new System.Reflection.PortableExecutable.PEReader(
                new MemoryStream(content, writable: false));
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            reader.GetMetadataReader());
    }

    enum LoadMode
    {
        CompletedNoMembers,
        CompletedMembers,
        PlatformCompleted,
        MultiplePlatformCompleted,
        PlatformIncomplete,
        PlatformThenUnsupported,
        Ambiguous,
        CancelAfterCompletedReply,
        Incomplete,
        ForeignReply,
    }

    sealed class TestInputs(LoadMode mode) :
        IEcosystemPopulationLoadInputs
    {
        public LoadMode Mode { get; } = mode;
        public int InvocationCount { get; set; }
        public LibraryContentOwner? Owner { get; init; }
        public CompletedPlatformPopulation? Platform
        {
            get;
            init;
        }
        public IReadOnlyList<CompletedPlatformPopulation>? Platforms
        {
            get;
            init;
        }
        public CancellationTokenSource? Cancellation { get; init; }
        public EcosystemPopulationLoaderReply? ForeignReply { get; set; }
        public EcosystemPopulationLoadInputSnapshot Snapshot { get; } =
            new(
                EcosystemPopulationOperationPolicyIdentity.Create(
                    "test-policy"),
                EcosystemPopulationCapabilityPlanIdentity.Create(
                    "test-capabilities"),
                EcosystemPopulationWorkIdentity.Create("test-work"));
    }

    sealed class WorkspaceFixture : IAsyncDisposable
    {
        readonly EcosystemPopulationLoaderSelection.Known<TestInputs> _known;

        WorkspaceFixture(
            InspectionWorkspace workspace,
            WorkspaceRegistrationRevision revision,
            WorkspaceEcosystemRegistrationDeclaration declaration,
            EcosystemPopulationLoaderSelection.Known<TestInputs> known)
        {
            Workspace = workspace;
            Revision = revision;
            Declaration = declaration;
            _known = known;
        }

        public InspectionWorkspace Workspace { get; }
        public WorkspaceRegistrationRevision Revision { get; }
        public WorkspaceEcosystemRegistrationDeclaration Declaration { get; }

        public static WorkspaceFixture Create(
            EcosystemPopulationLoaderBinding<TestInputs> binding)
        {
            WorkspaceEcosystemRegistrationDeclaration declaration =
                EcosystemPopulationLoadingTests.Declaration(
                    "ecosystem.test");
            var workspace = new InspectionWorkspace(
                new WorkspacePlan(
                    ImmutableArray.Create<WorkspaceRegistration>(
                        new WorkspaceRegistration.Ecosystem(
                            declaration))));
            WorkspaceRegistrationRevision revision = Read(workspace);
            var correspondence =
                new EcosystemPopulationLoaderCorrespondence<TestInputs>(
                    declaration,
                    binding);
            var known =
                Assert.IsType<
                    EcosystemPopulationLoaderSelection.Known<TestInputs>>(
                        correspondence.Select(
                            revision,
                            declaration,
                            EcosystemPopulationDemand
                                .WholePopulation.Instance));
            return new WorkspaceFixture(
                workspace,
                revision,
                declaration,
                known);
        }

        public EcosystemPopulationLoadRequest<TestInputs> Request(
            TestInputs inputs,
            CancellationToken cancellationToken = default) =>
            _known.CreateRequest(inputs, cancellationToken);

        public ValueTask DisposeAsync() => Workspace.DisposeAsync();
    }

    sealed class ArtifactFixture : IAsyncDisposable
    {
        readonly ArtifactSetSession _session;
        readonly ArtifactQueryLease _queryLease;
        readonly ArtifactContentReference _reference;

        ArtifactFixture(
            ArtifactSetSession session,
            ArtifactQueryLease queryLease,
            ArtifactContentReference reference)
        {
            _session = session;
            _queryLease = queryLease;
            _reference = reference;
        }

        public static async Task<ArtifactFixture> CreateAsync()
        {
            CancellationToken cancellationToken =
                TestContext.Current.CancellationToken;
            var session = new ArtifactSetSession();
            try
            {
                await session.AddRequiredAcquisitionAsync(
                    (scope, _) =>
                    {
                        ArtifactContribution contribution =
                            scope.Register(
                                new Provenance("test"),
                                _ => new MemoryStream(
                                    [42],
                                    writable: false));
                        return ValueTask.FromResult<
                            ArtifactAcquisitionOutcome>(
                                new ArtifactAcquisitionOutcome.Acquired(
                                    [contribution],
                                    ArtifactAcquisitionLeases.None));
                    },
                    cancellationToken: cancellationToken);
                Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                    await session.SealAsync(cancellationToken));
                ArtifactQueryAuthorization authorization =
                    session.CreateQueryAuthorization();
                ArtifactQueryLease lease =
                    session.IssueLease(authorization);
                ArtifactContentReference reference =
                    session.GetCatalog(lease)
                        .Select(
                            descriptor =>
                                session.GetContentReference(
                                    descriptor.Identity,
                                    lease))
                        .Single();
                return new ArtifactFixture(session, lease, reference);
            }
            catch
            {
                await session.DisposeAsync();
                throw;
            }
        }

        public LibraryContentOwner CreateOwner(string name)
            => CreateOwnerWithChild(name).Owner;

        public OwnerWithChild CreateOwnerWithChild(string name)
        {
            ManagedMetadataIdentity.Assembly identity =
                new(
                    new AssemblyReferenceIdentity(
                        name,
                        new Version(1, 0, 0, 0),
                        Culture: null,
                        PublicKeyToken: null));
            LibraryReference library = LibraryReference.CreateDirect(
                new LibraryAssemblyCorrespondence(
                    _reference,
                    identity,
                    _reference,
                    identity));
            ArtifactContentLease child =
                _session.IssueContentLease(_reference, _queryLease);
            return new OwnerWithChild(
                new LibraryContentOwner(
                    library,
                    [child]),
                child);
        }

        public async ValueTask DisposeAsync()
        {
            _queryLease.Dispose();
            await _session.DisposeAsync();
        }
    }

    sealed record Provenance(string Name) : IArtifactProvenance;
    sealed record CompletedPlatformPopulation(
        PlatformPopulationRealizationResult.Completed Population,
        ArtifactSetSession Artifacts);
    sealed class FailingArtifactLease(Exception failure) :
        IArtifactAcquisitionLease
    {
        public int Disposals { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.FromException(failure);
        }
    }
    readonly record struct OwnerWithChild(
        LibraryContentOwner Owner,
        ArtifactContentLease Child);
    readonly record struct ChildEvidence(
        EcosystemPopulationChildRequestIdentity Request,
        EcosystemPopulationChildReceiptIdentity Receipt);
}
