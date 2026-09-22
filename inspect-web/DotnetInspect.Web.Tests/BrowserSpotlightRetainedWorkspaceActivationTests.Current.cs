using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;
using NuGetFetch;

using CurrentActivationResult =
    DotnetInspect.Web.BrowserSpotlightRetainedCurrentActivationResult<
        DotnetInspect.Web.BrowserSpotlightExternalPackageWorkspaceRequest,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent,
        DotnetInspect.Web.TestPackageFailure>;
using CurrentDescriptor =
    DotnetInspect.Web.BrowserSpotlightDestinationDescriptor<
        DotnetInspect.Web.BrowserSpotlightExternalPackageWorkspaceRequest,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent>;
using CurrentDestination =
    DotnetInspect.Web.BrowserSpotlightDestination<
        DotnetInspect.Web.BrowserSpotlightExternalPackageWorkspaceRequest,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent>;
using CurrentProjectionResult =
    DotnetInspect.Web.BrowserSpotlightDestinationProjectionResult<
        DotnetInspect.Web.BrowserSpotlightExternalPackageWorkspaceRequest,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent>;
using PackageAcquisition =
    DotnetInspect.Web.BrowserSpotlightPackageAcquisitionResult<
        DotnetInspect.Web.TestPackageFailure>;
using PackageActivationResult =
    DotnetInspect.Web.BrowserSpotlightCurrentPackageActivationResult<
        DotnetInspect.Web.BrowserSpotlightExternalPackageWorkspaceRequest,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent,
        DotnetInspect.Web.TestPackageFailure>;

namespace DotnetInspect.Web.Tests;

public sealed partial class BrowserSpotlightRetainedWorkspaceActivationTests
{
    [Fact]
    public async Task CurrentNavigationUsesAdmittedActiveRealization()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateCurrentSourceAsync(owner, "source");
        var action = new TestNavigationAction("workspace");
        (CurrentDescriptor descriptor, InspectionWorkspace workspace) =
            await CurrentNavigationDescriptorAsync(
                owner,
                "source",
                action);
        bool acquired = false;
        bool focused = false;

        CurrentActivationResult result =
            await ExecuteCurrentAsync(
                owner,
                "source",
                descriptor,
                (requested, _) =>
                {
                    Assert.Same(action, requested);
                    return ValueTask.FromResult(
                        CurrentNavigationResult(
                            workspace,
                            descriptor.Basis.Scope));
                },
                (_, _) =>
                {
                    acquired = true;
                    throw new InvalidOperationException(
                        "Current Navigation must not acquire a Package.");
                },
                (_, _) =>
                {
                    focused = true;
                    throw new InvalidOperationException(
                        "Current Navigation must not focus a Package.");
                });

        Assert.IsType<CurrentActivationResult.Navigated>(result);
        Assert.False(acquired);
        Assert.False(focused);
        Assert.Same(posting, owner.Active);
    }

    [Fact]
    public async Task CurrentPackageAddUsesAdmittedActiveRealization()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateCurrentSourceAsync(owner, "source");
        BrowserSpotlightExternalPackageWorkspaceRequest external =
            ExternalRequest(WorkspacePlan.Empty);
        (CurrentDescriptor descriptor, InspectionWorkspace workspace) =
            await CurrentPackageDescriptorAsync(
                owner,
                "source",
                external);
        PackageRootBinding binding = CurrentPackageBinding(external.Package);
        bool navigated = false;

        CurrentActivationResult result =
            await ExecuteCurrentAsync(
                owner,
                "source",
                descriptor,
                (_, _) =>
                {
                    navigated = true;
                    throw new InvalidOperationException(
                        "Current Package activation must not use the Navigation-only callback.");
                },
                (_, _) => ValueTask.FromResult<PackageAcquisition>(
                    new PackageAcquisition.Acquired(binding)),
                (request, _) => ValueTask.FromResult(
                    CurrentNavigationResult(
                        workspace,
                        request.Scope)));

        var package =
            Assert.IsType<CurrentActivationResult.PackageOperation>(
                result);
        var settled =
            Assert.IsType<PackageActivationResult.Settled>(
                package.Result);
        var committed = Assert.IsType<
            BrowserSpotlightPackageMembershipResult.Committed>(
                settled.Membership);
        Assert.False(navigated);
        Assert.Same(posting, owner.Active);
        Assert.Contains(
            (await CurrentScope(workspace)).Packages,
            occurrence => ReferenceEquals(
                occurrence.Occurrence,
                committed.Occurrence));
    }

    [Fact]
    public async Task AcquisitionFailureDoesNotCreateFreshRealization()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        BrowserRetainedWorkspacePosting posting =
            await ActivateCurrentSourceAsync(owner, "source");
        BrowserSpotlightExternalPackageWorkspaceRequest external =
            ExternalRequest(WorkspacePlan.Empty);
        (CurrentDescriptor descriptor, InspectionWorkspace workspace) =
            await CurrentPackageDescriptorAsync(
                owner,
                "source",
                external);
        var failure = new TestPackageFailure("source denied");

        CurrentActivationResult result =
            await ExecuteCurrentAsync(
                owner,
                "source",
                descriptor,
                (_, _) => throw new InvalidOperationException(
                    "Current Package activation must not navigate directly."),
                (_, _) => ValueTask.FromResult<PackageAcquisition>(
                    new PackageAcquisition.NotAcquired(failure)),
                (_, _) => throw new InvalidOperationException(
                    "Failed acquisition must not focus."));

        var package =
            Assert.IsType<CurrentActivationResult.PackageOperation>(
                result);
        var unavailable =
            Assert.IsType<PackageActivationResult.PackageNotAcquired>(
                package.Result);
        Assert.Same(failure, unavailable.Result);
        Assert.Same(posting, owner.Active);
        WorkspaceScopeSnapshot scope = await CurrentScope(workspace);
        Assert.Same(descriptor.Basis.Scope.Revision, scope.Revision);
        Assert.Same(
            descriptor.Basis.Scope.PublicationBase,
            scope.PublicationBase);
    }

    [Fact]
    public async Task ReplacementBlocksStalePackageBeforeCallbacks()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateCurrentSourceAsync(owner, "source");
        BrowserSpotlightExternalPackageWorkspaceRequest external =
            ExternalRequest(WorkspacePlan.Empty);
        (CurrentDescriptor descriptor, _) =
            await CurrentPackageDescriptorAsync(
                owner,
                "source",
                external);
        _ = await ActivateCurrentSourceAsync(owner, "replacement");
        bool acquired = false;
        bool focused = false;

        CurrentActivationResult result =
            await ExecuteCurrentAsync(
                owner,
                "replacement",
                descriptor,
                (_, _) => throw new InvalidOperationException(
                    "Package activation must not navigate directly."),
                (_, _) =>
                {
                    acquired = true;
                    return ValueTask.FromResult<PackageAcquisition>(
                        new PackageAcquisition.Acquired(
                            CurrentPackageBinding(external.Package)));
                },
                (_, _) =>
                {
                    focused = true;
                    throw new InvalidOperationException(
                        "A stale Package descriptor must not focus.");
                });

        var package =
            Assert.IsType<CurrentActivationResult.PackageOperation>(
                result);
        var blocked =
            Assert.IsType<PackageActivationResult.Blocked>(
                package.Result);
        var stale =
            Assert.IsType<BrowserSpotlightActivationBlock.Stale>(
                blocked.Reason);
        Assert.Equal(
            BrowserSpotlightActivationStaleReason.WorkspaceIdentity,
            stale.Reason);
        Assert.False(acquired);
        Assert.False(focused);
    }

    [Fact]
    public async Task NoActiveRealizationPreservesAdmissionFailure()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateCurrentSourceAsync(owner, "source");
        (CurrentDescriptor descriptor, _) =
            await CurrentNavigationDescriptorAsync(
                owner,
                "source",
                new TestNavigationAction("workspace"));
        Assert.IsType<
            BrowserRetainedWorkspaceDeactivationResult.Deactivated>(
                await owner.DeactivateAsync(
                    "source",
                    TestContext.Current.CancellationToken));

        CurrentActivationResult result =
            await ExecuteCurrentAsync(
                owner,
                "source",
                descriptor,
                (_, _) => throw new InvalidOperationException(
                    "An unavailable realization must not navigate."),
                (_, _) => throw new InvalidOperationException(
                    "An unavailable realization must not acquire."),
                (_, _) => throw new InvalidOperationException(
                    "An unavailable realization must not focus."));

        var unavailable =
            Assert.IsType<CurrentActivationResult.NotAdmitted>(result);
        Assert.Equal(
            WorkspaceRealizationOperationUnavailableReason
                .NoActiveRealization,
            unavailable.Admission.Reason);
    }

    [Fact]
    public async Task CurrentNavigationLeaseDelaysDeactivation()
    {
        CompleteRestorationExecutionOptions options = await OptionsAsync();
        await using var owner =
            new BrowserRetainedWorkspaceActivationOwner(() => options);
        _ = await ActivateCurrentSourceAsync(owner, "source");
        (CurrentDescriptor descriptor, InspectionWorkspace workspace) =
            await CurrentNavigationDescriptorAsync(
                owner,
                "source",
                new TestNavigationAction("workspace"));
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<CurrentActivationResult> activation =
            ExecuteCurrentAsync(
                owner,
                "source",
                descriptor,
                async (_, cancellationToken) =>
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(cancellationToken);
                    return CurrentNavigationResult(
                        workspace,
                        descriptor.Basis.Scope);
                },
                (_, _) => throw new InvalidOperationException(
                    "Current Navigation must not acquire."),
                (_, _) => throw new InvalidOperationException(
                    "Current Navigation must not focus a Package.")).AsTask();
        await entered.Task;

        Task<BrowserRetainedWorkspaceDeactivationResult> deactivation =
            owner.DeactivateAsync(
                "source",
                TestContext.Current.CancellationToken);
        await Task.Yield();
        Assert.False(deactivation.IsCompleted);

        release.SetResult();
        Assert.IsType<CurrentActivationResult.Navigated>(
            await activation);
        Assert.IsType<
            BrowserRetainedWorkspaceDeactivationResult.Deactivated>(
                await deactivation);
    }

    static async Task<BrowserRetainedWorkspacePosting>
        ActivateCurrentSourceAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string id)
    {
        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await owner.ActivateAsync(
                    new BrowserRetainedWorkspaceActivationRequest(
                        id,
                        $"Workspace {id}",
                        $"/workspace/{id}",
                        CurrentPacket()),
                    TestContext.Current.CancellationToken));
        return activated.Posting;
    }

    static async Task<(CurrentDescriptor Descriptor, InspectionWorkspace Workspace)>
        CurrentNavigationDescriptorAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string retainedDefinitionId,
            TestNavigationAction action)
    {
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(owner, retainedDefinitionId);
        BrowserSpotlightActivationBasis basis = Basis(
            operation,
            generation: 1);
        var destination = new CurrentDestination.Current(
            StructuralSubjectIdentity.ForWorkspace(
                operation.Workspace.Identity),
            action);
        CurrentDescriptor descriptor =
            Assert.IsType<CurrentProjectionResult.Projected>(
                BrowserSpotlightDestinationProjection.Project(
                    basis,
                    destination)).Descriptor;
        return (descriptor, operation.Workspace);
    }

    static async Task<(CurrentDescriptor Descriptor, InspectionWorkspace Workspace)>
        CurrentPackageDescriptorAsync(
            BrowserRetainedWorkspaceActivationOwner owner,
            string retainedDefinitionId,
            BrowserSpotlightExternalPackageWorkspaceRequest external)
    {
        using WorkspaceRealizationOperationLease operation =
            await EnterAsync(owner, retainedDefinitionId);
        CurrentDescriptor descriptor = Descriptor(
            Basis(operation, generation: 1),
            external);
        return (descriptor, operation.Workspace);
    }

    static async Task<WorkspaceScopeSnapshot> CurrentScope(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;

    static NavigationOperationResult CurrentNavigationResult(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope)
    {
        ViewFacetRegistry facets = InspectionViewFacetCatalog.Registry;
        return NavigationTransitions.Initialize(
            workspace.Identity,
            new(
                scope,
                Package: null,
                (_, _) => new ViewFacetAvailabilitySnapshot(
                    facets.Descriptors.Select(
                        descriptor =>
                            new ViewFacetAvailabilityFact(
                                descriptor.Id,
                                ViewFacetAvailability.Available.Instance)))),
            facets).Result;
    }

    static PackageRootBinding CurrentPackageBinding(
        PackageSourceCoordinate coordinate)
    {
        byte[] assembly = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "Spotlight",
                "package",
                "System.Text.Json.dll"));
        IPackageContent content = new InMemoryPackageContent(
            Archive(("lib/net11.0/System.Text.Json.dll", assembly)),
            fromCache: false,
            producerKey: "nuget-org");
        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                coordinate,
                content,
                producerKey: "nuget-org",
                PackagePayloadOrigin.Download),
            selectionTargetFramework: "net11.0",
            runtimeIdentifier: null);
    }

    static ValueTask<CurrentActivationResult> ExecuteCurrentAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string sourceRetainedDefinitionId,
        CurrentDescriptor descriptor,
        BrowserSpotlightCurrentNavigationOperation<TestNavigationAction>
            navigate,
        BrowserSpotlightPackageAcquisitionOperation<
            BrowserSpotlightExternalPackageWorkspaceRequest,
            TestPackageFailure> acquirePackage,
        BrowserSpotlightPackageFocusOperation<
            BrowserSpotlightExternalPackageWorkspaceRequest,
            TestLibraryIntent> focusPackage) =>
        BrowserSpotlightRetainedCurrentActivation.ExecuteAsync<
            BrowserSpotlightExternalPackageWorkspaceRequest,
            TestNavigationAction,
            TestPlatformAction,
            TestLibraryIntent,
            TestPackageFailure>(
                owner,
                sourceRetainedDefinitionId,
                descriptor,
                navigate,
                acquirePackage,
                focusPackage,
                DateTimeOffset.UtcNow.AddMinutes(2),
                TestContext.Current.CancellationToken);

    static string CurrentPacket()
    {
        const int schemaVersion = InspectionDefinitionSchema.Version3;
        var package = new DefinitionMemberCoordinate.PackageCoordinate(
            "System.Text.Json",
            "9.0.4",
            "net9.0");
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            schemaVersion,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    "net9.0",
                    members: [package]),
            ],
            registrations:
            [
                new WorkspaceRegistration.PackagePrefix(
                    new PackagePrefixDeclaration("Example.")),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            schemaVersion,
            "navigation",
            [new NavigationTabDefinition("package", coordinate: package)],
            focus: "package"));
        registry.Add(new CommittedViewDefinition(
            schemaVersion,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "package",
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package()),
            ]));
        registry.Add(new ScenarioDefinition(
            schemaVersion,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));
        var definitions = Assert.IsType<
            InspectionDefinitionScenarioPreparationResult.Version3>(
                registry.PrepareScenario("scenario")).Definitions;
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(definitions);
        return WorkspaceSharePacketCodec.Encode(
            Assert.IsType<WorkspaceSharePacket>(projection.Packet));
    }
}
