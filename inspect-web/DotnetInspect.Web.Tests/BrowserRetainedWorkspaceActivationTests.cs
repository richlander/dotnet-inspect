using System.IO.Compression;
using System.Net;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Web.Tests;

[SupportedOSPlatform("browser")]
public sealed class BrowserRetainedWorkspaceActivationTests
{
    const string PackageId = "System.Text.Json";
    const string PackageVersion = "11.0.0-preview.7.26381.103";
    const string Framework = "net10.0";
    const string SourceUrl = "https://api.nuget.org/v3/index.json";
    const string Format1Packet =
        "eyJmIjoxLCJ0IjpbWyJQIixudWxsLCJuZXQxMC4wIixudWxsXV0sImciOltbMF1d"
        + "LCJhIjowLCJ4IjowfQ";

    [Fact]
    public async Task PreCancelledSelection_IsTypedAndDoesNotAdmitCandidate()
    {
        var host = new BrowserWorkspaceRealizationHost();
        await using var coordinator =
            new BrowserRetainedWorkspaceActivationCoordinator(
                host,
                () => Options(new InMemoryPackageStore()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failed = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await coordinator.ActivatePacketAsync(
                    "cancelled",
                    Format1Packet,
                    cancellation.Token));

        Assert.IsType<CompleteRestorationFailure.Cancelled>(failed.Failure);
        Assert.Equal(0, host.Capacity.Charged);
        Assert.Null(coordinator.Active);
    }

    [Fact]
    public async Task PacketFormat1_IsRejectedBeforeCandidateAdmission()
    {
        var store = new InMemoryPackageStore();
        var host = new BrowserWorkspaceRealizationHost();
        await using var coordinator =
            new BrowserRetainedWorkspaceActivationCoordinator(
                host,
                () => Options(store));

        var failed = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await coordinator.ActivatePacketAsync(
                    "legacy",
                    Format1Packet,
                    TestContext.Current.CancellationToken));

        var unsupported =
            Assert.IsType<CompleteRestorationFailure.UnsupportedVersion>(
                failed.Failure);
        Assert.Equal(
            WorkspaceSharePacketCodec.LegacyFormatVersion,
            unsupported.Version);
        Assert.Equal(0, host.Capacity.Charged);
        Assert.Null(coordinator.Active);
    }

    [Fact]
    public async Task RepeatedSelection_IsNoEffectWithoutAnotherCandidate()
    {
        InMemoryPackageStore store = await PackageStoreAsync();
        string packet = Packet();
        var host = new BrowserWorkspaceRealizationHost();
        await using var coordinator =
            new BrowserRetainedWorkspaceActivationCoordinator(
                host,
                () => Options(store));

        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await coordinator.ActivatePacketAsync(
                    "json",
                    packet,
                    TestContext.Current.CancellationToken));
        int charged = host.Capacity.Charged;
        var noEffect = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.NoEffect>(
                await coordinator.ActivatePacketAsync(
                    "json",
                    packet,
                    TestContext.Current.CancellationToken));

        Assert.Same(activated.Selection, noEffect.Selection);
        Assert.Equal(charged, host.Capacity.Charged);
        Assert.Equal(packet, ProjectedPacket(noEffect.Selection));
    }

    [Fact]
    public async Task AThenBThenA_UsesFreshRealizationIdentity()
    {
        InMemoryPackageStore store = await PackageStoreAsync();
        string packet = Packet();
        await using var coordinator =
            new BrowserRetainedWorkspaceActivationCoordinator(
                new BrowserWorkspaceRealizationHost(),
                () => Options(store));

        var firstA = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await coordinator.ActivatePacketAsync(
                    "a",
                    packet,
                    TestContext.Current.CancellationToken));
        var b = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await coordinator.ActivatePacketAsync(
                    "b",
                    packet,
                    TestContext.Current.CancellationToken));
        var secondA = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await coordinator.ActivatePacketAsync(
                    "a",
                    packet,
                    TestContext.Current.CancellationToken));

        Assert.NotSame(
            firstA.Selection.Workspace.Workspace,
            b.Selection.Workspace.Workspace);
        Assert.NotSame(
            firstA.Selection.Workspace.Workspace,
            secondA.Selection.Workspace.Workspace);
        Assert.NotEqual(
            firstA.Selection.ActivationId,
            secondA.Selection.ActivationId);
        Assert.Same(secondA.Selection, coordinator.Active);
        Assert.Equal(packet, ProjectedPacket(secondA.Selection));
    }

    [Fact]
    public async Task NewerSelection_SupersedesBlockedCandidateBeforeCutover()
    {
        InMemoryPackageStore readyStore = await PackageStoreAsync();
        string packet = Packet();
        var blocked = new BlockingHandler();
        int optionCall = 0;
        await using var coordinator =
            new BrowserRetainedWorkspaceActivationCoordinator(
                new BrowserWorkspaceRealizationHost(),
                () => Interlocked.Increment(ref optionCall) == 1
                    ? Options(new InMemoryPackageStore(), handler: blocked)
                    : Options(readyStore));

        Task<BrowserRetainedWorkspaceActivationResult> first =
            coordinator.ActivatePacketAsync(
                "a",
                packet,
                TestContext.Current.CancellationToken).AsTask();
        await blocked.Started.WaitAsync(
            TestContext.Current.CancellationToken);
        var second = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await coordinator.ActivatePacketAsync(
                    "b",
                    packet,
                    TestContext.Current.CancellationToken));
        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Superseded>(
            await first);

        Assert.Same(second.Selection, coordinator.Active);
        Assert.Equal("b", coordinator.Active?.RetainedDefinitionId);
    }

    [Fact]
    public async Task ActiveSelection_CancelsBlockedReplacementAsNoEffect()
    {
        InMemoryPackageStore readyStore = await PackageStoreAsync();
        string packet = Packet();
        var blocked = new BlockingHandler();
        int optionCall = 0;
        await using var coordinator =
            new BrowserRetainedWorkspaceActivationCoordinator(
                new BrowserWorkspaceRealizationHost(),
                () => Interlocked.Increment(ref optionCall) == 2
                    ? Options(new InMemoryPackageStore(), handler: blocked)
                    : Options(readyStore));
        var incumbent = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await coordinator.ActivatePacketAsync(
                    "a",
                    packet,
                    TestContext.Current.CancellationToken));
        Task<BrowserRetainedWorkspaceActivationResult> replacement =
            coordinator.ActivatePacketAsync(
                "b",
                packet,
                TestContext.Current.CancellationToken).AsTask();
        await blocked.Started.WaitAsync(
            TestContext.Current.CancellationToken);

        var noEffect = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.NoEffect>(
                await coordinator.ActivatePacketAsync(
                    "a",
                    packet,
                    TestContext.Current.CancellationToken));
        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Superseded>(
            await replacement);

        Assert.Same(incumbent.Selection, noEffect.Selection);
        Assert.Same(incumbent.Selection, coordinator.Active);
    }

    [Fact]
    public async Task SupersessionCancelsCapacityWaitWithoutReleasingPredecessor()
    {
        InMemoryPackageStore store = await PackageStoreAsync();
        string packet = Packet();
        var host = new BrowserWorkspaceRealizationHost();
        List<WorkspaceRealizationOperationLease> predecessors =
            await FillWithDrainingPredecessorsAsync(host);
        await using var coordinator =
            new BrowserRetainedWorkspaceActivationCoordinator(
                host,
                () => Options(store));

        Task<BrowserRetainedWorkspaceActivationResult> waiting =
            coordinator.ActivatePacketAsync(
                "waiting",
                packet,
                TestContext.Current.CancellationToken).AsTask();
        await Task.Yield();
        Assert.False(waiting.IsCompleted);

        var rejected = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await coordinator.ActivatePacketAsync(
                    "newer-invalid",
                    Format1Packet,
                    TestContext.Current.CancellationToken));
        Assert.IsType<CompleteRestorationFailure.UnsupportedVersion>(
            rejected.Failure);
        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Superseded>(
            await waiting.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(
            BrowserWorkspaceRealizationHost.MaxChargedRealizations,
            host.Capacity.Charged);

        foreach (WorkspaceRealizationOperationLease predecessor in predecessors)
            predecessor.Dispose();
    }

    [Fact]
    public void SupersededCleanupFailureRemainsVisible()
    {
        var cleanup = new CompleteRestorationFailure.CleanupFailed(
            "Cleanup failed.");

        var failed = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                BrowserRetainedWorkspaceActivationCoordinator
                    .RestorationFailure(
                        "workspace",
                        cleanup,
                        CompleteRestorationIntentStatus.Superseded));
        Assert.Same(cleanup, failed.Failure);

        Assert.IsType<BrowserRetainedWorkspaceActivationResult.Superseded>(
            BrowserRetainedWorkspaceActivationCoordinator.RestorationFailure(
                "workspace",
                new CompleteRestorationFailure.HostConstructionFailed(
                    "Superseded."),
                CompleteRestorationIntentStatus.Superseded));
    }

    [Fact]
    public async Task PredecessorSettlementHasExactAwaitableResult()
    {
        InMemoryPackageStore store = await PackageStoreAsync();
        string packet = Packet();
        var host = new BrowserWorkspaceRealizationHost();
        await using var coordinator =
            new BrowserRetainedWorkspaceActivationCoordinator(
                host,
                () => Options(store));
        _ = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await coordinator.ActivatePacketAsync(
                    "a",
                    packet,
                    TestContext.Current.CancellationToken));
        WorkspaceRealizationOperationAdmission admission =
            await host.EnterOperationAsync(
                TestContext.Current.CancellationToken);
        WorkspaceRealizationOperationLease predecessor =
            Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
                admission).Lease;

        var activated = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await coordinator.ActivatePacketAsync(
                    "b",
                    packet,
                    TestContext.Current.CancellationToken));
        BrowserRetainedWorkspaceSettlementReference reference =
            Assert.IsType<BrowserRetainedWorkspaceSettlementReference>(
                activated.PredecessorSettlement);
        Task<BrowserRetainedWorkspaceSettlementResult> settlement =
            coordinator.AwaitSettlementAsync(
                reference.SettlementId).AsTask();
        Assert.False(settlement.IsCompleted);

        predecessor.Dispose();
        var settled = Assert.IsType<
            BrowserRetainedWorkspaceSettlementResult.Settled>(
                await settlement.WaitAsync(
                    TestContext.Current.CancellationToken));
        Assert.Equal(reference.SettlementId, settled.SettlementId);
        Assert.True(settled.Settlement.Succeeded);
        Assert.Equal(
            WorkspaceRealizationRetirementReason.Replaced,
            settled.Settlement.Reason);

        Assert.IsType<BrowserRetainedWorkspaceSettlementResult.Unavailable>(
            await coordinator.AwaitSettlementAsync(reference.SettlementId));
    }

    [Fact]
    public async Task ObservedSettlementsRemoveBothBoundedRegistryIndexes()
    {
        await using var host = new BrowserWorkspaceRealizationHost();
        var prepared = Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult.Prepared>(
                await host.BeginCandidateAsync(
                    WorkspacePlan.Empty,
                    TestContext.Current.CancellationToken));
        var retiring = Assert.IsType<
            BrowserWorkspaceRealizationCandidateRetirementResult.Retiring>(
                host.AbandonCandidate(prepared.Candidate));
        WorkspaceRealizationSettlement settlement =
            await retiring.Retirement.Completion;
        var registry = new BrowserRetainedWorkspaceSettlementRegistry();

        for (int index = 0; index < 1_000; index++)
        {
            BrowserRetainedWorkspaceSettlementReference reference =
                registry.Register(Task.FromResult(settlement));
            Assert.IsType<BrowserRetainedWorkspaceSettlementResult.Settled>(
                await registry.AwaitAsync(reference.SettlementId));
        }

        Assert.Equal((0, 0), registry.Counts);
    }

    [Fact]
    public async Task FailedReplacement_PreservesIncumbentSelection()
    {
        InMemoryPackageStore store = await PackageStoreAsync();
        string packet = Packet();
        bool expired = false;
        await using var coordinator =
            new BrowserRetainedWorkspaceActivationCoordinator(
                new BrowserWorkspaceRealizationHost(),
                () => Options(
                    store,
                    expired
                        ? DateTimeOffset.UtcNow.AddMinutes(-1)
                        : DateTimeOffset.UtcNow.AddMinutes(1)));

        var incumbent = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Activated>(
                await coordinator.ActivatePacketAsync(
                    "a",
                    packet,
                    TestContext.Current.CancellationToken));
        expired = true;

        var failed = Assert.IsType<
            BrowserRetainedWorkspaceActivationResult.Failed>(
                await coordinator.ActivatePacketAsync(
                    "b",
                    packet,
                    TestContext.Current.CancellationToken));

        Assert.IsType<CompleteRestorationFailure.ScopeMutationFailed>(
            failed.Failure);
        Assert.Same(incumbent.Selection, coordinator.Active);
    }

    static string ProjectedPacket(
        BrowserRetainedWorkspaceSelection selection) =>
        Assert.IsType<CompleteRestorationProjection.Projectable>(
            selection.Workspace.Projection).CanonicalPacket;

    static string Packet()
    {
        var package =
            new DefinitionMemberCoordinate.PackageCoordinate(
                PackageId,
                PackageVersion,
                Framework);
        var registry = new InspectionDefinitionRegistry();
        registry.Add(new WorkspaceDefinition(
            InspectionDefinitionSchema.Version2,
            "workspace",
            [
                new WorkspaceContextDefinition(
                    "context",
                    framework: Framework,
                    members: [package]),
            ]));
        registry.Add(new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version2,
            "navigation",
            [new NavigationTabDefinition("package", coordinate: package)],
            "package"));
        registry.Add(new CommittedViewDefinition(
            InspectionDefinitionSchema.Version2,
            "view",
            [
                new CommittedViewStateDefinition(
                    null,
                    new PortableSubjectRequest.Workspace()),
                new CommittedViewStateDefinition(
                    "package",
                    new PortableSubjectRequest.Package(),
                    new PortableRetainedSubjectContext.Package(),
                    facet: "package.overview"),
            ]));
        registry.Add(new ScenarioDefinition(
            InspectionDefinitionSchema.Version2,
            "scenario",
            workspace: "workspace",
            context: "context",
            view: "view",
            navigation: "navigation"));
        var prepared =
            Assert.IsType<InspectionDefinitionScenarioPreparationResult.Version2>(
                registry.PrepareScenario("scenario"));
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(prepared.Definitions);
        return WorkspaceSharePacketCodec.Encode(
            Assert.IsType<WorkspaceSharePacket>(projection.Packet));
    }

    static async Task<InMemoryPackageStore> PackageStoreAsync()
    {
        byte[] assembly = await File.ReadAllBytesAsync(
            Path.Combine(
                AppContext.BaseDirectory,
                "RealAssets",
                "Spotlight",
                "package",
                "System.Text.Json.dll"),
            TestContext.Current.CancellationToken);
        byte[] package = Archive(
            ($"lib/{Framework}/System.Text.Json.dll", assembly));
        var store = new InMemoryPackageStore();
        await store.CommitAsync(
            PackageId,
            PackageVersion,
            NuGetCache.GetSourceKey(SourceUrl),
            new MemoryStream(package, writable: false),
            TestContext.Current.CancellationToken);
        return store;
    }

    static async Task<List<WorkspaceRealizationOperationLease>>
        FillWithDrainingPredecessorsAsync(
            BrowserWorkspaceRealizationHost host)
    {
        var predecessors =
            new List<WorkspaceRealizationOperationLease>();
        _ = await ActivateEmptyAsync(host);
        predecessors.Add(await EnterOperationAsync(host));
        _ = await ActivateEmptyAsync(host);
        predecessors.Add(await EnterOperationAsync(host));
        _ = await ActivateEmptyAsync(host);
        predecessors.Add(await EnterOperationAsync(host));
        _ = await ActivateEmptyAsync(host);
        return predecessors;
    }

    static async Task<WorkspaceRealization> ActivateEmptyAsync(
        BrowserWorkspaceRealizationHost host)
    {
        var prepared = Assert.IsType<
            BrowserWorkspaceRealizationCandidateStartResult.Prepared>(
                await host.BeginCandidateAsync(
                    WorkspacePlan.Empty,
                    TestContext.Current.CancellationToken));
        Assert.IsType<WorkspaceRealizationCandidateCompletionResult.Ready>(
            await host.CompleteCandidateAsync(
                prepared.Candidate,
                TestContext.Current.CancellationToken));
        return Assert.IsType<
            BrowserWorkspaceRealizationCutoverResult.Activated>(
                host.CutOver(prepared.Candidate)).Realization;
    }

    static async Task<WorkspaceRealizationOperationLease> EnterOperationAsync(
        BrowserWorkspaceRealizationHost host)
    {
        WorkspaceRealizationOperationAdmission admission =
            await host.EnterOperationAsync(
                TestContext.Current.CancellationToken);
        return Assert.IsType<WorkspaceRealizationOperationAdmission.Admitted>(
            admission).Lease;
    }

    static CompleteRestorationExecutionOptions Options(
        InMemoryPackageStore store,
        DateTimeOffset? deadline = null,
        HttpMessageHandler? handler = null)
    {
        ViewFacetRegistry facets = InspectionViewFacetCatalog.Registry;
        var available = new ViewFacetAvailabilitySnapshot(
            facets.Descriptors.Select(
                descriptor => new ViewFacetAvailabilityFact(
                    descriptor.Id,
                    ViewFacetAvailability.Available.Instance)));
        return new()
        {
            ContextLoad = new WorkspaceContextLoadOptions
            {
                HttpClient = new HttpClient(
                    handler ?? new RejectingHandler()),
                SourceAuthorization =
                    new UniformPackageSourceAuthorization(
                        [new PackageSource("nuget.org", SourceUrl)]),
                PackageStore = store,
            },
            ScopeDeadline =
                deadline ?? DateTimeOffset.UtcNow.AddMinutes(1),
            Facets = facets,
            FacetAvailability = (_, _) => available,
            PackageSurfaceLimits = BrowserApiSurfacePolicy.Limits,
        };
    }

    static byte[] Archive(params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream stream = archive.CreateEntry(path).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    sealed class RejectingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    sealed class BlockingHandler : HttpMessageHandler
    {
        readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task Started => _started.Task;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException(
                "The blocking response completed without cancellation.");
        }
    }
}
